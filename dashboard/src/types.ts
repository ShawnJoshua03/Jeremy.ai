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
}

export interface Sample {
  instrument: "left" | "right";
  idx: number;
  t_ms: number;
  pos: [number, number, number];
  valid: boolean;
  interaction: string;
}

export interface PracticeEvent {
  event_id: string;
  kind: "transfer_success" | "drop" | "invalid_release";
  t_ms: number;
  instrument: "left" | "right" | null;
  object_id: string | null;
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
}

export interface DemoSession {
  session: TrainingSession;
  metrics: SessionMetrics;
  samples: Sample[];
  events: PracticeEvent[];
}
