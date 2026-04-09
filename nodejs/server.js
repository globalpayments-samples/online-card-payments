/**
 * Global Payments 3DS2 Backend — Node.js / Express
 *
 * Endpoints:
 *   POST /get-access-token     — tokenization token for Drop-In UI
 *   POST /api/check-enrollment — Step 1: 3DS2 enrollment check
 *   POST /api/initiate-auth    — Step 3: initiate authentication with browser data
 *   POST /api/get-auth-result  — Step 5: retrieve final auth result
 *   POST /api/authorize-payment — Step 6: SALE with 3DS2 proof
 *
 * Uses payment_method.id (PMT token from Drop-In UI) instead of raw card numbers.
 */

import express from 'express';
import * as dotenv from 'dotenv';
import crypto from 'crypto';
import { randomUUID } from 'crypto';
import { getAccessToken, getGpApiBase, GP_VERSION } from './auth.js';

dotenv.config();

const app  = express();
const port = process.env.PORT || 8000;

app.use(express.static('.'));
app.use(express.urlencoded({ extended: true }));
app.use(express.json());

app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin',  '*');
    res.header('Access-Control-Allow-Methods', 'POST, GET, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type');
    if (req.method === 'OPTIONS') return res.sendStatus(204);
    next();
});

// ─── Helpers ─────────────────────────────────────────────────────────────────

async function gpRequest(method, path, body) {
    const token = await getAccessToken();
    const url   = `${getGpApiBase()}${path}`;
    const opts  = {
        method,
        headers: {
            'Content-Type':  'application/json',
            'X-GP-Version':  GP_VERSION,
            'Authorization': `Bearer ${token}`,
        },
    };
    if (body) opts.body = JSON.stringify(body);

    const resp = await fetch(url, opts);
    const data = await resp.json();

    if (!resp.ok) {
        const err    = new Error(data.error?.message || `GP-API error ${resp.status}`);
        err.gpData   = data;
        err.status   = resp.status;
        throw err;
    }
    return data;
}

function toMinorUnits(amount) {
    return String(Math.round(parseFloat(amount) * 100));
}

function twoDigitYear(year) {
    return String(year).slice(-2);
}

function gpError(res, err) {
    const d = err.gpData || {};
    return res.status(err.status || 500).json({
        success:         false,
        error:           err.message,
        gp_error_code:   d.error?.code,
        gp_error_detail: d.error?.detail,
        raw:             d,
    });
}

// ─── Routes ──────────────────────────────────────────────────────────────────

/**
 * POST /get-access-token
 * Generates a tokenization access token for the Drop-In UI.
 * Uses PMT_POST_Create_Single permission so the frontend can tokenize the card.
 */
app.post('/get-access-token', async (req, res) => {
    try {
        const appId  = process.env.GP_APP_ID;
        const appKey = process.env.GP_APP_KEY;
        if (!appId || !appKey) throw new Error('GP_APP_ID and GP_APP_KEY must be set');

        const nonce  = crypto.randomBytes(16).toString('hex');
        const secret = crypto.createHash('sha512').update(nonce + appKey).digest('hex');

        const apiEndpoint = process.env.GP_ENVIRONMENT === 'production'
            ? 'https://apis.globalpay.com/ucp/accesstoken'
            : 'https://apis.sandbox.globalpay.com/ucp/accesstoken';

        const response = await fetch(apiEndpoint, {
            method:  'POST',
            headers: { 'Content-Type': 'application/json', 'X-GP-Version': GP_VERSION },
            body:    JSON.stringify({
                app_id:           appId,
                nonce,
                secret,
                grant_type:       'client_credentials',
                seconds_to_expire: 600,
                permissions:      ['PMT_POST_Create_Single'],
            }),
        });

        const data = await response.json();
        if (!response.ok || !data.token) {
            throw new Error(data.error_description || 'Failed to generate access token');
        }

        res.json({ success: true, token: data.token, expiresIn: data.seconds_to_expire || 600 });
    } catch (err) {
        res.status(500).json({ success: false, message: 'Error generating access token', error: err.message });
    }
});

/**
 * POST /api/check-enrollment
 * Step 1 — Check if payment method is enrolled in 3DS2.
 * Uses payment_method.id (PMT token from Drop-In UI) instead of raw card data.
 */
