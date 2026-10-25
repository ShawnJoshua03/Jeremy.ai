// Shared types and pure helpers for the live dashboard. Rows come from the
// SpacetimeDB live* tables (see dashboard/spacetimedb/src/live_tables.ts).
import type { LiveCaption, LiveEvent, LiveFrame, LiveSession } from "../module_bindings/types";

export type { LiveCaption, LiveEvent, LiveFrame, LiveSession };

export interface Vec3 { x: number; y: number; z: number }

export const STEPS: readonly { id: string; title: string; short: string; phase: "access" | "ports" | "dissection" | "division" }[] = [
  { id: "veress", title: "Veress needle", short: "Veress", phase: "access" },
  { id: "insufflate", title: "Insufflation", short: "CO₂", phase: "access" },
  { id: "port_umbilical", title: "Umbilical port", short: "Umbilical", phase: "ports" },
  { id: "port_epigastric", title: "Epigastric port", short: "Epigastric", phase: "ports" },
  { id: "port_rsc_medial", title: "Right subcostal port (medial)", short: "RSC med.", phase: "ports" },
  { id: "port_rsc_lateral", title: "Right subcostal port (lateral)", short: "RSC lat.", phase: "ports" },
  { id: "retract", title: "Retract the gallbladder", short: "Retract", phase: "dissection" },
  { id: "dissect", title: "Dissect Calot's triangle", short: "Dissect", phase: "dissection" },
  { id: "clip_duct_distal", title: "Distal clip, cystic duct", short: "Distal clip", phase: "division" },
  { id: "ductotomy", title: "Ductotomy", short: "Ductotomy", phase: "division" },
  { id: "cholangiogram", title: "Cholangiogram", short: "Cholangio", phase: "division" },
  { id: "clip_duct_proximal", title: "Proximal clips, cystic duct", short: "Prox. clips", phase: "division" },
  { id: "divide_duct", title: "Divide the cystic duct", short: "Cut duct", phase: "division" },
  { id: "clip_artery", title: "Clip the cystic artery", short: "Artery clips", phase: "division" },
  { id: "divide_artery", title: "Divide the cystic artery", short: "Cut artery", phase: "division" },
];

export const ENTRY_STEPS = new Set(["veress", "port_umbilical", "port_epigastric", "port_rsc_medial", "port_rsc_lateral"]);

export type Severity = "major" | "minor" | "good" | "info";

export const FINDING_TEXT: Record<string, string> = {
  injury: "Tissue injury",
  hand_collision: "Instrument hit the abdominal wall",
  port_offset: "Port off the planned site or axis",
  cbd_clip: "Clip on the common bile duct",
  clip_crossing: "Clip crossing the structure",
  ductotomy_wrong_structure: "Ductotomy on the wrong structure",
  unsafe_ductotomy: "Ductotomy without a distal clip above it",
  ductotomy_before_cvs: "Ductotomy before the critical view",
  cholangiogram_not_in_duct: "Cholangiogram catheter not in the duct",
  cholangiogram_leak: "Contrast leak on the cholangiogram",
  cbd_cut: "Common bile duct cut",
  clip_too_close: "Clip too close to the cut",
  unsafe_division: "Divided without two proximal + one distal clip",
  division_before_cvs: "Division before the critical view",
  insufflation_out_of_range: "Insufflation outside 12–15 mmHg",
};

export const STRUCTURE_NAME: Record<string, string> = {
  cystic_duct: "Cystic duct",
  cystic_artery: "Cystic artery",
  cbd: "Common bile duct",
  fundus: "Gallbladder fundus",
  veil: "Peritoneum (Calot's)",
  abdominal_wall: "Abdominal wall",
  hepatic_artery: "Hepatic artery",
};

export function structureName(id: string): string {
  return STRUCTURE_NAME[id] ?? id.replace(/_/g, " ");
}

export interface Finding { code: string; major: boolean; text: string }

export function parseFindings(raw: string): Finding[] {
  if (!raw) return [];
  return raw.split("|").filter(Boolean).map((part) => {
    const [code, kind] = part.split(":");
    return { code, major: kind === "M", text: FINDING_TEXT[code] ?? code.replace(/_/g, " ") };
  });
}

export interface PortMark { id: string; x: number; z: number; placed: boolean }

export function parsePorts(raw: string): PortMark[] {
  if (!raw) return [];
  const out: PortMark[] = [];
  for (const part of raw.split("|")) {
    const [id, x, z, placed] = part.split(":");
    const px = Number(x), pz = Number(z);
    if (id && Number.isFinite(px) && Number.isFinite(pz)) out.push({ id, x: px, z: pz, placed: placed === "1" });
  }
  return out;
}

export type Anatomy = Record<string, Vec3[]>;

export function parseAnatomy(raw: string): Anatomy {
  const out: Anatomy = {};
  if (!raw) return out;
  for (const part of raw.split("|")) {
    const colon = part.indexOf(":");
    if (colon < 1) continue;
    const pts = part.slice(colon + 1).split(";").map((p) => {
      const [x, y, z] = p.split(",").map(Number);
      return { x, y, z };
    }).filter((p) => Number.isFinite(p.x) && Number.isFinite(p.z));
    if (pts.length) out[part.slice(0, colon)] = pts;
  }
  return out;
}

