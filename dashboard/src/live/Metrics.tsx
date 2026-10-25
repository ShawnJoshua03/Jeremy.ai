// Contextual live metrics: what matters for the current step.
import type { Finding, LiveFrame, LiveSession } from "./model";
import { ENTRY_STEPS, formatClock, scoreBand, structureName } from "./model";

export interface Summary { ports: number; portMm: number | null; portDeg: number | null; pressure: number | null }
import { Icon } from "./icons";

interface MetricsProps {
  frame: LiveFrame | undefined;
  session: LiveSession | undefined;
  distanceHistory: readonly number[];
  summary: Summary;
  findings: readonly Finding[];
}

export function Metrics({ frame, session, distanceHistory, summary, findings }: MetricsProps) {
  const stepId = frame?.stepId ?? "";
  const complete = session?.state === "Completed";
  return (
    <section className="lv-card lv-metrics" aria-label="Live metrics">
      <header className="lv-card-head">
        <div>
          <h2>{complete ? "Attempt complete" : frame?.stepTitle || "Live metrics"}</h2>
          <p className="lv-sub">{complete ? "Final readings" : frameHint(frame)}</p>
        </div>
      </header>
      {!frame ? (
        <p className="lv-empty">No telemetry yet.</p>
      ) : complete ? (
        <CompleteTiles frame={frame} session={session!} summary={summary} findings={findings} />
      ) : ENTRY_STEPS.has(stepId) && frame.entryActive ? (
        <EntryTiles f={frame} />
      ) : stepId === "insufflate" ? (
        <PressureTile pressure={frame.pressure} />
      ) : (
        <InsideTiles f={frame} history={distanceHistory} />
      )}
    </section>
  );
}

function frameHint(f: LiveFrame | undefined) {
  if (!f) return "";
  if (f.entryActive) return f.toolInHand ? f.entryTool : `Picking up the ${f.entryTool}`;
  if (f.stepIndex >= 6) return "Working inside the abdomen";
  return "";
}

function Bullet({ label, value, unit, tol, max, invert = false, decimals = 0 }: { label: string; value: number; unit: string; tol: number; max: number; invert?: boolean; decimals?: number }) {
  const known = value >= 0;
  const ok = known && value <= tol;
  const pct = known ? Math.min(100, (value / max) * 100) : 0;
  const tolPct = Math.min(100, (tol / max) * 100);
  return (
    <div className={`lv-tile lv-bullet ${!known ? "" : ok ? "lv-ok" : "lv-off"}`}>
      <div className="lv-tile-label">{label}</div>
      <div className="lv-tile-value">
        {known ? value.toFixed(decimals) : "—"}<small>{unit}</small>
        {known && <span className={`lv-tag ${ok ? "lv-tag-good" : "lv-tag-warn"}`}><Icon name={ok ? "check" : "alert"} size={11} />{ok ? "in tolerance" : invert ? "too low" : "outside"}</span>}
      </div>
      <div className="lv-track">
        <span className="lv-track-ok" style={{ width: `${tolPct}%` }} />
        <span className="lv-track-mark" style={{ left: `${pct}%` }} />
      </div>
      <div className="lv-track-scale"><span>0</span><span style={{ left: `${tolPct}%` }}>{tol}{unit}</span><span>{max}{unit}</span></div>
    </div>
  );
}