app.post('/api/check-enrollment', async (req, res) => {
    try {
        const { payment_token } = req.body;
        if (!payment_token) return res.status(400).json({ success: false, error: 'payment_token is required' });

        const payload = {
            account_name: process.env.GP_ACCOUNT_NAME || 'transaction_processing',
            account_id:   process.env.GP_ACCOUNT_ID,
            merchant_id:  process.env.GP_MERCHANT_ID,
            channel:      'CNP',
            country:      'GB',
            amount:       '1000',
            currency:     'GBP',
            reference:    randomUUID(),
            payment_method: {
                entry_mode: 'ECOM',
                id:         payment_token,
            },
            three_ds: {
                source:          'BROWSER',
                preference:      'NO_PREFERENCE',
                message_version: '2.2.0',
            },
            notifications: {
                challenge_return_url:       process.env.CHALLENGE_NOTIFICATION_URL,
                three_ds_method_return_url: process.env.METHOD_NOTIFICATION_URL,
            },
        };

        const raw = await gpRequest('POST', '/authentications', payload);

        const methodUrl  = raw.three_ds?.method_url || null;
        const methodData = methodUrl
            ? Buffer.from(JSON.stringify({
                threeDSServerTransID:  raw.id,
                methodNotificationURL: process.env.METHOD_NOTIFICATION_URL,
              })).toString('base64')
            : null;

        res.json({
            success: true,
            data: {
                server_trans_id:  raw.id,
                server_trans_ref: raw.three_ds?.server_trans_ref,
                enrolled:         raw.three_ds?.enrolled_status,
                message_version:  raw.three_ds?.message_version,
                method_url:       methodUrl,
                method_data:      methodData,
            },
            raw,
        });
    } catch (err) {
        gpError(res, err);
    }
});

/**
 * POST /api/initiate-auth
 * Step 3 — Initiate authentication with browser data.
 * Uses payment_method.id (PMT token) instead of raw card data.
 */
app.post('/api/initiate-auth', async (req, res) => {
    try {
        const {
            payment_token,
            server_trans_id,
            message_version,
            method_url_completion,
            browser_data,
            order,
        } = req.body;

        if (!payment_token)   return res.status(400).json({ success: false, error: 'payment_token is required' });
        if (!server_trans_id) return res.status(400).json({ success: false, error: 'server_trans_id is required' });

        // GP-API expects plain UUID — strip AUT_ prefix if present
        const serverTransRef = String(server_trans_id).replace(/^AUT_/, '');

        const amount   = order?.amount   || '10.00';
        const currency = order?.currency || 'GBP';

        const payload = {
            account_name: process.env.GP_ACCOUNT_NAME || 'transaction_processing',
            account_id:   process.env.GP_ACCOUNT_ID,
            merchant_id:  process.env.GP_MERCHANT_ID,
            channel:      'CNP',
            country:      'GB',
            amount:       toMinorUnits(amount),
            currency,
            reference:    randomUUID(),
            payment_method: {
                entry_mode: 'ECOM',
                id:         payment_token,
            },
            three_ds: {
                source:                'BROWSER',
                preference:            'NO_PREFERENCE',
                message_version:       message_version || '2.1.0',
                server_trans_ref:      serverTransRef,
                method_url_completion: method_url_completion || 'UNAVAILABLE',
            },
            order: {
                amount:            toMinorUnits(amount),
                currency,
                reference:         randomUUID(),
                address_indicator: false,
                date_time_created: new Date().toISOString(),
            },
            payer: {
                email: 'test@example.com',
                billing_address: {
                    line1:       '1 Test Street',
                    city:        'London',
                    postal_code: 'SW1A 1AA',
                    country:     '826',
                },
            },
            browser_data: {
                accept_header:         browser_data?.accept_header         || 'text/html,application/xhtml+xml',
                color_depth:           String(browser_data?.color_depth    || '24'),
                ip:                    browser_data?.ip                    || '123.123.123.123',
                java_enabled:          String(browser_data?.java_enabled   ?? 'false'),
                javascript_enabled:    String(browser_data?.javascript_enabled ?? 'true'),
                language:              browser_data?.language              || 'en-GB',
                screen_height:         String(browser_data?.screen_height  || '1080'),
                screen_width:          String(browser_data?.screen_width   || '1920'),
                challenge_window_size: browser_data?.challenge_window_size || 'FULL_SCREEN',
                timezone:              String(browser_data?.timezone       || '0'),
                user_agent:            browser_data?.user_agent            || 'Mozilla/5.0',
            },
            notifications: {
                challenge_return_url:       process.env.CHALLENGE_NOTIFICATION_URL,
                three_ds_method_return_url: process.env.METHOD_NOTIFICATION_URL,
            },
        };

        const raw = await gpRequest('POST', '/authentications', payload);

        res.json({
            success: true,
            data: {
                server_trans_id:      raw.id,
                status:               raw.status,
                acs_reference_number: raw.three_ds?.acs_reference_number,
                acs_trans_id:         raw.three_ds?.acs_trans_id,
                acs_signed_content:   raw.three_ds?.acs_signed_content,
                acs_challenge_url:    raw.three_ds?.acs_challenge_url || raw.three_ds?.challenge_value || null,
            },
            raw,
        });
    } catch (err) {
        gpError(res, err);
    }
});

