// Top-down map of the patient in the board-local frame (origin = navel,
// +x toward the feet, +z toward the patient's right). Drawn as an anatomical
// view: head at the top, the patient's right on the viewer's left.
import { useMemo, useState } from "react";
import type { Anatomy, LiveFrame, PortMark, Vec3 } from "./model";
import { portLabel } from "./model";

type View = "auto" | "wall" | "calot";

interface Box { x0: number; x1: number; z0: number; z1: number }
// x0..x1 head-to-feet, z0..z1 patient's left-to-right (metres).
const WALL: Box = { x0: -0.27, x1: 0.13, z0: -0.2, z1: 0.2 };
const CALOT: Box = { x0: -0.2, x1: -0.03, z0: -0.03, z1: 0.17 };

const W = 400; // viewBox width; height follows the box's aspect

const ANATOMY_STYLE: Record<string, { stroke: string; width: number; label: string }> = {
  gallbladder: { stroke: "var(--lv-gall)", width: 11, label: "Gallbladder" },
  cbd: { stroke: "var(--lv-cbd)", width: 4.5, label: "Common bile duct" },
  hepatic_artery: { stroke: "var(--lv-artery)", width: 3, label: "Hepatic artery" },
  cystic_duct: { stroke: "var(--lv-duct)", width: 3.5, label: "Cystic duct" },
  cystic_artery: { stroke: "var(--lv-artery)", width: 2.4, label: "Cystic artery" },
};

// Where each structure is labelled in the Calot view (fraction along its path).
const LABELS: { id: string; at: number; anchor: "start" | "end"; dx: number; dy: number }[] = [
  { id: "gallbladder", at: 1, anchor: "end", dx: -14, dy: -8 },
  { id: "cystic_duct", at: 0.5, anchor: "start", dx: 12, dy: 4 },
  { id: "cystic_artery", at: 0.15, anchor: "end", dx: -10, dy: 4 },
  { id: "cbd", at: 0.2, anchor: "start", dx: 12, dy: 0 },
];

interface MapProps {
  frame: LiveFrame | undefined;
  anatomy: Anatomy;
  ports: PortMark[];
  trail: readonly Vec3[];
  stepIndex: number;
}

