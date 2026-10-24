import { makeFunctionReference } from "convex/server";
import type { GenericId as Id } from "convex/values";

export interface Annotation {
  _id: Id<"annotations">;
  _creationTime: number;
  sessionId: string;
  annotationId: string;
  atMs: number;
  text: string;
  author: string;
  createdAt: number;
}

export interface CoachCommand {
  _id: Id<"commands">;
  _creationTime: number;
  sessionId: string;
  commandId: string;
  type: "highlight" | "pause" | "retry" | "annotation";
  atMs: number | null;
  state: "queued" | "acknowledged";
  createdAt: number;
  acknowledgedAt: number | null;
  ackStatus?: "applied" | "rejected";
}

export interface MirroredSession {
  _id: Id<"sessions">;
  _creationTime: number;
  sessionId: string;
  traineeId: string;
  exercise: string;
  status: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  elapsedS: number | null;
  metrics?: {
    completionPct: number;
    successfulTransfers: number;
    drops: number;
    invalidReleases: number;
    elapsedS: number;
    distanceByInstrument: Record<string, number>;
    totalDistanceM: number;
    pathEfficiency: number;
    practiceScore: number;
  };
}

export interface MirroredEvent {
  _id: Id<"events">;
  _creationTime: number;
  sessionId: string;
  eventId: string;
  kind: "transfer_success" | "drop" | "invalid_release";
  tMs: number;
  instrument: "left" | "right" | null;
  objectId: string | null;
}

export const convexApi = {
  sessions: {
    list: makeFunctionReference<"query", Record<string, never>, MirroredSession[]>(
      "annotations:sessions",
    ),
  },
  events: {
    list: makeFunctionReference<
      "query",
      { sessionId: string },
      MirroredEvent[]
    >("annotations:events"),
  },
  annotations: {
    list: makeFunctionReference<
      "query",
      { sessionId: string },
      Annotation[]
    >("annotations:list"),
  },
  commands: {
    list: makeFunctionReference<
      "query",
      { sessionId: string },
      CoachCommand[]
    >("commands:list"),
  },
};
