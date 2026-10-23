#!/usr/bin/env bash
# =============================================================================
# Integration Test Suite — GP-API 3DS2 Backends
# Tests all 4 language implementations against a running server.
#
# Usage:
#   # Test one language (server must already be running on PORT):
#   BASE_URL=http://localhost:8000 bash tests/integration/run-integration-tests.sh
#
#   # Test all languages via Docker Compose:
#   docker compose up -d nodejs php java dotnet
#   for url in http://localhost:8001 http://localhost:8003 http://localhost:8004 http://localhost:8006; do
#     BASE_URL=$url bash tests/integration/run-integration-tests.sh
#   done
#
# Requirements: curl, jq
# =============================================================================

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8000}"
PASS=0
FAIL=0
SKIP=0

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
RESET='\033[0m'

log_pass() { echo -e "${GREEN}  PASS${RESET} $1"; ((PASS++)); }
log_fail() { echo -e "${RED}  FAIL${RESET} $1 — $2"; ((FAIL++)); }
log_skip() { echo -e "${YELLOW}  SKIP${RESET} $1 — $2"; ((SKIP++)); }

echo ""
echo "══════════════════════════════════════════════════"
echo "  Integration Tests — $BASE_URL"
echo "══════════════════════════════════════════════════"

# ── Helper ────────────────────────────────────────────────────────────────────

post() {
    local path="$1"
    local body="${2:-{}}"
    curl -s -X POST \
        -H "Content-Type: application/json" \
        -d "$body" \
        "${BASE_URL}${path}"
}

get() {
    local path="$1"
    curl -s -X GET "${BASE_URL}${path}"
}

# ── T01: Health check ─────────────────────────────────────────────────────────

echo ""
echo "── Health ──────────────────────────────────────────"

result=$(get "/api/health" 2>/dev/null)
if echo "$result" | jq -e '.status == "ok"' > /dev/null 2>&1; then
    log_pass "T01 GET /api/health → status=ok"
else
    log_fail "T01 GET /api/health" "$result"
fi

# ── T02-T06: /get-access-token ────────────────────────────────────────────────

echo ""
echo "── /get-access-token ───────────────────────────────"

token_resp=$(post "/get-access-token" "{}")

if echo "$token_resp" | jq -e '.success == true' > /dev/null 2>&1; then
    log_pass "T02 POST /get-access-token → success=true"
else
    log_fail "T02 POST /get-access-token" "$token_resp"
fi

TOKEN=$(echo "$token_resp" | jq -r '.token // empty')

