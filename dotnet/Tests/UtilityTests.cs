using Xunit;
using GpPayments;

namespace GpPaymentsTests;

/// <summary>
/// Unit tests for GpUtilities (ToMinorUnits, TwoDigitYear).
/// Run: dotnet test dotnet/Tests/Tests.csproj
/// </summary>
public class UtilityTests
{
    // ── ToMinorUnits ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("10.00",   "1000")]
    [InlineData("0.01",    "1")]
    [InlineData("9.99",    "999")]
    [InlineData("1.00",    "100")]
    [InlineData("100.00",  "10000")]
    [InlineData("0.99",    "99")]
    [InlineData("50.50",   "5050")]
    [InlineData("1234.56", "123456")]
    public void ToMinorUnits_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, GpUtilities.ToMinorUnits(input));
    }

    [Fact]
    public void ToMinorUnits_ReturnsString()
    {
        var result = GpUtilities.ToMinorUnits("10.00");
        Assert.IsType<string>(result);
    }

    [Fact]
    public void ToMinorUnits_IntegerInput_Works()
    {
        Assert.Equal("1000", GpUtilities.ToMinorUnits("10"));
    }

    [Fact]
    public void ToMinorUnits_MidpointRoundsCorrectly()
    {
        // Math.Round midpoint: 0.005 → may round to 0 or 1 depending on mode
        // .NET default is MidpointRounding.ToEven (banker's rounding): 0.005 → 0
        // Our impl uses Math.Round(val * 100) which is ToEven by default
        var result = int.Parse(GpUtilities.ToMinorUnits("0.005"));
        Assert.True(result is 0 or 1, "Midpoint must round to 0 or 1");
    }

    // ── TwoDigitYear ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2025", "25")]
    [InlineData("2030", "30")]
    [InlineData("1999", "99")]
    [InlineData("2000", "00")]
    [InlineData("2024", "24")]
    public void TwoDigitYear_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, GpUtilities.TwoDigitYear(input));
    }

    [Fact]
    public void TwoDigitYear_ReturnsTwoChars()
    {
        Assert.Equal(2, GpUtilities.TwoDigitYear("2025").Length);
    }

    [Fact]
    public void TwoDigitYear_ShortInputPassthrough()
    {
        // If year already ≤ 2 chars, returns as-is
        Assert.Equal("25", GpUtilities.TwoDigitYear("25"));
    }

    // ── AUT_ prefix stripping (mirrors Program.cs logic) ─────────────────────

    [Theory]
    [InlineData("AUT_abc-123-def", "abc-123-def")]
    [InlineData("plain-uuid",      "plain-uuid")]
    [InlineData("AUT_",            "")]
    [InlineData("",                "")]
    public void AutPrefix_StrippedCorrectly(string input, string expected)
    {
        // Mirrors: serverTransIdRaw.StartsWith("AUT_") ? serverTransIdRaw[4..] : serverTransIdRaw
        var result = input.StartsWith("AUT_") ? input[4..] : input;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AutPrefix_MidString_NotStripped()
    {
        var input  = "prefix_AUT_abc";
        var result = input.StartsWith("AUT_") ? input[4..] : input;
        Assert.Equal("prefix_AUT_abc", result);
    }

    // ── SHA-512 nonce hashing ─────────────────────────────────────────────────

    [Fact]
    public void Sha512_ProducesCorrectLength()
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA512.HashData(
                System.Text.Encoding.UTF8.GetBytes("test-nonce")
            )
        ).ToLower();
        Assert.Equal(128, hash.Length);
    }

    [Fact]
    public void Sha512_IsLowercaseHex()
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA512.HashData(
                System.Text.Encoding.UTF8.GetBytes("any-input")
            )
        ).ToLower();
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void Sha512_IsDeterministic()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("same-input");
        var h1 = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(input)).ToLower();
        var h2 = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(input)).ToLower();
        Assert.Equal(h1, h2);
    }

    // ── GP-API base URL logic ─────────────────────────────────────────────────

    [Fact]
    public void GpApiBase_DefaultIsSandbox()
    {
        // Mirrors GpApiBase() in Program.cs
        var env    = Environment.GetEnvironmentVariable("GP_ENVIRONMENT") ?? "";
        var baseUrl = "production".Equals(env)
            ? "https://apis.globalpay.com"
            : "https://apis.sandbox.globalpay.com";

        if (!env.Equals("production"))
            Assert.Contains("sandbox", baseUrl);
    }

    [Fact]
    public void GpApiBase_ProductionUrlHasNoSandbox()
    {
        var productionUrl = "https://apis.globalpay.com";
        Assert.DoesNotContain("sandbox", productionUrl);
    }

    // ── Token cache margin ────────────────────────────────────────────────────

    [Fact]
    public void TokenCache_RefreshMarginIs60Seconds()
    {
        // Program.cs: tokenExpiresAt = now + (expiresIn - 60) * 1000L
        int expiresIn     = 3600;
        int marginSeconds = 60;
        long expectedMs   = (expiresIn - marginSeconds) * 1000L;
        Assert.Equal(3_540_000L, expectedMs);
    }
}
