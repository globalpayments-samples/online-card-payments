package com.globalpayments.example;

import io.github.cdimascio.dotenv.Dotenv;
import jakarta.servlet.ServletException;
import jakarta.servlet.annotation.WebServlet;
import jakarta.servlet.http.HttpServlet;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.Base64;
import java.util.UUID;
import java.util.concurrent.locks.ReentrantLock;
import java.util.stream.Collectors;
import java.util.zip.GZIPInputStream;

/**
 * Global Payments 3DS2 Backend — Java Servlet
 *
 * Endpoints:
 *   GET  /api/health             — health check
 *   POST /api/check-enrollment   — Step 1: enrollment check
 *   POST /api/initiate-auth      — Step 3: initiate authentication
 *   POST /api/get-auth-result    — Step 5: retrieve auth result
 *   POST /api/authorize-payment  — Step 6: SALE with 3DS2 proof
 *
 * Uses payment_method.id (PMT token from Drop-In UI) instead of raw card numbers.
 * Token caching with thread-safe refresh.
 */
@WebServlet(urlPatterns = {
    "/api/health",
    "/api/check-enrollment",
    "/api/initiate-auth",
    "/api/get-auth-result",
    "/api/authorize-payment"
})
public class GpApi3dsServlet extends HttpServlet {

    private static final long serialVersionUID = 1L;
    private static final String GP_VERSION = "2021-03-22";

    private static final HttpClient HTTP = HttpClient.newHttpClient();

    // Token cache
    private static final ReentrantLock TOKEN_LOCK = new ReentrantLock();
    private static volatile String cachedToken    = null;
    private static volatile long   tokenExpiresAt = 0; // epoch ms

    private Dotenv dotenv;

    @Override
    public void init() throws ServletException {
        dotenv = Dotenv.configure().ignoreIfMissing().load();
    }

    private String env(String key, String def) {
        String v = dotenv.get(key, null);
        if (v != null && !v.isEmpty()) return v;
        v = System.getenv(key);
        return (v != null && !v.isEmpty()) ? v : def;
    }

    private String baseUrl() {
        return "production".equals(env("GP_ENVIRONMENT", "sandbox"))
            ? "https://apis.globalpay.com/ucp"
            : "https://apis.sandbox.globalpay.com/ucp";
    }

    // ── CORS ──────────────────────────────────────────────────────────────────

    private static void addCors(HttpServletResponse res) {
        res.setHeader("Access-Control-Allow-Origin",  "*");
        res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.setHeader("Access-Control-Allow-Headers", "Content-Type");
    }

    @Override
    protected void doOptions(HttpServletRequest req, HttpServletResponse res)
            throws ServletException, IOException {
        addCors(res);
        res.setStatus(204);
    }

    // ── Routing ───────────────────────────────────────────────────────────────

    @Override
    protected void doGet(HttpServletRequest req, HttpServletResponse res)
            throws ServletException, IOException {
        addCors(res);
        res.setContentType("application/json");
        if ("/api/health".equals(req.getServletPath())) {
            res.getWriter().write("{\"status\":\"ok\",\"backend\":\"java\",\"version\":\"1.0.0\"}");
        } else {
            res.setStatus(404);
            res.getWriter().write("{\"error\":\"Not found\"}");
        }
    }

