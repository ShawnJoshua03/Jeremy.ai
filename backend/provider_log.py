"""Process-local record of which external service actually did work for a session ("the crew").

Clients animate an avatar only for real activity, so only callers that saw a provider succeed or
fail record here. Bounded and in memory, enough for the single-worker demo; a restart forgets it.
"""
from __future__ import annotations

import os
import threading
from collections import OrderedDict
from datetime import UTC, datetime

MAX_SESSIONS = 10_000
MAX_EVENTS = 500  # per session; narrator polling would otherwise grow without bound

_events: OrderedDict[str, list[dict]] = OrderedDict()
_status: dict[str, dict[str, set[str]]] = {}
_lock = threading.Lock()
_apify_live = False


def session_t_ms(row) -> int:
    """Milliseconds since the session started (0 before start), capped at its recorded length."""
    started = getattr(row, "started_at", None)
    if started is None:
        return 0
    if started.tzinfo is None:
        started = started.replace(tzinfo=UTC)
    t = max(0, int((datetime.now(UTC) - started).total_seconds() * 1000))
    elapsed = getattr(row, "elapsed_s", None)
    return min(t, int(elapsed * 1000)) if elapsed is not None else t


def record(sid: str, provider: str, action: str, status: str, t_ms: int = 0) -> None:
    with _lock:
        events = _events.setdefault(sid, [])
        _events.move_to_end(sid)
        if len(events) < MAX_EVENTS:
            events.append({"t_ms": max(0, int(t_ms)), "provider": provider, "action": action, "status": status})
        _status.setdefault(sid, {}).setdefault(provider, set()).add(status)
        while len(_events) > MAX_SESSIONS:
            old, _ = _events.popitem(last=False)
            _status.pop(old, None)


def mark_apify_live() -> None:
    """pricing.refresh_with_apify stored a fresh Apify dataset in this process."""
    global _apify_live
    _apify_live = True


def events(sid: str) -> list[dict]:
    with _lock:
        return sorted(({k: e[k] for k in ("t_ms", "provider", "action")} for e in _events.get(sid, [])),
                      key=lambda e: e["t_ms"])


def providers(sid: str) -> dict[str, str]:
    import pricing
    with _lock:
        seen = {k: set(v) for k, v in _status.get(sid, {}).items()}

    def pick(name: str, order: tuple[str, ...], default: str) -> str:
        return next((s for s in order if s in seen.get(name, ())), default)

    return {
        "claude": pick("claude", ("used", "fallback"), "off"),
        "cartesia": pick("cartesia", ("used", "unavailable"), "off"),
        "apify": "live" if _apify_live else "snapshot" if pricing.load_prices() else "off",
        "convex": pick("convex", ("mirrored",), "off"),
    }


def reset() -> None:
    global _apify_live
    with _lock:
        _events.clear()
        _status.clear()
    _apify_live = False
