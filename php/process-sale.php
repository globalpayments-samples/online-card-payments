<?php

declare(strict_types=1);

/**
 * Sale Transaction Processing Script
 *
 * This script processes Sale transactions using the Global Payments API with Drop-In UI tokens.
 * A Sale transaction combines authorization and capture in a single operation.
 *
 * PHP version 7.4 or higher
 *
 * @category  Payment_Processing
 * @package   GlobalPayments_DropInUI
 * @author    Global Payments
 * @license   MIT License
 * @link      https://github.com/globalpayments
 */

require_once 'vendor/autoload.php';

use Dotenv\Dotenv;

header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

// Handle preflight OPTIONS request
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

ini_set('display_errors', '0');

/**
 * Generate access token for payment processing
 *
 * Creates a new access token. Permissions are automatically assigned by the API
 * based on the app credentials.
 *
 * @return string The access token
 * @throws Exception If token generation fails
 */
function generatePaymentAccessToken(): array
{
    $nonce = bin2hex(random_bytes(16));

    $requestData = [
        'app_id' => $_ENV['GP_APP_ID'],
        'nonce' => $nonce,
        'secret' => hash('sha512', $nonce . $_ENV['GP_APP_KEY']),
        'grant_type' => 'client_credentials',
        'seconds_to_expire' => 600,
        'permissions' => ['PMT_POST_Create']
    ];

    $apiEndpoint = ($_ENV['GP_ENVIRONMENT'] ?? 'sandbox') === 'production'
        ? 'https://apis.globalpay.com/ucp/accesstoken'
        : 'https://apis.sandbox.globalpay.com/ucp/accesstoken';

    $ch = curl_init($apiEndpoint);
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_POST => true,
        CURLOPT_POSTFIELDS => json_encode($requestData),
        CURLOPT_HTTPHEADER => [
            'Content-Type: application/json',
            'X-GP-Version: 2021-03-22'
        ],
        CURLOPT_ENCODING => '', // Enable automatic decompression
        CURLOPT_TIMEOUT => 30
    ]);

    $response = curl_exec($ch);
    $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $curlError = curl_error($ch);
    curl_close($ch);

    if ($curlError) {
        throw new Exception('Token generation failed: ' . $curlError);
    }

    // Log raw response for debugging
    error_log("Raw API response: " . substr($response, 0, 200));

    $responseData = json_decode($response, true);

    if ($httpCode !== 200 || empty($responseData['token'])) {
        $errorMessage = $responseData['error_description'] ?? $responseData['message'] ?? 'Failed to generate payment token';
        // Log the full response for debugging
        error_log("Token generation failed. HTTP Code: $httpCode, Parsed: " . json_encode($responseData));
        throw new Exception($errorMessage);
    }

    // Return both token and merchant_id from scope
    return [
        'token' => $responseData['token'],
        'merchant_id' => $responseData['scope']['merchant_id'] ?? null
    ];
}

/**
 * Process a Sale transaction
 *
 * @param string $paymentReference The payment token from Drop-In UI
 * @param float $amount The transaction amount
 * @param string $currency The currency code (default: USD)
 * @return array The transaction response
 * @throws Exception If transaction fails
 */
function processSaleTransaction(string $paymentReference, float $amount, string $currency = 'USD'): array
{
    // Generate access token for payment processing
    $tokenData = generatePaymentAccessToken();
    $accessToken = $tokenData['token'];
    $merchantId = $tokenData['merchant_id'];

    // Prepare transaction request
    $transactionData = [
        'type' => 'SALE',
        'channel' => 'CNP',
        'amount' => (string) round($amount * 100), // Convert to cents
        'currency' => $currency,
        'reference' => 'Sale-' . time(),
        'country' => 'US',
        'payment_method' => [
            'entry_mode' => 'ECOM',
            'id' => $paymentReference
        ]
    ];

    // Add account_name (required by API)
    if (!empty($_ENV['GP_ACCOUNT_NAME'])) {
        $transactionData['account_name'] = $_ENV['GP_ACCOUNT_NAME'];
        error_log("Using account_name: " . $_ENV['GP_ACCOUNT_NAME']);
    } elseif (!empty($_ENV['GP_ACCOUNT_ID'])) {
        $transactionData['account_id'] = $_ENV['GP_ACCOUNT_ID'];
    } elseif ($merchantId) {
        // Last resort: try merchant_id if nothing else is available
        $transactionData['merchant_id'] = $merchantId;
        error_log("Using merchant_id from token: $merchantId");
    }

    // Determine API endpoint
    $apiEndpoint = ($_ENV['GP_ENVIRONMENT'] ?? 'sandbox') === 'production'
        ? 'https://apis.globalpay.com/ucp/transactions'
        : 'https://apis.sandbox.globalpay.com/ucp/transactions';

    // Log transaction request for debugging
    error_log("Transaction request to: $apiEndpoint");
    error_log("Transaction data: " . json_encode($transactionData));

    // Execute transaction request
    $ch = curl_init($apiEndpoint);
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_POST => true,
        CURLOPT_POSTFIELDS => json_encode($transactionData),
        CURLOPT_HTTPHEADER => [
            'Content-Type: application/json',
            'Authorization: Bearer ' . $accessToken,
            'X-GP-Version: 2021-03-22'
        ],
        CURLOPT_ENCODING => '', // Enable automatic decompression
        CURLOPT_TIMEOUT => 30
    ]);

    $response = curl_exec($ch);
    $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $curlError = curl_error($ch);
    curl_close($ch);

    if ($curlError) {
        throw new Exception('Transaction request failed: ' . $curlError);
    }

    $responseData = json_decode($response, true);

    // Log transaction response for debugging
    error_log("Transaction API response - HTTP: $httpCode, Body: " . json_encode($responseData));

    // Check for errors in response
    if ($httpCode >= 400) {
        $errorMessage = $responseData['error_description'] ?? $responseData['message'] ?? 'Transaction failed';
        error_log("Transaction failed: $errorMessage. Full response: " . json_encode($responseData));
        throw new Exception($errorMessage);
    }

    return $responseData;
}

try {
    // Load environment variables
    $dotenv = Dotenv::createImmutable(__DIR__);
    $dotenv->load();

    // Get JSON input
    $inputData = json_decode(file_get_contents('php://input'), true);

    // Validate required fields
    if (empty($inputData['payment_reference'])) {
        throw new Exception('Missing payment reference');
    }

    if (empty($inputData['amount']) || floatval($inputData['amount']) <= 0) {
        throw new Exception('Invalid amount');
    }

    $paymentReference = $inputData['payment_reference'];
    $amount = floatval($inputData['amount']);
    $currency = $inputData['currency'] ?? 'USD';

    // Process the Sale transaction
    $transactionResponse = processSaleTransaction($paymentReference, $amount, $currency);

    // Check transaction status
    if ($transactionResponse['status'] === 'CAPTURED') {
        echo json_encode([
            'success' => true,
            'message' => 'Payment successful!',
            'data' => [
                'transactionId' => $transactionResponse['id'],
                'status' => $transactionResponse['status'],
                'amount' => $amount,
                'currency' => $currency,
                'reference' => $transactionResponse['reference'] ?? '',
                'timestamp' => $transactionResponse['time_created'] ?? date('Y-m-d H:i:s')
            ]
        ]);
    } else {
        throw new Exception('Transaction not captured. Status: ' . ($transactionResponse['status'] ?? 'UNKNOWN'));
    }

} catch (Exception $e) {
    http_response_code(400);
    echo json_encode([
        'success' => false,
        'message' => 'Payment processing failed',
        'error' => $e->getMessage()
    ]);
}