    @Override
    protected void doPost(HttpServletRequest req, HttpServletResponse res)
            throws ServletException, IOException {
        addCors(res);
        res.setContentType("application/json");

        String body  = req.getReader().lines().collect(Collectors.joining());
        JSONObject input = new JSONObject(body.isEmpty() ? "{}" : body);

        try {
            switch (req.getServletPath()) {
                case "/api/check-enrollment"  -> handleCheckEnrollment(input, res);
                case "/api/initiate-auth"     -> handleInitiateAuth(input, res);
                case "/api/get-auth-result"   -> handleGetAuthResult(input, res);
                case "/api/authorize-payment" -> handleAuthorizePayment(input, res);
                default -> { res.setStatus(404); res.getWriter().write("{\"error\":\"Not found\"}"); }
            }
        } catch (Exception e) {
            writeError(res, e, 500);
        }
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void handleCheckEnrollment(JSONObject in, HttpServletResponse res) throws Exception {
        String paymentToken = in.optString("payment_token", "");
        if (paymentToken.isEmpty()) {
            res.setStatus(400);
            res.getWriter().write("{\"success\":false,\"error\":\"payment_token is required\"}");
            return;
        }

        JSONObject payload = new JSONObject();
        payload.put("account_name", env("GP_ACCOUNT_NAME", "transaction_processing"));
        String accountId = env("GP_ACCOUNT_ID", "");
        if (!accountId.isEmpty()) payload.put("account_id", accountId);
        String merchantId = env("GP_MERCHANT_ID", "");
        if (!merchantId.isEmpty()) payload.put("merchant_id", merchantId);
        payload.put("channel",   "CNP");
        payload.put("country",   "GB");
        payload.put("amount",    "1000");
        payload.put("currency",  "GBP");
        payload.put("reference", UUID.randomUUID().toString());
        payload.put("payment_method", new JSONObject()
            .put("entry_mode", "ECOM")
            .put("id", paymentToken));
        payload.put("three_ds", new JSONObject()
            .put("source",          "BROWSER")
            .put("preference",      "NO_PREFERENCE")
            .put("message_version", "2.2.0"));
        payload.put("notifications", new JSONObject()
            .put("challenge_return_url",       env("CHALLENGE_NOTIFICATION_URL", ""))
            .put("three_ds_method_return_url", env("METHOD_NOTIFICATION_URL",    "")));

        JSONObject raw = gpPost("/authentications", payload);

        String methodUrl  = null;
        String methodData = null;
        JSONObject tds = raw.optJSONObject("three_ds");
        if (tds != null && tds.has("method_url") && !tds.isNull("method_url")) {
            methodUrl = tds.getString("method_url");
            String mJson = new JSONObject()
                .put("threeDSServerTransID",  raw.getString("id"))
                .put("methodNotificationURL", env("METHOD_NOTIFICATION_URL", ""))
                .toString();
            methodData = Base64.getEncoder().encodeToString(mJson.getBytes(StandardCharsets.UTF_8));
        }

        JSONObject data = new JSONObject();
        data.put("server_trans_id",  raw.getString("id"));
        data.put("server_trans_ref", tds != null ? tds.optString("server_trans_ref", null) : null);
        data.put("enrolled",         tds != null ? tds.optString("enrolled_status",  null) : null);
        data.put("message_version",  tds != null ? tds.optString("message_version",  null) : null);
        data.put("method_url",       methodUrl);
        data.put("method_data",      methodData);

        writeSuccess(res, data, raw);
    }

    private void handleInitiateAuth(JSONObject in, HttpServletResponse res) throws Exception {
        String paymentToken        = in.optString("payment_token", "");
        String serverTransIdRaw    = in.optString("server_trans_id", "");
        String serverTransId       = serverTransIdRaw.startsWith("AUT_") ? serverTransIdRaw.substring(4) : serverTransIdRaw;
        String messageVersion      = in.optString("message_version",      "2.1.0");
        String methodUrlCompletion = in.optString("method_url_completion", "UNAVAILABLE");

        if (paymentToken.isEmpty()) {
            res.setStatus(400);
            res.getWriter().write("{\"success\":false,\"error\":\"payment_token is required\"}");
            return;
        }

        JSONObject order    = in.optJSONObject("order");
        String amount       = order != null ? order.optString("amount",   "10.00") : "10.00";
        String currency     = order != null ? order.optString("currency", "GBP")   : "GBP";
        JSONObject bd       = in.optJSONObject("browser_data");

        JSONObject payload = new JSONObject();
        payload.put("account_name", env("GP_ACCOUNT_NAME", "transaction_processing"));
        String accountId = env("GP_ACCOUNT_ID", "");
        if (!accountId.isEmpty()) payload.put("account_id", accountId);
        String merchantId = env("GP_MERCHANT_ID", "");
        if (!merchantId.isEmpty()) payload.put("merchant_id", merchantId);
        payload.put("channel",   "CNP");
        payload.put("country",   "GB");
        payload.put("amount",    toMinorUnits(amount));
        payload.put("currency",  currency);
        payload.put("reference", UUID.randomUUID().toString());
        payload.put("payment_method", new JSONObject()
            .put("entry_mode", "ECOM")
            .put("id", paymentToken));
        payload.put("three_ds", new JSONObject()
            .put("source",                "BROWSER")
            .put("preference",            "NO_PREFERENCE")
            .put("message_version",       messageVersion)
            .put("server_trans_ref",      serverTransId)
            .put("method_url_completion", methodUrlCompletion));
        payload.put("order", new JSONObject()
            .put("amount",            toMinorUnits(amount))
            .put("currency",          currency)
            .put("reference",         UUID.randomUUID().toString())
            .put("address_indicator", false)
            .put("date_time_created", Instant.now().toString()));
        payload.put("payer", new JSONObject()
            .put("email", "test@example.com")
            .put("billing_address", new JSONObject()
                .put("line1",       "1 Test Street")
                .put("city",        "London")
                .put("postal_code", "SW1A 1AA")
                .put("country",     "826")));
        payload.put("browser_data", new JSONObject()
            .put("accept_header",         bdField(bd, "accept_header",         "text/html,application/xhtml+xml"))
            .put("color_depth",           bdField(bd, "color_depth",           "24"))
            .put("ip",                    bdField(bd, "ip",                    "123.123.123.123"))
            .put("java_enabled",          bdField(bd, "java_enabled",          "false"))
            .put("javascript_enabled",    bdField(bd, "javascript_enabled",    "true"))
            .put("language",              bdField(bd, "language",              "en-GB"))
            .put("screen_height",         bdField(bd, "screen_height",         "1080"))
            .put("screen_width",          bdField(bd, "screen_width",          "1920"))
            .put("challenge_window_size", bdField(bd, "challenge_window_size", "FULL_SCREEN"))
            .put("timezone",              bdField(bd, "timezone",              "0"))
            .put("user_agent",            bdField(bd, "user_agent",            "Mozilla/5.0")));
        payload.put("notifications", new JSONObject()
            .put("challenge_return_url",       env("CHALLENGE_NOTIFICATION_URL", ""))
            .put("three_ds_method_return_url", env("METHOD_NOTIFICATION_URL",    "")));

        JSONObject raw = gpPost("/authentications", payload);
        JSONObject tds = raw.optJSONObject("three_ds");

        JSONObject data = new JSONObject();
        data.put("server_trans_id",      raw.getString("id"));
        data.put("status",               raw.optString("status", null));
        data.put("acs_reference_number", tds != null ? tds.optString("acs_reference_number", null) : null);
        data.put("acs_trans_id",         tds != null ? tds.optString("acs_trans_id",         null) : null);
        data.put("acs_signed_content",   tds != null ? tds.optString("acs_signed_content",   null) : null);
        String challengeUrl = tds != null
            ? (tds.has("acs_challenge_url") ? tds.optString("acs_challenge_url", null) : tds.optString("challenge_value", null))
            : null;
        data.put("acs_challenge_url", challengeUrl);

        writeSuccess(res, data, raw);
    }

    private void handleGetAuthResult(JSONObject in, HttpServletResponse res) throws Exception {
        String serverTransIdRaw = in.optString("server_trans_id", "");
        String serverTransId    = serverTransIdRaw.replaceFirst("^AUT_", "");
        if (serverTransId.isEmpty()) {
            res.setStatus(400);
            res.getWriter().write("{\"success\":false,\"error\":\"server_trans_id is required\"}");
            return;
        }

        JSONObject raw = gpGet("/authentications/" + serverTransId);
        JSONObject tds = raw.optJSONObject("three_ds");

        JSONObject data = new JSONObject();
        data.put("status",               raw.optString("status", null));
        data.put("eci",                  tds != null ? tds.optString("eci",                  null) : null);
        data.put("authentication_value", tds != null ? tds.optString("authentication_value", null) : null);
        data.put("ds_trans_ref",         tds != null ? tds.optString("ds_trans_ref",         null) : null);
        data.put("message_version",      tds != null ? tds.optString("message_version",      null) : null);
        data.put("server_trans_ref",     tds != null && tds.has("server_trans_ref")
            ? tds.getString("server_trans_ref") : raw.getString("id"));

        writeSuccess(res, data, raw);
    }

    private void handleAuthorizePayment(JSONObject in, HttpServletResponse res) throws Exception {
        String paymentToken = in.optString("payment_token", "");
        String amount       = in.optString("amount",   "10.00");
        String currency     = in.optString("currency", "GBP");
        JSONObject tds3     = in.optJSONObject("three_ds");

        if (paymentToken.isEmpty()) {
            res.setStatus(400);
            res.getWriter().write("{\"success\":false,\"error\":\"payment_token is required\"}");
            return;
        }

        JSONObject payload = new JSONObject();
        payload.put("account_name", env("GP_ACCOUNT_NAME", "transaction_processing"));
        String accountId = env("GP_ACCOUNT_ID", "");
        if (!accountId.isEmpty()) payload.put("account_id", accountId);
        String merchantId = env("GP_MERCHANT_ID", "");
        if (!merchantId.isEmpty()) payload.put("merchant_id", merchantId);
        payload.put("channel",   "CNP");
        payload.put("type",      "SALE");
        payload.put("amount",    toMinorUnits(amount));
        payload.put("currency",  currency);
        payload.put("reference", UUID.randomUUID().toString());
        payload.put("country",   "GB");
        payload.put("payment_method", new JSONObject()
            .put("entry_mode", "ECOM")
            .put("id", paymentToken));
        payload.put("three_ds", new JSONObject()
            .put("source",               "BROWSER")
            .put("authentication_value", tds3 != null ? tds3.optString("authentication_value", null) : null)
            .put("server_trans_ref",     tds3 != null ? tds3.optString("server_trans_ref",     null) : null)
            .put("ds_trans_ref",         tds3 != null ? tds3.optString("ds_trans_ref",         null) : null)
            .put("eci",                  tds3 != null ? tds3.optString("eci",                  null) : null)
            .put("message_version",      tds3 != null ? tds3.optString("message_version", "2.2.0") : "2.2.0"));

        JSONObject raw = gpPost("/transactions", payload);

        JSONObject data = new JSONObject();
        data.put("transaction_id", raw.getString("id"));
        data.put("status",         raw.optString("status", null));
        JSONObject action = raw.optJSONObject("action");
        data.put("result_code",    action != null ? action.optString("result_code", null) : null);
        data.put("amount",         raw.optString("amount",   null));
        data.put("currency",       raw.optString("currency", null));

        writeSuccess(res, data, raw);
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private JSONObject gpPost(String path, JSONObject body) throws Exception {
        return gpRequest("POST", path, body);
    }

    private JSONObject gpGet(String path) throws Exception {
        return gpRequest("GET", path, null);
    }

    private JSONObject gpRequest(String method, String path, JSONObject body) throws Exception {
        String token = getAccessToken();
        HttpRequest.Builder rb = HttpRequest.newBuilder()
            .uri(URI.create(baseUrl() + path))
            .header("Content-Type",  "application/json")
            .header("X-GP-Version",  GP_VERSION)
            .header("Authorization", "Bearer " + token);

        if ("GET".equals(method)) {
            rb.GET();
        } else {
            String payload = body != null ? body.toString() : "{}";
            rb.method(method, HttpRequest.BodyPublishers.ofString(payload));
        }

        HttpResponse<byte[]> resp = HTTP.send(rb.build(), HttpResponse.BodyHandlers.ofByteArray());
        String bodyStr = decodeBody(resp);
        JSONObject result = new JSONObject(bodyStr.isEmpty() ? "{}" : bodyStr);

        if (resp.statusCode() < 200 || resp.statusCode() >= 300) {
            String msg = result.optJSONObject("error") != null
                ? result.getJSONObject("error").optString("message", "GP-API error " + resp.statusCode())
                : "GP-API error " + resp.statusCode();
            RuntimeException ex = new RuntimeException(msg);
            ex.addSuppressed(new RuntimeException("STATUS:" + resp.statusCode()));
            ex.addSuppressed(new RuntimeException("RAW:" + bodyStr));
            throw ex;
        }
        return result;
    }

    // ── Token management ──────────────────────────────────────────────────────

    private String getAccessToken() throws Exception {
        if (cachedToken != null && System.currentTimeMillis() < tokenExpiresAt) return cachedToken;
        TOKEN_LOCK.lock();
        try {
            if (cachedToken != null && System.currentTimeMillis() < tokenExpiresAt) return cachedToken;
            return generateToken();
        } finally {
            TOKEN_LOCK.unlock();
        }
    }

    private String generateToken() throws Exception {
        String appId  = env("GP_APP_ID",  "");
        String appKey = env("GP_APP_KEY", "");
        if (appId.isEmpty() || appKey.isEmpty()) throw new Exception("GP_APP_ID and GP_APP_KEY must be set");

        String nonce  = Instant.now().toString();
        String secret = sha512Hex(nonce + appKey);

        JSONObject body = new JSONObject()
            .put("app_id",     appId)
            .put("nonce",      nonce)
            .put("secret",     secret)
            .put("grant_type", "client_credentials");

        HttpRequest req = HttpRequest.newBuilder()
            .uri(URI.create(baseUrl() + "/accesstoken"))
            .header("Content-Type", "application/json")
            .header("X-GP-Version", GP_VERSION)
            .POST(HttpRequest.BodyPublishers.ofString(body.toString()))
            .build();

        HttpResponse<byte[]> resp = HTTP.send(req, HttpResponse.BodyHandlers.ofByteArray());
        String tokenBody = decodeBody(resp);
        if (resp.statusCode() < 200 || resp.statusCode() >= 300)
            throw new Exception("Token generation failed (" + resp.statusCode() + "): " + tokenBody);

        JSONObject result = new JSONObject(tokenBody);
        cachedToken    = result.getString("token");
        int expiresIn  = result.optInt("seconds_to_expire", 3599);
        tokenExpiresAt = System.currentTimeMillis() + (expiresIn - 60) * 1000L;
        return cachedToken;
    }

    // ── Response writers ──────────────────────────────────────────────────────

    private static void writeSuccess(HttpServletResponse res, JSONObject data, JSONObject raw) throws IOException {
        JSONObject out = new JSONObject();
        out.put("success", true);
        out.put("data",    data);
        out.put("raw",     raw);
        res.getWriter().write(out.toString());
    }

    private static void writeError(HttpServletResponse res, Exception e, int status) throws IOException {
        int httpStatus = status;
        String rawBody = null;
        for (Throwable s : e.getSuppressed()) {
            if (s.getMessage() != null && s.getMessage().startsWith("STATUS:"))
                httpStatus = Integer.parseInt(s.getMessage().substring(7));
            if (s.getMessage() != null && s.getMessage().startsWith("RAW:"))
                rawBody = s.getMessage().substring(4);
        }
        res.setStatus(httpStatus);
        JSONObject out = new JSONObject();
        out.put("success", false);
        out.put("error",   e.getMessage());
        if (rawBody != null) {
            try { out.put("raw", new JSONObject(rawBody)); } catch (Exception ignored) {}
        }
        res.getWriter().write(out.toString());
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static String decodeBody(HttpResponse<byte[]> resp) throws IOException {
        byte[] bytes = resp.body();
        if (bytes == null || bytes.length == 0) return "";
        String enc = resp.headers().firstValue("content-encoding").orElse("").toLowerCase();
        if (enc.contains("gzip") || (bytes.length > 1 && (bytes[0] & 0xFF) == 0x1F && (bytes[1] & 0xFF) == 0x8B)) {
            try (GZIPInputStream gzip = new GZIPInputStream(new ByteArrayInputStream(bytes))) {
                return new String(gzip.readAllBytes(), StandardCharsets.UTF_8);
            }
        }
        return new String(bytes, StandardCharsets.UTF_8);
    }

    private static String bdField(JSONObject bd, String key, String def) {
        if (bd == null) return def;
        return bd.has(key) && !bd.isNull(key) ? bd.get(key).toString() : def;
    }

    private static String toMinorUnits(String amount) {
        return String.valueOf(new BigDecimal(amount)
            .multiply(BigDecimal.valueOf(100))
            .setScale(0, RoundingMode.HALF_UP)
            .intValueExact());
    }

    private static String sha512Hex(String input) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-512");
        byte[] hash = md.digest(input.getBytes(StandardCharsets.UTF_8));
        StringBuilder sb = new StringBuilder();
        for (byte b : hash) sb.append(String.format("%02x", b));
        return sb.toString();
    }
}
