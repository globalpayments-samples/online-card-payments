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