export interface EventView {
  id: bigint;
  t: number; // seconds into the attempt
  severity: Severity;
  title: string;
  detail: string;
  stepIndex: number;
}

const mm = (a: Vec3, b: Vec3) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z) * 1000;

/** Turns a raw recorder event into a feed line with a severity. */
export function describeEvent(e: LiveEvent): EventView {
  const base = { id: e.id, t: Number(e.elapsedMs) / 1000, stepIndex: e.stepIndex };
  const where = structureName(e.objectId);
  switch (e.kind) {
    case "step_completed": {
      const step = STEPS.find((s) => s.id === e.objectId);
      return { ...base, severity: "good", title: `${step?.title ?? e.objectId} complete`, detail: e.message };
    }
    case "port_placed": {
      const off = e.hasPos && e.hasTarget ? mm({ x: e.x, y: e.y, z: e.z }, { x: e.tx, y: e.ty, z: e.tz }) : 0;
      const angle = e.hasValue ? Math.abs(e.value) : 0;
      const bad = off > 15 || angle > 20;
      return { ...base, severity: bad ? "minor" : "good", title: `${portLabel(e.objectId)} port placed`, detail: `${off.toFixed(0)} mm from the mark · ${angle.toFixed(0)}° off axis${bad ? " — outside tolerance" : ""}` };
    }
    case "insufflation": {
      const ok = e.value >= 12 && e.value <= 15;
      return { ...base, severity: ok ? "good" : "minor", title: `Insufflated to ${e.value.toFixed(1)} mmHg`, detail: ok ? "In the 12–15 mmHg range" : "Outside the 12–15 mmHg range" };
    }
    case "critical_view":
      return { ...base, severity: "good", title: "Critical view of safety", detail: "Cystic duct and artery exposed" };
    case "clip_applied": {
      if (e.objectId === "cbd") return { ...base, severity: "major", title: "Clip on the common bile duct", detail: "Major error" };
      const crossing = e.detail === "crossing";
      return { ...base, severity: crossing ? "minor" : "info", title: `Clip on the ${where.toLowerCase()}`, detail: `${e.hasValue ? `${e.value.toFixed(0)} mm along` : ""}${crossing ? " · crossing at an angle" : ""}` };
    }
    case "ductotomy": {
      const wrong = e.objectId !== "cystic_duct";
      return { ...base, severity: wrong ? "major" : "info", title: wrong ? `Ductotomy on the ${where.toLowerCase()}` : "Ductotomy", detail: e.hasValue ? `${e.value.toFixed(0)} mm along the duct` : "" };
    }
    case "cholangiogram": {
      const bad = e.detail === "leak" || e.detail === "not_in_duct";
      return { ...base, severity: bad ? "minor" : "good", title: "Cholangiogram", detail: bad ? (e.detail === "leak" ? "Contrast leak" : "Catheter not in the duct") : "Normal filling, no leak" };
    }
    case "structure_cut": {
      const cbd = e.objectId === "cbd";
      return { ...base, severity: cbd ? "major" : "info", title: `${where} divided`, detail: cbd ? "Major error: common bile duct" : e.hasValue ? `${e.value.toFixed(0)} mm along` : "" };
    }
    case "injury":
      return { ...base, severity: "major", title: `Injury: ${where.toLowerCase()}`, detail: e.detail };
    case "hand_collision":
      return { ...base, severity: "minor", title: "Hit the abdominal wall", detail: "Entry blocked: off the mark or at the wrong angle" };
  }
  return { ...base, severity: "info", title: e.kind.replace(/_/g, " "), detail: e.detail };
}

export function portLabel(id: string): string {
  return ({ umbilical: "Umbilical", epigastric: "Epigastric", rsc_medial: "RSC medial", rsc_lateral: "RSC lateral", veress: "Veress" } as Record<string, string>)[id] ?? id;
}

export interface StepView {
  index: number;
  id: string;
  title: string;
  short: string;
  status: "done" | "active" | "pending";
  severity: Severity | null; // worst event during the step
  startS: number | null;
  endS: number | null;
}

const RANK: Record<Severity, number> = { info: 0, good: 1, minor: 2, major: 3 };

export function buildSteps(session: LiveSession | undefined, events: readonly EventView[], completedAt: Map<string, number>, elapsedS: number): StepView[] {
  const done = session?.stepsDone ?? 0;
  const current = session?.state === "Completed" ? STEPS.length : (session?.stepIndex ?? 0);
  let previousEnd = 0;
  return STEPS.map((s, index) => {
    const end = completedAt.get(s.id) ?? null;
    let worst: Severity | null = null;
    for (const e of events) {
      if (e.stepIndex !== index || e.severity === "good" || e.severity === "info") continue;
      if (!worst || RANK[e.severity] > RANK[worst]) worst = e.severity;
    }
    const status: StepView["status"] = end !== null || index < done ? "done" : index === current ? "active" : "pending";
    const view: StepView = {
      index, id: s.id, title: s.title, short: s.short, status, severity: worst,
      startS: status === "pending" ? null : previousEnd,
      endS: end ?? (status === "active" ? elapsedS : null),
    };
    if (end !== null) previousEnd = end;
    return view;
  });
}

export function formatClock(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "0:00";
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function scoreBand(score: number, major: number): Severity {
  if (major > 0) return "major";
  if (score >= 75) return "good";
  if (score >= 50) return "info";
  return "minor";
}
