import { internalMutationGeneric, queryGeneric } from "convex/server";
import { v } from "convex/values";
import { annotationFields, commandFields, eventFields, maxBatchSize, sessionFields } from "./validators";

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
    sessions: v.array(v.object(sessionFields)),
    events: v.array(v.object(eventFields)),
    annotations: v.array(v.object(annotationFields)),
    commands: v.array(v.object(commandFields)),
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
