using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Jeremy
{
    // Attach once to an empty object. Assign the Meta Building Blocks camera and controller anchors.
    // This creates exercise content only; XR origin, passthrough and ray UI stay owned by the installed SDK.
    public sealed class PegTransferRig : MonoBehaviour
    {
        public Transform head, leftController, rightController;
        public OVRHand leftHand, rightHand; // optional: set for hand tracking, leave empty for controllers
        public OVRSkeleton leftSkeleton, rightSkeleton;
        public string traineeId = "demo-trainee";
        public string backendOrigin = "";
        public bool allowEditorHttp;
        [Tooltip("Testing only: let the headset use http://<PC LAN IP>:8000. Leave off for HTTPS deployments.")]
        public bool allowLanHttp;
        [Tooltip("Backend API_KEY, if the backend sets one. Sent as X-API-Key.")]
        public string backendApiKey = "";
        public bool createHud = true;
        public PegBoard board;
        public const string PegTransferId = "peg_transfer";
        [Tooltip("Exercise at start: peg_transfer or wound_closure. Change at runtime with SetExercise.")]
        [SerializeField] private string exercise = PegTransferId;
        /// <summary>Active exercise id: "peg_transfer" or "wound_closure".</summary>
        public string ExerciseId => exercise == WoundPad.ExerciseId ? WoundPad.ExerciseId : exercise == LapCholeId ? LapCholeId : PegTransferId;
        public const string LapCholeId = "lap_chole"; // laparoscopic cholecystectomy (docs/LAP_CHOLE.md); anatomy only so far
        public bool LapChole => ExerciseId == LapCholeId;
        public bool WoundClosure => ExerciseId == WoundPad.ExerciseId;
        /// <summary>Raised after SetExercise switched exercise and the board content was swapped (argument: new id).</summary>
        public event System.Action<string> ExerciseChanged;
        public static bool IsKnownExercise(string id) => id == PegTransferId || id == WoundPad.ExerciseId || id == LapCholeId;
        [HideInInspector] public bool desktopPreview; // set by DesktopPreview before Awake
        public InstrumentController[] Instruments => instruments;
        public ExerciseStateMachine Exercise { get; private set; }
        public EventRecorder Recorder { get; private set; }
        public UploadQueue Uploads { get; private set; }
        /// <summary>Wound closure content, a child of the board at its origin (board-local frame). Inactive in peg transfer.</summary>
        public WoundPad Pad { get; private set; }
        public SutureDetector Suture { get; private set; }
        private InstrumentController[] instruments;
        private PoseSampler sampler;

        [Tooltip("In the editor or a desktop build with no headset, drive the instruments with mouse and keyboard.")]
        public bool desktopPreviewWithoutHeadset = true;

        private void Awake()
        {
            if (desktopPreviewWithoutHeadset && Application.platform != RuntimePlatform.Android
                && !UnityEngine.XR.XRSettings.isDeviceActive && GetComponent<DesktopPreview>() == null)
                gameObject.AddComponent<DesktopPreview>(); // its Awake swaps in a camera and mouse-driven poses
            if (desktopPreview) exercise = LapCholeId; // the desktop preview is the one lap chole scene
            // Tracked hands are saved inactive (QuestBuild). Match their skeleton/mesh type to the runtime's OpenXR hand
            // skeleton first, then wake them, so OVRSkeleton finds its tracking source in Awake (else the hand stays a
            // frozen, rotated mesh).
            foreach (OVRHand trackedHand in new[] { leftHand, rightHand })
            {
                if (trackedHand == null) continue;
                trackedHand.OnValidate();
                trackedHand.gameObject.SetActive(true);
            }
            Exercise = gameObject.AddComponent<ExerciseStateMachine>();
            Recorder = gameObject.AddComponent<EventRecorder>();
            Recorder.exercise = Exercise;
            if (board == null)
            {
                var boardObject = new GameObject("Peg board");
                boardObject.transform.SetParent(transform, false);
                board = boardObject.AddComponent<PegBoard>();
            }
            board.exercise = Exercise;
            board.Build();
            var padObject = new GameObject("Wound pad");
            padObject.transform.SetParent(board.transform, false);
            Pad = padObject.AddComponent<WoundPad>();
            Pad.Build(board);
            if (head == null && Camera.main != null) head = Camera.main.transform;
            board.Recenter(head);
            var detector = gameObject.AddComponent<TransferDetector>();
            detector.exercise = Exercise; detector.board = board; detector.recorder = Recorder;
            instruments = new[] { CreateInstrument("left", leftController, leftHand, leftSkeleton, detector), CreateInstrument("right", rightController, rightHand, rightSkeleton, detector) };
            Suture = gameObject.AddComponent<SutureDetector>();
            Suture.exercise = Exercise; Suture.board = board; Suture.pad = Pad; Suture.recorder = Recorder; Suture.instruments = instruments;
            sampler = gameObject.AddComponent<PoseSampler>();
            sampler.exercise = Exercise; sampler.board = board; sampler.recorder = Recorder; sampler.instruments = instruments;
            var api = gameObject.AddComponent<ApiClient>();
            api.baseUrl = backendOrigin; api.allowEditorHttp = allowEditorHttp; api.allowLanHttp = allowLanHttp; api.apiKey = backendApiKey;
            Uploads = gameObject.AddComponent<UploadQueue>();
            Uploads.api = api;
            Uploads.Initialize();
            Recorder.Recorded += OnEvent;
            Recorder.Sampled += OnSample;
            Exercise.Reset += OnReset;
            Exercise.Changed += OnState;
            var hud = gameObject.AddComponent<HudController>();
            hud.exercise = Exercise; hud.recorder = Recorder; hud.uploads = Uploads; hud.suture = Suture;
            var coach = gameObject.AddComponent<CoachPanel>();
            coach.api = api; coach.uploads = Uploads; coach.exercise = Exercise;
            coach.audioPlayer = gameObject.AddComponent<CoachAudioPlayer>();
            gameObject.AddComponent<LapCholeProcedure>().rig = this; // guided lap chole steps (idle in other exercises)
            gameObject.AddComponent<LapCholeNarrator>(); // dynamic Cartesia narration for lap chole (idle in other exercises)
            var commands = gameObject.AddComponent<CommandPoller>();
            commands.api = api; commands.uploads = Uploads; commands.exercise = Exercise; commands.board = board;
            commands.rig = this; commands.pad = Pad;
            if (createHud) BuildHud(hud, coach, commands);
            ApplyExercise();
            Exercise.ResetExercise();
        }
        /// <summary>Switch exercise: cancels any running attempt (the upload queue closes its session), swaps the
        /// board content and starts a fresh Ready attempt. Returns false for an unknown id.</summary>
        public bool SetExercise(string id)
        {
            if (!IsKnownExercise(id)) return false;
            bool changed = id != ExerciseId;
            exercise = id;
            if (Exercise != null)
            {
                Exercise.Cancel(); // under the old content, so grips release and the attempt uploads as cancelled
                ApplyExercise();
                Exercise.ResetExercise();
            }
            if (changed) ExerciseChanged?.Invoke(id);
            return true;
        }
        private void ApplyExercise()
        {
            bool wound = WoundClosure;
            Recorder.exerciseId = ExerciseId;
            board.SetPegContentVisible(!wound && !LapChole, !LapChole);
            Pad.gameObject.SetActive(wound);
            Suture.enabled = wound;
            foreach (InstrumentController instrument in instruments) instrument.pegsEnabled = !wound && !LapChole;
            // Headset: lap chole is an immersive operating room (passthrough off); other exercises use passthrough.
            if (!desktopPreview) LapCholeImmersive.Ensure(this).SetActive(LapChole);
        }
        public void ToggleExercise() => SetExercise(WoundClosure ? PegTransferId : WoundPad.ExerciseId);
        private bool placedWithTracking;
        private float autoStartAt = -1f;
        private void Update()
        {
            if (!desktopPreview && Exercise != null)
            {
                // At launch the head reads the tracking origin; place the board (and its HUD) again once it's real.
                if (!placedWithTracking && head != null && head.position.y > 0.8f)
                {
                    placedWithTracking = true;
                    if (!LapChole) board.Recenter(head);
                    autoStartAt = Time.time + 3f;
                }
                // Start without reaching for the panel: A or X on the controllers, or automatically after placement.
                bool startPressed = OVRInput.GetDown(OVRInput.Button.One) || OVRInput.GetDown(OVRInput.Button.Three);
                bool auto = autoStartAt > 0f && Time.time > autoStartAt;
                if ((startPressed || auto) && Exercise.State == ExerciseState.Ready) { Exercise.Begin(); autoStartAt = -1f; }
                else if (startPressed && Exercise.State == ExerciseState.Paused) Exercise.Resume();
                if (OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four)) { if (!LapChole) board.Recenter(head); else LapCholeImmersive.Ensure(this).Recenter(head); }
            }
            // Desktop preview only (DesktopPreview owns the other keys): 3 = lap chole (the one preview scene).
            Keyboard keys = desktopPreview ? Keyboard.current : null;
            if (keys == null) return;
            if (!LapChole && (keys.digit3Key.wasPressedThisFrame || keys.numpad3Key.wasPressedThisFrame)) SetExercise(LapCholeId);
        }
        private InstrumentController CreateInstrument(string side, Transform anchor, OVRHand hand, OVRSkeleton skeleton, TransferDetector detector)
        {
            var tool = new GameObject(side + " instrument");
            tool.SetActive(false);
            tool.transform.SetParent(transform, false);
            var instrument = tool.AddComponent<InstrumentController>();
            instrument.instrument = side; instrument.controllerPose = anchor; instrument.hand = hand; instrument.skeleton = skeleton;
            instrument.exercise = Exercise; instrument.board = board; instrument.detector = detector;
            instrument.desktopInput = desktopPreview;
            GameObject tip = new GameObject("Tip");
            tip.transform.SetParent(tool.transform, false);
            MakeToolPart(tip.transform, "Shaft", new Vector3(0, 0, -0.075f), new Vector3(0.006f, 0.006f, 0.12f));
            instrument.leftJaw = MakeJaw(tip.transform, "Left jaw", -0.004f);
            instrument.rightJaw = MakeJaw(tip.transform, "Right jaw", 0.004f);
            instrument.tip = tip.transform;
            tool.SetActive(true);
            return instrument;
        }
        private Transform MakeJaw(Transform parent, string name, float x)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false); pivot.localPosition = new Vector3(x, 0, -0.015f);
            MakeToolPart(pivot, "Jaw", new Vector3(0, 0, 0.012f), new Vector3(0.003f, 0.005f, 0.024f));
            return pivot;
        }
        private void MakeToolPart(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name; part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<Collider>().enabled = false;
            part.GetComponent<Renderer>().sharedMaterial = board.toolMaterial != null ? board.toolMaterial : board.pegMaterial;
        }
        private void OnReset()
        {
            foreach (InstrumentController instrument in instruments) instrument.ClearGrip();
            board.ResetObjects(); Recorder.ResetRecording(); sampler.ResetSampling();
            Pad.ResetPad(); Suture.ResetDetector();
        }
        private void OnState(ExerciseState state)
        {
            if (state == ExerciseState.Running) Uploads.BeginAttempt(Exercise.AttemptId, traineeId, ExerciseId);
            else if (state == ExerciseState.Paused)
            {
                sampler.BreakTrackingPath();
                foreach (InstrumentController instrument in instruments) instrument.ClearGrip();
            }
            else if (state == ExerciseState.Completed || state == ExerciseState.Cancelled)
            {
                sampler.BreakTrackingPath();
                foreach (InstrumentController instrument in instruments) instrument.ClearGrip();
                Uploads.EndAttempt(Exercise.AttemptId, Exercise.ElapsedSeconds, state == ExerciseState.Cancelled);
            }
        }
        private void OnEvent(RecordedEvent value) => Uploads.EnqueueEvent(Exercise.AttemptId, value);
        private void OnSample(PoseSample value) => Uploads.EnqueueSample(Exercise.AttemptId, value);
        public void RecenterBoard() { board.Recenter(head); }
        public void MoveBoardTo(Transform anchor) { if (anchor != null) board.Place(anchor.position, anchor.rotation); }
        private void OnApplicationPause(bool paused) { if (paused && Exercise != null) Exercise.Pause(); }
        // On a headset, losing focus (system menu) pauses. In the desktop preview, clicking another window should not.
        private void OnApplicationFocus(bool focused) { if (!focused && Exercise != null && !desktopPreview) Exercise.Pause(); }
        private void OnDestroy()
        {
            if (Exercise != null) { Exercise.Reset -= OnReset; Exercise.Changed -= OnState; }
            if (Recorder != null) { Recorder.Recorded -= OnEvent; Recorder.Sampled -= OnSample; }
        }

        private void BuildHud(HudController hud, CoachPanel coach, CommandPoller commands)
        {
            var panel = new GameObject("Practice HUD", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            panel.transform.SetParent(board.transform, false);
            panel.transform.localPosition = new Vector3(0, 0.22f, 0.30f);
            panel.transform.localScale = Vector3.one * 0.00065f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(760, 680);
            Canvas canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (head != null) canvas.worldCamera = head.GetComponent<Camera>();
            if (leftHand != null || rightHand != null)
                panel.AddComponent<HandRayPointer>().Setup(canvas, new[] { leftHand, rightHand });
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform background = UiKit.Rect(panel.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiKit.Panel(background, UiKit.Glass, 26);
            UiKit.PanelAt(background, "Header line", 24, 168, 712, 2, UiKit.Line, 1);
            UiKit.PanelAt(background, "Footer line", 24, 610, 712, 2, UiKit.Line, 1);
            hud.statusText = MakeText(panel.transform, "Status", font, new Vector2(0, 270), new Vector2(700, 120), 22);
            hud.resultsText = MakeText(panel.transform, "Results", font, new Vector2(0, 30), new Vector2(740, 330), 15);
            coach.coachingText = MakeText(panel.transform, "Coaching", font, new Vector2(0, -195), new Vector2(740, 100), 17);
            commands.annotationText = MakeText(panel.transform, "Instructor annotation", font, new Vector2(0, -270), new Vector2(740, 50), 18);
            hud.startButton = MakeButton(panel.transform, "Start", font, -300, 190);
            hud.pauseButton = MakeButton(panel.transform, "Pause/resume", font, -150, 190);
            hud.resetButton = MakeButton(panel.transform, "Reset", font, 0, 190);
            hud.retryUploadButton = MakeButton(panel.transform, "Retry upload", font, 150, 190);
            Button recenter = MakeButton(panel.transform, "Recenter", font, 300, 190);
            recenter.onClick.AddListener(RecenterBoard);
            coach.requestButton = MakeButton(panel.transform, "Coach", font, -80, -320);
            coach.stopSpeakingButton = MakeButton(panel.transform, "Stop speaking", font, 80, -320);
            hud.exerciseButton = MakeButton(panel.transform, "Switch exercise", font, 260, -320);
            hud.exerciseButton.onClick.AddListener(ToggleExercise);
            UiKit.StyleButton(hud.startButton, new Color(0.1f, 0.55f, 0.5f));
            UiKit.StyleButton(hud.pauseButton, new Color(0.17f, 0.24f, 0.31f));
            UiKit.StyleButton(hud.resetButton, new Color(0.45f, 0.2f, 0.2f));
            UiKit.StyleButton(hud.retryUploadButton, new Color(0.17f, 0.24f, 0.31f));
            UiKit.StyleButton(recenter, new Color(0.17f, 0.24f, 0.31f));
            UiKit.StyleButton(coach.requestButton, new Color(0.1f, 0.55f, 0.5f));
            UiKit.StyleButton(coach.stopSpeakingButton, new Color(0.17f, 0.24f, 0.31f));
            UiKit.StyleButton(hud.exerciseButton, new Color(0.17f, 0.24f, 0.31f));
            hud.resultsText.color = UiKit.Ink; coach.coachingText.color = new Color(0.75f, 0.95f, 0.9f); commands.annotationText.color = UiKit.Warn;
        }
        private static Text MakeText(Transform parent, string name, Font font, Vector2 position, Vector2 size, int fontSize)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Text));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            Text label = item.GetComponent<Text>(); label.font = font; label.fontSize = fontSize; label.color = Color.white;
            label.supportRichText = false; label.raycastTarget = false;
            return label;
        }
        private static Button MakeButton(Transform parent, string name, Font font, float x, float y)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(138, 46);
            item.GetComponent<Image>().color = new Color(0.12f, 0.28f, 0.36f);
            Text text = MakeText(item.transform, "Label", font, Vector2.zero, rect.sizeDelta, 17); text.text = name; text.alignment = TextAnchor.MiddleCenter;
            return item.GetComponent<Button>();
        }
    }
}
