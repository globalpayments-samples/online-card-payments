package com.globalpayments.example;

import io.github.cdimascio.dotenv.Dotenv;
import jakarta.servlet.ServletException;
import jakarta.servlet.annotation.WebServlet;
import jakarta.servlet.http.HttpServlet;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.json.JSONObject;

import java.io.IOException;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.stream.Collectors;

/**
 * Drop-In UI access token endpoint.
 *
 * Provides POST /get-access-token which generates a tokenization token
 * (PMT_POST_Create_Single permission) for the frontend Drop-In UI.
 */
@WebServlet(urlPatterns = {"/get-access-token"})
public class ProcessPaymentServlet extends HttpServlet {

    private static final long serialVersionUID = 1L;
    private Dotenv dotenv;

    @Override
    public void init() throws ServletException {
        dotenv = Dotenv.configure().ignoreIfMissing().load();
    }

    private String env(String key) {
        String v = dotenv.get(key, null);
        return (v != null && !v.isEmpty()) ? v : System.getenv(key);
    }

    private String generateNonce() {
        SecureRandom random = new SecureRandom();
        byte[] bytes = new byte[16];
        random.nextBytes(bytes);
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) sb.append(String.format("%02x", b));
        return sb.toString();
    }

    private String hashSecret(String nonce, String appKey) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-512");
        byte[] hash = digest.digest((nonce + appKey).getBytes(StandardCharsets.UTF_8));
        StringBuilder sb = new StringBuilder();
        for (byte b : hash) sb.append(String.format("%02x", b));
        return sb.toString();
    }

    @Override
    protected void doPost(HttpServletRequest request, HttpServletResponse response)
            throws ServletException, IOException {

        response.setContentType("application/json");
        response.setCharacterEncoding("UTF-8");
        response.setHeader("Access-Control-Allow-Origin",  "*");
        response.setHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        response.setHeader("Access-Control-Allow-Headers", "Content-Type");

        try {
            String appId  = env("GP_APP_ID");
            String appKey = env("GP_APP_KEY");
            if (appId == null || appKey == null) throw new Exception("GP_APP_ID and GP_APP_KEY must be set");

            String nonce  = generateNonce();
            String secret = hashSecret(nonce, appKey);

            JSONObject tokenRequest = new JSONObject();
            tokenRequest.put("app_id",            appId);
            tokenRequest.put("nonce",             nonce);
            tokenRequest.put("secret",            secret);
            tokenRequest.put("grant_type",        "client_credentials");
            tokenRequest.put("seconds_to_expire", 600);
            tokenRequest.put("permissions",       new String[]{"PMT_POST_Create_Single"});

            boolean isProd = "production".equals(env("GP_ENVIRONMENT"));
            String apiEndpoint = isProd
                ? "https://apis.globalpay.com/ucp/accesstoken"
                : "https://apis.sandbox.globalpay.com/ucp/accesstoken";

            URL url = new URL(apiEndpoint);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("POST");
            conn.setRequestProperty("Content-Type",  "application/json");
            conn.setRequestProperty("X-GP-Version",  "2021-03-22");
            conn.setDoOutput(true);

            try (OutputStream os = conn.getOutputStream()) {
                os.write(tokenRequest.toString().getBytes(StandardCharsets.UTF_8));
            }

            int code = conn.getResponseCode();
            java.io.InputStream rawStream = code == 200 ? conn.getInputStream() : conn.getErrorStream();
            String contentEncoding = conn.getHeaderField("Content-Encoding");
            java.io.InputStream stream = rawStream;
            String body;
            try {
                if (contentEncoding != null) {
                    String enc = contentEncoding.toLowerCase();
                    if (enc.contains("gzip"))    stream = new java.util.zip.GZIPInputStream(rawStream);
                    else if (enc.contains("deflate")) stream = new java.util.zip.InflaterInputStream(rawStream);
                }
                body = new java.io.BufferedReader(new java.io.InputStreamReader(stream, StandardCharsets.UTF_8))
                    .lines().collect(Collectors.joining());
            } finally {
                try { rawStream.close(); } catch (Exception ignored) {}
            }

            if (code != 200) throw new Exception("Failed to generate access token: " + body);

            JSONObject tokenResponse = new JSONObject(body);
            String token      = tokenResponse.getString("token");
            int    expiresIn  = tokenResponse.optInt("seconds_to_expire", 600);

            JSONObject result = new JSONObject();
            result.put("success",   true);
            result.put("token",     token);
            result.put("expiresIn", expiresIn);
            response.getWriter().write(result.toString());

        } catch (Exception e) {
            response.setStatus(HttpServletResponse.SC_INTERNAL_SERVER_ERROR);
            JSONObject err = new JSONObject();
            err.put("success", false);
            err.put("message", "Error generating access token");
            err.put("error",   e.getMessage());
            response.getWriter().write(err.toString());
        }
    }

    @Override
    protected void doOptions(HttpServletRequest req, HttpServletResponse res)
            throws ServletException, IOException {
        res.setHeader("Access-Control-Allow-Origin",  "*");
        res.setHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
        res.setHeader("Access-Control-Allow-Headers", "Content-Type");
        res.setStatus(204);
    }
}
