import { useEffect, useRef, useState, type FormEvent } from "react";
import type { EventView, Finding, LiveCaption } from "./model";
import { STEPS, formatClock } from "./model";
import { Icon } from "./icons";

const SEVERITY_ICON = { major: "octagon", minor: "alert", good: "check", info: "info" } as const;
const SEVERITY_LABEL = { major: "Major", minor: "Minor", good: "Done", info: "Info" } as const;

export function EventFeed({ events, findings }: { events: readonly EventView[]; findings: readonly Finding[] }) {
  const [filter, setFilter] = useState<"all" | "issues">("all");
  const rows = [...events].reverse().filter((e) => filter === "all" || e.severity === "major" || e.severity === "minor");
  const majors = findings.filter((f) => f.major);
  const minors = findings.filter((f) => !f.major);
  return (
    <section className="lv-card lv-feed" aria-label="Events and errors">
      <header className="lv-card-head">
        <div>
          <h2>Events &amp; errors</h2>
          <p className="lv-sub">{majors.length} major · {minors.length} minor findings</p>
        </div>
        <div className="lv-seg" role="group" aria-label="Filter events">
          <button type="button" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>All</button>
          <button type="button" aria-pressed={filter === "issues"} onClick={() => setFilter("issues")}>Issues</button>
        </div>
      </header>
      {findings.length > 0 && (
        <ul className="lv-findings">
          {findings.map((f, i) => (
            <li key={`${f.code}-${i}`} className={f.major ? "lv-finding-major" : "lv-finding-minor"}>
              <Icon name={f.major ? "octagon" : "alert"} size={13} />{f.text}
            </li>
          ))}
        </ul>
      )}
      <ol className="lv-events">
        {rows.length === 0 && <li className="lv-empty">{filter === "issues" ? "No errors so far." : "Nothing recorded yet."}</li>}
        {rows.map((e) => (
          <li key={e.id.toString()} className={`lv-event lv-event-${e.severity}`}>
            <span className="lv-event-icon" title={SEVERITY_LABEL[e.severity]}><Icon name={SEVERITY_ICON[e.severity]} size={14} /></span>
            <div className="lv-event-body">
              <div className="lv-event-title">{e.title}</div>
              {e.detail && <div className="lv-event-detail">{e.detail}</div>}
            </div>
            <div className="lv-event-meta">
              <time>{formatClock(e.t)}</time>
              <span>{STEPS[e.stepIndex]?.short ?? ""}</span>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

const SPEAKER = { coach: "Coach", sim: "Sim", trainee: "Trainee", instructor: "You" } as Record<string, string>;

export function CoachFeed({ captions, coachStatus, listening, micLevel, onNote }: {
  captions: readonly LiveCaption[]; coachStatus: string; listening: boolean; micLevel: number;
  onNote: (text: string) => Promise<void>;
}) {
  const listRef = useRef<HTMLOListElement>(null);
  const [showSim, setShowSim] = useState(true);
  const [note, setNote] = useState("");
  const rows = captions.filter((c) => showSim || c.speaker !== "sim");
  useEffect(() => {
    const el = listRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [rows.length]);
  async function submit(event: FormEvent) {
    event.preventDefault();
    const text = note.trim();
    if (!text) return;
    setNote("");
    await onNote(text);
  }
  return (
    <section className="lv-card lv-coach" aria-label="Coach transcript">
      <header className="lv-card-head">
        <div>
          <h2>Coach</h2>
          <p className="lv-sub">What the trainee hears, live</p>
        </div>
        <div className="lv-row">
          <span className={`lv-mic${listening ? " lv-mic-on" : ""}`} title={listening ? "Listening to the trainee" : "Mic level"}>
            <Icon name="mic" size={13} />
            <span className="lv-mic-bar"><span style={{ width: `${Math.round(Math.min(1, micLevel) * 100)}%` }} /></span>
          </span>
          <button type="button" className="lv-btn lv-btn-small" aria-pressed={showSim} onClick={() => setShowSim(!showSim)}>Sim lines</button>
        </div>
      </header>
      {coachStatus && (
        <div className="lv-read">
          <span className="lv-read-label">Coach's read</span>
          <p>{coachStatus}</p>
        </div>
      )}
      <ol className="lv-transcript" ref={listRef}>
        {rows.length === 0 && <li className="lv-empty">The coach has not spoken yet.</li>}
        {rows.map((c) => (
          <li key={c.id.toString()} className={`lv-line lv-line-${c.speaker}`}>
            <span className="lv-line-who">{SPEAKER[c.speaker] ?? c.speaker}</span>
            <span className="lv-line-text">{c.text}</span>
            <time>{formatClock(Number(c.elapsedMs) / 1000)}</time>
          </li>
        ))}
      </ol>
      <form className="lv-note" onSubmit={submit}>
        <input value={note} maxLength={300} placeholder="Add an instructor note to this attempt" aria-label="Instructor note" onChange={(e) => setNote(e.currentTarget.value)} />
        <button type="submit" className="lv-btn lv-btn-small" disabled={!note.trim()}>Note</button>
      </form>
    </section>
  );
}
