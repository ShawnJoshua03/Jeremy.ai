import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";

const commandType = v.union(
  v.literal("highlight"),
  v.literal("pause"),
  v.literal("retry"),
  v.literal("annotation"),
);

const sessionMetrics = v.object({
  completionPct: v.number(),
  successfulTransfers: v.number(),
  drops: v.number(),
  invalidReleases: v.number(),
  elapsedS: v.number(),
  distanceByInstrument: v.record(v.string(), v.number()),
  totalDistanceM: v.number(),
  pathEfficiency: v.number(),
  practiceScore: v.number(),
  // lap_chole only (docs/LAP_CHOLE.md)
  details: v.optional(v.record(v.string(), v.union(v.number(), v.string(), v.boolean()))),
});

export default defineSchema({
  sessions: defineTable({
    sessionId: v.string(),
    traineeId: v.string(),
    exercise: v.string(),
    status: v.string(),
    createdAt: v.string(),
    startedAt: v.union(v.string(), v.null()),
    completedAt: v.union(v.string(), v.null()),
    elapsedS: v.union(v.number(), v.null()),
    metrics: v.optional(sessionMetrics),
    updatedAt: v.number(),
  })
    .index("by_session_id", ["sessionId"])
    .index("by_updated_at", ["updatedAt"]),
  events: defineTable({
    sessionId: v.string(),
    eventId: v.string(),
    kind: v.union(
      v.literal("transfer_success"),
      v.literal("drop"),
      v.literal("invalid_release"),
      v.literal("stitch_placed"),
      v.literal("stitch_missed"),
      v.literal("tissue_contact"),
      v.literal("step_completed"),
      v.literal("port_placed"),
      v.literal("insufflation"),
      v.literal("critical_view"),
      v.literal("clip_applied"),
      v.literal("structure_cut"),
      v.literal("injury"),
      v.literal("ductotomy"),
      v.literal("cholangiogram"),
    ),
    tMs: v.number(),
    instrument: v.union(v.literal("left"), v.literal("right"), v.null()),
    objectId: v.union(v.string(), v.null()),
    source: v.union(v.array(v.number()), v.null()),
    target: v.union(v.array(v.number()), v.null()),
    // lap_chole only; the backend omits them when absent
    value: v.optional(v.number()),
    detail: v.optional(v.string()),
    updatedAt: v.number(),
  })
    .index("by_session_and_time", ["sessionId", "tMs"])
    .index("by_event_id", ["eventId"]),
  annotations: defineTable({
    sessionId: v.string(),
    annotationId: v.string(),
    atMs: v.number(),
    text: v.string(),
    author: v.string(),
    createdAt: v.number(),
  })
    .index("by_session_and_time", ["sessionId", "atMs"])
    .index("by_annotation_id", ["annotationId"]),
  commands: defineTable({
    sessionId: v.string(),
    commandId: v.string(),
    type: commandType,
    atMs: v.union(v.number(), v.null()),
    state: v.union(v.literal("queued"), v.literal("acknowledged")),
    createdAt: v.number(),
    acknowledgedAt: v.union(v.number(), v.null()),
    ackStatus: v.optional(v.union(v.literal("applied"), v.literal("rejected"))),
  })
    .index("by_session_and_time", ["sessionId", "createdAt"])
    .index("by_command_id", ["commandId"]),
});
