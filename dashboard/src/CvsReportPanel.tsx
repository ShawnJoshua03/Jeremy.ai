import { useEffect, useState } from "react";
import { api } from "./api";
import { exerciseLabel } from "./exercise";
import type { MistakeCostLine, SessionReport } from "./types";

interface CvsReportPanelProps {
  sessionId: string;
  isDemo: boolean;
  demoReport?: SessionReport;
}

interface CostReport {
  total_usd: number;
  lines: MistakeCostLine[];
}

interface ProviderCard {
  id: string;
  name: string;
  role: string;
  status: string;
  variant: "guide" | "claude" | "cartesia" | "apify" | "convex" | "generic";
  tag?: string;
  note?: string;
  active?: boolean;
}

const providerRoles: Record<string, { name: string; role: string; variant: ProviderCard["variant"] }> = {
  claude: { name: "Claude", role: "Writes the review", variant: "claude" },
  cartesia: { name: "Cartesia", role: "Speaks the review", variant: "cartesia" },
  apify: { name: "Apify", role: "Sources supply prices", variant: "apify" },
  convex: { name: "Convex", role: "Instructor live sync", variant: "convex" },
};

const providerDefaults: Record<string, string> = {
  cartesia: "unavailable",
  apify: "snapshot",
  convex: "off",
};

function providerStatusLabel(provider: string, status: string) {
  if (provider === "cartesia" && status === "unavailable") return "offline";
  return status.replaceAll("_", " ");
}

function ProviderAvatar({ provider }: { provider: ProviderCard }) {
  return (
    <article className="provider-card">
      <div className={`provider-avatar ${provider.active ? "provider-active" : ""}`} aria-hidden="true">
        <svg viewBox="0 0 48 48">
          {provider.variant === "guide" && (
            <>
              <rect x="11" y="13" width="26" height="23" rx="8" />
              <path d="M24 7v6M18 24h.1M30 24h.1M18 30c3.5 2 8.5 2 12 0M8 22v9m32-9v9" />
            </>
          )}
          {provider.variant === "claude" && (
            <>
              <path d="M10 12h28v20H23l-8 6v-6h-5z" />
              <path d="m23 17 2.2 4.8L30 24l-4.8 2.2L23 31l-2.2-4.8L16 24l4.8-2.2z" />
            </>
          )}
          {provider.variant === "cartesia" && (
            <>
              <circle cx="17" cy="24" r="3" />
              <path d="M23 18v12m6-17v22m6-16v10" />
            </>
          )}
          {provider.variant === "apify" && (
            <>
              <path d="m10 17 14-8 14 8v15l-14 8-14-8zM10 17l14 8 14-8M24 25v15" />
              <path d="M19 20h.1M29 20h.1" />
            </>
          )}
          {provider.variant === "convex" && (
            <>
              <circle cx="14" cy="16" r="4" />
              <circle cx="34" cy="16" r="4" />
              <circle cx="24" cy="33" r="4" />
              <path d="m18 18 4 11m8-11-4 11m-8-13h12" />
            </>
          )}
          {provider.variant === "generic" && (
            <>
              <circle cx="24" cy="24" r="14" />
              <circle cx="19" cy="22" r="1" />
              <circle cx="29" cy="22" r="1" />
              <path d="M18 29c3 3 9 3 12 0" />
            </>
          )}
        </svg>
      </div>
      <div className="provider-card-copy">
        <div className="provider-card-title">
          <strong>{provider.name}</strong>
          <span className={`provider-status provider-status-${provider.status.replaceAll(/[^a-z0-9]+/gi, "-")}`}>
            {providerStatusLabel(provider.id, provider.status)}
          </span>
        </div>
        <p>{provider.role}</p>
        {provider.tag && <span className="provider-tag">{provider.tag}</span>}
        {provider.note && <span className="provider-note">{provider.note}</span>}
      </div>
    </article>
  );
}

