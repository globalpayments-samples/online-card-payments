# Global Payments Drop-In UI - Sale Transaction (Python)

Complete implementation of Global Payments Drop-In UI for processing Sale transactions using the official Python SDK.

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
# Create virtual environment (recommended)
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt
```

### 4. Run Server
```bash
python server.py
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

- Python 3.7 or later
- pip (Python Package Installer)
- Global Payments account with APP_ID and APP_KEY

## Dependencies

This implementation uses:
- **globalpayments.api** (v2.0.4+) - Official Global Payments Python SDK
- **Flask** - Web framework
- **Flask-CORS** - CORS support
- **python-dotenv** - Environment variable management
- **requests** - HTTP library for API calls

## Implementation Files

### Core Files
- **index.html** - Payment form with Drop-In UI integration
- **server.py** - Flask server with two endpoints (get-access-token, process-sale)
- **.env** - Configuration (copy from .env.sample)
- **requirements.txt** - Dependencies

## How It Works

### Payment Flow

1. **Page Load**: Frontend requests tokenization token from `/get-access-token`
2. **Initialize**: Drop-In UI initializes with the access token
3. **Card Entry**: Customer enters card details (stays in browser, never touches your server)
4. **Tokenization**: Drop-In UI creates payment reference token
5. **Process**: Payment reference sent to `/process-sale`
6. **SDK Transaction**: Python SDK handles authentication and processes Sale transaction
7. **Result**: Success or error message displayed

### Architecture

```
Browser → /get-access-token → GP API (Tokenization Token)
  ↓
Drop-In UI (Card Tokenization)
  ↓
Browser → /process-sale → Python SDK → GP API (Sale Transaction)
  ↓
Success/Error
```

## API Endpoints

### POST /get-access-token

Generates access token with `PMT_POST_Create_Single` permission for Drop-In UI tokenization.

**Response:**
```json
{
  "success": true,
  "token": "access_token_string",
  "expiresIn": 600
}
```

### POST /process-sale

Processes Sale transaction using payment reference via Python SDK.

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
    "timestamp": "2025-01-15T10:30:45"
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
2. The Python SDK automatically detects the correct account from your APP_ID/APP_KEY
3. Manually setting the account name can cause "Access token and merchant info do not match" errors
4. Let the SDK handle account selection for best compatibility

## Testing

### Manual Test Flow

1. Start server: `python server.py`
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
| Drop-In UI not loading | Server not running | Run `python server.py` |
| Transaction declined | Invalid card/amount | Use test cards, check amount > 0 |
| CORS errors | Wrong URL | Access via http://localhost:8000 |

### cURL Testing

Test access token generation:
```bash
curl -X POST http://localhost:8000/get-access-token
```

## Key Features

- ✅ **Official Python SDK** - Uses globalpayments.api for reliability
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
- CORS enabled for local development
- Card data never touches your server
- Secure SHA-512 hashing for token generation
- SDK handles all sensitive operations

### Production Recommendations
1. **HTTPS Only** - Use SSL/TLS certificates
2. **CORS Restrictions** - Update allowed origins in server.py
3. **Rate Limiting** - Implement request throttling
4. **Input Validation** - Validate all inputs server-side
5. **Secure Logging** - Never log card data or tokens
6. **Error Handling** - Don't expose sensitive details to users
7. **Monitoring** - Set up transaction alerts
8. **Environment Variables** - Use secure credential storage (not in code)
9. **Production WSGI Server** - Use Gunicorn instead of Flask dev server

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
- Use Gunicorn or uWSGI (not Flask dev server)
- Configure HTTPS with valid SSL certificate
- Set proper environment variables:
  ```bash
  export FLASK_ENV=production
  gunicorn -w 4 -b 0.0.0.0:8000 server:app
  ```

### 4. Update CORS Settings
In `server.py`, configure CORS for your domain:
```python
CORS(app, origins=['https://yourdomain.com'])
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
1. Check Python console for detailed error messages
2. Verify amount is valid (> 0)
3. Ensure test cards are used in sandbox
4. Verify pip dependencies are installed
5. Check that SDK version is 2.0.4 or higher

### Dependencies Won't Install

**Symptoms:** `pip install` fails

**Solutions:**
1. Ensure Python 3.7+ is installed: `python --version`
2. Update pip: `pip install --upgrade pip`
3. Use virtual environment: `python -m venv venv && source venv/bin/activate`
4. Try installing dependencies individually

## Technical Implementation Details

### Two-Token Architecture

This implementation uses a **two-token approach**:

1. **Tokenization Token** (Frontend)
   - Permission: `PMT_POST_Create_Single`
   - Used by Drop-In UI to tokenize card data
   - Generated by `/get-access-token`

2. **Transaction Processing** (Backend)
   - Python SDK handles token generation internally
   - Auto-configured with `GpApiConfig`
   - Processes Sale transactions with tokenized payment reference

### Why Use the Python SDK?

The Python SDK provides:
- ✅ Automatic token generation and management
- ✅ Built-in error handling and retries
- ✅ Automatic account detection from credentials
- ✅ Type hints and IDE support
- ✅ Maintained by Global Payments
- ✅ Handles API versioning automatically

### SDK Configuration

The SDK is configured in `server.py`:

```python
config = GpApiConfig()
config.app_id = os.getenv('GP_APP_ID')
config.app_key = os.getenv('GP_APP_KEY')
config.environment = Environment.TEST  # or Environment.PRODUCTION
config.channel = Channel.CardNotPresent
config.country = 'US'

# Let SDK auto-detect account (recommended)
ServicesContainer.configure(config)
```

## Support & Resources

- [Global Payments Documentation](https://developer.globalpay.com/)
- [Python SDK Documentation](https://developer.globalpay.com/python)
- [Python SDK GitHub](https://github.com/globalpayments/python-sdk)
- [Drop-In UI Guide](https://developer.globalpay.com/docs/payments/online/drop-in-ui-guide)
- [API Reference](https://developer.globalpay.com/api)
- [Support Portal](https://developer.globalpay.com/support)

## License

MIT License
