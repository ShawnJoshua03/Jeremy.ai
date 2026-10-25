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
            if (recorder != null && recorder.LapChole) return; // the lap chole step panel gives its own feedback
            lastError = value.type == "drop" ? "Dropped: returned to source"
                : value.type == "tissue_contact" ? "Tissue contact: keep the tips off the wound edge"
                : value.type == "stitch_missed" ? "Missed bite: land within 8 mm of the left dot, then the right dot"
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
                string hint = Time.unscaledTime < errorUntil ? lastError : "";
                if (hint.Length == 0 && wound && suture != null)
                {
                    SutureGuide.Describe(suture, exercise, suture.instruments != null && suture.instruments.Length > 0 && suture.instruments[0] != null
                        && suture.instruments[0].desktopInput, out string step, out _);
                    hint = step;
                }
                if (recorder.LapChole)
                    statusText.text = $"Port placement  {exercise.State}  {exercise.ElapsedSeconds:F1}s  |  Steps {recorder.LapStepsCompleted}/{EventRecorder.LapSteps.Length}\n"
                        + $"Major errors: {recorder.LapMajor}  Minor: {recorder.LapMinor}\n{uploads?.Status}";
                else statusText.text = wound
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
            // The lap chole scene has no local score: port placement is scored on the server.
            string score = recorder.LapChole && serverMetrics == null ? "pending server scoring" : $"{metrics.practice_score:F1}/100";
            text.AppendLine($"Practice score: {score} | {metrics.completion_pct:F0}% complete");
            if (wound)
            {
                text.AppendLine($"Stitches placed: {metrics.successful_transfers}/{WoundPad.SiteCount} | Missed: {metrics.invalid_releases} | Tissue contacts: {metrics.drops}");
                text.AppendLine("Bite error: " + Millimetres(metrics.mean_bite_error_mm) + " (good <= 4 mm) | Spacing SD: " + Millimetres(metrics.spacing_sd_mm));
            }
            if (recorder.LapChole)
            {
                text.AppendLine($"Major errors: {metrics.drops} | Minor: {metrics.invalid_releases}");
                if (!string.IsNullOrEmpty(metrics.details_json)) text.AppendLine(LapDetails(metrics.details_json));
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
        // A few of the backend's port placement `details`, one line: ports, angles, vessel zone, sequence.
        private static string LapDetails(string json)
        {
            string Get(string key)
            {
                var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*([^,}]+)");
                return m.Success ? m.Groups[1].Value.Trim().Trim('"') : "-";
            }
            return $"Ports {Get("ports_placed")}/4 | mean error {Get("ports_mean_error_mm")} mm / {Get("ports_mean_angle_deg")}° | manipulation angle {Get("manipulation_angle_deg")}° | vessel-zone ports {Get("epigastric_zone_ports")} | sequence errors {Get("sequence_errors")}";
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
