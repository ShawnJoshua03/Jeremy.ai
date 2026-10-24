import { ApiError, getApiBase } from "./api";

export type AssessmentLevel = "needs_work" | "developing" | "proficient" | "not_assessed";

export interface AssessmentCriterion {
  id: string;
  label: string;
  value: number | null;
  unit: string;
  level: AssessmentLevel;
  rule: string;
}

export interface SessionAssessment {
  practice_score: number;
  criteria: AssessmentCriterion[];
  thresholds_note: string;
}

export interface CoachDrill {
  id: string;
  title: string;
  description: string;
  exercise_id: string;
}

export interface CoachingResponse {
  summary: string;
  tips: { text: string; event_id: string | null }[];
  answer: string | null;
  next_exercise_id: string;
  next_exercise: CoachDrill | null;
  source: "gemini" | "fallback" | string;
}

export const MAX_QUESTION_CHARS = 300;

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  // Same headers as api.ts: no API key in the browser bundle.
  const response = await fetch(`${getApiBase()}${path}`, {
    ...init,
    headers: init.body
      ? { Accept: "application/json", "Content-Type": "application/json" }
      : { Accept: "application/json" },
  });
  if (!response.ok) {
    let detail = response.statusText;
    try {
      const body: unknown = await response.json();
      if (typeof body === "object" && body !== null && "detail" in body && typeof body.detail === "string") {
        detail = body.detail;
      }
    } catch {
      // Non-JSON responses keep their HTTP status text.
    }
    throw new ApiError(detail || `Request failed (${response.status}).`, response.status);
  }
  return response.json() as Promise<T>;
}

export const coachApi = {
  getAssessment: (sessionId: string, signal?: AbortSignal) =>
    call<SessionAssessment>(`/sessions/${encodeURIComponent(sessionId)}/assessment`, { signal }),
  askCoach: (sessionId: string, question: string, signal?: AbortSignal) => {
    const trimmed = question.trim();
    return call<CoachingResponse>(`/sessions/${encodeURIComponent(sessionId)}/coach`, {
      method: "POST",
      body: JSON.stringify(trimmed ? { question: trimmed } : {}),
      signal,
    });
  },
};
