// "Aim" view: a small top-down view centred on what the trainee is aiming at
// right now (the entry mark during port steps, the target inside), with the
// tip's offset from it. The tip and centre are eased every animation frame.
// Board-local frame: +x toward the feet, +z toward the patient's right.
import { useRef, type MutableRefObject } from "react";
import { useCanvas, type History } from "./history";
import type { Anatomy } from "./model";

const C = { hair: "#e6e8eb", ring: "#cfd4d9", ink: "#1f2328", muted: "#8a929a", accent: "#0f8b8d", red: "#d93036", duct: "#3f9a6b", cbd: "#3b6fd1", artery: "#c4664f" };
const NAMES: [string, string, string][] = [["cystic_duct", "cystic duct", C.duct], ["cystic_artery", "cystic artery", C.artery], ["cbd", "common bile duct", C.cbd]];
const FONT = "12px 'IBM Plex Sans', sans-serif";

export function AimView({ history, anatomy }: { history: MutableRefObject<History>; anatomy: Anatomy }) {
  const s = useRef({ cx: 0, cz: 0, r: 0.04, tx: 0, tz: 0, hasTip: false, init: false, last: 0, shownMm: 0 });
  const anatomyRef = useRef(anatomy);
  anatomyRef.current = anatomy;

  const ref = useCanvas((ctx, w, h, now) => {
    const st = s.current;
    const dt = st.last ? Math.min(0.1, (now - st.last) / 1000) : 0.016;
    st.last = now;
    const f = history.current.latest;
    const size = Math.min(w, h);
    const ox = w / 2, oy = h / 2;
    ctx.font = FONT;
    if (!f) {
      ctx.fillStyle = C.muted; ctx.textAlign = "center";
      ctx.fillText("Waiting for the headset", ox, oy);
      return;
    }
    const inside = f.stepIndex >= 6;
    const entry = f.entryActive && !f.entryInserted;
    if (!inside && !entry) {
      ctx.fillStyle = C.muted; ctx.textAlign = "center";
      ctx.fillText(f.entryInserted ? `In at ${f.entryDepth.toFixed(0)} mm` : "No aiming in this step", ox, oy);
      return;
    }
    const goal = inside ? { x: f.targetX, z: f.targetZ, r: 0.03 } : { x: f.siteX, z: f.siteZ, r: 0.04 };
    const k = 1 - Math.exp(-dt * 6);
    if (!st.init) { st.cx = goal.x; st.cz = goal.z; st.r = goal.r; st.init = true; }
    st.cx += (goal.x - st.cx) * k; st.cz += (goal.z - st.cz) * k; st.r += (goal.r - st.r) * k;
    const R = size / 2 - 18;
    const scale = R / st.r; // px per metre
    const sx = (z: number) => ox - (z - st.cz) * scale; // patient's right to the left
    const sy = (x: number) => oy + (x - st.cx) * scale; // head up

    // Hairlines and orientation.
    ctx.strokeStyle = C.hair; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(ox - R, oy); ctx.lineTo(ox + R, oy); ctx.moveTo(ox, oy - R); ctx.lineTo(ox, oy + R); ctx.stroke();
    ctx.fillStyle = C.muted; ctx.textAlign = "center";
    ctx.fillText("head", ox, oy - R - 4);
    ctx.textAlign = "left"; ctx.fillText("patient's right", ox - R, oy - 6);

    // Tolerance ring.
    const tolMm = inside ? 10 : f.entryTolMm || 15;
    ctx.strokeStyle = C.ring;
    ctx.beginPath(); ctx.arc(ox, oy, (tolMm / 1000) * scale, 0, Math.PI * 2); ctx.stroke();
    ctx.fillStyle = C.muted; ctx.textAlign = "left";
    ctx.fillText(`${tolMm.toFixed(0)} mm`, ox + (tolMm / 1000) * scale * 0.72 + 4, oy + (tolMm / 1000) * scale * 0.72 + 12);

    // Inside: nearby structures, named.
    if (inside) {
      ctx.save();
      ctx.beginPath(); ctx.arc(ox, oy, R, 0, Math.PI * 2); ctx.clip();
      const a = anatomyRef.current;
      for (const [id, name, color] of NAMES) {
        const pts = a[id];
        if (!pts || pts.length < 2) continue;
        const hot = id === "cbd" && f.contact === "cbd";
        ctx.strokeStyle = hot ? C.red : color; ctx.lineWidth = hot ? 3 : 2; ctx.lineCap = "round";
        ctx.beginPath();
        pts.forEach((p, i) => (i ? ctx.lineTo(sx(p.z), sy(p.x)) : ctx.moveTo(sx(p.z), sy(p.x))));
        ctx.stroke();
        // Label at the visible point farthest from the centre (out of the way of the tip).
        let best: { x: number; y: number } | null = null, bestD = -1;
        for (const p of pts) {
          const px = sx(p.z), py = sy(p.x), d = Math.hypot(px - ox, py - oy);
          if (d < R - 30 && d > bestD) { bestD = d; best = { x: px, y: py }; }
        }
        if (best) { ctx.fillStyle = hot ? C.red : color; ctx.textAlign = best.x < ox ? "left" : "right"; ctx.fillText(name, best.x + (best.x < ox ? 6 : -6), best.y - 6); }
      }
      ctx.restore();
    }

    // Centre mark.
    ctx.strokeStyle = C.ink; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(ox, oy, 4, 0, Math.PI * 2); ctx.stroke();

    // Tip, eased.
    if (f.tipValid) {
      if (!st.hasTip) { st.tx = f.tipX; st.tz = f.tipZ; st.hasTip = true; }
      const kt = 1 - Math.exp(-dt * 10);
      st.tx += (f.tipX - st.tx) * kt; st.tz += (f.tipZ - st.tz) * kt;
      let px = sx(st.tz), py = sy(st.tx);
      const d = Math.hypot(px - ox, py - oy);
      const off = d > R; // off the view: pin to the edge
      if (off) { px = ox + ((px - ox) / d) * R; py = oy + ((py - oy) / d) * R; }
      ctx.strokeStyle = C.ring; ctx.setLineDash([3, 4]);
      ctx.beginPath(); ctx.moveTo(ox, oy); ctx.lineTo(px, py); ctx.stroke(); ctx.setLineDash([]);
      ctx.fillStyle = f.contact === "cbd" ? C.red : C.accent;
      ctx.beginPath(); ctx.arc(px, py, 6, 0, Math.PI * 2); ctx.fill();
      const mm = inside ? f.targetMm : f.entryMm;
      if (mm >= 0) {
        st.shownMm += (mm - st.shownMm) * (1 - Math.exp(-dt * 8));
        ctx.fillStyle = C.ink; ctx.textAlign = px < ox ? "right" : "left";
        ctx.fillText(`${st.shownMm.toFixed(0)} mm${off ? " away" : ""}`, px + (px < ox ? -10 : 10), py + 4);
      }
    } else st.hasTip = false;
  });

  return <canvas ref={ref} className="lv-canvas" aria-label="Tip position relative to the current mark or target" />;
}
