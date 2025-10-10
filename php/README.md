# Global Payments Drop-In UI - Sale Transaction (PHP)

Complete implementation of Global Payments Drop-In UI for processing Sale transactions (authorization + capture in a single step).

## Quick Start

### 1. Get Credentials
- Register at [Global Payments Developer Portal](https://developer.globalpay.com/)
- Create a new application
- Copy your **APP_ID** and **APP_KEY**

### 2. Configure
```bash
# Edit .env file
nano .env

# Add your credentials:
GP_APP_ID=your_app_id_here
GP_APP_KEY=your_app_key_here
GP_ACCOUNT_NAME=transaction_processing
GP_ENVIRONMENT=sandbox
```

### 3. Install & Run
```bash
composer install
php -S localhost:8000
```

### 4. Test
1. Open http://localhost:8000
2. Use test card: **4263 9826 4026 9299**
3. CVV: **123**, Expiry: any future date
4. Click **Process Payment**

## Test Cards (Sandbox)

| Brand | Card Number | CVV | Expiry |
|-------|-------------|-----|--------|
| Visa | 4263 9826 4026 9299 | 123 | Any future |
| Mastercard | 5425 2334 2424 1200 | 123 | Any future |
| Discover | 6011 0000 0000 0012 | 123 | Any future |

## Requirements

- PHP 7.4 or later
- cURL extension
- JSON extension
- Composer
- Global Payments account with APP_ID and APP_KEY

## Implementation Files

### Core Files
- **index.html** - Payment form with Drop-In UI integration
- **get-access-token.php** - Generates access tokens for Drop-In UI
- **process-sale.php** - Processes Sale transactions
- **.env** - Configuration (add your credentials here)
- **composer.json** - Dependencies

### Legacy Files (Unused)
- **config.php** - Old SDK configuration (not used)
- **process-payment.php** - Old SDK payment processing (not used)

## How It Works

### Payment Flow

1. **Page Load**: Frontend requests access token from `get-access-token.php`
2. **Initialize**: Drop-In UI initializes with the access token
3. **Card Entry**: Customer enters card details (stays in browser)
4. **Tokenization**: Drop-In UI creates payment reference token
5. **Process**: Token sent to `process-sale.php` for Sale transaction
6. **Result**: Success or error message displayed

### Architecture

```
Browser → get-access-token.php → Global Payments API (Token)
  ↓
Drop-In UI (Tokenization)
  ↓
Browser → process-sale.php → Global Payments API (Sale)
  ↓
Success/Error
```

## API Endpoints

### POST /get-access-token.php

Generates access token for Drop-In UI initialization.

**Response:**
```json
{
  "success": true,
  "token": "access_token_string",
  "expiresIn": 600
}
```

### POST /process-sale.php

Processes Sale transaction using payment reference.

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
    "currency": "USD"
  }
}
```

## Environment Variables

```env
# Application credentials
GP_APP_ID=your_app_id_here          # From developer dashboard
GP_APP_KEY=your_app_key_here        # From developer dashboard

# Account configuration
GP_ACCOUNT_NAME=transaction_processing

# Environment (sandbox or production)
GP_ENVIRONMENT=sandbox
```

## Testing

### Manual Test Flow

1. Start server: `php -S localhost:8000`
2. Open http://localhost:8000
3. Enter amount (e.g., 10.00)
4. Enter test card: **4263 9826 4026 9299**
5. CVV: **123**, Expiry: **12/26**
6. Click **Process Payment**
7. Verify success message with transaction ID

### Expected Results

**Success:**
```
✓ Payment Successful! Transaction ID: TRN_xxxxx
```

**Common Errors:**

| Error | Cause | Solution |
|-------|-------|----------|
| Failed to get access token | Invalid credentials | Check GP_APP_ID and GP_APP_KEY |
| Drop-In UI not loading | Server not running | Ensure `php -S localhost:8000` is running |
| Transaction failed | Invalid card/account | Use test cards, verify account active |
| CORS errors | Wrong URL | Access via http://localhost:8000 |

### API Testing

Test access token generation:
```bash
curl -X POST http://localhost:8000/get-access-token.php
```

## Key Features

- ✅ **PCI SAQ A Compliant** - Simplest compliance level
- ✅ **No SDK Required** - Direct API calls via cURL
- ✅ **Pre-built UI** - Drop-In UI handles card input
- ✅ **Sale Transactions** - Auth + Capture in one step
- ✅ **Secure** - Card data never touches your server
- ✅ **Mobile Responsive** - Works on all devices
- ✅ **Built-in Validation** - Automatic card validation

## Security Considerations

### Current Implementation (Development)
- Token-based authentication
- CORS headers for local development
- No sensitive card data on server
- Secure SHA-512 hashing

### Production Recommendations
1. **HTTPS Only** - Use SSL/TLS certificates
2. **CORS Restrictions** - Limit allowed origins
3. **Rate Limiting** - Prevent abuse
4. **Input Validation** - Validate all inputs
5. **Secure Logging** - Never log card data or tokens
6. **Error Handling** - Don't expose sensitive details
7. **Monitoring** - Set up transaction alerts
8. **Environment Variables** - Secure credential storage

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
  ```

### 4. Pre-Launch Checklist
- [ ] Test all payment scenarios
- [ ] Verify error handling
- [ ] Review security settings
- [ ] Set up monitoring/alerts
- [ ] Test with production credentials in sandbox
- [ ] Load test
- [ ] PCI compliance review

## Troubleshooting

### Access Token Generation Fails

**Symptoms:** Error generating access token

**Solutions:**
1. Verify GP_APP_ID and GP_APP_KEY in .env
2. Check internet connectivity
3. Ensure using sandbox credentials for testing
4. Verify account is active

### Drop-In UI Not Loading

**Symptoms:** Payment form doesn't appear

**Solutions:**
1. Check browser console for errors
2. Verify access token is generated
3. Ensure JavaScript is enabled
4. Check Drop-In UI library loads (network tab)

### Transaction Fails

**Symptoms:** Payment processing returns error

**Solutions:**
1. Verify test card details
2. Check amount is valid (> 0)
3. Ensure GP_ACCOUNT_NAME matches account
4. Check API response for error details

## What's Different from SDK Implementation

### Drop-In UI (This Implementation)
- ✅ No server-side SDK required
- ✅ PCI SAQ A compliance (simplest)
- ✅ Pre-built, mobile-responsive UI
- ✅ Built-in validation
- ✅ Lighter dependencies

### Traditional SDK
- PHP SDK dependency
- More control over UI
- Custom form implementation
- More complex setup

## Support & Resources

- [Global Payments Documentation](https://developer.globalpay.com/)
- [Drop-In UI Guide](https://developer.globalpay.com/docs/payments/online/drop-in-ui-guide)
- [API Reference](https://developer.globalpay.com/api)
- [Support Portal](https://developer.globalpay.com/support)

## License

MIT License
