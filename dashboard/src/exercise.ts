import type { PracticeEvent, SessionMetrics } from "./types";

/** Exercise ids the backend accepts (docs/WOUND_CLOSURE.md, docs/LAP_CHOLE.md). */
export type ExerciseId = "peg_transfer" | "wound_closure" | "lap_chole";

export function isWoundClosure(exercise?: string | null): boolean {
  return exercise === "wound_closure";
}

export function isLapChole(exercise?: string | null): boolean {
  return exercise === "lap_chole";
}

export function exerciseLabel(exercise?: string | null): string {
  if (exercise === "wound_closure") return "Wound closure";
  if (exercise === "lap_chole") return "Laparoscopic cholecystectomy";
  if (exercise === "peg_transfer" || !exercise) return "Peg transfer";
  return exercise.replaceAll("_", " ");
}

/** Labels for the shared metric fields; the other exercises reuse them (see the contracts). */
export function metricLabels(exercise?: string | null) {
  if (isLapChole(exercise)) {
    return { successes: "Steps completed", drops: "Major errors", invalid: "Minor errors", total: 15 };
  }
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

/** Progress kinds; a lap-chole clip or cut is judged in context by lapCholeFindings. */
const SUCCESS_KINDS = new Set<PracticeEvent["kind"]>([
  "transfer_success",
  "stitch_placed",
  "step_completed",
  "port_placed",
  "insufflation",
  "critical_view",
  "clip_applied",
  "structure_cut",
  "ductotomy",
  "cholangiogram",
]);

/** stitch_placed, transfer_success and lap-chole progress events are not errors by themselves. */
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
  step_completed: "Step completed",
  port_placed: "Port placed",
  insufflation: "Insufflation",
  critical_view: "Critical view",
  clip_applied: "Clip applied",
  structure_cut: "Structure cut",
  ductotomy: "Ductotomy",
  cholangiogram: "Cholangiogram",
  injury: "Injury",
};

export function eventLabel(kind: string): string {
  return EVENT_LABELS[kind] ?? kind.replaceAll("_", " ");
}

/** Headset highlight target ids: peg_transfer uses target_N, wound_closure site_N; lap_chole has none. */
export function highlightTargets(exercise?: string | null): string[] {
  if (isLapChole(exercise)) return [];
  return isWoundClosure(exercise)
    ? Array.from({ length: 5 }, (_, i) => `site_${i + 1}`)
    : Array.from({ length: 6 }, (_, i) => `target_${i + 1}`);
}

// ---------- laparoscopic cholecystectomy (docs/LAP_CHOLE.md) ----------

export const LAP_CHOLE_STEPS: { id: string; name: string }[] = [
  { id: "veress", name: "Veress needle" },
  { id: "insufflate", name: "Insufflation (12–15 mmHg)" },
  { id: "port_umbilical", name: "Umbilical port and scope" },
  { id: "port_epigastric", name: "Epigastric port" },
  { id: "port_rsc_medial", name: "Right subcostal port, medial" },
  { id: "port_rsc_lateral", name: "Right subcostal port, lateral" },
  { id: "retract", name: "Retract gallbladder fundus" },
  { id: "dissect", name: "Dissect Calot's triangle to the critical view" },
  { id: "clip_duct_distal", name: "Clip cystic duct near the gallbladder" },
  { id: "ductotomy", name: "Ductotomy below the clip" },
  { id: "cholangiogram", name: "Cholangiogram" },
  { id: "clip_duct_proximal", name: "Two proximal duct clips" },
  { id: "divide_duct", name: "Divide cystic duct" },
  { id: "clip_artery", name: "Clip cystic artery" },
  { id: "divide_artery", name: "Divide cystic artery" },
];

const STRUCTURES: Record<string, string> = {
  cystic_duct: "cystic duct",
  cystic_artery: "cystic artery",
  cbd: "common bile duct",
};

export interface LapCholeFinding {
  event: PracticeEvent;
  severity: "major" | "minor";
  reason: string;
}

