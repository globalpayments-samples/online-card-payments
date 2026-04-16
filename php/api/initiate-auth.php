<?php
declare(strict_types=1);
require_once __DIR__ . '/../vendor/autoload.php';
require_once __DIR__ . '/../src/GpApiClient.php';

use Dotenv\Dotenv;
$dotenv = Dotenv::createUnsafeMutable(__DIR__ . '/..');
$dotenv->load();

$input        = json_decode(file_get_contents('php://input'), true) ?? [];
$paymentToken = $input['payment_token'] ?? '';

if (!$paymentToken) {
    GpApiClient::jsonResponse(['success' => false, 'error' => 'payment_token is required'], 400);
}

$serverTransIdRaw         = $input['server_trans_id']             ?? '';
$serverTransId            = preg_replace('/^AUT_/', '', $serverTransIdRaw);
$messageVersion           = $input['message_version']              ?? '2.1.0';
$methodUrlCompletionStatus = $input['method_url_completion_status'] ?? 'NO';
$browserData              = $input['browser_data']                 ?? [];
$order                    = $input['order']                        ?? [];
$amount                   = $order['amount']                       ?? '10.00';
$currency                 = $order['currency']                     ?? 'GBP';

if (!$serverTransId) {
    GpApiClient::jsonResponse(['success' => false, 'error' => 'server_trans_id is required'], 400);
    exit;
}

function mapColorDepth(string $v): string {
    $map = [1=>'ONE_BIT',2=>'TWO_BITS',4=>'FOUR_BITS',8=>'EIGHT_BITS',
            15=>'FIFTEEN_BITS',16=>'SIXTEEN_BITS',24=>'TWENTY_FOUR_BITS',
            32=>'THIRTY_TWO_BITS',48=>'FORTY_EIGHT_BITS'];
    return $map[(int)$v] ?? 'TWENTY_FOUR_BITS';
}
function mapBool(string $v): string {
    return strtolower($v) === 'true' ? 'TRUE' : 'FALSE';
}

try {
    $raw = GpApiClient::request('POST', '/authentications', [
        'account_name'   => getenv('GP_ACCOUNT_NAME') ?: 'transaction_processing',
        'account_id'     => getenv('GP_ACCOUNT_ID')   ?: null,
        'merchant_id'    => getenv('GP_MERCHANT_ID')  ?: null,
        'channel'        => 'CNP',
        'country'        => 'GB',
        'amount'         => GpApiClient::toMinorUnits($amount),
        'currency'       => $currency,
        'reference'      => GpApiClient::uuid(),
        'payment_method' => [
            'entry_mode' => 'ECOM',
            'id'         => $paymentToken,
        ],
        'method_url_completion_status' => $methodUrlCompletionStatus,
        'three_ds' => [
            'source'           => 'BROWSER',
            'preference'       => 'NO_PREFERENCE',
            'message_version'  => $messageVersion,
            'server_trans_ref' => $serverTransId,
        ],
        'order' => [
            'amount'            => GpApiClient::toMinorUnits($amount),
            'currency'          => $currency,
            'reference'         => GpApiClient::uuid(),
            'address_indicator' => false,
            'date_time_created' => date('Y-m-d\TH:i:s.000\Z'),
        ],
        'payer' => [
            'email' => 'test@example.com',
            'billing_address' => [
                'line1'       => '1 Test Street',
                'city'        => 'London',
                'postal_code' => 'SW1A 1AA',
                'country'     => '826',
            ],
        ],
        'browser_data' => [
            'accept_header'        => $browserData['accept_header']         ?? 'text/html,application/xhtml+xml',
            'color_depth'          => mapColorDepth((string)($browserData['color_depth']        ?? '24')),
            'ip'                   => $browserData['ip']                    ?? '123.123.123.123',
            'java_enabled'         => mapBool((string)($browserData['java_enabled']       ?? 'false')),
            'javascript_enabled'   => mapBool((string)($browserData['javascript_enabled'] ?? 'true')),
            'language'             => $browserData['language']              ?? 'en-GB',
            'screen_height'        => (string) ($browserData['screen_height']      ?? '1080'),
            'screen_width'         => (string) ($browserData['screen_width']       ?? '1920'),
            'challenge_window_size' => $browserData['challenge_window_size'] ?? 'FULL_SCREEN',
            'timezone'             => (string) ($browserData['timezone']           ?? '0'),
            'user_agent'           => $browserData['user_agent']            ?? 'Mozilla/5.0',
        ],
        'notifications' => [
            'challenge_return_url'       => getenv('CHALLENGE_NOTIFICATION_URL') ?: null,
            'three_ds_method_return_url' => getenv('METHOD_NOTIFICATION_URL')    ?: null,
        ],
    ]);

    GpApiClient::jsonResponse([
        'success' => true,
        'data' => [
            'server_trans_id'      => $raw['id'],
            'status'               => $raw['status']                             ?? null,
            'acs_reference_number' => $raw['three_ds']['acs_reference_number']   ?? null,
            'acs_trans_id'         => $raw['three_ds']['acs_trans_id']           ?? null,
            'acs_signed_content'   => $raw['three_ds']['acs_signed_content']     ?? null,
            'acs_challenge_url'    => $raw['three_ds']['acs_challenge_url']      ?? $raw['three_ds']['challenge_value'] ?? null,
        ],
        'raw' => $raw,
    ]);
} catch (\Throwable $e) {
    GpApiClient::errorResponse($e);
}