export function AbdomenMap({ frame, anatomy, ports, trail, stepIndex }: MapProps) {
  const [view, setView] = useState<View>("auto");
  const inside = stepIndex >= 6;
  const active: "wall" | "calot" = view === "auto" ? (inside ? "calot" : "wall") : view;
  const box = active === "calot" ? CALOT : WALL;
  const scale = W / (box.z1 - box.z0); // px per metre
  const H = (box.x1 - box.x0) * scale;
  // Screen x = patient's right on the left, so it runs from z1 down to z0.
  const sx = (z: number) => (box.z1 - z) * scale;
  const sy = (x: number) => (x - box.x0) * scale;
  const mmPx = scale / 1000;

  const planned = ports.filter((p) => !p.placed);
  const placed = new Map(ports.filter((p) => p.placed).map((p) => [p.id, p]));

  const trailPath = useMemo(() => {
    if (trail.length < 2) return "";
    return trail.map((p, i) => `${i ? "L" : "M"}${sx(p.z).toFixed(1)},${sy(p.x).toFixed(1)}`).join("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trail, active]);

  const tip = frame?.tipValid ? { x: frame.tipX, z: frame.tipZ } : null;
  const target = frame ? { x: frame.targetX, z: frame.targetZ } : null;
  const entry = frame?.entryActive ? frame : null;
  const cbdContact = frame?.contact === "cbd";
  const contact = frame?.contact ?? "";

  // The trainee's head is usually off the map: clamp it to the edge as a marker.
  const head = frame ? { x: Math.max(box.x0 + 0.02, Math.min(box.x1 - 0.02, frame.headX)), z: Math.max(box.z0 + 0.012, Math.min(box.z1 - 0.012, frame.headZ)) } : null;
  const headOff = frame ? frame.headZ < box.z0 || frame.headZ > box.z1 || frame.headX < box.x0 || frame.headX > box.x1 : false;

  const scaleBarMm = active === "calot" ? 20 : 50;

  return (
    <section className="lv-card lv-map" aria-label="Abdomen map">
      <header className="lv-card-head">
        <div>
          <h2>{active === "calot" ? "Calot's triangle" : "Abdominal wall"}</h2>
          <p className="lv-sub">Top-down · head up · patient's right on the left</p>
        </div>
        <div className="lv-seg" role="group" aria-label="Map view">
          {(["auto", "wall", "calot"] as View[]).map((v) => (
            <button key={v} type="button" aria-pressed={view === v} onClick={() => setView(v)}>
              {v === "auto" ? "Auto" : v === "wall" ? "Wall" : "Calot"}
            </button>
          ))}
        </div>
      </header>
      <div className="lv-map-body">
        <svg viewBox={`0 0 ${W} ${H.toFixed(0)}`} role="img" aria-label="Top-down map of ports, anatomy and the instrument tip">
          <defs>
            <radialGradient id="lvSkin" cx="50%" cy="45%" r="60%">
              <stop offset="0%" stopColor="var(--lv-skin-hi)" />
              <stop offset="100%" stopColor="var(--lv-skin-lo)" />
            </radialGradient>
            <radialGradient id="lvTipGlow">
              <stop offset="0%" stopColor="var(--lv-accent)" stopOpacity="0.55" />
              <stop offset="100%" stopColor="var(--lv-accent)" stopOpacity="0" />
            </radialGradient>
            <pattern id="lvGrid" width={10 * mmPx * 10} height={10 * mmPx * 10} patternUnits="userSpaceOnUse">
              <path d={`M ${10 * mmPx * 10} 0 L 0 0 0 ${10 * mmPx * 10}`} fill="none" stroke="var(--lv-grid)" strokeWidth="1" />
            </pattern>
          </defs>
          <rect width={W} height={H} fill="url(#lvGrid)" />

          {/* Torso, ribs, navel (wall view) */}
          {active === "wall" && (
            <g>
              <path
                d={torsoPath(sx, sy)}
                fill="url(#lvSkin)" stroke="var(--lv-skin-edge)" strokeWidth="1.5"
              />
              <path d={costalPath(sx, sy)} fill="none" stroke="var(--lv-rib)" strokeWidth="1.5" strokeDasharray="4 5" />
              <circle cx={sx(0)} cy={sy(0)} r={4} fill="none" stroke="var(--lv-skin-edge)" strokeWidth="1.5" />
              <text x={sx(0.0) + 8} y={sy(0) + 16} className="lv-map-label">navel</text>
              <text x={sx(0.135)} y={sy(-0.215)} className="lv-map-label" textAnchor="middle">patient's right</text>
              <text x={sx(-0.135)} y={sy(-0.215)} className="lv-map-label" textAnchor="middle">left</text>
            </g>
          )}

          {/* Anatomy under the wall */}
          <g opacity={active === "wall" ? 0.38 : 1}>
            {Object.entries(ANATOMY_STYLE).map(([id, style]) => {
              const pts = anatomy[id];
              if (!pts || pts.length < 2) return null;
              const hot = contact === id || (id === "cbd" && cbdContact);
              return (
                <path key={id}
                  d={pts.map((p, i) => `${i ? "L" : "M"}${sx(p.z).toFixed(1)},${sy(p.x).toFixed(1)}`).join("")}
                  fill="none" stroke={style.stroke} strokeWidth={style.width * (active === "calot" ? 1.6 : 0.8) * (hot ? 1.5 : 1)}
                  strokeLinecap="round" strokeLinejoin="round"
                  className={hot ? (id === "cbd" ? "lv-danger-pulse" : "lv-hot") : undefined}
                />
              );
            })}
            {active === "calot" && anatomy.calot?.[0] && (
              <circle cx={sx(anatomy.calot[0].z)} cy={sy(anatomy.calot[0].x)} r={22 * mmPx}
                fill="var(--lv-veil)" opacity={frame ? Math.max(0.05, frame.veil * 0.5) : 0.4} />
            )}
            {active === "calot" && LABELS.map((l) => {
              const pts = anatomy[l.id];
              if (!pts || pts.length < 2) return null;
              const p = pts[Math.round(l.at * (pts.length - 1))];
              return (
                <text key={l.id} x={sx(p.z) + l.dx} y={sy(p.x) + l.dy} textAnchor={l.anchor} className="lv-map-label lv-map-label-strong">
                  {ANATOMY_STYLE[l.id].label}
                </text>
              );
            })}
          </g>

          {/* Ports: planned sites and where they actually went */}
          {active === "wall" && planned.map((p) => {
            const done = placed.get(p.id);
            return (
              <g key={p.id}>
                <circle cx={sx(p.z)} cy={sy(p.x)} r={done ? 6 : 7} fill="none" stroke="var(--lv-plan)" strokeWidth="1.5" strokeDasharray="3 3" />
                {done && (
                  <>
                    <line x1={sx(p.z)} y1={sy(p.x)} x2={sx(done.z)} y2={sy(done.x)} stroke="var(--lv-ink-2)" strokeWidth="1" />
                    <circle cx={sx(done.z)} cy={sy(done.x)} r={5} fill={offMm(p, done) > 15 ? "var(--lv-warn)" : "var(--lv-good)"} stroke="var(--lv-bg)" strokeWidth="2" />
                  </>
                )}
                <text x={sx(p.z)} y={sy(p.x) - 11} textAnchor="middle" className="lv-map-label">
                  {portLabel(p.id)}{done ? ` · ${offMm(p, done).toFixed(0)} mm` : ""}
                </text>
              </g>
            );
          })}

          {/* Entry: the mark and its tolerance */}
          {active === "wall" && entry && (
            <g>
              <circle cx={sx(entry.siteZ)} cy={sy(entry.siteX)} r={Math.max(4, entry.entryTolMm * mmPx)}
                fill={entry.entryLined ? "var(--lv-good-soft)" : "var(--lv-target-soft)"}
                stroke={entry.entryBlocked ? "var(--lv-bad)" : entry.entryLined ? "var(--lv-good)" : "var(--lv-target)"} strokeWidth="1.5" />
            </g>
          )}

          {/* Target for inside steps */}
          {target && inside && (
            <g className="lv-target-pulse" transform={`translate(${sx(target.z)},${sy(target.x)})`}>
              <circle r={active === "calot" ? 12 : 7} fill="none" stroke="var(--lv-target)" strokeWidth="2" />
              <circle r={2.5} fill="var(--lv-target)" />
            </g>
          )}

          {/* Tip trail and tip */}
          {trailPath && <path d={trailPath} fill="none" stroke="var(--lv-accent)" strokeWidth="1.6" strokeOpacity="0.5" strokeLinecap="round" strokeLinejoin="round" />}
          {tip && target && inside && (
            <line x1={sx(tip.z)} y1={sy(tip.x)} x2={sx(target.z)} y2={sy(target.x)} stroke="var(--lv-target)" strokeWidth="1" strokeDasharray="2 4" />
          )}
          {tip && (
            <g transform={`translate(${sx(tip.z)},${sy(tip.x)})`}>
              <circle r={18} fill="url(#lvTipGlow)" />
              <circle r={5.5} fill={cbdContact ? "var(--lv-bad)" : "var(--lv-accent)"} stroke="var(--lv-bg)" strokeWidth="2" />
            </g>
          )}

          {/* Trainee */}
          {head && !(headOff && active === "calot") && (
            <g transform={`translate(${sx(head.z)},${sy(head.x)})`} opacity={headOff ? 0.8 : 1}>
              <circle r={9} fill="var(--lv-surface-2)" stroke="var(--lv-ink-2)" strokeWidth="1.5" />
              <path d="M -4 -1 a 4 4 0 0 1 8 0" fill="none" stroke="var(--lv-ink-2)" strokeWidth="1.5" />
              <text y={22} textAnchor="middle" className="lv-map-label">trainee</text>
            </g>
          )}

          {/* Scale bar */}
          <g transform={`translate(${W - 16 - scaleBarMm * mmPx},${H - 16})`}>
            <line x1={0} x2={scaleBarMm * mmPx} y1={0} y2={0} stroke="var(--lv-ink-2)" strokeWidth="2" />
            <line x1={0} x2={0} y1={-4} y2={4} stroke="var(--lv-ink-2)" strokeWidth="2" />
            <line x1={scaleBarMm * mmPx} x2={scaleBarMm * mmPx} y1={-4} y2={4} stroke="var(--lv-ink-2)" strokeWidth="2" />
            <text x={(scaleBarMm * mmPx) / 2} y={-8} textAnchor="middle" className="lv-map-label">{scaleBarMm / 10} cm</text>
          </g>
        </svg>
        {!frame && <p className="lv-map-empty">Waiting for the headset…</p>}
      </div>
      <footer className="lv-legend">
        <span><i className="lv-sw lv-sw-tip" />Tip</span>
        <span><i className="lv-sw lv-sw-target" />Target</span>
        {active === "wall" ? (
          <>
            <span><i className="lv-sw lv-sw-plan" />Planned port</span>
            <span><i className="lv-sw lv-sw-good" />Placed</span>
          </>
        ) : (
          <>
            <span><i className="lv-sw lv-sw-duct" />Cystic duct</span>
            <span><i className="lv-sw lv-sw-artery" />Artery</span>
            <span><i className="lv-sw lv-sw-cbd" />CBD</span>
          </>
        )}
      </footer>
    </section>
  );
}

function offMm(a: { x: number; z: number }, b: { x: number; z: number }) {
  return Math.hypot(a.x - b.x, a.z - b.z) * 1000;
}

// Stylised torso outline from the chest to the hips (board-local metres).
function torsoPath(sx: (z: number) => number, sy: (x: number) => number) {
  const pts: [number, number][] = [
    [-0.27, -0.165], [-0.2, -0.17], [-0.12, -0.165], [-0.05, -0.15], [0.03, -0.152], [0.1, -0.165], [0.14, -0.17],
  ];
  const left = pts.map(([x, z]) => `${sx(z).toFixed(1)},${sy(x).toFixed(1)}`);
  const right = [...pts].reverse().map(([x, z]) => `${sx(-z).toFixed(1)},${sy(x).toFixed(1)}`);
  return `M${left.join(" L")} L${right.join(" L")} Z`;
}

// Costal margins meeting at the xiphoid.
function costalPath(sx: (z: number) => number, sy: (x: number) => number) {
  const arc = (side: 1 | -1) => [[-0.175, 0], [-0.15, 0.04 * side], [-0.12, 0.085 * side], [-0.1, 0.13 * side], [-0.095, 0.16 * side]]
    .map(([x, z], i) => `${i ? "L" : "M"}${sx(z).toFixed(1)},${sy(x).toFixed(1)}`).join("");
  return `${arc(1)} ${arc(-1)}`;
}
