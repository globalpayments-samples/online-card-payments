<?php

declare(strict_types=1);

/**
 * Sale Transaction Processing Script
 *
 * This script processes Sale transactions using the Global Payments GP-API with Drop-In UI tokens.
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
function generatePaymentAccessToken(): string
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
        CURLOPT_TIMEOUT => 30
    ]);

    $response = curl_exec($ch);
    $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $curlError = curl_error($ch);
    curl_close($ch);

    if ($curlError) {
        throw new Exception('Token generation failed: ' . $curlError);
    }

    $responseData = json_decode($response, true);

    if ($httpCode !== 200 || empty($responseData['token'])) {
        $errorMessage = $responseData['error_description'] ?? $responseData['message'] ?? 'Failed to generate payment token';
        throw new Exception($errorMessage);
    }

    return $responseData['token'];
}

/**
 * Process a Sale transaction
 *
 * @param string $paymentReference The payment token from Drop-In UI
 * @param float $amount The transaction amount
 * @param string $currency The currency code (default: USD)
 * @param string|null $billingZip Optional billing ZIP code for AVS
 * @return array The transaction response
 * @throws Exception If transaction fails
 */
function processSaleTransaction(string $paymentReference, float $amount, string $currency = 'USD', ?string $billingZip = null): array
{
    // Generate access token for payment processing
    $accessToken = generatePaymentAccessToken();

    // Prepare transaction request
    $transactionData = [
        'account_name' => $_ENV['GP_ACCOUNT_NAME'] ?? 'transaction_processing',
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

    // Add billing address if ZIP code provided
    if ($billingZip !== null && $billingZip !== '') {
        $transactionData['payment_method']['billing_address'] = [
            'postal_code' => $billingZip
        ];
    }

    // Determine API endpoint
    $apiEndpoint = ($_ENV['GP_ENVIRONMENT'] ?? 'sandbox') === 'production'
        ? 'https://apis.globalpay.com/ucp/transactions'
        : 'https://apis.sandbox.globalpay.com/ucp/transactions';

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

    // Check for errors in response
    if ($httpCode >= 400) {
        $errorMessage = $responseData['error_description'] ?? $responseData['message'] ?? 'Transaction failed';
        throw new Exception($errorMessage);
    }

    return $responseData;
}

try {
    // Load environment variables
    $dotenv = Dotenv::createImmutable(__DIR__);
    $dotenv->load();

    // Validate required credentials
    if (empty($_ENV['GP_APP_ID']) || empty($_ENV['GP_APP_KEY'])) {
        throw new Exception('Missing required credentials: GP_APP_ID and GP_APP_KEY');
    }

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
    $billingZip = $inputData['billing_zip'] ?? null;

    // Process the Sale transaction
    $transactionResponse = processSaleTransaction($paymentReference, $amount, $currency, $billingZip);

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
