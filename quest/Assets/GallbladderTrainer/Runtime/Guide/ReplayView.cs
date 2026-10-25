using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using GallbladderTrainer.Backend;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Plays a recorded session back over the anatomy: each track (left, right, head, or any future instrument) is a
    /// faint full-path ghost plus a bright glowing trail of the last few seconds with a light at its tip.
    /// Scrubber, play/pause, speed and an event caption sit on a holographic strip. Desktop: Space toggles play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReplayView : MonoBehaviour
    {
        [SerializeField] Vector3 stripPosition = new Vector3(0f, 0.985f, -0.60f);
        [SerializeField] Vector3 stripEuler = new Vector3(30f, 0f, 0f);
        [SerializeField] float trailSeconds = 3f;

        sealed class Track
        {
            public List<ReplayData.Point> Points;
            public LineRenderer Ghost, Core, Halo;
            public Transform Tip;
            public Vector3[] Buffer;
        }

        readonly List<Track> tracks = new List<Track>();
        readonly List<Material> mats = new List<Material>();
        ReplayData data;
        Transform root, visuals;
        HoloUi.Panel strip;
        Slider scrubber;
        Text timeText, captionText, playLabel, speedLabel, sourceLabel;
        RectTransform ticks;
        Action onClose;
        double t;
        float speed = 1f;
        bool playing, built;

        public bool IsOpen { get { return data != null; } }

        public void Show(ReplayData d, Transform workstationRoot, Action closed)
        {
            Hide(false);
            if (!built) BuildStrip(workstationRoot);
            data = d ?? new ReplayData();
            root = workstationRoot;
            onClose = closed;
            visuals = new GameObject("Jeremy Replay Trails").transform;
            visuals.SetParent(root, false);
            int i = 0;
            foreach (var kv in data.Tracks)
            {
                if (kv.Value.Count < 2) continue;
                tracks.Add(BuildTrack(kv.Key, kv.Value, i++));
            }
            BuildTicks();
            sourceLabel.text = (data.Source == "device" ? "Recorded on this headset" : "Recorded by the backend") +
                               (tracks.Count == 0 ? "  ·  no movement was recorded" : "");
            strip.Root.gameObject.SetActive(true);
            t = 0; playing = tracks.Count > 0; speed = 1f; speedLabel.text = "1×";
            Apply();
        }

        public void Hide(bool notify)
        {
            foreach (var m in mats) HoloUi.Kill(m);
            mats.Clear();
            tracks.Clear();
            if (visuals != null) HoloUi.Kill(visuals.gameObject);
            visuals = null;
            if (strip != null) strip.Root.gameObject.SetActive(false);
            data = null;
            if (notify && onClose != null) { var c = onClose; onClose = null; c(); }
        }

        void Update()
        {
            if (data == null) return;
            HoloUi.AssignCamera(strip.Canvas);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) TogglePlay();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Hide(true); return; }
