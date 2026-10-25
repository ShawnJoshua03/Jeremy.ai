"""One place that talks to Claude (Anthropic API). Callers get a JSON dict or LLMUnavailable.

Claude only phrases text from facts the backend already computed; every caller validates the
result and falls back to a template, so a slow or failed call never blocks the trainee.
"""
from __future__ import annotations

import json
import os

import anthropic

DEFAULT_MODEL = "claude-haiku-5-5"  # override with ANTHROPIC_MODEL
TIMEOUT_S = 10.0  # one request; the Quest waits 15 s


class LLMUnavailable(Exception):
    """No key, a provider error, a timeout or an unusable response. Use the template."""


def configured() -> bool:
    return bool(os.environ.get("ANTHROPIC_API_KEY", "").strip())


def model() -> str:
    return os.environ.get("ANTHROPIC_MODEL", "").strip() or DEFAULT_MODEL


def complete_json(prompt: str, schema: dict, *, max_tokens: int = 1024, timeout_s: float = TIMEOUT_S) -> dict:
    """Ask Claude for one JSON object matching `schema` (structured outputs)."""
    if not configured():
        raise LLMUnavailable("ANTHROPIC_API_KEY is not set")
    client = anthropic.Anthropic(timeout=timeout_s, max_retries=1)
    try:
        response = client.messages.create(
            model=model(),
            max_tokens=max_tokens,
            # Short phrasing work: low effort keeps latency near a second.
            output_config={"effort": "low", "format": {"type": "json_schema", "schema": schema}},
            messages=[{"role": "user", "content": prompt}],
        )
    except anthropic.APIError as exc:
        raise LLMUnavailable(type(exc).__name__) from None
    finally:
        client.close()
    if response.stop_reason in ("refusal", "max_tokens"):
        raise LLMUnavailable(response.stop_reason)
    text = next((b.text for b in response.content if b.type == "text"), None)
    try:
        data = json.loads(text) if text else None
    except ValueError:
        data = None
    if not isinstance(data, dict):
        raise LLMUnavailable("response was not a JSON object")
    return data
