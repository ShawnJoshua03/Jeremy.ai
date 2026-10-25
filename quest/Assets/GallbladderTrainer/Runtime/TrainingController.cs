using System.Collections;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GallbladderTrainer
{
    public enum PlacementMode { KeepAuthored, FrontOfXROrigin, FrontOfAnchor }

    [System.Serializable]
    public sealed class UiButton
    {
        public Button button;
        public Image background;
        public Text label;
    }

    /// <summary>
    /// Explore -> Assess -> Review state machine, UI binding, selection, overlays and local feedback.
    /// All references are assigned by WorkstationFactory; nothing needs manual Inspector wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingController : MonoBehaviour
    {
        [Header("Placement (relative to the existing XR rig)")]
        [SerializeField] PlacementMode placementMode = PlacementMode.FrontOfXROrigin;
        [Tooltip("Used when Placement Mode = FrontOfAnchor.")]
        [SerializeField] Transform placementAnchor;
        [SerializeField] float distanceFromUser = 1.15f;
        [Tooltip("Added to the rig/anchor Y to find the floor. 0 for floor-level tracking origins.")]
        [SerializeField] float floorOffsetY = 0f;
        [SerializeField] float placementDelaySeconds = 0.5f;

        [Header("Model controls")]
        [SerializeField] float rotateStepDegrees = 30f;
        [SerializeField] float rotateSpeed = 240f;
        [SerializeField] float scaleStep = 1.2f;
        [SerializeField] float minScale = 0.6f;
        [SerializeField] float maxScale = 1.8f;
        [SerializeField] float scaleSpeed = 2f;
        [SerializeField] float poseSpeed = 2.5f;

        [Header("Optional backend hook (MonoBehaviour implementing ITrainingBackendHook)")]
        [SerializeField] MonoBehaviour backendHook;

        // ---- references assigned by WorkstationFactory ----
        [SerializeField] internal Transform anatomyRoot;
        [SerializeField] internal Transform gallbladderPivot;
        [SerializeField] internal Transform labelRoot;
        [SerializeField] internal StructureView[] structures;
        [SerializeField] internal StructureView plateView;
        [SerializeField] internal Renderer triangleOverlay;
        [SerializeField] internal Material plateShownMaterial;
        [SerializeField] internal Material plateMissingMaterial;
        [SerializeField] internal Text taskHeader, taskTitle, taskBody;
        [SerializeField] internal Text answersStatus, answersNote;
        [SerializeField] internal Text feedbackHeader, feedbackBody, feedbackFooter;
        [SerializeField] internal Text badgeText, warningText, labelText;
        [SerializeField] internal Image badgeBorder;
        [SerializeField] internal GameObject warningRoot;
        [SerializeField] internal LineRenderer labelLine;
        [SerializeField] internal UiButton modeExplore, modeAssess, modeReview, prevButton, nextButton;
        [SerializeField] internal UiButton rotateLeft, rotateRight, larger, smaller, resetButton, labelsButton, overlaysButton, demoButton;
        [SerializeField] internal UiButton submitButton, demonstratedButton, notDemonstratedButton, requestButton, retryButton;

        static readonly Quaternion SeparatedPose = Quaternion.Euler(10f, 0f, 40f);
        static readonly Color ColButton = new Color(0.17f, 0.23f, 0.275f);
        static readonly Color ColActive = new Color(0.08f, 0.42f, 0.50f);
        static readonly Color ColText = new Color(0.95f, 0.96f, 0.97f);
        static readonly Color ColTextDim = new Color(0.45f, 0.5f, 0.55f);
        static readonly Color ColBadgeNone = new Color(0.20f, 0.255f, 0.30f);
        static readonly Color ColBadgeDemo = new Color(0.95f, 0.72f, 0.29f);
        static readonly Color ColBadgeRemote = new Color(0.24f, 0.86f, 1f);
        const string ModeHintExplore = "EXPLORE: select any structure to read its name and description. Rotate or resize the model with the controls below. Overlays can be toggled.";
        const string ModeHintAssess = "ASSESS: names and teaching overlays are hidden. Answer on the right when you are ready.";
        const string ModeHintReview = "REVIEW: relevant features are revealed. Compare them with your answer. Retry returns to Assess.";

        readonly StringBuilder sb = new StringBuilder(512);
        Exercise exercise;
        int exerciseIndex;
        TrainingMode mode = TrainingMode.Explore;
        StructureView selected, hovered;
        string instructorId, instructorNote;
        CommandSource instructorSource;
        bool labelsOn = true, overlaysOn = true, submitted;
        ResultStatus lastResult;
        string lastMessage, externalText, externalSource;
        float targetYaw, currentYaw, targetScale = 1f, currentScale = 1f, poseT, targetPoseT;
        Camera cam;
        Canvas[] canvases;
        bool canvasCameraAssigned;
        float nextIntegrationCheck;
        int lastIntegrationFlags = -1;
        bool valid;

        Camera Cam { get { if (cam == null) cam = Camera.main; return cam; } }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            valid = ValidateReferences();
            if (!valid) { enabled = false; return; }

            canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < structures.Length; i++)
            {
                structures[i].Hovered += v => NotifyHover(v, true);
                structures[i].Unhovered += v => NotifyHover(v, false);
                structures[i].Picked += NotifyPick;
            }

            Bind(modeExplore, () => SetMode(TrainingMode.Explore));
            Bind(modeAssess, () => SetMode(TrainingMode.Assess));
            Bind(modeReview, () => SetMode(TrainingMode.Review));
            Bind(prevButton, () => SetExercise(exerciseIndex - 1));
            Bind(nextButton, () => SetExercise(exerciseIndex + 1));
            Bind(rotateLeft, () => targetYaw += rotateStepDegrees);
            Bind(rotateRight, () => targetYaw -= rotateStepDegrees);
            Bind(larger, () => targetScale = Mathf.Clamp(targetScale * scaleStep, minScale, maxScale));
            Bind(smaller, () => targetScale = Mathf.Clamp(targetScale / scaleStep, minScale, maxScale));
            Bind(resetButton, ResetView);
            Bind(labelsButton, () => { labelsOn = !labelsOn; RefreshAll(); });
            Bind(overlaysButton, () => { if (mode == TrainingMode.Explore) { overlaysOn = !overlaysOn; RefreshAll(); } });
            Bind(demoButton, ToggleDemoInstructor);
            Bind(submitButton, SubmitSelection);
            Bind(demonstratedButton, () => SubmitDecision(DecisionIds.CriteriaDemonstrated));
            Bind(notDemonstratedButton, () => SubmitDecision(DecisionIds.CriteriaNotDemonstrated));
            Bind(requestButton, () => SubmitDecision(DecisionIds.RequestInstructor));
            Bind(retryButton, Retry);
        }

        void OnEnable()
        {
            TrainingEvents.InstructorCommandReceived += OnInstructorCommand;
            TrainingEvents.ExternalFeedbackReceived += OnExternalFeedback;
        }

        void OnDisable()
        {
            TrainingEvents.InstructorCommandReceived -= OnInstructorCommand;
            TrainingEvents.ExternalFeedbackReceived -= OnExternalFeedback;
        }

        void Start()
        {
            if (!valid) return;
            AssignCanvasCamera();
            SetExercise(0, true);
            CheckIntegration(true);
            if (placementMode != PlacementMode.KeepAuthored) StartCoroutine(PlaceRoutine());
        }

        void Update()
        {
            if (!valid) return;
            if (!Mathf.Approximately(currentYaw, targetYaw))
            {
                currentYaw = Mathf.MoveTowards(currentYaw, targetYaw, rotateSpeed * Time.deltaTime);
                anatomyRoot.localRotation = Quaternion.Euler(0f, currentYaw, 0f);
            }
            if (!Mathf.Approximately(currentScale, targetScale))
            {
                currentScale = Mathf.MoveTowards(currentScale, targetScale, scaleSpeed * Time.deltaTime);
                anatomyRoot.localScale = Vector3.one * currentScale;
            }
            if (!Mathf.Approximately(poseT, targetPoseT))
            {
                poseT = Mathf.MoveTowards(poseT, targetPoseT, poseSpeed * Time.deltaTime);
                gallbladderPivot.localRotation = Quaternion.Slerp(Quaternion.identity, SeparatedPose, poseT);
            }
            if (Time.unscaledTime >= nextIntegrationCheck)
            {
                nextIntegrationCheck = Time.unscaledTime + 3f;
                AssignCanvasCamera();
                CheckIntegration(false);
            }
        }

        void LateUpdate()
        {
            if (!valid || selected == null || !labelRoot.gameObject.activeSelf) return;
            Camera c = Cam;
            if (c == null) return;
            Vector3 anchor = selected.LabelAnchor.position;
            Vector3 camPos = c.transform.position;
            Vector3 p = anchor + Vector3.up * 0.07f + (camPos - anchor).normalized * 0.06f;
            labelRoot.position = p;
            Vector3 look = p - camPos;
            if (look.sqrMagnitude > 1e-6f) labelRoot.rotation = Quaternion.LookRotation(look, Vector3.up);
            labelLine.SetPosition(0, anchor);
            labelLine.SetPosition(1, p);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Re-places the workstation in front of the user (uses the Placement settings).</summary>
        public void Recenter() { ApplyPlacement(); }

        // ------------------------------------------------------------------ interaction entry points

        internal void NotifyHover(StructureView v, bool entered)
        {
            if (v == null) return;
            if (entered) hovered = v;
            else if (hovered == v) hovered = null;
            TrainingEvents.RaiseStructureHovered(v.Id, entered);
            RefreshHighlights();
        }

        internal void NotifyPick(StructureView v)
        {
            if (v == null || !v.IsPickable) return;
            selected = v;
            TrainingEvents.RaiseStructureSelected(v.Id);
            var hook = backendHook as ITrainingBackendHook;
            if (hook != null) hook.OnStructureSelected(v.Id);
            RefreshHighlights();
            RefreshLabel();
            RefreshAnswersPanel();
            RefreshFeedback();
        }

        // ------------------------------------------------------------------ state changes

        void SetExercise(int index, bool immediate = false)
        {
            int n = ExerciseLibrary.All.Length;
            exerciseIndex = ((index % n) + n) % n;
            exercise = ExerciseLibrary.All[exerciseIndex];
            selected = null; hovered = null;
            ClearInstructor();
            ClearResult();
            targetPoseT = exercise.LowerThirdSeparated ? 1f : 0f;
            if (immediate)
            {
                poseT = targetPoseT;
                gallbladderPivot.localRotation = Quaternion.Slerp(Quaternion.identity, SeparatedPose, poseT);
            }
            if (mode == TrainingMode.Review && exercise.Kind == ExerciseKind.Identify) selected = FindView(exercise.TargetStructureId);
            TrainingEvents.RaiseExerciseChanged(exercise.Id);
            RefreshAll();
        }

        void SetMode(TrainingMode m)
        {
            mode = m;
            if (m != TrainingMode.Review) ClearResult();
            externalText = null;
            selected = null;
            if (m == TrainingMode.Review && exercise.Kind == ExerciseKind.Identify) selected = FindView(exercise.TargetStructureId);
            TrainingEvents.RaiseModeChanged(mode);
            RefreshAll();
        }

        void ResetView()
        {
            currentYaw = Mathf.Repeat(currentYaw + 180f, 360f) - 180f;
            targetYaw = 0f;
            targetScale = 1f;
            labelsOn = true; overlaysOn = true;
            selected = null; hovered = null;
            ClearInstructor();
            externalText = null;
            if (mode == TrainingMode.Review && exercise.Kind == ExerciseKind.Identify) selected = FindView(exercise.TargetStructureId);
            RefreshAll();
        }

        void Retry()
        {
            ClearResult();
            externalText = null;
            selected = null;
            mode = TrainingMode.Assess;
            TrainingEvents.RaiseModeChanged(mode);
            RefreshAll();
        }

        void ClearResult() { submitted = false; lastResult = ResultStatus.None; lastMessage = null; }
        void ClearInstructor() { instructorId = null; instructorNote = null; }

        // ------------------------------------------------------------------ assessment

        void SubmitSelection()
        {
            if (mode != TrainingMode.Assess || submitted || exercise.Kind != ExerciseKind.Identify || selected == null) return;
            bool ok = selected.Id == exercise.TargetStructureId;
            lastResult = ok ? ResultStatus.Match : ResultStatus.Mismatch;
            lastMessage = ok ? exercise.FeedbackMatch : selected.DisplayName + " was selected. " + exercise.FeedbackMismatch;
            Finish(string.Empty);
        }

        void SubmitDecision(string decisionId)
        {
            if (mode != TrainingMode.Assess || submitted) return;
            if (decisionId == DecisionIds.RequestInstructor)
            {
                lastResult = ResultStatus.Unclear;
                lastMessage = "No result recorded. This build has no connected instructor; the request was raised as a local event for future integration.";
            }
            else if (exercise.Kind == ExerciseKind.Cvs)
            {
                bool ok = decisionId == exercise.ExpectedDecisionId;
                lastResult = ok ? ResultStatus.Match : ResultStatus.Mismatch;
                lastMessage = "You chose: " + ExerciseLibrary.DecisionLabel(decisionId) + ". " + (ok ? exercise.FeedbackMatch : exercise.FeedbackMismatch);
            }
            else return;
            Finish(decisionId);
        }

        void Finish(string decisionId)
        {
            submitted = true;
            var e = new DecisionEvent
            {
                ExerciseId = exercise.Id, DecisionId = decisionId,
                SelectedStructureId = selected != null ? selected.Id : null,
                Mode = mode, Result = lastResult, TimestampUtc = System.DateTime.UtcNow
            };
            TrainingEvents.RaiseDecision(e);
            var hook = backendHook as ITrainingBackendHook;
            if (hook != null) hook.OnDecisionSubmitted(e);
            RefreshAll();
        }

        // ------------------------------------------------------------------ instructor / external hooks

        void ToggleDemoInstructor()
        {
            var c = new InstructorCommand { Source = CommandSource.LocalDemo };
            if (instructorId == null)
            {
                c.StructureId = exercise.InstructorFocusId;
                c.Note = exercise.InstructorNote;
            }
            else c.Clear = true;
            TrainingEvents.SendInstructorCommand(c);
        }

        void OnInstructorCommand(InstructorCommand c)
        {
            if (!valid) return;
            if (c.Clear) { ClearInstructor(); RefreshAll(); return; }
            if (FindView(c.StructureId) == null)
            {
                Debug.LogWarning("[GallbladderTrainer] Unknown structure id in instructor command: " + c.StructureId);
                return;
            }
            instructorId = c.StructureId; instructorNote = c.Note; instructorSource = c.Source;
            RefreshAll();
        }

        void OnExternalFeedback(ExternalFeedback f)
        {
            if (!valid) return;
            externalText = f.Text;
            externalSource = string.IsNullOrEmpty(f.SourceLabel) ? "external" : f.SourceLabel;
            RefreshFeedback();
        }

        // ------------------------------------------------------------------ refresh

        void RefreshAll()
        {
            ApplyOverlayVisibility();
            RefreshHighlights();
            RefreshLabel();
            RefreshTaskPanel();
            RefreshAnswersPanel();
            RefreshFeedback();
            RefreshControls();
            RefreshBadge();
        }

        void ApplyOverlayVisibility()
        {
            bool show = mode == TrainingMode.Review || (mode == TrainingMode.Explore && overlaysOn);
            triangleOverlay.enabled = show;
            plateView.SetVisible(show);
            plateView.SetPickable(show);
            bool missing = mode == TrainingMode.Review && !exercise.CriteriaShown[1];
            plateView.OverrideBaseMaterial(missing ? plateMissingMaterial : plateShownMaterial);
            if (!show)
            {
                if (selected == plateView) selected = null;
                if (hovered == plateView) hovered = null;
            }
        }

        void RefreshHighlights()
        {
            for (int i = 0; i < structures.Length; i++)
            {
                StructureView s = structures[i];
                HighlightState st = HighlightState.None;
                if (s == selected) st = HighlightState.Selected;
                else if (instructorId != null && s.Id == instructorId) st = HighlightState.Instructor;
                else if (s == hovered) st = HighlightState.Hover;
                s.SetHighlight(st);
            }
        }

        void RefreshLabel()
        {
            bool show = labelsOn && selected != null;
            labelRoot.gameObject.SetActive(show);
            labelLine.enabled = show;
            if (show) labelText.text = mode == TrainingMode.Assess ? "Selected structure" : selected.DisplayName;
        }

        void RefreshTaskPanel()
        {
            taskHeader.text = "TASK  |  EXERCISE " + (exerciseIndex + 1) + " OF " + ExerciseLibrary.All.Length;
            taskTitle.text = mode == TrainingMode.Review ? exercise.ReviewName : exercise.Title;
            string hint = mode == TrainingMode.Explore ? ModeHintExplore : (mode == TrainingMode.Assess ? ModeHintAssess : ModeHintReview);
            taskBody.text = exercise.Task + "\n\n" + hint;
            SetToggle(modeExplore, mode == TrainingMode.Explore);
            SetToggle(modeAssess, mode == TrainingMode.Assess);
            SetToggle(modeReview, mode == TrainingMode.Review);
        }

        void RefreshAnswersPanel()
        {
            bool cvs = exercise.Kind == ExerciseKind.Cvs;
            bool canAnswer = mode == TrainingMode.Assess && !submitted;
            submitButton.button.gameObject.SetActive(!cvs);
            demonstratedButton.button.gameObject.SetActive(cvs);
            notDemonstratedButton.button.gameObject.SetActive(cvs);
            var rt = (RectTransform)requestButton.button.transform;
            rt.anchoredPosition = new Vector2(28f, cvs ? -424f : -324f);

            if (cvs) answersStatus.text = "Choose the answer that fits the prepared view.";
            else if (selected == null) answersStatus.text = "Selected: nothing yet.\nUse the controller ray and trigger to select a structure.";
            else answersStatus.text = mode == TrainingMode.Assess ? "Selected: one structure (name hidden in Assess)." : "Selected: " + selected.DisplayName;

            if (mode != TrainingMode.Assess) answersNote.text = "Answers are enabled in ASSESS mode.";
            else answersNote.text = submitted ? "Answer submitted. Use Retry, or switch to REVIEW." : "Ready to answer.";

            SetEnabled(submitButton, canAnswer && selected != null);
            SetEnabled(demonstratedButton, canAnswer);
            SetEnabled(notDemonstratedButton, canAnswer);
            SetEnabled(requestButton, canAnswer);
        }

        void RefreshFeedback()
        {
            feedbackHeader.text = externalText != null
                ? "FEEDBACK  |  Source: " + externalSource
                : "FEEDBACK  |  Local authored feedback (not live AI)";
            sb.Length = 0;
            if (externalText != null) sb.Append(externalText);
            else if (mode == TrainingMode.Explore)
            {
                if (selected != null) sb.Append(selected.DisplayName).Append('\n').Append(selected.Description);
                else sb.Append("Select a structure to read about it. Turn Overlays on to see the teaching regions.");
            }
            else if (mode == TrainingMode.Assess)
            {
                if (submitted) AppendResult();
                else sb.Append("Names and teaching overlays are hidden. Answer on the right when ready.");
            }
            else AppendReview();

            if (instructorId != null && externalText == null && (mode != TrainingMode.Review || !submitted))
            {
                sb.Append("\n\n");
                if (instructorSource == CommandSource.LocalDemo) sb.Append("DEMO instructor note (local simulation, not a connected instructor): ");
                else sb.Append("Instructor note: ");
                sb.Append(instructorNote);
            }
            feedbackBody.text = sb.ToString();
            feedbackFooter.text = ExerciseLibrary.Disclaimer + "\n" + ExerciseLibrary.SchematicNote;
            SetEnabled(retryButton, submitted || mode == TrainingMode.Review);
        }

        void AppendResult()
        {
            string color; string icon; string word;
            switch (lastResult)
            {
                case ResultStatus.Match:
                    color = "#7DDC9C"; icon = "[OK] ";
                    word = exercise.Kind == ExerciseKind.Identify ? "CORRECT STRUCTURE" : "MATCHES THE PREPARED EXERCISE"; break;
                case ResultStatus.Mismatch:
                    color = "#FF9A76"; icon = "[X] ";
                    word = exercise.Kind == ExerciseKind.Identify ? "NOT THE TARGET STRUCTURE" : "DOES NOT MATCH THE PREPARED EXERCISE"; break;
                default:
                    color = "#F2B84B"; icon = "[?] "; word = "UNCLEAR; INSTRUCTOR REVIEW REQUESTED"; break;
            }
            sb.Append("<color=").Append(color).Append("><b>").Append(icon).Append(word).Append("</b></color>\n").Append(lastMessage);
        }

        void AppendReview()
        {
            if (submitted) { AppendResult(); sb.Append("\n\n"); }
            else sb.Append("Reviewed without submitting an answer.\n");
            if (exercise.Kind == ExerciseKind.Cvs)
            {
                for (int i = 0; i < 3; i++)
                {
                    sb.Append(i + 1).Append(". ").Append(ExerciseLibrary.CriteriaText[i]).Append(": ")
                      .Append(exercise.CriteriaShown[i] ? "<color=#7DDC9C>[OK] SHOWN</color>" : "<color=#FF9A76>[X] NOT SHOWN</color>").Append('\n');
                }
                sb.Append("Prepared answer: ").Append(ExerciseLibrary.DecisionLabel(exercise.ExpectedDecisionId));
            }
            else
            {
                StructureView t = FindView(exercise.TargetStructureId);
                sb.Append("Target: ").Append(t != null ? t.DisplayName : exercise.TargetStructureId);
                if (t != null) sb.Append('\n').Append(t.Description);
            }
        }

        void RefreshControls()
        {
            labelsButton.label.text = labelsOn ? "Labels: On" : "Labels: Off";
            SetToggle(labelsButton, labelsOn);
            if (mode == TrainingMode.Explore) overlaysButton.label.text = overlaysOn ? "Overlays: On" : "Overlays: Off";
            else overlaysButton.label.text = mode == TrainingMode.Assess ? "Overlays: Hidden" : "Overlays: Shown";
            SetEnabled(overlaysButton, mode == TrainingMode.Explore);
            SetToggle(overlaysButton, mode == TrainingMode.Explore ? overlaysOn : mode == TrainingMode.Review);
            SetToggle(demoButton, instructorId != null);
        }

        void RefreshBadge()
        {
            string line2; Color border;
            if (instructorId == null) { line2 = "Instructor: none connected (local build)"; border = ColBadgeNone; }
            else if (instructorSource == CommandSource.LocalDemo) { line2 = "DEMO instructor highlight: local simulation"; border = ColBadgeDemo; }
            else { line2 = "Instructor highlight: remote command"; border = ColBadgeRemote; }
            badgeText.text = "MODE: " + mode.ToString().ToUpperInvariant() + "\n" + line2;
            badgeBorder.color = border;
        }

        // ------------------------------------------------------------------ XR integration + placement

        void CheckIntegration(bool force)
        {
            int flags = 0;
            if (FindAnyObjectByType<XRRayInteractor>() == null) flags |= 1;
            if (EventSystem.current == null) flags |= 2;
            else if (FindAnyObjectByType<XRUIInputModule>() == null) flags |= 4;
            if (!force && flags == lastIntegrationFlags) return;
            lastIntegrationFlags = flags;
            if (flags == 0) { warningRoot.SetActive(false); return; }
            sb.Length = 0;
            sb.Append("XR INTEGRATION WARNING\n");
            if ((flags & 1) != 0) sb.Append("No XR ray interactor found: controller-ray selection is unavailable (desktop mouse still works in the Editor).\n");
            if ((flags & 2) != 0) sb.Append("No EventSystem found: UI buttons will not respond. Add one with an XR UI Input Module.\n");
            if ((flags & 4) != 0) sb.Append("EventSystem has no XRUIInputModule: controller-ray clicks on UI buttons will not work.\n");
            warningText.text = sb.ToString().TrimEnd();
            warningRoot.SetActive(true);
        }

        void AssignCanvasCamera()
        {
            if (canvasCameraAssigned) return;
            Camera c = Cam;
            if (c == null) return;
            for (int i = 0; i < canvases.Length; i++) canvases[i].worldCamera = c;
            canvasCameraAssigned = true;
        }

        IEnumerator PlaceRoutine()
        {
            if (placementDelaySeconds > 0f) yield return new WaitForSeconds(placementDelaySeconds);
            ApplyPlacement();
        }

        void ApplyPlacement()
        {
            Transform anchor = null; Camera head = null;
            if (placementMode == PlacementMode.FrontOfAnchor) anchor = placementAnchor;
            else if (placementMode == PlacementMode.FrontOfXROrigin)
            {
                XROrigin origin = FindAnyObjectByType<XROrigin>();
                if (origin != null) { anchor = origin.transform; head = origin.Camera; }
            }
            if (anchor == null) return;
            Transform headT = head != null ? head.transform : (Cam != null ? Cam.transform : anchor);
            Vector3 fwd = Vector3.ProjectOnPlane(headT.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = headT.position + fwd * distanceFromUser;
            pos.y = anchor.position.y + floorOffsetY;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        }

        // ------------------------------------------------------------------ helpers

        StructureView FindView(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < structures.Length; i++) if (structures[i].Id == id) return structures[i];
            return null;
        }

        static void Bind(UiButton b, UnityAction a) { b.button.onClick.AddListener(a); }

        static void SetEnabled(UiButton b, bool on)
        {
            b.button.interactable = on;
            b.label.color = on ? ColText : ColTextDim;
        }

        static void SetToggle(UiButton b, bool active)
        {
            ColorBlock cb = b.button.colors;
            cb.normalColor = active ? ColActive : ColButton;
            cb.selectedColor = cb.normalColor;
            b.button.colors = cb;
        }

        bool ValidateReferences()
        {
            bool ok = anatomyRoot != null && gallbladderPivot != null && labelRoot != null && structures != null && structures.Length > 0
                && plateView != null && triangleOverlay != null && plateShownMaterial != null && plateMissingMaterial != null
                && taskHeader != null && taskTitle != null && taskBody != null && answersStatus != null && answersNote != null
                && feedbackHeader != null && feedbackBody != null && feedbackFooter != null && badgeText != null && warningText != null
                && labelText != null && badgeBorder != null && warningRoot != null && labelLine != null
                && Ok(modeExplore) && Ok(modeAssess) && Ok(modeReview) && Ok(prevButton) && Ok(nextButton)
                && Ok(rotateLeft) && Ok(rotateRight) && Ok(larger) && Ok(smaller) && Ok(resetButton) && Ok(labelsButton)
                && Ok(overlaysButton) && Ok(demoButton) && Ok(submitButton) && Ok(demonstratedButton)
                && Ok(notDemonstratedButton) && Ok(requestButton) && Ok(retryButton);
            if (ok) for (int i = 0; i < structures.Length; i++) if (structures[i] == null) ok = false;
            if (!ok) Debug.LogError("[GallbladderTrainer] TrainingController has missing references. Re-run Tools > Gallbladder Trainer > Build Scene.", this);
            return ok;
        }

        static bool Ok(UiButton b) { return b != null && b.button != null && b.label != null; }
    }
}
