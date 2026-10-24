"""Best-effort copy of stored rows to Convex.

SQLite stays the source of truth. Quest polls the mentorship routes.
CONVEX_URL and CONVEX_INGEST_SHARED_SECRET (old name CONVEX_DEPLOY_KEY) are
optional. If either is unset, this module does nothing. The secret is sent as the bearer token and is
never written to disk here. A failure does not propagate to the caller.
"""
from __future__ import annotations

import logging
import os
import time
from datetime import UTC, datetime

import httpx

log = logging.getLogger(__name__)

_COMMAND_TYPES = {
    "highlight_target": "highlight",
    "highlight": "highlight",
    "request_pause": "pause",
    "pause": "pause",
    "request_retry": "retry",
    "retry": "retry",
    "add_annotation": "annotation",
}


def _iso(value: datetime | None) -> str | None:
    if value is None:
        return None
    if isinstance(value, datetime):
        return value.isoformat()
    return str(value)


def _epoch_ms(value: datetime | None) -> int:
    if isinstance(value, datetime):
        if value.tzinfo is None:
            value = value.replace(tzinfo=UTC)
        return int(value.timestamp() * 1000)
    return int(time.time() * 1000)


def _session(row) -> dict:
    payload = {
        "sessionId": row.id,
        "traineeId": row.trainee_id,
        "exercise": row.exercise,
        "status": row.status,
        "createdAt": _iso(row.created_at),
        "startedAt": _iso(row.started_at),
        "completedAt": _iso(row.completed_at),
        "elapsedS": row.elapsed_s,
    }
    m = row.metrics
    if m:
        payload["metrics"] = {
            "completionPct": m["completion_pct"],
            "successfulTransfers": m["successful_transfers"],
            "drops": m["drops"],
            "invalidReleases": m["invalid_releases"],
            "elapsedS": m["elapsed_s"],
            "distanceByInstrument": m["distance_by_instrument"],
            "totalDistanceM": m["total_distance_m"],
            "pathEfficiency": m["path_efficiency"],
            "practiceScore": m["practice_score"],
        }
        if m.get("details"):  # lap_chole only; Convex declares it optional
            payload["metrics"]["details"] = m["details"]
    return payload


def _event(row: dict) -> dict:
    return {
        "sessionId": row["session_id"],
        "eventId": row["event_id"],
        "kind": row["kind"],
        "tMs": row["t_ms"],
        "instrument": row.get("instrument"),
        "objectId": row.get("object_id"),
        "source": row.get("source"),
        "target": row.get("target"),
        # lap_chole fields; sent only when present because Convex rejects null for v.optional
        **({"value": row["value"]} if row.get("value") is not None else {}),
        **({"detail": row["detail"]} if row.get("detail") is not None else {}),
    }


def _command(row) -> dict | None:
    kind = _COMMAND_TYPES.get(row.type)
    if kind is None:
        return None
    acked = row.ack_status is not None
    return {
        "sessionId": row.session_id,
        "commandId": row.command_id,
        "type": kind,
        "atMs": None,
        "state": "acknowledged" if acked else "queued",
        "createdAt": _epoch_ms(row.created_at),
        "acknowledgedAt": int(time.time() * 1000) if acked else None,
        **({"ackStatus": row.ack_status} if acked else {}),
    }


def _annotation_from_command(row) -> dict | None:
    # Notes from POST /annotations are already mirrored as annotation rows.
    if row.type != "add_annotation" or not (row.message or "").strip() or row.command_id.startswith("ann_"):
        return None
    return {
        "sessionId": row.session_id,
        "annotationId": row.command_id,
        "atMs": 0,
        "text": row.message,
        "author": "instructor",
        "createdAt": _epoch_ms(row.created_at),
    }


def _annotation(row) -> dict:
    return {
        "sessionId": row.session_id,
        "annotationId": row.annotation_id,
        "atMs": row.at_ms,
        "text": row.text,
        "author": row.author,
        "createdAt": _epoch_ms(row.created_at),
    }


def post_snapshot(sessions: list, events: list, annotations: list, commands: list,
                  session_only: bool = False) -> None:
    if not session_only and not events and not annotations and not commands:
        return
    url = os.getenv("CONVEX_URL", "").strip()
    # Must equal the Convex deployment's CONVEX_INGEST_SHARED_SECRET. CONVEX_DEPLOY_KEY is the old name.
    key = (os.getenv("CONVEX_INGEST_SHARED_SECRET", "") or os.getenv("CONVEX_DEPLOY_KEY", "")).strip()
    if not url or not key:
        return
    if not url.startswith("https://") and not url.startswith("http://"):
        return
    endpoint = url.rstrip("/")
    if not endpoint.endswith("/v1/mirror"):
        endpoint += "/v1/mirror"
    try:
        response = httpx.post(
            endpoint,
            json={
                "sessions": sessions,
                "events": events,
                "annotations": annotations,
                "commands": commands,
            },
            headers={"Authorization": "Bearer " + key},
            timeout=3.0,
            follow_redirects=False,
        )
        if response.status_code >= 300:
            log.warning("Convex mirror rejected the snapshot: HTTP %s", response.status_code)
    except Exception as exc:
        log.warning("Convex mirror unreachable: %s", type(exc).__name__)
        return


def mirror_session(session_row) -> None:
    """Mirror a status change (create, start, complete, cancel) on its own."""
    try:
        post_snapshot([_session(session_row)], [], [], [], session_only=True)
    except Exception:
        return


def mirror_events(session_row, events: list[dict]) -> None:
    try:
        post_snapshot([_session(session_row)], [_event(row) for row in events], [], [])
    except Exception:
        return


def mirror_command(session_row, command_row) -> None:
    try:
        command = _command(command_row)
        annotation = _annotation_from_command(command_row)
        post_snapshot(
            [_session(session_row)],
            [],
            [annotation] if annotation else [],
            [command] if command else [],
        )
    except Exception:
        return


def mirror_annotation(session_row, annotation_row, command_row=None) -> None:
    """Mirror a note, plus the headset command it queued, in one request."""
    try:
        command = _command(command_row) if command_row is not None else None
        post_snapshot([_session(session_row)], [], [_annotation(annotation_row)], [command] if command else [])
    except Exception:
        return
