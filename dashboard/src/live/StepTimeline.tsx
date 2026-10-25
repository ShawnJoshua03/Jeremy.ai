import type { StepView } from "./model";
import { formatClock } from "./model";

/** Fifteen thin segments; the hover title gives the step name and time. */
export function StepTimeline({ steps }: { steps: StepView[] }) {
  return (
    <ol className="lv-timeline" aria-label="Procedure steps">
      {steps.map((s) => {
        const tone = s.severity === "major" ? "bad" : s.severity === "minor" ? "warn" : s.status;
        const duration = s.startS !== null && s.endS !== null && s.status === "done" ? ` · ${formatClock(s.endS - s.startS)}` : "";
        return <li key={s.id} className={`lv-seg-${tone}`} title={`${s.index + 1}. ${s.title}${duration}`} />;
      })}
    </ol>
  );
}