function EntryTiles({ f }: { f: LiveFrame }) {
  const depthMax = Math.max(f.entryDanger * 1.15, 10);
  const pct = (mm: number) => `${Math.max(0, Math.min(100, (mm / depthMax) * 100))}%`;
  const depth = f.entryInserted ? f.entryDepth : 0;
  const zone = depth > f.entryDanger ? "bad" : depth > f.entryMaxDepth ? "warn" : depth >= f.entryMinDepth ? "good" : "idle";
  const status = !f.toolInHand ? "Picking up the instrument" : f.entryBlocked ? "Blocked by the skin: re-aim" : f.entryInserted ? (zone === "good" ? "In at target depth: hold still" : zone === "warn" ? "Past target depth" : zone === "bad" ? "Too deep" : "Inserting") : f.entryLined ? "On the mark and lined up" : f.entryHovering ? `Aiming${f.entryToward ? `: move ${f.entryToward}` : ""}` : "Approaching";
  const tone = f.entryBlocked || zone === "bad" ? "bad" : zone === "warn" ? "warn" : f.entryLined || zone === "good" ? "good" : "idle";
  return (
    <div className="lv-tiles">
      <div className={`lv-status lv-status-${tone}`}><span className="lv-dot" />{status}</div>
      <Bullet label="Offset from mark" value={f.entryMm} unit=" mm" tol={f.entryTolMm} max={Math.max(45, f.entryTolMm * 3)} />
      <Bullet label="Shaft angle" value={f.entryAngle} unit="°" tol={f.entryTolDeg} max={Math.max(45, f.entryTolDeg * 1.5)} />
      <div className={`lv-tile lv-depth lv-depth-${zone}`}>
        <div className="lv-tile-label">Depth</div>
        <div className="lv-tile-value">{f.entryInserted ? depth.toFixed(0) : "—"}<small> mm</small>
          <span className="lv-tag lv-tag-quiet">pops {f.entryPops}/{f.entryPopB > 0 ? 2 : 1}</span>
        </div>
        <div className="lv-depth-bar">
          <span className="lv-depth-target" style={{ left: pct(f.entryMinDepth), width: `calc(${pct(f.entryMaxDepth)} - ${pct(f.entryMinDepth)})` }} />
          <span className="lv-depth-danger" style={{ left: pct(f.entryDanger) }} />
          {f.entryPopA > 0 && <span className={`lv-depth-pop${f.entryPops >= 1 ? " lv-popped" : ""}`} style={{ left: pct(f.entryPopA) }} title="Fascia" />}
          {f.entryPopB > 0 && <span className={`lv-depth-pop${f.entryPops >= 2 ? " lv-popped" : ""}`} style={{ left: pct(f.entryPopB) }} title="Peritoneum" />}
          <span className="lv-depth-fill" style={{ width: pct(depth) }} />
        </div>
        <div className="lv-track-scale">
          <span>skin</span>
          <span style={{ left: pct((f.entryMinDepth + f.entryMaxDepth) / 2) }}>{f.entryMinDepth}–{f.entryMaxDepth} mm</span>
          <span>{f.entryDanger} mm danger</span>
        </div>
      </div>
    </div>
  );
}

function PressureTile({ pressure }: { pressure: number }) {
  const max = 20;
  const ok = pressure >= 12 && pressure <= 15;
  const high = pressure > 15;
  // Semicircle gauge.
  const angle = (v: number) => Math.PI * (1 - Math.min(max, Math.max(0, v)) / max);
  const pt = (v: number, r: number) => `${(100 + r * Math.cos(angle(v))).toFixed(1)},${(100 - r * Math.sin(angle(v))).toFixed(1)}`;
  const arc = (a: number, b: number, r: number) => `M${pt(a, r)} A${r},${r} 0 0 1 ${pt(b, r)}`;
  return (
    <div className="lv-tiles">
      <div className="lv-tile lv-gauge">
        <div className="lv-tile-label">Intra-abdominal pressure</div>
        <svg viewBox="0 0 200 116" role="img" aria-label={`Pressure ${pressure.toFixed(1)} mmHg`}>
          <path d={arc(0, max, 80)} fill="none" stroke="var(--lv-track)" strokeWidth="14" strokeLinecap="round" />
          <path d={arc(12, 15, 80)} fill="none" stroke="var(--lv-good)" strokeOpacity="0.45" strokeWidth="14" />
          <path d={arc(0, Math.max(0.2, pressure), 80)} fill="none" stroke={high ? "var(--lv-warn)" : "var(--lv-accent)"} strokeWidth="5" strokeLinecap="round" />
          <text x="100" y="92" textAnchor="middle" className="lv-gauge-value">{pressure.toFixed(1)}</text>
          <text x="100" y="110" textAnchor="middle" className="lv-map-label">mmHg · target 12–15</text>
        </svg>
        <span className={`lv-tag ${ok ? "lv-tag-good" : high ? "lv-tag-warn" : "lv-tag-quiet"}`}>
          <Icon name={ok ? "check" : high ? "alert" : "info"} size={11} />{ok ? "in range: release" : high ? "too high" : "filling"}
        </span>
      </div>
    </div>
  );
}

