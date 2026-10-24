"""Replay a synthetic peg-transfer, wound-closure or lap-chole session against a running API.

No headset required. Peg transfer (default): a 6-object transfer with two drops and
alternating-hand tip samples at 25 Hz. Wound closure (--exercise wound_closure): five
stitches on the docs/WOUND_CLOSURE.md geometry, right needle driver and left forceps,
two small bite errors, one tissue contact and one missed bite (then retried).
Laparoscopic cholecystectomy (--exercise lap_chole): the 15 steps of docs/LAP_CHOLE.md
in the video's order, with small port errors, 13.5 mmHg, the critical view reached, a
distal duct clip, a ductotomy below it, a cholangiogram in the duct, two proximal clips,
one crossing clip on the cystic artery (re-clipped) and two safe divisions.
--lap-chole-unsafe puts the first proximal "cystic duct" clip on the common bile duct
instead, which leaves the duct cut unsafe: two major errors and a bile duct injury line.
Uploads the trace, then prints metrics, cost, coach and assessment JSON.

    python tools/fake_quest.py --base-url http://127.0.0.1:8000
    python tools/fake_quest.py --exercise wound_closure
    python tools/fake_quest.py --exercise lap_chole [--lap-chole-unsafe]

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


class _Tips:
    """Both instrument tips moving along straight segments, sampled at 25 Hz."""

    def __init__(self, right: list[float], left: list[float], pace: float):
        self.samples: list[dict] = []
        self.idx = {"left": 0, "right": 0}
        self.t_ms = 0
        self.pos = {"right": right, "left": left}
        self.pace = pace

    def move(self, right_to, left_to, seconds, interaction="idle"):
        steps = max(1, round(seconds * self.pace * HZ))
        start = dict(self.pos)
        goal = {"right": list(right_to), "left": list(left_to)}
        for k in range(1, steps + 1):
            u = k / steps
            for hand in ("right", "left"):
                p = [round(a + (b - a) * u, 4) for a, b in zip(start[hand], goal[hand])]
                self.samples.append({"instrument": hand, "idx": self.idx[hand], "t_ms": self.t_ms, "pos": p,
                                     "valid": True, "interaction": interaction})
                self.idx[hand] += 1
            self.t_ms += DT_MS
        self.pos.update(goal)


def build_wound_trace() -> tuple[list[dict], list[dict], float]:
    """Wound closure in the board-local frame of docs/EXERCISES.md (y up, incision along z at x=0).

    Right hand = needle driver: hover above each entry point, bite at the entry, arc across
    the incision to the exit, lift. Left hand = forceps: moves beside the site and steadies
    the left edge. Each segment is linear at 25 Hz.
    """
    events: list[dict] = []
    tips = _Tips([0.02, 0.05, -0.08], [-0.03, 0.05, -0.08], WC_PACE)
    move = tips.move

    def bite(site, entry_off, exit_off, kind):
        z = WC_SITE_Z[site]
        entry = _add([-0.008, WC_TOP_Y, z], entry_off)
        exit_ = _add([0.008, WC_TOP_Y, z], exit_off)
        forceps = [-0.014, WC_TOP_Y + 0.002, z]
        move([entry[0], 0.03, entry[2]], [forceps[0], 0.02, z], 1.0)
        if site == WC_CONTACT_SITE and kind == "stitch_placed":
            contact = [-0.001, 0.008, round(z - 0.004, 4)]
            move(tips.pos["right"], contact, 0.4)
            events.append({"event_id": f"contact-{site + 1}", "kind": "tissue_contact", "t_ms": tips.t_ms - DT_MS,
                           "instrument": "left", "object_id": None, "source": contact})
        move(tips.pos["right"], forceps, 0.4, "grasping")
        move(entry, forceps, 0.4, "grasping")
        if kind == "stitch_missed":
            events.append({"event_id": f"miss-{site + 1}", "kind": "stitch_missed", "t_ms": tips.t_ms - DT_MS,
                           "instrument": "right", "object_id": f"site_{site + 1}", "source": entry, "target": None})
            move([entry[0], 0.03, entry[2]], forceps, 0.4)
            return
        arc_top = [0.0, WC_TOP_Y + 0.006, round((entry[2] + exit_[2]) / 2, 4)]
        move(arc_top, forceps, 0.4, "grasping")
        move(exit_, forceps, 0.4, "grasping")
        events.append({"event_id": f"stitch-{site + 1}", "kind": "stitch_placed", "t_ms": tips.t_ms - DT_MS,
                       "instrument": "right", "object_id": f"site_{site + 1}", "source": entry, "target": exit_})
        move([exit_[0], 0.03, exit_[2]], forceps, 0.4)

    for site in range(5):
        if site == WC_MISS_SITE:
            bite(site, (0.0, 0.0, 0.011), (0.0, 0.0, 0.0), "stitch_missed")
        bite(site, *WC_BITE_OFFSETS[site], "stitch_placed")
    return tips.samples, events, tips.samples[-1]["t_ms"] / 1000


# ---------- laparoscopic cholecystectomy ----------
# Board-local frame of docs/LAP_CHOLE.md: x toward the feet, y up, +z patient's right;
# the right upper quadrant is x < 0, z > 0. Skin of the abdomen at about y = 0.11.
LC_PORT_SITES = {  # ideal skin entry points
    "veress": (0.0, 0.12, 0.0),
    "umbilical": (0.0, 0.12, 0.0),
    "epigastric": (-0.15, 0.11, -0.01),
    "rsc_medial": (-0.11, 0.11, 0.07),
    "rsc_lateral": (-0.06, 0.10, 0.13),
}
# (actual offset in metres, insertion angle error in degrees): all within 15 mm and 20 degrees
LC_PORT_ERRORS = {
    "veress": ((0.002, 0.0, 0.002), 4.0),
    "umbilical": ((0.003, 0.0, -0.002), 6.0),
    "epigastric": ((-0.006, 0.0, 0.006), 12.0),
    "rsc_medial": ((0.004, 0.0, -0.004), 8.0),
    "rsc_lateral": ((0.008, 0.0, 0.007), 14.0),
}
# Cystic structures run from their proximal end (CBD / hepatic artery junction) toward the gallbladder.
LC_STRUCTURE_LINES = {
    "cystic_duct": ((-0.060, 0.035, 0.030), (-0.085, 0.040, 0.055)),
    "cystic_artery": ((-0.066, 0.040, 0.024), (-0.088, 0.044, 0.046)),
    "cbd": ((-0.040, 0.030, 0.035), (-0.090, 0.030, 0.035)),
}
LC_FUNDUS = (-0.105, 0.055, 0.075)
LC_CALOT = (-0.075, 0.040, 0.040)
LC_PACE = 1.0


def _along(structure: str, mm: float) -> list[float]:
    a, b = LC_STRUCTURE_LINES[structure]
    length = math.dist(a, b) * 1000
    u = mm / length
    return [round(p + (q - p) * u, 4) for p, q in zip(a, b)]


def build_lap_chole_trace(unsafe: bool = False) -> tuple[list[dict], list[dict], float]:
    """The 15 lap-chole steps. Right hand: Veress, trocars, dissector, clip applier, scissors.
    Left hand: grasper on the gallbladder fundus from step 7. Samples at 25 Hz, linear segments."""
    events: list[dict] = []
    tips = _Tips([0.05, 0.20, 0.05], [0.05, 0.20, -0.05], LC_PACE)
    move, pos = tips.move, tips.pos
    n = [0]

    def hold(seconds, interaction="idle"):
        move(pos["right"], pos["left"], seconds, interaction)

    def event(kind, object_id=None, instrument="right", **extra):
        n[0] += 1
        e = {"event_id": f"lc-{n[0]:02d}-{kind}", "kind": kind, "t_ms": tips.t_ms - DT_MS,
             "instrument": instrument, "object_id": object_id}
        e.update({k: v for k, v in extra.items() if v is not None})
        events.append(e)

    def step(step_id):
        event("step_completed", step_id, instrument=None, value=round((tips.t_ms - DT_MS) / 1000, 2))

    def port(port_id, step_id):
        site = LC_PORT_SITES[port_id]
        off, angle = LC_PORT_ERRORS[port_id]
        actual = _add(site, off)
        move([actual[0], actual[1] + 0.04, actual[2]], pos["left"], 3.0)
        move(actual, pos["left"], 2.0, "grasping")
        event("port_placed", port_id, source=actual, target=list(site), value=angle)
        hold(1.0, "grasping")
        step(step_id)
        move([actual[0], actual[1] + 0.03, actual[2]], pos["left"], 1.5)

    def on_structure(kind, structure, mm, **extra):
        """Clip, ductotomy or cut: approach the point on the structure, act, lift off."""
        at = _along(structure, mm)
        move([at[0], at[1] + 0.008, at[2]], pos["left"], 2.0)
        move(at, pos["left"], 1.5, "grasping")
        event(kind, structure, source=at, value=mm, **extra)
        move([at[0], at[1] + 0.008, at[2]], pos["left"], 1.0)

    def clip(structure, mm, detail="ok"):
        on_structure("clip_applied", structure, mm, detail=detail)

    def cholangiogram(depth_mm, result="ok"):
        at = _along("cystic_duct", 20.0)
        move([at[0], at[1] + 0.02, at[2]], pos["left"], 3.0)
        move(at, pos["left"], 4.0, "grasping")  # thread the catheter, inject, review the image
        hold(6.0, "grasping")
        event("cholangiogram", None, value=depth_mm, detail=result)
        move([at[0], at[1] + 0.02, at[2]], pos["left"], 2.0)  # remove the catheter

    port("veress", "veress")
    hold(10.0)  # gas running
    event("insufflation", None, instrument=None, value=13.5)
    step("insufflate")
    port("umbilical", "port_umbilical")
    port("epigastric", "port_epigastric")
    port("rsc_medial", "port_rsc_medial")
    port("rsc_lateral", "port_rsc_lateral")
    # step 7: left grasper takes the fundus and retracts it cephalad over the liver
    move(pos["right"], LC_FUNDUS, 4.0)
    move(pos["right"], [LC_FUNDUS[0] - 0.03, LC_FUNDUS[1] + 0.02, LC_FUNDUS[2]], 3.0, "grasping")
    step("retract")
    # step 8: dissect Calot's triangle in small strokes until the critical view
    for pct in (35.0, 70.0, 100.0):
        for k in range(6):
            dz = 0.006 if k % 2 else -0.006
            move([LC_CALOT[0] + 0.004 * (k % 3 - 1), LC_CALOT[1], LC_CALOT[2] + dz], pos["left"], 1.2, "grasping")
        event("critical_view", None, value=pct)
    step("dissect")
    # step 9: one clip on the cystic duct near the gallbladder (24 mm from the CBD)
    clip("cystic_duct", 24.0)
    step("clip_duct_distal")
    # step 10: ductotomy just below that clip
    on_structure("ductotomy", "cystic_duct", 20.0)
    step("ductotomy")
    # step 11: cholangiogram catheter through the ductotomy, contrast, review, remove
    cholangiogram(12.0)
    step("cholangiogram")
    # step 12: two proximal clips (6 and 9 mm), CBD side of the ductotomy
    if unsafe:
        clip("cbd", 20.0)  # mistakes the CBD for the cystic duct: major error
    else:
        clip("cystic_duct", 6.0)
    clip("cystic_duct", 9.0)
    step("clip_duct_proximal")
    # step 13: divide the duct between the proximal clips and the ductotomy
    on_structure("structure_cut", "cystic_duct", 16.0)
    step("divide_duct")
    # step 14: artery clips; the second does not cross the artery, so it is re-applied
    clip("cystic_artery", 5.0)
    clip("cystic_artery", 8.0, "crossing")
    clip("cystic_artery", 10.0)
    clip("cystic_artery", 22.0)
    step("clip_artery")
    # step 15: divide the artery between its clips
    on_structure("structure_cut", "cystic_artery", 16.0)
    step("divide_artery")
    hold(2.0)
    return tips.samples, events, tips.samples[-1]["t_ms"] / 1000


def build_for(exercise: str, lap_chole_unsafe: bool = False) -> tuple[list[dict], list[dict], float]:
    if exercise == "wound_closure":
        return build_wound_trace()
    if exercise == "lap_chole":
        return build_lap_chole_trace(lap_chole_unsafe)
    return build_trace()


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
           exercise: str = "peg_transfer", lap_chole_unsafe: bool = False) -> dict:
    if not client_request_id:
        client_request_id = uuid.uuid4().hex
    headers = _api_key_headers()
    created = _ok(client.post("/api/v1/sessions", json={
        "trainee_id": trainee_id,
        "exercise": exercise,
        "client_request_id": client_request_id,
    }, headers=headers))
    sid = created["id"]
    samples, events, elapsed_s = build_for(exercise, lap_chole_unsafe)
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
    parser = argparse.ArgumentParser(description="Replay a synthetic peg transfer, wound closure or lap chole")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000", help="Running API origin")
    parser.add_argument("--trainee-id", default="fake-quest")
    parser.add_argument("--exercise", choices=("peg_transfer", "wound_closure", "lap_chole"), default="peg_transfer")
    parser.add_argument("--lap-chole-unsafe", action="store_true",
                        help="lap_chole only: clip the common bile duct instead of the cystic duct (major errors)")
    parser.add_argument("--client-request-id", default=None, help="Reuse this create id when a response was lost")
    args = parser.parse_args()
    report = replay(
        UrllibClient(args.base_url),
        trainee_id=args.trainee_id,
        client_request_id=args.client_request_id,
        exercise=args.exercise,
        lap_chole_unsafe=args.lap_chole_unsafe,
    )
    print(json.dumps(
        {"session_id": report["session_id"], "exercise": report["exercise"], "metrics": report["metrics"],
         "cost": report["cost"], "coach": report["coach"], "assessment": report["assessment"]},
        indent=2,
    ))


if __name__ == "__main__":
    main()
