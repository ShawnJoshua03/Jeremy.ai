"""Coaching text. Gemini phrases feedback; it never produces scores.

The prompt contains only deterministic metrics and real event IDs. Output is
validated server-side: unknown event IDs are dropped, and if Gemini is not
configured or fails, a rule-based fallback is returned so coaching never blocks.
"""
from __future__ import annotations

import json
import os

import httpx
from pydantic import BaseModel, Field, ValidationError

GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
DEFAULT_MODEL = "gemini-3.5-flash"  # override with GEMINI_MODEL; not verified against the live API yet


class Tip(BaseModel):
    text: str = Field(max_length=240)
    event_id: str | None = None


class Coaching(BaseModel):
    summary: str = Field(max_length=400)
    tips: list[Tip] = Field(max_length=4)
    source: str = "fallback"  # gemini | fallback


_SCHEMA = {
    "type": "OBJECT",
    "properties": {
        "summary": {"type": "STRING"},
        "tips": {
            "type": "ARRAY",
            "items": {
                "type": "OBJECT",
                "properties": {"text": {"type": "STRING"}, "event_id": {"type": "STRING", "nullable": True}},
                "required": ["text"],
            },
        },
    },
    "required": ["summary", "tips"],
}


def fallback(metrics: dict, errors: list[dict]) -> Coaching:
    tips = [
        Tip(text=f"A {e['kind'].replace('_', ' ')} at {e['t_ms'] / 1000:.1f}s. Slow down and confirm the grasp before moving.",
            event_id=e["event_id"])
        for e in errors[:3]
    ]
    if metrics["path_efficiency"] < 0.5:
        tips.append(Tip(text="Your instrument paths were indirect. Move straight from source to target."))
    summary = (f"{metrics['successful_transfers']} of 6 transfers in {metrics['elapsed_s']:.0f}s "
               f"with {metrics['drops'] + metrics['invalid_releases']} errors. Practice score {metrics['practice_score']:.0f}/100.")
    return Coaching(summary=summary, tips=tips[:4], source="fallback")


def _prompt(metrics: dict, errors: list[dict]) -> str:
    return (
        "You coach a trainee after a virtual peg-transfer practice drill. Use ONLY the data below. "
        "Do not invent numbers, scores, or clinical claims. Be brief and encouraging. "
        "Give at most 4 tips; set event_id only to an ID from errors, else null.\n"
        + json.dumps({"metrics": metrics, "errors": errors})
    )


def validate(raw: dict, errors: list[dict]) -> Coaching:
    valid_ids = {e["event_id"] for e in errors}
    c = Coaching.model_validate({**raw, "source": "gemini"})
    for t in c.tips:
        if t.event_id not in valid_ids:
            t.event_id = None
    return c


def generate(metrics: dict, errors: list[dict], client: httpx.Client | None = None) -> Coaching:
    key = os.environ.get("GEMINI_API_KEY")
    if not key:
        return fallback(metrics, errors)
    model = os.environ.get("GEMINI_MODEL", DEFAULT_MODEL)
    body = {
        "contents": [{"parts": [{"text": _prompt(metrics, errors)}]}],
        "generationConfig": {"responseMimeType": "application/json", "responseSchema": _SCHEMA, "temperature": 0.3},
    }
    try:
        c = client or httpx.Client(timeout=25)
        r = c.post(GEMINI_URL.format(model=model), json=body, headers={"x-goog-api-key": key})
        r.raise_for_status()
        text = r.json()["candidates"][0]["content"]["parts"][0]["text"]
        return validate(json.loads(text), errors)
    except (httpx.HTTPError, KeyError, IndexError, ValueError, ValidationError):
        return fallback(metrics, errors)
