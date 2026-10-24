using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    // Attach once to an empty object. Assign the Meta Building Blocks camera and controller anchors.
    // This creates exercise content only; XR origin, passthrough and ray UI stay owned by the installed SDK.
    public sealed class PegTransferRig : MonoBehaviour
    {
        public Transform head, leftController, rightController;
        public string traineeId = "demo-trainee";
        public string backendOrigin = "";
        public bool allowEditorHttp;
        public bool createHud = true;
        public PegBoard board;
        public ExerciseStateMachine Exercise { get; private set; }
        public EventRecorder Recorder { get; private set; }
        public UploadQueue Uploads { get; private set; }
        private InstrumentController[] instruments;
        private PoseSampler sampler;

        private void Awake()
        {
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
            if (head == null && Camera.main != null) head = Camera.main.transform;
            board.Recenter(head);
            var detector = gameObject.AddComponent<TransferDetector>();
            detector.exercise = Exercise; detector.board = board; detector.recorder = Recorder;
            instruments = new[] { CreateInstrument("left", leftController, detector), CreateInstrument("right", rightController, detector) };
            sampler = gameObject.AddComponent<PoseSampler>();
            sampler.exercise = Exercise; sampler.board = board; sampler.recorder = Recorder; sampler.instruments = instruments;
            var api = gameObject.AddComponent<ApiClient>();
            api.baseUrl = backendOrigin; api.allowEditorHttp = allowEditorHttp;
            Uploads = gameObject.AddComponent<UploadQueue>();
            Uploads.api = api;
            Uploads.Initialize();
            Recorder.Recorded += OnEvent;
            Recorder.Sampled += OnSample;
            Exercise.Reset += OnReset;
            Exercise.Changed += OnState;
            var hud = gameObject.AddComponent<HudController>();
            hud.exercise = Exercise; hud.recorder = Recorder; hud.uploads = Uploads;
            var coach = gameObject.AddComponent<CoachPanel>();
            coach.api = api; coach.uploads = Uploads; coach.exercise = Exercise;
            coach.audioPlayer = gameObject.AddComponent<CoachAudioPlayer>();
            var commands = gameObject.AddComponent<CommandPoller>();
            commands.api = api; commands.uploads = Uploads; commands.exercise = Exercise; commands.board = board;
            if (createHud) BuildHud(hud, coach, commands);
            Exercise.ResetExercise();
        }
        private InstrumentController CreateInstrument(string hand, Transform anchor, TransferDetector detector)
        {
            var tool = new GameObject(hand + " instrument");
            tool.SetActive(false);
            tool.transform.SetParent(transform, false);
            var instrument = tool.AddComponent<InstrumentController>();
            instrument.instrument = hand; instrument.controllerPose = anchor;
            instrument.exercise = Exercise; instrument.board = board; instrument.detector = detector;
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
            part.GetComponent<Renderer>().sharedMaterial = board.pegMaterial;
        }
        private void OnReset()
        {
            foreach (InstrumentController instrument in instruments) instrument.ClearGrip();
            board.ResetObjects(); Recorder.ResetRecording(); sampler.ResetSampling();
        }
        private void OnState(ExerciseState state)
        {
            if (state == ExerciseState.Running) Uploads.BeginAttempt(Exercise.AttemptId, traineeId);
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
        private void OnApplicationFocus(bool focused) { if (!focused && Exercise != null) Exercise.Pause(); }
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
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.statusText = MakeText(panel.transform, "Status", font, new Vector2(0, 270), new Vector2(740, 120), 22);
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
            RectTransform rect = item.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(140, 42);
            item.GetComponent<Image>().color = new Color(0.12f, 0.28f, 0.36f);
            Text text = MakeText(item.transform, "Label", font, Vector2.zero, rect.sizeDelta, 17); text.text = name; text.alignment = TextAnchor.MiddleCenter;
            return item.GetComponent<Button>();
        }
    }
}
