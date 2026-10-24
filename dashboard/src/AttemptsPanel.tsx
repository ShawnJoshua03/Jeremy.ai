import { useEffect, useState } from "react";
import { api } from "./api";
import type { TraineeProgress } from "./types";

interface AttemptsPanelProps {
  traineeId: string;
  selectedId: string;
  /** Changes when the session list refreshes, so new attempts appear. */
  refreshKey: string;
  onSelect: (sessionId: string) => void;
}

function delta(current: number, previous: number | undefined, digits: number, lowerIsBetter = false) {
  if (previous === undefined) return null;
  const diff = current - previous;
  if (Math.abs(diff) < 10 ** -digits / 2) return <small className="delta flat">±0</small>;
  const better = lowerIsBetter ? diff < 0 : diff > 0;
  return (
    <small className={`delta ${better ? "better" : "worse"}`}>
      {diff > 0 ? "+" : "−"}
      {Math.abs(diff).toFixed(digits)}
    </small>
  );
}

export function AttemptsPanel({ traineeId, selectedId, refreshKey, onSelect }: AttemptsPanelProps) {
  const [rows, setRows] = useState<TraineeProgress[]>();
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    api
      .getTraineeProgress(traineeId, controller.signal)
      .then((result) => {
        setRows(result);
        setError("");
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) return;
        setError(cause instanceof Error ? cause.message : "Attempts could not be loaded.");
      });
    return () => controller.abort();
  }, [traineeId, refreshKey]);

  if (error) {
    return (
      <section className="panel attempts-panel" aria-labelledby="attempts-heading">
        <h2 id="attempts-heading">Attempts by {traineeId}</h2>
        <p className="form-error">{error}</p>
      </section>
    );
  }
  if (!rows || rows.length < 2) return null; // nothing to compare yet

  const first = rows[0];
  const best = rows.reduce((top, row) => (row.practice_score > top.practice_score ? row : top), first);
  const last = rows[rows.length - 1];

  return (
    <section className="panel attempts-panel" aria-labelledby="attempts-heading">
      <header className="panel-heading">
        <div>
          <h2 id="attempts-heading">Attempts by {traineeId}</h2>
          <p className="panel-subtitle">
            {rows.length} completed · score {first.practice_score.toFixed(0)} → {last.practice_score.toFixed(0)} · best{" "}
            {best.practice_score.toFixed(0)}
          </p>
        </div>
      </header>
      <div className="attempts-table-wrap">
        <table className="attempts-table">
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Score</th>
              <th scope="col">Time</th>
              <th scope="col">Efficiency</th>
              <th scope="col">Errors</th>
              <th scope="col">Est. cost</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => {
              const previous = index > 0 ? rows[index - 1] : undefined;
              const current = row.session_id === selectedId;
              return (
                <tr
                  key={row.session_id}
                  className={current ? "current" : undefined}
                  onClick={() => onSelect(row.session_id)}
                  aria-current={current ? "true" : undefined}
                >
                  <td className="num">
                    <button type="button" className="text-button" onClick={() => onSelect(row.session_id)}>
                      {index + 1}
                    </button>
                  </td>
                  <td className="num">
                    {row.practice_score.toFixed(1)} {delta(row.practice_score, previous?.practice_score, 1)}
                  </td>
                  <td className="num">
                    {row.elapsed_s.toFixed(0)} s {delta(row.elapsed_s, previous?.elapsed_s, 0, true)}
                  </td>
                  <td className="num">
                    {(row.path_efficiency * 100).toFixed(0)}%{" "}
                    {delta(row.path_efficiency * 100, previous ? previous.path_efficiency * 100 : undefined, 0)}
                  </td>
                  <td className="num">
                    {row.errors ?? "-"}{" "}
                    {row.errors !== undefined && delta(row.errors, previous?.errors, 0, true)}
                  </td>
                  <td className="num">
                    {row.cost_total_usd !== undefined ? `$${row.cost_total_usd.toFixed(0)}` : "-"}{" "}
                    {row.cost_total_usd !== undefined && delta(row.cost_total_usd, previous?.cost_total_usd, 0, true)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p className="small-note">
        Changes are against the previous attempt. Cost is an illustrative estimate, not billing data.
      </p>
    </section>
  );
}
