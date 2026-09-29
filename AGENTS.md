# Global Payments Online Card Payments

> Tokenize a card with the GP-API Drop-In UI, authenticate it with 3DS2, and charge it via a server-side Sale transaction, demonstrated in PHP, Node.js, Java, and .NET.

## Critical Patterns

1. **Two-token architecture: a tokenization access token (browser) and a backend access token (server).** `POST /get-access-token` calls `POST /ucp/accesstoken` directly with the `PMT_POST_Create_Single` permission and returns the short-lived token to the browser. The Drop-In UI uses it to tokenize the card into a `PMT_…` payment reference. Every 3DS2 and Sale call then goes out with a separate backend token (no `permissions` field) that the server generates itself and caches until 60 seconds before expiry: in memory for Node, Java and .NET, in `/tmp/gpapi_token.json` for PHP. Skipping the `PMT_POST_Create_Single` permission causes the Drop-In UI to silently fail to render the iframe.

2. **Every GP-API call is direct REST, not SDK.** All four implementations build `/ucp/accesstoken`, `/ucp/authentications` and `/ucp/transactions` requests by hand with an `X-GP-Version: 2021-03-22` header and a `Bearer` token. Both token calls use a SHA-512 hash of `nonce + appKey` as the `secret`: the tokenization token uses 16 random bytes hex-encoded as the nonce, the backend token uses an ISO-8601 timestamp, which the `transaction_processing` account requires. The Global Payments SDKs are still declared in each manifest, but nothing in the 3DS2 flow imports them. Only the legacy `php/process-sale.php` does.

3. **The Sale carries the 3DS2 proof.** The browser drives six steps in order: check enrollment, 3DS method iframe (only when a `method_url` comes back), initiate auth, ACS challenge (only when `acs_challenge_url` and `acs_signed_content` come back), get auth result, authorize payment. `/api/authorize-payment` sends `POST /ucp/transactions` with `type: "SALE"` and a `three_ds` block holding `authentication_value`, `server_trans_ref`, `ds_trans_ref`, `eci` and `message_version` from the auth result. Amounts go to GP-API as minor-unit strings (`"10.00"` becomes `"1000"`), browser data uses GP-API enums (`color_depth: "TWENTY_FOUR_BITS"`, `java_enabled: "TRUE"` or `"FALSE"`), and an `AUT_` prefix on `server_trans_id` is stripped before it goes back to GP-API. The unit tests in each language pin these mappings down.

4. **Leave `GP_ACCOUNT_NAME` as `transaction_processing`.** Every authentication and transaction request sends `account_name` from `GP_ACCOUNT_NAME`, falling back to `transaction_processing`, along with `account_id` from `GP_ACCOUNT_ID` and `merchant_id` from `GP_MERCHANT_ID`. Any other account name breaks authentication.

5. **Notification endpoints take form POSTs, and every payment has its own nonce.** The ACS POSTs form-encoded data (`cres=...`) to `/3ds/challenge-notification`, and the method iframe lands on `/3ds/method-notification`, so each backend routes these paths before any JSON parsing. The frontend creates a `flow_nonce` per payment, the backend appends it as `?nonce=` to both notification URLs, and the returned page posts `{type: 'methodComplete' | 'authResult', nonce}` to the parent window. The frontend only accepts a message whose origin matches the page and whose nonce matches the current flow.

## Repository Structure

### PHP (built-in PHP server + direct REST)
- [`php/router.php`](php/router.php): router for `php -S`; maps the health, token, 3DS2 and notification routes, returns 404 for any other `/api/*` path and serves static files otherwise
- [`php/get-access-token.php`](php/get-access-token.php): direct cURL POST to `/ucp/accesstoken`; SHA-512 secret built inline
- [`php/api/`](php/api/): one file per endpoint (`check-enrollment.php`, `initiate-auth.php`, `get-auth-result.php`, `authorize-payment.php`, `health.php`, `method-notification.php`, `challenge-notification.php`)
- [`php/src/GpApiClient.php`](php/src/GpApiClient.php): backend token with file cache, cURL wrapper, `toMinorUnits`, `mapColorDepth`, `mapBool`, JSON response helpers
- [`php/process-sale.php`](php/process-sale.php): legacy SDK Sale endpoint from before 3DS2; not routed and not called by the frontend, but the built-in server still runs it if requested by filename
- [`php/config.php`](php/config.php): legacy endpoint returning `PUBLIC_API_KEY`; not used by the Drop-In UI flow but left in place
- [`php/index.html`](php/index.html): 3DS2 frontend (copy of the shared `index.html`)
- [`php/tests/unit/GpApiClientTest.php`](php/tests/unit/GpApiClientTest.php): PHPUnit tests
- [`php/composer.json`](php/composer.json): `globalpayments/php-sdk` ^13.4 (only used by `process-sale.php`), `vlucas/phpdotenv` ^5.5, `phpunit/phpunit` ^10.5 (dev)

