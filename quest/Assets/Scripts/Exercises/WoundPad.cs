using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Wound closure content on the PegBoard frame. Geometry is docs/WOUND_CLOSURE.md, board-local metres;
    // keep it identical to backend scoring.py (WC_*). The pad is a child of the board at its origin.
    public sealed class WoundPad : MonoBehaviour
    {
        public const string ExerciseId = "wound_closure";
        public const int SiteCount = 5;
        public const float PadTop = 0.012f, PadWidth = 0.20f, PadDepth = 0.18f;
        public const float IncisionHalfLength = 0.065f, GapWidth = 0.004f, BiteOffset = 0.008f;
        public const float ExclusionHalfWidth = 0.003f, GoodBite = 0.004f, MaxBite = 0.008f;
        public static float SiteZ(int index) => -0.05f + 0.025f * index;
        public static Vector3 Entry(int index) => new Vector3(-BiteOffset, PadTop, SiteZ(index));
        public static Vector3 Exit(int index) => new Vector3(BiteOffset, PadTop, SiteZ(index));
        public static string SiteId(int index) => "site_" + (index + 1);
        public static bool TryTargetIndex(string targetId, out int index)
        {
            index = -1;
            if (targetId == null || !targetId.StartsWith("site_")) return false;
            if (!int.TryParse(targetId.Substring(5), out int number) || number < 1 || number > SiteCount) return false;
            index = number - 1;
            return true;
        }
        public static bool OverPad(Vector3 local) => Mathf.Abs(local.x) <= PadWidth / 2 && Mathf.Abs(local.z) <= PadDepth / 2;
        // Tip below the skin surface at the wound edge (just under the pad top down to the board) is tissue damage.
        public static bool InExclusionZone(Vector3 local) => Mathf.Abs(local.x) < ExclusionHalfWidth
            && Mathf.Abs(local.z) <= IncisionHalfLength && local.y < PadTop && local.y > -0.02f;
        /// <summary>Pad volume in board-local space (the board transform is the frame).</summary>
        public static Bounds LocalBounds => new Bounds(new Vector3(0, PadTop / 2, 0), new Vector3(PadWidth, PadTop, PadDepth));
        /// <summary>World-space axis-aligned bounds of the pad (for the OR environment's surgical window).</summary>
        public Bounds WorldBounds
        {
            get
            {
                Bounds local = LocalBounds;
                var result = new Bounds(transform.TransformPoint(local.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    result.Encapsulate(transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return result;
            }
        }
        public bool IsPlaced(int index) => index >= 0 && index < SiteCount && placed[index];

        private const int Segments = 13;
        private readonly bool[] placed = new bool[SiteCount];
        private readonly Renderer[] entryDots = new Renderer[SiteCount], exitDots = new Renderer[SiteCount];
        private readonly Transform[] gaps = new Transform[Segments], leftLips = new Transform[Segments], rightLips = new Transform[Segments];
        private readonly List<GameObject> stitchVisuals = new List<GameObject>();
        private Material skin, lip, incision, ink, suture;
        private LineRenderer pending;
        private int highlighted = -1;
        private bool built;

        public void Build(PegBoard board)
        {
            if (built) return;
            built = true;
            skin = board.MakeMaterial(new Color(0.93f, 0.74f, 0.64f));
            lip = board.MakeMaterial(new Color(0.84f, 0.56f, 0.52f));
            incision = board.MakeMaterial(new Color(0.36f, 0.03f, 0.05f));
            ink = board.MakeMaterial(new Color(0.42f, 0.20f, 0.62f)); // surgical skin marker
            suture = board.MakeMaterial(new Color(0.20f, 0.12f, 0.45f)); // violet braided suture
            skin.SetFloat("_Glossiness", 0.35f); lip.SetFloat("_Glossiness", 0.45f); incision.SetFloat("_Glossiness", 0.7f);
            var pad = new GameObject("Skin pad", typeof(MeshFilter), typeof(MeshRenderer));
            pad.transform.SetParent(transform, false);
            pad.GetComponent<MeshFilter>().sharedMesh = SlabMesh(PadWidth, PadDepth, PadTop, 0.018f, 0.004f, 8);
            pad.GetComponent<MeshRenderer>().sharedMaterial = skin;
            for (int s = 0; s < Segments; s++)
            {
                gaps[s] = Part("Incision", PrimitiveType.Cube, incision);
                leftLips[s] = Part("Wound edge L", PrimitiveType.Cube, lip);
                rightLips[s] = Part("Wound edge R", PrimitiveType.Cube, lip);
            }
            for (int i = 0; i < SiteCount; i++)
            {
                entryDots[i] = Dot(SiteId(i) + " entry", Entry(i));
                exitDots[i] = Dot(SiteId(i) + " exit", Exit(i));
            }
            pending = Line("Needle path", 2, 0.0006f);
            pending.enabled = false;
            ResetPad();
        }
        public void ResetPad()
        {
            for (int i = 0; i < SiteCount; i++) placed[i] = false;
            foreach (GameObject item in stitchVisuals) Destroy(item);
            stitchVisuals.Clear();
            HidePending();
            highlighted = -1;
            foreach (Renderer dot in entryDots) if (dot != null) dot.SetPropertyBlock(null);
            foreach (Renderer dot in exitDots) if (dot != null) dot.SetPropertyBlock(null);
            LayoutIncision();
        }
        public void MarkPlaced(int index, Vector3 entryHit, Vector3 exitHit)
        {
            if (index < 0 || index >= SiteCount || placed[index]) return;
            placed[index] = true;
            var stitch = new GameObject(SiteId(index) + " stitch").transform;
            stitch.SetParent(transform, false);
            stitchVisuals.Add(stitch.gameObject);
            Vector3 lift = Vector3.up * 0.0005f;
            var points = new Vector3[9];
            for (int k = 0; k < points.Length; k++)
            {
                float t = k / (points.Length - 1f);
                points[k] = Vector3.Lerp(entryHit, exitHit, t) + lift + Vector3.up * (0.0016f * 4 * t * (1 - t));
            }
            LineRenderer loop = Line("Suture", points.Length, 0.0007f, stitch);
            loop.SetPositions(points);
            Vector3 knot = exitHit + Vector3.up * 0.0009f;
            Transform bead = Part("Knot", PrimitiveType.Sphere, suture, stitch);
            bead.localPosition = knot; bead.localScale = new Vector3(0.0024f, 0.0016f, 0.0024f);
            LineRenderer tails = Line("Suture tails", 3, 0.0005f, stitch);
            tails.SetPositions(new[] { knot + new Vector3(0.004f, -0.0003f, 0.0025f), knot, knot + new Vector3(0.0045f, -0.0003f, -0.002f) });
            PegBoard.Tint(entryDots[index], new Color(0.25f, 0.62f, 0.4f));
            PegBoard.Tint(exitDots[index], new Color(0.25f, 0.62f, 0.4f));
            LayoutIncision();
        }
        public void ShowPending(Vector3 entryLocal, Vector3 tipLocal)
        {
            if (pending == null) return;
            pending.enabled = true;
            pending.SetPosition(0, entryLocal + Vector3.up * 0.0005f);
            pending.SetPosition(1, tipLocal);
        }
        public void HidePending() { if (pending != null) pending.enabled = false; }
        public void HighlightTarget(int index)
        {
            ClearHighlights();
            if (index < 0 || index >= SiteCount) return;
            highlighted = index;
            PegBoard.Tint(entryDots[index], Color.yellow);
            PegBoard.Tint(exitDots[index], Color.yellow);
        }
        public void ClearHighlights()
        {
            if (highlighted < 0) return;
            int index = highlighted;
            highlighted = -1;
            if (entryDots[index] == null) return;
            if (placed[index])
            {
                PegBoard.Tint(entryDots[index], new Color(0.25f, 0.62f, 0.4f));
                PegBoard.Tint(exitDots[index], new Color(0.25f, 0.62f, 0.4f));
            }
            else { entryDots[index].SetPropertyBlock(null); exitDots[index].SetPropertyBlock(null); }
        }

        // The gap narrows and the edges come together around each placed stitch; the ends taper.
        private void LayoutIncision()
        {
            float length = IncisionHalfLength * 2 / Segments;
            for (int s = 0; s < Segments; s++)
            {
                if (gaps[s] == null) continue;
                float z = -IncisionHalfLength + (s + 0.5f) * length;
                float pull = 0;
                for (int i = 0; i < SiteCount; i++)
                    if (placed[i]) pull += Mathf.Exp(-Mathf.Pow((z - SiteZ(i)) / 0.012f, 2));
                float taper = 1 - Mathf.Pow(Mathf.Abs(z) / IncisionHalfLength, 4);
                float width = Mathf.Max(0.0005f, GapWidth * taper * Mathf.Clamp(1 - 0.7f * pull, 0.3f, 1));
                gaps[s].localPosition = new Vector3(0, PadTop - 0.001f, z);
                gaps[s].localScale = new Vector3(width, 0.0026f, length * 1.02f);
                float edge = width / 2 + 0.0012f;
                leftLips[s].localPosition = new Vector3(-edge, PadTop + 0.0001f, z);
                rightLips[s].localPosition = new Vector3(edge, PadTop + 0.0001f, z);
                Vector3 lipScale = new Vector3(0.0024f, 0.0012f + 0.0006f * taper, length * 1.02f);
                leftLips[s].localScale = rightLips[s].localScale = lipScale;
            }
        }
        private Renderer Dot(string label, Vector3 position)
        {
            Transform dot = Part(label, PrimitiveType.Cylinder, ink);
            dot.localPosition = position + Vector3.up * 0.0001f;
            dot.localScale = new Vector3(0.003f, 0.0004f, 0.003f);
            return dot.GetComponent<Renderer>();
        }
        private Transform Part(string label, PrimitiveType type, Material material, Transform parent = null)
        {
            GameObject child = GameObject.CreatePrimitive(type);
            child.name = label;
            Destroy(child.GetComponent<Collider>());
            child.transform.SetParent(parent != null ? parent : transform, false);
            child.GetComponent<Renderer>().sharedMaterial = material;
            return child.transform;
        }
        private LineRenderer Line(string label, int count, float width, Transform parent = null)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(parent != null ? parent : transform, false);
            line.useWorldSpace = false;
            line.positionCount = count;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = suture;
            return line;
        }
        // Rounded-rectangle slab: flat top, bevelled top edge, vertical sides down to y = 0 (sits on the board).
        private static Mesh SlabMesh(float width, float depth, float height, float radius, float bevel, int cornerSegments)
        {
            int ringCount = 4 * (cornerSegments + 1);
            float[] inset = { bevel, bevel * 0.35f, 0, 0 };
            float[] y = { height, height - bevel * 0.45f, height - bevel, 0 };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r < 4; r++) vertices.AddRange(Outline(width, depth, radius, inset[r], y[r], cornerSegments));
            int top = vertices.Count;
            vertices.AddRange(Outline(width, depth, radius, inset[0], y[0], cornerSegments)); // separate copy keeps the top flat
            int centre = vertices.Count;
            vertices.Add(new Vector3(0, height, 0));
            for (int i = 0; i < ringCount; i++)
            {
                int next = (i + 1) % ringCount;
                Triangle(vertices, triangles, centre, top + next, top + i, Vector3.up);
                for (int r = 0; r < 3; r++)
                {
                    int a = r * ringCount + i, b = r * ringCount + next, c = (r + 1) * ringCount + i, d = (r + 1) * ringCount + next;
                    Vector3 outward = new Vector3(vertices[a].x, 0, vertices[a].z);
                    Triangle(vertices, triangles, a, b, c, outward);
                    Triangle(vertices, triangles, b, d, c, outward);
                }
            }
            var mesh = new Mesh { name = "Skin pad" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        private static List<Vector3> Outline(float width, float depth, float radius, float inset, float y, int cornerSegments)
        {
            var points = new List<Vector3>();
            float r = radius - inset, cx = width / 2 - radius, cz = depth / 2 - radius;
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = corner == 0 || corner == 3 ? 1 : -1, sz = corner < 2 ? 1 : -1;
                for (int k = 0; k <= cornerSegments; k++)
                {
                    float a = (corner + k / (float)cornerSegments) * Mathf.PI / 2;
                    points.Add(new Vector3(sx * cx + Mathf.Cos(a) * r, y, sz * cz + Mathf.Sin(a) * r));
                }
            }
            return points;
        }
        // Unity's front face normal is Cross(b - a, c - a); flip the winding when it disagrees with the intended side.
        private static void Triangle(List<Vector3> v, List<int> triangles, int a, int b, int c, Vector3 facing)
        {
            bool flip = Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), facing) < 0;
            triangles.Add(a); triangles.Add(flip ? c : b); triangles.Add(flip ? b : c);
        }
    }
}
