import type { DemoSession, Sample } from "./types";

function samples(
  instrument: Sample["instrument"],
  points: Array<[number, number, number, number]>,
): Sample[] {
  return points.map(([t_ms, x, y, z], idx) => ({
    instrument,
    idx,
    t_ms,
    pos: [x, y, z],
    valid: true,
    interaction: idx % 4 === 0 ? "grasping" : "moving",
  }));
}

export const demoSessions: DemoSession[] = [
  {
    session: {
      id: "preview-maya-chen",
      trainee_id: "Maya Chen",
      exercise: "peg_transfer",
      status: "running",
      created_at: "2026-10-24T14:22:00Z",
      started_at: "2026-10-24T14:22:00Z",
      completed_at: null,
      elapsed_s: null,
    },
    metrics: {
      completion_pct: 50,
      successful_transfers: 3,
      drops: 1,
      invalid_releases: 1,
      elapsed_s: 142,
      distance_by_instrument: { left: 0.42, right: 0.38 },
      total_distance_m: 0.8,
      path_efficiency: 0.64,
      practice_score: 55.7,
    },
    samples: [
      ...samples("left", [
        [0, -0.21, 0.03, -0.12],
        [9000, -0.16, 0.04, -0.08],
        [18000, -0.10, 0.05, -0.04],
        [31000, -0.04, 0.05, 0.01],
        [47000, 0.02, 0.06, 0.04],
        [61000, 0.06, 0.06, 0.08],
        [79000, 0.12, 0.05, 0.04],
        [96000, 0.18, 0.05, 0.01],
        [118000, 0.22, 0.04, -0.04],
        [142000, 0.16, 0.04, -0.09],
      ]),
      ...samples("right", [
        [0, 0.19, 0.02, -0.11],
        [11000, 0.12, 0.03, -0.08],
        [24000, 0.08, 0.03, -0.02],
        [39000, 0.03, 0.04, 0.03],
        [52000, -0.02, 0.05, 0.07],
        [68000, -0.08, 0.05, 0.03],
        [87000, -0.13, 0.04, -0.02],
        [104000, -0.18, 0.03, -0.07],
        [129000, -0.11, 0.03, -0.12],
      ]),
    ].sort((a, b) => a.t_ms - b.t_ms),
    events: [
      {
        event_id: "preview-drop-1",
        kind: "drop",
        t_ms: 61000,
        instrument: "left",
        object_id: "peg-4",
      },
      {
        event_id: "preview-invalid-1",
        kind: "invalid_release",
        t_ms: 104000,
        instrument: "right",
        object_id: "peg-5",
      },
    ],
  },
  {
    session: {
      id: "preview-noah-patel",
      trainee_id: "Noah Patel",
      exercise: "peg_transfer",
      status: "completed",
      created_at: "2026-10-24T13:48:00Z",
      started_at: "2026-10-24T13:48:00Z",
      completed_at: "2026-10-24T13:51:28Z",
      elapsed_s: 208,
    },
    metrics: {
      completion_pct: 100,
      successful_transfers: 6,
      drops: 0,
      invalid_releases: 0,
      elapsed_s: 208,
      distance_by_instrument: { left: 0.36, right: 0.31 },
      total_distance_m: 0.67,
      path_efficiency: 0.71,
      practice_score: 78.9,
    },
    samples: [],
    events: [],
  },
  {
    session: {
      id: "preview-elena-ruiz",
      trainee_id: "Elena Ruiz",
      exercise: "peg_transfer",
      status: "ready",
      created_at: "2026-10-24T13:41:00Z",
      started_at: null,
      completed_at: null,
      elapsed_s: null,
    },
    metrics: {
      completion_pct: 0,
      successful_transfers: 0,
      drops: 0,
      invalid_releases: 0,
      elapsed_s: 0,
      distance_by_instrument: {},
      total_distance_m: 0,
      path_efficiency: 0,
      practice_score: 0,
    },
    samples: [],
    events: [],
  },
];

const maya = demoSessions[0];
console.assert(
  maya.metrics.successful_transfers + maya.metrics.drops +
    maya.metrics.invalid_releases === 5,
  "Demo Maya event count should match the five completed or failed attempts.",
);
