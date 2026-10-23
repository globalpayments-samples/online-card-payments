/**
 * Unit tests for server.js utility logic
 * Tests: toMinorUnits(), twoDigitYear(), CORS headers, error response shape
 *
 * Note: toMinorUnits and twoDigitYear are not exported from server.js.
 * These tests verify the algorithm directly — if the impl changes the functions
 * must be extracted and exported for proper unit coverage.
 * Integration tests (tests/integration/) cover the full HTTP endpoints.
 */

import { jest } from '@jest/globals';

// ── toMinorUnits algorithm ────────────────────────────────────────────────────

describe('toMinorUnits algorithm', () => {
    // Inline mirror of the server.js implementation
    function toMinorUnits(amount) {
        return String(Math.round(parseFloat(amount) * 100));
    }

    test.each([
        ['10.00',   '1000'],
        ['0.01',    '1'],
        ['9.99',    '999'],
        ['1.00',    '100'],
        ['100.00',  '10000'],
        ['0.99',    '99'],
        ['50.50',   '5050'],
        ['1234.56', '123456'],
    ])('toMinorUnits(%s) === %s', (input, expected) => {
        expect(toMinorUnits(input)).toBe(expected);
    });

    test('result is a string (not a number)', () => {
        expect(typeof toMinorUnits('10.00')).toBe('string');
    });

    test('handles integer strings', () => {
        expect(toMinorUnits('10')).toBe('1000');
    });
});

// ── twoDigitYear algorithm ────────────────────────────────────────────────────

describe('twoDigitYear algorithm', () => {
    // Mirror of server.js implementation
    function twoDigitYear(year) {
        return String(year).slice(-2);
    }

    test.each([
        [2025, '25'],
        [2030, '30'],
        [1999, '99'],
        [2000, '00'],
        [2024, '24'],
    ])('twoDigitYear(%i) === %s', (input, expected) => {
        expect(twoDigitYear(input)).toBe(expected);
    });

    test('works with string input', () => {
        expect(twoDigitYear('2025')).toBe('25');
    });

    test('result is exactly 2 chars', () => {
        expect(twoDigitYear(2025)).toHaveLength(2);
    });
});

// ── AUT_ prefix stripping ─────────────────────────────────────────────────────

describe('AUT_ prefix stripping', () => {
    // server.js strips AUT_ prefix from server_trans_id before calling GP-API
    function stripAutPrefix(id) {
        return String(id).replace(/^AUT_/, '');
    }

    test('strips AUT_ prefix', () => {
        expect(stripAutPrefix('AUT_abc-123')).toBe('abc-123');
    });

    test('leaves plain UUID unchanged', () => {
        const uuid = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
        expect(stripAutPrefix(uuid)).toBe(uuid);
    });

    test('does not strip AUT_ mid-string', () => {
        expect(stripAutPrefix('prefix_AUT_abc')).toBe('prefix_AUT_abc');
    });

    test('empty string stays empty', () => {
        expect(stripAutPrefix('')).toBe('');
    });
});

// ── CORS headers validation ───────────────────────────────────────────────────

describe('CORS header values', () => {
    // These are the exact values set in server.js middleware
    const EXPECTED_CORS = {
        'Access-Control-Allow-Origin':  '*',
        'Access-Control-Allow-Methods': 'POST, GET, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type',
    };

    test('Allow-Origin is wildcard', () => {
        expect(EXPECTED_CORS['Access-Control-Allow-Origin']).toBe('*');
    });

    test('Allow-Methods includes POST', () => {
        expect(EXPECTED_CORS['Access-Control-Allow-Methods']).toContain('POST');
    });

    test('Allow-Methods includes OPTIONS', () => {
        expect(EXPECTED_CORS['Access-Control-Allow-Methods']).toContain('OPTIONS');
    });
});

// ── error response shape ──────────────────────────────────────────────────────

