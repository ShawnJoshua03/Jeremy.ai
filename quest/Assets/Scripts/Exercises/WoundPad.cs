using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Wound closure content on the PegBoard frame. Geometry is docs/WOUND_CLOSURE.md, board-local metres;
    // keep it identical to backend scoring.py (WC_*). The pad is a child of the board at its origin.
    // Visuals: a skin surface mesh split along the incision (the cut gapes, rolls into a wet groove and
    // closes as knots are tied), blood along the edges, layered tissue on the pad sides, marker dots,
    // a pulsing halo on the current target, and tied interrupted stitches.
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
        /// <summary>Bite error of one stitch in mm, as the backend averages it: mean of the entry and exit distances.</summary>
        public static float BiteErrorMm(int index, Vector3 entryHit, Vector3 exitHit) =>
            (Vector3.Distance(entryHit, Entry(index)) + Vector3.Distance(exitHit, Exit(index))) * 500f;
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
        public bool IsTied(int index) => index >= 0 && index < SiteCount && stitches[index] != null;

        private const float Bevel = 0.004f, Corner = 0.018f, Thread = 0.00035f;
        private const int Rows = 73, Columns = 22;
        private readonly bool[] placed = new bool[SiteCount];
        private readonly Stitch[] stitches = new Stitch[SiteCount];
        private readonly Renderer[] entryDots = new Renderer[SiteCount], exitDots = new Renderer[SiteCount];
        private Mesh leftSkin, rightSkin, groove, leftBlood, rightBlood;
        private Transform guideHalo;
        private Renderer guideHaloRenderer;
        private readonly Transform[] instructorHalo = new Transform[2];
        private int highlighted = -1, guideSite = -1;
        private bool guideExit, built, dirty = true;
        private float eversion, eversionTarget, eversionZ;

        private sealed class Stitch
        {
            public int index;
            public Vector3 entry, exit;
            public float tension;
            public LineRenderer over, under, tailA, tailB;
            public Transform knot;
            public GameObject root;
        }

        public void Build(PegBoard board)
        {
            if (built) return;
            built = true;
            var slab = new GameObject("Skin pad", typeof(MeshFilter), typeof(MeshRenderer));
            slab.transform.SetParent(transform, false);
            slab.GetComponent<MeshFilter>().sharedMesh = SlabMesh(PadWidth, PadDepth, PadTop, Corner, Bevel, 8);
            slab.GetComponent<MeshRenderer>().sharedMaterial = WoundArt.SlabSide;
            leftSkin = SurfaceObject("Skin (left of incision)", WoundArt.Skin, true);
            rightSkin = SurfaceObject("Skin (right of incision)", WoundArt.Skin, true);
            groove = SurfaceObject("Incision", WoundArt.WoundBed, true);
            leftBlood = SurfaceObject("Edge blood L", WoundArt.Blood, false);
            rightBlood = SurfaceObject("Edge blood R", WoundArt.Blood, false);
            Mesh disc = DiscMesh(20);
            for (int i = 0; i < SiteCount; i++)
            {
                entryDots[i] = WoundArt.MeshObject(transform, SiteId(i) + " entry", disc, WoundArt.Ink, false);
                exitDots[i] = WoundArt.MeshObject(transform, SiteId(i) + " exit", disc, WoundArt.Ink, false);
            }
            Mesh annulus = WoundArt.Annulus();
            guideHaloRenderer = WoundArt.MeshObject(transform, "Target halo", annulus, WoundArt.Glow, false);
            guideHalo = guideHaloRenderer.transform;
            guideHalo.gameObject.SetActive(false);
            Material yellow = WoundArt.Fade(new Color(1f, 0.85f, 0.2f, 0.85f), 0.2f);
            for (int k = 0; k < 2; k++)
            {
                instructorHalo[k] = WoundArt.MeshObject(transform, "Instructor highlight", annulus, yellow, false).transform;
                instructorHalo[k].gameObject.SetActive(false);
            }
            ResetPad();
        }
        public void ResetPad()
        {
            for (int i = 0; i < SiteCount; i++)
            {
                placed[i] = false;
                if (stitches[i] != null) Destroy(stitches[i].root);
                stitches[i] = null;
            }
            highlighted = -1; guideSite = -1;
            eversion = eversionTarget = 0;
            ClearHighlights();
            dirty = true;
            Rebuild();
            RefreshDots();
        }
        /// <summary>Bite completed (entry then exit). The stitch shows once its knot is tied (MarkTied).</summary>
        public void MarkPlaced(int index, Vector3 entryHit, Vector3 exitHit)
        {
            if (index < 0 || index >= SiteCount || placed[index]) return;
            placed[index] = true;
            pendingEntry[index] = entryHit; pendingExit[index] = exitHit;
            RefreshDots();
        }
        private static Mesh sphere;
        private readonly Vector3[] pendingEntry = new Vector3[SiteCount], pendingExit = new Vector3[SiteCount];
        /// <summary>Knot tied: lay the interrupted stitch and let it draw the edges together.</summary>
        public void MarkTied(int index)
        {
            if (index < 0 || index >= SiteCount || !placed[index] || stitches[index] != null) return;
            var stitch = new Stitch { index = index, entry = pendingEntry[index], exit = pendingExit[index] };
            stitch.root = new GameObject(SiteId(index) + " stitch");
            stitch.root.transform.SetParent(transform, false);
            stitch.over = WoundArt.Thread(stitch.root.transform, "Suture over the wound", Thread);
            stitch.under = WoundArt.Thread(stitch.root.transform, "Suture exit limb", Thread);
            stitch.tailA = WoundArt.Thread(stitch.root.transform, "Cut tail", Thread * 0.95f);
            stitch.tailB = WoundArt.Thread(stitch.root.transform, "Cut tail", Thread * 0.95f);
            stitch.knot = new GameObject("Knot").transform;
            stitch.knot.SetParent(stitch.root.transform, false);
            if (sphere == null)
            {
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere = probe.GetComponent<MeshFilter>().sharedMesh;
                Destroy(probe);
            }
            Vector3[] lumps = { new Vector3(0, 0, 0), new Vector3(0.0005f, 0.0001f, 0.0004f), new Vector3(-0.0004f, 0.00005f, -0.0004f) };
            float[] size = { 0.0013f, 0.0010f, 0.0011f };
            for (int k = 0; k < lumps.Length; k++)
            {
                MeshRenderer lump = WoundArt.MeshObject(stitch.knot, "Throw", sphere, WoundArt.Suture);
                lump.transform.localPosition = lumps[k];
                lump.transform.localScale = new Vector3(size[k], size[k] * 0.75f, size[k]);
            }
            stitches[index] = stitch;
            LayStitch(stitch);
            RefreshDots();
        }
        /// <summary>Where the knot of a site sits (board-local), for feedback popups.</summary>
        public Vector3 KnotPoint(int index) => SurfacePoint(new Vector3(0.0035f, 0, SiteZ(index) + 0.0007f)) + Vector3.up * 0.0008f;
        // Kept for older callers; the needle and thread are drawn by SutureGuide.
        public void ShowPending(Vector3 entryLocal, Vector3 tipLocal) { }
        public void HidePending() { }

        /// <summary>Instructor highlight (yellow halos on both marks of a site).</summary>
        public void HighlightTarget(int index)
        {
            ClearHighlights();
            if (index < 0 || index >= SiteCount) return;
            highlighted = index;
            for (int k = 0; k < 2; k++) instructorHalo[k].gameObject.SetActive(true);
        }
        public void ClearHighlights()
        {
            highlighted = -1;
            foreach (Transform halo in instructorHalo) if (halo != null) halo.gameObject.SetActive(false);
        }
        /// <summary>Trainee guidance: the site and mark (entry or exit) to aim at next; -1 hides the halo.</summary>
        public void SetGuide(int site, bool exit)
        {
            if (site == guideSite && exit == guideExit) return;
            guideSite = site; guideExit = exit;
            RefreshDots();
        }
        /// <summary>Forceps lifting (everting) the left edge around board z; amount 0..1.</summary>
        public void SetEversion(float amount, float z)
        {
            eversionTarget = Mathf.Clamp01(amount);
            if (amount > 0) eversionZ = z;
        }

        private void Update()
        {
            if (!built) return;
            float t = Time.time;
            if (Mathf.Abs(eversion - eversionTarget) > 1e-4f) { eversion = Mathf.MoveTowards(eversion, eversionTarget, Time.deltaTime * 4f); dirty = true; }
            foreach (Stitch stitch in stitches)
                if (stitch != null && stitch.tension < 1)
                {
                    stitch.tension = Mathf.MoveTowards(stitch.tension, 1, Time.deltaTime * 1.6f);
                    dirty = true;
                }
            if (dirty)
            {
                Rebuild();
                foreach (Stitch stitch in stitches) if (stitch != null) LayStitch(stitch);
                RefreshDotPositions();
                dirty = false;
            }
            if (guideHalo.gameObject.activeSelf)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);
                guideHalo.localScale = new Vector3(1, 1, 1) * (0.0024f + 0.0009f * pulse);
                guideHaloRenderer.sharedMaterial.SetColor("_EmissionColor", new Color(0.3f, 1f, 0.7f) * (0.8f + 1.4f * pulse));
            }
            if (highlighted >= 0)
            {
                float s = 0.0030f + 0.0006f * Mathf.Sin(t * 5f);
                instructorHalo[0].localPosition = SurfacePoint(Entry(highlighted)) + Vector3.up * 0.0001f;
                instructorHalo[1].localPosition = SurfacePoint(Exit(highlighted)) + Vector3.up * 0.0001f;
                instructorHalo[0].localScale = instructorHalo[1].localScale = Vector3.one * s;
            }
        }

        // ---------- surface model ----------
        private float Pull(float z)
        {
            float pull = 0;
            foreach (Stitch stitch in stitches)
                if (stitch != null) pull += stitch.tension * Mathf.Exp(-Mathf.Pow((z - SiteZ(stitch.index)) / 0.011f, 2));
            return pull;
        }
        /// <summary>Half the gap between the cut edges at z (0 beyond the incision); closes as knots are tied.</summary>
        public float HalfGap(float z)
        {
            float f = Mathf.Abs(z) / IncisionHalfLength;
            if (f >= 1) return 0;
            float shape = Mathf.Pow(1 - f * f, 0.6f);
            return GapWidth / 2 * shape * Mathf.Clamp(1 - 0.88f * Pull(z), 0.08f, 1f);
        }
        private float Eversion(float z) => eversion * Mathf.Exp(-Mathf.Pow((z - eversionZ) / 0.013f, 2));
        // Skin point on one side (sign -1 left, +1 right) at distance d from the cut edge.
        private Vector3 SkinPoint(int sign, float d, float z)
        {
            float hg = HalfGap(z), open = hg / (GapWidth / 2);
            float x = sign * (hg + d), y = PadTop - 0.0008f * open * Mathf.Exp(-d / 0.0007f);
            if (sign < 0)
            {
                float ev = Eversion(z);
                y += ev * (0.0026f * Mathf.Exp(-d / 0.006f) + 0.0008f * Mathf.Exp(-d / 0.0007f));
                x -= ev * 0.0012f * Mathf.Exp(-d / 0.004f);
            }
            foreach (Stitch stitch in stitches)
            {
                if (stitch == null) continue;
                float dz = z - SiteZ(stitch.index), dx = Mathf.Abs(x) - BiteOffset;
                y -= 0.00025f * stitch.tension * Mathf.Exp(-(dx * dx + dz * dz) / (0.0016f * 0.0016f)); // puckered bite holes
            }
            return new Vector3(x, y, z);
        }
        /// <summary>Board-local point on the visible skin above/below `local` (x, z kept).</summary>
        public Vector3 SurfacePoint(Vector3 local)
        {
            float hg = HalfGap(local.z);
            int sign = local.x < 0 ? -1 : 1;
            float d = Mathf.Max(0, Mathf.Abs(local.x) - hg);
            Vector3 p = SkinPoint(sign, d, local.z);
            return new Vector3(local.x, Mathf.Abs(local.x) < hg ? PadTop - 0.002f : p.y, local.z);
        }

        private void Rebuild()
        {
            float wx = PadWidth / 2 - Bevel, dz = PadDepth / 2 - Bevel;
            BuildSkin(leftSkin, -1, wx, dz);
            BuildSkin(rightSkin, 1, wx, dz);
            BuildGroove();
            BuildBlood(leftBlood, -1);
            BuildBlood(rightBlood, 1);
        }
        private void BuildSkin(Mesh mesh, int sign, float wx, float dz)
        {
            var vertices = new Vector3[Rows * Columns];
            var uvs = new Vector2[vertices.Length];
            for (int r = 0; r < Rows; r++)
            {
                float z = -dz + 2 * dz * r / (Rows - 1f);
                float hg = HalfGap(z);
                for (int c = 0; c < Columns; c++)
                {
                    float u = c / (Columns - 1f), xi = hg + (wx - hg) * Mathf.Pow(u, 1.8f); // denser at the cut
                    Vector3 p = ClampRounded(SkinPoint(sign, xi - hg, z), wx, dz);
                    vertices[r * Columns + c] = p;
                    uvs[r * Columns + c] = new Vector2(sign * xi / 0.12f + 0.5f, z / 0.12f);
                }
            }
            if (mesh.vertexCount != vertices.Length)
            {
                mesh.Clear();
                mesh.vertices = vertices;
                var triangles = new List<int>();
                for (int r = 0; r < Rows - 1; r++)
                    for (int c = 0; c < Columns - 1; c++)
                    {
                        int a = r * Columns + c, b = a + 1, d = a + Columns, e = d + 1;
                        // Unity front face normal is Cross(b - a, c - a); flip for the left half (x decreasing with c).
                        if (sign > 0) triangles.AddRange(new[] { a, d, b, b, d, e });
                        else triangles.AddRange(new[] { a, b, d, b, e, d });
                    }
                mesh.SetTriangles(triangles, 0);
            }
            else mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        }
        private static Vector3 ClampRounded(Vector3 p, float wx, float dz)
        {
            float r = Corner - Bevel, cx = wx - r, cz = dz - r;
            if (Mathf.Abs(p.x) <= cx || Mathf.Abs(p.z) <= cz) return p;
            var centre = new Vector2(Mathf.Sign(p.x) * cx, Mathf.Sign(p.z) * cz);
            Vector2 offset = new Vector2(p.x, p.z) - centre;
            if (offset.magnitude <= r) return p;
            offset = offset.normalized * r;
            return new Vector3(centre.x + offset.x, p.y, centre.y + offset.y);
        }
        private void BuildGroove()
        {
            const int across = 9;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            int rows = 0;
            float dz = PadDepth / 2 - Bevel;
            for (int r = 0; r < Rows; r++)
            {
                float z = -dz + 2 * dz * r / (Rows - 1f);
                if (Mathf.Abs(z) > IncisionHalfLength + 0.003f) continue;
                Vector3 left = SkinPoint(-1, 0, z), right = SkinPoint(1, 0, z);
                float f = Mathf.Clamp01(Mathf.Abs(z) / IncisionHalfLength);
                float depth = 0.0042f * Mathf.Sqrt(Mathf.Max(0, 1 - f * f)) * Mathf.Lerp(0.55f, 1f, HalfGap(z) / (GapWidth / 2 + 1e-6f));
                for (int k = 0; k < across; k++)
                {
                    float q = k / (across - 1f) * 2 - 1, w = Mathf.Pow(Mathf.Max(0, 1 - q * q), 0.6f);
                    vertices.Add(Vector3.Lerp(left, right, (q + 1) / 2) + Vector3.down * depth * w);
                    uvs.Add(new Vector2(z / 0.05f, w));
                }
                rows++;
            }
            var triangles = new List<int>();
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < across - 1; k++)
                {
                    int a = r * across + k, b = a + 1, c = a + across, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            groove.Clear();
            groove.SetVertices(vertices); groove.SetUVs(0, uvs); groove.SetTriangles(triangles, 0);
            // Faces must point up out of the groove.
            int[] t = groove.triangles;
            if (t.Length >= 3)
            {
                Vector3 n = Vector3.Cross(vertices[t[1]] - vertices[t[0]], vertices[t[2]] - vertices[t[0]]);
                if (n.y < 0) { for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; } groove.triangles = t; }
            }
            groove.RecalculateNormals(); groove.RecalculateTangents(); groove.RecalculateBounds();
        }
        private void BuildBlood(Mesh mesh, int sign)
        {
            float[] reach = { 0f, 0.0007f, 0.0016f, 0.0028f };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            int rows = 0;
            float dz = PadDepth / 2 - Bevel;
            for (int r = 0; r < Rows; r++)
            {
                float z = -dz + 2 * dz * r / (Rows - 1f);
                if (Mathf.Abs(z) > IncisionHalfLength) continue;
                float fade = Mathf.Clamp01(HalfGap(z) / (GapWidth / 2) * 1.6f);
                for (int k = 0; k < reach.Length; k++)
                {
                    vertices.Add(SkinPoint(sign, reach[k] * (0.4f + 0.6f * fade), z) + Vector3.up * 0.00005f);
                    uvs.Add(new Vector2(k / (reach.Length - 1f) + (1 - fade) * 0.6f, z / 0.04f + (sign > 0 ? 0.37f : 0)));
                }
                rows++;
            }
            var triangles = new List<int>();
            int n = reach.Length;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < n - 1; k++)
                {
                    int a = r * n + k, b = a + 1, c = a + n, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            int[] t = mesh.triangles;
            if (t.Length >= 3 && Vector3.Cross(vertices[t[1]] - vertices[t[0]], vertices[t[2]] - vertices[t[0]]).y < 0)
            {
                for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
                mesh.triangles = t;
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        // ---------- dots, halo, stitches ----------
        private void RefreshDots()
        {
            if (!built) return;
            Color ink = new Color(0.36f, 0.16f, 0.55f), faded = new Color(0.62f, 0.50f, 0.72f);
            for (int i = 0; i < SiteCount; i++)
            {
                bool active = i == guideSite;
                Color c = guideSite < 0 || active || placed[i] ? ink : faded;
                WoundArt.SetGlow(entryDots[i], c, active && !guideExit ? new Color(0.25f, 0.7f, 0.5f) * 0.6f : Color.black);
                WoundArt.SetGlow(exitDots[i], c, active && guideExit ? new Color(0.25f, 0.7f, 0.5f) * 0.6f : Color.black);
            }
            guideHalo.gameObject.SetActive(guideSite >= 0);
            RefreshDotPositions();
        }
        private void RefreshDotPositions()
        {
            for (int i = 0; i < SiteCount; i++)
            {
                entryDots[i].transform.localPosition = SurfacePoint(Entry(i)) + Vector3.up * 0.00006f;
                exitDots[i].transform.localPosition = SurfacePoint(Exit(i)) + Vector3.up * 0.00006f;
                entryDots[i].transform.localScale = exitDots[i].transform.localScale = new Vector3(0.0013f, 0.00005f, 0.0013f);
            }
            if (guideSite >= 0)
                guideHalo.localPosition = SurfacePoint(guideExit ? Exit(guideSite) : Entry(guideSite)) + Vector3.up * 0.00012f;
        }
        private void LayStitch(Stitch stitch)
        {
            float slack = 1 - stitch.tension;
            Vector3 entry = SurfacePoint(stitch.entry) + Vector3.up * 0.0002f, exit = SurfacePoint(stitch.exit) + Vector3.up * 0.0002f;
            Vector3 knot = KnotPoint(stitch.index) + new Vector3(0, slack * 0.0015f, 0);
            stitch.knot.localPosition = knot;
            var over = new Vector3[12];
            for (int k = 0; k < over.Length; k++)
            {
                float t = k / (over.Length - 1f);
                Vector3 p = Vector3.Lerp(entry, knot, t);
                float surface = SurfacePoint(p).y + 0.0002f;
                p.y = Mathf.Max(p.y, surface) + (0.0005f + slack * 0.002f) * Mathf.Sin(t * Mathf.PI) * (1 - t * 0.3f);
                over[k] = p;
            }
            stitch.over.positionCount = over.Length; stitch.over.SetPositions(over);
            var limb = new Vector3[5];
            for (int k = 0; k < limb.Length; k++)
            {
                float t = k / (limb.Length - 1f);
                Vector3 p = Vector3.Lerp(exit, knot, t);
                p.y += (0.0003f + slack * 0.0012f) * Mathf.Sin(t * Mathf.PI);
                limb[k] = p;
            }
            stitch.under.positionCount = limb.Length; stitch.under.SetPositions(limb);
            float tail = Mathf.Lerp(0.012f, 0.0042f, Mathf.Clamp01(stitch.tension * 1.5f)); // long ends until trimmed
            SetTail(stitch.tailA, knot, new Vector3(0.55f, 0, 0.8f), tail);
            SetTail(stitch.tailB, knot, new Vector3(0.75f, 0, -0.6f), tail * 0.9f);
        }
        private void SetTail(LineRenderer line, Vector3 knot, Vector3 direction, float length)
        {
            var points = new Vector3[4];
            for (int k = 0; k < points.Length; k++)
            {
                float t = k / (points.Length - 1f);
                Vector3 p = knot + direction.normalized * length * t;
                p.y = Mathf.Lerp(knot.y, SurfacePoint(p).y + 0.00025f, Mathf.Sqrt(t));
                points[k] = p;
            }
            line.positionCount = points.Length; line.SetPositions(points);
        }

        private Mesh SurfaceObject(string label, Material material, bool shadows)
        {
            var mesh = new Mesh { name = label };
            mesh.MarkDynamic();
            WoundArt.MeshObject(transform, label, mesh, material, shadows);
            return mesh;
        }
        private static Mesh DiscMesh(int segments)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                float wobble = 1 + 0.06f * Mathf.Sin(a * 3 + 1.3f); // hand-drawn marker dot
                vertices.Add(new Vector3(Mathf.Cos(a) * wobble, 0, Mathf.Sin(a) * wobble));
            }
            for (int i = 0; i < segments; i++) triangles.AddRange(new[] { 0, 1 + (i + 1) % segments, 1 + i });
            var mesh = new Mesh { name = "Marker dot" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            int[] t = mesh.triangles;
            if (Vector3.Cross(vertices[t[1]] - vertices[t[0]], vertices[t[2]] - vertices[t[0]]).y < 0)
            {
                for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
                mesh.triangles = t;
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Rounded-rectangle slab without a top cap (the skin surface meshes cover it): bevel and sides down to
        // y = 0. UV v = height fraction so the side texture shows the tissue layers.
        private static Mesh SlabMesh(float width, float depth, float height, float radius, float bevel, int cornerSegments)
        {
            int ringCount = 4 * (cornerSegments + 1);
            float[] inset = { bevel, bevel * 0.35f, 0, 0 };
            float[] y = { height, height - bevel * 0.45f, height - bevel, 0 };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r < 4; r++)
            {
                List<Vector3> ring = Outline(width, depth, radius, inset[r], y[r], cornerSegments);
                for (int i = 0; i < ring.Count; i++)
                {
                    vertices.Add(ring[i]);
                    uvs.Add(new Vector2(i / (float)ring.Count * 6f, y[r] / height));
                }
            }
            for (int i = 0; i < ringCount; i++)
            {
                int next = (i + 1) % ringCount;
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
            mesh.SetUVs(0, uvs);
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
