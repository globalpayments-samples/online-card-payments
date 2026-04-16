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
}
