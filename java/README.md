# Java backend

Jakarta Servlet 5 / Tomcat 10 implementation of the GP-API 3DS2 payment flow.

## Requirements

- Java 11 or later
- Maven 3.6 or later
- GP-API sandbox credentials

## Setup

```bash
cp .env.sample .env
# fill in GP_APP_ID, GP_APP_KEY, and the notification URLs
mvn clean package
mvn cargo:run
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

- `src/main/java/.../GpApi3dsServlet.java` — all 3DS2 and payment endpoints
- `src/main/java/.../ProcessPaymentServlet.java` — access token endpoint
- `src/main/webapp/index.html` — 3DS2-aware frontend (shared across all backends)
- `src/test/java/.../GpApi3dsServletTest.java` — JUnit 5 tests

## Testing

```bash
mvn test
```

42 tests covering utility functions, GP-API enum mappings, payload structure, and CORS header values. Private static methods are tested via reflection.

## Notes

Token caching uses `static volatile` fields on `ProcessPaymentServlet` with a `ReentrantLock` for thread safety.

GP-API responses are GZIP-compressed. The servlet decodes them manually using `java.util.zip.GZIPInputStream`.

The notification endpoints must be routed before the request body is read as JSON. The ACS POSTs form-encoded data (`cres=...`), not a JSON body, so reading the body as JSON first would throw an exception. `doPost` checks the path before calling `req.getReader()`.
