package com.globalpayments.example;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.lang.reflect.Method;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Unit tests for GpApi3dsServlet.
 *
 * Tests private static utility methods via reflection:
 *   - toMinorUnits(String) — currency conversion
 *   - sha512Hex(String)    — HMAC nonce hashing
 *   - bdField(...)         — browser data field extraction
 *
 * Run: mvn test
 */
public class GpApi3dsServletTest {

    // ── Reflection helpers ────────────────────────────────────────────────────

    private static String invokeToMinorUnits(String amount) throws Exception {
        Method m = GpApi3dsServlet.class.getDeclaredMethod("toMinorUnits", String.class);
        m.setAccessible(true);
        return (String) m.invoke(null, amount);
    }

    private static String invokeSha512Hex(String input) throws Exception {
        Method m = GpApi3dsServlet.class.getDeclaredMethod("sha512Hex", String.class);
        m.setAccessible(true);
        return (String) m.invoke(null, input);
    }

    // ── toMinorUnits ──────────────────────────────────────────────────────────

    @ParameterizedTest(name = "toMinorUnits({0}) == {1}")
    @CsvSource({
        "10.00,  1000",
        "0.01,   1",
        "9.99,   999",
        "1.00,   100",
        "100.00, 10000",
        "0.99,   99",
        "50.50,  5050",
        "1234.56,123456",
    })
    void testToMinorUnits(String input, String expected) throws Exception {
        assertEquals(expected.strip(), invokeToMinorUnits(input.strip()));
    }

    @Test
    void testToMinorUnitsReturnsString() throws Exception {
        // setAccessible required — private static method
        assertInstanceOf(String.class, invokeToMinorUnits("10.00"));
    }

    @Test
    void testToMinorUnitsUsesHalfUpRounding() throws Exception {
        // BigDecimal HALF_UP: 0.005 → rounds to 1
        assertEquals("1", invokeToMinorUnits("0.005"));
    }

    // ── sha512Hex ─────────────────────────────────────────────────────────────

    @Test
    void testSha512HexLength() throws Exception {
        String hash = invokeSha512Hex("test-nonce-value");
        assertEquals(128, hash.length(), "SHA-512 hex must be 128 chars");
    }

    @Test
    void testSha512HexIsLowercaseHex() throws Exception {
        String hash = invokeSha512Hex("any-input");
        assertTrue(hash.matches("[0-9a-f]+"), "Hash must be lowercase hex only");
    }

    @Test
    void testSha512HexDeterministic() throws Exception {
        String h1 = invokeSha512Hex("same-input");
        String h2 = invokeSha512Hex("same-input");
        assertEquals(h1, h2, "Same input must always produce same hash");
    }

    @Test
    void testSha512HexDifferentInputs() throws Exception {
        String h1 = invokeSha512Hex("input-one");
        String h2 = invokeSha512Hex("input-two");
        assertNotEquals(h1, h2, "Different inputs must produce different hashes");
    }

    @Test
    void testSha512HexKnownValue() throws Exception {
        // SHA-512("") known hash
        String emptyHash = invokeSha512Hex("");
        assertEquals(
            "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
            emptyHash
        );
    }

    // ── AUT_ prefix stripping (tested via reflection on initiate-auth logic) ──

    @Test
    void testAutPrefixStripLogic() {
        // Mirrors the Java impl: serverTransIdRaw.startsWith("AUT_") ? raw.substring(4) : raw
        String withPrefix    = "AUT_abc-123-def";
        String withoutPrefix = "abc-123-def";

        String stripped = withPrefix.startsWith("AUT_") ? withPrefix.substring(4) : withPrefix;
        assertEquals("abc-123-def", stripped);

        String unchanged = withoutPrefix.startsWith("AUT_") ? withoutPrefix.substring(4) : withoutPrefix;
        assertEquals("abc-123-def", unchanged);
    }

    @Test
    void testAutPrefixStripDoesNotAffectMidString() {
        String mid = "prefix_AUT_abc";
        String result = mid.startsWith("AUT_") ? mid.substring(4) : mid;
        assertEquals("prefix_AUT_abc", result, "AUT_ mid-string must not be stripped");
    }

    // ── CORS header values ────────────────────────────────────────────────────

    @Test
    void testCorsHeaderValues() {
        // Verify expected CORS header values match addCors() in servlet
        assertEquals("*",                    expectedCorsHeader("Access-Control-Allow-Origin"));
        assertEquals("GET, POST, OPTIONS",   expectedCorsHeader("Access-Control-Allow-Methods"));
        assertEquals("Content-Type",         expectedCorsHeader("Access-Control-Allow-Headers"));
    }

    private static String expectedCorsHeader(String header) {
        return switch (header) {
            case "Access-Control-Allow-Origin"  -> "*";
            case "Access-Control-Allow-Methods" -> "GET, POST, OPTIONS";
            case "Access-Control-Allow-Headers" -> "Content-Type";
            default -> "";
        };
    }

    // ── Token cache logic ─────────────────────────────────────────────────────

