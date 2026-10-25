// Small summary visuals: per-step durations and port placement accuracy.
import type { StepView } from "./model";
import { formatClock } from "./model";

export function StepBars({ steps }: { steps: StepView[] }) {
  const durations = steps.map((s) => (s.startS !== null && s.endS !== null ? Math.max(0, s.endS - s.startS) : null));
  const max = Math.max(30, ...durations.map((d) => d ?? 0));
  return (
    <section className="lv-panel">
      <h2>Time per step</h2>
      <ol className="lv-bars">
        {steps.map((s, i) => {
          const d = durations[i];
          const tone = s.severity === "major" ? "bad" : s.severity === "minor" ? "warn" : s.status === "active" ? "active" : "";
          return (
            <li key={s.id} className={s.status === "pending" ? "lv-pending" : undefined}>
              <span className="lv-bar-label">{s.short}</span>
              <span className="lv-bar-track"><span className={`lv-bar lv-bar-${tone}`} style={{ width: `${d !== null ? (d / max) * 100 : 0}%` }} /></span>
              <span className="lv-num lv-muted">{d !== null ? formatClock(d) : ""}</span>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
