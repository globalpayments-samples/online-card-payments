# Node.js backend

Express 4 implementation of the GP-API 3DS2 payment flow.

## Requirements

- Node.js 18 or later
- npm
- GP-API sandbox credentials

## Setup

```bash
cp .env.sample .env
# fill in GP_APP_ID, GP_APP_KEY, and the notification URLs
npm install
npm start
```

The server starts on port 8000 by default. Open `http://localhost:8000` to load the payment form.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | Serves `index.html` |
| GET | `/api/health` | Health check |
| POST | `/get-access-token` | PMT token for Drop-In UI |
| POST | `/api/check-enrollment` | 3DS2 step 1 |
| POST | `/api/initiate-auth` | 3DS2 step 3 |
| POST | `/api/get-auth-result` | 3DS2 step 5 |
| POST | `/api/authorize-payment` | Final SALE charge |
| GET/POST | `/3ds/method-notification` | Silent iframe callback |
| GET/POST | `/3ds/challenge-notification` | ACS challenge callback |

## Files

- `server.js` — all route handlers and GP-API logic
- `auth.js` — access token generation and caching
- `index.html` — 3DS2-aware frontend (shared across all backends)
- `tests/unit/` — Jest unit tests

## Testing

```bash
npm test
```

67 tests covering token caching, utility functions, GP-API enum mappings, payload structure, and notification endpoint contracts.

## Notes

The module uses ES modules (`"type": "module"` in `package.json`). Jest requires `--experimental-vm-modules` to run, which the `npm test` script sets automatically.

Token state is held in memory at the module level in `auth.js`. A single process handles all requests, so there are no concurrency concerns. In a multi-process deployment you would want a shared cache.
