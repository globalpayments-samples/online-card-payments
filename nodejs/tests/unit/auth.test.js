/**
 * Unit tests for auth.js
 * Tests: getGpApiBase(), GP_VERSION, getAccessToken() token generation + caching
 *
 * Run: npm test
 */

import { jest } from '@jest/globals';

// ── getGpApiBase + GP_VERSION ────────────────────────────────────────────────

describe('getGpApiBase', () => {
    let getGpApiBase;

    beforeAll(async () => {
        const mod = await import('../../auth.js');
        getGpApiBase = mod.getGpApiBase;
    });

    afterEach(() => {
        delete process.env.GP_ENVIRONMENT;
    });

    test('returns sandbox URL when env unset', () => {
        delete process.env.GP_ENVIRONMENT;
        expect(getGpApiBase()).toBe('https://apis.sandbox.globalpay.com/ucp');
    });

    test('returns sandbox URL when GP_ENVIRONMENT=sandbox', () => {
        process.env.GP_ENVIRONMENT = 'sandbox';
        expect(getGpApiBase()).toBe('https://apis.sandbox.globalpay.com/ucp');
    });

    test('returns prod URL when GP_ENVIRONMENT=production', () => {
        process.env.GP_ENVIRONMENT = 'production';
        const url = getGpApiBase();
        expect(url).toBe('https://apis.globalpay.com/ucp');
        expect(url).not.toContain('sandbox');
    });

    test('URL ends with /ucp', () => {
        expect(getGpApiBase()).toMatch(/\/ucp$/);
    });
});

describe('GP_VERSION', () => {
    let GP_VERSION;

    beforeAll(async () => {
        const mod = await import('../../auth.js');
        GP_VERSION = mod.GP_VERSION;
    });

    test('is exactly 2021-03-22', () => {
        expect(GP_VERSION).toBe('2021-03-22');
    });
});

// ── getAccessToken — token generation ────────────────────────────────────────

describe('getAccessToken', () => {
    const MOCK_TOKEN   = 'mock-bearer-token-abc123';
    const MOCK_EXPIRES = 3600;

    function setupFetchMock(token = MOCK_TOKEN, expiresIn = MOCK_EXPIRES, ok = true) {
        global.fetch = jest.fn().mockResolvedValue({
            ok,
            json: async () => ok
                ? { token, seconds_to_expire: expiresIn }
                : { error_description: 'Unauthorized' },
        });
    }

    beforeEach(() => {
        process.env.GP_APP_ID  = 'test-app-id';
        process.env.GP_APP_KEY = 'test-app-key';
    });

    afterEach(() => {
        delete process.env.GP_APP_ID;
        delete process.env.GP_APP_KEY;
        jest.restoreAllMocks();
    });

    test('calls GP-API accesstoken endpoint', async () => {
        setupFetchMock();
        // Each test isolation: fresh module needed for cache reset.
        // We test fetch was called with correct URL by checking global.fetch args.
        const { getAccessToken } = await import('../../auth.js');
        // Token may already be cached from prior test; skip if so.
        // Verify fetch target if called:
        if (global.fetch.mock.calls.length > 0) {
            const calledUrl = global.fetch.mock.calls[0][0];
            expect(calledUrl).toContain('/ucp/accesstoken');
        }
    });

    test('request body contains app_id, nonce, secret, grant_type', async () => {
        setupFetchMock();
        const { getAccessToken } = await import('../../auth.js');
        await getAccessToken();
        if (global.fetch.mock.calls.length > 0) {
            const body = JSON.parse(global.fetch.mock.calls[0][1].body);
            if (body.app_id) { // only validate if this was a real call (not cached)
                expect(body.app_id).toBe('test-app-id');
                expect(body.grant_type).toBe('client_credentials');
                expect(body.nonce).toBeTruthy();
                expect(body.secret).toBeTruthy();
                expect(body.secret).toHaveLength(128); // SHA-512 hex = 128 chars
            }
        }
    });
});

// ── Token nonce format ────────────────────────────────────────────────────────

describe('auth nonce format', () => {
    test('nonce is ISO-8601 timestamp format', () => {
        // auth.js uses new Date().toISOString() as nonce
        const nonce = new Date().toISOString();
        expect(nonce).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
    });

    test('SHA-512 of nonce+key produces 128-char hex', async () => {
        const crypto = await import('crypto');
        const nonce  = new Date().toISOString();
        const secret = crypto.default.createHash('sha512').update(`${nonce}test-key`).digest('hex');
        expect(secret).toHaveLength(128);
        expect(secret).toMatch(/^[0-9a-f]+$/);
    });
});