function ProviderCrew({ report }: { report: SessionReport }) {
  const statuses = {
    ...providerDefaults,
    claude: report.source === "claude" ? "used" : "fallback",
    ...report.providers,
  };
  const events = report.provider_events ?? [];
  const lines = report.mistake_cost.lines ?? [];
  const sourced = lines.filter((line) => !line.assumed).length;
  const assumed = lines.filter((line) => line.assumed).length;
  const cards: ProviderCard[] = [
    {
      id: "robot-guide",
      name: "Robot guide",
      role: "Session review",
      status: "report ready",
      variant: "guide",
    },
    ...Object.entries(statuses).map(([id, status]): ProviderCard => {
      const definition = providerRoles[id];
      const activeStatus = ["used", "live", "mirrored"].includes(status);
      return {
        id,
        name: definition?.name ?? id.replaceAll("_", " "),
        role: definition?.role ?? "Session provider",
        status,
        variant: definition?.variant ?? "generic",
        active: activeStatus && events.some((event) => event.provider === id),
        ...(id === "apify" ? { note: `${sourced} sourced · ${assumed} assumed prices` } : {}),
      };
    }),
  ];
  return (
    <section className="report-section provider-crew-section" aria-labelledby="provider-crew-heading">
      <header className="report-section-heading">
        <div>
          <h3 id="provider-crew-heading">Report crew</h3>
          <p className="panel-subtitle">Service activity for this session</p>
        </div>
      </header>
      <div className="provider-crew">
        {cards.map((provider) => <ProviderAvatar key={provider.id} provider={provider} />)}
      </div>
    </section>
  );
}

function amountLabel(value: number | null | undefined, suffix = "") {
  return value == null || !Number.isFinite(value)
    ? "—"
    : `${value.toFixed(1)}${suffix}`;
}

function currency(value: number | null | undefined) {
  return value == null || !Number.isFinite(value) ? "—" : `$${value.toFixed(2)}`;
}

function readableId(value: string | null | undefined) {
  return value ? value.replaceAll("_", " ") : "—";
}

function reportSourceLabel(source: string) {
  if (source === "claude") return "Claude";
  if (source === "fallback") return "Fallback";
  return source.replaceAll("_", " ") || "Unknown source";
}

function ScoreRing({ score }: { score: number }) {
  const radius = 39;
  const circumference = 2 * Math.PI * radius;
  const normalized = Math.max(0, Math.min(100, score));
  return (
    <div className="report-score">
      <svg viewBox="0 0 100 100" role="img" aria-label={`Score ${score} out of 100`}>
        <circle className="score-ring-track" cx="50" cy="50" r={radius} />
        <circle
          className="score-ring-value"
          cx="50"
          cy="50"
          r={radius}
          strokeDasharray={`${(normalized / 100) * circumference} ${circumference}`}
          transform="rotate(-90 50 50)"
        />
      </svg>
      <span className="report-score-value num">{Math.round(score)}</span>
      <span className="report-score-unit">/ 100</span>
    </div>
  );
}

function ExerciseCard({ exercise }: { exercise: SessionReport["exercises"][number] }) {
  const result =
    exercise.correct == null
      ? "Not assessed"
      : exercise.correct
        ? "Correct"
        : "Needs review";
  const dwellEntries = Object.entries(exercise.hover_dwell_s ?? {});
  return (
    <article className="report-exercise">
      <div className="report-exercise-heading">
        <div>
          <h3>{exercise.title || readableId(exercise.exercise_id)}</h3>
          <p className="report-exercise-id">{exercise.exercise_id}</p>
        </div>
        <span className={`report-result ${exercise.correct === true ? "is-correct" : exercise.correct === false ? "is-incorrect" : ""}`}>
          {exercise.unclear ? "Unclear" : result}
        </span>
      </div>
      <dl className="report-decision">
        <div><dt>Decision</dt><dd>{readableId(exercise.decision)}</dd></div>
        <div><dt>Expected</dt><dd>{readableId(exercise.expected)}</dd></div>
        <div><dt>Time to decision</dt><dd>{amountLabel(exercise.time_to_decision_s, " s")}</dd></div>
      </dl>
      <div className="report-inspected">
        <span className="report-label">Structures inspected</span>
        {exercise.structures_inspected.length > 0 ? (
          <ol>
            {exercise.structures_inspected.map((structure, index) => (
              <li key={`${structure}-${index}`}>{readableId(structure)}</li>
            ))}
          </ol>
        ) : (
          <p className="report-muted">No structure inspections recorded.</p>
        )}
      </div>
      {dwellEntries.length > 0 && (
        <p className="report-dwell">
          Hover dwell: {dwellEntries
            .map(([id, seconds]) => `${readableId(id)} ${seconds.toFixed(1)} s`)
            .join(" · ")}
        </p>
      )}
      <p className="report-review">{exercise.review}</p>
      <p className="report-tip"><strong>Next step:</strong> {exercise.tip}</p>
    </article>
  );
}

