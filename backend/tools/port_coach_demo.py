"""Print what the port-placement voice coach would say for a few scripted placements. No server, no keys.

    cd backend
    .\\.venv\\Scripts\\python -m tools.port_coach_demo
"""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import port_coach  # noqa: E402

# Ideal sites as in tools/fake_quest.py (board-local metres; x feet, -x head, y up, +z patient right).
SITES = {
    "veress": (0.0, 0.12, 0.0),
    "umbilical": (0.0, 0.12, 0.0),
    "epigastric": (-0.15, 0.11, -0.01),
    "rsc_medial": (-0.11, 0.11, 0.07),
    "rsc_lateral": (-0.06, 0.10, 0.13),
}


def placed(t_ms, port, dx_mm=0.0, dz_mm=0.0, angle=5.0, detail=None):
    x, y, z = SITES[port]
    return {"event_id": f"demo-{t_ms}", "kind": "port_placed", "t_ms": t_ms, "object_id": port,
            "source": (x + dx_mm / 1000, y, z + dz_mm / 1000), "target": SITES[port],
            "value": angle, "detail": detail}


LAST_SESSION = [placed(500, "epigastric", dx_mm=34, angle=10, detail="depth_mm=40")]

SESSION = [
    placed(1000, "veress", dx_mm=2, dz_mm=1, angle=4, detail="depth_mm=31"),           # good
    placed(2000, "umbilical", dx_mm=1, angle=6, detail="depth_mm=26"),                  # too shallow
    placed(3000, "epigastric", dx_mm=22, dz_mm=-2, angle=8, detail="depth_mm=42"),      # 22 mm toward the feet
    placed(4000, "epigastric", dx_mm=6, angle=7, detail="depth_mm=41"),                 # retry, in tolerance
    placed(5000, "rsc_medial", dz_mm=-19, angle=9),                                     # no depth sent
    placed(6000, "rsc_lateral", dx_mm=-4, angle=27, detail="depth_mm=36;tilt=flat"),    # angle too flat
]


def main() -> None:
    out = port_coach.coach_placements(SESSION, LAST_SESSION)
    print("Immediate cues (spoken when each port is placed):")
    for c in out["cues"]:
        print(f"  [{c['port']}, try {c['attempt']}, {c['kind']}] {c['text']}")
    print("\nDebrief (spoken after the last port):")
    for line in out["debrief"]["lines"]:
        print(f"  {line}")


if __name__ == "__main__":
    main()
