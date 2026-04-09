/**
 * Global Payments 3DS2 Backend — .NET Minimal API
 *
 * Endpoints:
 *   POST /get-access-token      — tokenization token for Drop-In UI
 *   POST /api/check-enrollment  — Step 1: 3DS2 enrollment check
 *   POST /api/initiate-auth     — Step 3: initiate authentication with browser data
 *   POST /api/get-auth-result   — Step 5: retrieve final auth result
 *   POST /api/authorize-payment — Step 6: SALE with 3DS2 proof
 *
 * Uses payment_method.id (PMT token from Drop-In UI) instead of raw card numbers.
 * Token caching with 60-second refresh margin.
 */

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dotenv.net;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);
var app     = builder.Build();

// CORS
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Access-Control-Allow-Origin"]  = "*";
    ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    if (ctx.Request.Method == "OPTIONS") { ctx.Response.StatusCode = 204; return; }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// ─── Token cache (backend API calls) ─────────────────────────────────────────

string? cachedToken    = null;
long    tokenExpiresAt = 0; // Unix ms

string GpApiBase() => "production".Equals(Environment.GetEnvironmentVariable("GP_ENVIRONMENT"))
    ? "https://apis.globalpay.com"
    : "https://apis.sandbox.globalpay.com";

using var httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
    { BaseAddress = new Uri(GpApiBase()) };

async Task<string> GetApiTokenAsync()
{
    if (cachedToken != null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < tokenExpiresAt)
        return cachedToken;

    var appId  = Environment.GetEnvironmentVariable("GP_APP_ID")  ?? throw new Exception("GP_APP_ID not set");
    var appKey = Environment.GetEnvironmentVariable("GP_APP_KEY") ?? throw new Exception("GP_APP_KEY not set");
    var nonce  = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    var secret = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes($"{nonce}{appKey}"))).ToLower();

    var body = JsonSerializer.Serialize(new { app_id = appId, nonce, secret, grant_type = "client_credentials" });
    var req  = new HttpRequestMessage(HttpMethod.Post, "/ucp/accesstoken")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    req.Headers.Add("X-GP-Version", "2021-03-22");

    var resp = await httpClient.SendAsync(req);
    var json = await resp.Content.ReadAsStringAsync();
    var doc  = JsonDocument.Parse(json).RootElement;
    if (!resp.IsSuccessStatusCode) throw new Exception($"Token generation failed ({resp.StatusCode}): {json}");

    cachedToken    = doc.GetProperty("token").GetString();
    var expiresIn  = doc.GetProperty("seconds_to_expire").GetInt32();
    tokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (expiresIn - 60) * 1000L;
    return cachedToken!;
}

// ─── GP-API request helper ────────────────────────────────────────────────────

async Task<(JsonElement root, bool ok, int status)> GpRequest(string method, string path, object? body = null)
{
    var token = await GetApiTokenAsync();
    var req   = new HttpRequestMessage(new HttpMethod(method), path);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    req.Headers.Add("X-GP-Version", "2021-03-22");
    if (body != null)
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    var resp = await httpClient.SendAsync(req);
    var json = await resp.Content.ReadAsStringAsync();
    var root = JsonDocument.Parse(json.Length > 0 ? json : "{}").RootElement;
    return (root, resp.IsSuccessStatusCode, (int)resp.StatusCode);
}

static string ToMinorUnits(string amount) =>
    ((int)Math.Round(double.Parse(amount) * 100)).ToString();

static string TwoDigitYear(string year) =>
    year.Length > 2 ? year[^2..] : year;

IResult GpError(JsonElement root, int status)
{
    var msg = root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
        ? m.GetString() : $"GP-API error {status}";
    return Results.Json(new
    {
        success         = false,
        error           = msg,
        gp_error_code   = root.TryGetProperty("error", out var e2) && e2.TryGetProperty("code",   out var c) ? c.GetString() : null,
        gp_error_detail = root.TryGetProperty("error", out var e3) && e3.TryGetProperty("detail", out var d) ? d.GetString() : null,
        raw             = root
    }, statusCode: status);
}

// ─── Routes ───────────────────────────────────────────────────────────────────

/**
 * POST /get-access-token
 * Generates tokenization token for the Drop-In UI (PMT_POST_Create_Single permission).
 */