### Node.js (Express + direct REST)
- [`nodejs/server.js`](nodejs/server.js): every route; `POST /get-access-token` builds the token request inline, the `/api/*` handlers call GP-API through `gpRequest()`; also holds `toMinorUnits`, `mapColorDepth`, `mapBool` and the notification pages
- [`nodejs/auth.js`](nodejs/auth.js): backend token generation and in-memory cache, `getGpApiBase()`
- [`nodejs/index.html`](nodejs/index.html): 3DS2 frontend (copy of the shared `index.html`)
- [`nodejs/tests/unit/`](nodejs/tests/unit/): Jest tests (`auth.test.js`, `server.test.js`)
- [`nodejs/package.json`](nodejs/package.json): `express` ^4.18, `dotenv` ^16.3, `jest` ^29.7 (dev); `globalpayments-api` ^3.10.6 is still listed but not imported

### Java (Jakarta Servlet + direct REST)
- [`java/src/main/java/com/globalpayments/example/ProcessPaymentServlet.java`](java/src/main/java/com/globalpayments/example/ProcessPaymentServlet.java): `/get-access-token` only; `generateNonce()`, `hashSecret()`, direct POST to `/ucp/accesstoken`
- [`java/src/main/java/com/globalpayments/example/GpApi3dsServlet.java`](java/src/main/java/com/globalpayments/example/GpApi3dsServlet.java): `/api/health`, the four `/api/*` 3DS2 and payment routes and both `/3ds/*` notification routes; backend token cache in `static volatile` fields behind a `ReentrantLock`, `java.net.http.HttpClient` with manual GZIP decoding
- [`java/src/main/webapp/index.html`](java/src/main/webapp/index.html): 3DS2 frontend (copy of the shared `index.html`)
- [`java/src/test/java/com/globalpayments/example/GpApi3dsServletTest.java`](java/src/test/java/com/globalpayments/example/GpApi3dsServletTest.java): JUnit 5 tests, private helpers reached via reflection
- [`java/pom.xml`](java/pom.xml): `globalpayments-sdk` 14.2.20 (declared, not used), Jakarta Servlet 5.0, org.json, dotenv-java, JUnit 5, Cargo Maven plugin

### .NET (ASP.NET Core minimal API + direct REST)
- [`dotnet/Program.cs`](dotnet/Program.cs): every route via `MapGet`, `MapPost` and `MapMethods`; backend token cache and a `GpRequest()` helper over `HttpClient` with automatic decompression; listens on `PORT` (default 8000)
- [`dotnet/Utilities.cs`](dotnet/Utilities.cs): `GpUtilities.ToMinorUnits`, `TwoDigitYear`, `MapColorDepth`, `MapBool`
- [`dotnet/wwwroot/index.html`](dotnet/wwwroot/index.html): 3DS2 frontend (copy of the shared `index.html`)
- [`dotnet/Tests/`](dotnet/Tests/): xUnit tests (`UtilityTests.cs`)
- [`dotnet/dotnet.csproj`](dotnet/dotnet.csproj): `GlobalPayments.Api` 9.0.16 (declared, not used), `DotEnv.Net` 3.2.1, net9.0

### Shared
- [`index.html`](index.html): the 3DS2 frontend; the four per-language `index.html` files are identical copies of it, and those copies are what each server actually serves
- [`docker-compose.yml`](docker-compose.yml): `nodejs`, `php`, `java` and `dotnet` services, each built from its own `Dockerfile`, reading `<language>/.env` and health-checked on `/api/health`; plus a `tests` service under the `testing` profile
- [`Dockerfile.tests`](Dockerfile.tests): Alpine image that runs the integration runner against all four services
- [`tests/integration/run-integration-tests.sh`](tests/integration/run-integration-tests.sh): curl-based integration runner; needs a running backend and real sandbox credentials
- [`docker-run.sh`](docker-run.sh): wrapper for build, start, stop, test, logs, clean and status across the compose services
- [`README.md`](README.md): root onboarding doc

