import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useQuery } from "convex/react";
import { ApiError, api, getApiBase } from "./api";
import { AttemptsPanel } from "./AttemptsPanel";
import { CoachPanel } from "./CoachPanel";
import { CollaborationPanel } from "./CollaborationPanel";
import { convexApi, type MirroredEvent, type MirroredSession } from "./convexApi";
import { demoCvsReplay, demoSessions } from "./demoData";
import { detailLabel, exerciseLabel, formatDetail, formatMm, isLapCholeCvs, metricLabels } from "./exercise";
import { CvsReplayPanel } from "./CvsReplayPanel";
import { CvsReportPanel } from "./CvsReportPanel";
import { ReplayPanel } from "./ReplayPanel";
import type {
  DemoSession,
  PracticeEvent,
  Sample,
  SessionMetrics,
  SessionSummary,
} from "./types";

interface AppProps {
  convexReady: boolean;
  convexConfigError: string;
  onOpenCall?: () => void;
  startInSample?: boolean;
}

interface MirroredEventsState {
  sessionId: string;
  events: PracticeEvent[];
}

type View = "sessions" | "replay" | "report";

const trackedStorageKey = "jeremy.ai.dashboard.trackedSessions";
const pollIntervalMs = 10_000;

function readTrackedIds(): string[] {
  try {
    const stored = localStorage.getItem(trackedStorageKey);
    const parsed: unknown = stored ? JSON.parse(stored) : [];
    return Array.isArray(parsed)
      ? [
          ...new Set(
            parsed.filter(
              (value): value is string =>
                typeof value === "string" && value.trim().length > 0,
            ),
          ),
        ]
      : [];
  } catch {
    return [];
  }
}

function formatDuration(seconds: number | null | undefined) {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "-";
  const wholeSeconds = Math.max(0, Math.floor(seconds));
  return `${Math.floor(wholeSeconds / 60)}:${String(wholeSeconds % 60).padStart(2, "0")}`;
}

function formatPercent(value: number | undefined, digits = 0) {
  return value === undefined || !Number.isFinite(value)
    ? "-"
    : `${value.toFixed(digits)}%`;
}

function elapsedSeconds(session: SessionSummary["session"]) {
  if (!session) return undefined;
  if (session.elapsed_s !== null) return session.elapsed_s;
  if (session.status !== "running" || !session.started_at) return undefined;
  const start = Date.parse(session.started_at);
  return Number.isFinite(start) ? Math.max(0, (Date.now() - start) / 1000) : undefined;
}

function statusLabel(status: string | undefined) {
  switch (status) {
    case "running":
      return "Running";
    case "ready":
      return "Ready";
    case "completed":
      return "Completed";
    case "cancelled":
      return "Cancelled";
    default:
      return status ?? "Unavailable";
  }
}

function statusClass(status: string | undefined) {
  return status === "running"
    ? "running"
    : status === "completed"
      ? "completed"
      : status === "ready"
        ? "ready"
        : "neutral";
}

function getMetricsFromDemo(session?: DemoSession): SessionMetrics | undefined {
  return session?.metrics;
}

function toSessionSummary(row: MirroredSession): SessionSummary {
  return {
    id: row.sessionId,
    session: {
      id: row.sessionId,
      trainee_id: row.traineeId,
      exercise: row.exercise,
      status: row.status,
      created_at: row.createdAt,
      started_at: row.startedAt,
      completed_at: row.completedAt,
      elapsed_s: row.elapsedS,
    },
    ...(row.metrics
      ? {
          metrics: {
            completion_pct: row.metrics.completionPct,
            successful_transfers: row.metrics.successfulTransfers,
            drops: row.metrics.drops,
            invalid_releases: row.metrics.invalidReleases,
            elapsed_s: row.metrics.elapsedS,
            distance_by_instrument: row.metrics.distanceByInstrument,
            total_distance_m: row.metrics.totalDistanceM,
            path_efficiency: row.metrics.pathEfficiency,
            practice_score: row.metrics.practiceScore,
            details: row.metrics.details ?? null,
          },
        }
      : {}),
  };
}

function toPracticeEvent(row: MirroredEvent): PracticeEvent {
  return {
    event_id: row.eventId,
    kind: row.kind,
    t_ms: row.tMs,
    instrument: row.instrument,
    object_id: row.objectId,
    source: row.source?.length === 3 ? (row.source as [number, number, number]) : null,
    target: row.target?.length === 3 ? (row.target as [number, number, number]) : null,
    value: row.value ?? null,
    detail: row.detail ?? null,
  };
}

