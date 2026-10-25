import { useEffect, useRef, useState, type FormEvent } from "react";
import { formatClock } from "./model";

export interface LogLine { key: string; t: number; who: string; text: string; tone: string }

/** A quiet, append-only text log. Sticks to the bottom only if the reader is already there. */
export function Log({ title, lines, empty, onNote }: {
  title: string; lines: readonly LogLine[]; empty: string; onNote?: (text: string) => Promise<void>;
}) {
  const listRef = useRef<HTMLOListElement>(null);
  const atBottom = useRef(true);
  const [note, setNote] = useState("");
  useEffect(() => {
    const el = listRef.current;
    if (el && atBottom.current) el.scrollTop = el.scrollHeight;
  }, [lines.length]);
  async function submit(event: FormEvent) {
    event.preventDefault();
    const text = note.trim();
    if (!text || !onNote) return;
    setNote("");
    await onNote(text);
  }
  return (
    <section className="lv-log" aria-label={title}>
      <h2>{title}</h2>
      <ol ref={listRef} onScroll={(e) => { const el = e.currentTarget; atBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 24; }}>
        {lines.length === 0 && <li className="lv-muted">{empty}</li>}
        {lines.map((l) => (
          <li key={l.key} className={l.tone ? `lv-tone-${l.tone}` : undefined}>
            <time className="lv-num">{formatClock(l.t)}</time>
            <span>{l.who && <b>{l.who} </b>}{l.text}</span>
          </li>
        ))}
      </ol>
      {onNote && (
        <form onSubmit={submit} className="lv-note">
          <input value={note} maxLength={300} placeholder="Add a note to this attempt" aria-label="Instructor note" onChange={(e) => setNote(e.currentTarget.value)} />
        </form>
      )}
    </section>
  );
}
