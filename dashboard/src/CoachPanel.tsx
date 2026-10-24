import { useEffect, useState, type FormEvent } from "react";
import {
  coachApi,
  MAX_QUESTION_CHARS,
  type AssessmentCriterion,
  type AssessmentLevel,
  type CoachingResponse,
  type SessionAssessment,
} from "./coachApi";

const LEVEL_LABEL: Record<AssessmentLevel, string> = {
  proficient: "Proficient",
  developing: "Developing",
  needs_work: "Needs work",
  not_assessed: "Not assessed",
};

function formatValue(criterion: AssessmentCriterion) {
  if (criterion.value === null) return "-";
  if (criterion.unit === "%") return `${criterion.value.toFixed(1)}%`;
  if (criterion.unit === "s") return `${criterion.value.toFixed(0)} s`;
  if (criterion.unit === "ratio") return criterion.value.toFixed(2);
  return String(criterion.value);
}

function errorText(error: unknown) {
  return error instanceof Error ? error.message : "Request failed.";
}

export function CoachPanel({ sessionId }: { sessionId: string }) {
  const [assessment, setAssessment] = useState<SessionAssessment>();
  const [assessmentError, setAssessmentError] = useState("");
  const [question, setQuestion] = useState("");
  const [coaching, setCoaching] = useState<CoachingResponse>();
  const [busy, setBusy] = useState(false);
  const [coachError, setCoachError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    coachApi
      .getAssessment(sessionId, controller.signal)
      .then(setAssessment)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setAssessmentError(errorText(error));
      });
    return () => controller.abort();
  }, [sessionId]);

  async function ask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setCoachError("");
    try {
      setCoaching(await coachApi.askCoach(sessionId, question));
    } catch (error) {
      setCoachError(errorText(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel coach-panel" aria-labelledby="coach-heading">
      <header className="panel-heading">
        <div>
          <h2 id="coach-heading">Coach &amp; assessment</h2>
          <p className="panel-subtitle">Rule-based ratings from stored metrics · AI coaching explains, never scores</p>
        </div>
      </header>

      {assessmentError && <p className="form-error" role="status">Assessment unavailable. {assessmentError}</p>}
      {!assessment && !assessmentError && <p className="small-note">Loading assessment…</p>}
      {assessment && (
        <>
          <table className="assessment-table">
            <thead>
              <tr>
                <th scope="col">Criterion</th>
                <th scope="col">Value</th>
                <th scope="col">Level</th>
              </tr>
            </thead>
            <tbody>
              {assessment.criteria.map((criterion) => (
                <tr key={criterion.id} title={criterion.rule}>
                  <th scope="row">{criterion.label}</th>
                  <td className="num">{formatValue(criterion)}</td>
                  <td>
                    <span className={`status-tag level-${criterion.level}`}>{LEVEL_LABEL[criterion.level]}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="small-note">
            Practice score {assessment.practice_score.toFixed(1)}/100. {assessment.thresholds_note} Hover a row for its rule.
          </p>
        </>
      )}

      <form className="annotation-form coach-form" onSubmit={(event) => void ask(event)}>
        <label className="sr-only" htmlFor="coach-question">
          Ask the coach about this attempt
        </label>
        <textarea
          id="coach-question"
          value={question}
          maxLength={MAX_QUESTION_CHARS}
          onChange={(event) => setQuestion(event.currentTarget.value)}
          placeholder="Ask about this attempt, or leave blank for a debrief…"
          rows={2}
        />
        <div className="form-footer">
          <span>{question.length}/{MAX_QUESTION_CHARS}</span>
          <button className="button button-primary" type="submit" disabled={busy}>
            {busy ? "Asking…" : question.trim() ? "Ask coach" : "Get debrief"}
          </button>
        </div>
      </form>
      {coachError && <p className="form-error" role="alert">{coachError}</p>}

      {coaching && (
        <div className="coach-result" aria-live="polite">
          {coaching.answer && <p className="coach-answer">{coaching.answer}</p>}
          <p>{coaching.summary}</p>
          {coaching.tips.length > 0 && (
            <ul className="coach-tips">
              {coaching.tips.map((tip, index) => (
                <li key={`${tip.event_id ?? "tip"}-${index}`}>{tip.text}</li>
              ))}
            </ul>
          )}
          {coaching.next_exercise && (
            <p className="coach-drill">
              <strong>Next drill: {coaching.next_exercise.title}</strong>
              <small>{coaching.next_exercise.description}</small>
            </p>
          )}
          <p className="small-note">
            {coaching.source === "gemini" ? "Phrased by Gemini from the metrics above." : "Rule-based fallback (Gemini not used)."}
          </p>
        </div>
      )}
    </section>
  );
}