app.MapPost("/get-access-token", async () =>
{
    try
    {
        var appId  = Environment.GetEnvironmentVariable("GP_APP_ID")  ?? throw new Exception("GP_APP_ID not set");
        var appKey = Environment.GetEnvironmentVariable("GP_APP_KEY") ?? throw new Exception("GP_APP_KEY not set");

        var nonceBytes = new byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce  = Convert.ToHexString(nonceBytes).ToLower();
        var secret = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes($"{nonce}{appKey}"))).ToLower();

        var apiEndpoint = "production".Equals(Environment.GetEnvironmentVariable("GP_ENVIRONMENT"))
            ? "https://apis.globalpay.com/ucp/accesstoken"
            : "https://apis.sandbox.globalpay.com/ucp/accesstoken";

        using var tokenClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        tokenClient.DefaultRequestHeaders.Add("X-GP-Version", "2021-03-22");

        var payload = JsonSerializer.Serialize(new
        {
            app_id            = appId,
            nonce,
            secret,
            grant_type        = "client_credentials",
            seconds_to_expire = 600,
            permissions       = new[] { "PMT_POST_Create_Single" }
        });
        var response     = await tokenClient.PostAsync(apiEndpoint, new StringContent(payload, Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to generate access token: {responseBody}");

        var doc       = JsonDocument.Parse(responseBody).RootElement;
        var token     = doc.GetProperty("token").GetString();
        var expiresIn = doc.TryGetProperty("seconds_to_expire", out var exp) ? exp.GetInt32() : 600;

        return Results.Ok(new { success = true, token, expiresIn });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = "Error generating access token", error = ex.Message }, statusCode: 500);
    }
});

/**
 * POST /api/check-enrollment
 * Step 1 — Check if payment method is enrolled in 3DS2.
 */
