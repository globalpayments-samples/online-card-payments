# Global Payments Drop-In UI - Sale Transaction (PHP)

Complete implementation of Global Payments Drop-In UI for processing Sale transactions using the official PHP SDK.

## Quick Start

### 1. Get Credentials
- Register at [Global Payments Developer Portal](https://developer.globalpay.com/)
- Create a new application
- Copy your **APP_ID** and **APP_KEY**

### 2. Configure
```bash
# Copy sample environment file
cp .env.sample .env

# Edit .env file and add your credentials:
GP_APP_ID=your_app_id_here
GP_APP_KEY=your_app_key_here
GP_ENVIRONMENT=sandbox

# IMPORTANT: Leave GP_ACCOUNT_NAME commented out (SDK auto-detects)
```

### 3. Install Dependencies
```bash
composer install
```

### 4. Run Server
```bash
php -S localhost:8000
```

### 5. Test Payment
1. Open http://localhost:8000
2. Use test card: **4263 9826 4026 9299**
3. CVV: **123**, Expiry: any future date
4. Click **SUBMIT**

## Test Cards (Sandbox)

| Brand | Card Number | CVV | Expiry |
|-------|-------------|-----|--------|
| Visa | 4263 9826 4026 9299 | 123 | Any future |
| Visa | 4263 9700 0000 5262 | 123 | Any future |
| Mastercard | 5425 2334 2424 1200 | 123 | Any future |
| Discover | 6011 0000 0000 0012 | 123 | Any future |

## Requirements

- PHP 7.4 or later
- cURL extension
- JSON extension
- Composer
- Global Payments account with APP_ID and APP_KEY

## Dependencies

This implementation uses:
- **globalpayments/php-sdk** (v13.4.0+) - Official Global Payments PHP SDK
- **vlucas/phpdotenv** - Environment variable management

## Implementation Files

### Core Files
- **index.html** - Payment form with Drop-In UI integration
- **get-access-token.php** - Generates access tokens for Drop-In UI (tokenization)
- **process-sale.php** - Processes Sale transactions using PHP SDK
- **.env** - Configuration (copy from .env.sample)
- **composer.json** - Dependencies

## How It Works

### Payment Flow

1. **Page Load**: Frontend requests tokenization token from `get-access-token.php`
2. **Initialize**: Drop-In UI initializes with the access token
3. **Card Entry**: Customer enters card details (stays in browser, never touches your server)
4. **Tokenization**: Drop-In UI creates payment reference token
5. **Process**: Payment reference sent to `process-sale.php`
6. **SDK Transaction**: PHP SDK handles authentication and processes Sale transaction
7. **Result**: Success or error message displayed

### Architecture

```
Browser → get-access-token.php → GP API (Tokenization Token)
  ↓
Drop-In UI (Card Tokenization)
  ↓
Browser → process-sale.php → PHP SDK → GP API (Sale Transaction)
  ↓
Success/Error
```

## API Endpoints

### POST /get-access-token.php

Generates access token with `PMT_POST_Create_Single` permission for Drop-In UI tokenization.

**Response:**
```json
{
  "success": true,
  "token": "access_token_string",
  "expiresIn": 600
}
```

### POST /process-sale.php

Processes Sale transaction using payment reference via PHP SDK.

**Request:**
```json
{
  "payment_reference": "PMT_xxxxx",
  "amount": 10.00,
  "currency": "USD"
}
```

**Response (Success):**
```json
{
  "success": true,
  "message": "Payment successful!",
  "data": {
    "transactionId": "TRN_xxxxx",
    "status": "CAPTURED",
    "amount": 10.00,
    "currency": "USD",
    "reference": "xxxxx",
    "timestamp": "2025-01-15 10:30:45"
  }
}
```

## Configuration

### Environment Variables

```env
# Application credentials (REQUIRED)
GP_APP_ID=your_app_id_here          # From developer dashboard
GP_APP_KEY=your_app_key_here        # From developer dashboard

# Environment: sandbox or production
GP_ENVIRONMENT=sandbox

# Account Name (OPTIONAL - RECOMMENDED TO LEAVE COMMENTED OUT)
# The SDK automatically detects the correct account from your credentials
# Only uncomment if you have multiple accounts and need to specify one
# GP_ACCOUNT_NAME=Transaction_Processing
```

### ⚠️ Important Configuration Notes

1. **Do NOT manually set `GP_ACCOUNT_NAME`** unless you have multiple accounts
2. The PHP SDK automatically detects the correct account from your APP_ID/APP_KEY
3. Manually setting the account name can cause "Access token and merchant info do not match" errors
4. Let the SDK handle account selection for best compatibility

## Testing

### Manual Test Flow

1. Start server: `php -S localhost:8000`
2. Open http://localhost:8000
3. Enter amount (e.g., 10.00)
4. Enter test card: **4263 9826 4026 9299**
5. CVV: **123**, Expiry: **12/29**
6. Click **SUBMIT**
7. Verify success message with transaction ID

### Expected Results

**Success:**
```
✓ Success! Payment Successful! Transaction ID: TRN_xxxxx
```

**Common Errors:**

| Error | Cause | Solution |
|-------|-------|----------|
| Failed to get access token | Invalid credentials | Verify GP_APP_ID and GP_APP_KEY in .env |
| Access token and merchant info do not match | Account name mismatch | Comment out GP_ACCOUNT_NAME in .env |
| Drop-In UI not loading | Server not running | Run `php -S localhost:8000` |
| Transaction declined | Invalid card/amount | Use test cards, check amount > 0 |
| CORS errors | Wrong URL | Access via http://localhost:8000 |

### cURL Testing

Test access token generation:
```bash
curl -X POST http://localhost:8000/get-access-token.php
```

## Key Features

- ✅ **Official PHP SDK** - Uses globalpayments/php-sdk for reliability
- ✅ **PCI SAQ A Compliant** - Card data never touches your server
- ✅ **Pre-built UI** - Drop-In UI handles card input and validation
- ✅ **Sale Transactions** - Authorization + Capture in one step
- ✅ **Auto-Configuration** - SDK auto-detects account settings
- ✅ **Mobile Responsive** - Works on all devices
- ✅ **Secure** - Token-based authentication with SHA-512 hashing
- ✅ **Error Handling** - Comprehensive exception handling

## Security Considerations

### Current Implementation (Development)
- Token-based authentication via SDK
- CORS headers for local development
- Card data never touches your server
- Secure SHA-512 hashing for token generation
- SDK handles all sensitive operations

### Production Recommendations
1. **HTTPS Only** - Use SSL/TLS certificates
2. **CORS Restrictions** - Update allowed origins in process-sale.php
3. **Rate Limiting** - Implement request throttling
4. **Input Validation** - Validate all inputs server-side
5. **Secure Logging** - Never log card data or tokens
6. **Error Handling** - Don't expose sensitive details to users
7. **Monitoring** - Set up transaction alerts
8. **Environment Variables** - Use secure credential storage (not in code)

## Production Deployment

### 1. Update Configuration
```env
GP_ENVIRONMENT=production
```

### 2. Update Frontend
In `index.html`, change:
```javascript
GlobalPayments.configure({
  accessToken: accessToken,
  apiVersion: '2021-03-22',
  env: 'production'  // Change from 'sandbox'
});
```

### 3. Server Setup
- Use Apache/Nginx (not PHP built-in server)
- Configure HTTPS with valid SSL certificate
- Set proper PHP settings:
  ```ini
  display_errors = Off
  log_errors = On
  error_log = /var/log/php/error.log
  ```

### 4. Update CORS Headers
In `process-sale.php` and `get-access-token.php`, update:
```php
header('Access-Control-Allow-Origin: https://yourdomain.com');
```

### 5. Pre-Launch Checklist
- [ ] Test with production credentials in sandbox
- [ ] Verify all error handling
- [ ] Review security settings (HTTPS, CORS, etc.)
- [ ] Set up monitoring/alerts
- [ ] Load test the application
- [ ] PCI compliance review
- [ ] Update environment to production
- [ ] Test real transactions with small amounts

## Troubleshooting

### "Access token and merchant info do not match"

**Cause:** Account name is manually set but doesn't match token's account.

**Solution:**
1. Open `.env` file
2. Comment out or remove the `GP_ACCOUNT_NAME` line
3. Let the SDK auto-detect the account
4. Restart the server

### Access Token Generation Fails

**Symptoms:** Error generating access token

**Solutions:**
1. Verify GP_APP_ID and GP_APP_KEY in .env are correct
2. Check internet connectivity
3. Ensure using sandbox credentials for testing
4. Verify account is active in developer portal
5. Check for typos in credentials (no extra spaces)

### Drop-In UI Not Loading

**Symptoms:** Payment form doesn't appear

**Solutions:**
1. Check browser console for JavaScript errors
2. Verify access token is generated successfully
3. Ensure JavaScript is enabled
4. Check Drop-In UI library loads (network tab)
5. Clear browser cache

### Transaction Fails After Tokenization

**Symptoms:** Card tokenizes but transaction fails

**Solutions:**
1. Check PHP error logs for detailed error messages
2. Verify amount is valid (> 0)
3. Ensure test cards are used in sandbox
4. Verify composer dependencies are installed
5. Check that SDK version is 13.4.0 or higher

### Composer Dependencies Won't Install

**Symptoms:** `composer install` fails

**Solutions:**
1. Ensure PHP 7.4+ is installed: `php -v`
2. Update Composer: `composer self-update`
3. Clear Composer cache: `composer clear-cache`
4. Delete vendor folder and try again

## Technical Implementation Details

### Two-Token Architecture

This implementation uses a **two-token approach**:

1. **Tokenization Token** (Frontend)
   - Permission: `PMT_POST_Create_Single`
   - Used by Drop-In UI to tokenize card data
   - Generated by `get-access-token.php`

2. **Transaction Processing** (Backend)
   - PHP SDK handles token generation internally
   - Auto-configured with `GpApiConfig`
   - Processes Sale transactions with tokenized payment reference

### Why Use the PHP SDK?

The PHP SDK provides:
- ✅ Automatic token generation and management
- ✅ Built-in error handling and retries
- ✅ Automatic account detection from credentials
- ✅ Type safety and code completion
- ✅ Maintained by Global Payments
- ✅ Handles API versioning automatically

### SDK Configuration

The SDK is configured in `process-sale.php`:

```php
$config = new GpApiConfig();
$config->appId = $_ENV['GP_APP_ID'];
$config->appKey = $_ENV['GP_APP_KEY'];
$config->environment = Environment::TEST; // or Environment::PRODUCTION
$config->channel = Channel::CardNotPresent;
$config->country = 'US';

// Let SDK auto-detect account (recommended)
ServicesContainer::configureService($config);
```

## Support & Resources

- [Global Payments Documentation](https://developer.globalpay.com/)
- [PHP SDK Documentation](https://developer.globalpay.com/php)
- [PHP SDK GitHub](https://github.com/globalpayments/php-sdk)
- [Drop-In UI Guide](https://developer.globalpay.com/docs/payments/online/drop-in-ui-guide)
- [API Reference](https://developer.globalpay.com/api)
- [Support Portal](https://developer.globalpay.com/support)

## License

MIT License
