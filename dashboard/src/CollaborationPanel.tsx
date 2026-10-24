import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useQuery } from "convex/react";
import { api } from "./api";
import { convexApi, type CoachCommand } from "./convexApi";
import type { InstructorAnnotation, PracticeEvent } from "./types";

interface CollaborationPanelProps {
  sessionId: string;
  currentTimeMs: number;
  events: PracticeEvent[];
  convexReady: boolean;
  onSelectTime: (timeMs: number) => void;
}

interface DisplayAnnotation {
  id: string;
  atMs: number;
  text: string;
  author: string;
}

function formatTime(timeMs: number) {
  const seconds = Math.floor(timeMs / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function toDisplayAnnotation(annotation: InstructorAnnotation): DisplayAnnotation | null {
  const atMs = annotation.at_ms ?? annotation.atMs;
  if (typeof atMs !== "number" || !Number.isFinite(atMs) || typeof annotation.text !== "string") {
    return null;
  }
  return {
    id: annotation.id ?? annotation.annotation_id ?? annotation.annotationId ?? `${atMs}-${annotation.text}`,
    atMs,
    text: annotation.text,
    author: typeof annotation.author === "string" ? annotation.author : "Instructor",
  };
}

function ConvexCollaborationBridge({
  sessionId,
  onAnnotations,
  onCommands,
}: {
  sessionId: string;
  onAnnotations: (rows: DisplayAnnotation[] | undefined) => void;
  onCommands: (rows: CoachCommand[] | undefined) => void;
}) {
  const annotations = useQuery(convexApi.annotations.list, { sessionId });
  const commands = useQuery(convexApi.commands.list, { sessionId });

  useEffect(() => {
    onAnnotations(
      annotations?.map((annotation) => ({
        id: annotation.annotationId,
        atMs: annotation.atMs,
        text: annotation.text,
        author: annotation.author,
      })),
    );
  }, [annotations, onAnnotations]);

  useEffect(() => onCommands(commands), [commands, onCommands]);
  return null;
}

export function CollaborationPanel({
  sessionId,
  currentTimeMs,
  events,
  convexReady,
  onSelectTime,
}: CollaborationPanelProps) {
  const [restAnnotations, setRestAnnotations] = useState<DisplayAnnotation[] | null>(null);
  const [mirroredAnnotations, setMirroredAnnotations] = useState<DisplayAnnotation[]>();
  const [commands, setCommands] = useState<CoachCommand[]>();
  const [annotationsLoading, setAnnotationsLoading] = useState(true);
  const [annotationsError, setAnnotationsError] = useState("");
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const refreshAnnotations = useCallback(async (signal?: AbortSignal) => {
    try {
      const rows: InstructorAnnotation[] = await api.getAnnotations(sessionId, signal);
      setRestAnnotations(
        rows
          .map(toDisplayAnnotation)
          .filter((annotation): annotation is DisplayAnnotation => annotation !== null)
          .sort((left, right) => left.atMs - right.atMs),
      );
      setAnnotationsError("");
    } catch (cause) {
      if (cause instanceof Error && cause.name === "AbortError") return;
      setRestAnnotations(null);
      setAnnotationsError(
        cause instanceof Error ? cause.message : "Instructor notes could not be loaded.",
      );
    } finally {
      if (!signal?.aborted) setAnnotationsLoading(false);
    }
  }, [sessionId]);

  const updateMirroredAnnotations = useCallback(
    (rows: DisplayAnnotation[] | undefined) => setMirroredAnnotations(rows),
    [],
  );
  const updateCommands = useCallback(
    (rows: CoachCommand[] | undefined) => setCommands(rows),
    [],
  );

  useEffect(() => {
    const controller = new AbortController();
    setAnnotationsLoading(true);
    void refreshAnnotations(controller.signal);
    const timer = window.setInterval(
      () => void refreshAnnotations(controller.signal),
      10_000,
    );
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [refreshAnnotations]);

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
      await refreshAnnotations();
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

  const annotations = restAnnotations ?? mirroredAnnotations;
  const orderedAnnotations = [...(annotations ?? [])].sort((left, right) => left.atMs - right.atMs);
  const recentCommands = (commands ?? []).slice(0, 5);

  return (
    <>
      {convexReady && (
        <ConvexCollaborationBridge
          sessionId={sessionId}
          onAnnotations={updateMirroredAnnotations}
          onCommands={updateCommands}
        />
      )}
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
        {!commands && (
          <p className="small-note">
            {convexReady
              ? "Connecting to the command mirror…"
              : "Convex is unavailable; command history cannot be shown."}
          </p>
        )}
      </section>

      <section className="panel collaboration-panel" aria-labelledby="annotations-heading">
        <header className="panel-heading">
          <div>
            <h2 id="annotations-heading">Instructor notes</h2>
            <p className="panel-subtitle">Backend notes · attached to {formatTime(currentTimeMs)}</p>
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
        {annotationsError && (
          <div className="form-error" role="status">
            {mirroredAnnotations !== undefined
              ? `Backend notes unavailable; showing the Convex mirror. ${annotationsError}`
              : `Backend notes could not be loaded. ${annotationsError}`}
            <button
              type="button"
              className="text-button"
              onClick={() => {
                setAnnotationsLoading(true);
                void refreshAnnotations();
              }}
            >
              Retry
            </button>
          </div>
        )}
        <div className="annotation-list">
          {annotations === undefined ? (
            annotationsLoading ? <p className="empty-copy">Loading instructor notes…</p> : null
          ) : orderedAnnotations.length > 0 ? (
            orderedAnnotations.map((annotation) => (
              <button
                type="button"
                className="annotation-row"
                key={annotation.id}
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