export function CvsReportPanel({ sessionId, isDemo, demoReport }: CvsReportPanelProps) {
  const [report, setReport] = useState<SessionReport>();
  const [costTotal, setCostTotal] = useState<number>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [costError, setCostError] = useState("");
  const [costReport, setCostReport] = useState<CostReport>();
  const [costLoading, setCostLoading] = useState(false);
  const [animatedCost, setAnimatedCost] = useState(0);
  const [revealedCostLines, setRevealedCostLines] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setReport(undefined);
    setCostTotal(undefined);
    setError("");
    setCostError("");
    setCostReport(undefined);
    if (isDemo) {
      setReport(demoReport);
      setCostTotal(demoReport?.mistake_cost.total_usd);
      setError(demoReport ? "" : "No report is available for this preview session.");
      setLoading(false);
      return () => controller.abort();
    }

    setLoading(true);
    void Promise.allSettled([
      api.getReport(sessionId, controller.signal),
      api.getMistakeCostSummary(sessionId, controller.signal),
    ]).then(([reportResult, costResult]) => {
      if (controller.signal.aborted) return;
      if (reportResult.status === "fulfilled") {
        setReport(reportResult.value);
        setError("");
      } else {
        setError(reportResult.reason instanceof Error ? reportResult.reason.message : "The session report could not be loaded.");
      }
      if (costResult.status === "fulfilled") {
        setCostTotal(costResult.value.total_usd);
        setCostError("");
      } else {
        setCostError(costResult.reason instanceof Error ? costResult.reason.message : "The free cost summary could not be loaded.");
      }
      setLoading(false);
    });
    return () => controller.abort();
  }, [demoReport, isDemo, sessionId]);

  useEffect(() => {
    if (!report) return;
    const costLines = report.mistake_cost.lines ?? [];
    const target = costTotal ?? report.mistake_cost.total_usd;
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    setAnimatedCost(0);
    setRevealedCostLines(0);
    if (reducedMotion || !Number.isFinite(target)) {
      setAnimatedCost(Number.isFinite(target) ? target : 0);
      setRevealedCostLines(costLines.length);
      return;
    }

    const lineSum = costLines.reduce((sum, line) => sum + Math.abs(line.usd), 0);
    let cumulative = 0;
    const revealAt = costLines.map((line, index) => {
      cumulative += Math.abs(line.usd);
      return lineSum > 0
        ? (cumulative / lineSum) * 0.82
        : ((index + 1) / (costLines.length + 1)) * 0.82;
    });
    const durationMs = Math.max(900, Math.min(2200, costLines.length * 360 + 500));
    let startedAt: number | undefined;
    let frame = 0;
    const animate = (now: number) => {
      startedAt ??= now;
      const progress = Math.min(1, (now - startedAt) / durationMs);
      const eased = 1 - Math.pow(1 - progress, 3);
      setAnimatedCost(target * eased);
      setRevealedCostLines(revealAt.filter((threshold) => progress >= threshold).length);
      if (progress < 1) frame = window.requestAnimationFrame(animate);
      else {
        setAnimatedCost(target);
        setRevealedCostLines(costLines.length);
      }
    };
    frame = window.requestAnimationFrame(animate);
    return () => window.cancelAnimationFrame(frame);
  }, [costTotal, report]);

  async function loadItemizedReport() {
    setCostLoading(true);
    setCostError("");
    setCostReport(undefined);
    try {
      const result = await api.getMistakeCostReport(sessionId);
      setCostReport(result);
    } catch (reason) {
      setCostError(reason instanceof Error ? reason.message : "The itemized report could not be loaded.");
    } finally {
      setCostLoading(false);
    }
  }

  return (
    <section className="panel cvs-report-panel" aria-labelledby="cvs-report-heading">
      <header className="panel-heading">
        <div>
          <h2 id="cvs-report-heading">Session report</h2>
          <p className="panel-subtitle">
            {report?.trainee_id ?? "Selected trainee"} · {report ? exerciseLabel(report.exercise) : "Session review"}
          </p>
        </div>
        {isDemo && <span className="source-badge source-preview">Synthetic preview</span>}
      </header>

      {loading ? (
        <p className="report-state" role="status">Loading session report…</p>
      ) : error ? (
        <p className="report-state report-error" role="alert">{error}</p>
      ) : report ? (
        <>
          <div className="report-overview">
            <ScoreRing score={report.score} />
            <div className="report-overview-copy">
              <div className="report-grade-row">
                <h3>{report.grade.replaceAll("_", " ")}</h3>
                <span className={`source-badge ${report.source === "claude" ? "source-claude" : report.source === "fallback" ? "source-fallback" : ""}`}>
                  {reportSourceLabel(report.source)}
                </span>
              </div>
              <p>{report.summary}</p>
            </div>
          </div>
          <ProviderCrew report={report} />

          <section className="report-section" aria-labelledby="report-exercises-heading">
            <header className="report-section-heading">
              <h3 id="report-exercises-heading">Exercise review</h3>
              <span>{report.exercises.length} exercise{report.exercises.length === 1 ? "" : "s"}</span>
            </header>
            {report.exercises.length > 0 ? (
              <div className="report-exercises">
                {report.exercises.map((exercise, index) => (
                  <ExerciseCard key={`${exercise.exercise_id}-${index}`} exercise={exercise} />
                ))}
              </div>
            ) : (
              <p className="report-muted">No exercise results were recorded.</p>
            )}
          </section>

          <section className="report-section" aria-labelledby="report-movement-heading">
            <header className="report-section-heading">
              <h3 id="report-movement-heading">Movement</h3>
            </header>
            <dl className="report-movement">
              <div><dt>Hand path</dt><dd className="num">{amountLabel(report.movement.hand_path_m, " m")}</dd></div>
              <div><dt>Head path</dt><dd className="num">{amountLabel(report.movement.head_path_m, " m")}</dd></div>
              <div><dt>Smoothness</dt><dd className="num">{amountLabel(report.movement.smoothness * 100, "%")}</dd></div>
              <div><dt>Idle time</dt><dd className="num">{amountLabel(report.movement.idle_s, " s")}</dd></div>
              <div><dt>Samples</dt><dd className="num">{report.movement.samples}</dd></div>
            </dl>
          </section>

          <div className="report-focus-grid">
            <section className="report-section">
              <header className="report-section-heading"><h3>Strengths</h3></header>
              {report.strengths.length > 0 ? (
                <ul>{report.strengths.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul>
              ) : <p className="report-muted">No strengths were recorded.</p>}
            </section>
            <section className="report-section">
              <header className="report-section-heading"><h3>Focus next</h3></header>
              {report.focus_next.length > 0 ? (
                <ul>{report.focus_next.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul>
              ) : <p className="report-muted">No follow-up focus was recorded.</p>}
            </section>
          </div>

          <section className="report-section cost-section" aria-labelledby="mistake-cost-heading">
            <header className="report-section-heading">
              <div>
                <h3 id="mistake-cost-heading">Mistake cost</h3>
                <p className="panel-subtitle">Illustrative estimate, not billing data.</p>
              </div>
              <strong className="cost-total num">{currency(animatedCost)}</strong>
            </header>
            {costError && <p className="report-error" role="alert">{costError}</p>}
            {isDemo ? (
              <p className="report-muted">Itemized reports are disabled for synthetic preview sessions.</p>
            ) : (
              <button
                type="button"
                className="button button-secondary"
                onClick={() => void loadItemizedReport()}
                disabled={costLoading}
              >
                {costLoading ? "Loading itemized report…" : "Show itemized report"}
              </button>
            )}
            {report.mistake_cost.lines.length > 0 ? (
              <div className="report-cost-lines" aria-label="Illustrative cost estimate lines">
                {report.mistake_cost.lines.slice(0, revealedCostLines).map((line, index) => (
                  <div className="cost-line cost-line-enter" key={`${line.label}-${index}`}>
                    <span>
                      {line.label}{line.source ? ` · ${line.source}` : ""}{line.assumed ? " · assumed" : ""}
                    </span>
                    <strong className="num">{currency(line.usd)}</strong>
                  </div>
                ))}
              </div>
            ) : (
              <p className="report-muted">No illustrative cost lines were recorded for this session.</p>
            )}
            {costReport && (
              <div className="itemized-cost-report" role="status">
                <h4>Itemized estimate</h4>
                {costReport.lines.length > 0 ? costReport.lines.map((line, index) => (
                  <div className="cost-line" key={`${line.label}-${index}`}>
                    <span>{line.label}{line.source ? ` · ${line.source}` : ""}{line.assumed ? " · assumed" : ""}</span>
                    <strong className="num">{currency(line.usd)}</strong>
                  </div>
                )) : <p className="report-muted">No itemized cost lines were returned.</p>}
              </div>
            )}
          </section>
          <p className="report-disclaimer">{report.disclaimer}</p>
        </>
      ) : null}
    </section>
  );
}
