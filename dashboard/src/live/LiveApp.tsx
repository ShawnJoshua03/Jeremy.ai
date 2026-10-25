// Local live instructor view: the Quest casting window plus live sim
// telemetry from the local SpacetimeDB (liveSession / liveFrame / liveEvent /
// liveCaption, published by LiveTelemetry.cs on the headset).
import { useEffect, useMemo, useRef, useState } from "react";
import { useReducer, useSpacetimeDB, useTable } from "spacetimedb/react";
import { reducers, tables } from "../module_bindings";
import { AbdomenMap } from "./AbdomenMap";
import { CastView, type CastSource } from "./CastView";
import { CoachFeed, EventFeed } from "./Feeds";
import { Metrics } from "./Metrics";
import { StepTimeline } from "./StepTimeline";
import { Icon } from "./icons";
import {
  STEPS, buildSteps, describeEvent, formatClock, parseAnatomy, parseFindings, parsePorts, scoreBand, structureName,
  type LiveSession, type Vec3,
} from "./model";
import "./live.css";

const LIVE_MS = 2500; // frame age that still counts as live
const TRAIL = 140;
const HISTORY = 150;

function useNow(ms: number) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), ms);
    return () => window.clearInterval(id);
  }, [ms]);
  return now;
}

const bySeen = (a: LiveSession, b: LiveSession) => (a.lastSeen > b.lastSeen ? -1 : a.lastSeen < b.lastSeen ? 1 : 0);

