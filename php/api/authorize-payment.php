<?php
declare(strict_types=1);
require_once __DIR__ . '/../vendor/autoload.php';
require_once __DIR__ . '/../src/GpApiClient.php';

use Dotenv\Dotenv;
$dotenv = Dotenv::createUnsafeMutable(__DIR__ . '/..');
$dotenv->load();

$input        = json_decode(file_get_contents('php://input'), true) ?? [];
$paymentToken = $input['payment_token'] ?? '';
$amount       = $input['amount']        ?? '10.00';
$currency     = $input['currency']      ?? 'GBP';
$threeDsIn    = $input['three_ds']      ?? [];

if (!$paymentToken) {
    GpApiClient::jsonResponse(['success' => false, 'error' => 'payment_token is required'], 400);
}

try {
    $payload = [
        'account_name'   => getenv('GP_ACCOUNT_NAME') ?: 'transaction_processing',
        'account_id'     => getenv('GP_ACCOUNT_ID')   ?: null,
        'merchant_id'    => getenv('GP_MERCHANT_ID')  ?: null,
        'channel'        => 'CNP',
        'type'           => 'SALE',
        'amount'         => GpApiClient::toMinorUnits($amount),
        'currency'       => $currency,
        'reference'      => GpApiClient::uuid(),
        'country'        => 'GB',
        'payment_method' => [
            'entry_mode' => 'ECOM',
            'id'         => $paymentToken,
        ],
    ];

    // GP-API only applies 3DS to a sale through payment_method.authentication.
    // It takes the ECI, authentication value and DS reference from the
    // authentication it finds by id.
    $authRef = ($threeDsIn['server_trans_id'] ?? '') ?: ($threeDsIn['server_trans_ref'] ?? '');
    if ($authRef) {
        $payload['payment_method']['authentication'] = ['id' => GpApiClient::authenticationId((string) $authRef)];
    }

    $raw = GpApiClient::request('POST', '/transactions', $payload);

    GpApiClient::jsonResponse([
        'success' => true,
        'data' => [
            'transaction_id' => $raw['id'],
            'status'         => $raw['status']                ?? null,
            'result_code'    => $raw['action']['result_code'] ?? null,
            'amount'         => $raw['amount']                ?? null,
            'currency'       => $raw['currency']              ?? null,
        ],
        'raw' => $raw,
    ]);
} catch (\Throwable $e) {
    GpApiClient::errorResponse($e);
}
