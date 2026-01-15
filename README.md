# Global Payments Drop-In UI - Sale Transaction (Multi-Language)

Complete implementation of Global Payments Drop-In UI for processing Sale transactions using the official SDKs across 4 programming languages. All implementations follow the same architecture and use modern GP-API with GpApiConfig.

## 🚀 Available Implementations

| Language | Framework | SDK Version | Port | Status |
|----------|-----------|-------------|------|--------|
| [**PHP**](./php/)- ([Preview](https://githubbox.com/globalpayments-samples/drop-in-ui-payments/tree/main/php)) | Built-in Server | v13.4+ | 8000 | ✅ Complete |
| [**Node.js**](./nodejs/)- ([Preview](https://githubbox.com/globalpayments-samples/drop-in-ui-payments/tree/main/nodejs)) | Express.js | v3.10.6+ | 8000 | ✅ Complete |
| [**Java**](./java/)- ([Preview](https://githubbox.com/globalpayments-samples/drop-in-ui-payments/tree/main/java)) | Jakarta Servlet | v14.2.20 | 8000 | ✅ Complete |
| [**.NET**](./dotnet/)- ([Preview](https://githubbox.com/globalpayments-samples/drop-in-ui-payments/tree/main/dotnet)) | ASP.NET Core | v9.0.16 | 8000 | ✅ Complete |

## 🏗️ Architecture

All implementations use the **same architecture**:

### Two-Token System
1. **Tokenization Token** - Generated server-side with `PMT_POST_Create_Single` permission for Drop-In UI
2. **Transaction Token** - SDK-generated automatically during transaction processing

### Two API Endpoints
1. **POST /get-access-token** - Generates access token for Drop-In UI initialization
2. **POST /process-sale** - Processes Sale transaction using payment reference from Drop-In UI

### Payment Flow
```
Browser → /get-access-token → GP API (Tokenization Token)
   ↓
Drop-In UI (Card Tokenization - PCI Compliant)
   ↓
Browser → /process-sale → SDK → GP API (Sale Transaction)
   ↓
Success/Error Response
```

## ⚡ Quick Start

### 1. Choose Your Language

```bash
cd php        # or nodejs, java, dotnet
```

### 2. Configure Credentials

```bash
# Copy environment template
cp .env.sample .env

# Edit .env with your credentials
GP_API_APP_ID=your_app_id_here
GP_API_APP_KEY=your_app_key_here
GP_ENVIRONMENT=sandbox
```

### 3. Install & Run

**PHP:**
```bash
composer install
php -S localhost:8000
```

**Node.js:**
```bash
npm install
npm start
```

**Java:**
```bash
mvn clean package
mvn cargo:run
```

**.NET:**
```bash
dotnet restore
dotnet run
```

### 4. Test Payment

1. Open http://localhost:8000
2. Enter amount (e.g., 10.00)
3. Use test card: **4263 9826 4026 9299**
4. CVV: **123**, Expiry: Any future date
5. Click **SUBMIT**
6. Verify success with transaction ID

## 🧪 Test Cards (Sandbox)

| Brand | Card Number | CVV | Expiry |
|-------|-------------|-----|--------|
| Visa | 4263 9826 4026 9299 | 123 | Any future |
| Visa | 4263 9700 0000 5262 | 123 | Any future |
| Mastercard | 5425 2334 2424 1200 | 123 | Any future |
| Discover | 6011 0000 0000 0012 | 123 | Any future |

More test cards: [Global Payments Test Cards](https://developer.globalpay.com/resources/test-cards)

## 🔧 Configuration

All implementations use the same environment variables:

```env
# Required
GP_API_APP_ID=your_app_id_here          # From developer dashboard
GP_API_APP_KEY=your_app_key_here        # From developer dashboard

# Optional
GP_ENVIRONMENT=sandbox              # sandbox or production

# Not Recommended (SDK auto-detects)
# GP_ACCOUNT_NAME=Transaction_Processing
```

### ⚠️ Important Configuration Notes

1. **Do NOT manually set `GP_ACCOUNT_NAME`** - The SDK automatically detects the correct account from your `APP_ID`/`APP_KEY`
2. Manually setting the account name can cause "Access token and merchant info do not match" errors
3. Let the SDK handle account selection for best compatibility

## 🎨 Features

### Consistent Across All Languages

- ✅ **Modern GP-API** - Uses GpApiConfig (not legacy Portico)
- ✅ **Drop-In UI** - Pre-built payment form from Global Payments
- ✅ **PCI SAQ A Compliant** - Card data never touches your server
- ✅ **Two-Token Architecture** - Secure tokenization + transaction flow
- ✅ **Auto-Configuration** - SDK auto-detects account settings
- ✅ **Centered UI** - Professional, responsive design
- ✅ **Error Handling** - Comprehensive error handling
- ✅ **Test Cards Link** - Elegant button to test cards documentation

### Security

- 🔒 SHA-512 hashing for token generation
- 🔒 Environment variables for credentials (not in code)
- 🔒 Drop-In UI handles card input (PCI compliant)
- 🔒 Token-based authentication
- 🔒 HTTPS ready for production

## 📁 Project Structure

Each language implementation follows this structure:

```
language/
├── server file          # Main application file
├── index.html          # Drop-In UI frontend (or in static/webapp/wwwroot)
├── .env                # Credentials (not tracked in git)
├── .env.sample         # Configuration template
├── README.md           # Language-specific documentation
└── dependencies file   # package.json, requirements.txt, pom.xml, etc.
```

### Implementation Files

| Language | Server File | HTML Location | Config File |
|----------|------------|---------------|-------------|
| PHP | `get-access-token.php`, `process-sale.php` | `index.html` | `composer.json` |
| Node.js | `server.js` | `index.html` | `package.json` |
| Java | `ProcessPaymentServlet.java` | `src/main/webapp/index.html` | `pom.xml` |
| .NET | `Program.cs` | `wwwroot/index.html` | `dotnet.csproj` |

## 🔍 Technical Details

### Endpoint Implementation

**Get Access Token:**
```
POST /get-access-token
Response: { "success": true, "token": "...", "expiresIn": 600 }
```

**Process Sale:**
```
POST /process-sale
Body: { "payment_reference": "PMT_...", "amount": 10.00, "currency": "USD" }
Response: { "success": true, "message": "Payment successful!", "data": {...} }
```

### SDK Configuration Pattern

All implementations use this pattern:

```javascript
// Conceptual example
config = new GpApiConfig()
config.appId = GP_API_APP_ID
config.appKey = GP_API_APP_KEY
config.environment = GP_ENVIRONMENT
config.channel = CardNotPresent
config.country = "US"
// Note: Don't set account name - SDK auto-detects

ServicesContainer.configure(config)
```

## 🚀 Production Deployment

### 1. Update Configuration

```env
GP_ENVIRONMENT=production
```

### 2. Update Frontend

In `index.html`, change Drop-In UI environment:

```javascript
GlobalPayments.configure({
  accessToken: accessToken,
  apiVersion: '2021-03-22',
  env: 'production'  // Change from 'sandbox'
});
```

### 3. Security Checklist

- [ ] Use production credentials
- [ ] Enable HTTPS/SSL
- [ ] Configure CORS for your domain
- [ ] Set up rate limiting
- [ ] Enable logging and monitoring
- [ ] Review error handling (don't expose sensitive details)
- [ ] Test with production credentials in sandbox first
- [ ] Use production web server (not development server)

### 4. Server Recommendations

- **PHP:** Use Apache/Nginx with PHP-FPM
- **Node.js:** Use PM2 or similar process manager
- **Java:** Use Tomcat or similar servlet container
- **.NET:** Use Kestrel behind reverse proxy (Nginx/IIS)

## 📖 Documentation

### Per-Language READMEs

Each implementation has its own detailed README:

- [PHP README](./php/README.md) - Comprehensive PHP documentation
- [Node.js README](./nodejs/README.md) - Node.js specific guide
- [Java README](./java/README.md) - Java/Maven documentation
- [.NET README](./dotnet/README.md) - .NET Core guide

### External Resources

- [Global Payments Documentation](https://developer.globalpay.com/)
- [Drop-In UI Guide](https://developer.globalpay.com/docs/payments/online/drop-in-ui-guide)
- [GP-API Reference](https://developer.globalpay.com/api)
- [Test Cards](https://developer.globalpay.com/resources/test-cards)

## 🐛 Troubleshooting

### Common Issues

**"Access token and merchant info do not match"**
- **Solution:** Comment out `GP_ACCOUNT_NAME` in `.env` file. Let SDK auto-detect.

**"Failed to generate access token"**
- **Solution:** Verify `GP_API_APP_ID` and `GP_API_APP_KEY` are correct in `.env` file.

**Drop-In UI not loading**
- **Solution:** Check browser console for errors. Verify access token is generated successfully.

**Transaction declined**
- **Solution:** Ensure using test cards in sandbox. Verify amount > 0.

**Server won't start**
- **Solution:** Check if port 8000 is already in use. Verify dependencies are installed.

### Getting Help

1. Check language-specific README for detailed troubleshooting
2. Review [Global Payments Documentation](https://developer.globalpay.com/)
3. Check [GitHub Issues](https://github.com/globalpayments)

## 🔄 Migration from Legacy Portico

This project uses modern **GP-API** with **GpApiConfig** (not legacy Portico/Heartland API).

### Key Differences

| Legacy (Portico) | Modern (GP-API) |
|------------------|-----------------|
| PorticoConfig | GpApiConfig |
| SECRET_API_KEY | GP_API_APP_ID + GP_API_APP_KEY |
| Manual account config | Auto-detection |
| Basic forms | Drop-In UI |

If migrating from Portico, see the commit history on the `rewriting-implementations` branch for migration patterns.

## 📊 Project Stats

- **4 Languages:** PHP, Node.js, Java, .NET
- **100% Feature Parity:** All implementations identical
- **PCI Compliant:** SAQ A level compliance
- **Production Ready:** Comprehensive error handling
- **Well Documented:** Complete READMEs for each language

## 📄 License

MIT License

## 🙋 Contributing

Each language implementation follows the same architecture. When contributing:

1. Maintain consistency across all languages
2. Update all language implementations for feature additions
3. Keep .env.sample files identical
4. Ensure Drop-In UI integration remains consistent
5. Test with sandbox credentials before committing

## 🎯 Roadmap

Potential future enhancements:

- [ ] Authorization (pre-auth) transactions
- [ ] Refund processing
- [ ] Recurring payments/subscriptions
- [ ] Multi-currency support
- [ ] Webhook handling for payment notifications
- [ ] Payment method management (save cards)

## ⭐ Acknowledgments

Built with official Global Payments SDKs:
- [PHP SDK](https://github.com/globalpayments/php-sdk)
- [Node.js SDK](https://github.com/globalpayments/node-sdk)
- [Java SDK](https://github.com/globalpayments/java-sdk)
- [.NET SDK](https://github.com/globalpayments/dotnet-sdk)
