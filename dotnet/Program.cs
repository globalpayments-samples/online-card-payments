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

static string ToMinorUnits(string amount) => GpPayments.GpUtilities.ToMinorUnits(amount);

static string MapColorDepth(string v) => GpPayments.GpUtilities.MapColorDepth(v);
static string MapBool(string v)       => GpPayments.GpUtilities.MapBool(v);
// The nonce goes straight into an inline script, so only let through what the
// page generates (a UUID). Anything else comes back as undefined.
static string NotificationNonce(string nonce) =>
    System.Text.RegularExpressions.Regex.IsMatch(nonce ?? "", "^[A-Za-z0-9-]{1,64}$") ? $"'{nonce}'" : "undefined";

// GP-API errors come back as { error_code, detailed_error_description }
IResult GpError(JsonElement root, int status)
{
    string? Field(string k) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    var code   = Field("error_code");
    var detail = Field("detailed_error_description");
    return Results.Json(new
    {
        success         = false,
        error           = !string.IsNullOrEmpty(detail) ? detail : !string.IsNullOrEmpty(code) ? code : $"GP-API error {status}",
        gp_error_code   = code,
        gp_error_detail = detail,
        raw             = root
    }, statusCode: status);
}

// ─── Routes ───────────────────────────────────────────────────────────────────

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", backend = "dotnet", version = "1.0.0" }));

app.MapMethods("/3ds/challenge-notification", new[] { "GET", "POST" }, (HttpRequest req) =>
{
    var nonce = NotificationNonce(req.Query["nonce"].ToString());
    return Results.Content(
        $"<!DOCTYPE html><html><body><script>var msg={{type:'authResult',nonce:{nonce}}};try{{window.parent.postMessage(msg,'*');}}catch(_){{}}try{{window.top.postMessage(msg,'*');}}catch(_){{}}</script></body></html>",
        "text/html");
});

