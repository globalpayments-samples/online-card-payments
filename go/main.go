// Package main implements Global Payments Drop-In UI for Sale transactions using the Go SDK.
// It provides endpoints for access token generation and payment processing with Drop-In UI.
package main

import (
	"bytes"
	"context"
	"crypto/sha512"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"log"
	"math/rand"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/globalpayments/go-sdk/api"
	"github.com/globalpayments/go-sdk/api/entities/enums/environment"
	"github.com/globalpayments/go-sdk/api/entities/transactions"
	"github.com/globalpayments/go-sdk/api/paymentmethods"
	"github.com/globalpayments/go-sdk/api/serviceconfigs"
	"github.com/globalpayments/go-sdk/api/utils/stringutils"
	"github.com/joho/godotenv"
)

// TokenRequest represents the access token request payload
type TokenRequest struct {
	AppID           string   `json:"app_id"`
	Nonce           string   `json:"nonce"`
	Secret          string   `json:"secret"`
	GrantType       string   `json:"grant_type"`
	SecondsToExpire int      `json:"seconds_to_expire"`
	Permissions     []string `json:"permissions"`
}

// TokenResponse represents the access token response from GP API
type TokenResponse struct {
	Token           string `json:"token"`
	SecondsToExpire int    `json:"seconds_to_expire"`
}

// Response represents a standardized API response
type Response struct {
	Success   bool        `json:"success"`
	Message   string      `json:"message,omitempty"`
	Token     string      `json:"token,omitempty"`
	ExpiresIn int         `json:"expiresIn,omitempty"`
	Data      interface{} `json:"data,omitempty"`
	Error     string      `json:"error,omitempty"`
}

// PaymentRequest represents the payment processing request
type PaymentRequest struct {
	PaymentReference string  `json:"payment_reference"`
	Amount           float64 `json:"amount"`
	Currency         string  `json:"currency"`
}

// PaymentData represents the payment response data
type PaymentData struct {
	TransactionID string `json:"transactionId"`
	Status        string `json:"status"`
	Amount        string `json:"amount"`
	Currency      string `json:"currency"`
	Reference     string `json:"reference"`
	Timestamp     string `json:"timestamp"`
}

// generateNonce creates a random 32-character hexadecimal string
func generateNonce() string {
	rand.Seed(time.Now().UnixNano())
	bytes := make([]byte, 16)
	rand.Read(bytes)
	return hex.EncodeToString(bytes)
}

// hashSecret creates SHA-512 hash of nonce + appKey
func hashSecret(nonce, appKey string) string {
	hash := sha512.New()
	hash.Write([]byte(nonce + appKey))
	return hex.EncodeToString(hash.Sum(nil))
}

// handleGetAccessToken handles the /get-access-token endpoint
func handleGetAccessToken(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	w.Header().Set("Content-Type", "application/json")

	// Generate nonce and secret
	nonce := generateNonce()
	secret := hashSecret(nonce, os.Getenv("GP_APP_KEY"))

	// Build token request
	tokenReq := TokenRequest{
		AppID:           os.Getenv("GP_APP_ID"),
		Nonce:           nonce,
		Secret:          secret,
		GrantType:       "client_credentials",
		SecondsToExpire: 600,
		Permissions:     []string{"PMT_POST_Create_Single"},
	}

	// Determine API endpoint
	apiEndpoint := "https://apis.sandbox.globalpay.com/ucp/accesstoken"
	if os.Getenv("GP_ENVIRONMENT") == "production" {
		apiEndpoint = "https://apis.globalpay.com/ucp/accesstoken"
	}

	// Marshal request
	requestBody, err := json.Marshal(tokenReq)
	if err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Error generating access token",
			Error:   err.Error(),
		})
		return
	}

	// Make API request
	client := &http.Client{Timeout: 10 * time.Second}
	req, err := http.NewRequest("POST", apiEndpoint, bytes.NewBuffer(requestBody))
	if err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Error generating access token",
			Error:   err.Error(),
		})
		return
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("X-GP-Version", "2021-03-22")

	resp, err := client.Do(req)
	if err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Error generating access token",
			Error:   err.Error(),
		})
		return
	}
	defer resp.Body.Close()

	// Parse response
	var tokenResp TokenResponse
	if err := json.NewDecoder(resp.Body).Decode(&tokenResp); err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Error parsing token response",
			Error:   err.Error(),
		})
		return
	}

	if tokenResp.Token == "" {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Failed to generate access token",
			Error:   "No token in response",
		})
		return
	}

	// Return success response
	json.NewEncoder(w).Encode(Response{
		Success:   true,
		Token:     tokenResp.Token,
		ExpiresIn: tokenResp.SecondsToExpire,
	})
}

