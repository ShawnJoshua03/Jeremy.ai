// Local live instructor view: the Quest casting window plus live sim
// telemetry from the local SpacetimeDB (liveSession / liveFrame / liveEvent /
// liveCaption). Deliberately quiet: numbers refresh a few times a second, no
// animation, append-only logs, red only for a real safety alert.
import { useEffect, useMemo, useRef, useState } from "react";
import { useReducer, useSpacetimeDB, useTable } from "spacetimedb/react";
import { reducers, tables } from "../module_bindings";
import { AbdomenMap } from "./AbdomenMap";
import { CastView, type CastSource } from "./CastView";
import { RelaySender, RelayViewer, useRelayAvailable } from "./CastRelay";
import { Log } from "./Feeds";
import { Metrics } from "./Metrics";
import { StepTimeline } from "./StepTimeline";
import {
  STEPS, buildSteps, describeEvent, formatClock, parseAnatomy, parsePorts,
  type LiveSession, type Vec3,
} from "./model";
import "./live.css";

const LIVE_MS = 2500; // frame age that still counts as live
const TRAIL = 120;
const DISPLAY_MS = 333; // numbers and text refresh at ~3 Hz

function useNow(ms: number) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), ms);
    return () => window.clearInterval(id);
  }, [ms]);
  return now;
}

/** The latest value, but re-rendered at most every `ms`. */
function useThrottled<T>(value: T, ms: number): T {
  const [shown, setShown] = useState(value);
  const latest = useRef(value);
  latest.current = value;
  useEffect(() => {
    const id = window.setInterval(() => setShown(latest.current), ms);
    return () => window.clearInterval(id);
  }, [ms]);
  // Show the first value immediately (and after a session switch to nothing).
  useEffect(() => {
    if (shown === undefined || value === undefined) setShown(value);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value === undefined]);
  return shown;
}

const STALE_MS = 10_000;
const byStart = (a: LiveSession, b: LiveSession) => (a.startedAt > b.startedAt ? -1 : a.startedAt < b.startedAt ? 1 : 0);
const rank = (s: LiveSession) => (s.device === "quest" ? 1 : 0);
/** Negative when a should be followed over b. */
const better = (a: LiveSession, b: LiveSession) => rank(b) - rank(a) || byStart(a, b);

