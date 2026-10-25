// Top-down map in the board-local frame (origin = navel, +x toward the feet,
// +z toward the patient's right): head up, patient's right on the left.
// Shows the abdominal wall for the entry steps and Calot's triangle after.
import type { Anatomy, LiveFrame, PortMark, Vec3 } from "./model";

interface Box { x0: number; x1: number; z0: number; z1: number }
const WALL: Box = { x0: -0.25, x1: 0.1, z0: -0.19, z1: 0.19 };
const CALOT: Box = { x0: -0.2, x1: -0.03, z0: -0.03, z1: 0.17 };
const W = 400;

const STRUCTURES: [string, string, number][] = [
  ["gallbladder", "var(--lv-s-gall)", 8],
  ["cbd", "var(--lv-s-cbd)", 3.5],
  ["hepatic_artery", "var(--lv-s-artery)", 2],
  ["cystic_duct", "var(--lv-s-duct)", 2.5],
  ["cystic_artery", "var(--lv-s-artery)", 2],
];

interface MapProps { frame: LiveFrame | undefined; anatomy: Anatomy; ports: PortMark[]; trail: readonly Vec3[]; stepIndex: number }

export function AbdomenMap({ frame, anatomy, ports, trail, stepIndex }: MapProps) {
  const calot = stepIndex >= 6;
  const box = calot ? CALOT : WALL;
  const scale = W / (box.z1 - box.z0);
  const H = Math.round((box.x1 - box.x0) * scale);
  const sx = (z: number) => (box.z1 - z) * scale;
  const sy = (x: number) => (x - box.x0) * scale;
  const line = (pts: readonly { x: number; z: number }[]) => pts.map((p, i) => `${i ? "L" : "M"}${sx(p.z).toFixed(1)},${sy(p.x).toFixed(1)}`).join("");

  const planned = ports.filter((p) => !p.placed);
  const placed = new Map(ports.filter((p) => p.placed).map((p) => [p.id, p]));
  const tip = frame?.tipValid ? frame : null;
  const cbd = frame?.contact === "cbd";

  return (
    <section className="lv-map" aria-label="Abdomen map">
      <h2>{calot ? "Calot's triangle" : "Abdominal wall"} <span className="lv-muted">· top-down, patient's right on the left</span></h2>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Map of ports, anatomy and the instrument tip">
        {!calot && (
          <g fill="none" stroke="var(--lv-line)" strokeWidth="1">
            <path d={torso(sx, sy)} />
            <circle cx={sx(0)} cy={sy(0)} r={3} />
          </g>
        )}
        <g opacity={calot ? 0.9 : 0.25} fill="none" strokeLinecap="round" strokeLinejoin="round">
          {STRUCTURES.map(([id, color, width]) => {
            const pts = anatomy[id];
            if (!pts || pts.length < 2) return null;
            const hot = id === "cbd" && cbd;
            return <path key={id} d={line(pts)} stroke={hot ? "var(--lv-red)" : color} strokeWidth={width * (calot ? 1.4 : 0.8)} />;
          })}
        </g>
        {!calot && planned.map((p) => {
          const done = placed.get(p.id);
          return (
            <g key={p.id}>
              <circle cx={sx(p.z)} cy={sy(p.x)} r={6} fill="none" stroke="var(--lv-muted)" strokeWidth="1" strokeDasharray="2 3" />
              {done && <circle cx={sx(done.z)} cy={sy(done.x)} r={3.5} fill="var(--lv-ink)" />}
            </g>
          );
        })}
        {!calot && frame?.entryActive && (
          <circle cx={sx(frame.siteZ)} cy={sy(frame.siteX)} r={Math.max(4, frame.entryTolMm * scale / 1000)} fill="none" stroke="var(--lv-ink-2)" strokeWidth="1" />
        )}
        {calot && frame && (
          <circle cx={sx(frame.targetZ)} cy={sy(frame.targetX)} r={8} fill="none" stroke="var(--lv-ink-2)" strokeWidth="1.2" />
        )}
        {trail.length > 1 && <path d={line(trail)} fill="none" stroke="var(--lv-accent)" strokeOpacity="0.35" strokeWidth="1.2" />}
        {tip && <circle cx={sx(tip.tipZ)} cy={sy(tip.tipX)} r={4} fill={cbd ? "var(--lv-red)" : "var(--lv-accent)"} />}
      </svg>
    </section>
  );
}

function torso(sx: (z: number) => number, sy: (x: number) => number) {
  const pts: [number, number][] = [[-0.25, -0.165], [-0.12, -0.165], [-0.05, -0.15], [0.03, -0.152], [0.1, -0.165]];
  const l = pts.map(([x, z]) => `${sx(z).toFixed(1)},${sy(x).toFixed(1)}`);
  const r = [...pts].reverse().map(([x, z]) => `${sx(-z).toFixed(1)},${sy(x).toFixed(1)}`);
  return `M${l.join(" L")} M${r.join(" L")}`;
}