// handleProcessSale handles the /process-sale endpoint
func handleProcessSale(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	w.Header().Set("Content-Type", "application/json")

	// Parse JSON request
	var paymentReq PaymentRequest
	if err := json.NewDecoder(r.Body).Decode(&paymentReq); err != nil {
		w.WriteHeader(http.StatusBadRequest)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Invalid request",
			Error:   err.Error(),
		})
		return
	}

	// Validate input
	if paymentReq.PaymentReference == "" {
		w.WriteHeader(http.StatusBadRequest)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Missing payment reference",
		})
		return
	}

	if paymentReq.Amount <= 0 {
		w.WriteHeader(http.StatusBadRequest)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Invalid amount",
		})
		return
	}

	if paymentReq.Currency == "" {
		paymentReq.Currency = "USD"
	}

	// Configure Global Payments SDK
	config := serviceconfigs.NewGpApiConfig()
	config.AppId = os.Getenv("GP_APP_ID")
	config.AppKey = os.Getenv("GP_APP_KEY")

	if os.Getenv("GP_ENVIRONMENT") == "production" {
		config.Environment = environment.PRODUCTION
	} else {
		config.Environment = environment.TEST
	}

	config.Channel = "CNP"
	config.Country = "US"

	// Configure service
	err := api.ConfigureService(config, "default")
	if err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Configuration error",
			Error:   err.Error(),
		})
		return
	}

	// Create card data from payment reference token
	card := paymentmethods.NewCreditCardDataWithToken(paymentReq.PaymentReference)

	// Process the charge
	amountStr := strconv.FormatFloat(paymentReq.Amount, 'f', 2, 64)
	val, _ := stringutils.ToDecimalAmount(amountStr)
	transaction := card.ChargeWithAmount(val)
	transaction.WithCurrency(paymentReq.Currency)

	ctx := context.Background()
	response, err := api.ExecuteGateway[transactions.Transaction](ctx, transaction)
	if err != nil {
		w.WriteHeader(http.StatusBadRequest)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Payment processing failed",
			Error:   err.Error(),
		})
		return
	}

	// Check response code
	if response.GetResponseCode() != "00" && response.GetResponseCode() != "SUCCESS" {
		w.WriteHeader(http.StatusBadRequest)
		json.NewEncoder(w).Encode(Response{
			Success: false,
			Message: "Transaction declined",
			Error:   response.GetResponseMessage(),
		})
		return
	}

	// Return success response
	json.NewEncoder(w).Encode(Response{
		Success: true,
		Message: "Payment successful!",
		Data: PaymentData{
			TransactionID: response.GetTransactionId(),
			Status:        response.GetResponseMessage(),
			Amount:        amountStr,
			Currency:      paymentReq.Currency,
			Reference:     response.GetReferenceNumber(),
			Timestamp:     time.Now().Format(time.RFC3339),
		},
	})
}

func main() {
	// Initialize environment configuration
	err := godotenv.Load()
	if err != nil {
		log.Println("Warning: .env file not found, using environment variables")
	}

	// Set up routes
	http.Handle("/", http.FileServer(http.Dir("static")))
	http.HandleFunc("/get-access-token", handleGetAccessToken)
	http.HandleFunc("/process-sale", handleProcessSale)

	// Get port from environment variable or use default
	port := os.Getenv("PORT")
	if port == "" {
		port = "8000"
	}

	log.Printf("✅ Server running at http://localhost:%s", port)
	log.Printf("Environment: %s", getEnv("GP_ENVIRONMENT", "sandbox"))
	log.Fatal(http.ListenAndServe("0.0.0.0:"+port, nil))
}

// getEnv gets an environment variable with a default value
func getEnv(key, defaultValue string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return defaultValue
}
