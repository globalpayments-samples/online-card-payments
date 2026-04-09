/**
 * GP-API Token Cache
 *
 * Manages OAuth2 bearer token generation and caching for backend API calls.
 * Regenerates automatically when within 60 seconds of expiry.
 * Uses ISO-8601 timestamp as nonce (required for transaction_processing account).
 */

import crypto from 'crypto';

const GP_VERSION = '2021-03-22';

export function getGpApiBase() {
    return process.env.GP_ENVIRONMENT === 'production'
        ? 'https://apis.globalpay.com/ucp'
        : 'https://apis.sandbox.globalpay.com/ucp';
}

let cachedToken    = null;
let tokenExpiresAt = 0; // epoch ms

async function generateToken() {
    const appId  = process.env.GP_APP_ID;
    const appKey = process.env.GP_APP_KEY;

    if (!appId || !appKey) throw new Error('GP_APP_ID and GP_APP_KEY must be set');

    const nonce  = new Date().toISOString();
    const secret = crypto.createHash('sha512').update(`${nonce}${appKey}`).digest('hex');

    const response = await fetch(`${getGpApiBase()}/accesstoken`, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json', 'X-GP-Version': GP_VERSION },
        body:    JSON.stringify({ app_id: appId, nonce, secret, grant_type: 'client_credentials' }),
    });

    if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(`Token generation failed (${response.status}): ${JSON.stringify(err)}`);
    }

    const data     = await response.json();
    cachedToken    = data.token;
    tokenExpiresAt = Date.now() + (data.seconds_to_expire - 60) * 1000;

    return cachedToken;
}

export async function getAccessToken() {
    if (cachedToken && Date.now() < tokenExpiresAt) return cachedToken;
    return generateToken();
}

export { GP_VERSION };
