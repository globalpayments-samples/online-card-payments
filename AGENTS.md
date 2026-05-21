# Global Payments Online Card Payments

> Tokenize a card with the GP-API Drop-In UI and charge it via a server-side Sale transaction, demonstrated in PHP, Node.js, Java, and .NET.

## Critical Patterns

1. **Two-token architecture: a tokenization access token (browser) and a transaction token (server).** Each backend first calls `POST /ucp/accesstoken` directly with the `PMT_POST_Create_Single` permission and returns the short-lived token to the browser. The Drop-In UI uses it to tokenize the card into a `PMT_…` payment reference. The server then takes that reference and processes the charge using the SDK — which generates its own transaction-scoped token internally. Skipping the `PMT_POST_Create_Single` permission causes the Drop-In UI to silently fail to render the iframe.

2. **The access token call is direct REST, not SDK.** All four implementations build the `/ucp/accesstoken` request by hand: SHA-512 hash of `nonce + appKey` as the `secret`, `X-GP-Version: 2021-03-22` header, no `X-GP-Api-Key` header. The SDK is only used for the `card.charge()` (Sale) call. Mixing this up — for example, trying to generate the tokenization token through the SDK — produces a token without the right permissions for Drop-In UI.

3. **Use `charge()`, not `verify()`, for a Sale.** The PHP `process-sale.php`, Node `server.js`, Java `handleProcessSale()`, and .NET `/process-sale` handler all call `card.charge(amount).withCurrency(currency).execute()`. This combines auth + capture in one call. `verify()` would tokenize/validate the card without moving funds, leaving the merchant unpaid — easy mistake when adapting from a tokenization-only sample.

4. **Do not set `accountName` / `GP_ACCOUNT_NAME` on `GpApiConfig`.** Every implementation leaves account name unset and lets the SDK auto-detect from the `GP_APP_ID` / `GP_APP_KEY` pair. Setting it manually causes the "Access token and merchant info do not match" error documented in the troubleshooting section of the README. The `.env.sample` keeps `GP_ACCOUNT_NAME` commented out for the same reason.

## Repository Structure

### PHP (built-in PHP server + Global Payments SDK)
- [`php/get-access-token.php`](php/get-access-token.php) — direct cURL POST to `/ucp/accesstoken`; SHA-512 secret built inline
- [`php/process-sale.php`](php/process-sale.php) — configures `GpApiConfig`, calls `CreditCardData->charge()`
- [`php/config.php`](php/config.php) — legacy endpoint returning `PUBLIC_API_KEY`; not used by the Drop-In UI flow but left in place
- [`php/index.html`](php/index.html) — frontend Drop-In UI form
- [`php/composer.json`](php/composer.json) — `globalpayments/php-sdk` ^13.4, `vlucas/phpdotenv` ^5.5

### Node.js (Express + Global Payments SDK)
- [`nodejs/server.js`](nodejs/server.js) — `POST /get-access-token` handler builds the token request inline; `POST /process-sale` handler configures `GpApiConfig` and calls `card.charge()`
- [`nodejs/index.html`](nodejs/index.html) — frontend Drop-In UI form
- [`nodejs/package.json`](nodejs/package.json) — `globalpayments-api` ^3.10.6, `express` ^4.18, `dotenv` ^16.3

### Java (Jakarta Servlet + Global Payments SDK)
- [`java/src/main/java/com/globalpayments/example/ProcessPaymentServlet.java`](java/src/main/java/com/globalpayments/example/ProcessPaymentServlet.java) — single servlet on both routes; `generateNonce()`, `hashSecret()`, `handleGetAccessToken()`, `handleProcessSale()`
- [`java/src/main/webapp/index.html`](java/src/main/webapp/index.html) — frontend Drop-In UI form
- [`java/pom.xml`](java/pom.xml) — `globalpayments-sdk` 14.2.20, Jakarta Servlet 5.0, org.json, Cargo Maven plugin

### .NET (ASP.NET Core minimal API + Global Payments SDK)
- [`dotnet/Program.cs`](dotnet/Program.cs) — `GenerateNonce()`, `HashSecret()`, `ConfigureEndpoints()` registers both routes; charge uses `CreditCardData.Charge()`
- [`dotnet/wwwroot/index.html`](dotnet/wwwroot/index.html) — frontend Drop-In UI form
- [`dotnet/dotnet.csproj`](dotnet/dotnet.csproj) — `GlobalPayments.Api` 9.0.16, `DotEnv.Net` 3.2.1, net9.0

### Shared
- [`index.html`](index.html) — legacy root form (broken stylesheet reference, posts to non-existent `/process-payment.php`); the per-language `index.html` copies are what each server actually serves
- [`docker-compose.yml`](docker-compose.yml) — multi-service compose; currently lists `python` and `go` services that do not exist in the tree and references stale `PUBLIC_API_KEY` / `SECRET_API_KEY` env vars
- [`README.md`](README.md) — root onboarding doc

## API Surface

| Method | Path (Node/Java/.NET) | Path (PHP) | Purpose |
|--------|----------------------|------------|---------|
| POST | `/get-access-token` | `/get-access-token.php` | Returns a short-lived Drop-In UI tokenization token (`PMT_POST_Create_Single`) |
| POST | `/process-sale` | `/process-sale.php` | Charges the tokenized payment reference via the SDK; returns transaction details |

