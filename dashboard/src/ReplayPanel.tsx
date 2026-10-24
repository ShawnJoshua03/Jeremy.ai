import { useMemo } from "react";
import type { PracticeEvent, Sample } from "./types";

interface ReplayPanelProps {
  samples: Sample[];
  events: PracticeEvent[];
  currentTimeMs: number;
  durationMs: number;
  isPlaying: boolean;
  onTimeChange: (timeMs: number) => void;
  onTogglePlay: () => void;
  isDemo: boolean;
  loading: boolean;
  error?: string;
}

const width = 1000;
const height = 260;
const margin = { left: 64, right: 20, top: 18, bottom: 38 };

interface Bounds {
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
}

function timeLabel(timeMs: number) {
  const seconds = Math.floor(timeMs / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function getBounds(samples: Sample[]): Bounds | null {
  let bounds: Bounds | null = null;
  for (const sample of samples) {
    if (!sample.valid) continue;
    if (!bounds) {
      bounds = {
        minX: sample.pos[0],
        maxX: sample.pos[0],
        minZ: sample.pos[2],
        maxZ: sample.pos[2],
      };
      continue;
    }
    bounds.minX = Math.min(bounds.minX, sample.pos[0]);
    bounds.maxX = Math.max(bounds.maxX, sample.pos[0]);
    bounds.minZ = Math.min(bounds.minZ, sample.pos[2]);
    bounds.maxZ = Math.max(bounds.maxZ, sample.pos[2]);
  }
  return bounds;
}

function getCoordinates(bounds: Bounds | null) {
  if (!bounds) return () => null;
  const xSpan = Math.max(bounds.maxX - bounds.minX, 0.1);
  const zSpan = Math.max(bounds.maxZ - bounds.minZ, 0.1);
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;

  return (sample: Sample) => {
    if (!sample.valid) return null;
    return {
      x: margin.left + ((sample.pos[0] - bounds.minX) / xSpan) * plotWidth,
      y: margin.top + plotHeight - ((sample.pos[2] - bounds.minZ) / zSpan) * plotHeight,
    };
  };
}

// Same rule as backend/scoring.py: a gap longer than this is not drawn as motion.
const MAX_GAP_MS = 250;

function pathSegments(samples: Sample[], position: (sample: Sample) => { x: number; y: number } | null) {
  const paths: string[] = [];
  let active: string[] = [];
  let lastMs: number | null = null;
  for (const sample of samples) {
    const point = position(sample);
    const gap = lastMs !== null && sample.t_ms - lastMs > MAX_GAP_MS;
    lastMs = sample.t_ms;
    if (!point || gap) {
      if (active.length > 0) paths.push(active.join(" "));
      active = [];
      if (point) active.push(`M${point.x.toFixed(1)},${point.y.toFixed(1)}`);
      continue;
    }
    active.push(`${active.length === 0 ? "M" : "L"}${point.x.toFixed(1)},${point.y.toFixed(1)}`);
  }
  if (active.length > 0) paths.push(active.join(" "));
  return paths;
}

export function ReplayPanel({
  samples,
  events,
  currentTimeMs,
  durationMs: playbackDurationMs,
  isPlaying,
  onTimeChange,
  onTogglePlay,
  isDemo,
  loading,
  error,
}: ReplayPanelProps) {
  const durationMs = Math.max(
    samples.reduce((max, sample) => Math.max(max, sample.t_ms), 0),
    events.reduce((max, event) => Math.max(max, event.t_ms), 0),
    playbackDurationMs,
    1000,
  );
  const bounds = useMemo(() => getBounds(samples), [samples]);
  const position = useMemo(() => getCoordinates(bounds), [bounds]);
  const paths = useMemo(() => {
    return {
      left: pathSegments(
        samples.filter((sample) => sample.instrument === "left"),
        position,
      ),
      right: pathSegments(
        samples.filter((sample) => sample.instrument === "right"),
        position,
      ),
    };
  }, [position, samples]);
  const tips = useMemo(() => {
    const latest = new Map<string, Sample>();
    for (const sample of samples) {
      if (
        sample.valid &&
        sample.t_ms <= currentTimeMs &&
        (!latest.has(sample.instrument) ||
          latest.get(sample.instrument)!.t_ms < sample.t_ms)
      ) {
        latest.set(sample.instrument, sample);
      }
    }
    return [...latest.values()];
  }, [currentTimeMs, samples]);
  const markers = events.filter((event) => event.kind !== "transfer_success");

  return (
    <section className="panel replay-panel" aria-labelledby="replay-heading">
      <header className="panel-heading">
        <div>
          <h2 id="replay-heading">Instrument replay</h2>
          <p className="panel-subtitle">
            Board-local tip paths
            {isDemo ? " · preview data" : " · REST samples"}
          </p>
        </div>
        <div className="replay-actions">
          <button
            type="button"
            className="button button-secondary play-button"
            onClick={onTogglePlay}
            disabled={samples.length === 0}
          >
            {isPlaying ? "Pause replay" : "Play replay"}
          </button>
          <span className="replay-time num">{timeLabel(currentTimeMs)}</span>
        </div>
      </header>

      {error ? (
        <div className="inline-error" role="alert">
          <span>{error}</span>
        </div>
      ) : (
        <>
          <div className="plot-wrap">
            <svg
              className="replay-plot"
              viewBox={`0 0 ${width} ${height}`}
              role="img"
              aria-label="Left and right instrument tip paths across the peg board"
            >
              <title>Instrument tip paths in the board's local frame</title>
              {[0, 1, 2, 3].map((step) => {
                const y =
                  margin.top +
                  (step * (height - margin.top - margin.bottom)) / 3;
                const z =
                  bounds &&
                  (bounds.maxZ - (step * (bounds.maxZ - bounds.minZ)) / 3).toFixed(2);
                return (
                  <g key={step}>
                    <line
                      x1={margin.left}
                      x2={width - margin.right}
                      y1={y}
                      y2={y}
                      className="chart-grid"
                    />
                    {bounds && (
                      <text
                        x={margin.left - 9}
                        y={y + 4}
                        textAnchor="end"
                        className="chart-label"
                      >
                        {z}
                      </text>
                    )}
                  </g>
                );
              })}
              {[0, 1, 2].map((step) => {
                const x =
                  margin.left +
                  (step * (width - margin.left - margin.right)) / 2;
                const value =
                  bounds &&
                  (
                    bounds.minX +
                    (step * (bounds.maxX - bounds.minX)) / 2
                  ).toFixed(2);
                return (
                  <g key={step}>
                    <line
                      x1={x}
                      x2={x}
                      y1={margin.top}
                      y2={height - margin.bottom}
                      className="chart-grid vertical"
                    />
                    {bounds && (
                      <text
                        x={x}
                        y={height - 10}
                        textAnchor={step === 0 ? "start" : step === 2 ? "end" : "middle"}
                        className="chart-label"
                      >
                        {value}
                      </text>
                    )}
                  </g>
                );
              })}
              <line
                x1={margin.left}
                x2={width - margin.right}
                y1={height - margin.bottom}
                y2={height - margin.bottom}
                className="chart-axis"
              />
              <text x={margin.left} y={margin.top - 5} className="chart-label">
                Board x / z (m)
              </text>
              {paths.left.map((path, index) => (
                <path key={`left-${index}`} d={path} className="path-left" />
              ))}
              {paths.right.map((path, index) => (
                <path key={`right-${index}`} d={path} className="path-right" />
              ))}
              {tips.map((sample) => {
                  const point = position(sample);
                  if (!point) return null;
                  return (
                    <circle
                      key={`tip-${sample.instrument}`}
                      cx={point.x}
                      cy={point.y}
                      r="6"
                      className={
                        sample.instrument === "left" ? "tip-left" : "tip-right"
                      }
                    />
                  );
                })}
            </svg>
            {loading && <span className="plot-note">Loading motion samples…</span>}
            {!loading && !error && samples.length === 0 && (
              <span className="plot-note">
                {isDemo
                  ? "No sample motion recorded for this session."
                  : "No pose samples recorded yet."}
              </span>
            )}
          </div>

          <div className="legend" aria-label="Instrument paths">
            <span><i className="legend-dot left-dot" />Left instrument</span>
            <span><i className="legend-dot right-dot" />Right instrument</span>
            <span><i className="legend-dot error-dot" />Error event</span>
          </div>

          <div className="timeline">
            <div className="timeline-track" aria-hidden="true">
              <span className="timeline-progress" style={{ width: `${(currentTimeMs / durationMs) * 100}%` }} />
              {markers.map((event) => (
                <button
                  key={event.event_id}
                  type="button"
                  className="event-marker"
                  style={{ left: `${(event.t_ms / durationMs) * 100}%` }}
                  aria-label={`${event.kind.replaceAll("_", " ")} at ${timeLabel(event.t_ms)}`}
                  title={`${event.kind.replaceAll("_", " ")} · ${timeLabel(event.t_ms)}`}
                  onClick={() => onTimeChange(event.t_ms)}
                />
              ))}
            </div>
            <label className="sr-only" htmlFor="replay-time">
              Replay position
            </label>
            <input
              id="replay-time"
              className="timeline-input"
              type="range"
              min="0"
              max={durationMs}
              step="250"
              value={Math.min(currentTimeMs, durationMs)}
              onChange={(event) => onTimeChange(Number(event.currentTarget.value))}
            />
            <div className="timeline-labels">
              <span>0:00</span>
              <span>{timeLabel(durationMs)}</span>
            </div>
          </div>

          <div className="event-list">
            <div className="event-list-heading">
              <h3>Error events</h3>
              <span>{markers.length} recorded</span>
            </div>
            {markers.length > 0 ? (
              markers.map((event) => (
                <div className="event-row" key={event.event_id}>
                  <div>
                    <strong>{event.kind === "drop" ? "Dropped peg" : "Invalid release"}</strong>
                    <span className="event-meta">
                      {timeLabel(event.t_ms)}
                      {event.instrument ? ` · ${event.instrument} instrument` : ""}
                      {event.object_id ? ` · ${event.object_id}` : ""}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="text-button"
                    onClick={() => onTimeChange(event.t_ms)}
                    title="Jump to this event in replay"
                  >
                    Jump to event
                  </button>
                </div>
              ))
            ) : (
              <p className="empty-copy">
                {isDemo
                  ? "No errors in this preview session."
                  : "Error markers are unavailable: the REST API accepts events but does not expose an event history."}
              </p>
            )}
          </div>
        </>
      )}
    </section>
  );
}