/**
 * POST /api/get-auth-result
 * Step 5 — Retrieve final authentication result (ECI, auth value, etc.)
 */
app.post('/api/get-auth-result', async (req, res) => {
    try {
        const { server_trans_id } = req.body;
        if (!server_trans_id) return res.status(400).json({ success: false, error: 'server_trans_id is required' });

        const transRef  = String(server_trans_id).replace(/^AUT_/, '');
        const accountQs = new URLSearchParams({
            account_name: process.env.GP_ACCOUNT_NAME || 'transaction_processing',
            account_id:   process.env.GP_ACCOUNT_ID   || '',
            merchant_id:  process.env.GP_MERCHANT_ID  || '',
        }).toString();

        const raw = await gpRequest('GET', `/authentications/${transRef}?${accountQs}`);

        res.json({
            success: true,
            data: {
                status:               raw.status,
                eci:                  raw.three_ds?.eci,
                authentication_value: raw.three_ds?.authentication_value,
                ds_trans_ref:         raw.three_ds?.ds_trans_ref,
                message_version:      raw.three_ds?.message_version,
                server_trans_ref:     raw.three_ds?.server_trans_ref || raw.id,
            },
            raw,
        });
    } catch (err) {
        gpError(res, err);
    }
});

/**
 * POST /api/authorize-payment
 * Step 6 — SALE transaction using payment token + 3DS2 authentication proof.
 * Uses payment_method.id (PMT token) instead of raw card data.
 */
app.post('/api/authorize-payment', async (req, res) => {
    try {
        const { payment_token, amount, currency, three_ds } = req.body;
        if (!payment_token) return res.status(400).json({ success: false, error: 'payment_token is required' });

        const payload = {
            account_name: process.env.GP_ACCOUNT_NAME || 'transaction_processing',
            account_id:   process.env.GP_ACCOUNT_ID,
            merchant_id:  process.env.GP_MERCHANT_ID,
            channel:      'CNP',
            type:         'SALE',
            amount:       toMinorUnits(amount || '10.00'),
            currency:     currency || 'GBP',
            reference:    randomUUID(),
            country:      'GB',
            payment_method: {
                entry_mode: 'ECOM',
                id:         payment_token,
            },
            three_ds: {
                source:               'BROWSER',
                authentication_value: three_ds?.authentication_value,
                server_trans_ref:     three_ds?.server_trans_ref,
                ds_trans_ref:         three_ds?.ds_trans_ref,
                eci:                  three_ds?.eci,
                message_version:      three_ds?.message_version || '2.2.0',
            },
        };

        const raw = await gpRequest('POST', '/transactions', payload);

        res.json({
            success: true,
            data: {
                transaction_id: raw.id,
                status:         raw.status,
                result_code:    raw.action?.result_code,
                amount:         raw.amount,
                currency:       raw.currency,
            },
            raw,
        });
    } catch (err) {
        gpError(res, err);
    }
});

// ─── Start ────────────────────────────────────────────────────────────────────

app.listen(port, '0.0.0.0', () => {
    console.log(`Server running at http://localhost:${port}`);
    console.log(`Environment: ${process.env.GP_ENVIRONMENT || 'sandbox'}`);
});
