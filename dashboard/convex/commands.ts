import { queryGeneric } from "convex/server";
import { v } from "convex/values";

export const list = queryGeneric({
  args: { sessionId: v.string() },
  handler: async (ctx, { sessionId }) =>
    ctx.db
      .query("commands")
      .withIndex("by_session_and_time", (q) => q.eq("sessionId", sessionId))
      .order("desc")
      .take(50),
});

export const pending = queryGeneric({
  args: { sessionId: v.string() },
  handler: async (ctx, { sessionId }) =>
    ctx.db
      .query("commands")
      .withIndex("by_session_and_time", (q) => q.eq("sessionId", sessionId))
      .filter((q) => q.eq(q.field("state"), "queued"))
      .order("asc")
      .take(50),
});
