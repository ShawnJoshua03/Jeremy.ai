import type { PracticeEvent, SessionMetrics } from "./types";

/** Exercise ids the backend accepts (docs/WOUND_CLOSURE.md). */
export type ExerciseId = "peg_transfer" | "wound_closure";

export function isWoundClosure(exercise?: string | null): boolean {
  return exercise === "wound_closure";
}

export function exerciseLabel(exercise?: string | null): string {
  if (exercise === "wound_closure") return "Wound closure";
  if (exercise === "peg_transfer" || !exercise) return "Peg transfer";
  return exercise.replaceAll("_", " ");
}

/** Labels for the shared metric fields; wound closure reuses them for stitches, contacts and misses. */
export function metricLabels(exercise?: string | null) {
  return isWoundClosure(exercise)
    ? { successes: "Stitches placed", drops: "Tissue contacts", invalid: "Missed stitches", total: 5 }
    : { successes: "Successful transfers", drops: "Drops", invalid: "Invalid releases", total: 6 };
}

export function formatMm(value?: number | null): string {
  return value == null ? "-" : `${value.toFixed(1)} mm`;
}

export function errorCount(metrics?: SessionMetrics | null): number {
  return metrics ? metrics.drops + metrics.invalid_releases : 0;
}

const SUCCESS_KINDS = new Set<PracticeEvent["kind"]>(["transfer_success", "stitch_placed"]);

/** stitch_placed and transfer_success are progress, not errors. */
export function isErrorEvent(event: Pick<PracticeEvent, "kind">): boolean {
  return !SUCCESS_KINDS.has(event.kind);
}

const EVENT_LABELS: Record<string, string> = {
  transfer_success: "Transfer",
  drop: "Dropped peg",
  invalid_release: "Invalid release",
  stitch_placed: "Stitch placed",
  stitch_missed: "Missed stitch",
  tissue_contact: "Tissue contact",
};

export function eventLabel(kind: string): string {
  return EVENT_LABELS[kind] ?? kind.replaceAll("_", " ");
}

/** Headset highlight target ids: peg_transfer uses target_N, wound_closure uses site_N. */
export function highlightTargets(exercise?: string | null): string[] {
  return isWoundClosure(exercise)
    ? Array.from({ length: 5 }, (_, i) => `site_${i + 1}`)
    : Array.from({ length: 6 }, (_, i) => `target_${i + 1}`);
}
