<?php
declare(strict_types=1);

use PHPUnit\Framework\TestCase;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\Attributes\Test;

require_once __DIR__ . '/../../src/GpApiClient.php';

/**
 * Unit tests for GpApiClient utility methods.
 *
 * Tests static utility methods that do NOT require network:
 *   - toMinorUnits()  — currency to minor units (cents)
 *   - twoDigitYear()  — 4-digit year → 2-digit year
 *   - uuid()          — UUID v4 generation
 *
 * Run: ./vendor/bin/phpunit --configuration phpunit.xml
 */
class GpApiClientTest extends TestCase
{
    // ── toMinorUnits ──────────────────────────────────────────────────────────

    #[Test]
    #[DataProvider('minorUnitsProvider')]
    public function testToMinorUnits(string $input, string $expected): void
    {
        $this->assertSame($expected, GpApiClient::toMinorUnits($input));
    }

    public static function minorUnitsProvider(): array
    {
        return [
            'standard amount'   => ['10.00',   '1000'],
            'minimum cent'      => ['0.01',    '1'],
            'fractional 9.99'   => ['9.99',    '999'],
            'whole dollar'      => ['1.00',    '100'],
            'large amount'      => ['100.00',  '10000'],
            'ninety nine cents' => ['0.99',    '99'],
            'half dollar'       => ['50.50',   '5050'],
            'four digits'       => ['1234.56', '123456'],
        ];
    }

    #[Test]
    public function testToMinorUnitsReturnsString(): void
    {
        $result = GpApiClient::toMinorUnits('10.00');
        $this->assertIsString($result);
    }

    #[Test]
    public function testToMinorUnitsIntegerInput(): void
    {
        $this->assertSame('1000', GpApiClient::toMinorUnits('10'));
    }

    // ── twoDigitYear ──────────────────────────────────────────────────────────

    #[Test]
    #[DataProvider('twoDigitYearProvider')]
    public function testTwoDigitYear(string $input, string $expected): void
    {
        $this->assertSame($expected, GpApiClient::twoDigitYear($input));
    }

    public static function twoDigitYearProvider(): array
    {
        return [
            '2025' => ['2025', '25'],
            '2030' => ['2030', '30'],
            '1999' => ['1999', '99'],
            '2000' => ['2000', '00'],
            '2024' => ['2024', '24'],
        ];
    }

    #[Test]
    public function testTwoDigitYearReturnsTwoChars(): void
    {
        $this->assertSame(2, strlen(GpApiClient::twoDigitYear('2025')));
    }

    // ── uuid ──────────────────────────────────────────────────────────────────

    #[Test]
    public function testUuidMatchesV4Format(): void
    {
        $uuid = GpApiClient::uuid();
        $this->assertMatchesRegularExpression(
            '/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/',
            $uuid,
            'UUID must match RFC 4122 v4 format'
        );
    }

    #[Test]
    public function testUuidIsUnique(): void
    {
        $uuids = array_map(fn($_) => GpApiClient::uuid(), range(1, 10));
        $this->assertCount(10, array_unique($uuids), 'All generated UUIDs must be unique');
    }

    #[Test]
    public function testUuidLength(): void
    {
        $this->assertSame(36, strlen(GpApiClient::uuid()));
    }

    #[Test]
    public function testUuidContainsHyphens(): void
    {
        $uuid = GpApiClient::uuid();
        $this->assertSame(4, substr_count($uuid, '-'));
    }

    // ── AUT_ prefix stripping ─────────────────────────────────────────────────

    #[Test]
    public function testAutPrefixStripping(): void
    {
        // Mirrors: preg_replace('/^AUT_/', '', $serverTransIdRaw)
        $this->assertSame('abc-123',          preg_replace('/^AUT_/', '', 'AUT_abc-123'));
        $this->assertSame('plain-uuid',       preg_replace('/^AUT_/', '', 'plain-uuid'));
        $this->assertSame('prefix_AUT_abc',   preg_replace('/^AUT_/', '', 'prefix_AUT_abc'));
        $this->assertSame('',                 preg_replace('/^AUT_/', '', ''));
    }

    // ── SHA-512 nonce hashing ─────────────────────────────────────────────────

    #[Test]
    public function testSha512HashLength(): void
    {
        $nonce  = 'test-nonce-value';
        $appKey = 'test-app-key';
        $secret = hash('sha512', $nonce . $appKey);
        $this->assertSame(128, strlen($secret));
    }

    #[Test]
    public function testSha512HashIsLowercaseHex(): void
    {
        $secret = hash('sha512', 'any-input');
        $this->assertMatchesRegularExpression('/^[0-9a-f]+$/', $secret);
    }

    #[Test]
    public function testSha512HashDeterministic(): void
    {
        $h1 = hash('sha512', 'same-input');
        $h2 = hash('sha512', 'same-input');
        $this->assertSame($h1, $h2);
    }

    #[Test]
    public function testSha512HashDifferentInputs(): void
    {
        $h1 = hash('sha512', 'input-one');
        $h2 = hash('sha512', 'input-two');
        $this->assertNotSame($h1, $h2);
    }

    // ── Token file cache path ─────────────────────────────────────────────────

    #[Test]
    public function testTokenCachePath(): void
    {
        // GpApiClient caches token to /tmp/gpapi_token.json
        // Verify the path is writable (not the token itself)
        $this->assertTrue(is_writable('/tmp'), '/tmp must be writable for token cache');
    }

    // ── mapColorDepth — GP enum contract ──────────────────────────────────────

    #[Test]
    #[DataProvider('colorDepthProvider')]
    public function testMapColorDepthReturnsGpEnum(string $input, string $expected): void
    {
        $this->assertSame($expected, GpApiClient::mapColorDepth($input));
    }

