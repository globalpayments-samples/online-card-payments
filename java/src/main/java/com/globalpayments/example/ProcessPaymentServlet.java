package com.globalpayments.example;

import com.global.api.ServicesContainer;
import com.global.api.entities.Transaction;
import com.global.api.entities.enums.Channel;
import com.global.api.entities.enums.Environment;
import com.global.api.entities.exceptions.ApiException;
import com.global.api.entities.exceptions.ConfigurationException;
import com.global.api.paymentMethods.CreditCardData;
import com.global.api.serviceConfigs.GpApiConfig;
import io.github.cdimascio.dotenv.Dotenv;
import jakarta.servlet.ServletException;
import jakarta.servlet.annotation.WebServlet;
import jakarta.servlet.http.HttpServlet;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.json.JSONObject;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.OutputStream;
import java.math.BigDecimal;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.stream.Collectors;

/**
 * Global Payments Drop-In UI - Sale Transaction Servlet (Java)
 *
 * This servlet implements Global Payments Drop-In UI integration
 * for processing Sale transactions using the official Java SDK.
 *
 * Endpoints:
 * - POST /get-access-token: Generates access token for Drop-In UI tokenization
 * - POST /process-sale: Processes Sale transaction using payment reference
 *
 * @author Global Payments
 * @version 2.0
 */

@WebServlet(urlPatterns = {"/get-access-token", "/process-sale"})
public class ProcessPaymentServlet extends HttpServlet {

    private static final long serialVersionUID = 1L;
    private final Dotenv dotenv = Dotenv.load();

