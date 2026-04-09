# Testing Plan — Online Card Payments

> Executor: Read this fully before starting. Follow sections in order.
> Caveman mode OK for executor responses.

---

## Context

Multi-language payment sample using GlobalPayments SDK. **4 implementations, identical behavior:**

| Lang | Entry | Port | Framework |
|------|-------|------|-----------|
| Java | `ProcessPaymentServlet.java`, `GpApi3dsServlet.java` | 8000 | Jakarta Servlet + Tomcat |
| Node.js | `server.js`, `auth.js` | 8000 | Express.js |
| PHP | `get-access-token.php`, `process-sale.php`, `router.php` | 8000 | Built-in server |
| .NET | `Program.cs` | 8000 | ASP.NET Core minimal API |

**Zero existing test coverage.** Playwright E2E infrastructure exists (Dockerfile.tests, docker-compose.yml) but no test files.

---

## Prerequisites

### Credentials
All tests need a valid `.env` in each language dir. Copy from `.env.sample`:

```bash
GP_APP_ID=<sandbox_app_id>
GP_APP_KEY=<sandbox_app_key>
GP_ENVIRONMENT=sandbox
# Leave GP_ACCOUNT_NAME blank — SDK auto-detects
```

> **CRITICAL:** Setting `GP_ACCOUNT_NAME` manually breaks token validation. Leave blank.

### Tools needed
- Docker + Docker Compose
- Node.js 18+ (for E2E tests)
- Java 25 + Maven (for Java unit tests)
- PHP 8.1+ + Composer (for PHP tests)
- .NET 9 SDK (for .NET tests)

---

## Test Levels

```
Level 1: Unit Tests (per-language, no network)
Level 2: Integration Tests (per-language, hits GP-API sandbox)
Level 3: E2E Tests (Playwright, full browser flow)
Level 4: Cross-Language Parity Tests (same input → same output)
```

---

## Level 1: Unit Tests

### 1A — Node.js Unit Tests

**Setup:**
```bash
cd nodejs/
npm install --save-dev jest
# Add to package.json: "test": "node --experimental-vm-modules node_modules/.bin/jest"
```

**File to create:** `nodejs/tests/unit/auth.test.js`

**Test cases:**

| # | Function | What to test | Expected |
|---|----------|-------------|---------|
| U1 | `generateToken()` in auth.js | Nonce is 32-char hex string | `nonce.length === 32` |
| U2 | `generateToken()` in auth.js | Hash is 128-char SHA-512 hex | `secret.length === 128` |
| U3 | `getGpApiBase()` | Returns sandbox URL when `GP_ENVIRONMENT=sandbox` | Contains `apis.sandbox.globalpay.com` |
| U4 | `getGpApiBase()` | Returns prod URL when `GP_ENVIRONMENT=production` | Contains `apis.globalpay.com` (no sandbox) |
| U5 | Token cache | Second call within TTL returns same token | Token object reused |
| U6 | Token cache | Call after TTL expires regenerates token | New token fetched |

**Notes:**
- Mock `fetch` for U5/U6 — don't hit real API in unit tests
- `auth.js` uses ES modules — jest config needs `"transform": {}` + `--experimental-vm-modules`

---

**File to create:** `nodejs/tests/unit/server.test.js`

| # | What to test | Expected |
|---|-------------|---------|
| U7 | `POST /get-access-token` with mocked token | Returns `{ success: true, token: "...", expiresIn: 600 }` |
| U8 | `POST /get-access-token` when token fails | Returns `{ success: false }` with 4xx/5xx status |
| U9 | CORS headers present on responses | `Access-Control-Allow-Origin` header exists |

---

### 1B — Java Unit Tests

**Setup:** Add to `java/pom.xml` test scope:
```xml
<dependency>
  <groupId>org.junit.jupiter</groupId>
  <artifactId>junit-jupiter</artifactId>
  <version>5.10.0</version>
  <scope>test</scope>
</dependency>
<dependency>
  <groupId>org.mockito</groupId>
  <artifactId>mockito-core</artifactId>
  <version>5.4.0</version>
  <scope>test</scope>
</dependency>
```

