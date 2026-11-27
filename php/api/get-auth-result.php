<?php
declare(strict_types=1);
require_once __DIR__ . '/../vendor/autoload.php';
require_once __DIR__ . '/../src/GpApiClient.php';

use Dotenv\Dotenv;
$dotenv = Dotenv::createUnsafeMutable(__DIR__ . '/..');
$dotenv->load();

$input            = json_decode(file_get_contents('php://input'), true) ?? [];
$serverTransId    = (string) ($input['server_trans_id'] ?? '');

if (!$serverTransId) {
    GpApiClient::jsonResponse(['success' => false, 'error' => 'server_trans_id is required'], 400);
}

try {
    $authId = GpApiClient::authenticationId($serverTransId);
    $raw = GpApiClient::request('POST', '/authentications/' . rawurlencode($authId) . '/result', []);

    GpApiClient::jsonResponse([
        'success' => true,
        'data' => [
            'server_trans_id'      => $raw['id']                               ?? null,
            'status'               => $raw['status']                           ?? null,
            'eci'                  => $raw['three_ds']['eci']                  ?? null,
            'authentication_value' => $raw['three_ds']['authentication_value'] ?? null,
            'ds_trans_ref'         => $raw['three_ds']['ds_trans_ref']         ?? null,
            'message_version'      => $raw['three_ds']['message_version']      ?? null,
            'server_trans_ref'     => $raw['three_ds']['server_trans_ref']     ?? $raw['id'],
        ],
        'raw' => $raw,
    ]);
} catch (\Throwable $e) {
    GpApiClient::errorResponse($e);
}