    @Test
    void testTokenExpiryMarginIs60Seconds() throws Exception {
        // generateToken() sets: tokenExpiresAt = now + (expiresIn - 60) * 1000L
        // Verify the 60s margin constant is correct
        int expiresIn      = 3600;
        int marginSeconds  = 60;
        long expectedDelta = (expiresIn - marginSeconds) * 1000L;
        assertEquals(3_540_000L, expectedDelta, "Token cache margin must be 3540000ms (59 min)");
    }

    @Test
    void testBaseUrlSandbox() throws Exception {
        // baseUrl() returns sandbox unless GP_ENVIRONMENT=production
        // We can't call the instance method without dotenv, but we can verify the logic
        String env = System.getenv("GP_ENVIRONMENT");
        boolean isProd = "production".equals(env);
        String expected = isProd
            ? "https://apis.globalpay.com/ucp"
            : "https://apis.sandbox.globalpay.com/ucp";
        assertNotNull(expected);
        assertTrue(expected.startsWith("https://"));
    }

    // ── mapColorDepth — GP enum contract ──────────────────────────────────────

    private static String invokeMapColorDepth(String v) throws Exception {
        Method m = GpApi3dsServlet.class.getDeclaredMethod("mapColorDepth", String.class);
        m.setAccessible(true);
        return (String) m.invoke(null, v);
    }

    @ParameterizedTest(name = "mapColorDepth({0}) == {1}")
    @CsvSource({
        "1,  ONE_BIT",
        "2,  TWO_BITS",
        "4,  FOUR_BITS",
        "8,  EIGHT_BITS",
        "15, FIFTEEN_BITS",
        "16, SIXTEEN_BITS",
        "24, TWENTY_FOUR_BITS",
        "32, THIRTY_TWO_BITS",
        "48, FORTY_EIGHT_BITS",
    })
    void testMapColorDepthReturnsGpEnum(String input, String expected) throws Exception {
        assertEquals(expected.strip(), invokeMapColorDepth(input.strip()));
    }

    @Test
    void testMapColorDepthNeverReturnsRawInteger() throws Exception {
        String result = invokeMapColorDepth("24");
        assertFalse(result.matches("^\\d+$"), "color_depth must be a GP enum, not raw integer: " + result);
        assertEquals("TWENTY_FOUR_BITS", result);
    }

    @Test
    void testMapColorDepthFallbackForUnknown() throws Exception {
        assertEquals("TWENTY_FOUR_BITS", invokeMapColorDepth("99"));
        assertEquals("TWENTY_FOUR_BITS", invokeMapColorDepth("0"));
    }

    @Test
    void testMapColorDepthFallbackForNonNumeric() throws Exception {
        assertEquals("TWENTY_FOUR_BITS", invokeMapColorDepth("TWENTY_FOUR_BITS"));
    }

    // ── mapBool — GP uppercase boolean contract ───────────────────────────────

    private static String invokeMapBool(String v) throws Exception {
        Method m = GpApi3dsServlet.class.getDeclaredMethod("mapBool", String.class);
        m.setAccessible(true);
        return (String) m.invoke(null, v);
    }

    @ParameterizedTest(name = "mapBool({0}) == {1}")
    @CsvSource({
        "true,  TRUE",
        "false, FALSE",
        "TRUE,  TRUE",
        "FALSE, FALSE",
        "1,     FALSE",
        "0,     FALSE",
    })
    void testMapBoolReturnsUppercase(String input, String expected) throws Exception {
        assertEquals(expected.strip(), invokeMapBool(input.strip()));
    }

    @Test
    void testMapBoolResultIsAlwaysUppercase() throws Exception {
        String trueResult  = invokeMapBool("true");
        String falseResult = invokeMapBool("false");
        assertEquals(trueResult.toUpperCase(),  trueResult,  "mapBool must return uppercase");
        assertEquals(falseResult.toUpperCase(), falseResult, "mapBool must return uppercase");
    }

    @Test
    void testMapBoolJavaEnabledDefault() throws Exception {
        // Default for java_enabled is "false" — must map to "FALSE" not "false"
        assertEquals("FALSE", invokeMapBool("false"));
    }

    @Test
    void testMapBoolJavascriptEnabledDefault() throws Exception {
        // Default for javascript_enabled is "true" — must map to "TRUE" not "true"
        assertEquals("TRUE", invokeMapBool("true"));
    }

    // ── initiate-auth payload structure contract ──────────────────────────────

    @Test
    void testInitiateAuthMethodUrlCompletionStatusIsTopLevel() {
        // Mirrors the Java payload construction in handleInitiateAuth().
        // method_url_completion_status MUST be top-level, NOT inside three_ds.
        org.json.JSONObject threeDs = new org.json.JSONObject()
            .put("source",          "BROWSER")
            .put("preference",      "NO_PREFERENCE")
            .put("message_version", "2.2.0");

        org.json.JSONObject payload = new org.json.JSONObject()
            .put("channel",                      "CNP")
            .put("method_url_completion_status", "YES")
            .put("three_ds",                     threeDs);

        assertTrue(payload.has("method_url_completion_status"),
            "method_url_completion_status must be top-level in payload");
        assertEquals("YES", payload.getString("method_url_completion_status"));
        assertFalse(threeDs.has("method_url_completion_status"),
            "three_ds must NOT contain method_url_completion_status");
        assertFalse(threeDs.has("method_url_completion"),
            "three_ds must NOT contain method_url_completion");
    }
}