function Spark({ values }: { values: readonly number[] }) {
  if (values.length < 2) return null;
  const max = Math.max(30, ...values);
  const w = 220, h = 44;
  const d = values.map((v, i) => `${i ? "L" : "M"}${((i / (values.length - 1)) * w).toFixed(1)},${(h - (Math.min(v, max) / max) * (h - 4) - 2).toFixed(1)}`).join("");
  return (
    <svg className="lv-spark" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden="true">
      <line x1="0" x2={w} y1={h - (5 / max) * (h - 4) - 2} y2={h - (5 / max) * (h - 4) - 2} stroke="var(--lv-good)" strokeOpacity="0.5" strokeDasharray="3 3" />
      <path d={d} fill="none" stroke="var(--lv-accent)" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}

function InsideTiles({ f, history }: { f: LiveFrame; history: readonly number[] }) {
  const expected = f.stepId === "retract" ? "fundus" : f.stepId === "dissect" ? "veil" : f.stepId.includes("artery") ? "cystic_artery" : "cystic_duct";
  const contact = f.contact;
  const cbd = contact === "cbd";
  const right = contact && contact === expected;
  const wrong = contact && !right;
  return (
    <div className="lv-tiles">
      <div className={`lv-tile lv-jaws ${cbd ? "lv-jaws-bad" : right ? "lv-jaws-good" : wrong ? "lv-jaws-warn" : ""}`}>
        <div className="lv-tile-label">Jaws on</div>
        <div className="lv-tile-value lv-tile-value-text">
          {cbd && <Icon name="octagon" size={18} />}
          {contact ? structureName(contact) : "Nothing"}
        </div>
        <div className="lv-faint">{cbd ? "Danger: common bile duct" : right ? "Correct structure" : wrong ? `Wanted: ${structureName(expected)}` : `Target: ${structureName(expected)}`}</div>
      </div>
      <div className="lv-tile">
        <div className="lv-tile-label">Tip to target</div>
        <div className="lv-tile-value">{f.tipValid && f.targetMm >= 0 ? f.targetMm.toFixed(0) : "—"}<small> mm</small></div>
        <Spark values={history} />
      </div>
      {f.stepId === "dissect" && <Progress label="Peritoneum cleared" value={1 - f.veil} />}
      {f.stepId === "cholangiogram" && <Progress label="Contrast" value={f.contrast} />}
      {f.stepIndex >= 8 && (
        <div className="lv-tile lv-clips">
          <div className="lv-tile-label">Clips</div>
          <ClipRow label="Cystic duct" n={f.ductClips} of={3} />
          <ClipRow label="Cystic artery" n={f.arteryClips} of={3} />
        </div>
      )}
    </div>
  );
}

function Progress({ label, value }: { label: string; value: number }) {
  const pct = Math.round(Math.max(0, Math.min(1, value)) * 100);
  return (
    <div className="lv-tile">
      <div className="lv-tile-label">{label}</div>
      <div className="lv-tile-value">{pct}<small>%</small></div>
      <div className="lv-track"><span className="lv-track-fill" style={{ width: `${pct}%` }} /></div>
    </div>
  );
}

function ClipRow({ label, n, of }: { label: string; n: number; of: number }) {
  return (
    <div className="lv-clip-row">
      <span>{label}</span>
      <span className="lv-clip-dots" aria-label={`${n} of ${of}`}>
        {Array.from({ length: of }, (_, i) => <i key={i} className={i < n ? "lv-on" : ""} />)}
      </span>
    </div>
  );
}

function CompleteTiles({ frame, session, summary, findings }: { frame: LiveFrame; session: LiveSession; summary: Summary; findings: readonly Finding[] }) {
  const score = session.score >= 0 ? session.score : 0;
  const band = scoreBand(score, session.major);
  const r = 42, c = 2 * Math.PI * r;
  return (
    <div className="lv-tiles">
      <div className={`lv-tile lv-summary lv-summary-${band}`}>
        <svg viewBox="0 0 100 100" className="lv-ring" role="img" aria-label={`Score ${score.toFixed(0)} of 100`}>
          <circle cx="50" cy="50" r={r} fill="none" stroke="var(--lv-track)" strokeWidth="8" />
          <circle cx="50" cy="50" r={r} fill="none" stroke="currentColor" strokeWidth="8" strokeLinecap="round"
            strokeDasharray={`${(c * Math.min(100, score)) / 100} ${c}`} transform="rotate(-90 50 50)" />
          <text x="50" y="56" textAnchor="middle" className="lv-ring-value">{score.toFixed(0)}</text>
        </svg>
        <div className="lv-summary-text">
          <div className="lv-tile-label">Practice score</div>
          <strong>{session.major > 0 ? "Capped by a major error" : band === "good" ? "Safe, efficient attempt" : "Completed with findings"}</strong>
          <span className="lv-faint">{formatClock(session.elapsedS)} · {session.stepsDone}/{session.stepCount} steps · {session.major} major · {session.minor} minor</span>
          {findings.length > 0 && <span className="lv-faint">Review: {findings.map((f) => f.text.toLowerCase()).join("; ")}</span>}
        </div>
      </div>
      <div className="lv-tile">
        <div className="lv-tile-label">Port accuracy ({summary.ports})</div>
        <div className="lv-tile-value">{summary.portMm !== null ? summary.portMm.toFixed(0) : "—"}<small> mm</small>
          <small>· {summary.portDeg !== null ? summary.portDeg.toFixed(0) : "—"}°</small></div>
        <div className="lv-faint">mean offset · mean angle</div>
      </div>
      <div className="lv-tile">
        <div className="lv-tile-label">Insufflation</div>
        <div className="lv-tile-value">{(summary.pressure ?? frame.pressure).toFixed(1)}<small> mmHg</small></div>
        <div className="lv-faint">target 12–15</div>
      </div>
      <div className="lv-tile lv-clips">
        <div className="lv-tile-label">Clips placed</div>
        <ClipRow label="Cystic duct" n={frame.ductClips} of={3} />
        <ClipRow label="Cystic artery" n={frame.arteryClips} of={3} />
      </div>
    </div>
  );
}
