"""x402 v1 exact USDC on Base Sepolia. Fail closed; verify AND settle."""
from __future__ import annotations

import base64
import binascii
import json
import os
import re
from urllib.parse import urlparse

import httpx
from fastapi import HTTPException
from fastapi.responses import JSONResponse

NETWORK = "base-sepolia"
ASSET = "0x036CbD53842c5426634e7929541eC2318f3dCF7e"
AMOUNT = "10000"  # 0.01 USDC, six decimals; never the illustrative medical cost.
ADDRESS = re.compile(r"0x[0-9a-fA-F]{40}")


class PaymentRequired(Exception):
    pass


def requirements(resource: str) -> dict:
    pay_to = os.getenv("X402_PAY_TO", "").strip()
    if not ADDRESS.fullmatch(pay_to) or int(pay_to, 16) == 0:
        raise HTTPException(503, "X402_PAY_TO is not configured with a recipient address")
    return {"scheme": "exact", "network": NETWORK, "maxAmountRequired": AMOUNT,
            "asset": ASSET, "payTo": pay_to, "resource": resource,
            "description": "Laparoscopic cholecystectomy itemized mistake-cost report",
            "mimeType": "application/json", "outputSchema": None, "maxTimeoutSeconds": 60,
            "extra": {"name": "USDC", "version": "2"}}


def payment_required(required: dict) -> JSONResponse:
    return JSONResponse(status_code=402, content={"x402Version": 1,
                        "error": "A valid, settled X-PAYMENT is required", "accepts": [required]},
                        headers={"Cache-Control": "no-store", "Vary": "X-PAYMENT"})


def _decode(header: str | None, required: dict) -> dict:
    if not header or len(header) > 16384:
        raise PaymentRequired()
    try:
        payload = json.loads(base64.b64decode(header, validate=True))
        if (not isinstance(payload, dict) or type(payload.get("x402Version")) is not int
                or payload["x402Version"] != 1 or payload.get("scheme") != "exact"
                or payload.get("network") != NETWORK):
            raise ValueError("wrong payment protocol")
        inner = payload["payload"]
        auth = inner["authorization"]
        if (not isinstance(inner["signature"], str) or not inner["signature"].startswith("0x")
                or not ADDRESS.fullmatch(auth["from"])
                or auth["to"].lower() != required["payTo"].lower()
                or auth["value"] != AMOUNT
                or not re.fullmatch(r"0x[0-9a-fA-F]{64}", auth["nonce"])
                or not all(isinstance(auth[key], str) and auth[key].isdigit()
                           for key in ("validAfter", "validBefore"))):
            raise ValueError("wrong authorization")
        return payload
    except (ValueError, KeyError, TypeError, AttributeError, binascii.Error):
        raise PaymentRequired() from None


def verify_and_settle(header: str | None, required: dict, client: httpx.Client | None = None) -> dict:
    payload = _decode(header, required)
    facilitator = os.getenv("X402_FACILITATOR_URL", "").strip().rstrip("/")
    url = urlparse(facilitator)
    if url.scheme != "https" or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise HTTPException(503, "X402_FACILITATOR_URL must be a trusted HTTPS x402 v1 facilitator")
    if client is None:
        with httpx.Client(timeout=30) as owned:
            return verify_and_settle(header, required, owned)
    body = {"x402Version": 1, "paymentPayload": payload, "paymentRequirements": required}
    try:
        response = client.post(f"{facilitator}/verify", json=body, timeout=30)
        response.raise_for_status()
        verified = response.json()
        if not isinstance(verified, dict) or verified.get("isValid") is not True:
            raise PaymentRequired()
        # No automatic settlement retries: an HTTP timeout can occur after broadcast.
        response = client.post(f"{facilitator}/settle", json=body, timeout=30)
        response.raise_for_status()
        receipt = response.json()
        if not isinstance(receipt, dict) or receipt.get("success") is not True:
            raise PaymentRequired()
        if (receipt.get("network") != NETWORK
                or not isinstance(receipt.get("transaction"), str)
                or not re.fullmatch(r"0x[0-9a-fA-F]{64}", receipt["transaction"])
                or receipt.get("payer", "").lower() != payload["payload"]["authorization"]["from"].lower()):
            raise ValueError("invalid settlement receipt")
        return receipt
    except (httpx.HTTPError, ValueError, TypeError, AttributeError):
        raise HTTPException(503, "Payment service unavailable or settlement outcome unknown; reconcile before retrying") from None


def receipt_header(receipt: dict) -> str:
    return base64.b64encode(json.dumps(receipt, separators=(",", ":")).encode()).decode("ascii")