JSON request/response shapes are identical across all four implementations. The PHP implementation uses the built-in PHP web server with no router, so endpoints are exposed at their `.php` filename — each language's `index.html` is wired accordingly (PHP's calls `fetch('get-access-token.php')`, the others call `fetch('/get-access-token')`). There is no `GET /config` endpoint in this project (PHP's `config.php` is a vestigial file not on the documented flow).

## Environment Variables

```bash
GP_APP_ID=your_app_id_here       # GP-API application ID (from developer dashboard)
GP_APP_KEY=your_app_key_here     # GP-API application key (used for SHA-512 secret + SDK config)
GP_ENVIRONMENT=sandbox           # "sandbox" or "production"; selects the API endpoint
# GP_ACCOUNT_NAME=...            # Optional and discouraged — keep commented; SDK auto-detects
PORT=8000                        # Optional; Node/.NET honor this, PHP run.sh honors it, Java is fixed by Cargo
```

Each language directory has its own `.env.sample` — copy to `.env` and fill in credentials.

## API Request Shape

Applies to the access-token call only — both PHP, Node.js, Java, and .NET make this request by hand (the SDK handles the `/process-sale` wire format).

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

| Brand | Number | CVV | Expiry |
|-------|--------|-----|--------|
| Visa | 4263970000005262 | 123 | Any future date |
| Mastercard | 5425230000004415 | 123 | Any future date |

Get sandbox credentials at [developer.globalpayments.com](https://developer.globalpayments.com).

## Architecture Summary

**Tokenization:** browser → `POST /get-access-token` → server SHA-512 + direct REST → GP-API returns tokenization token → Drop-In UI iframe tokenizes the card → `PMT_…` reference returned to browser.

**Charge:** browser → `POST /process-sale` with `{payment_reference, amount, currency}` → server configures `GpApiConfig` → `CreditCardData(token).charge(amount).withCurrency(currency).execute()` → response with `transactionId`, `responseCode`, `responseMessage`.

## Security Notes

These demos have no authentication on either endpoint, log nothing about transactions, allow CORS from `*`, and store credentials in plain `.env` files. The `index.html` at the root references a broken stylesheet (`styles.csscss/styles.css`) and posts to a non-existent endpoint — do not serve it. For production: add auth on `/process-sale`, restrict CORS, use a secrets manager, enable HTTPS, and remove or repair the root `index.html`.

## How to Run

```bash
cd php && ./run.sh       # PHP — :8000 (composer install + php -S 0.0.0.0:$PORT)
cd nodejs && ./run.sh    # Node.js — :8000 (npm install + npm start)
cd java && ./run.sh      # Java — :8000 (mvn clean package cargo:run; cargo.servlet.port=8000 in pom.xml)
cd dotnet && ./run.sh    # .NET — :8000 (dotnet restore + dotnet run; honors PORT env)
```

`docker-compose up` currently fails because the compose file references `python` and `go` services that are not present in this repo. Use the per-language `./run.sh` until the compose file is repaired.

The `/get-access-token` and `/process-sale` endpoints can be exercised with curl, but a full end-to-end flow requires a real browser to render the Drop-In UI iframe (`https://js.globalpay.com/4.1.11/globalpayments.js`) and produce the `PMT_…` payment reference.

## How to Verify

```bash
# Get a tokenization access token (no body required)
curl -X POST http://localhost:8000/get-access-token         # Node, Java, .NET
curl -X POST http://localhost:8000/get-access-token.php     # PHP
# Expected: {"success":true,"token":"...","expiresIn":600}

# Process a sale — requires a real PMT_... reference from the Drop-In UI
curl -X POST http://localhost:8000/process-sale \
  -H "Content-Type: application/json" \
  -d '{"payment_reference":"PMT_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx","amount":10.00,"currency":"USD"}'
# PHP variant: POST http://localhost:8000/process-sale.php (same body)
# Expected: {"success":true,"message":"Payment successful!","data":{"transactionId":"TRN_...","status":"SUCCESS","amount":10,"currency":"USD","reference":"...","timestamp":"..."}}
```

A synthetic `payment_reference` will return `{"success":false,"message":"Payment processing failed","error":"..."}` from the SDK — to validate the charge path end-to-end, open the browser at `http://localhost:8000`, tokenize a test card with the Drop-In UI, and submit the form.

## Making Changes

All four implementations expose identical behavior. A change to one must be applied to all — each language in a separate commit. Do not modify shared files (`index.html` at the root, `docker-compose.yml`) without confirming the change is consistent with every per-language implementation. The per-language `index.html` files (`php/index.html`, `nodejs/index.html`, `java/src/main/webapp/index.html`, `dotnet/wwwroot/index.html`) are independent copies — frontend changes must be applied to all four. Python and Go implementations are intentionally absent; do not add them without explicit instruction.

## SDK Versions

- **PHP**: `globalpayments/php-sdk` ^13.4
- **Node.js**: `globalpayments-api` ^3.10.6
- **Java**: `globalpayments-sdk` (com.heartlandpaymentsystems) 14.2.20
- **.NET**: `GlobalPayments.Api` 9.0.16