app.MapMethods("/3ds/method-notification", new[] { "GET", "POST" }, (HttpRequest req) =>
{
    var nonce = NotificationNonce(req.Query["nonce"].ToString());
    return Results.Content(
        $"<!DOCTYPE html><html><body><script>var msg={{type:'methodComplete',nonce:{nonce}}};try{{window.parent.postMessage(msg,'*');}}catch(_){{}}try{{window.top.postMessage(msg,'*');}}catch(_){{}}</script></body></html>",
        "text/html");
});

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

        var gpEnv = Environment.GetEnvironmentVariable("GP_ENVIRONMENT") ?? "sandbox";
        return Results.Ok(new { success = true, token, expiresIn, environment = gpEnv });
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
        var flowNonce    = root.TryGetProperty("flow_nonce", out var fn) ? fn.GetString() : null;
        var nonceQuery   = !string.IsNullOrEmpty(flowNonce) ? $"?nonce={Uri.EscapeDataString(flowNonce)}" : "";
        var challengeUrl = Environment.GetEnvironmentVariable("CHALLENGE_NOTIFICATION_URL") + nonceQuery;
        var methodUrl    = Environment.GetEnvironmentVariable("METHOD_NOTIFICATION_URL")    + nonceQuery;

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
            if (tds.TryGetProperty("method_data", out var mdProp) &&
                mdProp.TryGetProperty("encoded_method_data", out var emd) &&
                emd.ValueKind != JsonValueKind.Null)
            {
                mData = emd.GetString();
            }
            else
            {
                var mJson = JsonSerializer.Serialize(new
                {
                    threeDSServerTransID  = r.GetProperty("id").GetString(),
                    methodNotificationURL = methodUrl
                });
                mData = Convert.ToBase64String(Encoding.UTF8.GetBytes(mJson));
            }
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

        var paymentToken              = Get("payment_token");
        var serverTransId             = Get("server_trans_id");
        var messageVersion            = Get("message_version", "2.1.0");
        var methodUrlCompletionStatus = Get("method_url_completion_status", "NO");

        if (string.IsNullOrEmpty(paymentToken))
            return Results.BadRequest(new { success = false, error = "payment_token is required" });
        if (string.IsNullOrEmpty(serverTransId))
            return Results.BadRequest(new { success = false, error = "server_trans_id is required" });

        var order    = root.TryGetProperty("order", out var o) ? o : default;
        var amount   = order.ValueKind != JsonValueKind.Undefined && order.TryGetProperty("amount",   out var a) ? a.GetString()! : "10.00";
        var currency = order.ValueKind != JsonValueKind.Undefined && order.TryGetProperty("currency", out var c) ? c.GetString()! : "GBP";
        var bd       = root.TryGetProperty("browser_data", out var bde) ? bde : default;

        string Bd(string k, string def) => bd.ValueKind != JsonValueKind.Undefined && bd.TryGetProperty(k, out var v) ? v.ToString() : def;

        var accountName  = Environment.GetEnvironmentVariable("GP_ACCOUNT_NAME") ?? "transaction_processing";
        var accountId    = Environment.GetEnvironmentVariable("GP_ACCOUNT_ID");
        var merchantId   = Environment.GetEnvironmentVariable("GP_MERCHANT_ID");
        var iaNonce      = root.TryGetProperty("flow_nonce", out var iafn) ? iafn.GetString() : null;
        var iaNonceQuery = !string.IsNullOrEmpty(iaNonce) ? $"?nonce={Uri.EscapeDataString(iaNonce)}" : "";
        var challengeUrl = Environment.GetEnvironmentVariable("CHALLENGE_NOTIFICATION_URL") + iaNonceQuery;
        var methodUrl    = Environment.GetEnvironmentVariable("METHOD_NOTIFICATION_URL")    + iaNonceQuery;

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
            payment_method               = new { entry_mode = "ECOM", id = paymentToken },
            method_url_completion_status = methodUrlCompletionStatus,
            three_ds = new
            {
                source           = "BROWSER",
                preference       = "NO_PREFERENCE",
                message_version  = messageVersion,
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
                color_depth           = MapColorDepth(Bd("color_depth",        "24")),
                ip                    = Bd("ip",                    "123.123.123.123"),
                java_enabled          = MapBool(Bd("java_enabled",             "false")),
                javascript_enabled    = MapBool(Bd("javascript_enabled",       "true")),
                language              = Bd("language",              "en-GB"),
                screen_height         = Bd("screen_height",         "1080"),
                screen_width          = Bd("screen_width",          "1920"),
                challenge_window_size = Bd("challenge_window_size", "FULL_SCREEN"),
                timezone              = Bd("timezone",              "0"),
                user_agent            = Bd("user_agent",            "Mozilla/5.0")
            },
            notifications = new { challenge_return_url = challengeUrl, three_ds_method_return_url = methodUrl }
        };

        var authId = GpPayments.GpUtilities.AuthenticationId(serverTransId);
        var (r, ok, status) = await GpRequest("POST", $"/ucp/authentications/{Uri.EscapeDataString(authId)}/initiate", payload);
        if (!ok) return GpError(r, status);

        var tds = r.TryGetProperty("three_ds", out var t) ? t : default;
        string? Tds(string k) => tds.ValueKind != JsonValueKind.Undefined && tds.TryGetProperty(k, out var v) ? v.GetString() : null;
        string? NonEmpty(string? v) => string.IsNullOrEmpty(v) ? null : v;

        // For a browser challenge GP-API returns the ACS URL and the encoded CReq
        // the page has to post to it
        return Results.Ok(new
        {
            success = true,
            data = new
            {
                server_trans_id      = r.GetProperty("id").GetString(),
                status               = r.TryGetProperty("status", out var s) ? s.GetString() : null,
                acs_reference_number = Tds("acs_reference_number"),
                acs_trans_id         = Tds("acs_trans_ref"),
                acs_signed_content   = NonEmpty(Tds("challenge_value")),
                acs_challenge_url    = NonEmpty(Tds("acs_challenge_request_url"))
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
        var serverTransId = root.TryGetProperty("server_trans_id", out var stidProp) ? stidProp.GetString() : null;

        if (string.IsNullOrEmpty(serverTransId))
            return Results.BadRequest(new { success = false, error = "server_trans_id is required" });

        var authId = GpPayments.GpUtilities.AuthenticationId(serverTransId);
        var (r, ok, status) = await GpRequest("POST", $"/ucp/authentications/{Uri.EscapeDataString(authId)}/result", new { });
        if (!ok) return GpError(r, status);

        var tds = r.TryGetProperty("three_ds", out var t) ? t : default;
        string? Tds(string k) => tds.ValueKind != JsonValueKind.Undefined && tds.TryGetProperty(k, out var v) ? v.GetString() : null;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                server_trans_id      = r.TryGetProperty("id", out var id) ? id.GetString() : null,
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
        var authRef     = Td("server_trans_id") is { Length: > 0 } sid ? sid : Td("server_trans_ref");

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
            // GP-API only applies 3DS to a sale through payment_method.authentication.
            // It takes the ECI, authentication value and DS reference from the
            // authentication it finds by id.
            payment_method = string.IsNullOrEmpty(authRef)
                ? (object)new { entry_mode = "ECOM", id = paymentToken }
                : new { entry_mode = "ECOM", id = paymentToken, authentication = new { id = GpPayments.GpUtilities.AuthenticationId(authRef) } }
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
