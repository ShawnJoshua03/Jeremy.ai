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
            var light = new GameObject("Desktop preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        private void Start()
        {
            hud = GetComponent<HudController>();
            coach = GetComponent<CoachPanel>();
            // Look down at the board; aim right of it so the board sits left of the screen HUD.
            Transform board = rig.board.transform;
            view.transform.position = board.TransformPoint(new Vector3(0.10f, 0.62f, -0.42f));
            view.transform.LookAt(board.TransformPoint(new Vector3(0.15f, 0f, 0.03f)));
            MoveHudToScreen();
            Park(leftPose, -1);
            Park(rightPose, 1);
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
            panel.localScale = Vector3.one * 0.95f;
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
        }

        private void Park(Transform pose, int side)
        {
            Transform board = rig.board.transform;
            pose.position = board.TransformPoint(new Vector3(0.26f * side, hoverHeight, -0.12f));
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
            if (mouse == null || rig.Instruments == null) return;
            InstrumentController tool = rig.Instruments[active];
            rig.Instruments[1 - active].desktopClosed = false;
            bool grasp = mouse.leftButton.isPressed;
            tool.desktopClosed = grasp;
            Transform board = rig.board.transform;
            float height = grasp && tool.Held != null ? liftHeight : hoverHeight;
            var plane = new Plane(board.up, board.TransformPoint(new Vector3(0, height, 0)));
            Ray ray = view.ScreenPointToRay(mouse.position.ReadValue());
            if (plane.Raycast(ray, out float distance))
            {
                Transform pose = active == 0 ? leftPose : rightPose;
                pose.position = ray.GetPoint(distance);
                pose.rotation = board.rotation;
            }
        }

        private void OnGUI()
        {
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
            string hand = active == 0 ? "LEFT" : "RIGHT";
            GUI.Box(new Rect(12, 12, 380, 150),
                "Jeremy.ai desktop preview (no headset)\n" +
                "State: " + rig.Exercise.State + "    Active instrument: " + hand + "\n\n" +
                "Enter  start / resume        P  pause\n" +
                "Mouse  move instrument       Hold left button  grasp\n" +
                "Tab    switch hand           R  reset\n" +
                "C      ask the coach (needs a backend URL)", style);
        }
    }
}
