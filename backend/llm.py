"""One place that talks to Claude (Anthropic API). Callers get a JSON dict or LLMUnavailable.

Claude only phrases text from facts the backend already computed; every caller validates the
result and falls back to a template, so a slow or failed call never blocks the trainee.
"""
from __future__ import annotations

import json
import os
import time
from typing import NamedTuple

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


class Completion(NamedTuple):
    text: str
    input_tokens: int
    output_tokens: int


_down_until = 0.0  # monotonic time before which complete_text() skips Claude after a failure
_SPEND_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_spend.json")


def spent_usd() -> float:
    """Estimated spend on complete_text() calls so far (illustrative, not billing data)."""
    try:
        with open(os.environ.get("ASK_CLAUDE_SPEND_FILE", _SPEND_FILE), encoding="utf-8") as f:
            return float(json.load(f).get("usd", 0.0))
    except (OSError, ValueError, TypeError):
        return 0.0


def _add_spend(input_tokens: int, output_tokens: int) -> None:
    """Conservative estimate (defaults: $1 per million input tokens, $5 per million output tokens)."""
    usd = (input_tokens * float(os.environ.get("ASK_CLAUDE_IN_USD_PER_MTOK", "1") or 1)
           + output_tokens * float(os.environ.get("ASK_CLAUDE_OUT_USD_PER_MTOK", "5") or 5)) / 1e6
    try:
        with open(os.environ.get("ASK_CLAUDE_SPEND_FILE", _SPEND_FILE), "w", encoding="utf-8") as f:
            json.dump({"usd": round(spent_usd() + usd, 6)}, f)
    except OSError:
        pass


def complete_text(prompt: str, *, max_tokens: int = 160, timeout_s: float = 4.0) -> Completion:
    """One short plain-text reply for the live path (ask-the-coach).

    Raises LLMUnavailable with no key, over the ASK_CLAUDE_BUDGET_USD cap (default 5), during the
    two minutes after a failed call, or on any provider error. The caller falls back to a template.
    """
    global _down_until
    if not configured():
        raise LLMUnavailable("ANTHROPIC_API_KEY is not set")
    if time.monotonic() < _down_until:
        raise LLMUnavailable("backing off after a failure")
    if spent_usd() >= float(os.environ.get("ASK_CLAUDE_BUDGET_USD", "5") or 5):
        raise LLMUnavailable("spend cap reached")
    client = anthropic.Anthropic(timeout=timeout_s, max_retries=0)
    try:
        response = client.messages.create(
            model=os.environ.get("ASK_CLAUDE_MODEL", "").strip() or model(),
            max_tokens=max_tokens,
            messages=[{"role": "user", "content": prompt}],
        )
    except anthropic.APIError as exc:
        _down_until = time.monotonic() + 120.0
        raise LLMUnavailable(type(exc).__name__) from None
    finally:
        client.close()
    _add_spend(response.usage.input_tokens, response.usage.output_tokens)
    if response.stop_reason == "refusal":
        raise LLMUnavailable("refusal")
    text = " ".join(b.text for b in response.content if b.type == "text").strip()
    if not text:
        raise LLMUnavailable("empty response")
    return Completion(text, response.usage.input_tokens, response.usage.output_tokens)
