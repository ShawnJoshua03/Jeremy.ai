export interface TrainingSession {
  id: string;
  trainee_id: string;
  exercise: string;
  status: string;
  created_at: string;
  started_at: string | null;
  completed_at: string | null;
  elapsed_s: number | null;
  metrics?: SessionMetrics | null;
}

export interface SessionMetrics {
  completion_pct: number;
  successful_transfers: number;
  drops: number;
  invalid_releases: number;
  elapsed_s: number;
  distance_by_instrument: Record<string, number>;
  total_distance_m: number;
  path_efficiency: number;
  practice_score: number;
  /** Wound closure only; null or absent for peg transfer. */
  exercise?: string | null;
  mean_bite_error_mm?: number | null;
  spacing_sd_mm?: number | null;
  /** Laparoscopic cholecystectomy only; null for the other exercises. */
  details?: Record<string, number | string | boolean> | null;
}

export interface Sample {
  instrument: "left" | "right" | "head";
  idx: number;
  t_ms: number;
  pos: [number, number, number];
  valid: boolean;
  interaction: string;
}

export interface PracticeEvent {
  event_id: string;
  kind: string;
  t_ms: number;
  instrument?: string | null;
  object_id: string | null;
  source?: [number, number, number] | null;
  target?: [number, number, number] | null;
  /** Lap chole: seconds, degrees, mmHg, percent or mm depending on the kind. */
  value?: number | null;
  detail?: string | null;
}

export interface InstructorAnnotation {
  id?: string;
  annotation_id?: string;
  annotationId?: string;
  session_id?: string;
  sessionId?: string;
  at_ms?: number;
  atMs?: number;
  text: string;
  author: string;
  created_at?: number | string;
  createdAt?: number | string;
}

export interface SessionSummary {
  id: string;
  session?: TrainingSession;
  metrics?: SessionMetrics;
  error?: string;
}

export interface TraineeProgress {
  session_id: string;
  completed_at: string;
  practice_score: number;
  elapsed_s: number;
  path_efficiency: number;
  successful_transfers?: number;
  errors?: number;
  cost_total_usd?: number;
  exercise?: string;
}

export interface DemoSession {
  session: TrainingSession;
  metrics: SessionMetrics;
  samples: Sample[];
  events: PracticeEvent[];
  report?: SessionReport;
}

export interface ReportExercise {
  exercise_id: string;
  title: string;
  decision: string | null;
  expected: string | null;
  correct: boolean | null;
  unclear: boolean;
  time_to_decision_s: number | null;
  structures_inspected: string[];
  hover_dwell_s: Record<string, number>;
  hand_path_m: number;
  head_path_m: number;
  hesitation_s: number;
  review: string;
  tip: string;
}

export interface MistakeCostLine {
  label: string;
  usd: number;
  source: string;
  assumed: boolean;
}

export interface SessionReport {
  session_id: string;
  exercise: string;
  trainee_id: string;
  completed_at: string;
  score: number;
  grade: "excellent" | "competent" | "needs_practice" | string;
  exercises: ReportExercise[];
  movement: {
    hand_path_m: number;
    head_path_m: number;
    smoothness: number;
    idle_s: number;
    samples: number;
  };
  summary: string;
  strengths: string[];
  focus_next: string[];
  mistake_cost: {
    total_usd: number;
    lines: MistakeCostLine[];
    report_url: string;
    paid_detail_requires_x402: boolean;
  };
  source: "gemini" | "fallback" | string;
  disclaimer: string;
  providers?: Record<string, string>;
  provider_events?: Array<{
    t_ms: number;
    provider: string;
    action: string;
  }>;
}

export interface ReplayEvent {
  t_ms: number;
  kind: string;
  object_id: string | null;
  detail: string | null;
  event_id?: string;
}

export interface SessionReplay {
  duration_ms: number;
  tracks: Record<string, Array<[number, number, number, number]>>;
  events: ReplayEvent[];
}

export interface PaymentRequirement {
  scheme?: string;
  network?: string;
  maxAmountRequired?: string;
  asset?: string;
  payTo?: string;
  resource?: string;
  description?: string;
  extra?: {
    name?: string;
    version?: string;
  };
}