**File to create:** `java/src/test/java/com/globalpayments/example/ProcessPaymentServletTest.java`

| # | Method | What to test | Expected |
|---|--------|-------------|---------|
| U10 | `generateNonce()` | Returns 32-char hex string | `nonce.matches("[0-9a-f]{32}")` |
| U11 | `hashSecret(nonce, key)` | Returns 128-char hex string | `hash.matches("[0-9a-f]{128}")` |
| U12 | `hashSecret(nonce, key)` | Same inputs → same output | Deterministic |
| U13 | `hashSecret(nonce, key)` | Different nonce → different hash | Not equal |

**File to create:** `java/src/test/java/com/globalpayments/example/GpApi3dsServletTest.java`

| # | Method | What to test | Expected |
|---|--------|-------------|---------|
| U14 | `toMinorUnits(10.00)` | Converts dollars to cents | Returns `1000` |
| U15 | `toMinorUnits(0.01)` | Smallest unit | Returns `1` |
| U16 | `toMinorUnits(9.99)` | Fractional dollars | Returns `999` |
| U17 | Token cache (via reflection) | Concurrent `getAccessToken()` calls | Single token generated (lock works) |

**Run:** `cd java/ && mvn test`

---

### 1C — PHP Unit Tests

**Setup:**
```bash
cd php/
composer require --dev phpunit/phpunit:^10
```

**File to create:** `php/tests/unit/ConfigTest.php`

| # | What to test | Expected |
|---|-------------|---------|
| U18 | `config.php` returns `GP_ENVIRONMENT` from env | Returns `sandbox` when set |
| U19 | `config.php` defaults to `sandbox` when env missing | Returns `sandbox` |

**File to create:** `php/tests/unit/GetAccessTokenTest.php`

| # | What to test | Expected |
|---|-------------|---------|
| U20 | Nonce generation uses `random_bytes(16)` → 32 hex chars | Length is 32 |
| U21 | SHA-512 hash of nonce+key is 128 hex chars | Length is 128 |

**Run:** `cd php/ && ./vendor/bin/phpunit tests/unit/`

---

### 1D — .NET Unit Tests

**Setup:**
```bash
cd dotnet/
dotnet add package xunit
dotnet add package Moq
```

**File to create:** `dotnet/Tests/UtilityTests.cs`

| # | Method | What to test | Expected |
|---|--------|-------------|---------|
| U22 | `ToMinorUnits(10.00m)` | Dollar to cents | Returns `1000` |
| U23 | `ToMinorUnits(0.01m)` | Minimum | Returns `1` |
| U24 | `TwoDigitYear(2025)` | Full year | Returns `"25"` |
| U25 | `TwoDigitYear(2030)` | Far year | Returns `"30"` |
| U26 | Token cache | Concurrent token requests | Single token generated |

**Run:** `cd dotnet/ && dotnet test`

---

## Level 2: Integration Tests

> These hit the real GP-API sandbox. Need valid credentials.

### 2A — Token Endpoint (`/get-access-token`)

Run against each language individually (start the service first).

**For each language:**
```bash
# Start service (pick one)
cd nodejs/ && npm start           # http://localhost:8000
cd java/ && mvn cargo:run         # http://localhost:8000
cd php/ && php -S localhost:8000 router.php
cd dotnet/ && dotnet run          # http://localhost:8000
```

**Test cases (curl or HTTP client):**

| # | Request | Expected |
|---|---------|---------|
| I1 | `POST /get-access-token` no body | `{ success: true, token: "...", expiresIn: 600 }` |
| I2 | Token is non-empty string | `token.length > 0` |
| I3 | Token has GP-API JWT format | Starts with GP-API header chars |
| I4 | Status code | HTTP 200 |
| I5 | Call twice, within 60s | Same token returned (cache works) |
| I6 | `Content-Type` response header | `application/json` |

