/**
 * Global Payments Drop-In UI - Sale Transaction (Node.js)
 *
 * This Express application implements Global Payments Drop-In UI integration
 * for processing Sale transactions using the official Node.js SDK.
 */

import express from 'express';
import * as dotenv from 'dotenv';
import crypto from 'crypto';
import {
    ServicesContainer,
    GpApiConfig,
    CreditCardData,
    Channel,
    ApiError
} from 'globalpayments-api';

// Load environment variables
dotenv.config();

const app = express();
const port = process.env.PORT || 8000;

// Middleware
app.use(express.static('.'));
app.use(express.urlencoded({ extended: true }));
app.use(express.json());

// CORS headers
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'POST, GET, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type');
    if (req.method === 'OPTIONS') {
        return res.sendStatus(200);
    }
    next();
});

/**
 * Generate access token for Drop-In UI (tokenization)
 * Uses PMT_POST_Create_Single permission for card tokenization
 */
app.post('/get-access-token', async (req, res) => {
    try {
        const nonce = crypto.randomBytes(16).toString('hex');
        const secret = crypto.createHash('sha512')
            .update(nonce + process.env.GP_API_APP_KEY)
            .digest('hex');

        const tokenRequest = {
            app_id: process.env.GP_API_APP_ID,
            nonce: nonce,
            secret: secret,
            grant_type: 'client_credentials',
            seconds_to_expire: 600,
            permissions: ['PMT_POST_Create_Single']
        };

        const apiEndpoint = process.env.GP_ENVIRONMENT === 'production'
            ? 'https://apis.globalpay.com/ucp/accesstoken'
            : 'https://apis.sandbox.globalpay.com/ucp/accesstoken';

        const response = await fetch(apiEndpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-GP-Version': '2021-03-22'
            },
            body: JSON.stringify(tokenRequest)
        });

        const data = await response.json();

        if (!response.ok || !data.token) {
            throw new Error(data.error_description || 'Failed to generate access token');
        }

        res.json({
            success: true,
            token: data.token,
            expiresIn: data.seconds_to_expire || 600
        });

    } catch (error) {
        res.status(500).json({
            success: false,
            message: 'Error generating access token',
            error: error.message
        });
    }
});

/**
 * Process Sale transaction using Global Payments SDK
 * Uses the payment reference from Drop-In UI to process the charge
 */
app.post('/process-sale', async (req, res) => {
    try {
        // Validate input
        if (!req.body.payment_reference) {
            throw new Error('Missing payment reference');
        }

        if (!req.body.amount || parseFloat(req.body.amount) <= 0) {
            throw new Error('Invalid amount');
        }

        const paymentReference = req.body.payment_reference;
        const amount = parseFloat(req.body.amount);
        const currency = req.body.currency || 'USD';

        // Configure Global Payments SDK
        const config = new GpApiConfig();
        config.appId = process.env.GP_API_APP_ID;
        config.appKey = process.env.GP_API_APP_KEY;
        config.environment = process.env.GP_ENVIRONMENT === 'production'
            ? 'production'
            : 'test';
        config.channel = Channel.CardNotPresent;
        config.country = 'US';

        // Note: Don't set account name - let SDK auto-detect

        // Configure the service
        ServicesContainer.configureService(config);

        // Create card data from payment reference token
        const card = new CreditCardData();
        card.token = paymentReference;

        // Process the charge
        const response = await card.charge(amount)
            .withCurrency(currency)
            .execute();

        // Check response
        if (response.responseCode === '00' || response.responseCode === 'SUCCESS') {
            res.json({
                success: true,
                message: 'Payment successful!',
                data: {
                    transactionId: response.transactionId,
                    status: response.responseMessage,
                    amount: amount,
                    currency: currency,
                    reference: response.referenceNumber || '',
                    timestamp: new Date().toISOString()
                }
            });
        } else {
            throw new Error('Transaction declined: ' + (response.responseMessage || 'Unknown error'));
        }

    } catch (error) {
        res.status(400).json({
            success: false,
            message: 'Payment processing failed',
            error: error.message
        });
    }
});

// Start server
app.listen(port, '0.0.0.0', () => {
    console.log(`✅ Server running at http://localhost:${port}`);
    console.log(`Environment: ${process.env.GP_ENVIRONMENT || 'sandbox'}`);
});