#endif
            if (playing && data.DurationMs > 0)
            {
                t += Time.unscaledDeltaTime * 1000.0 * speed;
                if (t >= data.DurationMs) { t = data.DurationMs; playing = false; }
                Apply();
            }
            // gentle tip shimmer
            float s = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var tr in tracks) if (tr.Tip != null) tr.Tip.localScale = Vector3.one * 0.014f * s;
        }

        void Apply()
        {
            long now = (long)t;
            long from = now - (long)(trailSeconds * 1000f);
            foreach (var tr in tracks)
            {
                int n = 0;
                var pts = tr.Points;
                int start = LowerBound(pts, from);
                for (int i = start; i < pts.Count && pts[i].T <= now && n < tr.Buffer.Length; i++) tr.Buffer[n++] = pts[i].P;
                tr.Core.positionCount = n;
                tr.Halo.positionCount = n;
                if (n > 0)
                {
                    tr.Core.SetPositions(tr.Buffer);
                    tr.Halo.SetPositions(tr.Buffer);
                    tr.Tip.gameObject.SetActive(true);
                    tr.Tip.localPosition = tr.Buffer[n - 1];
                }
                else tr.Tip.gameObject.SetActive(false);
            }
            scrubber.SetValueWithoutNotify(data.DurationMs > 0 ? (float)(t / data.DurationMs) : 0f);
            timeText.text = Clock(now) + " / " + Clock(data.DurationMs);
            playLabel.text = playing ? "Pause" : "Play";

            ReplayData.Mark? last = null;
            foreach (var e in data.Events) { if (e.T <= now) last = e; else break; }
            captionText.text = last.HasValue ? Clock(last.Value.T) + "   " + Describe(last.Value) : "";
        }

        static int LowerBound(List<ReplayData.Point> pts, long t)
        {
            int lo = 0, hi = pts.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (pts[mid].T < t) lo = mid + 1; else hi = mid; }
            return lo;
        }

        static string Describe(ReplayData.Mark e)
        {
            string what = HoloUi.Humanize(e.Kind);
            if (e.Kind == "decision_submitted") return "Decision · " + HoloUi.Humanize(e.ObjectId) + " → " + HoloUi.DecisionText(e.Detail);
            if (e.Kind == "mode_changed") return "Mode → " + e.Detail;
            string s = what;
            if (!string.IsNullOrEmpty(e.ObjectId)) s += " · " + HoloUi.Humanize(e.ObjectId);
            if (!string.IsNullOrEmpty(e.Detail) && e.Kind != "structure_selected") s += " · " + HoloUi.Humanize(e.Detail);
            return s;
        }

        static string Clock(long ms)
        {
            long s = Math.Max(0, ms / 1000);
            return (s / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        void TogglePlay()
        {
            if (data == null) return;
            if (!playing && t >= data.DurationMs) t = 0;
            playing = !playing;
            Apply();
        }

        void CycleSpeed()
        {
            speed = speed >= 4f ? 1f : speed * 2f;
            speedLabel.text = speed.ToString("0", CultureInfo.InvariantCulture) + "×";
        }

        // ------------------------------------------------------------------ construction

        static Color TrackColor(string name, int i)
        {
            switch (name)
            {
                case "right": return HoloUi.Cyan;
                case "left": return HoloUi.Violet;
                case "head": return new Color(0.85f, 0.92f, 1f);
                default: return i % 2 == 0 ? HoloUi.Amber : HoloUi.Green;
            }
        }

        Track BuildTrack(string name, List<ReplayData.Point> pts, int i)
        {
            Color c = TrackColor(name, i);
            bool head = name == "head";
            var tr = new Track { Points = pts, Buffer = new Vector3[Mathf.Max(2, Mathf.CeilToInt(trailSeconds * 25f) + 4)] };

            var ghostPts = new Vector3[pts.Count];
            for (int k = 0; k < pts.Count; k++) ghostPts[k] = pts[k].P;
            tr.Ghost = Line(name + " ghost", c, head ? 0.06f : 0.12f, head ? 0.06f : 0.12f, 0.0016f);
            tr.Ghost.positionCount = ghostPts.Length;
            tr.Ghost.SetPositions(ghostPts);
            tr.Halo = Line(name + " glow", c, 0f, head ? 0.12f : 0.28f, head ? 0.008f : 0.014f);
            tr.Core = Line(name + " trail", Color.Lerp(c, Color.white, 0.35f), 0f, head ? 0.5f : 1f, head ? 0.0025f : 0.004f);

            Material tipMat = HoloUi.Glowing(Color.Lerp(c, Color.white, 0.3f));
            mats.Add(tipMat);
            tr.Tip = HoloUi.Primitive(visuals, name + " tip", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.014f, tipMat).transform;
            return tr;
        }

        LineRenderer Line(string name, Color c, float alphaTail, float alphaHead, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visuals, false);
            var lr = go.AddComponent<LineRenderer>();
            Material m = HoloUi.Translucent(Color.white);
            mats.Add(m);
            lr.sharedMaterial = m;
            lr.useWorldSpace = false;
            lr.widthMultiplier = width;
            lr.widthCurve = AnimationCurve.Linear(0f, 0.35f, 1f, 1f);
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(alphaTail, 0f), new GradientAlphaKey(alphaHead, 1f) });
            lr.colorGradient = g;
            lr.positionCount = 0;
            return lr;
        }

        void BuildTicks()
        {
            for (int i = ticks.childCount - 1; i >= 0; i--) HoloUi.Kill(ticks.GetChild(i).gameObject);
            if (data.DurationMs <= 0) return;
            foreach (var e in data.Events)
            {
                if (e.Kind != "decision_submitted" && e.Kind != "instructor_requested") continue;
                float x = Mathf.Clamp01((float)e.T / data.DurationMs) * 640f;
                Color c = e.Kind == "instructor_requested" ? HoloUi.Amber : HoloUi.Cyan;
                HoloUi.Box(ticks, "Tick", c, x - 2f, 0f, 4f, 14f);
            }
        }

        void BuildStrip(Transform workstationRoot)
        {
            built = true;
            strip = HoloUi.MakePanel(workstationRoot, "Jeremy ReplayStrip", 1000, 170, 0.001f, true);
            strip.Root.localPosition = stripPosition;
            strip.Root.localRotation = Quaternion.Euler(stripEuler);
            Transform s = strip.Root;
            HoloUi.Label(s, "Title", "<b>REPLAY</b>", 18, HoloUi.Cyan, TextAnchor.UpperLeft, 24, 12, 120, 24);
            sourceLabel = HoloUi.Label(s, "Source", "", 16, HoloUi.TextDim, TextAnchor.UpperLeft, 120, 14, 520, 22);
            HoloUi.Label(s, "Legend", "<color=#3DDCFF>■</color> right   <color=#B388FF>■</color> left   <color=#D9EBFF>■</color> head", 16, HoloUi.TextDim, TextAnchor.UpperRight, 640, 14, 336, 22);

            HoloUi.Button(s, "Play", "Pause", 22, 24, 50, 120, 56, TogglePlay, out playLabel);
            scrubber = MakeSlider(s, 164, 66, 640, 24);
            ticks = HoloUi.Rect(s, "Ticks");
            HoloUi.TopLeft(ticks, 164, 92, 640, 14);
            timeText = HoloUi.Label(s, "Time", "00:00 / 00:00", 18, HoloUi.TextBody, TextAnchor.UpperLeft, 164, 108, 200, 24);
            HoloUi.Button(s, "Speed", "1×", 20, 820, 50, 70, 56, CycleSpeed, out speedLabel);
            Text cl;
            HoloUi.Button(s, "Close", "Back", 20, 900, 50, 76, 56, () => Hide(true), out cl);
            captionText = HoloUi.Label(s, "Caption", "", 18, HoloUi.TextMain, TextAnchor.UpperLeft, 370, 108, 606, 50);
            strip.Root.gameObject.SetActive(false);
        }

        Slider MakeSlider(Transform parent, float x, float y, float w, float h)
        {
            RectTransform r = HoloUi.Rect(parent, "Scrubber");
            HoloUi.TopLeft(r, x, y, w, h);
            Image bg = HoloUi.Box(r, "Track", new Color(1f, 1f, 1f, 0.10f));
            bg.rectTransform.anchorMin = new Vector2(0f, 0.35f); bg.rectTransform.anchorMax = new Vector2(1f, 0.65f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
            bg.raycastTarget = true;

            RectTransform fillArea = HoloUi.Rect(r, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.35f); fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = fillArea.offsetMax = Vector2.zero;
            Image fill = HoloUi.Box(fillArea, "Fill", HoloUi.Cyan);
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = HoloUi.Rect(r, "Handle Area");
            HoloUi.Stretch(handleArea, 0f);
            handleArea.offsetMin = new Vector2(10f, 0f); handleArea.offsetMax = new Vector2(-10f, 0f);
            Image handle = HoloUi.Box(handleArea, "Handle", Color.white);
            handle.sprite = HoloUi.Disc();
            handle.rectTransform.sizeDelta = new Vector2(24f, 0f);
            handle.raycastTarget = true;

            var slider = r.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v =>
            {
                if (data == null) return;
                t = v * data.DurationMs;
                playing = false;
                Apply();
            });
            return slider;
        }

        void OnDestroy() { Hide(false); }
    }
}
