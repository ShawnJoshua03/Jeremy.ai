using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Jeremy
{
    // A short click-through briefing before the lap chole starts: what you'll do, with pictures from the sim, read
    // aloud by the coach. Headset: a panel in front of the trainee, pinch-click Back / Next / Start with the hand ray.
    // Desktop: a centred overlay (click, or arrow keys; Enter skips and starts as before). Shown once per launch.
    public sealed class LapCholeIntro : MonoBehaviour
    {
        struct Slide { public string image, title, body; }
        static readonly Slide[] Slides =
        {
            new Slide { image = "Intro/slide1", title = "Welcome to the OR",
                body = "You're about to perform a laparoscopic gallbladder removal, start to finish: fifteen steps, in the order a surgeon does them. I'll coach you the whole way." },
            new Slide { image = "Intro/slide2", title = "Grab the right tool",
                body = "Your instruments are on the tray to your right. The one you need next glows. Reach over and pinch it to pick it up." },
            new Slide { image = "Intro/slide3", title = "Enter at the mark",
                body = "Put the tip on the purple cross. Line the shaft up with the faint guide line; it turns green when you're aligned. Push in slowly, feel the layers pop, then hold still at the right depth." },
            new Slide { image = "Intro/slide4", title = "Work through the ports",
                body = "Inside, watch the big monitor. Each instrument pivots at its port, so move the handle and the tip follows. The yellow glow marks your target." },
            new Slide { image = "Intro/slide5", title = "Your coach",
                body = "I'll tell you when you drift, when you're lined up, and before anything risky. Ask me anything out loud, any time: am I doing this right? What's next?" },
            new Slide { image = "Intro/slide6", title = "Ready",
                body = "That's it. Press Start and we'll begin with the Veress needle." },
        };

        public static System.Collections.Generic.IEnumerable<string> SpokenLines() { foreach (Slide s in Slides) yield return s.body; }
        public bool Open { get; private set; }
        /// <summary>True until the trainee has clicked through the briefing: nothing else shows or speaks before that.</summary>
        public static bool Blocking(PegTransferRig rig)
        {
            if (rig == null || !rig.LapChole) return false;
            LapCholeIntro intro = rig.GetComponent<LapCholeIntro>();
            return intro != null && (intro.Open || !intro.Shown) && rig.Exercise != null && rig.Exercise.State == ExerciseState.Ready;
        }
        public bool Shown { get; private set; }
        PegTransferRig rig;
        LapCholeNarrator narrator;
        GameObject root;
        RawImage picture;
        Text title, body, nextLabel;
        readonly Image[] dots = new Image[Slides.Length];
        int index;

        void Update()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (narrator == null) narrator = GetComponent<LapCholeNarrator>();
            if (rig == null || rig.Exercise == null) return;
            LapCholeProcedure proc = GetComponent<LapCholeProcedure>();
            bool ready = rig.LapChole && proc != null && proc.Active && rig.Exercise.State == ExerciseState.Ready;
            if (!Shown && ready && (rig.desktopPreview || (rig.head != null && rig.head.position.y > 0.8f))) Show();
            if (Open && rig.Exercise.State != ExerciseState.Ready) Close(false); // started some other way (Enter, bridge, demo)
            if (!Open) return;
            if (!rig.desktopPreview && rig.head != null) Follow();
            Keyboard k = Keyboard.current;
            if (rig.desktopPreview && k != null)
            {
                if (k.rightArrowKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Next();
                if (k.leftArrowKey.wasPressedThisFrame) Back();
                if (k.escapeKey.wasPressedThisFrame) Close(true);
            }
        }

        public void Replay() { Shown = false; }

        void Show()
        {
            Shown = true; Open = true; index = 0;
            if (root == null) Build();
            root.SetActive(true);
            Place();
            Render();
        }

        void Close(bool start)
        {
            Open = false;
            if (root != null) root.SetActive(false);
            if (start && rig.Exercise.State == ExerciseState.Ready) rig.Exercise.Begin();
        }

        void Next() { if (index < Slides.Length - 1) { index++; Render(); } else Close(true); }
        void Back() { if (index > 0) { index--; Render(); } }

        void Render()
        {
            Slide s = Slides[index];
            Texture2D tex = Resources.Load<Texture2D>(s.image);
            picture.texture = tex;
            picture.gameObject.SetActive(tex != null);
            if (tex != null)
            {
                // Fit the picture inside its frame without stretching.
                RectTransform frame = (RectTransform)picture.transform.parent;
                float fw = frame.sizeDelta.x, fh = frame.sizeDelta.y, aspect = tex.width / (float)tex.height;
                float w = fw, h = fw / aspect; if (h > fh) { h = fh; w = fh * aspect; }
                picture.rectTransform.sizeDelta = new Vector2(w, h);
            }
            title.text = s.title;
            body.text = s.body;
            nextLabel.text = index == Slides.Length - 1 ? "Start" : "Next";
            for (int i = 0; i < dots.Length; i++) dots[i].color = i == index ? UiKit.Accent : i < index ? new Color(0.3f, 0.6f, 0.58f) : UiKit.Idle;
            if (narrator != null) narrator.SpeakIntro(s.body);
        }

        // ---------------- layout ----------------
        const float W = 1180f, H = 640f;

        void Build()
        {
            root = new GameObject("Lap chole intro", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            var rt = (RectTransform)root.transform;
            if (rig.desktopPreview)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 1f;
                RectTransform dim = UiKit.Rect(rt, "Dim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace;
                if (rig.head != null) canvas.worldCamera = rig.head.GetComponent<Camera>();
                rt.sizeDelta = new Vector2(W, H);
                rt.localScale = Vector3.one * 0.0008f;
                root.AddComponent<HandRayPointer>().Setup(canvas, new[] { rig.leftHand, rig.rightHand });
            }
            RectTransform card = UiKit.Rect(rt, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            card.sizeDelta = new Vector2(W, H);
            if (rig.desktopPreview) card.localScale = Vector3.one * 0.8f;
            UiKit.Panel(card, UiKit.Glass, 28);
            // Picture on the left, text on the right.
            Image frame = UiKit.PanelAt(card, "Picture frame", 28, 28, 640, 584, UiKit.Card, 18);
            RectTransform pic = UiKit.Rect(frame.transform, "Picture", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            picture = pic.gameObject.AddComponent<RawImage>(); picture.raycastTarget = false;
            UiKit.Label(card, "Kicker", 700, 40, 440, 22, 15, UiKit.Accent, FontStyle.Bold).text = "BEFORE YOU START";
            title = UiKit.Label(card, "Title", 700, 68, 440, 60, 36, UiKit.Ink, FontStyle.Bold);
            body = UiKit.Label(card, "Body", 700, 140, 440, 330, 22, new Color(0.84f, 0.88f, 0.92f));
            for (int i = 0; i < dots.Length; i++) dots[i] = UiKit.PanelAt(card, "Dot " + i, 700 + i * 26, 500, 18, 8, UiKit.Idle, 4);
            MakeButton(card, "Back", 700, 540, 130, Back, new Color(0.17f, 0.24f, 0.31f));
            nextLabel = MakeButton(card, "Next", 846, 540, 170, Next, new Color(0.1f, 0.55f, 0.5f));
            MakeButton(card, "Skip", 1032, 540, 120, () => Close(true), new Color(0.2f, 0.22f, 0.26f));
        }

        Text MakeButton(RectTransform parent, string label, float x, float y, float w, UnityEngine.Events.UnityAction action, Color color)
        {
            Image img = UiKit.PanelAt(parent, label + " button", x, y, w, 62, color, 14);
            img.raycastTarget = true;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(action);
            Text t = UiKit.Label(img.transform, "Label", 0, 0, w, 62, 22, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.text = label;
            UiKit.StyleButton(button, color, 14);
            return t;
        }

        // Headset: a metre in front of the trainee at eye height, facing them.
        void Place()
        {
            if (rig.desktopPreview || rig.head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            root.transform.position = rig.head.position + forward * 1.0f + Vector3.down * 0.08f;
            Face();
        }

        // Keep the briefing dead ahead at eye height: if it drifts out of view (or was placed before the headset was on),
        // glide it back in front.
        void Follow()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();
            Vector3 target = rig.head.position + fwd * 1.0f + Vector3.down * 0.05f;
            Vector3 to = root.transform.position - rig.head.position;
            float off = Vector3.Angle(rig.head.forward, to);
            if (off > 22f || Mathf.Abs(root.transform.position.y - target.y) > 0.25f || Vector3.Distance(root.transform.position, target) > 0.6f) following = true;
            if (following)
            {
                root.transform.position = Vector3.Lerp(root.transform.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                if (Vector3.Distance(root.transform.position, target) < 0.03f) following = false;
            }
            Face();
        }
        bool following;

        void Face()
        {
            Vector3 away = root.transform.position - rig.head.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