export function LiveApp({ nav }: { nav: React.ReactNode }) {
  const { isActive, connectionError } = useSpacetimeDB();
  const now = useNow(500);
  const [sessions] = useTable(tables.liveSession);
  const [pinned, setPinned] = useState<string | null>(null);
  const sorted = useMemo(() => [...sessions].sort(bySeen), [sessions]);
  // Follow the most recently active session unless one is picked.
  const sessionId = pinned && sorted.some((s) => s.sessionId === pinned) ? pinned : sorted[0]?.sessionId ?? "";
  const session = sorted.find((s) => s.sessionId === sessionId);

  const frameQuery = useMemo(() => tables.liveFrame.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const eventQuery = useMemo(() => tables.liveEvent.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const captionQuery = useMemo(() => tables.liveCaption.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const [frames] = useTable(frameQuery);
  const [eventRows] = useTable(eventQuery);
  const [captionRows] = useTable(captionQuery);
  const frame = frames[0];
  const pushCaption = useReducer(reducers.pushCaption);
  const liveClear = useReducer(reducers.liveClear);

  const [cast, setCast] = useState<CastSource | null>(null);

  // Client-side history: tip trail, distance to target, frame rate.
  const [trail, setTrail] = useState<Vec3[]>([]);
  const [history, setHistory] = useState<number[]>([]);
  const arrivals = useRef<number[]>([]);
  const lastStep = useRef<string>("");
  const lastSeq = useRef<bigint>(-1n);
  useEffect(() => {
    setTrail([]); setHistory([]); arrivals.current = []; lastSeq.current = -1n; lastStep.current = "";
  }, [sessionId]);
  useEffect(() => {
    if (!frame || frame.seq === lastSeq.current) return;
    lastSeq.current = frame.seq;
    const t = Date.now();
    arrivals.current = [...arrivals.current.filter((a) => t - a < 3000), t];
    const stepKey = `${frame.stepIndex}`;
    const newStep = stepKey !== lastStep.current;
    lastStep.current = stepKey;
    if (frame.tipValid) {
      const p = { x: frame.tipX, y: frame.tipY, z: frame.tipZ };
      setTrail((prev) => (newStep ? [p] : [...prev.slice(-TRAIL + 1), p]));
    } else if (newStep) setTrail([]);
    if (frame.tipValid && frame.targetMm >= 0 && frame.stepIndex >= 6) setHistory((prev) => (newStep ? [frame.targetMm] : [...prev.slice(-HISTORY + 1), frame.targetMm]));
    else if (newStep) setHistory([]);
  }, [frame]);

  const hz = arrivals.current.length > 1 ? (arrivals.current.length - 1) / Math.max(0.5, (arrivals.current[arrivals.current.length - 1] - arrivals.current[0]) / 1000) : 0;
  const age = session ? now - Number(session.lastSeen) : Infinity;
  const live = age < LIVE_MS;
  const stale = !live && age < 30_000;

  const events = useMemo(() => [...eventRows].sort((a, b) => (a.id < b.id ? -1 : 1)).map(describeEvent), [eventRows]);
  const completedAt = useMemo(() => {
    const map = new Map<string, number>();
    for (const e of eventRows) if (e.kind === "step_completed") map.set(e.objectId, Number(e.elapsedMs) / 1000);
    return map;
  }, [eventRows]);
  const captions = useMemo(() => [...captionRows].sort((a, b) => (a.id < b.id ? -1 : 1)), [captionRows]);
  const anatomy = useMemo(() => parseAnatomy(session?.anatomy ?? ""), [session?.anatomy]);
  const ports = useMemo(() => parsePorts(frame?.ports ?? ""), [frame?.ports]);
  const findings = useMemo(() => parseFindings(session?.findings ?? ""), [session?.findings]);
  const elapsed = frame?.elapsedS ?? session?.elapsedS ?? 0;
  const steps = buildSteps(session, events, completedAt, elapsed);
  const stepIndex = frame?.stepIndex ?? 0;
  const cbd = live && frame?.contact === "cbd";
  const lastMajor = [...events].reverse().find((e) => e.severity === "major");
  const recentMajor = lastMajor && elapsed - lastMajor.t < 6;

  const score = session && session.score >= 0 ? session.score : null;
  const band = scoreBand(score ?? 0, session?.major ?? 0);

  const overlay = frame && (
    <div className="lv-hud">
      <div className="lv-hud-top">
        <span className="lv-chip lv-chip-dark">
          <span className="lv-hud-step">{Math.min(stepIndex + 1, STEPS.length)}/{STEPS.length}</span>
          {session?.state === "Completed" ? "Procedure complete" : frame.stepTitle}
        </span>
        {cbd && <span className="lv-chip lv-chip-danger"><Icon name="octagon" size={14} />Jaws on the common bile duct</span>}
        {!cbd && recentMajor && <span className="lv-chip lv-chip-danger"><Icon name="octagon" size={14} />{lastMajor.title}</span>}
        {!cbd && !recentMajor && frame.contact && frame.contact !== "veil" && <span className="lv-chip lv-chip-dark">Jaws on {structureName(frame.contact).toLowerCase()}</span>}
      </div>
      <div className="lv-hud-bottom">
        {frame.entryActive && frame.toolInHand && (
          <span className="lv-chip lv-chip-dark lv-mono">
            {frame.entryInserted ? `depth ${frame.entryDepth.toFixed(0)} mm` : `${frame.entryMm >= 0 ? frame.entryMm.toFixed(0) : "—"} mm · ${frame.entryAngle >= 0 ? frame.entryAngle.toFixed(0) : "—"}°`}
          </span>
        )}
        {!frame.entryActive && frame.tipValid && frame.targetMm >= 0 && stepIndex >= 6 && (
          <span className="lv-chip lv-chip-dark lv-mono">{frame.targetMm.toFixed(0)} mm to target</span>
        )}
        {frame.stepId === "insufflate" && <span className="lv-chip lv-chip-dark lv-mono">{frame.pressure.toFixed(1)} mmHg</span>}
      </div>
    </div>
  );

  return (
    <div className="lv-shell">
      <header className="lv-top">
        <div className="lv-brand">
          <span className="lv-logo" aria-hidden="true" />
          <div>
            <strong>Jeremy.ai</strong>
            <span>Live instructor</span>
          </div>
        </div>

        <div className="lv-top-session">
          <span className={`lv-live ${live ? "lv-live-on" : stale ? "lv-live-stale" : ""}`}>
            <span className="lv-dot" />{live ? "LIVE" : stale ? "STALE" : session ? "OFFLINE" : "WAITING"}
          </span>
          <label className="lv-select">
            <span className="lv-visually-hidden">Session</span>
            <select value={pinned ?? ""} onChange={(e) => setPinned(e.currentTarget.value || null)}>
              <option value="">Follow latest{sorted[0] ? ` (${sorted[0].trainee})` : ""}</option>
              {sorted.map((s) => (
                <option key={s.sessionId} value={s.sessionId}>
                  {s.trainee} · {s.device} · {new Date(Number(s.startedAt)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })} · {s.state}
                </option>
              ))}
            </select>
          </label>
          {session && (
            <div className="lv-top-stats">
              <span className="lv-stat"><small>Trainee</small>{session.trainee}</span>
              <span className="lv-stat"><small>State</small>{session.state}</span>
              <span className="lv-stat lv-mono"><small>Elapsed</small>{formatClock(elapsed)}</span>
              <span className="lv-stat lv-mono"><small>Steps</small>{session.stepsDone}/{session.stepCount}</span>
              <span className={`lv-stat lv-score lv-score-${band}`}><small>Score</small>{score !== null ? score.toFixed(0) : "—"}{session.major > 0 && <em title="Capped at 40 by a major error">cap</em>}</span>
              <span className={`lv-stat ${session.major ? "lv-stat-bad" : ""}`}><small>Major</small>{session.major}</span>
              <span className={`lv-stat ${session.minor ? "lv-stat-warn" : ""}`}><small>Minor</small>{session.minor}</span>
            </div>
          )}
        </div>

        <div className="lv-top-right">
          <span className="lv-conn" title={connectionError ? connectionError.message : isActive ? "Connected to the local SpacetimeDB" : "Connecting"}>
            <span className={`lv-dot ${connectionError ? "lv-dot-bad" : isActive ? "lv-dot-good" : ""}`} />
            {connectionError ? "DB offline" : isActive ? `DB${live ? ` · ${hz.toFixed(0)} Hz` : ""}` : "Connecting"}
          </span>
          {session && (
            <button type="button" className="lv-icon-btn" title="Delete this session's live data"
              onClick={() => { if (window.confirm(`Delete the live data for ${session.trainee}?`)) void liveClear({ sessionId: session.sessionId }); }}>
              <Icon name="trash" />
            </button>
          )}
          {nav}
        </div>
      </header>

      <main className="lv-grid">
        <div className="lv-col-main">
          <CastView source={cast} onSource={setCast} danger={Boolean(cbd || recentMajor)} overlay={overlay} caption={live ? frame?.caption ?? "" : ""} />
          <StepTimeline steps={steps} />
        </div>
        <div className="lv-col-side">
          <AbdomenMap frame={frame} anatomy={anatomy} ports={ports} trail={trail} stepIndex={stepIndex} />
          <Metrics frame={frame} session={session} distanceHistory={history} />
        </div>
        <div className="lv-col-feeds">
          <EventFeed events={events} findings={findings} />
          <CoachFeed captions={captions} coachStatus={live ? frame?.coachStatus ?? "" : ""} listening={Boolean(frame?.listening)} micLevel={live ? frame?.micLevel ?? 0 : 0}
            onNote={(text) => pushCaption({ sessionId, speaker: "instructor", text, elapsedMs: BigInt(Math.round(elapsed * 1000)), stepIndex })} />
        </div>
      </main>
      {!session && isActive && (
        <div className="lv-waiting">
          <h2>Waiting for the headset</h2>
          <p>Start the lap chole on the Quest (with <code>adb reverse tcp:3000 tcp:3000</code> over USB), or run the demo publisher:</p>
          <code>node scripts/fake-headset.mjs --loop</code>
        </div>
      )}
    </div>
  );
}