---

### 2B — 3DS2 Check Enrollment (`/api/check-enrollment`)

**Setup:** Need access token from I1 first. Use token in all 3DS2 calls.

| # | Request body | Expected response |
|---|-------------|-----------------|
| I7 | `{ "amount": "10.00", "currency": "USD", "tokenResponse": "<valid PMT token>" }` | `{ "serverTransactionId": "...", "status": "ENROLLED" or "NOT_ENROLLED" }` |
| I8 | Missing `amount` field | Error response, HTTP 4xx |
| I9 | Invalid currency code | Error or 4xx |
| I10 | Amount as string vs number | Both accepted or graceful error |

---

### 2C — Health Check

| # | Request | Expected |
|---|---------|---------|
| I11 | `GET /api/health` (Java) | HTTP 200, `{ "status": "ok" }` |
| I12 | `GET /api/health` (.NET) | HTTP 200, `{ "status": "ok" }` |

> Note: Node.js and PHP do **not** have health endpoints. Confirm this is intentional.

---

### 2D — Full 3DS2 Flow (Happy Path)

> This requires browser interaction (Drop-In UI provides browser fingerprint data).
> Manual test or use E2E (Level 3). Automate via Playwright.

Steps:
1. `POST /get-access-token` → get token
2. Load index.html with token → Drop-In UI renders
3. Enter test card (see GP sandbox card list in README)
4. Drop-In emits `payment-reference` event with `PMT_xxx`
5. `POST /api/check-enrollment` with amount + PMT token
6. `POST /api/initiate-auth` with serverTransactionId + browser fingerprint data
7. Challenge shown (if CHALLENGE_REQUIRED) or skip
8. `POST /api/get-auth-result` → authStatus = `SUCCESS_AUTHENTICATED`
9. `POST /api/authorize-payment` → transaction approved

Expected final response:
```json
{
  "success": true,
  "message": "Payment successful!",
  "data": { "transactionId": "TRN_...", "status": "CAPTURED" }
}
```

---

## Level 3: E2E Tests (Playwright)

### Setup

```bash
npm install --save-dev @playwright/test
npx playwright install chromium
```

**Create files:**
- `playwright.config.js`
- `tests/e2e/basic-payment.spec.js`
- `tests/e2e/3ds2-flow.spec.js`

### playwright.config.js template

```js
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 60_000,
  retries: 1,
  use: {
    headless: true,
    screenshot: 'on-first-retry',
    video: 'on-first-retry',
  },
  projects: [
    { name: 'nodejs',  use: { baseURL: 'http://nodejs:8000'  } },
    { name: 'java',    use: { baseURL: 'http://java:8000'    } },
    { name: 'php',     use: { baseURL: 'http://php:8000'     } },
    { name: 'dotnet',  use: { baseURL: 'http://dotnet:8000'  } },
  ],
});
```

### E2E Test Cases

**File:** `tests/e2e/basic-payment.spec.js`

| # | Scenario | Steps | Expected |
|---|----------|-------|---------|
| E1 | Page loads | Navigate to `/` | Title visible, Drop-In UI renders in iframe |
| E2 | Drop-In loads | Wait for iframe from `pay.sandbox.realexpayments.com` | Iframe visible |
| E3 | Token fetched on load | Check network tab or page JS | No 4xx errors on token request |

**File:** `tests/e2e/3ds2-flow.spec.js`

| # | Scenario | Steps | Expected |
|---|----------|-------|---------|
| E4 | Full frictionless flow | Use frictionless test card, submit | "Payment successful!" shown |
| E5 | Challenge flow | Use challenge test card, complete challenge | "Payment successful!" shown |
| E6 | Declined card | Use declined test card | Error message shown (not crash) |
| E7 | Invalid amount | Submit with 0 amount | Validation error |

