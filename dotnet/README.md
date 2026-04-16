# .NET backend

ASP.NET Core 9 minimal API implementation of the GP-API 3DS2 payment flow.

## Requirements

- .NET 9 SDK
- GP-API sandbox credentials

## Setup

```bash
cp .env.sample .env
# fill in GP_APP_ID, GP_APP_KEY, and the notification URLs
dotnet restore
dotnet run
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

- `Program.cs` — all route handlers and GP-API logic (top-level statements)
- `Utilities.cs` — `GpPayments.GpUtilities` static class (`ToMinorUnits`, `MapColorDepth`, `MapBool`, `TwoDigitYear`)
- `wwwroot/index.html` — 3DS2-aware frontend (shared across all backends)
- `Tests/UtilityTests.cs` — xUnit tests

## Testing

```bash
dotnet test Tests/Tests.csproj
```

52 tests covering utility functions, GP-API enum mappings, and payload structure.

## Notes

The base URL for GP-API does not include `/ucp` in this backend. It is added per-request as part of the path (e.g., `/ucp/authentications`). All four backends reach the same final URLs.

GZIP decompression is handled automatically via `HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }`.

Token state is held in process-level variables (`cachedToken`, `tokenExpiresAt`). This is fine for a single-instance deployment but would need a shared cache for horizontal scaling.

`TwoDigitYear` in `GpUtilities` is available for use but not currently called by any endpoint. It is kept because the unit tests cover it.
