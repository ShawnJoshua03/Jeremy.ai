import type {
  Sample,
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
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function responseError(response: Response): Promise<ApiError> {
  let detail = response.statusText;
  try {
    const body: unknown = await response.json();
    if (
      typeof body === "object" &&
      body !== null &&
      "detail" in body &&
      typeof body.detail === "string"
    ) {
      detail = body.detail;
    }
  } catch {
    // Non-JSON responses keep their HTTP status text.
  }
  return new ApiError(detail || `Request failed (${response.status}).`, response.status);
}

async function request<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    headers: { Accept: "application/json" },
    signal,
  });

  if (!response.ok) throw await responseError(response);
  return response.json() as Promise<T>;
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
  getSession: (id: string, signal?: AbortSignal) =>
    request<TrainingSession>(`/sessions/${encodeURIComponent(id)}`, signal),
  getMetrics: (id: string, signal?: AbortSignal) =>
    request<SessionMetrics>(`/sessions/${encodeURIComponent(id)}/metrics`, signal),
  getSamples: (id: string, signal?: AbortSignal) =>
    request<Sample[]>(`/sessions/${encodeURIComponent(id)}/samples`, signal),
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
  sendCommand: (
    sessionId: string,
    body: { type: "highlight" | "pause" | "retry"; at_ms?: number },
  ) => write(`/sessions/${encodeURIComponent(sessionId)}/commands`, body),
};

export function getApiBase() {
  return apiBase;
}