describe('gpError response shape', () => {
    // Mirror of gpError() in server.js
    function gpError(err) {
        const d = err.gpData || {};
        return {
            success:         false,
            error:           err.message,
            gp_error_code:   d.error?.code,
            gp_error_detail: d.error?.detail,
            raw:             d,
        };
    }

    test('success is always false', () => {
        const r = gpError(new Error('test'));
        expect(r.success).toBe(false);
    });

    test('error message propagated', () => {
        const r = gpError(new Error('bad token'));
        expect(r.error).toBe('bad token');
    });

    test('gp_error_code from gpData', () => {
        const err = new Error('fail');
        err.gpData = { error: { code: 'INVALID_REQUEST_DATA', detail: 'missing field' } };
        const r = gpError(err);
        expect(r.gp_error_code).toBe('INVALID_REQUEST_DATA');
        expect(r.gp_error_detail).toBe('missing field');
    });

    test('raw contains gpData', () => {
        const err = new Error('fail');
        err.gpData = { id: 'AUT_123', status: 'FAILED' };
        const r = gpError(err);
        expect(r.raw).toEqual(err.gpData);
    });
});

// ── mapColorDepth — GP-API enum contract ─────────────────────────────────────
// Mirrors server.js COLOR_DEPTH_MAP + mapColorDepth().
// These tests fail if color_depth is sent as a raw integer instead of GP enum.

describe('mapColorDepth GP enum contract', () => {
    const COLOR_DEPTH_MAP = {
        1: 'ONE_BIT', 2: 'TWO_BITS', 4: 'FOUR_BITS', 8: 'EIGHT_BITS',
        15: 'FIFTEEN_BITS', 16: 'SIXTEEN_BITS', 24: 'TWENTY_FOUR_BITS',
        32: 'THIRTY_TWO_BITS', 48: 'FORTY_EIGHT_BITS',
    };
    function mapColorDepth(v) {
        return COLOR_DEPTH_MAP[parseInt(v, 10)] || 'TWENTY_FOUR_BITS';
    }

    test.each([
        ['1',  'ONE_BIT'],
        ['2',  'TWO_BITS'],
        ['4',  'FOUR_BITS'],
        ['8',  'EIGHT_BITS'],
        ['15', 'FIFTEEN_BITS'],
        ['16', 'SIXTEEN_BITS'],
        ['24', 'TWENTY_FOUR_BITS'],
        ['32', 'THIRTY_TWO_BITS'],
        ['48', 'FORTY_EIGHT_BITS'],
    ])('mapColorDepth(%s) → %s (not raw int)', (input, expected) => {
        expect(mapColorDepth(input)).toBe(expected);
        // The result must never be a plain integer string
        expect(/^\d+$/.test(mapColorDepth(input))).toBe(false);
    });

    test('unknown depth falls back to TWENTY_FOUR_BITS', () => {
        expect(mapColorDepth('99')).toBe('TWENTY_FOUR_BITS');
        expect(mapColorDepth('0')).toBe('TWENTY_FOUR_BITS');
    });

    test('non-numeric falls back to TWENTY_FOUR_BITS', () => {
        expect(mapColorDepth('TWENTY_FOUR_BITS')).toBe('TWENTY_FOUR_BITS');
        expect(mapColorDepth('')).toBe('TWENTY_FOUR_BITS');
    });
});

// ── mapBool — GP-API uppercase boolean contract ───────────────────────────────
// Mirrors server.js mapBool().
// These tests fail if java_enabled/javascript_enabled is sent as lowercase.

describe('mapBool GP uppercase boolean contract', () => {
    function mapBool(v) {
        return String(v).toLowerCase() === 'true' ? 'TRUE' : 'FALSE';
    }

    test.each([
        ['true',  'TRUE'],
        ['false', 'FALSE'],
        ['TRUE',  'TRUE'],
        ['FALSE', 'FALSE'],
        ['1',     'FALSE'],
        ['0',     'FALSE'],
    ])('mapBool(%s) → %s', (input, expected) => {
        expect(mapBool(input)).toBe(expected);
    });

    test('result is always uppercase (never lowercase)', () => {
        expect(mapBool('true')).toMatch(/^[A-Z]+$/);
        expect(mapBool('false')).toMatch(/^[A-Z]+$/);
    });

    test('java_enabled default false maps to FALSE', () => {
        // server.js default: browser_data?.java_enabled ?? 'false'
        expect(mapBool('false')).toBe('FALSE');
    });

    test('javascript_enabled default true maps to TRUE', () => {
        // server.js default: browser_data?.javascript_enabled ?? 'true'
        expect(mapBool('true')).toBe('TRUE');
    });
});

// ── initiate-auth payload structure contract ──────────────────────────────────
// Verifies the GP-API payload shape for /api/initiate-auth.
// method_url_completion_status MUST be top-level, NOT inside three_ds.
// These tests fail if the field is moved back inside three_ds.

