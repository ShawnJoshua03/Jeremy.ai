import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";
import { annotationFields, commandFields, eventFields, sessionFields } from "./validators";

export default defineSchema({
  sessions: defineTable({ ...sessionFields, updatedAt: v.number() })
    .index("by_session_id", ["sessionId"])
    .index("by_updated_at", ["updatedAt"]),
  events: defineTable({ ...eventFields, updatedAt: v.number() })
    .index("by_session_and_time", ["sessionId", "tMs"])
    .index("by_event_id", ["eventId"]),
  annotations: defineTable(annotationFields)
    .index("by_session_and_time", ["sessionId", "atMs"])
    .index("by_annotation_id", ["annotationId"]),
  commands: defineTable(commandFields)
    .index("by_session_and_time", ["sessionId", "createdAt"])
    .index("by_command_id", ["commandId"]),
});
