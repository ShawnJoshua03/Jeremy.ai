using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Jeremy
{
    // Editor/desktop preview without a headset. Add next to PegTransferRig and press Play.
    // Mouse moves the active instrument over the board; hold the left button to grasp; Tab switches hands.
    // Enter starts or resumes, P pauses, R resets, C asks the coach.
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(PegTransferRig))]
    public sealed class DesktopPreview : MonoBehaviour
    {
        [Tooltip("Skip the preview when an XR headset is active.")]
        public bool onlyWithoutHeadset = true;
        public float hoverHeight = 0.035f;
        public float liftHeight = 0.07f;
        private PegTransferRig rig;
        private Camera view;
        private Transform leftPose, rightPose;
        private int active = 1; // 0 left, 1 right
        private HudController hud;
        private CoachPanel coach;
        private GUIStyle style;
        private float yaw, pitch;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Light sun;
        private OperatingRoom room;
        private bool wound;
        private readonly System.Collections.Generic.List<GameObject> lapDress = new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<GameObject> openDress = new System.Collections.Generic.List<GameObject>();

        private void Awake()
        {
            rig = GetComponent<PegTransferRig>();
            if (onlyWithoutHeadset && XRSettings.isDeviceActive) { enabled = false; return; }
            foreach (Camera other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) other.enabled = false;
            var cameraObject = new GameObject("Desktop preview camera");
            cameraObject.tag = "MainCamera";
            view = cameraObject.AddComponent<Camera>();
            view.nearClipPlane = 0.02f;
            view.fieldOfView = 55;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            cameraObject.transform.SetPositionAndRotation(new Vector3(0, 1.5f, 0), Quaternion.identity);
            leftPose = new GameObject("Desktop left pose").transform;
            rightPose = new GameObject("Desktop right pose").transform;
            rig.head = cameraObject.transform;
            rig.leftController = leftPose;
            rig.rightController = rightPose;
            rig.leftHand = null; rig.rightHand = null;
            rig.leftSkeleton = null; rig.rightSkeleton = null;
            rig.desktopPreview = true;
            sun = new GameObject("Desktop preview light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        private void Start()
        {
            hud = GetComponent<HudController>();
            coach = GetComponent<CoachPanel>();
            // Surgeon standing at the table side, looking down at the field with the laparoscopy monitor beyond.
            Transform board = rig.board.transform;
            view.fieldOfView = 62;
            view.allowMSAA = true;
            view.transform.SetPositionAndRotation(board.TransformPoint(new Vector3(-0.2f, 0.72f, -1.0f)), board.rotation * Quaternion.Euler(22f, 14f, 0f));
            homePosition = view.transform.position; homeRotation = view.transform.rotation;
            Vector3 angles = homeRotation.eulerAngles; yaw = angles.y; pitch = angles.x;
            MoveHudToScreen();
            if (sun != null) { sun.intensity = 0.3f; sun.color = new Color(0.9f, 0.95f, 1f); sun.shadows = LightShadows.None; }
            room = OperatingRoom.Create(board);
            for (int i = 0; i < rig.Instruments.Length; i++) DressInstrument(rig.Instruments[i], i == 0 ? -1 : 1);
            rig.ExerciseChanged += ApplyExercise;
            ApplyExercise(rig.ExerciseId);
            Park(leftPose, -1);
            Park(rightPose, 1);
        }

        private void OnDestroy()
        {
            if (rig != null) rig.ExerciseChanged -= ApplyExercise;
        }

        // Peg transfer: abdomen dome with trocars and long laparoscopic graspers. Wound closure: open window, short instruments.
        private void ApplyExercise(string id)
        {
            wound = id == WoundPad.ExerciseId;
            if (room != null) room.SetExercise(wound);
            foreach (GameObject g in lapDress) if (g != null) g.SetActive(!wound);
            foreach (GameObject g in openDress) if (g != null) g.SetActive(wound);
        }

        // The headset HUD is a small world-space panel; on a monitor show it as a screen panel with mouse clicks.
        private void MoveHudToScreen()
        {
            Canvas hudCanvas = null;
            foreach (Canvas c in rig.GetComponentsInChildren<Canvas>(true)) if (c.name == "Practice HUD") hudCanvas = c;
            if (hudCanvas == null) return;
            var screen = new GameObject("Desktop HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;
            var panel = (RectTransform)hudCanvas.transform;
            panel.SetParent(screen.transform, false);
            panel.localRotation = Quaternion.identity;
            panel.localScale = Vector3.one * 0.85f; // a little smaller so the operating field stays clear
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-12f, 0f);
            panel.sizeDelta = new Vector2(760f, 680f);
            if (panel.GetComponent<Image>() == null)
            {
                var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                background.transform.SetParent(panel, false);
                background.transform.SetAsFirstSibling();
                var rect = (RectTransform)background.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var image = background.GetComponent<Image>();
                image.color = new Color(0.07f, 0.08f, 0.1f, 0.88f);
                image.raycastTarget = false;
            }
            if (EventSystem.current == null)
                new GameObject("Desktop EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            // Keys belong to the preview: no WASD/arrow UI navigation, and Enter must not also "click" the last button.
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
        }

        // Laparoscopic look: each shaft enters through its trocar; in open surgery it comes from above the hands.
        private void AimAtPort(Transform pose, int side)
        {
            Vector3 port = rig.board.transform.TransformPoint(wound ? OperatingRoom.OpenPivot(side) : OperatingRoom.InstrumentPort(side));
            Vector3 direction = pose.position - port;
            if (direction.sqrMagnitude > 1e-6f) pose.rotation = Quaternion.LookRotation(direction, rig.board.transform.up);
        }

        // Preview-only instrument bodies on the existing tip: a laparoscopic grasper (long insulated shaft, pistol grip
        // outside the body) and an open needle driver (right) / forceps (left). The tip and jaws stay as they are.
        private void DressInstrument(InstrumentController tool, int side)
        {
            if (tool == null || tool.tip == null) return;
            Material black = OrKit.Mat(new Color(0.05f, 0.055f, 0.06f), 0.55f);
            Material grey = OrKit.Mat(new Color(0.3f, 0.32f, 0.35f), 0.45f);
            Material accent = OrKit.Mat(new Color(0.12f, 0.5f, 0.62f), 0.4f);
            Material steel = OrKit.Mat(new Color(0.78f, 0.8f, 0.83f), 0.8f, 0.85f);
            Quaternion along = Quaternion.Euler(90f, 0f, 0f); // cylinder axis onto the tip's z
            Transform lap = new GameObject("Preview laparoscopic grasper").transform;
            lap.SetParent(tool.tip, false);
            OrKit.Part(lap, "Insulated shaft", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.235f), new Vector3(0.0075f, 0.21f, 0.0075f), black, along);
            OrKit.Part(lap, "Collar", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.027f), new Vector3(0.0085f, 0.004f, 0.0085f), steel, along);
            OrKit.Part(lap, "Rotation knob", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.452f), new Vector3(0.022f, 0.012f, 0.022f), grey, along);
            OrKit.Box(lap, "Handle body", new Vector3(0f, -0.004f, -0.495f), new Vector3(0.018f, 0.034f, 0.07f), grey);
            OrKit.Box(lap, "Fixed grip", new Vector3(0f, -0.055f, -0.52f), new Vector3(0.016f, 0.095f, 0.026f), grey, new Vector3(-18f, 0f, 0f));
            OrKit.Box(lap, "Trigger", new Vector3(0f, -0.05f, -0.475f), new Vector3(0.013f, 0.075f, 0.016f), accent, new Vector3(12f, 0f, 0f));
            lapDress.Add(lap.gameObject);
            Transform open = new GameObject(side > 0 ? "Preview needle driver" : "Preview forceps").transform;
            open.SetParent(tool.tip, false);
            if (side > 0)
            {
                Mesh ring = OrKit.Torus(1f, 0.2f, 18, 8);
                OrKit.Box(open, "Box joint", new Vector3(0f, 0f, -0.03f), new Vector3(0.011f, 0.005f, 0.012f), steel);
                foreach (int s in new[] { -1, 1 })
                {
                    OrKit.Box(open, "Shank", new Vector3(0.006f * s, 0f, -0.085f), new Vector3(0.0045f, 0.004f, 0.11f), steel, new Vector3(0f, -5f * s, 0f));
                    OrKit.MeshPart(open, "Finger ring", ring, new Vector3(0.016f * s, 0f, -0.15f), Quaternion.identity, Vector3.one * 0.011f, steel);
                }
            }
            else
            {
                foreach (int s in new[] { -1, 1 })
                    OrKit.Box(open, "Arm", new Vector3(0.005f * s, 0f, -0.075f), new Vector3(0.0035f, 0.007f, 0.12f), steel, new Vector3(0f, -3.5f * s, 0f));
                OrKit.Box(open, "Spring end", new Vector3(0f, 0f, -0.137f), new Vector3(0.016f, 0.007f, 0.012f), steel);
            }
            openDress.Add(open.gameObject);
        }

        private void Park(Transform pose, int side)
        {
            Transform board = rig.board.transform;
            pose.position = board.TransformPoint(new Vector3(0.215f * side, hoverHeight, -0.15f));
            pose.rotation = board.rotation;
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keys != null)
            {
                if (keys.tabKey.wasPressedThisFrame)
                {
                    active = 1 - active;
                    if (active == 0) Park(rightPose, 1); else Park(leftPose, -1);
                }
                if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame)
                {
                    if (rig.Exercise.State == ExerciseState.Paused) rig.Exercise.Resume();
                    else rig.Exercise.Begin();
                }
                if (keys.pKey.wasPressedThisFrame && hud != null) hud.TogglePause();
                if (keys.rKey.wasPressedThisFrame) rig.Exercise.ResetExercise();
                if (keys.cKey.wasPressedThisFrame && coach != null) coach.RequestCoaching();
            }
            if (mouse != null && keys != null) FlyCamera(mouse, keys);
            if (mouse == null || rig.Instruments == null) return;
            InstrumentController tool = rig.Instruments[active];
            rig.Instruments[1 - active].desktopClosed = false;
            bool grasp = mouse.leftButton.isPressed;
            tool.desktopClosed = grasp;
            Transform board = rig.board.transform;
            // Wound closure: closing the driver lowers the tip onto the pad surface so a bite can land on a suture point.
            float height = wound ? (grasp ? WoundPad.PadTop + 0.002f : WoundPad.PadTop + 0.012f) : grasp && tool.Held != null ? liftHeight : hoverHeight;
            var plane = new Plane(board.up, board.TransformPoint(new Vector3(0, height, 0)));
            Ray ray = view.ScreenPointToRay(mouse.position.ReadValue());
            if (plane.Raycast(ray, out float distance))
            {
                Transform pose = active == 0 ? leftPose : rightPose;
                pose.position = ray.GetPoint(distance);
            }
            AimAtPort(leftPose, -1);
            AimAtPort(rightPose, 1);
        }

        // W/A/S/D or arrows move along the ground plane relative to the view, Q/E down/up, Shift faster,
        // mouse wheel moves along the view direction, hold the right mouse button to look, F back to the start view.
        private void FlyCamera(Mouse mouse, Keyboard keys)
        {
            Transform t = view.transform;
            if (keys.fKey.wasPressedThisFrame)
            {
                t.SetPositionAndRotation(homePosition, homeRotation);
                Vector3 home = homeRotation.eulerAngles; yaw = home.y; pitch = home.x;
                return;
            }
            if (mouse.rightButton.isPressed)
            {
                Vector2 look = mouse.delta.ReadValue() * 0.15f;
                yaw += look.x;
                pitch = Mathf.Clamp(Mathf.DeltaAngle(0, pitch - look.y), -89f, 89f);
                t.rotation = Quaternion.Euler(pitch, yaw, 0);
            }
            Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(t.up, Vector3.up);
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 move = Vector3.zero;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) move += forward;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) move -= forward;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) move += right;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) move -= right;
            if (keys.eKey.isPressed) move += Vector3.up;
            if (keys.qKey.isPressed) move -= Vector3.up;
            bool fast = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
            if (move.sqrMagnitude > 1f) move.Normalize();
            t.position += move * (fast ? 1.5f : 0.5f) * Time.unscaledDeltaTime;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f) t.position += t.forward * Mathf.Sign(scroll) * (fast ? 0.25f : 0.08f);
        }

        private void OnGUI()
        {
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
            string hand = active == 0 ? "LEFT" : "RIGHT";
            GUI.Box(new Rect(12, 12, 430, 232),
                "Jeremy.ai desktop preview (no headset)\n" +
                "Exercise: " + (wound ? "wound closure" : "peg transfer") + "    State: " + rig.Exercise.State + "\n" +
                "Active instrument: " + hand + "\n\n" +
                "Enter  start / resume        P  pause\n" +
                "Mouse  move instrument       Hold left button  grasp\n" +
                "Tab    switch hand           R  reset\n" +
                "1 / 2  peg transfer / wound closure\n" +
                "C      ask the coach (needs a backend URL)\n" +
                "W/A/S/D move · Q/E down/up · hold right mouse to look · wheel zoom · F reset view", style);
        }
    }
}
