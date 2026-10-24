"""Per-session limit on the paid coaching routes (Gemini text, Cartesia audio).

In-process sliding window, enough for the single-worker demo deployment.
COACH_RATE_LIMIT_PER_MIN sets the limit per session per route (default 12; 0 disables).
"""
from __future__ import annotations

import os
import threading
import time
from collections import deque

from fastapi import HTTPException

WINDOW_S = 60.0
_hits: dict[tuple[str, str], deque[float]] = {}
_lock = threading.Lock()


def limit() -> int:
    try:
        return max(0, int(os.environ.get("COACH_RATE_LIMIT_PER_MIN", "12")))
    except ValueError:
        return 12


def check(route: str, sid: str) -> None:
    """Raise 429 when this session has used its quota for this route in the last minute."""
    allowed = limit()
    if allowed == 0:
        return
    now = time.monotonic()
    with _lock:
        hits = _hits.setdefault((route, sid), deque())
        while hits and now - hits[0] >= WINDOW_S:
            hits.popleft()
        if len(hits) >= allowed:
            retry = max(1, int(WINDOW_S - (now - hits[0])) + 1)
            raise HTTPException(429, "too many coaching requests; try again shortly",
                                headers={"Retry-After": str(retry)})
        hits.append(now)
        if len(_hits) > 10_000:  # drop idle keys so memory stays bounded
            for key in [k for k, v in _hits.items() if not v or now - v[-1] >= WINDOW_S]:
                del _hits[key]


def reset() -> None:
    with _lock:
        _hits.clear()
