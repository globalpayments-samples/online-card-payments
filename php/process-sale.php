<?php

declare(strict_types=1);

/**
 * Sale Transaction Processing Script
 *
 * This script processes Sale transactions using the Global Payments PHP SDK with Drop-In UI tokens.
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
use GlobalPayments\Api\ServiceConfigs\Gateways\GpApiConfig;
use GlobalPayments\Api\ServicesContainer;
use GlobalPayments\Api\PaymentMethods\CreditCardData;
use GlobalPayments\Api\Entities\Enums\Environment;
use GlobalPayments\Api\Entities\Enums\Channel;

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

    // Configure Global Payments SDK
    $config = new GpApiConfig();
    $config->appId = $_ENV['GP_APP_ID'];
    $config->appKey = $_ENV['GP_APP_KEY'];
    $config->environment = ($_ENV['GP_ENVIRONMENT'] ?? 'sandbox') === 'production'
        ? Environment::PRODUCTION
        : Environment::TEST;
    $config->channel = Channel::CardNotPresent;
    $config->country = 'US';

    // Note: Don't set account name - let SDK auto-detect from credentials

    // Configure the service
    ServicesContainer::configureService($config);

    // Create card data from the payment reference (token from Drop-In UI)
    $card = new CreditCardData();
    $card->token = $paymentReference;

    // Process the charge
    $response = $card->charge($amount)
        ->withCurrency($currency)
        ->execute();

    // Check response
    if ($response->responseCode === '00' || $response->responseCode === 'SUCCESS') {
        echo json_encode([
            'success' => true,
            'message' => 'Payment successful!',
            'data' => [
                'transactionId' => $response->transactionId,
                'status' => $response->responseMessage,
                'amount' => $amount,
                'currency' => $currency,
                'reference' => $response->referenceNumber ?? '',
                'timestamp' => date('Y-m-d H:i:s')
            ]
        ]);
    } else {
        throw new Exception('Transaction declined: ' . ($response->responseMessage ?? 'Unknown error'));
    }

} catch (\GlobalPayments\Api\Entities\Exceptions\GatewayException $e) {
    http_response_code(400);
    echo json_encode([
        'success' => false,
        'message' => 'Payment processing failed',
        'error' => $e->getMessage()
    ]);
} catch (\GlobalPayments\Api\Entities\Exceptions\ApiException $e) {
    http_response_code(400);
    echo json_encode([
        'success' => false,
        'message' => 'API error',
        'error' => $e->getMessage()
    ]);
} catch (Exception $e) {
    http_response_code(400);
    echo json_encode([
        'success' => false,
        'message' => 'Payment processing failed',
        'error' => $e->getMessage()
    ]);
}
