<?php

declare(strict_types=1);

/**
 * Access Token Generation Endpoint
 *
 * This script generates a restricted, single-use access token for the Drop-In UI.
 * The token is used by the frontend to securely initialize the payment form and tokenize cards.
 *
 * The token expires after 10 minutes and has limited permissions for payment method creation only.
 *
 * PHP version 7.4 or higher
 *
 * @category  Authentication
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

try {
    // Load environment variables
    $dotenv = Dotenv::createImmutable(__DIR__);
    $dotenv->load();

    // Validate required environment variables
    if (empty($_ENV['GP_APP_ID']) || empty($_ENV['GP_APP_KEY'])) {
        throw new Exception('Missing required credentials: GP_APP_ID and GP_APP_KEY');
    }

    // Generate a unique nonce for security
    $nonce = bin2hex(random_bytes(16));

    // Prepare request data for access token
    // For Drop-In UI, we need PMT_POST_Create_Single permission for card tokenization
    // This is the exact permission used in the official Global Payments sample
    $requestData = [
        'app_id' => $_ENV['GP_APP_ID'],
        'nonce' => $nonce,
        'secret' => hash('sha512', $nonce . $_ENV['GP_APP_KEY']),
        'grant_type' => 'client_credentials',
        'seconds_to_expire' => 600, // 10 minutes
        'permissions' => ['PMT_POST_Create_Single']
    ];

    // Determine API endpoint based on environment
    $apiEndpoint = ($_ENV['GP_ENVIRONMENT'] ?? 'sandbox') === 'production'
        ? 'https://apis.globalpay.com/ucp/accesstoken'
        : 'https://apis.sandbox.globalpay.com/ucp/accesstoken';

    // Initialize cURL request
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

    // Execute request
    $response = curl_exec($ch);
    $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $curlError = curl_error($ch);
    curl_close($ch);

    // Check for cURL errors
    if ($curlError) {
        throw new Exception('Connection error: ' . $curlError);
    }

    // Parse response
    $responseData = json_decode($response, true);

    // Check for successful response
    if ($httpCode !== 200 || empty($responseData['token'])) {
        $errorMessage = $responseData['error_description'] ?? $responseData['message'] ?? 'Failed to generate access token';
        throw new Exception($errorMessage);
    }

    // Return access token to client
    echo json_encode([
        'success' => true,
        'token' => $responseData['token'],
        'expiresIn' => $responseData['seconds_to_expire'] ?? 600
    ]);

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        'success' => false,
        'message' => 'Error generating access token',
        'error' => $e->getMessage()
    ]);
}