app.MapPost("/api/check-enrollment", async (HttpRequest req) =>
{
    try
    {
        var root         = (await JsonDocument.ParseAsync(req.Body)).RootElement;
        var paymentToken = root.TryGetProperty("payment_token", out var pt) ? pt.GetString() : null;
        if (string.IsNullOrEmpty(paymentToken))
            return Results.BadRequest(new { success = false, error = "payment_token is required" });

        var accountName  = Environment.GetEnvironmentVariable("GP_ACCOUNT_NAME") ?? "transaction_processing";
        var accountId    = Environment.GetEnvironmentVariable("GP_ACCOUNT_ID");
        var merchantId   = Environment.GetEnvironmentVariable("GP_MERCHANT_ID");
        var challengeUrl = Environment.GetEnvironmentVariable("CHALLENGE_NOTIFICATION_URL");
        var methodUrl    = Environment.GetEnvironmentVariable("METHOD_NOTIFICATION_URL");

        var payload = new
        {
            account_name   = accountName,
            account_id     = accountId,
            merchant_id    = merchantId,
            channel        = "CNP",
            country        = "GB",
            amount         = "1000",
            currency       = "GBP",
            reference      = Guid.NewGuid().ToString(),
            payment_method = new { entry_mode = "ECOM", id = paymentToken },
            three_ds       = new { source = "BROWSER", preference = "NO_PREFERENCE", message_version = "2.2.0" },
            notifications  = new { challenge_return_url = challengeUrl, three_ds_method_return_url = methodUrl }
        };

        var (r, ok, status) = await GpRequest("POST", "/ucp/authentications", payload);
        if (!ok) return GpError(r, status);

        string? mUrl = null, mData = null;
        if (r.TryGetProperty("three_ds", out var tds) &&
            tds.TryGetProperty("method_url", out var mu) &&
            mu.ValueKind != JsonValueKind.Null)
        {
            mUrl  = mu.GetString();
            var mJson = JsonSerializer.Serialize(new
            {
                threeDSServerTransID  = r.GetProperty("id").GetString(),
                methodNotificationURL = methodUrl
            });
            mData = Convert.ToBase64String(Encoding.UTF8.GetBytes(mJson));
        }

        var tds2 = r.TryGetProperty("three_ds", out var t2) ? t2 : default;
        string? Tds(string k) => tds2.ValueKind != JsonValueKind.Undefined && tds2.TryGetProperty(k, out var v) ? v.GetString() : null;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                server_trans_id  = r.GetProperty("id").GetString(),
                server_trans_ref = Tds("server_trans_ref"),
                enrolled         = Tds("enrolled_status"),
                message_version  = Tds("message_version"),
                method_url       = mUrl,
                method_data      = mData
            },
            raw = r
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

/**
 * POST /api/initiate-auth
 * Step 3 — Initiate authentication with browser data.
 */
app.MapPost("/api/initiate-auth", async (HttpRequest req) =>
{
    try
    {
        var root = (await JsonDocument.ParseAsync(req.Body)).RootElement;

        string Get(string k, string def = "") => root.TryGetProperty(k, out var v) ? v.GetString() ?? def : def;

        var paymentToken        = Get("payment_token");
        var serverTransIdRaw    = Get("server_trans_id");
        var serverTransId       = serverTransIdRaw.StartsWith("AUT_") ? serverTransIdRaw[4..] : serverTransIdRaw;
        var messageVersion      = Get("message_version", "2.1.0");
        var methodUrlCompletion = Get("method_url_completion", "UNAVAILABLE");

        if (string.IsNullOrEmpty(paymentToken))
            return Results.BadRequest(new { success = false, error = "payment_token is required" });

        var order    = root.TryGetProperty("order", out var o) ? o : default;
        var amount   = order.ValueKind != JsonValueKind.Undefined && order.TryGetProperty("amount",   out var a) ? a.GetString()! : "10.00";
        var currency = order.ValueKind != JsonValueKind.Undefined && order.TryGetProperty("currency", out var c) ? c.GetString()! : "GBP";
        var bd       = root.TryGetProperty("browser_data", out var bde) ? bde : default;

        string Bd(string k, string def) => bd.ValueKind != JsonValueKind.Undefined && bd.TryGetProperty(k, out var v) ? v.ToString() : def;

        var accountName  = Environment.GetEnvironmentVariable("GP_ACCOUNT_NAME") ?? "transaction_processing";
        var accountId    = Environment.GetEnvironmentVariable("GP_ACCOUNT_ID");
        var merchantId   = Environment.GetEnvironmentVariable("GP_MERCHANT_ID");
        var challengeUrl = Environment.GetEnvironmentVariable("CHALLENGE_NOTIFICATION_URL");
        var methodUrl    = Environment.GetEnvironmentVariable("METHOD_NOTIFICATION_URL");

        var payload = new
        {
            account_name   = accountName,
            account_id     = accountId,
            merchant_id    = merchantId,
            channel        = "CNP",
            country        = "GB",
            amount         = ToMinorUnits(amount),
            currency,
            reference      = Guid.NewGuid().ToString(),
            payment_method = new { entry_mode = "ECOM", id = paymentToken },
            three_ds = new
            {
                source                = "BROWSER",
                preference            = "NO_PREFERENCE",
                message_version       = messageVersion,
                server_trans_ref      = serverTransId,
                method_url_completion = methodUrlCompletion
            },
            order = new
            {
                amount            = ToMinorUnits(amount),
                currency,
                reference         = Guid.NewGuid().ToString(),
                address_indicator = false,
                date_time_created = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
            },
            payer = new
            {
                email           = "test@example.com",
                billing_address = new { line1 = "1 Test Street", city = "London", postal_code = "SW1A 1AA", country = "826" }
            },
            browser_data = new
            {
                accept_header         = Bd("accept_header",         "text/html,application/xhtml+xml"),
                color_depth           = Bd("color_depth",           "24"),
                ip                    = Bd("ip",                    "123.123.123.123"),
                java_enabled          = Bd("java_enabled",          "false"),
                javascript_enabled    = Bd("javascript_enabled",    "true"),
                language              = Bd("language",              "en-GB"),
                screen_height         = Bd("screen_height",         "1080"),
                screen_width          = Bd("screen_width",          "1920"),
                challenge_window_size = Bd("challenge_window_size", "FULL_SCREEN"),
                timezone              = Bd("timezone",              "0"),
                user_agent            = Bd("user_agent",            "Mozilla/5.0")
            },
            notifications = new { challenge_return_url = challengeUrl, three_ds_method_return_url = methodUrl }
        };

        var (r, ok, status) = await GpRequest("POST", "/ucp/authentications", payload);
        if (!ok) return GpError(r, status);

        var tds = r.TryGetProperty("three_ds", out var t) ? t : default;
        string? Tds(string k) => tds.ValueKind != JsonValueKind.Undefined && tds.TryGetProperty(k, out var v) ? v.GetString() : null;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                server_trans_id      = r.GetProperty("id").GetString(),
                status               = r.TryGetProperty("status", out var s) ? s.GetString() : null,
                acs_reference_number = Tds("acs_reference_number"),
                acs_trans_id         = Tds("acs_trans_id"),
                acs_signed_content   = Tds("acs_signed_content"),
                acs_challenge_url    = Tds("acs_challenge_url") ?? Tds("challenge_value")
            },
            raw = r
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

/**
 * POST /api/get-auth-result
 * Step 5 — Retrieve final authentication result.
 */
app.MapPost("/api/get-auth-result", async (HttpRequest req) =>
{
    try
    {
        var root             = (await JsonDocument.ParseAsync(req.Body)).RootElement;
        var serverTransIdRaw = root.GetProperty("server_trans_id").GetString();
        var serverTransId    = serverTransIdRaw?.StartsWith("AUT_") == true ? serverTransIdRaw[4..] : serverTransIdRaw;

        if (string.IsNullOrEmpty(serverTransId))
            return Results.BadRequest(new { success = false, error = "server_trans_id is required" });

        var (r, ok, status) = await GpRequest("GET", $"/ucp/authentications/{serverTransId}");
        if (!ok) return GpError(r, status);

        var tds = r.TryGetProperty("three_ds", out var t) ? t : default;
        string? Tds(string k) => tds.ValueKind != JsonValueKind.Undefined && tds.TryGetProperty(k, out var v) ? v.GetString() : null;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                status               = r.TryGetProperty("status", out var s) ? s.GetString() : null,
                eci                  = Tds("eci"),
                authentication_value = Tds("authentication_value"),
                ds_trans_ref         = Tds("ds_trans_ref"),
                message_version      = Tds("message_version"),
                server_trans_ref     = Tds("server_trans_ref") ?? r.GetProperty("id").GetString()
            },
            raw = r
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

/**
 * POST /api/authorize-payment
 * Step 6 — SALE transaction with 3DS2 proof using payment token.
 */
app.MapPost("/api/authorize-payment", async (HttpRequest req) =>
{
    try
    {
        var root = (await JsonDocument.ParseAsync(req.Body)).RootElement;

        string Get(string k, string def = "") => root.TryGetProperty(k, out var v) ? v.GetString() ?? def : def;
        var paymentToken = Get("payment_token");
        var amount       = Get("amount", "10.00");
        var currency     = Get("currency", "GBP");

        if (string.IsNullOrEmpty(paymentToken))
            return Results.BadRequest(new { success = false, error = "payment_token is required" });

        var tds3 = root.TryGetProperty("three_ds", out var t3) ? t3 : default;
        string? Td(string k) => tds3.ValueKind != JsonValueKind.Undefined && tds3.TryGetProperty(k, out var v) ? v.GetString() : null;

        var accountName = Environment.GetEnvironmentVariable("GP_ACCOUNT_NAME") ?? "transaction_processing";
        var accountId   = Environment.GetEnvironmentVariable("GP_ACCOUNT_ID");
        var merchantId  = Environment.GetEnvironmentVariable("GP_MERCHANT_ID");

        var payload = new
        {
            account_name   = accountName,
            account_id     = accountId,
            merchant_id    = merchantId,
            channel        = "CNP",
            type           = "SALE",
            amount         = ToMinorUnits(amount),
            currency,
            reference      = Guid.NewGuid().ToString(),
            country        = "GB",
            payment_method = new { entry_mode = "ECOM", id = paymentToken },
            three_ds = new
            {
                source               = "BROWSER",
                authentication_value = Td("authentication_value"),
                server_trans_ref     = Td("server_trans_ref"),
                ds_trans_ref         = Td("ds_trans_ref"),
                eci                  = Td("eci"),
                message_version      = Td("message_version") ?? "2.2.0"
            }
        };

        var (r, ok, status) = await GpRequest("POST", "/ucp/transactions", payload);
        if (!ok) return GpError(r, status);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                transaction_id = r.GetProperty("id").GetString(),
                status         = r.TryGetProperty("status",   out var s)  ? s.GetString()  : null,
                result_code    = r.TryGetProperty("action",   out var ac) && ac.TryGetProperty("result_code", out var rc) ? rc.GetString() : null,
                amount         = r.TryGetProperty("amount",   out var am) ? am.GetString()  : null,
                currency       = r.TryGetProperty("currency", out var cu) ? cu.GetString()  : null
            },
            raw = r
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

// ─── Start ────────────────────────────────────────────────────────────────────

var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
app.Urls.Add($"http://0.0.0.0:{port}");
Console.WriteLine($"Server running at http://localhost:{port}");
Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("GP_ENVIRONMENT") ?? "sandbox"}");
app.Run();
