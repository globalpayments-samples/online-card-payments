using GlobalPayments.Api;
using GlobalPayments.Api.Entities;
using GlobalPayments.Api.PaymentMethods;
using GlobalPayments.Api.Services;
using dotenv.net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardPaymentSample;

/// <summary>
/// Global Payments Drop-In UI - Sale Transaction (.NET)
///
/// This application implements Global Payments Drop-In UI integration
/// for processing Sale transactions using the official .NET SDK.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Load environment variables from .env file
        DotEnv.Load();

        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        // Configure static file serving for the payment form
        app.UseDefaultFiles();
        app.UseStaticFiles();

        ConfigureEndpoints(app);

        var port = System.Environment.GetEnvironmentVariable("PORT") ?? "8000";
        app.Urls.Add($"http://0.0.0.0:{port}");

        Console.WriteLine($"✅ Server running at http://localhost:{port}");
        Console.WriteLine($"Environment: {System.Environment.GetEnvironmentVariable("GP_ENVIRONMENT") ?? "sandbox"}");

        app.Run();
    }

    /// <summary>
    /// Generates a random nonce for access token request
    /// </summary>
    private static string GenerateNonce()
    {
        var bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Creates SHA-512 hash of nonce + appKey
    /// </summary>
    private static string HashSecret(string nonce, string appKey)
    {
        using (var sha512 = SHA512.Create())
        {
            var bytes = Encoding.UTF8.GetBytes(nonce + appKey);
            var hash = sha512.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }

    /// <summary>
    /// Configures the application's HTTP endpoints
    /// </summary>
    private static void ConfigureEndpoints(WebApplication app)
    {
        // Endpoint for generating access token for Drop-In UI
        app.MapPost("/get-access-token", async (HttpContext context) =>
        {
            try
            {
                // Generate nonce and secret
                var nonce = GenerateNonce();
                var secret = HashSecret(nonce, System.Environment.GetEnvironmentVariable("GP_APP_KEY") ?? "");

                // Build token request
                var tokenRequest = new
                {
                    app_id = System.Environment.GetEnvironmentVariable("GP_APP_ID"),
                    nonce = nonce,
                    secret = secret,
                    grant_type = "client_credentials",
                    seconds_to_expire = 600,
                    permissions = new[] { "PMT_POST_Create_Single" }
                };

                // Determine API endpoint
                var apiEndpoint = "production".Equals(System.Environment.GetEnvironmentVariable("GP_ENVIRONMENT"))
                    ? "https://apis.globalpay.com/ucp/accesstoken"
                    : "https://apis.sandbox.globalpay.com/ucp/accesstoken";

                // Make API request
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("X-GP-Version", "2021-03-22");

                var jsonContent = JsonSerializer.Serialize(tokenRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(apiEndpoint, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to generate access token: {responseBody}");
                }

                // Parse response
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
                var token = tokenResponse.GetProperty("token").GetString();
                var expiresIn = tokenResponse.TryGetProperty("seconds_to_expire", out var exp) ? exp.GetInt32() : 600;

                return Results.Ok(new
                {
                    success = true,
                    token = token,
                    expiresIn = expiresIn
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    success = false,
                    message = "Error generating access token",
                    error = ex.Message
                }, statusCode: 500);
            }
        });

        // Endpoint for processing sale transactions
        app.MapPost("/process-sale", async (HttpContext context) =>
        {
            try
            {
                // Read JSON request body
                var jsonBody = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);

                // Validate input
                if (!jsonBody.TryGetProperty("payment_reference", out var paymentRefElement))
                {
                    throw new Exception("Missing payment reference");
                }

                if (!jsonBody.TryGetProperty("amount", out var amountElement) ||
                    !amountElement.TryGetDecimal(out var amount) || amount <= 0)
                {
                    throw new Exception("Invalid amount");
                }

                var paymentReference = paymentRefElement.GetString() ?? "";
                var currency = jsonBody.TryGetProperty("currency", out var currElement)
                    ? currElement.GetString() ?? "USD"
                    : "USD";

                // Configure Global Payments SDK
                var config = new GpApiConfig
                {
                    AppId = System.Environment.GetEnvironmentVariable("GP_APP_ID"),
                    AppKey = System.Environment.GetEnvironmentVariable("GP_APP_KEY"),
                    Environment = "production".Equals(System.Environment.GetEnvironmentVariable("GP_ENVIRONMENT"))
                        ? GlobalPayments.Api.Entities.Environment.PRODUCTION
                        : GlobalPayments.Api.Entities.Environment.TEST,
                    Channel = GlobalPayments.Api.Entities.Channel.CardNotPresent,
                    Country = "US"
                };

                // Note: Don't set AccountName - let SDK auto-detect

                // Configure the service
                ServicesContainer.ConfigureService(config);

                // Create card data from payment reference token
                var card = new CreditCardData
                {
                    Token = paymentReference
                };

                // Process the charge
                var transaction = card.Charge(amount)
                    .WithCurrency(currency)
                    .Execute();

                // Check response
                if (transaction.ResponseCode == "00" || transaction.ResponseCode == "SUCCESS")
                {
                    return Results.Ok(new
                    {
                        success = true,
                        message = "Payment successful!",
                        data = new
                        {
                            transactionId = transaction.TransactionId,
                            status = transaction.ResponseMessage,
                            amount = amount.ToString(),
                            currency = currency,
                            reference = transaction.ReferenceNumber ?? "",
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    });
                }
                else
                {
                    throw new Exception($"Transaction declined: {transaction.ResponseMessage}");
                }
            }
            catch (ApiException ex)
            {
                return Results.Json(new
                {
                    success = false,
                    message = "Payment processing failed",
                    error = ex.Message
                }, statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    success = false,
                    message = "Payment processing failed",
                    error = ex.Message
                }, statusCode: 400);
            }
        });
    }
}