function ConvexMirrorBridge({
  sessionId,
  onSessions,
  onEvents,
}: {
  sessionId: string;
  onSessions: (rows: SessionSummary[]) => void;
  onEvents: (rows: MirroredEventsState) => void;
}) {
  const sessions = useQuery(convexApi.sessions.list, {});
  const events = useQuery(
    convexApi.events.list,
    sessionId ? { sessionId } : "skip",
  );

  useEffect(() => {
    if (sessions !== undefined) onSessions(sessions.map(toSessionSummary));
  }, [onSessions, sessions]);

  useEffect(() => {
    if (sessionId && events !== undefined) {
      onEvents({ sessionId, events: events.map(toPracticeEvent) });
    }
  }, [events, onEvents, sessionId]);

  return null;
}

function BrandMark() {
  return (
    <svg className="brand-mark" viewBox="0 0 32 32" role="img" aria-label="Jeremy.ai">
      <path d="M8 7v12.5a5.5 5.5 0 0 0 11 0V7M24 7v16" />
      <circle cx="24" cy="25" r="1.4" />
    </svg>
  );
}

function App({ convexReady, convexConfigError, onOpenCall, startInSample = false }: AppProps) {
  const [trackedIds] = useState(readTrackedIds);
  const [liveSessions, setLiveSessions] = useState<SessionSummary[]>([]);
  const [restListLoaded, setRestListLoaded] = useState(false);
  const [sessionListError, setSessionListError] = useState("");
  const [mirroredSessions, setMirroredSessions] = useState<SessionSummary[]>();
  const [mirroredEvents, setMirroredEvents] = useState<MirroredEventsState | null>(null);
  const [restEvents, setRestEvents] = useState<MirroredEventsState | null>(null);
  const [eventsError, setEventsError] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [demoMode, setDemoMode] = useState(startInSample);
  const [view, setView] = useState<View>("sessions");
  const [menuOpen, setMenuOpen] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [currentTimeMs, setCurrentTimeMs] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [samples, setSamples] = useState<Sample[]>([]);
  const [events, setEvents] = useState<PracticeEvent[]>([]);
  const [samplesLoading, setSamplesLoading] = useState(false);
  const [samplesError, setSamplesError] = useState("");
  const [search, setSearch] = useState("");
  const requestInFlight = useRef(false);
  const lastGoodSessions = useRef(new Map<string, SessionSummary>());

  const refreshSessions = useCallback(async () => {
    if (demoMode || requestInFlight.current) return;
    requestInFlight.current = true;
    setRefreshing(true);
    try {
      let listedSessions: SessionSummary[] = [];
      let listLoaded = false;
      try {
        const rows = await api.getSessions();
        listedSessions = rows.map((session) => ({
          id: session.id,
          session,
          ...(session.metrics ? { metrics: session.metrics } : {}),
        }));
        listLoaded = true;
        setSessionListError("");
      } catch (error) {
        setSessionListError(
          error instanceof Error ? error.message : "Session list could not be loaded.",
        );
      }

      const trackedSessions = await Promise.all(
        trackedIds.map(async (id): Promise<SessionSummary> => {
          try {
            const session = await api.getSession(id);
            try {
              const metrics = await api.getMetrics(id);
              const summary = { id, session, metrics };
              lastGoodSessions.current.set(id, summary);
              return summary;
            } catch (error) {
              if (error instanceof ApiError && error.status === 409) {
                const summary = { id, session };
                lastGoodSessions.current.set(id, summary);
                return summary;
              }
              const previous = lastGoodSessions.current.get(id);
              return {
                id,
                session,
                metrics: previous?.metrics,
                error: error instanceof Error ? error.message : "Metrics could not be loaded.",
              };
            }
          } catch (error) {
            const previous = lastGoodSessions.current.get(id);
            return {
              id,
              session: previous?.session,
              metrics: previous?.metrics,
              error: error instanceof Error ? error.message : "Session could not be loaded.",
            };
          }
        }),
      );
      const summaries = new Map(listedSessions.map((row) => [row.id, row]));
      for (const row of trackedSessions) {
        const listed = summaries.get(row.id);
        summaries.set(row.id, {
          ...listed,
          ...row,
          session: row.session ?? listed?.session,
          metrics: row.metrics ?? listed?.metrics,
        });
      }
      const nextSessions = [...summaries.values()];
      for (const row of nextSessions) {
        if (row.session) lastGoodSessions.current.set(row.id, row);
      }
      setRestListLoaded(listLoaded);
      setLiveSessions(nextSessions);
      if (listLoaded || trackedSessions.some((row) => row.session)) {
        setLastUpdated(new Date());
      }
    } finally {
      requestInFlight.current = false;
      setRefreshing(false);
    }
  }, [demoMode, trackedIds]);

  useEffect(() => {
    void refreshSessions();
    const timer = window.setInterval(() => void refreshSessions(), pollIntervalMs);
    return () => window.clearInterval(timer);
  }, [refreshSessions]);

  const demoRows = useMemo<SessionSummary[]>(
    () =>
      demoSessions.map((demo) => ({
        id: demo.session.id,
        session: demo.session,
        metrics: demo.metrics,
      })),
    [],
  );
  const mergeLiveSessions = useMemo(() => {
    const rows = new Map(mirroredSessions?.map((row) => [row.id, row]) ?? []);
    for (const row of liveSessions) {
      const mirrored = rows.get(row.id);
      rows.set(row.id, {
        ...mirrored,
        ...row,
        session: row.session ?? mirrored?.session,
        metrics: row.metrics ?? mirrored?.metrics,
      });
    }
    return [...rows.values()];
  }, [liveSessions, mirroredSessions]);
  const sessionRows = demoMode
    ? demoRows
    : !restListLoaded && convexReady && mirroredSessions !== undefined
      ? mergeLiveSessions
      : liveSessions;
  const replayEvents = demoMode
    ? events
    : restEvents?.sessionId === selectedId
      ? restEvents.events
      : mirroredEvents?.sessionId === selectedId
      ? mirroredEvents.events
      : [];
  const updateMirroredSessions = useCallback(
    (rows: SessionSummary[]) => setMirroredSessions(rows),
    [],
  );
  const updateMirroredEvents = useCallback(
    (rows: MirroredEventsState) => setMirroredEvents(rows),
    [],
  );
  const visibleRows = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return sessionRows;
    return sessionRows.filter((row) =>
      (row.session?.trainee_id ?? "").toLowerCase().includes(query),
    );
  }, [search, sessionRows]);
  const selectedSummary = sessionRows.find((row) => row.id === selectedId);
  // A retry or reset on the headset starts a new session for the same trainee.
  const newerAttempt = useMemo(() => {
    const current = selectedSummary?.session;
    if (demoMode || !current) return undefined;
    const createdAt = Date.parse(current.created_at);
    return sessionRows
      .filter(
        (row) =>
          row.id !== current.id &&
          row.session?.trainee_id === current.trainee_id &&
          Date.parse(row.session.created_at) > createdAt,
      )
      .sort((a, b) => Date.parse(b.session!.created_at) - Date.parse(a.session!.created_at))[0];
  }, [demoMode, selectedSummary, sessionRows]);
  const selectedDemo = demoMode
    ? demoSessions.find((demo) => demo.session.id === selectedId)
    : undefined;

  useEffect(() => {
    if (!sessionRows.some((row) => row.id === selectedId)) {
      setSelectedId(sessionRows[0]?.id ?? "");
    }
  }, [selectedId, sessionRows]);

  useEffect(() => {
    const controller = new AbortController();
    setCurrentTimeMs(0);
    setPlaying(false);
    setSamplesError("");
    if (!selectedId) {
      setSamples([]);
      setEvents([]);
      setRestEvents(null);
      setEventsError("");
      setSamplesLoading(false);
      return () => controller.abort();
    }
    if (demoMode && selectedDemo) {
      setSamples(selectedDemo.samples);
      setEvents(selectedDemo.events);
      setRestEvents(null);
      setEventsError("");
      setSamplesLoading(false);
      return () => controller.abort();
    }
    setSamples([]);
    setEvents([]);
    setRestEvents(null);
    setEventsError("");
    setSamplesLoading(true);
    void api
      .getSamples(selectedId, controller.signal)
      .then((result) => setSamples(result))
      .catch((error: unknown) => {
        if (error instanceof Error && error.name === "AbortError") return;
        setSamplesError(error instanceof Error ? error.message : "Motion samples could not be loaded.");
      })
      .finally(() => setSamplesLoading(false));
    void api
      .getEvents(selectedId, controller.signal)
      .then((result) => {
        setRestEvents({ sessionId: selectedId, events: result });
        setEventsError("");
      })
      .catch((error: unknown) => {
        if (error instanceof Error && error.name === "AbortError") return;
        setEventsError(
          error instanceof Error ? error.message : "Session events could not be loaded.",
        );
      });
    return () => controller.abort();
  }, [demoMode, selectedDemo, selectedId]);

  // While the trainee is practising, keep the replay current without Convex.
  const selectedRunning = !demoMode && selectedSummary?.session?.status === "running";
  const wasRunning = useRef<{ id: string; running: boolean }>({ id: "", running: false });
  useEffect(() => {
    if (!selectedId) return;
    const controller = new AbortController();
    const refresh = () => {
      void api
        .getSamples(selectedId, controller.signal)
        .then(setSamples)
        .catch(() => undefined);
      void api
        .getEvents(selectedId, controller.signal)
        .then((result) => setRestEvents({ sessionId: selectedId, events: result }))
        .catch(() => undefined);
    };
    const previous = wasRunning.current;
    wasRunning.current = { id: selectedId, running: selectedRunning };
    // The session just finished: fetch once more so the last uploads are not missed.
    if (!selectedRunning && previous.id === selectedId && previous.running) refresh();
    if (!selectedRunning) return () => controller.abort();
    const timer = window.setInterval(refresh, 3_000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [selectedId, selectedRunning]);

  const durationSeconds = demoMode
    ? selectedDemo?.metrics.elapsed_s ?? 0
    : elapsedSeconds(selectedSummary?.session) ?? selectedSummary?.metrics?.elapsed_s ?? 0;
  const timelineEndMs = Math.max(
    durationSeconds * 1000,
    samples.reduce((max, sample) => Math.max(max, sample.t_ms), 0),
    replayEvents.reduce((max, event) => Math.max(max, event.t_ms), 0),
    1000,
  );

  useEffect(() => {
    if (!playing) return;
    const timer = window.setInterval(() => {
      setCurrentTimeMs((current) => {
        if (current + 250 >= timelineEndMs) {
          setPlaying(false);
          return timelineEndMs;
        }
        return current + 250;
      });
    }, 250);
    return () => window.clearInterval(timer);
  }, [playing, timelineEndMs]);

  const totals = useMemo(() => {
    const running = sessionRows.filter((row) => row.session?.status === "running").length;
    const completed = sessionRows.filter((row) => row.session?.status === "completed").length;
    const scored = sessionRows
      .filter((row) => row.session?.status === "completed" && row.metrics)
      .map((row) => row.metrics!.practice_score);
    const errors = sessionRows.reduce(
      (sum, row) => sum + (row.metrics?.drops ?? 0) + (row.metrics?.invalid_releases ?? 0),
      0,
    );
    return {
      running,
      completed,
      meanScore:
        scored.length > 0
          ? scored.reduce((sum, score) => sum + score, 0) / scored.length
          : undefined,
      errors,
    };
  }, [sessionRows]);

  function openSample() {
    setDemoMode(true);
    setView("sessions");
    setMenuOpen(false);
  }

  function openLiveSessions() {
    setDemoMode(false);
    setView("sessions");
    setMenuOpen(false);
  }

  function openReplay() {
    if (!selectedId && sessionRows[0]) setSelectedId(sessionRows[0].id);
    setView("replay");
    setMenuOpen(false);
  }

  function openReport() {
    if (!selectedId && sessionRows[0]) setSelectedId(sessionRows[0].id);
    setView("report");
    setMenuOpen(false);
  }

  function selectSession(id: string) {
    setSelectedId(id);
    setView("replay");
  }

  const selectedMetrics =
    (demoMode ? getMetricsFromDemo(selectedDemo) : selectedSummary?.metrics) ?? undefined;
  const selectedLabels = metricLabels(selectedSummary?.session?.exercise);
  const hasFallbackSessions = convexReady && mirroredSessions !== undefined;
  const hasApiError =
    liveSessions.some((row) => row.error) || (Boolean(sessionListError) && !hasFallbackSessions);
  const apiState = demoMode
    ? "preview"
    : hasApiError
      ? "error"
      : restListLoaded || hasFallbackSessions || (trackedIds.length > 0 && lastUpdated)
        ? "current"
        : "idle";
  const sessionList = (
    <section className="panel session-panel" aria-labelledby="sessions-heading">
      <header className="panel-heading session-heading">
        <div>
          <h2 id="sessions-heading">{demoMode ? "Preview sessions" : "Live sessions"}</h2>
          <p className="panel-subtitle">
            {sessionRows.length} session{sessionRows.length === 1 ? "" : "s"}
            {demoMode
              ? " · synthetic preview"
              : restListLoaded
                ? " · backend list"
                : hasFallbackSessions
                  ? " · Convex mirror fallback"
                  : " · tracked in this browser"}
            {!restListLoaded && sessionListError && (
              <span className="row-error">Backend list unavailable: {sessionListError}</span>
            )}
          </p>
        </div>
        <button
          type="button"
          className="icon-button refresh-button"
          onClick={() => void refreshSessions()}
          disabled={demoMode || refreshing}
          aria-label={refreshing ? "Refreshing REST details" : "Refresh REST details"}
          title={demoMode ? "Switch to live sessions to refresh" : "Refresh tracked session details"}
        >
          <RefreshIcon />
        </button>
      </header>
      <div className="session-toolbar">
        <label className="sr-only" htmlFor="session-search">
          Search by trainee name
        </label>
        <input
          id="session-search"
          className="input search-input"
          type="search"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          placeholder="Search by trainee name"
        />
        {!demoMode && lastUpdated && (
          <span className="updated-stamp">
            Updated {lastUpdated.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
          </span>
        )}
      </div>
      {sessionRows.length === 0 ? (
        <div className="empty-state">
          <strong>
            {restListLoaded
              ? "No sessions in the backend list yet"
              : hasFallbackSessions
                ? "No sessions in the live mirror yet"
                : "No sessions tracked yet"}
          </strong>
          <p>Sessions appear here when a trainee starts an attempt on the headset.</p>
          <button type="button" className="button button-secondary" onClick={openSample}>
            View sample sessions
          </button>
        </div>
      ) : visibleRows.length === 0 ? (
        <p className="empty-copy filter-empty">No tracked sessions match “{search}”.</p>
      ) : (
        <div className="session-list" role="list" aria-label="Training sessions">
          {visibleRows.map((row) => (
            <div className="session-list-row" role="listitem" key={row.id}>
              <button
                type="button"
                className="session-select"
                aria-pressed={selectedId === row.id}
                onClick={() => selectSession(row.id)}
              >
                <span className="session-primary">
                  <span className="trainee-name">{row.session?.trainee_id ?? row.id}</span>
                  <span className={`status-tag ${statusClass(row.session?.status)}`}>
                    {row.session ? statusLabel(row.session.status) : "Unavailable"}
                  </span>
                </span>
                <span className="session-secondary">
                  <span className="session-id">{row.id}</span>
                  <span>{formatDuration(elapsedSeconds(row.session))}</span>
                </span>
                <span className="session-metrics">
                  <span className="session-exercise">{row.session ? exerciseLabel(row.session.exercise) : "-"}</span>
                  <span className="session-score">
                    {row.session?.status === "completed" && row.metrics
                      ? `${row.metrics.practice_score.toFixed(1)} / 100`
                      : "Score -"}
                  </span>
                </span>
              </button>
              {row.error && (
                <span className="row-error" title={row.error}>
                  {row.session ? `Showing last successful data. ${row.error}` : row.error}
                </span>
              )}
            </div>
          ))}
        </div>
      )}
      {selectedSummary?.session && (
        <div className="selected-summary">
          <div className="selected-summary-heading">
            <h3>Selected session</h3>
            <div className="selected-summary-actions">
              {isLapCholeCvs(selectedSummary.session.exercise) && selectedSummary.session.status === "completed" && (
                <button type="button" className="text-button" onClick={openReport}>
                  Open report
                </button>
              )}
              <span className={`status-tag ${statusClass(selectedSummary.session.status)}`}>
                {statusLabel(selectedSummary.session.status)}
              </span>
            </div>
          </div>
          <dl className="summary-grid">
            <div>
              <dt>Practice score</dt>
              <dd className={selectedSummary.session.status === "completed" && selectedMetrics ? "" : "missing-value"}>
                {selectedSummary.session.status === "completed"
                  ? selectedMetrics?.practice_score.toFixed(1) ?? "-"
                  : "-"}
                <small> /100</small>
              </dd>
            </div>
            <div><dt>Completion</dt><dd>{formatPercent(selectedMetrics?.completion_pct, 1)}</dd></div>
            <div><dt>Exercise</dt><dd>{exerciseLabel(selectedSummary.session.exercise)}</dd></div>
            {!isLapCholeCvs(selectedSummary.session.exercise) && (
              <>
                <div><dt>{metricLabels(selectedSummary.session.exercise).successes}</dt><dd className={selectedMetrics ? "" : "missing-value"}>{selectedMetrics?.successful_transfers ?? "-"}</dd></div>
                <div><dt>Errors</dt><dd className={selectedMetrics ? "" : "missing-value"}>{selectedMetrics ? selectedMetrics.drops + selectedMetrics.invalid_releases : "-"}</dd></div>
              </>
            )}
            <div><dt>Path efficiency</dt><dd className={selectedMetrics ? "" : "missing-value"}>{selectedMetrics ? `${(selectedMetrics.path_efficiency * 100).toFixed(1)}%` : "-"}</dd></div>
            <div><dt>Elapsed</dt><dd>{formatDuration(durationSeconds)}</dd></div>
          </dl>
          <p className="small-note">
            {selectedSummary.session.status === "completed"
              ? "Rule-based practice score; not clinically validated."
              : "Final practice score is available after completion."}
          </p>
        </div>
      )}
      {demoMode && (
        <p className="demo-disclaimer">
          Preview names, events and metrics are synthetic. They are not returned by the backend.
        </p>
      )}
    </section>
  );

  return (
    <div className="app-frame">
      {convexReady && (
        <ConvexMirrorBridge
          sessionId={selectedId}
          onSessions={updateMirroredSessions}
          onEvents={updateMirroredEvents}
        />
      )}
      <aside className={`sidebar ${menuOpen ? "open" : ""}`} aria-label="Main navigation">
        <div className="brand-row">
          <BrandMark />
          <span>Jeremy.ai</span>
          <button
            type="button"
            className="icon-button mobile-close"
            onClick={() => setMenuOpen(false)}
            aria-label="Close navigation"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="workspace-name">
          <span className="workspace-mark">J</span>
          <span><strong>Instructor console</strong><small>Surgical skills lab</small></span>
        </div>
        <nav className="navigation">
          <a className="nav-item" href="#home">
            <PreviewIcon /> Home
          </a>
          {onOpenCall && (
            <button type="button" className="nav-item" onClick={onOpenCall}>
              <PreviewIcon /> Live call
            </button>
          )}
          <p className="nav-section-title">Practice</p>
          <button
            type="button"
            className="nav-item"
            aria-current={view === "sessions" ? "page" : undefined}
            onClick={openLiveSessions}
          >
            <ListIcon /> Live sessions
          </button>
          <button
            type="button"
            className="nav-item"
            aria-current={view === "replay" ? "page" : undefined}
            onClick={openReplay}
          >
            <ReplayIcon /> Replay
          </button>
          <button
            type="button"
            className="nav-item"
            aria-current={view === "report" ? "page" : undefined}
            onClick={openReport}
            disabled={!isLapCholeCvs(selectedSummary?.session?.exercise) || selectedSummary?.session?.status !== "completed"}
            title="Available for completed critical-view sessions"
          >
            <ReportIcon /> Session report
          </button>
        </nav>
        <div className="sidebar-footer">
          <span className={`connection-dot ${convexReady ? "configured" : ""}`} />
          <span>{convexReady ? "Convex live mirror" : "REST session data"}</span>
        </div>
      </aside>

      {menuOpen && (
        <button
          type="button"
          className="menu-scrim"
          onClick={() => setMenuOpen(false)}
          aria-label="Close navigation menu"
        />
      )}

      <div className="work-sheet">
        <header className="topbar">
          <button
            type="button"
            className="icon-button menu-toggle"
            onClick={() => setMenuOpen(true)}
            aria-label="Open navigation"
          >
            <MenuIcon />
          </button>
          <span className="topbar-title">Instructor console</span>
          <span className="topbar-spacer" />
          {demoMode && <span className="demo-flag">Sample preview</span>}
          <span className={`api-indicator ${apiState}`}>
            <i />
            {apiState === "preview"
              ? "Preview data"
              : apiState === "error"
                ? "REST request failed"
                : apiState === "current"
                  ? convexReady && mirroredSessions !== undefined
                    ? restListLoaded
                      ? "REST live list"
                      : "Convex fallback"
                    : "REST data current"
                  : convexReady
                    ? "Connecting to Convex"
                    : "REST · no session tracked"}
          </span>
        </header>

        <main className="canvas">
          <div className="title-row">
            <div>
              <h1>{view === "sessions" ? "Live coaching" : view === "report" ? "Session report" : "Session replay"}</h1>
              <p className="title-subtitle">
                {view === "sessions"
                  ? "Monitor peg-transfer, wound-closure, and Laparoscopic cholecystectomy: critical view of safety sessions from the browser."
                  : view === "report"
                    ? "Review deterministic exercise results, movement and coaching notes."
                    : "Review recorded movement, events and coaching notes."}
              </p>
            </div>
          </div>

          {convexConfigError && (
            <div className="notice notice-error" role="alert">
              {convexConfigError}
            </div>
          )}

          <section className="kpi-strip" aria-label="Tracked session metrics">
            <Metric label="Running now" value={String(totals.running)} />
            <Metric label="Completed" value={String(totals.completed)} />
            <Metric
              label="Mean practice score"
              value={totals.meanScore === undefined ? "-" : totals.meanScore.toFixed(1)}
              suffix={totals.meanScore === undefined ? "" : " /100"}
            />
            <Metric label="Recorded errors" value={String(totals.errors)} />
          </section>

          <div className="dashboard-grid">
            <div className="main-column">
              {view === "sessions" ? (
                sessionList
              ) : view === "report" ? (
                selectedId ? (
                  <CvsReportPanel
                    key={selectedId}
                    sessionId={selectedId}
                    isDemo={demoMode}
                    demoReport={selectedDemo?.report}
                  />
                ) : (
                  <section className="panel"><p className="empty-copy">Select a completed critical-view session to review its report.</p></section>
                )
              ) : (
                <>
                  {isLapCholeCvs(selectedSummary?.session?.exercise) ? (
                    <CvsReplayPanel
                      key={selectedId}
                      sessionId={selectedId}
                      isDemo={demoMode}
                      demoReplay={selectedDemo ? demoCvsReplay : undefined}
                    />
                  ) : (
                    <ReplayPanel
                      samples={samples}
                      events={replayEvents}
                      exercise={selectedSummary?.session?.exercise}
                      currentTimeMs={currentTimeMs}
                      durationMs={timelineEndMs}
                      isPlaying={playing}
                      onTimeChange={setCurrentTimeMs}
                      onTogglePlay={() => setPlaying((value) => !value)}
                      isDemo={demoMode}
                      loading={samplesLoading}
                      error={samplesError}
                    />
                  )}
                  {!isLapCholeCvs(selectedSummary?.session?.exercise) && eventsError && (
                    <p className="small-note" role="status">
                      {mirroredEvents?.sessionId === selectedId
                        ? `Backend events unavailable; showing the Convex mirror. ${eventsError}`
                        : `Session events could not be loaded. ${eventsError}`}
                    </p>
                  )}
                  {newerAttempt && (
                    <div className="newer-attempt" role="status">
                      <span>
                        {selectedSummary?.session?.trainee_id} started a newer attempt
                        {newerAttempt.session?.status ? ` (${statusLabel(newerAttempt.session.status).toLowerCase()})` : ""}.
                        Notes and commands go to the session you are viewing.
                      </span>
                      <button
                        type="button"
                        className="button button-secondary"
                        onClick={() => selectSession(newerAttempt.id)}
                      >
                        Switch to newer attempt
                      </button>
                    </div>
                  )}
                  {!isLapCholeCvs(selectedSummary?.session?.exercise) && <div className="replay-detail-row">
                    <section className="panel detail-panel" aria-labelledby="detail-heading">
                      <header className="panel-heading">
                        <div>
                          <h2 id="detail-heading">Session metrics</h2>
                          <p className="panel-subtitle">
                            {selectedSummary?.session?.trainee_id ?? "Choose a session"} ·
                            {" "}{exerciseLabel(selectedSummary?.session?.exercise)}
                          </p>
                        </div>
                      </header>
                      <dl className="detail-metrics">
                        <Metric
                          label="Practice score"
                          value={selectedSummary?.session?.status === "completed" ? selectedMetrics?.practice_score.toFixed(1) ?? "-" : "-"}
                          suffix="/100"
                        />
                        <Metric label={selectedLabels.successes} value={String(selectedMetrics?.successful_transfers ?? "-")} />
                        <Metric label={selectedLabels.drops} value={String(selectedMetrics?.drops ?? "-")} />
                        <Metric label={selectedLabels.invalid} value={String(selectedMetrics?.invalid_releases ?? "-")} />
                        {selectedMetrics?.mean_bite_error_mm != null && (
                          <Metric label="Mean bite error" value={formatMm(selectedMetrics.mean_bite_error_mm)} />
                        )}
                        {selectedMetrics?.spacing_sd_mm != null && (
                          <Metric label="Stitch spacing SD" value={formatMm(selectedMetrics.spacing_sd_mm)} />
                        )}
                        <Metric label="Path efficiency" value={selectedMetrics ? `${(selectedMetrics.path_efficiency * 100).toFixed(1)}%` : "-"} />
                        <Metric label="Instrument travel" value={selectedMetrics ? `${selectedMetrics.total_distance_m.toFixed(2)} m` : "-"} />
                      </dl>
                      {selectedMetrics?.details && Object.keys(selectedMetrics.details).length > 0 && (
                        <div className="attempts-table-wrap">
                          <table className="attempts-table details-table" aria-label="Exercise details">
                            <thead>
                              <tr>
                                <th scope="col">Detail</th>
                                <th scope="col">Value</th>
                              </tr>
                            </thead>
                            <tbody>
                              {Object.entries(selectedMetrics.details).map(([key, value]) => (
                                <tr key={key}>
                                  <td>{detailLabel(key)}</td>
                                  <td className="num">{formatDetail(value)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                      <p className="small-note">Deterministic practice score; not a clinical assessment.</p>
                    </section>
                  </div>}
                  {selectedId && selectedSummary?.session?.status === "completed" && !demoMode && (
                    <CoachPanel key={selectedId} sessionId={selectedId} />
                  )}
                  {selectedId && selectedSummary?.session?.trainee_id && !demoMode && (
                    <AttemptsPanel
                      traineeId={selectedSummary.session.trainee_id}
                      selectedId={selectedId}
                      exercise={selectedSummary.session.exercise}
                      refreshKey={sessionRows
                        .filter((row) => row.session?.trainee_id === selectedSummary.session?.trainee_id)
                        .map((row) => `${row.id}:${row.session?.status}`)
                        .join(",")}
                      onSelect={selectSession}
                    />
                  )}
                </>
              )}
            </div>

            <aside className="right-rail" aria-label="Session list and coaching">
              {view !== "sessions" ? (
                <>
                  <div className="rail-heading">
                    <h2>{demoMode ? "Preview sessions" : "Sessions"}</h2>
                  </div>
                  <div className="panel rail-sessions">
                <div className="rail-search-row">
                  <label className="sr-only" htmlFor="rail-session-search">Filter sessions</label>
                  <input
                    id="rail-session-search"
                    className="input search-input"
                    type="search"
                    value={search}
                    onChange={(event) => setSearch(event.currentTarget.value)}
                    placeholder="Search trainees"
                  />
                </div>
                {sessionRows.length === 0 ? (
                  <div className="empty-state compact-empty">
                    <strong>No sessions yet</strong>
                    <p>Sessions appear here when a trainee starts an attempt on the headset.</p>
                    <button type="button" className="button button-secondary" onClick={openSample}>
                      View sample sessions
                    </button>
                  </div>
                ) : visibleRows.length === 0 ? (
                  <p className="empty-copy rail-empty">No matching sessions.</p>
                ) : (
                  <div className="rail-session-list" role="list">
                    {visibleRows.slice(0, 5).map((row) => (
                      <button
                        type="button"
                        role="listitem"
                        key={row.id}
                        className="rail-session"
                        aria-pressed={selectedId === row.id}
                        onClick={() => selectSession(row.id)}
                      >
                        <span className="rail-session-main">
                          <strong>{row.session?.trainee_id ?? row.id}</strong>
                          <span className={`status-tag ${statusClass(row.session?.status)}`}>
                            {row.session ? statusLabel(row.session.status) : "Unavailable"}
                          </span>
                        </span>
                        <span className="rail-session-meta">
                          {row.session?.status === "completed" && row.metrics
                            ? `${row.metrics.practice_score.toFixed(1)} / 100`
                            : "Score -"}
                          {" · "}
                          {formatDuration(elapsedSeconds(row.session))}
                        </span>
                      </button>
                    ))}
                  </div>
                )}
                {sessionRows.length > 5 && (
                  <button type="button" className="button button-secondary view-all" onClick={openLiveSessions}>
                    View all {sessionRows.length} sessions
                  </button>
                )}
                {demoMode && <p className="demo-disclaimer">Synthetic preview data</p>}
                  </div>
                </>
              ) : null}

              {selectedId && selectedSummary?.session && !demoMode && !isLapCholeCvs(selectedSummary.session.exercise) && (
                <CollaborationPanel
                  key={selectedId}
                  sessionId={selectedId}
                  currentTimeMs={currentTimeMs}
                  events={replayEvents}
                  exercise={selectedSummary.session.exercise}
                  convexReady={convexReady}
                  onSelectTime={setCurrentTimeMs}
                />
              )}
              {demoMode && (
                <section className="panel collaboration-unavailable">
                  <h2>Preview mode</h2>
                  <p>Annotations and headset controls are disabled for synthetic sessions.</p>
                </section>
              )}
              {!demoMode && (
                <p className="api-footnote">
                  REST · <span>{getApiBase()}</span>
                </p>
              )}
            </aside>
          </div>
        </main>
      </div>
    </div>
  );
}

function Metric({ label, value, suffix = "" }: { label: string; value: string; suffix?: string }) {
  return (
    <div className="metric">
      <span className="metric-label">{label}</span>
      <span className={`metric-value num ${value === "-" ? "missing-value" : ""}`}>
        {value}
        {suffix && <small>{suffix}</small>}
      </span>
    </div>
  );
}

function RefreshIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M16 8a6.2 6.2 0 0 0-10.8-2L4 7.5M4 4.5v3h3M4 12a6.2 6.2 0 0 0 10.8 2L16 12.5m0 3v-3h-3" />
    </svg>
  );
}

function CloseIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="m5 5 10 10M15 5 5 15" />
    </svg>
  );
}

function MenuIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M3 5h14M3 10h14M3 15h14" />
    </svg>
  );
}

function ListIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M7 5h10M7 10h10M7 15h10M3 5h.1M3 10h.1M3 15h.1" />
    </svg>
  );
}

function ReplayIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M3.5 9.5a6.5 6.5 0 1 0 2-4.7L3 7.2m.5-4v4h4M10 6v4l2.5 1.5" />
    </svg>
  );
}

function ReportIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M5 2.5h7l3 3V17a.5.5 0 0 1-.5.5h-9A.5.5 0 0 1 5 17V2.5Z" />
      <path d="M12 2.5V6h3M7.5 9h5M7.5 12h5M7.5 15h3" />
    </svg>
  );
}

function PreviewIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M2.5 10s2.7-5 7.5-5 7.5 5 7.5 5-2.7 5-7.5 5-7.5-5-7.5-5Z" />
      <circle cx="10" cy="10" r="2" />
    </svg>
  );
}

export default App;
