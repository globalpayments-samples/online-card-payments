# PHP backend

Built-in PHP server implementation of the GP-API 3DS2 payment flow.

## Requirements

- PHP 8.0 or later (with `curl` and `json` extensions)
- Composer
- GP-API sandbox credentials

## Setup

```bash
cp .env.sample .env
# fill in GP_APP_ID, GP_APP_KEY, and the notification URLs
composer install
php -S localhost:8000 router.php
```

Open `http://localhost:8000` to load the payment form.

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

- `router.php` — routes requests to the appropriate handler
- `get-access-token.php` — PMT token endpoint
- `api/` — one file per 3DS2 and payment endpoint
- `src/GpApiClient.php` — HTTP client, token cache, and utility methods (`toMinorUnits`, `mapColorDepth`, `mapBool`)
- `index.html` — 3DS2-aware frontend (shared across all backends)
- `tests/unit/` — PHPUnit tests

## Testing

```bash
./vendor/bin/phpunit
```

61 tests covering the HTTP client utilities, GP-API enum mappings, payload structure, and notification endpoint contracts.

## Notes

Token caching uses a file at `/tmp/gpapi_token.json`. This works fine for the built-in PHP server (single process). In a production setup with multiple workers you would want a shared cache like Redis or Memcached.

The notification endpoints (`/3ds/method-notification`, `/3ds/challenge-notification`) receive form-encoded POST bodies from the issuer, not JSON. `router.php` routes these before any JSON parsing happens.
