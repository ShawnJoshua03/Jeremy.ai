using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Laparoscopic cholecystectomy anatomy (docs/LAP_CHOLE.md), styled after the Teleflex Hem-o-lok / Endo5 animation:
    // glossy red liver above, green gallbladder retracted over it, green cystic duct, blue common bile duct, salmon
    // hepatic and cystic arteries, and a translucent yellow-green peritoneal veil over Calot's triangle.
    //
    // Built as a child of the board (board-local frame: x toward the feet, y up, +z the patient's right). The organs
    // are authored in a "view frame" (x screen-right, y screen-up toward the liver, z away from the scope) that is
    // placed so the umbilical scope looks straight at Calot's triangle. Hooks for the interactive steps:
    //   Liver, Gallbladder, Fundus, Neck, Peritoneum transforms; CysticDuct / CysticArtery / CommonBileDuct /
    //   HepaticArtery board-local polylines ordered proximal -> distal (proximal = the CBD / hepatic-artery end,
    //   as the contract measures clip and cut distances); DistanceAlong / PointAlong; SetPeritoneumCover;
    //   ScopePose for the laparoscope; StructureRenderer for highlighting.
    public sealed class LapCholeAnatomy : MonoBehaviour
    {
        public const string ExerciseId = "lap_chole";
        /// <summary>Render layer of the organs and internal props (the preview room camera does not draw it).</summary>
        public const int Layer = 29;
        public Transform View { get; private set; }
        public Transform Liver { get; private set; }
        public Transform Gallbladder { get; private set; }
        public Transform Fundus { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Peritoneum { get; private set; }
        public Vector3[] CysticDuct { get; private set; }
        public Vector3[] CysticArtery { get; private set; }
        public Vector3[] CommonBileDuct { get; private set; }
        public Vector3[] HepaticArtery { get; private set; }
        /// <summary>Suggested laparoscope pose (board-local): position, look target and vertical field of view.</summary>
        public Vector3 ScopePosition { get; private set; }
        public Vector3 ScopeTarget { get; private set; }
        public Vector3 ScopeUp { get; private set; }
        public float ScopeFieldOfView => 50f;
        private readonly Dictionary<string, Renderer> renderers = new Dictionary<string, Renderer>();
        private readonly List<Material> owned = new List<Material>();
        private Material veil;
        private float veilAlpha = 1f;

        /// <summary>Default site of the umbilical port (board-local) when no skin height is known (headset).</summary>
        public static readonly Vector3 DefaultUmbilicalPort = new Vector3(0.03f, 0.03f, 0f);
        /// <summary>Calot's triangle, board-local: deep in the right upper quadrant.</summary>
        public static readonly Vector3 CalotCenter = new Vector3(-0.11f, -0.085f, 0.06f);
        private const float ScopeDistance = 0.095f;

        public static LapCholeAnatomy Build(Transform board, Vector3 umbilicalPort)
        {
            var go = new GameObject("Lap chole anatomy");
            go.transform.SetParent(board, false);
            var anatomy = go.AddComponent<LapCholeAnatomy>();
            anatomy.Create(umbilicalPort);
            return anatomy;
        }

        public Renderer StructureRenderer(string id) => renderers.TryGetValue(id, out Renderer r) ? r : null;

        private Material Own(Material m) { owned.Add(m); return m; }

        // Clean, glossy CG look: smooth colour with a faint organic normal map so highlights break up a little.
        private Material Organ(Color color, float smoothness, float bump, SurgicalField.TissueStyle detail)
        {
            Material m = SurgicalField.Standard(color, smoothness);
            if (detail != null)
            {
                m.SetTexture("_BumpMap", SurgicalField.TissueTextures(detail).normal);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
                m.mainTextureScale = new Vector2(2f, 2f);
            }
            return Own(m);
        }

        private void Create(Vector3 port)
        {
            // View frame: origin at Calot's triangle, -z toward the umbilical port (the scope looks along +z).
            Vector3 dir = (CalotCenter - port).normalized;
            Vector3 upHint = new Vector3(-1f, 0.55f, 0f); // screen up = cephalad / anterior: the liver sits at the top
            Quaternion frame = Quaternion.LookRotation(dir, upHint);
            View = new GameObject("View frame").transform;
            View.SetParent(transform, false);
            View.localPosition = CalotCenter; View.localRotation = frame;
            ScopePosition = CalotCenter - dir * ScopeDistance;
            ScopeTarget = CalotCenter + frame * new Vector3(0.006f, 0.02f, 0f);
            ScopeUp = frame * Vector3.up;

            Material liver = Organ(new Color(0.42f, 0.075f, 0.055f), 0.58f, 0.2f, SurgicalField.Liver);
            Material gall = Organ(new Color(0.08f, 0.3f, 0.15f), 0.66f, 0.15f, SurgicalField.Gallbladder);
            Material duct = Organ(new Color(0.22f, 0.58f, 0.32f), 0.82f, 0.1f, null);
            Material bile = Organ(new Color(0.08f, 0.28f, 0.78f), 0.84f, 0.1f, null);
            Material artery = Organ(new Color(0.95f, 0.52f, 0.42f), 0.75f, 0.1f, null);
            Material arteryThin = Organ(new Color(0.96f, 0.66f, 0.58f), 0.7f, 0f, null);
            Material bowel = Organ(new Color(0.42f, 0.16f, 0.12f), 0.6f, 0.3f, SurgicalField.Bowel);

            // Liver: one big smooth lobe filling the top of the view, its inferior surface toward the scope.
            Liver = Part("Liver", SurgicalField.Blob(new Vector3(0.13f, 0.042f, 0.08f), 3, 0.035f, 0.55f, 48, 80), liver,
                new Vector3(0.012f, 0.064f, 0.05f), Quaternion.Euler(-24f, 0f, 5f)).transform;
            renderers["liver"] = Liver.GetComponent<Renderer>();
            // Duodenum / omentum low in the background, mostly lost in the haze.
            Part("Duodenum", SurgicalField.Blob(new Vector3(0.09f, 0.035f, 0.05f), 7, 0.06f, 0.2f), bowel,
                new Vector3(0.02f, -0.13f, 0.09f), Quaternion.Euler(10f, 20f, -12f));

            // Gallbladder: neck (t = 0) to the fundus (t = 1), retracted up over the liver edge.
            var gb = new[]
            {
                new Vector3(0.0f, -0.004f, 0.004f), new Vector3(-0.008f, 0.008f, 0.0f), new Vector3(-0.03f, 0.021f, -0.006f),
                new Vector3(-0.058f, 0.036f, -0.008f), new Vector3(-0.082f, 0.05f, -0.004f), new Vector3(-0.1f, 0.062f, 0.004f),
            };
            Gallbladder = Part("Gallbladder", SurgicalField.Tube(gb, t =>
            {
                float body = Mathf.Lerp(0.0048f, 0.0155f, Mathf.SmoothStep(0f, 1f, t / 0.5f));
                float cap = t > 0.86f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - 0.86f) / 0.14f, 2f))) : 1f;
                return body * cap * (1f + 0.04f * Mathf.Sin(t * 23f));
            }, 120, 40), gall, Vector3.zero, Quaternion.identity).transform;
            renderers["gallbladder"] = Gallbladder.GetComponent<Renderer>();
            Fundus = Marker("Fundus", SurgicalField.CurvePoint(gb, 0.93f));
            Neck = Marker("Neck", SurgicalField.CurvePoint(gb, 0.04f));

            // Cystic duct: CBD junction (proximal) up to the gallbladder neck.
            var cd = new[]
            {
                new Vector3(-0.005f, -0.064f, 0.013f), new Vector3(0.001f, -0.046f, 0.008f), new Vector3(0.003f, -0.026f, 0.005f),
                new Vector3(0.002f, -0.01f, 0.004f), new Vector3(0.0f, -0.002f, 0.004f),
            };
            renderers["cystic_duct"] = Part("Cystic duct", SurgicalField.Tube(cd, t => 0.0043f * (1f + 0.06f * Mathf.Sin(t * 40f)), 80, 28), duct,
                Vector3.zero, Quaternion.identity).GetComponent<Renderer>();
            // Common bile duct: common hepatic duct from under the liver down past the junction.
            var cbd = new[]
            {
                new Vector3(0.012f, -0.17f, 0.012f), new Vector3(0.004f, -0.11f, 0.014f), new Vector3(-0.006f, -0.064f, 0.016f),
                new Vector3(-0.014f, -0.015f, 0.022f), new Vector3(-0.02f, 0.025f, 0.032f), new Vector3(-0.024f, 0.05f, 0.045f),
            };
            renderers["cbd"] = Part("Common bile duct", SurgicalField.Tube(cbd, t => 0.0064f, 96, 32), bile,
                Vector3.zero, Quaternion.identity).GetComponent<Renderer>();
            // Hepatic artery (right branch) on the right, and the cystic artery branching from it to the neck.
            var ha = new[]
            {
                new Vector3(0.034f, -0.17f, 0.022f), new Vector3(0.028f, -0.1f, 0.019f), new Vector3(0.024f, -0.055f, 0.018f),
                new Vector3(0.022f, -0.015f, 0.026f), new Vector3(0.012f, 0.025f, 0.038f), new Vector3(0.004f, 0.05f, 0.05f),
            };
            renderers["hepatic_artery"] = Part("Hepatic artery", SurgicalField.Tube(ha, t => 0.0046f, 96, 28), artery,
                Vector3.zero, Quaternion.identity).GetComponent<Renderer>();
            var ca = new[]
            {
                new Vector3(0.024f, -0.058f, 0.018f), new Vector3(0.022f, -0.036f, 0.009f), new Vector3(0.016f, -0.016f, 0.002f),
                new Vector3(0.009f, -0.0f, -0.002f), new Vector3(0.004f, 0.01f, -0.004f),
            };
            renderers["cystic_artery"] = Part("Cystic artery", SurgicalField.Tube(ca, t => 0.0029f * (t > 0.94f ? Mathf.Sqrt((1f - t) / 0.06f) : 1f), 80, 24), artery,
                Vector3.zero, Quaternion.identity).GetComponent<Renderer>();
            // Superficial branch running along the top of the gallbladder.
            var sb = new[] { new Vector3(0.004f, 0.01f, -0.004f), new Vector3(-0.02f, 0.026f, -0.016f), new Vector3(-0.048f, 0.042f, -0.019f), new Vector3(-0.076f, 0.056f, -0.016f) };
            Part("Cystic artery branch", SurgicalField.Tube(sb, t => 0.0015f * (t > 0.9f ? Mathf.Sqrt((1f - t) / 0.1f) : 1f), 60, 16), arteryThin, Vector3.zero, Quaternion.identity);

            // Peritoneal veil over Calot's triangle (cleared during dissection).
            veil = Own(SurgicalField.Standard(new Color(0.74f, 0.84f, 0.55f, 0.62f), 0.45f));
            MakeFade(veil);
            veil.mainTexture = VeilTexture();
            Peritoneum = Part("Peritoneum over Calot's triangle", VeilMesh(), veil, Vector3.zero, Quaternion.identity, false).transform;
            renderers["peritoneum"] = Peritoneum.GetComponent<Renderer>();

            CysticDuct = ToBoard(cd); CysticArtery = ToBoard(ca); CommonBileDuct = ToBoard(cbd); HepaticArtery = ToBoard(ha);
            SetLayer(transform, Layer);
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private Transform Marker(string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(View, false); t.localPosition = local;
            return t;
        }

        private GameObject Part(string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, bool shadows = true) =>
            SurgicalField.Add(View, name, mesh, material, position, rotation, Vector3.one, shadows).gameObject;

        // Polyline (board-local) sampled along the view-frame curve.
        private Vector3[] ToBoard(Vector3[] controls, int samples = 48)
        {
            var points = new Vector3[samples + 1];
            for (int i = 0; i <= samples; i++)
                points[i] = transform.InverseTransformPoint(View.TransformPoint(SurgicalField.CurvePoint(controls, i / (float)samples)));
            return points;
        }

        /// <summary>Distance (metres) along a polyline from its proximal end to the closest point to p (board-local).</summary>
        public static float DistanceAlong(Vector3[] path, Vector3 p, out float offPath)
        {
            float best = float.MaxValue, along = 0f, run = 0f;
            offPath = float.MaxValue;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = path[i - 1], ab = path[i] - a;
                float len = ab.magnitude, t = len > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / (len * len)) : 0f;
                float d = Vector3.Distance(p, a + ab * t);
                if (d < best) { best = d; along = run + t * len; }
                run += len;
            }
            offPath = best;
            return along;
        }

        /// <summary>Board-local point at a distance (metres) from the proximal end of a polyline.</summary>
        public static Vector3 PointAlong(Vector3[] path, float metres)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float len = Vector3.Distance(path[i - 1], path[i]);
                if (metres <= len) return Vector3.Lerp(path[i - 1], path[i], len > 0f ? metres / len : 0f);
                metres -= len;
            }
            return path[path.Length - 1];
        }

        /// <summary>1 = the peritoneum fully covers Calot's triangle, 0 = cleared (critical view of safety).</summary>
        public void SetPeritoneumCover(float amount)
        {
            veilAlpha = Mathf.Clamp01(amount);
            if (Peritoneum != null) Peritoneum.gameObject.SetActive(veilAlpha > 0.01f);
            Color c = veil.color; c.a = 0.62f * veilAlpha; veil.color = c;
        }

        private static void MakeFade(Material m)
        {
            m.SetFloat("_Mode", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // Wispy fatty membrane: streaky alpha, brighter yellow where thicker.
        private static Texture2D VeilTexture()
        {
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Peritoneal veil", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = SurgicalField.Fbm(u, v, 4, 4, 61), streak = SurgicalField.Fbm(u * 0.25f, v, 8, 3, 67);
                    float a = Mathf.Clamp01(0.35f + n * 1.6f + streak * 1.4f);
                    a *= a;
                    px[y * size + x] = new Color(1f, 1f, 0.85f + 0.15f * a, a);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        // A gently bulging sheet spanning the triangle between the duct, the bile duct and the liver edge.
        private static Mesh VeilMesh()
        {
            Vector3 a = new Vector3(-0.055f, 0.04f, -0.012f), b = new Vector3(0.028f, 0.034f, 0.004f);
            Vector3 c = new Vector3(0.022f, -0.08f, 0.002f), d = new Vector3(-0.04f, -0.08f, -0.006f);
            const int n = 24;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float u = j / (float)n, v = i / (float)n;
                    Vector3 p = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
                    p.z -= 0.009f * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI);
                    vertices[i * (n + 1) + j] = p;
                    uvs[i * (n + 1) + j] = new Vector2(u, v);
                }
            Mesh front = SurgicalField.GridMesh("Veil", vertices, uvs, n, n, true);
            // Two-sided: append the flipped copy.
            var tris = front.triangles;
            var all = new int[tris.Length * 2];
            tris.CopyTo(all, 0);
            for (int k = 0; k < tris.Length; k += 3) { all[tris.Length + k] = tris[k]; all[tris.Length + k + 1] = tris[k + 2]; all[tris.Length + k + 2] = tris[k + 1]; }
            front.triangles = all;
            return front;
        }

        private void OnDestroy() { foreach (Material m in owned) if (m != null) Destroy(m); }
    }
}