**Test cards** (from GP-API sandbox docs):
- Frictionless success: `4263970000005262`
- Challenge required: `4012001038443335`
- Declined: `4000120000001154`

### Run via Docker

```bash
docker compose --profile testing up --build
# Or run tests service only (services must already be up):
docker compose run --rm tests
```

---

## Level 4: Cross-Language Parity Tests

Goal: Same HTTP request to all 4 backends → same response shape.

**Script to create:** `tests/parity/parity-check.sh`

```bash
#!/bin/bash
# Start all services, then run:

AMOUNT="10.00"
CURRENCY="USD"

for PORT in 8001 8002 8003 8004; do  # adjust per docker-compose ports
  echo "=== Port $PORT ==="
  curl -s -X POST "http://localhost:$PORT/get-access-token" \
    -H "Content-Type: application/json" | jq '.success, (.token | length)'
done
```

**Parity assertions:**

| # | Field | All languages must agree |
|---|-------|------------------------|
| P1 | `POST /get-access-token` → `.success` | `true` |
| P2 | `POST /get-access-token` → `.expiresIn` | `600` |
| P3 | `POST /get-access-token` → `.token` is non-empty | `true` |
| P4 | `POST /api/check-enrollment` response shape | Same JSON keys |
| P5 | `POST /api/authorize-payment` success response | `{ success: true, message: "Payment successful!" }` |
| P6 | HTTP status codes | All return 200 for valid requests |

---

## Test Execution Order

```
Day 1 (unit tests — no credentials needed):
  □ 1A Node.js unit tests
  □ 1B Java unit tests  
  □ 1C PHP unit tests
  □ 1D .NET unit tests

Day 2 (integration — need sandbox credentials):
  □ Set up .env files in all 4 language dirs
  □ 2A Token endpoint — all 4 languages
  □ 2B Check enrollment — all 4 languages
  □ 2C Health check (Java + .NET)

Day 3 (E2E + parity):
  □ Create playwright.config.js
  □ Create E2E test files
  □ Run via Docker Compose
  □ Parity check across languages
```

---

## Known Issues to Verify

| # | Issue | Where | How to verify |
|---|-------|-------|--------------|
| K1 | `GP_ACCOUNT_NAME` breaks token auth | All langs | Set it and confirm 4xx, then blank it |
| K2 | Java pom.xml targets Java 25 | `java/pom.xml` | Run `mvn verify` and confirm no compile errors |
| K3 | PHP no token cache | `php/get-access-token.php` | Check response time on repeated calls |
| K4 | Node.js uses ES modules | `nodejs/package.json` | Jest needs `--experimental-vm-modules` |
| K5 | .NET gzip auto-decompression | `Program.cs` | GP-API response decompresses cleanly |

---

## Pass/Fail Criteria

| Level | Pass condition |
|-------|--------------|
| Unit | 100% of unit tests green, no mocks bypassing actual logic |
| Integration | Token endpoint returns valid token; 3DS2 enrollment returns valid serverTransactionId |
| E2E | Frictionless test card completes full flow on all 4 backends |
| Parity | All 4 backends return identical response shapes for identical inputs |

---

## Files to Create (checklist for executor)

```
nodejs/tests/unit/auth.test.js
nodejs/tests/unit/server.test.js
java/src/test/java/com/globalpayments/example/ProcessPaymentServletTest.java
java/src/test/java/com/globalpayments/example/GpApi3dsServletTest.java
php/tests/unit/ConfigTest.php
php/tests/unit/GetAccessTokenTest.php
dotnet/Tests/UtilityTests.cs
playwright.config.js
tests/e2e/basic-payment.spec.js
tests/e2e/3ds2-flow.spec.js
tests/parity/parity-check.sh
```

Modify existing:
```
java/pom.xml           — add JUnit 5 + Mockito test deps
php/composer.json      — add phpunit/phpunit dev dep
nodejs/package.json    — add jest + test script
dotnet/dotnet.csproj   — add xunit + Moq
```