/** Same rules as backend/scoring.py lap_chole_findings, for highlighting only. Scores come from the backend. */
export function lapCholeFindings(events: PracticeEvent[]): LapCholeFinding[] {
  const ordered = [...events].sort((a, b) => a.t_ms - b.t_ms);
  const cvsTimes = ordered
    .filter(
      (e) =>
        (e.kind === "step_completed" && e.object_id === "dissect") ||
        (e.kind === "critical_view" && (e.value ?? 0) >= 100),
    )
    .map((e) => e.t_ms);
  const cvsMs = cvsTimes.length ? Math.min(...cvsTimes) : null;
  const clips = ordered.filter((e) => e.kind === "clip_applied");
  const out: LapCholeFinding[] = [];
  for (const e of ordered) {
    const name = STRUCTURES[e.object_id ?? ""] ?? e.object_id ?? "structure";
    if (e.kind === "injury") {
      out.push({ event: e, severity: "major", reason: `Injury: ${(e.object_id ?? "").replaceAll("_", " ")}${e.detail ? ` (${e.detail})` : ""}` });
    } else if (e.kind === "port_placed" && e.source && e.target) {
      const mm = Math.hypot(...e.source.map((v, i) => v - e.target![i])) * 1000;
      const deg = Math.abs(e.value ?? 0);
      if (mm > 15 || deg > 20) {
        out.push({ event: e, severity: "minor", reason: `Port ${mm.toFixed(0)} mm from site, ${deg.toFixed(0)}° off` });
      }
    } else if (e.kind === "clip_applied") {
      if (e.object_id === "cbd") out.push({ event: e, severity: "major", reason: "Clip on the common bile duct" });
      if ((e.detail ?? "").toLowerCase() === "crossing") {
        out.push({ event: e, severity: "minor", reason: `Clip not fully across the ${name}` });
      }
    } else if (e.kind === "ductotomy") {
      if (e.object_id !== "cystic_duct") {
        out.push({ event: e, severity: "major", reason: `Ductotomy in the ${name}` });
      } else if (
        !clips.some(
          (c) => c.object_id === "cystic_duct" && c.t_ms <= e.t_ms && c.value != null && e.value != null && c.value > e.value,
        )
      ) {
        out.push({ event: e, severity: "major", reason: "Ductotomy without a distal clip above it" });
      }
      if (cvsMs === null || e.t_ms < cvsMs) {
        out.push({ event: e, severity: "major", reason: "Ductotomy before the critical view" });
      }
    } else if (e.kind === "cholangiogram") {
      const result = (e.detail ?? "").toLowerCase();
      if (result === "not_in_duct" || result === "leak") {
        out.push({
          event: e,
          severity: "minor",
          reason: result === "leak" ? "Cholangiogram contrast leak" : "Cholangiogram catheter not in the duct",
        });
      }
    } else if (e.kind === "structure_cut") {
      if (e.object_id === "cbd") {
        out.push({ event: e, severity: "major", reason: "Common bile duct cut" });
        continue;
      }
      const cut = e.value;
      const before = clips.filter((c) => c.object_id === e.object_id && c.t_ms <= e.t_ms && c.value != null);
      const proximal = cut == null ? 0 : before.filter((c) => c.value! <= cut - 2).length;
      const distal = cut == null ? 0 : before.filter((c) => c.value! >= cut + 2).length;
      if (proximal < 2 || distal < 1) {
        out.push({ event: e, severity: "major", reason: `Unsafe cut of the ${name} (${proximal} proximal, ${distal} distal clips)` });
      }
      if (cvsMs === null || e.t_ms < cvsMs) {
        out.push({ event: e, severity: "major", reason: `${name} divided before the critical view` });
      }
    }
  }
  const pressures = ordered.filter((e) => e.kind === "insufflation" && e.value != null);
  const last = pressures[pressures.length - 1];
  if (last && (last.value! < 12 || last.value! > 15)) {
    out.push({ event: last, severity: "minor", reason: `Final pressure ${last.value!.toFixed(1)} mmHg` });
  }
  return out.sort((a, b) => a.event.t_ms - b.event.t_ms);
}

/** Error events to mark on the replay timeline, for any exercise. */
export function errorEvents(events: PracticeEvent[], exercise?: string | null): PracticeEvent[] {
  if (!isLapChole(exercise)) return events.filter(isErrorEvent);
  const seen = new Set<string>();
  return lapCholeFindings(events)
    .map((f) => f.event)
    .filter((e) => (seen.has(e.event_id) ? false : (seen.add(e.event_id), true)));
}

const DETAIL_LABELS: Record<string, string> = {
  steps_completed: "Steps completed",
  steps_total: "Steps in the drill",
  ports_placed: "Ports placed",
  ports_mean_error_mm: "Mean port error (mm)",
  ports_max_error_mm: "Worst port error (mm)",
  ports_mean_angle_deg: "Mean port angle error (°)",
  insufflation_mmhg: "Final insufflation (mmHg)",
  insufflation_in_range: "Pressure in 12–15 mmHg",
  critical_view_pct: "Critical view cleared (%)",
  critical_view_reached: "Critical view reached",
  clips_cystic_duct: "Clips on cystic duct",
  clips_cystic_artery: "Clips on cystic artery",
  crossing_clips: "Crossing clips",
  safe_divisions: "Safe divisions",
  unsafe_divisions: "Unsafe divisions",
  divisions_before_cvs: "Divisions before critical view",
  ductotomies: "Ductotomies",
  unsafe_ductotomies: "Unsafe ductotomies",
  cholangiogram_depth_mm: "Cholangiogram catheter depth (mm)",
  cholangiogram_result: "Cholangiogram result",
  cholangiogram_errors: "Cholangiogram errors",
  cbd_errors: "Common bile duct clips or cuts",
  injuries: "Injuries",
  clipping_errors: "Clipping and division errors",
  bile_duct_injury: "Bile duct injury",
  major_errors: "Major errors",
  minor_errors: "Minor errors",
};

export function detailLabel(key: string): string {
  return DETAIL_LABELS[key] ?? key.replaceAll("_", " ");
}

export function formatDetail(value: number | string | boolean): string {
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (typeof value === "number") return Number.isInteger(value) ? String(value) : value.toFixed(1);
  return value;
}
