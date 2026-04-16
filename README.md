# Online Card Payments with 3DS2

Sample implementations of a card payment flow using the Global Payments Drop-In UI and 3DS2 authentication. The same backend is written in four languages so you can pick the one that fits your stack.

Available backends: [PHP](./php/), [Node.js](./nodejs/), [Java](./java/), [.NET](./dotnet/)

---

## How the payment flow works

The Drop-In UI handles card entry in the browser. Your server never sees the raw card number. When the customer submits the form, the card is tokenized on the client side into a payment method token (PMT), which is then passed through a 3DS2 authentication sequence before the final charge.

The six steps, all driven by the frontend after the initial page load:

1. **Check enrollment** — your server asks GP whether the card is enrolled in 3DS2.
2. **3DS method** — if the issuer provides a `method_url`, the browser loads it in a hidden iframe to gather device fingerprint data.
3. **Initiate authentication** — your server submits browser data and receives either a frictionless approval or a challenge requirement.
4. **ACS challenge** — if a challenge is required, an iframe overlay presents the issuer's authentication page to the cardholder.
5. **Get auth result** — your server retrieves the final authentication status.
6. **Authorize payment** — your server submits a SALE transaction with the 3DS2 proof attached.

---

## Quick start

All backends listen on port 8000 by default. Pick one and follow these steps.

**1. Copy and fill in the environment file**

```bash
cd nodejs    # or php, java, dotnet
cp .env.sample .env
```

Edit `.env` and fill in at least `GP_APP_ID`, `GP_APP_KEY`, and the notification URLs (see below).

**2. Install and run**

Node.js:
```bash
npm install
npm start
```

PHP:
```bash
composer install
php -S localhost:8000 router.php
```

Java:
```bash
mvn clean package
mvn cargo:run
```

.NET:
```bash
dotnet restore
dotnet run
```

**3. Open the browser**

Go to `http://localhost:8000` and use one of the test cards below.

---

## Environment variables

| Variable | Required | Notes |
|---|---|---|
| `GP_APP_ID` | yes | From the GP developer portal |
| `GP_APP_KEY` | yes | From the GP developer portal |
| `GP_ENVIRONMENT` | yes | `sandbox` or `production` |
| `GP_MERCHANT_ID` | no | Auto-detected from token if blank |
| `GP_ACCOUNT_ID` | no | Auto-detected from token if blank |
| `GP_ACCOUNT_NAME` | no | Leave blank or set to `transaction_processing`. Setting any other value will break authentication. |
| `METHOD_NOTIFICATION_URL` | yes | Public URL for `/3ds/method-notification` on your server |
| `CHALLENGE_NOTIFICATION_URL` | yes | Public URL for `/3ds/challenge-notification` on your server |

Both notification URLs must be reachable from the public internet because the ACS (the bank's authentication server) POSTs to them. For local development, expose your server with [ngrok](https://ngrok.com):

```bash
ngrok http 8000
# Then set both vars to: https://<your-subdomain>.ngrok.io/3ds/...
```

---

## Test cards (sandbox)

| Scenario | Card number |
|---|---|
| Frictionless success (no challenge) | 4263 9700 0000 5262 |
| Challenge required | 4012 0010 3844 3335 |
| Declined | 4000 1200 0000 1154 |

Use any future expiry date and any 3-digit CVV. More test cards at [developer.globalpay.com/resources/test-cards](https://developer.globalpay.com/resources/test-cards).

---

## Running with Docker

Each backend has an entry in `docker-compose.yml`. Copy and fill in an `.env` file per language first, then start whichever backends you need:

```bash
docker compose up nodejs
# or php, java, dotnet
# or all four at once: docker compose up
```

Port mappings when all four run simultaneously:

| Backend | Host port |
|---|---|
| Node.js | 8001 |
| PHP | 8003 |
| Java | 8004 |
| .NET | 8006 |

---

## Running the tests

Unit tests cover utility functions and GP-API payload mapping (color depth enums, boolean uppercasing, payload field placement, notification endpoint contracts).

```bash
# Node.js
cd nodejs && npm test

# PHP
cd php && ./vendor/bin/phpunit

# .NET
dotnet test dotnet/Tests/Tests.csproj

# Java
mvn -f java/pom.xml test
```

An integration test runner is also included at `tests/integration/run-integration-tests.sh`. It requires a running backend and real sandbox credentials since it calls live GP-API endpoints.

```bash
BASE_URL=http://localhost:8000 bash tests/integration/run-integration-tests.sh
```

---

## Project structure

```
.
├── index.html                  shared frontend (language-agnostic)
├── docker-compose.yml
├── tests/integration/          curl-based integration test runner
├── nodejs/
│   ├── server.js               Express backend
│   ├── auth.js                 token cache module
│   ├── index.html
│   └── tests/unit/
├── php/
│   ├── router.php
│   ├── get-access-token.php
│   ├── api/                    one file per endpoint
│   ├── src/GpApiClient.php     HTTP client with token cache
│   ├── index.html
│   └── tests/unit/
├── java/
│   └── src/main/java/.../
│       ├── GpApi3dsServlet.java     3DS2 endpoints
│       └── ProcessPaymentServlet.java
├── dotnet/
│   ├── Program.cs
│   ├── Utilities.cs            ToMinorUnits, MapColorDepth, MapBool
│   └── Tests/
└── online-card-payments.sln
```

---

## Resources

- [Global Payments developer portal](https://developer.globalpay.com/)
- [Drop-In UI guide](https://developer.globalpay.com/docs/payments/online/drop-in-ui-guide)
- [GP-API reference](https://developer.globalpay.com/api)
- [PHP SDK](https://github.com/globalpayments/php-sdk)
- [Node.js SDK](https://github.com/globalpayments/node-sdk)
- [Java SDK](https://github.com/globalpayments/java-sdk)
- [.NET SDK](https://github.com/globalpayments/dotnet-sdk)
