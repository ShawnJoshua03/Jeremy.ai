using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    [Serializable] public sealed class InstructorCommand
    {
        public string commandId, type, targetId, expiresAt, message;
    }
    [Serializable] public sealed class CommandBatch { public InstructorCommand[] commands; }
    [Serializable] public sealed class CommandAck { public string status, message; }

    public sealed class CommandPoller : MonoBehaviour
    {
        [Tooltip("Poll GET /api/v1/sessions/{id}/commands for instructor commands. Turns itself off on 405.")]
        public bool pollingEnabled = true;
        public ApiClient api;
        public UploadQueue uploads;
        public ExerciseStateMachine exercise;
        public PegBoard board;
        public PegTransferRig rig; // optional: selects the wound closure target set
        public WoundPad pad;
        public Text annotationText;
        public string Status { get; private set; }
        private readonly Dictionary<string, CommandAck> applied = new Dictionary<string, CommandAck>();
        private string activeSession;
        private sealed class PendingAck { public string session, command; public CommandAck ack; }
        private readonly List<PendingAck> acknowledgements = new List<PendingAck>();
        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.75f);
                if (api == null || uploads == null || !api.IsConfigured) continue;
                // Retry acknowledgements even if request_retry changed the active local attempt.
                if (acknowledgements.Count > 0) yield return Acknowledge(acknowledgements[0]);
                if (!pollingEnabled) continue;
                PendingAttempt pending = uploads.Find(exercise.AttemptId);
                // After a reset or retry the new attempt has no server session until Start;
                // keep listening on the previous session so instructor notes still arrive.
                bool previousAttempt = pending == null || !pending.started;
                if (previousAttempt) pending = uploads.LatestStarted();
                if (pending == null) continue;
                string session = pending.sessionId;
                if (activeSession != session) { applied.Clear(); activeSession = session; }
                ApiResult response = null;
                yield return api.Send("GET", ApiClient.SessionPath(session) + "/commands", null, value => response = value);
                if (!response.Success)
                {
                    Status = "Commands unavailable (HTTP " + response.Status + ")";
                    if (response.Status == 405) pollingEnabled = false; // route missing; a 404 is per session
                    continue;
                }
                CommandBatch batch = null;
                try { batch = JsonUtility.FromJson<CommandBatch>(response.Body); }
                catch (Exception) { Status = "Invalid command payload"; }
                if (batch?.commands == null || batch.commands.Length > 100) continue;
                foreach (InstructorCommand command in batch.commands)
                {
                    if (command == null || string.IsNullOrWhiteSpace(command.commandId) || command.commandId.Length > 128) continue;
                    if (!applied.TryGetValue(command.commandId, out CommandAck ack))
                    {
                        if (applied.Count >= 1024) { Status = "Command deduplication capacity reached"; break; }
                        if (acknowledgements.Count >= 1024) { Status = "Command acknowledgement capacity reached"; break; }
                        // On the previous attempt's session only show notes and highlights; pause and
                        // retry were meant for an attempt that no longer exists.
                        bool harmless = command.type == "add_annotation" || command.type == "highlight_target";
                        ack = pending.attemptId == exercise.AttemptId || (previousAttempt && harmless)
                            ? Apply(command) : Reject("Attempt changed");
                        applied.Add(command.commandId, ack); // Remember before ack; retries must never repeat reset/pause.
                    }
                    PendingAck queued = acknowledgements.Find(x => x.session == session && x.command == command.commandId);
                    if (queued == null)
                    {
                        queued = new PendingAck { session = session, command = command.commandId, ack = ack };
                        acknowledgements.Add(queued);
                    }
                    yield return Acknowledge(queued);
                }
            }
        }
        private IEnumerator Acknowledge(PendingAck pending)
        {
            ApiResult result = null;
            yield return api.Send("POST", ApiClient.SessionPath(pending.session) + "/commands/" + Uri.EscapeDataString(pending.command) + "/ack",
                JsonUtility.ToJson(pending.ack), value => result = value);
            Status = result.Success ? "Command acknowledged" : "Acknowledgement pending";
            if (result.Success) acknowledgements.Remove(pending);
        }
        public CommandAck Apply(InstructorCommand command)
        {
            if (!DateTimeOffset.TryParse(command.expiresAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset expiry) || expiry <= DateTimeOffset.UtcNow) return Reject("Expired or invalid expiry");
            if (exercise.State == ExerciseState.Idle || exercise.State == ExerciseState.Cancelled) return Reject("No active exercise");
            bool wound = rig != null && rig.WoundClosure && pad != null;
            switch (command.type)
            {
                case "highlight_target":
                    if (!TryTarget(wound, command.targetId, out int index)) return Reject("Unknown target");
                    if (wound) pad.HighlightTarget(index); else board.HighlightTarget(index);
                    break;
                case "add_annotation":
                    if (!string.IsNullOrEmpty(command.targetId) && !TryTarget(wound, command.targetId, out _)) return Reject("Unknown target");
                    if (string.IsNullOrWhiteSpace(command.message) || command.message.Length > 500 || annotationText == null) return Reject("Invalid annotation");
                    annotationText.supportRichText = false;
                    annotationText.text = command.message; break;
                case "request_pause":
                    if (!exercise.Pause() && exercise.State != ExerciseState.Paused) return Reject("Cannot pause this state");
                    break;
                case "request_retry": exercise.ResetExercise(); break;
                default: return Reject("Unsupported command");
            }
            return new CommandAck { status = "applied", message = "Applied locally" };
        }
        // "site_N" in wound closure, "target_N" in peg transfer.
        private bool TryTarget(bool wound, string targetId, out int index) =>
            wound ? WoundPad.TryTargetIndex(targetId, out index) : board.TryTargetIndex(targetId, out index);
        private static CommandAck Reject(string reason) => new CommandAck { status = "rejected", message = reason };
    }
}