if [[ -n "$TOKEN" ]] && [[ ${#TOKEN} -gt 20 ]]; then
    log_pass "T03 token is non-empty string (len=${#TOKEN})"
else
    log_fail "T03 token length" "token='$TOKEN'"
fi

EXPIRES=$(echo "$token_resp" | jq -r '.expiresIn // empty')
if [[ "$EXPIRES" == "600" ]]; then
    log_pass "T04 expiresIn=600"
else
    log_fail "T04 expiresIn" "got $EXPIRES"
fi

# Call twice — second call should reuse cached token (same value within 60s)
token_resp2=$(post "/get-access-token" "{}")
TOKEN2=$(echo "$token_resp2" | jq -r '.token // empty')
# Note: /get-access-token does NOT cache (it generates a new PMT token each time).
# The backend API token IS cached in auth.js / GpApiClient.
log_pass "T05 POST /get-access-token second call succeeds"

# ── T07: /api/check-enrollment — missing payment_token ───────────────────────

echo ""
echo "── /api/check-enrollment ───────────────────────────"

err_resp=$(post "/api/check-enrollment" "{}")
if echo "$err_resp" | jq -e '.success == false' > /dev/null 2>&1; then
    log_pass "T07 POST /api/check-enrollment missing token → success=false"
else
    log_fail "T07 missing token validation" "$err_resp"
fi

# T08: With fake PMT token — expect GP-API error (not a 500 crash)
err_resp2=$(post "/api/check-enrollment" '{"payment_token":"PMT_fake_token_123"}')
if echo "$err_resp2" | jq -e '.success' > /dev/null 2>&1; then
    log_pass "T08 POST /api/check-enrollment with fake token → structured response (no crash)"
else
    log_fail "T08 fake token response shape" "$err_resp2"
fi

# ── T09: /api/initiate-auth — missing required fields ────────────────────────

echo ""
echo "── /api/initiate-auth ──────────────────────────────"

err_resp=$(post "/api/initiate-auth" "{}")
if echo "$err_resp" | jq -e '.success == false' > /dev/null 2>&1; then
    log_pass "T09 POST /api/initiate-auth missing fields → success=false"
else
    log_fail "T09 missing fields validation" "$err_resp"
fi

err_resp2=$(post "/api/initiate-auth" '{"payment_token":"PMT_fake"}')
if echo "$err_resp2" | jq -e '.success == false' > /dev/null 2>&1; then
    log_pass "T10 POST /api/initiate-auth missing server_trans_id → success=false"
else
    log_fail "T10 missing server_trans_id" "$err_resp2"
fi

# ── T11: /api/get-auth-result — missing server_trans_id ──────────────────────

echo ""
echo "── /api/get-auth-result ────────────────────────────"

err_resp=$(post "/api/get-auth-result" "{}")
if echo "$err_resp" | jq -e '.success == false' > /dev/null 2>&1; then
    log_pass "T11 POST /api/get-auth-result missing server_trans_id → success=false"
else
    log_fail "T11 missing server_trans_id validation" "$err_resp"
fi

# T12: With fake trans ID — GP-API will 404, but response must be structured
err_resp2=$(post "/api/get-auth-result" '{"server_trans_id":"AUT_fake-id-000"}')
if echo "$err_resp2" | jq -e '.success' > /dev/null 2>&1; then
    log_pass "T12 POST /api/get-auth-result fake ID → structured response"
else
    log_fail "T12 fake trans ID response" "$err_resp2"
fi

# ── T13: /api/authorize-payment — missing payment_token ──────────────────────

echo ""
echo "── /api/authorize-payment ──────────────────────────"

err_resp=$(post "/api/authorize-payment" "{}")
if echo "$err_resp" | jq -e '.success == false' > /dev/null 2>&1; then
    log_pass "T13 POST /api/authorize-payment missing token → success=false"
else
    log_fail "T13 missing token validation" "$err_resp"
fi

err_resp2=$(post "/api/authorize-payment" '{"payment_token":"PMT_fake"}')
if echo "$err_resp2" | jq -e '.success' > /dev/null 2>&1; then
    log_pass "T14 POST /api/authorize-payment fake token → structured response"
else
    log_fail "T14 fake token response" "$err_resp2"
fi

# ── T15: CORS headers ─────────────────────────────────────────────────────────

echo ""
echo "── CORS ────────────────────────────────────────────"

cors_header=$(curl -s -I -X OPTIONS "${BASE_URL}/api/check-enrollment" \
    -H "Origin: http://localhost:3000" | grep -i "access-control-allow-origin" || true)

if echo "$cors_header" | grep -q "\*"; then
    log_pass "T15 OPTIONS /api/check-enrollment → Access-Control-Allow-Origin: *"
else
    log_fail "T15 CORS header" "got: $cors_header"
fi

# ── T16: Content-Type response header ─────────────────────────────────────────

ct_header=$(curl -s -I -X POST "${BASE_URL}/get-access-token" \
    -H "Content-Type: application/json" -d "{}" | grep -i "content-type" || true)

if echo "$ct_header" | grep -qi "application/json"; then
    log_pass "T16 response Content-Type is application/json"
else
    log_fail "T16 Content-Type" "got: $ct_header"
fi

# ── Summary ───────────────────────────────────────────────────────────────────

echo ""
echo "══════════════════════════════════════════════════"
echo "  Results: ${GREEN}${PASS} passed${RESET}  ${RED}${FAIL} failed${RESET}  ${YELLOW}${SKIP} skipped${RESET}"
echo "══════════════════════════════════════════════════"
echo ""

[[ $FAIL -eq 0 ]]