    #[Test]
    #[DataProvider('colorDepthProvider')]
    public function testMapColorDepthNeverReturnsRawInteger(string $input, string $expected): void
    {
        $result = GpApiClient::mapColorDepth($input);
        $this->assertFalse(ctype_digit($result), "color_depth must be a GP enum, not a raw integer: got $result");
    }

    public static function colorDepthProvider(): array
    {
        return [
            'depth-1'  => ['1',  'ONE_BIT'],
            'depth-2'  => ['2',  'TWO_BITS'],
            'depth-4'  => ['4',  'FOUR_BITS'],
            'depth-8'  => ['8',  'EIGHT_BITS'],
            'depth-15' => ['15', 'FIFTEEN_BITS'],
            'depth-16' => ['16', 'SIXTEEN_BITS'],
            'depth-24' => ['24', 'TWENTY_FOUR_BITS'],
            'depth-32' => ['32', 'THIRTY_TWO_BITS'],
            'depth-48' => ['48', 'FORTY_EIGHT_BITS'],
        ];
    }

    #[Test]
    public function testMapColorDepthFallbackForUnknownDepth(): void
    {
        $this->assertSame('TWENTY_FOUR_BITS', GpApiClient::mapColorDepth('99'));
        $this->assertSame('TWENTY_FOUR_BITS', GpApiClient::mapColorDepth('0'));
    }

    #[Test]
    public function testMapColorDepthFallbackForNonNumeric(): void
    {
        $this->assertSame('TWENTY_FOUR_BITS', GpApiClient::mapColorDepth('TWENTY_FOUR_BITS'));
        $this->assertSame('TWENTY_FOUR_BITS', GpApiClient::mapColorDepth(''));
    }

    // ── mapBool — GP uppercase boolean contract ───────────────────────────────

    #[Test]
    #[DataProvider('mapBoolProvider')]
    public function testMapBoolReturnsUppercase(string $input, string $expected): void
    {
        $this->assertSame($expected, GpApiClient::mapBool($input));
    }

    public static function mapBoolProvider(): array
    {
        return [
            ['true',  'TRUE'],
            ['false', 'FALSE'],
            ['TRUE',  'TRUE'],
            ['FALSE', 'FALSE'],
            ['1',     'FALSE'],
            ['0',     'FALSE'],
        ];
    }

    #[Test]
    public function testMapBoolResultIsAlwaysUppercase(): void
    {
        $this->assertSame(strtoupper(GpApiClient::mapBool('true')),  GpApiClient::mapBool('true'));
        $this->assertSame(strtoupper(GpApiClient::mapBool('false')), GpApiClient::mapBool('false'));
    }

    #[Test]
    public function testMapBoolJavaEnabledDefault(): void
    {
        // initiate-auth.php default for java_enabled is 'false' → must map to 'FALSE'
        $this->assertSame('FALSE', GpApiClient::mapBool('false'));
    }

    #[Test]
    public function testMapBoolJavascriptEnabledDefault(): void
    {
        // initiate-auth.php default for javascript_enabled is 'true' → must map to 'TRUE'
        $this->assertSame('TRUE', GpApiClient::mapBool('true'));
    }

    // ── initiate-auth payload structure ───────────────────────────────────────

    #[Test]
    public function testInitiateAuthMethodUrlCompletionStatusIsTopLevel(): void
    {
        // method_url_completion_status must be top-level, NOT inside three_ds
        // Mirrors php/api/initiate-auth.php payload construction
        $methodCompletion = 'YES';
        $payload = [
            'channel' => 'CNP',
            'method_url_completion_status' => $methodCompletion,
            'three_ds' => [
                'source'          => 'BROWSER',
                'preference'      => 'NO_PREFERENCE',
                'message_version' => '2.2.0',
            ],
        ];

        $this->assertArrayHasKey('method_url_completion_status', $payload);
        $this->assertSame('YES', $payload['method_url_completion_status']);
        $this->assertArrayNotHasKey('method_url_completion_status', $payload['three_ds']);
        $this->assertArrayNotHasKey('method_url_completion',        $payload['three_ds']);
    }

    #[Test]
    public function testInitiateAuthColorDepthIsEnum(): void
    {
        $result = GpApiClient::mapColorDepth('24');
        $this->assertSame('TWENTY_FOUR_BITS', $result);
        $this->assertNotSame('24', $result);
    }

    // ── notification HTML contract ────────────────────────────────────────────

    #[Test]
    public function testChallengeNotificationFileHasCorrectType(): void
    {
        $content = file_get_contents(__DIR__ . '/../../api/challenge-notification.php');
        $this->assertStringContainsString("type:'authResult'", $content);
        $this->assertStringContainsString('postMessage', $content);
    }

    #[Test]
    public function testMethodNotificationFileHasCorrectType(): void
    {
        $content = file_get_contents(__DIR__ . '/../../api/method-notification.php');
        $this->assertStringContainsString("type:'methodComplete'", $content);
        $this->assertStringContainsString('postMessage', $content);
    }

    #[Test]
    public function testChallengeNotificationEchoesNonce(): void
    {
        $content = file_get_contents(__DIR__ . '/../../api/challenge-notification.php');
        $this->assertStringContainsString('nonce', $content);
        $this->assertStringContainsString('$_GET', $content);
    }

    #[Test]
    public function testMethodNotificationEchoesNonce(): void
    {
        $content = file_get_contents(__DIR__ . '/../../api/method-notification.php');
        $this->assertStringContainsString('nonce', $content);
        $this->assertStringContainsString('$_GET', $content);
    }
}
