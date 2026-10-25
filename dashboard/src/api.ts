import type {
  Sample,
  InstructorAnnotation,
  MistakeCostLine,
  PracticeEvent,
  SessionReplay,
  SessionReport,
  SessionMetrics,
  TrainingSession,
  TraineeProgress,
} from "./types";

const apiBase = (
  import.meta.env.VITE_API_URL?.trim() || "/api/v1"
).replace(/\/+$/, "");

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly body?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function responseError(response: Response): Promise<ApiError> {
  let detail = response.statusText;
  let payload: unknown;
  try {
    payload = await response.json();
    if (
      typeof payload === "object" &&
      payload !== null &&
      "detail" in payload &&
      typeof payload.detail === "string"
    ) {
      detail = payload.detail;
    }
  } catch {
    // Non-JSON responses keep their HTTP status text.
  }
  return new ApiError(detail || `Request failed (${response.status}).`, response.status, payload);
}

async function request<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    headers: { Accept: "application/json" },
    signal,
  });

  if (!response.ok) throw await responseError(response);
  return response.json() as Promise<T>;
}

async function requestArray<T>(
  path: string,
  key: string,
  signal?: AbortSignal,
): Promise<T[]> {
  const payload: unknown = await request(path, signal);
  if (Array.isArray(payload)) return payload as T[];
  if (typeof payload === "object" && payload !== null) {
    const records = (payload as Record<string, unknown>)[key];
    if (Array.isArray(records)) return records as T[];
  }
  throw new ApiError(`Unexpected response while loading ${key}.`, 200);
}

async function write(path: string, body: Record<string, unknown>) {
  const response = await fetch(`${apiBase}${path}`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw await responseError(response);
}

export const api = {
  getSessions: (signal?: AbortSignal) =>
    requestArray<TrainingSession>("/sessions", "sessions", signal),
  getSession: (id: string, signal?: AbortSignal) =>
    request<TrainingSession>(`/sessions/${encodeURIComponent(id)}`, signal),
  getMetrics: (id: string, signal?: AbortSignal) =>
    request<SessionMetrics>(`/sessions/${encodeURIComponent(id)}/metrics`, signal),
  getSamples: (id: string, signal?: AbortSignal) =>
    request<Sample[]>(`/sessions/${encodeURIComponent(id)}/samples`, signal),
  getEvents: (id: string, signal?: AbortSignal) =>
    requestArray<PracticeEvent>(
      `/sessions/${encodeURIComponent(id)}/events`,
      "events",
      signal,
    ),
  getReport: (id: string, signal?: AbortSignal) =>
    request<SessionReport>(`/sessions/${encodeURIComponent(id)}/report`, signal),
  getReplay: (id: string, signal?: AbortSignal) =>
    request<SessionReplay>(`/sessions/${encodeURIComponent(id)}/replay`, signal),
  getMistakeCostSummary: (id: string, signal?: AbortSignal) =>
    request<{ total_usd: number }>(
      `/sessions/${encodeURIComponent(id)}/mistake-cost/summary`,
      signal,
    ),
  getMistakeCostReport: (id: string, signal?: AbortSignal) =>
    request<{ total_usd: number; lines: MistakeCostLine[] }>(
      `/sessions/${encodeURIComponent(id)}/mistake-cost/report`,
      signal,
    ),
  getAnnotations: (id: string, signal?: AbortSignal) =>
    requestArray<InstructorAnnotation>(
      `/sessions/${encodeURIComponent(id)}/annotations`,
      "annotations",
      signal,
    ),
  getTraineeProgress: (traineeId: string, signal?: AbortSignal) =>
    request<TraineeProgress[]>(
      `/trainees/${encodeURIComponent(traineeId)}/progress`,
      signal,
    ),
  addAnnotation: (
    sessionId: string,
    body: { at_ms: number; text: string; author: string },
  ) =>
    write(`/sessions/${encodeURIComponent(sessionId)}/annotations`, body),
  getCommandHistory: (sessionId: string, signal?: AbortSignal) =>
    requestArray<CommandHistoryRow>(
      `/sessions/${encodeURIComponent(sessionId)}/commands/history`,
      "commands",
      signal,
    ),
  sendCommand: (sessionId: string, command: HeadsetCommand) =>
    write(`/sessions/${encodeURIComponent(sessionId)}/commands`, commandBody(command)),
};

/** A command as stored by the backend, with its delivery state. */
export interface CommandHistoryRow {
  commandId: string;
  type: string;
  targetId: string;
  message: string;
  state: "queued" | "applied" | "rejected" | "expired";
  ackMessage: string;
  createdAt: string | null;
}

/** Commands the headset applies (see quest/Assets/Scripts/Mentorship/CommandPoller.cs). */
export type HeadsetCommand =
  | { type: "highlight_target"; targetId: string }
  | { type: "request_pause" }
  | { type: "request_retry" };

const COMMAND_TTL_MS = 5 * 60 * 1000;

function newCommandId() {
  const random =
    typeof crypto !== "undefined" && "randomUUID" in crypto
      ? crypto.randomUUID().replace(/-/g, "")
      : `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`;
  return `dash_${random}`;
}

function commandBody(command: HeadsetCommand) {
  return {
    commandId: newCommandId(),
    type: command.type,
    targetId: command.type === "highlight_target" ? command.targetId : "",
    expiresAt: new Date(Date.now() + COMMAND_TTL_MS).toISOString(),
    message: "",
    exerciseId: "",
  };
}

/** Peg N belongs on target N, so a peg event maps to the target to highlight.
 *  Wound-closure events carry site_N, which is itself the highlight id. */
export function targetForObject(objectId: string | null | undefined): string | null {
  const match = /^peg[_-]([1-6])$/.exec(objectId ?? "");
  if (match) return `target_${match[1]}`;
  return /^site_[1-5]$/.test(objectId ?? "") ? objectId! : null;
}

export const TARGET_IDS = ["target_1", "target_2", "target_3", "target_4", "target_5", "target_6"];

export function getApiBase() {
  return apiBase;
}
