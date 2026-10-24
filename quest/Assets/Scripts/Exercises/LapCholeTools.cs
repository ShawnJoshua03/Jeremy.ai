using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Desktop preview only: the interactive lap chole instruments and the procedure's own small props
    // (Hem-o-lok clips, cut tube ends, ductotomy slit, pulsing target highlights, the cholangiogram image).
    // Everything here is built in board-local coordinates under a parent the procedure owns, so a reset just
    // destroys it. Shafts are unit-length lathes stretched each frame between the trocar pivot and the tip.
    public enum LapToolKind { Grasper, Dissector, ClipApplier, Scissors, Catheter }

    public sealed class LapTool
    {
        public readonly LapToolKind kind;
        public readonly Transform root;
        private readonly Transform shaft, handle;
        private readonly Transform[] jaws = new Transform[2];
        private readonly GameObject loadedClip;
        private readonly float jawOpen;
        public Vector3 TipWorld => root.position;

        public LapTool(Transform parent, LapToolKind kind, LapCholeKit kit)
        {
            this.kind = kind;
            root = new GameObject("Lap tool " + kind).transform;
            root.SetParent(parent, false);
            bool white = kind == LapToolKind.ClipApplier;
            bool catheter = kind == LapToolKind.Catheter;
            Material shaftMat = catheter ? kit.catheter : white ? kit.white : kit.black;
            float radius = catheter ? 0.0009f : white ? 0.0028f : 0.0026f;
            float back = catheter ? 0.016f : 0.016f; // shaft starts this far behind the tip (jaws / metal tube in front)
            shaft = new GameObject("Shaft").transform;
            shaft.SetParent(root, false);
            shaft.localPosition = new Vector3(0f, 0f, -back);
            var unit = SurgicalField.Lathe(new[] { new Vector2(0f, -0.0005f), new Vector2(radius, 0f), new Vector2(radius, 1f) }, 24);
            SurgicalField.Add(shaft, "Shaft mesh", unit, shaftMat, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
            if (catheter)
            {
                // Metal support tube: the front 15 mm, tip at the root (it threads into the duct).
                SurgicalField.Add(root, "Metal support tube", SurgicalField.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0011f, 0.0006f), new Vector2(0.0012f, 0.015f), new Vector2(0.0009f, 0.016f) }, 20),
                    kit.steel, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
                SurgicalField.Add(root, "Catheter tip", SurgicalField.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.0007f, -0.0035f), new Vector2(0.0007f, 0.001f) }, 16),
                    kit.catheter, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
            }
            else
            {
                // Ribbed insulation collar behind the jaws, as on the Teleflex graspers.
                for (int k = 0; k < 4; k++)
                    SurgicalField.Add(root, "Rib", OrKit.Torus(1f, 0.14f, 24, 6), shaftMat, new Vector3(0f, 0f, -0.02f - k * 0.0035f), Quaternion.Euler(90f, 0f, 0f), Vector3.one * radius * 1.15f);
                jawOpen = kind == LapToolKind.Scissors ? 16f : kind == LapToolKind.ClipApplier ? 10f : 20f;
                for (int s = 0; s < 2; s++)
                {
                    int sgn = s == 0 ? -1 : 1;
                    var pivot = new GameObject("Jaw pivot").transform;
                    pivot.SetParent(root, false);
                    pivot.localPosition = new Vector3(0.0009f * sgn, 0f, -0.015f);
                    jaws[s] = pivot;
                    Material m = kind == LapToolKind.ClipApplier ? kit.grey : kit.steel;
                    if (kind == LapToolKind.Dissector)
                    {
                        // Maryland: two segments curving to the right.
                        SurgicalField.Add(pivot, "Jaw", SurgicalField.RoundedBox(new Vector3(0.0016f, 0.0015f, 0.009f), 0.0006f, 2), m, new Vector3(0f, 0f, 0.0045f), Quaternion.identity, Vector3.one);
                        SurgicalField.Add(pivot, "Jaw tip", SurgicalField.RoundedBox(new Vector3(0.0014f, 0.0013f, 0.0065f), 0.0006f, 2), m, new Vector3(0.0012f, 0f, 0.0115f), Quaternion.Euler(0f, 22f, 0f), Vector3.one);
                    }
                    else if (kind == LapToolKind.Scissors)
                        SurgicalField.Add(pivot, "Blade", SurgicalField.RoundedBox(new Vector3(0.0011f, 0.0016f, 0.015f), 0.0004f, 2), m, new Vector3(0f, 0f, 0.0075f), Quaternion.Euler(0f, 8f, 0f), Vector3.one);
                    else
                        SurgicalField.Add(pivot, "Jaw", SurgicalField.RoundedBox(new Vector3(0.0018f, 0.0016f, 0.015f), 0.0007f, 2), m, new Vector3(0f, 0f, 0.0075f), Quaternion.identity, Vector3.one);
                }
                if (kind == LapToolKind.ClipApplier)
                {
                    loadedClip = LapCholeKit.ClipMesh(root, kit, 0.0095f).gameObject;
                    loadedClip.transform.localPosition = new Vector3(0f, 0f, -0.006f);
                    loadedClip.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }
            }
            // Handle outside the patient (seen in the room view).
            handle = new GameObject("Handle").transform;
            handle.SetParent(root, false);
            if (white)
            {
                SurgicalField.Add(handle, "Nose cone", SurgicalField.Lathe(new[] { new Vector2(0.013f, 0f), new Vector2(0.0125f, 0.008f), new Vector2(0.007f, 0.034f), new Vector2(0.003f, 0.05f) }, 32), kit.green, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
                SurgicalField.Add(handle, "Housing", SurgicalField.RoundedBox(new Vector3(0.034f, 0.03f, 0.09f), 0.014f), kit.white, new Vector3(0f, 0f, -0.045f), Quaternion.identity, Vector3.one);
                SurgicalField.Add(handle, "Trigger", SurgicalField.RoundedBox(new Vector3(0.016f, 0.06f, 0.017f), 0.0075f), kit.green, new Vector3(0f, -0.04f, -0.03f), Quaternion.Euler(-20f, 0f, 0f), Vector3.one);
            }
            else if (!catheter)
            {
                SurgicalField.Add(handle, "Knob", SurgicalField.Lathe(new[] { new Vector2(0.004f, 0f), new Vector2(0.011f, 0.004f), new Vector2(0.011f, 0.014f), new Vector2(0.004f, 0.018f) }, 28, 12, 0.08f), kit.black, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
                SurgicalField.Add(handle, "Grip", SurgicalField.RoundedBox(new Vector3(0.016f, 0.09f, 0.026f), 0.007f), kit.black, new Vector3(0f, -0.05f, -0.035f), Quaternion.Euler(-18f, 0f, 0f), Vector3.one);
                SurgicalField.Add(handle, "Ring", OrKit.Torus(1f, 0.18f, 24, 8), kit.black, new Vector3(0f, -0.04f, -0.005f), Quaternion.Euler(0f, 0f, 90f), Vector3.one * 0.013f);
            }
        }

        public void SetVisible(bool on) { if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on); }
        public void SetClipLoaded(bool on) { if (loadedClip != null) loadedClip.SetActive(on); }

        /// <summary>Place the tool: the shaft runs from the trocar pivot (board-local) to the tip (board-local).</summary>
        public void Pose(Transform board, Vector3 pivotLocal, Vector3 tipLocal, bool closed, float outside = 0.26f)
        {
            Vector3 pivot = board.TransformPoint(pivotLocal), tip = board.TransformPoint(tipLocal);
            Vector3 dir = tip - pivot;
            if (dir.sqrMagnitude < 1e-8f) dir = -board.up;
            dir.Normalize();
            root.SetPositionAndRotation(tip, Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir, board.up)) > 0.95f ? board.forward : board.up));
            float inside = Vector3.Distance(pivot, tip);
            shaft.localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, inside + outside - 0.016f));
            handle.localPosition = new Vector3(0f, 0f, -(inside + outside));
            float a = closed ? 0f : jawOpen;
            if (jaws[0] != null) jaws[0].localRotation = Quaternion.Euler(0f, -a, 0f);
            if (jaws[1] != null) jaws[1].localRotation = Quaternion.Euler(0f, a, 0f);
        }
    }

    /// <summary>Materials and small meshes the procedure shares.</summary>
    public sealed class LapCholeKit
    {
        public Material black, steel, grey, white, green, clip, catheter, slit, glowSkin, glowInside, guide, balloon;
        private readonly List<Material> owned = new List<Material>();
        private Mesh ring;
        private Material Own(Material m) { owned.Add(m); return m; }

        public LapCholeKit()
        {
            black = Own(SurgicalField.Standard(new Color(0.03f, 0.035f, 0.04f), 0.7f));
            steel = Own(SurgicalField.Standard(new Color(0.72f, 0.74f, 0.77f), 0.8f, 0.85f));
            grey = Own(SurgicalField.Standard(new Color(0.55f, 0.57f, 0.6f), 0.7f, 0.6f));
            white = Own(SurgicalField.Standard(new Color(0.93f, 0.94f, 0.94f), 0.55f));
            green = Own(SurgicalField.Standard(new Color(0.08f, 0.5f, 0.2f), 0.6f));
            clip = Own(SurgicalField.Standard(new Color(0.96f, 0.96f, 0.93f), 0.5f));
            catheter = Own(OrKit.Fade(new Color(0.92f, 0.9f, 0.78f, 0.75f), 0.85f));
            slit = Own(SurgicalField.Standard(new Color(0.12f, 0.03f, 0.02f), 0.75f));
            glowSkin = Own(OrKit.Glow(new Color(0.2f, 0.95f, 0.75f), 1.6f));
            glowInside = Own(OrKit.Fade(new Color(1f, 0.92f, 0.35f, 0.5f), 0.3f));
            glowInside.EnableKeyword("_EMISSION"); glowInside.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.3f) * 0.9f);
            guide = Own(OrKit.Fade(new Color(0.2f, 0.95f, 0.75f, 0.45f), 0.2f));
            guide.EnableKeyword("_EMISSION"); guide.SetColor("_EmissionColor", new Color(0.2f, 0.95f, 0.75f) * 0.8f);
            balloon = Own(OrKit.Fade(new Color(0.95f, 0.95f, 0.97f, 0.8f), 0.7f));
        }

        public Mesh Ring => ring != null ? ring : ring = OrKit.Torus(1f, 0.09f, 48, 8);

        /// <summary>White Hem-o-lok clip, closed: two legs across the structure (local x), hinge at -x, latch at +x.
        /// Local z runs along the structure, y toward the viewer.</summary>
        public static Transform ClipMesh(Transform parent, LapCholeKit kit, float length, float gap = -1f)
        {
            var t = new GameObject("Hem-o-lok clip").transform;
            t.SetParent(parent, false);
            if (gap < 0f) gap = length * 0.13f;
            foreach (int s in new[] { -1, 1 })
                SurgicalField.Add(t, "Leg", SurgicalField.RoundedBox(new Vector3(length, 0.0011f, 0.0017f), 0.0005f, 2), kit.clip, new Vector3(0f, gap * s, 0f), Quaternion.identity, Vector3.one);
            SurgicalField.Add(t, "Hinge", OrKit.Torus(1f, 0.3f, 16, 6), kit.clip, new Vector3(-length * 0.5f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one * (gap + 0.0006f));
            SurgicalField.Add(t, "Latch", SurgicalField.RoundedBox(new Vector3(0.0018f, gap * 2f + 0.0022f, 0.0019f), 0.0007f, 2), kit.clip, new Vector3(length * 0.5f, 0f, 0f), Quaternion.identity, Vector3.one);
            return t;
        }

        public void Destroy() { foreach (Material m in owned) if (m != null) Object.Destroy(m); if (ring != null) Object.Destroy(ring); }

        // ---------- fluoroscopy-style cholangiogram ----------
        /// <summary>Render the cholangiogram: bile tree points (2D, 0..1) with their contrast arrival order (0..1).
        /// fill 0..1; leak = contrast pooling at the ductotomy; outside = catheter not in the duct (no filling).</summary>
        public static void DrawCholangiogram(Texture2D tex, List<Vector3> tree, Vector2 ductotomy, float fill, bool leak, bool outside, int seed = 5)
        {
            int w = tex.width, h = tex.height;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    float n = SurgicalField.Fbm(u, v, 6, 3, seed) * 0.12f;
                    float rib = 0.05f * Mathf.Sin((v * 9f + u * 2.5f) * Mathf.PI) * Mathf.Clamp01((u - 0.55f) * 3f);
                    float g = r > 0.49f ? 0.02f : Mathf.Clamp01(0.62f + n + rib - r * 0.35f);
                    byte b = (byte)(g * 255);
                    px[y * w + x] = new Color32(b, b, b, 255);
                }
            void Disc(Vector2 c, float rad, float dark)
            {
                int x0 = Mathf.FloorToInt((c.x - rad) * w), x1 = Mathf.CeilToInt((c.x + rad) * w);
                int y0 = Mathf.FloorToInt((c.y - rad) * h), y1 = Mathf.CeilToInt((c.y + rad) * h);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                    {
                        float d = Vector2.Distance(new Vector2(x / (float)w, y / (float)h), c) / rad;
                        if (d > 1f) continue;
                        int i = y * w + x;
                        float k = dark * Mathf.Clamp01((1f - d) * 3f);
                        byte old = px[i].r;
                        byte nv = (byte)Mathf.Lerp(old, 18, k);
                        if (nv < old) px[i] = new Color32(nv, nv, nv, 255);
                    }
            }
            if (!outside && !leak)
                foreach (Vector3 p in tree) if (p.z <= fill) Disc(new Vector2(p.x, p.y), 0.016f, 0.95f);
            if (leak)
            {
                foreach (Vector3 p in tree) if (p.z <= fill * 0.25f) Disc(new Vector2(p.x, p.y), 0.014f, 0.8f);
                var rng = new System.Random(seed);
                for (int k = 0; k < 14; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * 0.06f * fill;
                    Disc(ductotomy + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, 0.02f + 0.03f * fill * (float)rng.NextDouble(), 0.6f);
                }
            }
            if (outside)
            {
                var rng = new System.Random(seed + 1);
                for (int k = 0; k < 10; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * 0.08f * fill;
                    Disc(ductotomy + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, 0.03f * fill + 0.01f, 0.55f);
                }
            }
            // Catheter: a thin dark line coming in from the upper left to the ductotomy.
            for (float t = 0f; t <= 1f; t += 0.02f) Disc(Vector2.Lerp(new Vector2(0.05f, 0.92f), ductotomy, t), 0.005f, 0.7f);
            tex.SetPixels32(px); tex.Apply(false);
        }
    }
}
