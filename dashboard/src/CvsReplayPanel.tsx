import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import type { ReplayEvent, SessionReplay } from "./types";

interface CvsReplayPanelProps {
  sessionId: string;
  isDemo: boolean;
  demoReplay?: SessionReplay;
}

interface Point {
  t: number;
  x: number;
  y: number;
  z: number;
}

const plotWidth = 760;
const plotHeight = 245;
const inset = 24;
const trackColors: Record<string, string> = {
  left: "var(--accent)",
  right: "var(--blue)",
  head: "#69736f",
};

function labelTime(milliseconds: number) {
  const seconds = Math.floor(milliseconds / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function readableEvent(event: ReplayEvent) {
  return [
    event.kind.replaceAll("_", " "),
    event.object_id,
    event.detail,
  ].filter(Boolean).join(" · ");
}

function pointsFromReplay(replay: SessionReplay): Record<string, Point[]> {
  return Object.fromEntries(
    Object.entries(replay.tracks).map(([instrument, samples]) => [
      instrument,
      samples
        .filter((sample) => sample.length >= 4 && sample.every(Number.isFinite))
        .map(([t, x, y, z]) => ({ t, x, y, z })),
    ]),
  );
}

function project(points: Point[], horizontal: "x", vertical: "y" | "z") {
  if (points.length === 0) return () => null;
  const minX = Math.min(...points.map((point) => point[horizontal]));
  const maxX = Math.max(...points.map((point) => point[horizontal]));
  const minY = Math.min(...points.map((point) => point[vertical]));
  const maxY = Math.max(...points.map((point) => point[vertical]));
  const xSpan = Math.max(maxX - minX, 0.1);
  const ySpan = Math.max(maxY - minY, 0.1);
  return (point: Point) => ({
    x: inset + ((point[horizontal] - minX) / xSpan) * (plotWidth - inset * 2),
    y: plotHeight - inset - ((point[vertical] - minY) / ySpan) * (plotHeight - inset * 2),
  });
}

function Plot({
  title,
  axis,
  tracks,
  currentTime,
}: {
  title: string;
  axis: "y" | "z";
  tracks: Record<string, Point[]>;
  currentTime: number;
}) {
  const allPoints = Object.values(tracks).flat();
  const position = useMemo(() => project(allPoints, "x", axis), [allPoints, axis]);
  const minX = allPoints.length ? Math.min(...allPoints.map((point) => point.x)) : 0;
  const maxX = allPoints.length ? Math.max(...allPoints.map((point) => point.x)) : 0;
  const minVertical = allPoints.length ? Math.min(...allPoints.map((point) => point[axis])) : 0;
  const maxVertical = allPoints.length ? Math.max(...allPoints.map((point) => point[axis])) : 0;
  return (
    <section className="cvs-replay-view" aria-label={`${title} instrument paths`}>
      <h3>{title}</h3>
      {allPoints.length === 0 ? (
        <p className="report-muted">No valid movement points were recorded.</p>
      ) : (
        <svg
          className="cvs-replay-plot"
          viewBox={`0 0 ${plotWidth} ${plotHeight}`}
          role="img"
          aria-label={`${title} view of tracked instrument and head movement`}
        >
          {[0, 1, 2, 3].map((step) => {
            const y = inset + (step * (plotHeight - inset * 2)) / 3;
            return <line key={step} x1={inset} x2={plotWidth - inset} y1={y} y2={y} className="chart-grid" />;
          })}
          {Object.entries(tracks).map(([instrument, samples]) => {
            const active = samples.filter((point) => point.t <= currentTime);
            const coords = active.map(position).filter((point): point is NonNullable<typeof point> => point !== null);
            const path = coords.map((point, index) => `${index === 0 ? "M" : "L"}${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(" ");
            const current = active.at(-1);
            const dot = current ? position(current) : null;
            const color = trackColors[instrument] ?? "#7b8480";
            return (
              <g key={instrument} style={{ color }}>
                {path && <path className="cvs-track-path" d={path} />}
                {dot && <circle className="cvs-track-tip" cx={dot.x} cy={dot.y} r="5" />}
              </g>
            );
          })}
          <text x={inset} y={plotHeight - 5} className="chart-label">X (m)</text>
          <text x={inset} y={15} className="chart-label">{axis.toUpperCase()} (m)</text>
          <text x={inset} y={plotHeight - inset - 3} className="chart-label axis-vertical-value">{minVertical.toFixed(2)}</text>
          <text x={inset} y={inset + 4} className="chart-label axis-vertical-value">{maxVertical.toFixed(2)}</text>
          <text x={inset} y={plotHeight - 27} className="chart-label">{minX.toFixed(2)}</text>
          <text x={plotWidth - inset} y={plotHeight - 27} textAnchor="end" className="chart-label">{maxX.toFixed(2)}</text>
        </svg>
      )}
    </section>
  );
}

export function CvsReplayPanel({ sessionId, isDemo, demoReplay }: CvsReplayPanelProps) {
  const [replay, setReplay] = useState<SessionReplay>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [currentTime, setCurrentTime] = useState(0);
  const [playing, setPlaying] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setCurrentTime(0);
    setPlaying(false);
    setError("");
    if (isDemo) {
      setReplay(demoReplay);
      setError(demoReplay ? "" : "No replay is available for this preview session.");
      setLoading(false);
      return () => controller.abort();
    }
    setReplay(undefined);
    setLoading(true);
    void api.getReplay(sessionId, controller.signal)
      .then(setReplay)
      .catch((reason: unknown) => {
        if (reason instanceof Error && reason.name === "AbortError") return;
        setError(reason instanceof Error ? reason.message : "The session replay could not be loaded.");
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [demoReplay, isDemo, sessionId]);

  const tracks = useMemo(
    () => replay ? pointsFromReplay(replay) : {},
    [replay],
  );
  const events = replay?.events ?? [];
  const duration = Math.max(
    replay?.duration_ms ?? 0,
    events.reduce((max, event) => Math.max(max, event.t_ms), 0),
    ...Object.values(tracks).flat().map((point) => point.t),
    1000,
  );

  useEffect(() => {
    if (!playing) return;
    const timer = window.setInterval(() => {
      setCurrentTime((time) => {
        if (time + 250 >= duration) {
          setPlaying(false);
          return duration;
        }
        return time + 250;
      });
    }, 250);
    return () => window.clearInterval(timer);
  }, [duration, playing]);

  const legend = Object.keys(tracks);
  const timelineEvents = [...events].sort((a, b) => a.t_ms - b.t_ms);

  return (
    <section className="panel cvs-replay-panel" aria-labelledby="cvs-replay-heading">
      <header className="panel-heading">
        <div>
          <h2 id="cvs-replay-heading">Movement replay</h2>
          <p className="panel-subtitle">Workstation-local paths · {isDemo ? "synthetic preview" : "recorded replay"}</p>
        </div>
        <button
          type="button"
          className="button button-secondary"
          onClick={() => setPlaying((value) => !value)}
          disabled={loading || Boolean(error) || (legend.length === 0 && events.length === 0)}
        >
          {playing ? "Pause" : "Play"}
        </button>
      </header>

      {loading ? (
        <p className="report-state" role="status">Loading replay…</p>
      ) : error ? (
        <p className="report-state report-error" role="alert">{error}</p>
      ) : replay ? (
        <>
          <div className="cvs-replay-grid">
            <Plot title="Top-down view" axis="z" tracks={tracks} currentTime={currentTime} />
            <Plot title="Side view" axis="y" tracks={tracks} currentTime={currentTime} />
          </div>
          <div className="cvs-replay-legend" aria-label="Movement tracks">
            {legend.map((instrument) => (
              <span key={instrument}>
                <i style={{ background: trackColors[instrument] ?? "#7b8480" }} />
                {instrument.replaceAll("_", " ")}
              </span>
            ))}
          </div>
          <div className="cvs-replay-timeline">
            <div className="cvs-event-markers" aria-label="Replay event markers">
              {timelineEvents.map((event, index) => (
                <button
                  key={event.event_id ?? `${event.t_ms}-${event.kind}-${index}`}
                  type="button"
                  className={`cvs-event-marker ${event.kind === "structure_selected" ? "selection-marker" : event.kind === "decision_submitted" || event.kind === "instructor_requested" ? "decision-marker" : ""}`}
                  style={{ left: `${Math.min(100, Math.max(0, (event.t_ms / duration) * 100))}%` }}
                  onClick={() => setCurrentTime(event.t_ms)}
                  title={`${readableEvent(event)} · ${labelTime(event.t_ms)}`}
                  aria-label={`Jump to ${readableEvent(event)} at ${labelTime(event.t_ms)}`}
                />
              ))}
            </div>
            <label className="sr-only" htmlFor="cvs-replay-position">Replay position</label>
            <input
              id="cvs-replay-position"
              type="range"
              min="0"
              max={duration}
              step="250"
              value={Math.min(currentTime, duration)}
              onChange={(event) => setCurrentTime(Number(event.currentTarget.value))}
            />
            <div className="cvs-replay-time">
              <span>{labelTime(currentTime)}</span>
              <span>{labelTime(duration)}</span>
            </div>
          </div>
          <section className="report-section replay-events" aria-labelledby="replay-events-heading">
            <header className="report-section-heading">
              <h3 id="replay-events-heading">Recorded events</h3>
              <span>{events.length} event{events.length === 1 ? "" : "s"}</span>
            </header>
            {timelineEvents.length > 0 ? (
              <ol>
                {timelineEvents.map((event, index) => (
                  <li key={event.event_id ?? `${event.t_ms}-${event.kind}-${index}`}>
                    <button type="button" className="replay-event-jump" onClick={() => setCurrentTime(event.t_ms)}>
                      <span>{readableEvent(event)}</span>
                      <time className="num">{labelTime(event.t_ms)}</time>
                    </button>
                  </li>
                ))}
              </ol>
            ) : (
              <p className="report-muted">No events were recorded.</p>
            )}
          </section>
        </>
      ) : null}
    </section>
  );
}
