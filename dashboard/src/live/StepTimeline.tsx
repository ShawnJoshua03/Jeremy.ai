import type { StepView } from "./model";
import { formatClock } from "./model";
import { Icon } from "./icons";

const PHASES = [
  { label: "Access", from: 0, to: 1 },
  { label: "Ports", from: 2, to: 5 },
  { label: "Exposure", from: 6, to: 7 },
  { label: "Clip & divide", from: 8, to: 14 },
];

export function StepTimeline({ steps }: { steps: StepView[] }) {
  const active = steps.find((s) => s.status === "active");
  const done = steps.filter((s) => s.status === "done").length;
  return (
    <section className="lv-card lv-timeline" aria-label="Procedure steps">
      <header className="lv-card-head">
        <div>
          <h2>Procedure</h2>
          <p className="lv-sub">{done} of {steps.length} steps{active ? ` · now: ${active.title}` : done === steps.length ? " · complete" : ""}</p>
        </div>
      </header>
      <div className="lv-phases" aria-hidden="true">
        {PHASES.map((p) => (
          <span key={p.label} style={{ gridColumn: `${p.from + 1} / ${p.to + 2}` }}>{p.label}</span>
        ))}
      </div>
      <ol className="lv-steps">
        {steps.map((s) => {
          const tone = s.severity === "major" ? "bad" : s.severity === "minor" ? "warn" : s.status === "done" ? "good" : s.status === "active" ? "active" : "idle";
          const duration = s.startS !== null && s.endS !== null ? s.endS - s.startS : null;
          return (
            <li key={s.id} className={`lv-step lv-step-${tone}${s.status === "active" ? " lv-step-now" : ""}`}
              title={`${s.index + 1}. ${s.title}${duration !== null ? ` · ${formatClock(duration)}` : ""}${s.severity === "major" ? " · major error" : s.severity === "minor" ? " · minor finding" : ""}`}>
              <span className="lv-step-bar" />
              <span className="lv-step-num">
                {s.status === "done" && !s.severity ? <Icon name="check" size={12} /> : s.severity === "major" ? <Icon name="octagon" size={12} /> : s.severity === "minor" ? <Icon name="alert" size={12} /> : s.index + 1}
              </span>
              <span className="lv-step-name">{s.short}</span>
              <span className="lv-step-time">{duration !== null ? formatClock(duration) : "—"}</span>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
