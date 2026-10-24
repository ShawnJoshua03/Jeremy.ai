import { internalMutationGeneric, queryGeneric } from "convex/server";
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
});

const session = v.object({
  sessionId: v.string(),
  traineeId: v.string(),
  exercise: v.string(),
  status: v.string(),
  createdAt: v.string(),
  startedAt: v.union(v.string(), v.null()),
  completedAt: v.union(v.string(), v.null()),
  elapsedS: v.union(v.number(), v.null()),
  metrics: v.optional(sessionMetrics),
});

const event = v.object({
  sessionId: v.string(),
  eventId: v.string(),
  kind: v.union(
    v.literal("transfer_success"),
    v.literal("drop"),
    v.literal("invalid_release"),
  ),
  tMs: v.number(),
  instrument: v.union(v.literal("left"), v.literal("right"), v.null()),
  objectId: v.union(v.string(), v.null()),
  source: v.union(v.array(v.number()), v.null()),
  target: v.union(v.array(v.number()), v.null()),
});

const annotation = v.object({
  sessionId: v.string(),
  annotationId: v.string(),
  atMs: v.number(),
  text: v.string(),
  author: v.string(),
  createdAt: v.number(),
});

const command = v.object({
  sessionId: v.string(),
  commandId: v.string(),
  type: commandType,
  atMs: v.union(v.number(), v.null()),
  state: v.union(v.literal("queued"), v.literal("acknowledged")),
  createdAt: v.number(),
  acknowledgedAt: v.union(v.number(), v.null()),
});

const maxBatchSize = 1000;

export const list = queryGeneric({
  args: { sessionId: v.string() },
  handler: async (ctx, { sessionId }) =>
    ctx.db
      .query("annotations")
      .withIndex("by_session_and_time", (q) => q.eq("sessionId", sessionId))
      .order("desc")
      .take(100),
});

export const sessions = queryGeneric({
  args: {},
  handler: async (ctx) =>
    ctx.db
      .query("sessions")
      .withIndex("by_updated_at")
      .order("desc")
      .take(100),
});

export const events = queryGeneric({
  args: { sessionId: v.string() },
  handler: async (ctx, { sessionId }) =>
    ctx.db
      .query("events")
      .withIndex("by_session_and_time", (q) => q.eq("sessionId", sessionId))
      .order("asc")
      .take(1000),
});

export const ingestSnapshot = internalMutationGeneric({
  args: {
    sessions: v.array(session),
    events: v.array(event),
    annotations: v.array(annotation),
    commands: v.array(command),
  },
  handler: async (ctx, snapshot) => {
    for (const [name, records] of Object.entries(snapshot)) {
      if (records.length > maxBatchSize) {
        throw new Error(`${name} exceeds the ${maxBatchSize}-record limit.`);
      }
    }

    const updatedAt = Date.now();
    for (const record of snapshot.sessions) {
      const existing = await ctx.db
        .query("sessions")
        .withIndex("by_session_id", (q) => q.eq("sessionId", record.sessionId))
        .first();
      if (existing) {
        await ctx.db.patch(existing._id, { ...record, updatedAt });
      } else {
        await ctx.db.insert("sessions", { ...record, updatedAt });
      }
    }

    for (const record of snapshot.events) {
      const existing = await ctx.db
        .query("events")
        .withIndex("by_event_id", (q) => q.eq("eventId", record.eventId))
        .filter((q) => q.eq(q.field("sessionId"), record.sessionId))
        .first();
      if (existing) {
        await ctx.db.patch(existing._id, { ...record, updatedAt });
      } else {
        await ctx.db.insert("events", { ...record, updatedAt });
      }
    }

    for (const record of snapshot.annotations) {
      const existing = await ctx.db
        .query("annotations")
        .withIndex("by_annotation_id", (q) =>
          q.eq("annotationId", record.annotationId),
        )
        .first();
      if (existing) {
        await ctx.db.patch(existing._id, record);
      } else {
        await ctx.db.insert("annotations", record);
      }
    }

    for (const record of snapshot.commands) {
      const existing = await ctx.db
        .query("commands")
        .withIndex("by_command_id", (q) => q.eq("commandId", record.commandId))
        .first();
      if (existing) {
        await ctx.db.patch(existing._id, record);
      } else {
        await ctx.db.insert("commands", record);
      }
    }
  },
});
