// Client-side history of live frames for the charts and the map. Kept in a
// mutable store (not React state) so canvases can read it every animation
// frame without re-rendering anything.
import { useEffect, useRef } from "react";
import type { LiveFrame } from "./model";

export interface Sample {
  t: number; // attempt seconds
  wall: number; // performance.now() when received
  step: number;
  dist: number | null; // mm to the entry mark (entry steps) or the target (inside)
  angle: number | null;
  depth: number | null;
  pressure: number | null;
  speed: number | null; // tip speed, mm/s (smoothed)
  tip: { x: number; y: number; z: number } | null;
  contact: string;
}

export interface History {
  session: string;
  samples: Sample[];
  version: number;
  latest: LiveFrame | undefined;
}

const MAX = 12000; // ~25 min at 8 Hz

export function useHistory(frame: LiveFrame | undefined, sessionId: string) {
  const ref = useRef<History>({ session: "", samples: [], version: 0, latest: undefined });
  const lastSeq = useRef<bigint>(-1n);
  useEffect(() => {
    const h = ref.current;
    if (h.session !== sessionId) {
      h.session = sessionId; h.samples = []; h.latest = undefined; h.version++;
      lastSeq.current = -1n;
    }
    if (!frame || frame.sessionId !== sessionId || frame.seq === lastSeq.current) return;
    lastSeq.current = frame.seq;
    const prev = h.samples[h.samples.length - 1];
    const t = frame.elapsedS;
    if (prev && t < prev.t - 1) h.samples = []; // a restarted attempt under the same id
    const tip = frame.tipValid ? { x: frame.tipX, y: frame.tipY, z: frame.tipZ } : null;
    let speed: number | null = null;
    if (tip && prev?.tip && t > prev.t && t - prev.t < 1) {
      const raw = (Math.hypot(tip.x - prev.tip.x, tip.y - prev.tip.y, tip.z - prev.tip.z) * 1000) / (t - prev.t);
      speed = prev.speed !== null ? prev.speed * 0.6 + raw * 0.4 : raw;
    }
    const entry = frame.entryActive;
    h.samples.push({
      t,
      wall: performance.now(),
      step: frame.stepIndex,
      dist: entry ? (frame.entryInserted ? null : frame.entryMm >= 0 ? frame.entryMm : null) : frame.tipValid && frame.targetMm >= 0 && frame.stepIndex >= 6 ? frame.targetMm : null,
      angle: entry && !frame.entryInserted && frame.entryAngle >= 0 ? frame.entryAngle : null,
      depth: entry && frame.entryInserted && frame.entryDepth >= 0 ? frame.entryDepth : null,
      pressure: frame.stepIndex <= 2 || frame.pressure > 0 ? frame.pressure : null,
      speed,
      tip,
      contact: frame.contact,
    });
    if (h.samples.length > MAX) h.samples.splice(0, h.samples.length - MAX);
    h.latest = frame;
    h.version++;
  }, [frame, sessionId]);
  return ref;
}

/** Attempt time now, advanced smoothly between frames while frames keep arriving. */
export function smoothTime(h: History, now: number): number {
  const last = h.samples[h.samples.length - 1];
  if (!last) return 0;
  const since = now - last.wall;
  return last.t + (since < 1500 ? since / 1000 : 0);
}

/** Canvas sized to its box (device-pixel aware) and redrawn every animation frame. */
export function useCanvas(draw: (ctx: CanvasRenderingContext2D, w: number, h: number, now: number) => void) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawRef = useRef(draw);
  drawRef.current = draw;
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    let w = 0, h = 0, raf = 0;
    const resize = () => {
      const r = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      w = r.width; h = r.height;
      canvas.width = Math.max(1, Math.round(w * dpr));
      canvas.height = Math.max(1, Math.round(h * dpr));
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    const ro = new ResizeObserver(resize);
    ro.observe(canvas);
    resize();
    const loop = (now: number) => {
      ctx.clearRect(0, 0, w, h);
      drawRef.current(ctx, w, h, now);
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => { cancelAnimationFrame(raf); ro.disconnect(); };
  }, []);
  return canvasRef;
}