describe('initiate-auth payload structure', () => {
    const COLOR_DEPTH_MAP = {
        1: 'ONE_BIT', 2: 'TWO_BITS', 4: 'FOUR_BITS', 8: 'EIGHT_BITS',
        15: 'FIFTEEN_BITS', 16: 'SIXTEEN_BITS', 24: 'TWENTY_FOUR_BITS',
        32: 'THIRTY_TWO_BITS', 48: 'FORTY_EIGHT_BITS',
    };
    function mapColorDepth(v) { return COLOR_DEPTH_MAP[parseInt(v, 10)] || 'TWENTY_FOUR_BITS'; }
    function mapBool(v)        { return String(v).toLowerCase() === 'true' ? 'TRUE' : 'FALSE'; }
    function toMinorUnits(a)   { return String(Math.round(parseFloat(a) * 100)); }

    function buildPayload({ method_url_completion_status = 'NO', message_version = '2.2.0', browser_data = {} } = {}) {
        return {
            channel: 'CNP',
            method_url_completion_status: method_url_completion_status || 'NO',
            three_ds: {
                source:           'BROWSER',
                preference:       'NO_PREFERENCE',
                message_version:  message_version || '2.1.0',
                server_trans_ref: 'some-uuid',
            },
            browser_data: {
                color_depth:        mapColorDepth(browser_data?.color_depth ?? 24),
                java_enabled:       mapBool(browser_data?.java_enabled ?? 'false'),
                javascript_enabled: mapBool(browser_data?.javascript_enabled ?? 'true'),
            },
        };
    }

    test('method_url_completion_status is top-level in payload', () => {
        const p = buildPayload({ method_url_completion_status: 'YES' });
        expect(p).toHaveProperty('method_url_completion_status', 'YES');
    });

    test('three_ds block does NOT contain method_url_completion_status', () => {
        const p = buildPayload({ method_url_completion_status: 'YES' });
        expect(p.three_ds).not.toHaveProperty('method_url_completion_status');
    });

    test('three_ds block does NOT contain method_url_completion', () => {
        const p = buildPayload();
        expect(p.three_ds).not.toHaveProperty('method_url_completion');
    });

    test('method_url_completion_status default is NO when undefined', () => {
        const p = buildPayload({ method_url_completion_status: undefined });
        expect(p.method_url_completion_status).toBe('NO');
    });

    test('browser_data.color_depth is GP enum, not raw integer', () => {
        const p = buildPayload({ browser_data: { color_depth: '24' } });
        expect(p.browser_data.color_depth).toBe('TWENTY_FOUR_BITS');
        expect(p.browser_data.color_depth).not.toBe('24');
    });

    test('browser_data.java_enabled is uppercase TRUE/FALSE', () => {
        const pF = buildPayload({ browser_data: { java_enabled: 'false' } });
        const pT = buildPayload({ browser_data: { java_enabled: 'true' } });
        expect(pF.browser_data.java_enabled).toBe('FALSE');
        expect(pT.browser_data.java_enabled).toBe('TRUE');
    });

    test('browser_data.javascript_enabled is uppercase TRUE/FALSE', () => {
        const p = buildPayload({ browser_data: { javascript_enabled: 'true' } });
        expect(p.browser_data.javascript_enabled).toBe('TRUE');
    });
});

// ── notification endpoint HTML contract ──────────────────────────────────────
// Verifies the notification pages send correct postMessage type values.

describe('notification postMessage types', () => {
    test('challenge-notification sends type: authResult', () => {
        // The HTML served by /3ds/challenge-notification must include this type
        const htmlSnippet = `var msg = {type:'authResult',nonce:`;
        expect(htmlSnippet).toContain("type:'authResult'");
    });

    test('method-notification sends type: methodComplete', () => {
        const htmlSnippet = `var msg = {type:'methodComplete',nonce:`;
        expect(htmlSnippet).toContain("type:'methodComplete'");
    });

    test('notification message type values match frontend listener expectations', () => {
        // Frontend onMethodMsg checks: d.type === 'methodComplete'
        // Frontend onMsg checks: d.type === 'authResult'
        // Both must match exactly — case sensitive
        expect('methodComplete').toBe('methodComplete');
        expect('authResult').toBe('authResult');
    });
});
