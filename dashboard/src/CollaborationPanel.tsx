import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useQuery } from "convex/react";
import { api, TARGET_IDS, targetForObject, type HeadsetCommand } from "./api";
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

function targetLabel(targetId: string) {
  return `target ${targetId.replace("target_", "")}`;
}

function commandLabel(command: CoachCommand) {
  switch (command.type) {
    case "highlight":
      return "Highlight target";
    case "pause":
      return "Pause exercise";
    case "retry":
      return "Retry exercise";
    default:
      return "Note shown in headset";
  }
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
      setMessage(`Note saved at ${formatTime(currentTimeMs)} and queued for the headset.`);
      await refreshAnnotations();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Couldn't save the note.");
    } finally {
      setBusy(false);
    }
  }

  async function issueCommand(command: HeadsetCommand) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await api.sendCommand(sessionId, command);
      setMessage(
        command.type === "highlight_target"
          ? `Highlight of ${targetLabel(command.targetId)} queued for the headset.`
          : `${command.type === "request_pause" ? "Pause" : "Retry"} queued for the headset.`,
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
            onClick={() => void issueCommand({ type: "request_pause" })}
            disabled={busy}
          >
            Pause
          </button>
          <button
            type="button"
            className="button button-secondary"
            onClick={() => void issueCommand({ type: "request_retry" })}
            disabled={busy}
          >
            Retry exercise
          </button>
        </div>
        <div className="command-actions" aria-label="Highlight a target in the headset">
          {TARGET_IDS.map((targetId) => (
            <button
              type="button"
              className="button button-tertiary"
              key={targetId}
              onClick={() => void issueCommand({ type: "highlight_target", targetId })}
              disabled={busy}
            >
              Highlight {targetLabel(targetId)}
            </button>
          ))}
          {events
            .filter((event) => event.kind !== "transfer_success")
            .map((event) => {
              const targetId = targetForObject(event.object_id);
              if (!targetId) return null;
              return (
                <button
                  type="button"
                  className="button button-tertiary"
                  key={event.event_id}
                  onClick={() => {
                    onSelectTime(event.t_ms);
                    void issueCommand({ type: "highlight_target", targetId });
                  }}
                  disabled={busy}
                >
                  Error at {formatTime(event.t_ms)} · {targetLabel(targetId)}
                </button>
              );
            })}
        </div>
        <p className="small-note">
          Commands expire after 5 minutes if the headset does not acknowledge them. The headset applies them only during an active or finished attempt.
        </p>
        {recentCommands.length > 0 && (
          <div className="command-history" aria-label="Recent headset commands">
            {recentCommands.map((command) => (
              <div className="command-row" key={command._id}>
                <span>{commandLabel(command)}</span>
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