    /**
     * Generates a random nonce for access token request
     */
    private String generateNonce() {
        SecureRandom random = new SecureRandom();
        byte[] bytes = new byte[16];
        random.nextBytes(bytes);
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) {
            sb.append(String.format("%02x", b));
        }
        return sb.toString();
    }

    /**
     * Creates SHA-512 hash of nonce + appKey
     */
    private String hashSecret(String nonce, String appKey) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-512");
        byte[] hash = digest.digest((nonce + appKey).getBytes(StandardCharsets.UTF_8));
        StringBuilder sb = new StringBuilder();
        for (byte b : hash) {
            sb.append(String.format("%02x", b));
        }
        return sb.toString();
    }

    /**
     * Handles POST requests
     */
    @Override
    protected void doPost(HttpServletRequest request, HttpServletResponse response)
            throws ServletException, IOException {

        response.setContentType("application/json");
        response.setCharacterEncoding("UTF-8");

        String path = request.getServletPath();

        if ("/get-access-token".equals(path)) {
            handleGetAccessToken(request, response);
        } else if ("/process-sale".equals(path)) {
            handleProcessSale(request, response);
        }
    }

    /**
     * Handles /get-access-token endpoint
     * Generates access token with PMT_POST_Create_Single permission for Drop-In UI
     */
    private void handleGetAccessToken(HttpServletRequest request, HttpServletResponse response)
            throws IOException {
        try {
            // Generate nonce and secret
            String nonce = generateNonce();
            String secret = hashSecret(nonce, dotenv.get("GP_APP_KEY"));

            // Build token request JSON
            JSONObject tokenRequest = new JSONObject();
            tokenRequest.put("app_id", dotenv.get("GP_APP_ID"));
            tokenRequest.put("nonce", nonce);
            tokenRequest.put("secret", secret);
            tokenRequest.put("grant_type", "client_credentials");
            tokenRequest.put("seconds_to_expire", 600);
            tokenRequest.put("permissions", new String[]{"PMT_POST_Create_Single"});

            // Determine API endpoint
            String apiEndpoint = "production".equals(dotenv.get("GP_ENVIRONMENT"))
                ? "https://apis.globalpay.com/ucp/accesstoken"
                : "https://apis.sandbox.globalpay.com/ucp/accesstoken";

            // Make API request
            URL url = new URL(apiEndpoint);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("POST");
            conn.setRequestProperty("Content-Type", "application/json");
            conn.setRequestProperty("X-GP-Version", "2021-03-22");
            conn.setDoOutput(true);

            // Send request
            try (OutputStream os = conn.getOutputStream()) {
                byte[] input = tokenRequest.toString().getBytes(StandardCharsets.UTF_8);
                os.write(input, 0, input.length);
            }

            // Read response
            int responseCode = conn.getResponseCode();
            BufferedReader br = new BufferedReader(
                new java.io.InputStreamReader(
                    responseCode == 200 ? conn.getInputStream() : conn.getErrorStream(),
                    StandardCharsets.UTF_8
                )
            );
            String responseBody = br.lines().collect(Collectors.joining());
            br.close();

            if (responseCode != 200) {
                throw new Exception("Failed to generate access token: " + responseBody);
            }

            // Parse response
            JSONObject tokenResponse = new JSONObject(responseBody);
            String token = tokenResponse.getString("token");
            int expiresIn = tokenResponse.optInt("seconds_to_expire", 600);

            // Return success response
            JSONObject successResponse = new JSONObject();
            successResponse.put("success", true);
            successResponse.put("token", token);
            successResponse.put("expiresIn", expiresIn);

            response.getWriter().write(successResponse.toString());

        } catch (Exception e) {
            response.setStatus(HttpServletResponse.SC_INTERNAL_SERVER_ERROR);
            JSONObject errorResponse = new JSONObject();
            errorResponse.put("success", false);
            errorResponse.put("message", "Error generating access token");
            errorResponse.put("error", e.getMessage());
            response.getWriter().write(errorResponse.toString());
        }
    }

    /**
     * Handles /process-sale endpoint
     * Processes Sale transaction using Global Payments SDK
     */
    private void handleProcessSale(HttpServletRequest request, HttpServletResponse response)
            throws IOException {
        try {
            // Read JSON request body
            BufferedReader reader = request.getReader();
            String requestBody = reader.lines().collect(Collectors.joining());
            JSONObject jsonRequest = new JSONObject(requestBody);

            // Validate input
            if (!jsonRequest.has("payment_reference")) {
                throw new ApiException("Missing payment reference");
            }

            if (!jsonRequest.has("amount") || jsonRequest.getDouble("amount") <= 0) {
                throw new ApiException("Invalid amount");
            }

            String paymentReference = jsonRequest.getString("payment_reference");
            BigDecimal amount = jsonRequest.getBigDecimal("amount");
            String currency = jsonRequest.optString("currency", "USD");

            // Configure Global Payments SDK
            GpApiConfig config = new GpApiConfig();
            config.setAppId(dotenv.get("GP_APP_ID"));
            config.setAppKey(dotenv.get("GP_APP_KEY"));
            config.setEnvironment("production".equals(dotenv.get("GP_ENVIRONMENT"))
                ? Environment.PRODUCTION
                : Environment.TEST);
            config.setChannel(Channel.CardNotPresent);
            config.setCountry("US");

            // Note: Don't set account name - let SDK auto-detect

            // Configure the service
            ServicesContainer.configureService(config);

            // Create card data from payment reference token
            CreditCardData card = new CreditCardData();
            card.setToken(paymentReference);

            // Process the charge
            Transaction transaction = card.charge(amount)
                .withCurrency(currency)
                .execute();

            // Check response
            String responseCode = transaction.getResponseCode();
            if ("00".equals(responseCode) || "SUCCESS".equals(responseCode)) {
                JSONObject successResponse = new JSONObject();
                successResponse.put("success", true);
                successResponse.put("message", "Payment successful!");

                JSONObject data = new JSONObject();
                data.put("transactionId", transaction.getTransactionId());
                data.put("status", transaction.getResponseMessage());
                data.put("amount", amount.toString());
                data.put("currency", currency);
                data.put("reference", transaction.getReferenceNumber() != null ? transaction.getReferenceNumber() : "");
                data.put("timestamp", transaction.getTimestamp() != null ? transaction.getTimestamp() : "");

                successResponse.put("data", data);
                response.getWriter().write(successResponse.toString());
            } else {
                throw new ApiException("Transaction declined: " + transaction.getResponseMessage());
            }

        } catch (ApiException | ConfigurationException e) {
            response.setStatus(HttpServletResponse.SC_BAD_REQUEST);
            JSONObject errorResponse = new JSONObject();
            errorResponse.put("success", false);
            errorResponse.put("message", "Payment processing failed");
            errorResponse.put("error", e.getMessage());
            response.getWriter().write(errorResponse.toString());
        } catch (Exception e) {
            response.setStatus(HttpServletResponse.SC_BAD_REQUEST);
            JSONObject errorResponse = new JSONObject();
            errorResponse.put("success", false);
            errorResponse.put("message", "Payment processing failed");
            errorResponse.put("error", e.getMessage());
            response.getWriter().write(errorResponse.toString());
        }
    }
}