export function LiveApp({ nav }: { nav: React.ReactNode }) {
  const { isActive, connectionError, identity } = useSpacetimeDB();
  const now = useNow(1000);
  const [sessions] = useTable(tables.liveSession);
  const [pinned, setPinned] = useState<string | null>(null);
  const sorted = useMemo(() => [...sessions].sort(byStart), [sessions]);
  // "Latest session" locks onto one session. It moves only when a better one
  // starts: a headset beats an editor/sim run, then the newer start wins.
  // Sessions silent for over 10 s are not candidates. Frame updates never switch it.
  const [followed, setFollowed] = useState("");
  useEffect(() => {
    const current = sessions.find((s) => s.sessionId === followed);
    const live = sessions.filter((s) => now - Number(s.lastSeen) < STALE_MS);
    const best = [...live].sort(better)[0] ?? (current ? undefined : sorted[0]);
    if (!best || best.sessionId === followed) return;
    if (!current || better(best, current) < 0) setFollowed(best.sessionId);
  }, [sessions, sorted, followed, now]);
  const sessionId = pinned && sorted.some((s) => s.sessionId === pinned) ? pinned : followed;
  const rawSession = sorted.find((s) => s.sessionId === sessionId);

  const frameQuery = useMemo(() => tables.liveFrame.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const eventQuery = useMemo(() => tables.liveEvent.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const captionQuery = useMemo(() => tables.liveCaption.where((r) => r.sessionId.eq(sessionId)), [sessionId]);
  const [frames] = useTable(frameQuery);
  const [eventRows] = useTable(eventQuery);
  const [captionRows] = useTable(captionQuery);
  const rawFrame = frames[0];
  const pushCaption = useReducer(reducers.pushCaption);

  // Numbers and text: throttled. The map uses the raw frame (positions only).
  const frame = useThrottled(rawFrame, DISPLAY_MS);
  const session = useThrottled(rawSession, DISPLAY_MS);

  const [cast, setCast] = useState<CastSource | null>(null);
  const [relayOn, setRelayOn] = useState(true);
  const [relayStatus, setRelayStatus] = useState("");
  const [remoteStream, setRemoteStream] = useState<MediaStream | null>(null);
  const relayAvailable = useRelayAvailable(identity?.toHexString());
  const viewing = !cast && relayAvailable && Boolean(identity);
  useEffect(() => {
    if (!viewing) setRemoteStream(null);
  }, [viewing]);
  const shownSource: CastSource | null = cast ?? (viewing && remoteStream ? { kind: "remote", label: "Relayed", stream: remoteStream } : null);

  // Tip trail (map only), reset per step and per session.
  const [trail, setTrail] = useState<Vec3[]>([]);
  const lastStep = useRef(-1);
  const lastSeq = useRef<bigint>(-1n);
  useEffect(() => {
    setTrail([]); lastSeq.current = -1n; lastStep.current = -1;
  }, [sessionId]);
  useEffect(() => {
    if (!rawFrame || rawFrame.seq === lastSeq.current) return;
    lastSeq.current = rawFrame.seq;
    const newStep = rawFrame.stepIndex !== lastStep.current;
    lastStep.current = rawFrame.stepIndex;
    if (rawFrame.tipValid) {
      const p = { x: rawFrame.tipX, y: rawFrame.tipY, z: rawFrame.tipZ };
      setTrail((prev) => (newStep ? [p] : [...prev.slice(-TRAIL + 1), p]));
    } else if (newStep) setTrail([]);
  }, [rawFrame]);

  const age = rawSession ? now - Number(rawSession.lastSeen) : Infinity;
  const live = age < LIVE_MS;
  const ended = session?.state === "Completed" || session?.state === "Cancelled";

  const events = useMemo(() => [...eventRows].sort((a, b) => (a.id < b.id ? -1 : 1)).map(describeEvent), [eventRows]);
  const completedAt = useMemo(() => {
    const map = new Map<string, number>();
    for (const e of eventRows) if (e.kind === "step_completed") map.set(e.objectId, Number(e.elapsedMs) / 1000);
    return map;
  }, [eventRows]);
  const captions = useMemo(() => [...captionRows].filter((c) => c.speaker !== "sim").sort((a, b) => (a.id < b.id ? -1 : 1)), [captionRows]);
  const anatomy = useMemo(() => parseAnatomy(rawSession?.anatomy ?? ""), [rawSession?.anatomy]);
  const ports = useMemo(() => parsePorts(rawFrame?.ports ?? ""), [rawFrame?.ports]);
  const elapsed = frame?.elapsedS ?? session?.elapsedS ?? 0;
  const steps = buildSteps(session, events, completedAt, elapsed);
  const stepIndex = frame?.stepIndex ?? 0;

  // Safety alert: jaws on the common bile duct, or a major error in the last few seconds.
  const lastMajor = [...events].reverse().find((e) => e.severity === "major");
  const alert = live && frame?.contact === "cbd"
    ? "Jaws on the common bile duct"
    : lastMajor && elapsed - lastMajor.t < 8 ? lastMajor.title : "";

  const status = ended ? (session?.state === "Completed" ? "Complete" : "Ended") : live ? "Live" : session ? "No signal" : "Waiting";
  const score = session && session.score >= 0 ? session.score : null;

  const logLines = useMemo(() => {
    const lines = events.map((e) => ({ key: `e${e.id}`, t: e.t, who: "", text: e.title, tone: e.severity === "major" ? "bad" : e.severity === "minor" ? "warn" : e.severity === "good" && e.title.endsWith("complete") ? "quiet" : "" }));
    return lines;
  }, [events]);
  const coachLines = useMemo(() => captions.map((c) => ({
    key: `c${c.id}`, t: Number(c.elapsedMs) / 1000, who: c.speaker === "instructor" ? "Note" : c.speaker === "trainee" ? "Trainee" : "", text: c.text, tone: c.speaker === "instructor" ? "note" : "",
  })), [captions]);

  return (
    <div className="lv">
      <header className="lv-top">
        <span className="lv-brand">Jeremy.ai <span className="lv-muted">Live</span></span>
        <select className="lv-session" value={pinned ?? ""} onChange={(e) => setPinned(e.currentTarget.value || null)} aria-label="Session">
          <option value="">Follow latest{rawSession && !pinned ? ` · ${rawSession.trainee} (${rawSession.device})` : ""}</option>
          {sorted.map((s) => (
            <option key={s.sessionId} value={s.sessionId}>
              {s.trainee} · {s.device} · {new Date(Number(s.startedAt)).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })} · {s.state}
            </option>
          ))}
        </select>
        <span className="lv-spacer" />
        <span className={`lv-status${live ? " lv-status-live" : ""}`}><i />{status}</span>
        <span className="lv-muted">{connectionError ? "Database offline" : isActive ? "" : "Connecting…"}</span>
        {nav}
      </header>

      <main className="lv-main">
        <div className="lv-left">
          <CastView source={shownSource} onSource={setCast} alert={alert}
            caption={live ? frame?.caption ?? "" : ""}
            relay={cast?.stream ? { on: relayOn, status: relayStatus, toggle: () => { setRelayOn(!relayOn); setRelayStatus(""); } } : undefined}
            remoteNote={viewing ? relayStatus || "Connecting to the shared headset view…" : undefined} />
          {identity && cast?.stream && relayOn && <RelaySender key={cast.stream.id} identity={identity} stream={cast.stream} onStatus={setRelayStatus} />}
          {identity && viewing && <RelayViewer identity={identity} onStream={setRemoteStream} onStatus={setRelayStatus} />}
          <StepTimeline steps={steps} />
          <div className="lv-logs">
            <Log title="Events" lines={logLines} empty="Nothing recorded yet." />
            <Log title="Coach" lines={coachLines} empty="The coach has not spoken yet."
              onNote={(text) => pushCaption({ sessionId, speaker: "instructor", text, elapsedMs: BigInt(Math.round(elapsed * 1000)), stepIndex })} />
          </div>
        </div>

        <aside className="lv-right">
          <section className="lv-summary">
            <div className="lv-step">
              <span className="lv-muted lv-num">{session ? `${Math.min(stepIndex + 1, STEPS.length)}/${STEPS.length}` : "—"}</span>
              <span>{ended ? (session?.state === "Completed" ? "Procedure complete" : "Attempt ended") : frame?.stepTitle || "Waiting for the headset"}</span>
            </div>
            <dl className="lv-facts">
              <div><dt>Time</dt><dd className="lv-num">{formatClock(elapsed)}</dd></div>
              <div><dt>Errors</dt><dd className="lv-num">
                <span className={session?.major ? "lv-bad" : ""}>{session?.major ?? 0} major</span>
                <span className="lv-muted"> · </span>{session?.minor ?? 0} minor</dd></div>
              <div><dt>Score</dt><dd className="lv-num">{score !== null ? score.toFixed(0) : "—"}</dd></div>
            </dl>
            {alert && <p className="lv-alert">{alert}</p>}
            {live && frame?.coachStatus && <p className="lv-read">{frame.coachStatus}</p>}
          </section>
          <Metrics frame={frame} ended={ended} />
          <AbdomenMap frame={rawFrame} anatomy={anatomy} ports={ports} trail={trail} stepIndex={rawFrame?.stepIndex ?? 0} />
          {session && <p className="lv-muted lv-small">{session.trainee} · {session.device}</p>}
        </aside>
      </main>
      {!rawSession && isActive && (
        <p className="lv-waiting lv-muted">
          Waiting for a session. Start the lap chole on the headset, or run <code>node scripts/fake-headset.mjs --loop</code>.
        </p>
      )}
    </div>
  );
}
