"""Replay a synthetic peg-transfer or wound-closure session against a running API.

No headset required. Peg transfer (default): a 6-object transfer with two drops and
alternating-hand tip samples at 25 Hz. Wound closure (--exercise wound_closure): five
stitches on the docs/WOUND_CLOSURE.md geometry, right needle driver and left forceps,
two small bite errors, one tissue contact and one missed bite (then retried).
Uploads the trace, then prints metrics, cost, coach and assessment JSON.

    python tools/fake_quest.py --base-url http://127.0.0.1:8000
    python tools/fake_quest.py --exercise wound_closure

Live Gemini and Cartesia are not called by this script. Coach output is whatever
the server returns (the rule-based fallback when GEMINI_API_KEY is unset).
When API_KEY is set, each call sends X-API-Key. The script does not print that key.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import urllib.error
import urllib.request
import uuid

HZ = 25
DT_MS = 1000 // HZ  # 40 ms
OBJECTS = 6
DROP_OBJECTS = (1, 4)  # zero-based indexes; two drops


def build_trace() -> tuple[list[dict], list[dict], float]:
    """Transfers laid out like quest/Assets/Scripts/Exercises/PegBoard.cs, in the board's local frame.

    Sources sit at x=-0.10 m and targets at x=+0.10 m, one row per peg along z; y is up.
    Hands alternate (even pegs left, odd pegs right). Each move lifts in a small arc and
    takes one second. Object IDs and interaction names match the Unity client.
    """
    samples: list[dict] = []
    events: list[dict] = []
    t_ms = 0
    idx = {"left": 0, "right": 0}
    for i in range(OBJECTS):
        hand = "left" if i % 2 == 0 else "right"
        z = round((i - 2.5) * 0.065, 4)
        source = [-0.10, 0.025, z]
        target = [0.10, 0.025, z]
        for step in range(HZ):
            u = step / (HZ - 1)
            pos = [
                round(source[0] + (target[0] - source[0]) * u, 4),
                round(0.025 + 0.04 * math.sin(math.pi * u), 4),
                z,
            ]
            samples.append({
                "instrument": hand,
                "idx": idx[hand],
                "t_ms": t_ms,
                "pos": pos,
                "valid": True,
                "interaction": "grasping" if step else "idle",
            })
            if i in DROP_OBJECTS and step == HZ // 2:
                events.append({
                    "event_id": f"drop-{i + 1}",
                    "kind": "drop",
                    "t_ms": t_ms,
                    "instrument": hand,
                    "object_id": f"peg_{i + 1}",
                })
            idx[hand] += 1
            t_ms += DT_MS
        events.append({
            "event_id": f"xfer-{i + 1}",
            "kind": "transfer_success",
            "t_ms": t_ms - DT_MS,
            "instrument": hand,
            "object_id": f"peg_{i + 1}",
            "source": source,
            "target": target,
        })
    elapsed_s = (samples[-1]["t_ms"]) / 1000
    return samples, events, elapsed_s


WC_SITE_Z = [round(-0.05 + 0.025 * i, 4) for i in range(5)]
WC_TOP_Y = 0.012
# (site index, entry offset, exit offset) in metres added to the contract points
WC_BITE_OFFSETS = {
    0: ((0.0005, 0.0, 0.0), (-0.0005, 0.0, 0.0005)),
    1: ((0.0, 0.0, 0.0025), (0.001, 0.0, 0.0)),        # 2.5 mm entry error
    2: ((0.0, 0.0, 0.0), (0.0, 0.0, -0.0005)),
    3: ((-0.0005, 0.0, 0.0), (0.0, 0.0, 0.005)),       # 5 mm exit error: placed, not good
    4: ((0.0, 0.0, -0.0005), (0.0005, 0.0, 0.0)),
}
WC_MISS_SITE = 2  # first try at site_3 bites 11 mm off and is missed, then retried
WC_CONTACT_SITE = 3  # forceps dips into the wound edge while moving to site_4
WC_PACE = 2.5  # slows every segment to a novice-like ~45 s attempt
UPLOAD_CHUNK = 1000  # server MAX_BATCH


def _add(a, b):
    return [round(x + y, 4) for x, y in zip(a, b)]


def build_wound_trace() -> tuple[list[dict], list[dict], float]:
    """Wound closure in the board-local frame of docs/WOUND_CLOSURE.md (y up, incision along z at x=0).

    Right hand = needle driver: hover above each entry point, bite at the entry, arc across
    the incision to the exit, lift. Left hand = forceps: moves beside the site and steadies
    the left edge. Each segment is linear at 25 Hz.
    """
    samples: list[dict] = []
    events: list[dict] = []
    idx = {"left": 0, "right": 0}
    t = [0]
    pos = {"right": [0.02, 0.05, -0.08], "left": [-0.03, 0.05, -0.08]}

    def move(right_to, left_to, seconds, interaction="idle"):
        steps = max(1, round(seconds * WC_PACE * HZ))
        start = {"right": pos["right"], "left": pos["left"]}
        goal = {"right": right_to, "left": left_to}
        for k in range(1, steps + 1):
            u = k / steps
            for hand in ("right", "left"):
                p = [round(a + (b - a) * u, 4) for a, b in zip(start[hand], goal[hand])]
                samples.append({"instrument": hand, "idx": idx[hand], "t_ms": t[0], "pos": p,
                                "valid": True, "interaction": interaction})
                idx[hand] += 1
            t[0] += DT_MS
        pos["right"], pos["left"] = list(right_to), list(left_to)

    def bite(site, entry_off, exit_off, kind):
        z = WC_SITE_Z[site]
        entry = _add([-0.008, WC_TOP_Y, z], entry_off)
        exit_ = _add([0.008, WC_TOP_Y, z], exit_off)
        forceps = [-0.014, WC_TOP_Y + 0.002, z]
        move([entry[0], 0.03, entry[2]], [forceps[0], 0.02, z], 1.0)
        if site == WC_CONTACT_SITE and kind == "stitch_placed":
            contact = [-0.001, 0.008, round(z - 0.004, 4)]
            move(pos["right"], contact, 0.4)
            events.append({"event_id": f"contact-{site + 1}", "kind": "tissue_contact", "t_ms": t[0] - DT_MS,
                           "instrument": "left", "object_id": None, "source": contact})
        move(pos["right"], forceps, 0.4, "grasping")
        move(entry, forceps, 0.4, "grasping")
        if kind == "stitch_missed":
            events.append({"event_id": f"miss-{site + 1}", "kind": "stitch_missed", "t_ms": t[0] - DT_MS,
                           "instrument": "right", "object_id": f"site_{site + 1}", "source": entry, "target": None})
            move([entry[0], 0.03, entry[2]], forceps, 0.4)
            return
        arc_top = [0.0, WC_TOP_Y + 0.006, round((entry[2] + exit_[2]) / 2, 4)]
        move(arc_top, forceps, 0.4, "grasping")
        move(exit_, forceps, 0.4, "grasping")
        events.append({"event_id": f"stitch-{site + 1}", "kind": "stitch_placed", "t_ms": t[0] - DT_MS,
                       "instrument": "right", "object_id": f"site_{site + 1}", "source": entry, "target": exit_})
        move([exit_[0], 0.03, exit_[2]], forceps, 0.4)

    for site in range(5):
        if site == WC_MISS_SITE:
            bite(site, (0.0, 0.0, 0.011), (0.0, 0.0, 0.0), "stitch_missed")
        bite(site, *WC_BITE_OFFSETS[site], "stitch_placed")
    elapsed_s = samples[-1]["t_ms"] / 1000
    return samples, events, elapsed_s


def _api_key_headers() -> dict[str, str]:
    key = os.environ.get("API_KEY", "")
    if not key.strip():
        return {}
    return {"X-API-Key": key}


class UrllibClient:
    """Minimal client matching TestClient.post/get for a running server."""

    def __init__(self, base_url: str):
        self.base = base_url.rstrip("/")

    def post(self, path: str, json: dict | None = None, headers: dict | None = None):
        return self._send("POST", path, json, headers)

    def get(self, path: str, headers: dict | None = None):
        return self._send("GET", path, None, headers)

    def _send(self, method: str, path: str, body: dict | None, extra: dict | None):
        data = None if body is None else json.dumps(body).encode()
        headers = {"Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        headers.update(_api_key_headers())
        if extra:
            headers.update(extra)
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req) as resp:
                raw = resp.read().decode()
                status = resp.status
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode()
            status = exc.code
        payload = json.loads(raw) if raw else None

        class Response:
            status_code = status

            def json(self):
                return payload

        return Response()


def _ok(resp):
    if resp.status_code not in (200, 201):
        raise SystemExit(f"HTTP {resp.status_code}: {resp.json()}")
    return resp.json()


def replay(client, trainee_id: str = "fake-quest", client_request_id: str | None = None,
           exercise: str = "peg_transfer") -> dict:
    if not client_request_id:
        client_request_id = uuid.uuid4().hex
    headers = _api_key_headers()
    created = _ok(client.post("/api/v1/sessions", json={
        "trainee_id": trainee_id,
        "exercise": exercise,
        "client_request_id": client_request_id,
    }, headers=headers))
    sid = created["id"]
    samples, events, elapsed_s = build_wound_trace() if exercise == "wound_closure" else build_trace()
    if created["status"] == "completed":
        # A finished session rejects another upload. This trace is the one already stored.
        uploaded_samples = {"accepted": 0, "duplicates": len(samples)}
        uploaded_events = {"accepted": 0, "duplicates": len(events)}
        metrics = _ok(client.get(f"/api/v1/sessions/{sid}/metrics", headers=headers))
    else:
        _ok(client.post(f"/api/v1/sessions/{sid}/start", headers=headers))
        uploaded_samples = {"accepted": 0, "duplicates": 0}
        for i in range(0, len(samples), UPLOAD_CHUNK):
            part = _ok(client.post(
                f"/api/v1/sessions/{sid}/samples", json={"samples": samples[i:i + UPLOAD_CHUNK]}, headers=headers,
            ))
            uploaded_samples = {k: uploaded_samples[k] + part[k] for k in uploaded_samples}
        uploaded_events = _ok(client.post(
            f"/api/v1/sessions/{sid}/events", json={"events": events}, headers=headers,
        ))
        metrics = _ok(client.post(
            f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": elapsed_s}, headers=headers,
        ))
    cost = _ok(client.get(f"/api/v1/sessions/{sid}/cost", headers=headers))
    coach = _ok(client.post(f"/api/v1/sessions/{sid}/coach", headers=headers))
    assessment = _ok(client.get(f"/api/v1/sessions/{sid}/assessment", headers=headers))
    return {
        "exercise": exercise,
        "session_id": sid,
        "elapsed_s": elapsed_s,
        "sample_upload": uploaded_samples,
        "event_upload": uploaded_events,
        "metrics": metrics,
        "cost": cost,
        "coach": coach,
        "assessment": assessment,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Replay a synthetic peg transfer or wound closure")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000", help="Running API origin")
    parser.add_argument("--trainee-id", default="fake-quest")
    parser.add_argument("--exercise", choices=("peg_transfer", "wound_closure"), default="peg_transfer")
    parser.add_argument("--client-request-id", default=None, help="Reuse this create id when a response was lost")
    args = parser.parse_args()
    report = replay(
        UrllibClient(args.base_url),
        trainee_id=args.trainee_id,
        client_request_id=args.client_request_id,
        exercise=args.exercise,
    )
    print(json.dumps(
        {"session_id": report["session_id"], "exercise": report["exercise"], "metrics": report["metrics"],
         "cost": report["cost"], "coach": report["coach"], "assessment": report["assessment"]},
        indent=2,
    ))


if __name__ == "__main__":
    main()
