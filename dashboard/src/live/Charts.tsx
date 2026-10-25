// Stacked time-series lanes over the whole attempt on one shared time axis,
// drawn on a canvas every animation frame (the time axis advances smoothly).
import { useRef, type MutableRefObject } from "react";
import { smoothTime, useCanvas, type History, type Sample } from "./history";
import type { EventView } from "./model";

interface Lane {
  label: string;
  unit: string;
  value: (s: Sample) => number | null;
  min?: number;
  max: number; // minimum top of the y range
  cap: number; // the range never grows past this (outliers clip at the top)
  band?: [number, number];
  decimals?: number;
}

const LANES: Lane[] = [
  { label: "Distance to mark / target", unit: "mm", value: (s) => s.dist, max: 40, cap: 80 },
  { label: "Entry depth", unit: "mm", value: (s) => s.depth, max: 50, cap: 70 },
  { label: "Insufflation pressure", unit: "mmHg", value: (s) => s.pressure, max: 18, cap: 25, band: [12, 15], decimals: 1 },
  { label: "Tip speed", unit: "mm/s", value: (s) => s.speed, max: 60, cap: 150 },
];

const C = { line: "#e6e8eb", grid: "#f0f1f3", muted: "#8a929a", ink: "#1f2328", ink2: "#59636b", accent: "#0f8b8d", band: "rgba(15,139,141,0.07)", red: "#d93036", amber: "#c28a1c" };
const FONT = "11px 'IBM Plex Sans', sans-serif";
const PAD_L = 8, PAD_R = 64, TOP = 22, AXIS = 20, GAP = 14;

function fmt(t: number) {
  const m = Math.floor(t / 60), s = Math.floor(t % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function Charts({ history, events }: { history: MutableRefObject<History>; events: readonly EventView[] }) {
  const eventsRef = useRef(events);
  eventsRef.current = events;
  const yMax = useRef<number[]>(LANES.map((l) => l.max));

  const canvasRef = useCanvas((ctx, w, h, now) => {
    const hist = history.current;
    const samples = hist.samples;
    const T = Math.max(60, smoothTime(hist, now) * 1.04);
    const plotW = w - PAD_L - PAD_R;
    const x = (t: number) => PAD_L + (t / T) * plotW;
    const laneH = (h - TOP - AXIS - GAP * (LANES.length - 1)) / LANES.length;
    ctx.font = FONT;

    // Step boundaries and event markers across all lanes.
    ctx.lineWidth = 1;
    let prevStep = -1;
    for (const s of samples) {
      if (s.step !== prevStep) {
        prevStep = s.step;
        const px = Math.round(x(s.t)) + 0.5;
        ctx.strokeStyle = C.grid;
        ctx.beginPath(); ctx.moveTo(px, TOP - 4); ctx.lineTo(px, h - AXIS); ctx.stroke();
        ctx.fillStyle = C.muted; ctx.textAlign = "left";
        if (plotW / Math.max(1, T) * 8 > 10) ctx.fillText(String(s.step + 1), px + 3, TOP - 8);
      }
    }
    for (const e of eventsRef.current) {
      if (e.severity !== "major" && e.severity !== "minor") continue;
      ctx.fillStyle = e.severity === "major" ? C.red : C.amber;
      ctx.beginPath(); ctx.arc(x(e.t), TOP - 12, 3, 0, Math.PI * 2); ctx.fill();
    }

    // Decimate to about one point per pixel.
    const stride = Math.max(1, Math.floor(samples.length / Math.max(1, plotW)));

    LANES.forEach((lane, i) => {
      const top = TOP + i * (laneH + GAP);
      const bottom = top + laneH;
      // y range: grows to fit, eased so it never jumps.
      let peak = lane.max;
      for (let j = 0; j < samples.length; j += stride) { const v = lane.value(samples[j]); if (v !== null && v > peak) peak = v; }
      peak = Math.min(peak, lane.cap);
      yMax.current[i] += (peak * 1.1 - yMax.current[i]) * 0.08;
      const top_ = yMax.current[i];
      const y = (v: number) => bottom - (Math.max(0, Math.min(top_, v)) / top_) * laneH;

      ctx.strokeStyle = C.line; ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(PAD_L, Math.round(bottom) + 0.5); ctx.lineTo(PAD_L + plotW, Math.round(bottom) + 0.5); ctx.stroke();
      if (lane.band) {
        ctx.fillStyle = C.band;
        ctx.fillRect(PAD_L, y(lane.band[1]), plotW, y(lane.band[0]) - y(lane.band[1]));
      }

      // Line with gaps where the lane has no value.
      ctx.strokeStyle = C.accent; ctx.lineWidth = 1.5; ctx.lineJoin = "round";
      ctx.beginPath();
      let pen = false, last: number | null = null;
      for (let j = 0; j < samples.length; j += stride) {
        const s = samples[j];
        const v = lane.value(s);
        if (v === null) { pen = false; continue; }
        if (!pen) ctx.moveTo(x(s.t), y(v)); else ctx.lineTo(x(s.t), y(v));
        pen = true;
      }
      ctx.stroke();
      const tail = samples[samples.length - 1];
      last = tail ? lane.value(tail) : null;
      if (last !== null && tail) {
        ctx.fillStyle = C.accent;
        ctx.beginPath(); ctx.arc(x(tail.t), y(last), 2.5, 0, Math.PI * 2); ctx.fill();
      }

      ctx.fillStyle = C.muted; ctx.textAlign = "left";
      ctx.fillText(`${lane.label} (${lane.unit})`, PAD_L, top + 10);
      ctx.textAlign = "right";
      ctx.fillStyle = last !== null ? C.ink : C.muted;
      ctx.font = "15px 'IBM Plex Sans', sans-serif";
      ctx.fillText(last !== null ? last.toFixed(lane.decimals ?? 0) : "—", w - 4, top + 16);
      ctx.font = FONT;
    });

    // Time axis.
    ctx.fillStyle = C.muted; ctx.textAlign = "center";
    const step = T > 600 ? 120 : T > 240 ? 60 : 30;
    for (let t = 0; t <= T; t += step) { ctx.textAlign = t === 0 ? "left" : "center"; ctx.fillText(fmt(t), x(t), h - 5); }
  });

  return <canvas ref={canvasRef} className="lv-canvas" aria-label="Live charts: distance, angle, depth, pressure and tip speed over the attempt" />;
}
