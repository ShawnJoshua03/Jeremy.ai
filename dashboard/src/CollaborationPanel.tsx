import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useQuery } from "convex/react";
import { api, targetForObject, type CommandHistoryRow, type HeadsetCommand } from "./api";
import { eventLabel, highlightTargets, isErrorEvent } from "./exercise";
import { convexApi, type CoachCommand } from "./convexApi";
import type { InstructorAnnotation, PracticeEvent } from "./types";

interface CollaborationPanelProps {
  sessionId: string;
  currentTimeMs: number;
  events: PracticeEvent[];
  exercise?: string;
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
  return targetId.startsWith("site_")
    ? `site ${targetId.replace("site_", "")}`
    : `target ${targetId.replace("target_", "")}`;
}

interface DisplayCommand {
  id: string;
  label: string;
  state: "queued" | "applied" | "rejected" | "expired" | "acknowledged";
  detail: string;
}

const STATE_LABEL: Record<DisplayCommand["state"], string> = {
  queued: "Queued",
  applied: "Applied",
  rejected: "Rejected",
  expired: "Expired",
  acknowledged: "Acknowledged",
};

function restCommandLabel(row: CommandHistoryRow) {
  switch (row.type) {
    case "highlight_target":
      return `Highlight ${targetLabel(row.targetId)}`;
    case "request_pause":
      return "Pause exercise";
    case "request_retry":
      return "Retry exercise";
    case "add_annotation":
      return `Note: ${row.message}`;
    default:
      return row.type;
  }
}

function fromRest(row: CommandHistoryRow): DisplayCommand {
  return { id: row.commandId, label: restCommandLabel(row), state: row.state, detail: row.ackMessage };
}

function fromConvex(command: CoachCommand): DisplayCommand {
  const label =
    command.type === "highlight"
      ? "Highlight target"
      : command.type === "pause"
        ? "Pause exercise"
        : command.type === "retry"
          ? "Retry exercise"
          : "Note shown in headset";
  return { id: command._id, label, state: command.ackStatus ?? command.state, detail: "" };
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
  exercise,
  convexReady,
  onSelectTime,
}: CollaborationPanelProps) {
  const [restAnnotations, setRestAnnotations] = useState<DisplayAnnotation[] | null>(null);
  const [mirroredAnnotations, setMirroredAnnotations] = useState<DisplayAnnotation[]>();
  const [commands, setCommands] = useState<CoachCommand[]>();
  const [restCommands, setRestCommands] = useState<DisplayCommand[] | null>(null);
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

  const refreshCommands = useCallback(async (signal?: AbortSignal) => {
    try {
      const rows = await api.getCommandHistory(sessionId, signal);
      setRestCommands(rows.map(fromRest));
    } catch (cause) {
      if (cause instanceof Error && cause.name === "AbortError") return;
      setRestCommands(null);
    }
  }, [sessionId]);

  useEffect(() => {
    const controller = new AbortController();
    void refreshCommands(controller.signal);
    const timer = window.setInterval(() => void refreshCommands(controller.signal), 3_000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [refreshCommands]);

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
      await Promise.all([refreshAnnotations(), refreshCommands()]);
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
      await refreshCommands();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Couldn't send the command.");
    } finally {
      setBusy(false);
    }
  }

  const annotations = restAnnotations ?? mirroredAnnotations;
  const orderedAnnotations = [...(annotations ?? [])].sort((left, right) => left.atMs - right.atMs);
  const recentCommands = (restCommands ?? commands?.map(fromConvex) ?? []).slice(0, 6);

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
          {highlightTargets(exercise).map((targetId) => (
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
            .filter(isErrorEvent)
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
                  {eventLabel(event.kind)} at {formatTime(event.t_ms)} · {targetLabel(targetId)}
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
              <div className="command-row" key={command.id} title={command.detail || undefined}>
                <span>{command.label}</span>
                <span className={`command-state ${command.state}`}>
                  {STATE_LABEL[command.state]}
                  {command.state === "rejected" && command.detail ? ` · ${command.detail}` : ""}
                </span>
              </div>
            ))}
          </div>
        )}
        {!restCommands && !commands && (
          <p className="small-note">
            {convexReady
              ? "Connecting to the command mirror…"
              : "Command history is unavailable from the backend and Convex."}
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
