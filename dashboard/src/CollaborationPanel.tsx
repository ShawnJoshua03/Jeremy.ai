import { useState, type FormEvent } from "react";
import { useQuery } from "convex/react";
import { api } from "./api";
import { convexApi } from "./convexApi";
import type { PracticeEvent } from "./types";

interface CollaborationPanelProps {
  sessionId: string;
  currentTimeMs: number;
  events: PracticeEvent[];
  onSelectTime: (timeMs: number) => void;
}

function formatTime(timeMs: number) {
  const seconds = Math.floor(timeMs / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

export function CollaborationPanel({
  sessionId,
  currentTimeMs,
  events,
  onSelectTime,
}: CollaborationPanelProps) {
  const annotations = useQuery(convexApi.annotations.list, { sessionId });
  const commands = useQuery(convexApi.commands.list, { sessionId });
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function addAnnotation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await api.addAnnotation(sessionId, {
        at_ms: currentTimeMs,
        text: text.trim(),
        author: "Instructor",
      });
      setText("");
      setMessage(`Note sent to the backend at ${formatTime(currentTimeMs)}.`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Couldn't save the note.");
    } finally {
      setBusy(false);
    }
  }

  async function issueCommand(type: "highlight" | "pause" | "retry", atMs?: number) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await api.sendCommand(sessionId, {
        type,
        ...(atMs === undefined ? {} : { at_ms: atMs }),
      });
      setMessage(
        type === "highlight"
          ? `Highlight queued by the backend for ${formatTime(atMs ?? currentTimeMs)}.`
          : `${type === "pause" ? "Pause" : "Retry"} command queued by the backend.`,
      );
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Couldn't send the command.");
    } finally {
      setBusy(false);
    }
  }

  const orderedAnnotations = [...(annotations ?? [])].reverse();
  const recentCommands = (commands ?? []).slice(0, 5);
  const commandsReady = commands !== undefined;

  return (
    <>
      <section className="panel collaboration-panel" aria-labelledby="commands-heading">
        <header className="panel-heading">
          <div>
            <h2 id="commands-heading">Headset controls</h2>
            <p className="panel-subtitle">Commands are queued by the backend and mirrored live</p>
          </div>
        </header>
        <div className="command-actions">
          <button
            type="button"
            className="button button-secondary"
            onClick={() => void issueCommand("highlight", currentTimeMs)}
            disabled={busy}
          >
            Highlight current time
          </button>
          <button
            type="button"
            className="button button-secondary"
            onClick={() => void issueCommand("pause")}
            disabled={busy}
          >
            Pause
          </button>
          <button
            type="button"
            className="button button-secondary"
            onClick={() => void issueCommand("retry")}
            disabled={busy}
          >
            Retry exercise
          </button>
          {events
            .filter((event) => event.kind !== "transfer_success")
            .map((event) => (
              <button
                type="button"
                className="button button-tertiary"
                key={event.event_id}
                onClick={() => {
                  onSelectTime(event.t_ms);
                  void issueCommand("highlight", event.t_ms);
                }}
                disabled={busy}
              >
                Highlight {formatTime(event.t_ms)}
              </button>
            ))}
        </div>
        <p className="small-note">
          Commands remain queued until the headset client acknowledges them.
        </p>
        {recentCommands.length > 0 && (
          <div className="command-history" aria-label="Recent headset commands">
            {recentCommands.map((command) => (
              <div className="command-row" key={command._id}>
                <span>
                  {command.type === "highlight" && command.atMs !== null
                    ? `Highlight · ${formatTime(command.atMs)}`
                    : command.type === "pause"
                      ? "Pause exercise"
                      : "Retry exercise"}
                </span>
                <span className={`command-state ${command.state}`}>
                  {command.state === "queued" ? "Queued" : "Acknowledged"}
                </span>
              </div>
            ))}
          </div>
        )}
        {!commandsReady && <p className="small-note">Connecting to live commands…</p>}
      </section>

      <section className="panel collaboration-panel" aria-labelledby="annotations-heading">
        <header className="panel-heading">
          <div>
            <h2 id="annotations-heading">Instructor notes</h2>
            <p className="panel-subtitle">Saved by the backend · attached to {formatTime(currentTimeMs)}</p>
          </div>
        </header>
        <form className="annotation-form" onSubmit={(event) => void addAnnotation(event)}>
          <label className="sr-only" htmlFor="annotation-text">
            Add an instructor note
          </label>
          <textarea
            id="annotation-text"
            value={text}
            maxLength={500}
            onChange={(event) => setText(event.currentTarget.value)}
            placeholder="Add a coaching note at the current time…"
            rows={3}
            required
          />
          <div className="form-footer">
            <span>{text.length}/500</span>
            <button className="button button-primary" type="submit" disabled={busy || !text.trim()}>
              Add note
            </button>
          </div>
        </form>
        {error && <p className="form-error" role="alert">{error}</p>}
        {message && <p className="form-success" role="status">{message}</p>}
        <div className="annotation-list">
          {annotations === undefined ? (
            <p className="empty-copy">Loading instructor notes…</p>
          ) : orderedAnnotations.length > 0 ? (
            orderedAnnotations.map((annotation) => (
              <button
                type="button"
                className="annotation-row"
                key={annotation._id}
                onClick={() => onSelectTime(annotation.atMs)}
                aria-label={`Go to note at ${formatTime(annotation.atMs)}`}
              >
                <span className="annotation-time num">{formatTime(annotation.atMs)}</span>
                <span className="annotation-content">
                  <span>{annotation.text}</span>
                  <small>{annotation.author}</small>
                </span>
              </button>
            ))
          ) : (
            <p className="empty-copy">No notes on this session yet.</p>
          )}
        </div>
      </section>
    </>
  );
}
