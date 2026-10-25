// At most three numbers for the current step.
import type { LiveFrame } from "./model";
import { ENTRY_STEPS, structureName } from "./model";

interface Item { label: string; value: string; unit?: string; note?: string; bad?: boolean }

function items(f: LiveFrame): Item[] {
  if (ENTRY_STEPS.has(f.stepId) && f.entryActive) {
    const off = f.entryMm >= 0 ? f.entryMm : null;
    const ang = f.entryAngle >= 0 ? f.entryAngle : null;
    const depth = f.entryInserted ? f.entryDepth : null;
    return [
      { label: "Offset", value: off !== null ? off.toFixed(0) : "—", unit: "mm", note: `within ${f.entryTolMm.toFixed(0)}` },
      { label: "Angle", value: ang !== null ? ang.toFixed(0) : "—", unit: "°", note: `within ${f.entryTolDeg.toFixed(0)}` },
      { label: "Depth", value: depth !== null ? depth.toFixed(0) : "—", unit: "mm", note: `${f.entryMinDepth.toFixed(0)}–${f.entryMaxDepth.toFixed(0)}`, bad: depth !== null && depth > f.entryDanger },
    ];
  }
  if (f.stepId === "insufflate") {
    return [{ label: "Pressure", value: f.pressure.toFixed(1), unit: "mmHg", note: "target 12–15" }];
  }
  const out: Item[] = [
    { label: "To target", value: f.tipValid && f.targetMm >= 0 ? f.targetMm.toFixed(0) : "—", unit: "mm" },
    { label: "Jaws on", value: f.contact ? structureName(f.contact) : "—", bad: f.contact === "cbd" },
  ];
  if (f.stepId === "dissect") out.push({ label: "Cleared", value: Math.round((1 - f.veil) * 100).toString(), unit: "%" });
  else if (f.stepId === "cholangiogram") out.push({ label: "Contrast", value: Math.round(f.contrast * 100).toString(), unit: "%" });
  else if (f.stepIndex >= 8) out.push({ label: "Clips", value: `${f.ductClips} · ${f.arteryClips}`, note: "duct · artery" });
  return out;
}

export function Metrics({ frame, ended }: { frame: LiveFrame | undefined; ended: boolean }) {
  if (!frame || ended) return null;
  return (
    <dl className="lv-metrics">
      {items(frame).map((m) => (
        <div key={m.label}>
          <dt>{m.label}</dt>
          <dd className={m.bad ? "lv-bad" : undefined}>
            <span className="lv-big lv-num">{m.value}</span>{m.unit && <span className="lv-muted"> {m.unit}</span>}
          </dd>
          {m.note && <dd className="lv-muted lv-small">{m.note}</dd>}
        </div>
      ))}
    </dl>
  );
}