## API Surface

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/health` | Returns `{"status":"ok","backend":"<language>","version":"1.0.0"}` |
| POST | `/get-access-token` | Returns a short-lived Drop-In UI tokenization token (`PMT_POST_Create_Single`) |
| POST | `/api/check-enrollment` | Step 1: `POST /ucp/authentications` with the `PMT_…` token; returns `server_trans_id`, `enrolled`, `message_version`, `method_url`, `method_data` |
| POST | `/api/initiate-auth` | Step 3: `POST /ucp/authentications` with browser data, order and payer; returns `status` plus the ACS challenge fields |
| POST | `/api/get-auth-result` | Step 5: `GET /ucp/authentications/{id}`; returns `status`, `eci`, `authentication_value`, `ds_trans_ref`, `message_version`, `server_trans_ref` |
| POST | `/api/authorize-payment` | Step 6: `POST /ucp/transactions` SALE with the 3DS2 proof; returns `transaction_id`, `status`, `result_code`, `amount`, `currency` |
| GET, POST | `/3ds/method-notification` | Method iframe callback; posts `methodComplete` to the parent window |
| GET, POST | `/3ds/challenge-notification` | ACS challenge callback; posts `authResult` to the parent window |

JSON request/response shapes are identical across all four implementations. The `/api/*` routes answer `{"success":true,"data":{...},"raw":{...}}` where `raw` is the untouched GP-API body, and failures come back as `{"success":false,"error":"...","gp_error_code":"...","gp_error_detail":"...","raw":{...}}`. PHP runs behind `router.php`, so it exposes the same paths as the other three and every frontend copy calls `fetch('/get-access-token')` and the `/api/*` paths. There is no `GET /config` endpoint in this project (PHP's `config.php` is a vestigial file not on the documented flow).

## Environment Variables

```bash
GP_APP_ID=your_app_id_here                  # GP-API application ID (from developer dashboard)
GP_APP_KEY=your_app_key_here                # GP-API application key (used for the SHA-512 secret)
GP_ENVIRONMENT=sandbox                      # "sandbox" or "production"; selects the API endpoint
GP_MERCHANT_ID=MER_your_merchant_id_here    # Sent as merchant_id on every GP-API call
GP_ACCOUNT_ID=TRA_your_account_id_here      # Sent as account_id on every GP-API call
GP_ACCOUNT_NAME=transaction_processing      # Must stay transaction_processing; any other value breaks authentication
METHOD_NOTIFICATION_URL=https://<your-public-host>/3ds/method-notification
CHALLENGE_NOTIFICATION_URL=https://<your-public-host>/3ds/challenge-notification
PORT=8000                                   # Only in the Node and .NET samples; Node/.NET honor it, PHP run.sh reads it from the shell, Java is fixed by Cargo
```

Each language directory has its own `.env.sample`; copy it to `.env` and fill in credentials. Both notification URLs must be reachable from the public internet because the ACS POSTs to them; for local work, expose the server with `ngrok http 8000` and point both variables at the tunnel.

## API Request Shape

Applies to the tokenization token call (`/get-access-token`). The backend token that the 3DS2 calls use has the same shape without `seconds_to_expire` and `permissions`, and uses an ISO-8601 timestamp as the nonce.

- `POST https://apis.sandbox.globalpay.com/ucp/accesstoken` (or `https://apis.globalpay.com/...` in production)
- Headers: `Content-Type: application/json`, `X-GP-Version: 2021-03-22`
- Body:
  - `app_id` — `GP_APP_ID`
  - `nonce` — 16 random bytes hex-encoded
  - `secret` — `SHA512(nonce + GP_APP_KEY)` lowercase hex
  - `grant_type: "client_credentials"`
  - `seconds_to_expire: 600`
  - `permissions: ["PMT_POST_Create_Single"]` — required for Drop-In UI tokenization

## Test Cards

| Scenario | Number | CVV | Expiry |
|----------|--------|-----|--------|
| Frictionless success (no challenge) | 4263970000005262 | 123 | Any future date |
| Challenge required | 4012001038443335 | 123 | Any future date |
| Declined | 4000120000001154 | 123 | Any future date |

Get sandbox credentials at [developer.globalpayments.com](https://developer.globalpayments.com).

## Architecture Summary

**Tokenization:** browser → `POST /get-access-token` → server SHA-512 + direct REST → GP-API returns tokenization token → Drop-In UI iframe tokenizes the card → `PMT_…` reference returned to browser.

**Authentication:** browser → `POST /api/check-enrollment` with `{payment_token, flow_nonce}` → if a `method_url` comes back, hidden iframe posts `method_data` to it and waits for `methodComplete` → `POST /api/initiate-auth` with browser data and the method completion status → if the ACS asks for a challenge, iframe overlay posts `acs_signed_content` to `acs_challenge_url` and waits for `authResult` → `POST /api/get-auth-result` → `eci`, `authentication_value`, `ds_trans_ref`, `server_trans_ref`, `message_version`.

**Charge:** browser → `POST /api/authorize-payment` with `{payment_token, amount, currency, three_ds}` → server `POST /ucp/transactions` with `type: "SALE"` and the 3DS2 proof → response with `transaction_id`, `status`, `result_code`, `amount`, `currency`.

## Security Notes

These demos have no authentication on any endpoint, log nothing about transactions, allow CORS from `*`, and store credentials in plain `.env` files. The `/api/*` responses also hand the full GP-API body back to the browser under `raw`, and the notification pages post to the parent with target origin `*` (the frontend checks origin and nonce on receipt). The legacy `php/process-sale.php` is still reachable by filename on the PHP server. For production: add auth on the `/api/*` routes, restrict CORS, stop returning `raw`, use a secrets manager, enable HTTPS, and remove the legacy PHP files.

## How to Run

```bash
cd php && ./run.sh       # PHP, :8000 (composer install + php -S 0.0.0.0:$PORT router.php)
cd nodejs && ./run.sh    # Node.js — :8000 (npm install + npm start)
cd java && ./run.sh      # Java — :8000 (mvn clean package cargo:run; cargo.servlet.port=8000 in pom.xml)
cd dotnet && ./run.sh    # .NET — :8000 (dotnet restore + dotnet run; honors PORT env)
```

`docker compose up` builds and starts all four backends on host ports 8001 (Node.js), 8003 (PHP), 8004 (Java) and 8006 (.NET); each needs its own `.env` first. `docker compose --profile testing up` also runs the integration runner against them, which calls live GP-API and needs real sandbox credentials. `./docker-run.sh` wraps the same commands.

The `/api/health` and `/get-access-token` endpoints can be exercised with curl, but a full end-to-end flow requires a real browser to render the Drop-In UI iframe (`https://js.globalpay.com/v1.js`), produce the `PMT_…` payment reference and run the 3DS method and challenge iframes.

## How to Verify

```bash
# Health check
curl http://localhost:8000/api/health
# Expected: {"status":"ok","backend":"nodejs","version":"1.0.0"} (backend is php, java or dotnet on the others)

# Get a tokenization access token (no body required)
curl -X POST http://localhost:8000/get-access-token
# Expected: {"success":true,"token":"...","expiresIn":600,"environment":"sandbox"}

# Check enrollment, requires a real PMT_... reference from the Drop-In UI
curl -X POST http://localhost:8000/api/check-enrollment \
  -H "Content-Type: application/json" \
  -d '{"payment_token":"PMT_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx","flow_nonce":"test"}'
# Expected: {"success":true,"data":{"server_trans_id":"...","enrolled":"...","message_version":"...","method_url":...,"method_data":...},"raw":{...}}
```

A synthetic `payment_token` will return `{"success":false,"error":"...","gp_error_code":"...",...}` from GP-API. To validate the whole flow, point both notification URLs at a public tunnel, open the browser at `http://localhost:8000`, tokenize a test card with the Drop-In UI, and submit the form. `BASE_URL=http://localhost:8000 bash tests/integration/run-integration-tests.sh` covers the same endpoints with curl against a running backend.

## Making Changes

All four implementations expose identical behavior. A change to one must be applied to all, each language in a separate commit, with the unit tests in each language updated to match. Do not modify shared files (`docker-compose.yml`, `Dockerfile.tests`, `tests/integration/`) without confirming the change is consistent with every per-language implementation. The frontend exists as five identical copies (`index.html` at the root, `php/index.html`, `nodejs/index.html`, `java/src/main/webapp/index.html`, `dotnet/wwwroot/index.html`); frontend changes must be applied to all five. Python and Go implementations are intentionally absent; do not add them without explicit instruction.

## SDK Versions

These are still declared in each manifest, but the 3DS2 flow does not use them. Only the legacy `php/process-sale.php` imports the PHP SDK.

- **PHP**: `globalpayments/php-sdk` ^13.4
- **Node.js**: `globalpayments-api` ^3.10.6
- **Java**: `globalpayments-sdk` (com.heartlandpaymentsystems) 14.2.20
- **.NET**: `GlobalPayments.Api` 9.0.16
