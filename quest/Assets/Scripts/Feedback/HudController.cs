using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    public sealed class HudController : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        public EventRecorder recorder;
        public UploadQueue uploads;
        public SutureDetector suture; // optional: shows the awaited exit bite in wound closure
        public Text statusText, resultsText;
        public Button startButton, pauseButton, resetButton, retryUploadButton, exerciseButton;
        private Metrics serverMetrics;
        private CostBreakdown cost;
        private string lastError;
        private float errorUntil;
        private void Start()
        {
            if (startButton != null) startButton.onClick.AddListener(StartExercise);
            if (pauseButton != null) pauseButton.onClick.AddListener(TogglePause);
            if (resetButton != null) resetButton.onClick.AddListener(ResetExercise);
            if (retryUploadButton != null) retryUploadButton.onClick.AddListener(RetryUpload);
            if (uploads != null) uploads.ResultsAvailable += ShowResults;
            if (recorder != null) recorder.Recorded += OnEvent;
            if (exercise != null) exercise.Reset += ClearResults;
        }
        public void StartExercise() { exercise.Begin(); }
        public void ResetExercise() { exercise.ResetExercise(); }
        public void RetryUpload() { uploads.RetryBlocked(); }
        public void TogglePause()
        {
            if (exercise.State == ExerciseState.Paused) exercise.Resume(); else exercise.Pause();
        }
        private void OnEvent(RecordedEvent value)
        {
            if (value.type == "transfer_success" || value.type == "stitch_placed") return;
            lastError = value.type == "drop" ? "Dropped: returned to source"
                : value.type == "tissue_contact" ? "Tissue contact: keep the tips off the wound edge"
                : value.type == "stitch_missed" ? "Missed bite: close within 8 mm of the entry, then the exit mark"
                : "Release inside the matching target";
            errorUntil = Time.unscaledTime + 3;
        }
        private void ClearResults() { serverMetrics = null; cost = null; lastError = null; }
        private void ShowResults(PendingAttempt attempt)
        {
            if (attempt.attemptId != exercise.AttemptId) return;
            serverMetrics = attempt.metrics;
            cost = attempt.cost;
        }
        private void Update()
        {
            if (exercise == null || recorder == null) return;
            if (startButton != null) startButton.interactable = exercise.State == ExerciseState.Ready;
            if (pauseButton != null) pauseButton.interactable = exercise.State == ExerciseState.Running || exercise.State == ExerciseState.Paused;
            if (exerciseButton != null) exerciseButton.interactable = exercise.State != ExerciseState.Running && exercise.State != ExerciseState.Paused;
            bool wound = recorder.WoundClosure;
            if (statusText != null)
            {
                string hint = Time.unscaledTime < errorUntil ? lastError
                    : wound && suture != null && suture.PendingSite >= 0 ? $"Needle in at site {suture.PendingSite + 1}: close on its exit mark"
                    : wound && exercise.State == ExerciseState.Ready ? "Right = needle driver: close on a purple entry mark, then its exit mark" : "";
                statusText.text = wound
                    ? $"Wound closure  {exercise.State}  {exercise.ElapsedSeconds:F1}s  |  Stitches {recorder.StitchesPlaced}/{WoundPad.SiteCount}\n"
                        + $"Missed: {recorder.StitchesMissed}  Tissue contacts: {recorder.TissueContacts}\n{uploads?.Status}\n{hint}"
                    : $"{exercise.State}  {exercise.ElapsedSeconds:F1}s  |  {recorder.SuccessfulTransfers}/6\n"
                        + $"Errors: {recorder.Drops + recorder.InvalidReleases}\n{uploads?.Status}\n{hint}";
                if (uploads != null && uploads.Find(exercise.AttemptId) == null && exercise.State != ExerciseState.Ready && exercise.State != ExerciseState.Idle)
                    statusText.text += "\nLocal only: upload queue capacity reached.";
            }
            if (resultsText == null) return;
            if (exercise.State != ExerciseState.Completed) { resultsText.text = ""; return; }
            Metrics metrics = serverMetrics ?? recorder.LocalMetrics();
            var text = new StringBuilder(serverMetrics == null ? "Local results (awaiting upload)\n" : "Server results\n");
            text.AppendLine($"Practice score: {metrics.practice_score:F1}/100 | {metrics.completion_pct:F0}% complete");
            if (wound)
            {
                text.AppendLine($"Stitches placed: {metrics.successful_transfers}/{WoundPad.SiteCount} | Missed: {metrics.invalid_releases} | Tissue contacts: {metrics.drops}");
                text.AppendLine("Bite error: " + Millimetres(metrics.mean_bite_error_mm) + " (good <= 4 mm) | Spacing SD: " + Millimetres(metrics.spacing_sd_mm));
            }
            text.AppendLine($"Path: {metrics.total_distance_m:F2} m | Efficiency: {metrics.path_efficiency:P0}");
            text.AppendLine("Prototype practice rubric; not clinically validated.");
            if (cost == null) text.AppendLine("Itemized cost available after upload.");
            else
            {
                text.AppendLine($"Estimated total: ${cost.total_usd:F2}");
                if (cost.lines != null) foreach (CostLine line in cost.lines)
                {
                    text.AppendLine($"{line.label}: ${line.total_usd:F2}" + (line.assumed ? " (assumption)" : ""));
                    text.AppendLine(line.source);
                }
                text.AppendLine(cost.disclaimer);
                text.AppendLine("Error risk is an illustrative assumption, not a measured patient risk.");
            }
            resultsText.text = text.ToString();
        }
        private static string Millimetres(float value) => value >= 0 ? value.ToString("F1") + " mm" : "n/a";
        private void OnDestroy()
        {
            if (uploads != null) uploads.ResultsAvailable -= ShowResults;
            if (recorder != null) recorder.Recorded -= OnEvent;
            if (exercise != null) exercise.Reset -= ClearResults;
            if (startButton != null) startButton.onClick.RemoveListener(StartExercise);
            if (pauseButton != null) pauseButton.onClick.RemoveListener(TogglePause);
            if (resetButton != null) resetButton.onClick.RemoveListener(ResetExercise);
            if (retryUploadButton != null) retryUploadButton.onClick.RemoveListener(RetryUpload);
        }
    }
}
