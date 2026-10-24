using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    public sealed class CoachPanel : MonoBehaviour
    {
        public ApiClient api;
        public UploadQueue uploads;
        public ExerciseStateMachine exercise;
        public CoachAudioPlayer audioPlayer;
        public Text coachingText;
        public Button requestButton, stopSpeakingButton;
        public bool requestAfterCompletion;
        private string requestedAttempt;
        private bool busy;
        private void Start()
        {
            if (uploads != null) uploads.ResultsAvailable += OnResults;
            if (exercise != null) exercise.Reset += Clear;
            if (requestButton != null) requestButton.onClick.AddListener(RequestCoaching);
            if (stopSpeakingButton != null) stopSpeakingButton.onClick.AddListener(StopSpeaking);
        }
        private void OnResults(PendingAttempt pending)
        {
            if (requestAfterCompletion && pending.attemptId == exercise.AttemptId && requestedAttempt != pending.attemptId) RequestCoaching();
        }
        public void RequestCoaching()
        {
            if (busy || uploads == null || api == null) return;
            PendingAttempt pending = uploads.Find(exercise.AttemptId);
            if (pending == null || !pending.completed) { Show("Coaching is available after the attempt uploads. Local metrics remain available offline."); return; }
            StartCoroutine(Request(pending.attemptId, pending.sessionId));
        }
        private IEnumerator Request(string attempt, string session)
        {
            busy = true;
            requestedAttempt = attempt;
            Show("Requesting coaching...");
            ApiResult result = null;
            yield return api.Send("POST", ApiClient.SessionPath(session) + "/coach", "{}", value => result = value);
            busy = false;
            if (attempt != exercise.AttemptId) yield break;
            if (!result.Success) { Show("Coaching unavailable. Review the local metrics and try again later."); yield break; }
            try
            {
                Coaching feedback = JsonUtility.FromJson<Coaching>(result.Body);
                if (feedback == null || string.IsNullOrEmpty(feedback.summary)) throw new FormatException();
                var text = new StringBuilder(feedback.summary);
                if (feedback.tips != null) foreach (CoachingTip tip in feedback.tips) text.Append("\n• ").Append(tip.text);
                string spoken = text.ToString();
                if (feedback.next_exercise != null && !string.IsNullOrEmpty(feedback.next_exercise.title)) text.Append("\n\nNext drill: ").Append(feedback.next_exercise.title);
                Show(text.ToString());
                if (audioPlayer != null) audioPlayer.PlaySession(api, session, spoken);
            }
            catch (Exception) { Show("Coaching response unavailable. Local results are still available."); }
        }
        public void StopSpeaking() { if (audioPlayer != null) audioPlayer.StopSpeaking(); }
        private void Clear() { requestedAttempt = null; StopSpeaking(); Show(""); }
        private void Show(string value) { if (coachingText != null) coachingText.text = value; }
        private void OnDestroy()
        {
            if (uploads != null) uploads.ResultsAvailable -= OnResults;
            if (exercise != null) exercise.Reset -= Clear;
            if (requestButton != null) requestButton.onClick.RemoveListener(RequestCoaching);
            if (stopSpeakingButton != null) stopSpeakingButton.onClick.RemoveListener(StopSpeaking);
        }
    }
}
