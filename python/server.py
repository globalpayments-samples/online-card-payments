"""
Global Payments Drop-In UI - Sale Transaction (Python)

This Flask application implements Global Payments Drop-In UI integration
for processing Sale transactions using the official Python SDK.
"""

import os
import hashlib
import secrets
import requests
from flask import Flask, request, jsonify, send_from_directory
from flask_cors import CORS
from dotenv import load_dotenv
from globalpayments.api import ServicesContainer
from globalpayments.api.services_config import GpApiConfig
from globalpayments.api.payment_methods import CreditCardData
from globalpayments.api.entities.enums import Channel, Environment
from globalpayments.api.entities.exceptions import ApiException

# Load environment variables
load_dotenv()

# Initialize application
app = Flask(__name__, static_folder='.')
CORS(app)  # Enable CORS for development

@app.route('/')
def index():
    """Serve the main HTML page."""
    return send_from_directory('.', 'index.html')

@app.route('/get-access-token', methods=['POST'])
def get_access_token():
    """
    Generate access token for Drop-In UI (tokenization)
    Uses PMT_POST_Create_Single permission for card tokenization
    """
    try:
        # Generate nonce and secret
        nonce = secrets.token_hex(16)
        secret_string = nonce + os.getenv('GP_APP_KEY')
        secret = hashlib.sha512(secret_string.encode()).hexdigest()

        # Build token request
        token_request = {
            'app_id': os.getenv('GP_APP_ID'),
            'nonce': nonce,
            'secret': secret,
            'grant_type': 'client_credentials',
            'seconds_to_expire': 600,
            'permissions': ['PMT_POST_Create_Single']
        }

        # Determine API endpoint
        api_endpoint = 'https://apis.globalpay.com/ucp/accesstoken' if os.getenv('GP_ENVIRONMENT') == 'production' \
            else 'https://apis.sandbox.globalpay.com/ucp/accesstoken'

        # Make API request
        response = requests.post(
            api_endpoint,
            json=token_request,
            headers={
                'Content-Type': 'application/json',
                'X-GP-Version': '2021-03-22'
            }
        )

        data = response.json()

        if not response.ok or 'token' not in data:
            raise Exception(data.get('error_description', 'Failed to generate access token'))

        return jsonify({
            'success': True,
            'token': data['token'],
            'expiresIn': data.get('seconds_to_expire', 600)
        })

    except Exception as e:
        return jsonify({
            'success': False,
            'message': 'Error generating access token',
            'error': str(e)
        }), 500

@app.route('/process-sale', methods=['POST'])
def process_sale():
    """
    Process Sale transaction using Global Payments SDK
    Uses the payment reference from Drop-In UI to process the charge
    """
    try:
        # Get JSON data
        data = request.get_json()

        # Validate input
        if not data or 'payment_reference' not in data:
            raise ValueError('Missing payment reference')

        if 'amount' not in data or float(data['amount']) <= 0:
            raise ValueError('Invalid amount')

        payment_reference = data['payment_reference']
        amount = float(data['amount'])
        currency = data.get('currency', 'USD')

        # Configure Global Payments SDK
        config = GpApiConfig()
        config.app_id = os.getenv('GP_APP_ID')
        config.app_key = os.getenv('GP_APP_KEY')
        config.environment = Environment.PRODUCTION if os.getenv('GP_ENVIRONMENT') == 'production' \
            else Environment.TEST
        config.channel = Channel.CardNotPresent
        config.country = 'US'

        # Note: Don't set account name - let SDK auto-detect

        # Configure the service
        ServicesContainer.configure(config)

        # Create card data from payment reference token
        card = CreditCardData()
        card.token = payment_reference

        # Process the charge
        response = card.charge(amount) \
            .with_currency(currency) \
            .execute()

        # Check response
        if response.response_code in ['00', 'SUCCESS']:
            return jsonify({
                'success': True,
                'message': 'Payment successful!',
                'data': {
                    'transactionId': response.transaction_id,
                    'status': response.response_message,
                    'amount': amount,
                    'currency': currency,
                    'reference': response.reference_number or '',
                    'timestamp': response.timestamp or ''
                }
            })
        else:
            raise Exception(f"Transaction declined: {response.response_message or 'Unknown error'}")

    except ApiException as e:
        return jsonify({
            'success': False,
            'message': 'Payment processing failed',
            'error': str(e)
        }), 400
    except Exception as e:
        return jsonify({
            'success': False,
            'message': 'Payment processing failed',
            'error': str(e)
        }), 400

# Start server
if __name__ == '__main__':
    port = int(os.getenv('PORT', 8000))
    print(f'✅ Server running at http://localhost:{port}')
    print(f"Environment: {os.getenv('GP_ENVIRONMENT', 'sandbox')}")
    app.run(host='0.0.0.0', port=port, debug=True)
