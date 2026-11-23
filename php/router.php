<?php
/**
 * PHP built-in server router for GP-API 3DS2 backend.
 * Dispatches /api/* and legacy /get-access-token requests.
 * Usage: ./run.sh
 */

$uri    = $_SERVER['REQUEST_URI'];
$method = $_SERVER['REQUEST_METHOD'];

// CORS preflight
if ($method === 'OPTIONS') {
    header('Access-Control-Allow-Origin: *');
    header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
    header('Access-Control-Allow-Headers: Content-Type');
    http_response_code(204);
    exit;
}

$routes = [
    'GET /api/health'                  => __DIR__ . '/api/health.php',
    'POST /get-access-token'           => __DIR__ . '/get-access-token.php',
    'POST /api/check-enrollment'       => __DIR__ . '/api/check-enrollment.php',
    'POST /api/initiate-auth'          => __DIR__ . '/api/initiate-auth.php',
    'POST /api/get-auth-result'        => __DIR__ . '/api/get-auth-result.php',
    'POST /api/authorize-payment'      => __DIR__ . '/api/authorize-payment.php',
    'GET /3ds/challenge-notification'  => __DIR__ . '/api/challenge-notification.php',
    'POST /3ds/challenge-notification' => __DIR__ . '/api/challenge-notification.php',
    'GET /3ds/method-notification'     => __DIR__ . '/api/method-notification.php',
    'POST /3ds/method-notification'    => __DIR__ . '/api/method-notification.php',
];

// Strip query string for routing
$path = strtok($uri, '?');
$key  = $method . ' ' . $path;

foreach ($routes as $route => $file) {
    if ($route === $key) {
        require $file;
        exit;
    }
}

// 404 for unmatched API routes
if (str_starts_with($path, '/api/')) {
    header('Content-Type: application/json');
    http_response_code(404);
    echo json_encode(['error' => 'Not found', 'path' => $path]);
    exit;
}

// Anything else: serve the page, its assets and the endpoint scripts only,
// never .env, composer files, vendor/ or the server's own files
$path = rawurldecode(parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?? '/');
$isAsset = preg_match('#^/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_.-]+\.(?:html|css|js|png|jpe?g|gif|svg|ico|webp|woff2?)$#', $path);
$isEndpoint = preg_match('#^/[A-Za-z0-9_-]+\.php$#', $path) && $path !== '/router.php';
$isHidden = preg_match('#(^|/)(\.|vendor/|node_modules/)#', $path);

if ($path === '/' || (($isAsset || $isEndpoint) && !$isHidden)) {
    return false;
}

http_response_code(404);
echo 'Not Found';
return true;
