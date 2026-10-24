using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Laparoscopic specimen retrieval on the exercise frame (board-local metres: x right, y up, z away from the
    // trainee). Six small specimens (gallstones / tissue fragments) lie on the operative field on the left; the
    // trainee lifts each with a grasper and drops it into the open retrieval bag on the right. The exercise id stays
    // "peg_transfer": objects are still peg_N, targets target_N (the N-th resting spot inside the bag), and events
    // keep board-local source / target. Wound closure hides all of this and shows the plain practice base instead.
    public sealed class PegBoard : MonoBehaviour
    {
        public const int ObjectCount = 6;
        /// <summary>Centre of the retrieval bag opening on the bed, and its opening radius.</summary>
        public static readonly Vector3 BagCenter = new Vector3(0.10f, 0f, 0.0f);
        public const float BagRadius = 0.058f, BagRingHeight = 0.05f;
        [Tooltip("A specimen released within this distance of the bag axis (horizontal) counts as retrieved.")]
        public float targetRadius = 0.058f;
        [Tooltip("Release height band above the bed that still drops into the bag.")]
        public float targetHeightTolerance = 0.13f;
        public Material boardMaterial;
        public Material pegMaterial;
        public Material targetMaterial;
        public Material toolMaterial;
        public ExerciseStateMachine exercise;
        public readonly List<PegObject> Objects = new List<PegObject>();
        private readonly List<Renderer> targets = new List<Renderer>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<GameObject> pegContent = new List<GameObject>(); // the operative field: hidden in wound closure
        private readonly List<GameObject> woundOnly = new List<GameObject>();  // plain practice base under the wound pad
        public bool PegContentVisible { get; private set; } = true;

        // Where each specimen starts (x, z) and its size; heights follow the tissue bed.
        private static readonly Vector3[] Spots =
        {
            new Vector3(-0.150f, 0.0105f, -0.085f), new Vector3(-0.085f, 0.0095f, -0.110f), new Vector3(-0.120f, 0.0120f, -0.020f),
            new Vector3(-0.050f, 0.0100f, -0.045f), new Vector3(-0.140f, 0.0110f, 0.050f), new Vector3(-0.065f, 0.0125f, 0.035f),
        };
        public static float SpecimenRadius(int index) => Spots[index].y;
        public Vector3 Source(int index) => new Vector3(Spots[index].x, BedY(Spots[index].x, Spots[index].z) + Spots[index].y * 0.8f, Spots[index].z);
        /// <summary>Resting spot N inside the bag: one in the middle, five around it.</summary>
        public Vector3 Target(int index)
        {
            Vector3 c = BagCenter;
            if (index > 0)
            {
                float a = (index - 1) * Mathf.PI * 0.4f + 0.3f;
                c += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.028f;
            }
            return new Vector3(c.x, BagBottom + 0.0115f, c.z);
        }

        // ---------- tissue bed shape (shared with the preview room so organs and the bag sit on it) ----------
        public const float BedRx = 0.31f, BedRz = 0.25f;
        public static float BedY(float x, float z)
        {
            float y = 0.004f * Mathf.Sin(x * 21f + 0.7f) * Mathf.Cos(z * 17f - 0.4f) + 0.0025f * Mathf.Sin(x * 47f + z * 29f);
            float e = Mathf.Sqrt((x / BedRx) * (x / BedRx) + (z / BedRz) * (z / BedRz));
            return y - 0.045f * Mathf.SmoothStep(0f, 1f, (e - 0.8f) / 0.2f) - 0.03f * Mathf.Max(0f, e - 1f);
        }
        private static float bagBottom = float.NaN;
        public static float BagBottom
        {
            get
            {
                if (!float.IsNaN(bagBottom)) return bagBottom;
                float top = -1f;
                for (int k = 0; k < 64; k++)
                    for (int r = 0; r <= 3; r++)
                    {
                        float a = k * Mathf.PI / 32f, d = BagRadius * 1.1f * r / 3f;
                        top = Mathf.Max(top, BedY(BagCenter.x + Mathf.Cos(a) * d, BagCenter.z + Mathf.Sin(a) * d));
                    }
                return bagBottom = top + 0.002f;
            }
        }

        public void Build()
        {
            if (Objects.Count > 0) return;
            boardMaterial = boardMaterial != null ? boardMaterial : MakeMaterial(new Color(0.86f, 0.86f, 0.83f));
            toolMaterial = toolMaterial != null ? toolMaterial : Own(SurgicalField.Standard(new Color(0.72f, 0.74f, 0.77f), 0.78f, 0.9f));
            // Plain practice base: only under the wound-closure pad.
            Material baseMaterial = MakeMaterial(new Color(0.18f, 0.19f, 0.21f));
            woundOnly.Add(Primitive("Board base", PrimitiveType.Cube, new Vector3(0, -0.03f, 0), new Vector3(0.40f, 0.012f, 0.46f), baseMaterial));
            woundOnly.Add(Primitive("Board", PrimitiveType.Cube, new Vector3(0, -0.012f, 0), new Vector3(0.38f, 0.024f, 0.44f), boardMaterial));

            Transform field = new GameObject("Operative field").transform;
            field.SetParent(transform, false);
            pegContent.Add(field.gameObject);
            BuildTissue(field);
            BuildBag(field);

            var stoneTextures = SurgicalField.StoneTextures();
            Color[] stoneColours =
            {
                new Color(0.78f, 0.70f, 0.42f), new Color(0.55f, 0.45f, 0.26f), new Color(0.62f, 0.66f, 0.38f),
                new Color(0.86f, 0.78f, 0.52f), new Color(0.46f, 0.36f, 0.22f), new Color(0.70f, 0.58f, 0.30f),
            };
            for (int i = 0; i < ObjectCount; i++)
            {
                Material stone = Own(SurgicalField.Textured(stoneTextures, new Vector2(1.5f, 1.5f), stoneColours[i], 1.2f));
                if (i == 0) pegMaterial = stone;
                var item = new GameObject("peg_" + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
                item.transform.SetParent(transform, false);
                item.transform.localScale = new Vector3(1.15f, 0.8f, 1f) * SpecimenRadius(i) * (i % 2 == 0 ? 1f : 0.92f);
                item.GetComponent<MeshFilter>().sharedMesh = SurgicalField.Stone(i + 1);
                var renderer = item.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = stone;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                PegObject peg = item.AddComponent<PegObject>();
                peg.Initialize(this, i);
                peg.ResetObject();
                Objects.Add(peg);
                pegContent.Add(item);
            }
            SetPegContentVisible(PegContentVisible);
        }

        private void BuildTissue(Transform field)
        {
            // Tissue bed: an oval of mesentery and bowel wall, gently lobulated, falling away at the rim.
            const int rings = 56, segments = 128;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float r = Mathf.Pow(i / (float)rings, 0.85f), a = j * Mathf.PI * 2f / segments;
                    float x = BedRx * 1.12f * r * Mathf.Cos(a), z = BedRz * 1.12f * r * Mathf.Sin(a);
                    vertices[i * (segments + 1) + j] = new Vector3(x, BedY(x, z), z);
                    uvs[i * (segments + 1) + j] = new Vector2(x * 2.4f, z * 2.4f);
                }
            Mesh bed = SurgicalField.GridMesh("Tissue bed", vertices, uvs, rings, segments, true);
            SurgicalField.Add(field, "Tissue bed", bed, Own(SurgicalField.Tissue(SurgicalField.Mesentery, Vector2.one, 1.1f)),
                Vector3.zero, Quaternion.identity, Vector3.one);

            // Small-bowel loops around the edges of the field.
            Material bowel = Own(SurgicalField.Tissue(SurgicalField.Bowel, new Vector2(1f, 1f), 0.9f));
            Loop(field, bowel, 0.022f, 1, new Vector2(0.17f, -0.23f), new Vector2(0.22f, -0.15f), new Vector2(0.26f, -0.07f), new Vector2(0.215f, 0.01f), new Vector2(0.25f, 0.09f), new Vector2(0.19f, 0.17f));
            Loop(field, bowel, 0.02f, 2, new Vector2(-0.25f, 0.12f), new Vector2(-0.215f, 0.04f), new Vector2(-0.25f, -0.04f), new Vector2(-0.205f, -0.12f), new Vector2(-0.15f, -0.19f));
            Loop(field, bowel, 0.019f, 3, new Vector2(0.0f, 0.125f), new Vector2(0.08f, 0.095f), new Vector2(0.15f, 0.12f), new Vector2(0.2f, 0.085f));
            Loop(field, bowel, 0.017f, 4, new Vector2(0.27f, -0.02f), new Vector2(0.29f, 0.05f), new Vector2(0.26f, 0.13f));
            // Liver edge at the far side with the gallbladder tucked under it.
            Material liver = Own(SurgicalField.Tissue(SurgicalField.Liver, new Vector2(1.5f, 1f), 0.6f));
            SurgicalField.Add(field, "Liver edge", SurgicalField.Blob(new Vector3(0.17f, 0.034f, 0.07f), 5, 0.06f, 0.35f), liver,
                new Vector3(-0.10f, BedY(-0.10f, 0.175f) + 0.012f, 0.178f), Quaternion.Euler(4f, -8f, -3f), Vector3.one);
            Material gall = Own(SurgicalField.Tissue(SurgicalField.Gallbladder, new Vector2(1f, 1f), 0.5f));
            SurgicalField.Add(field, "Gallbladder", SurgicalField.Blob(new Vector3(0.048f, 0.019f, 0.022f), 9, 0.05f, 0.4f), gall,
                new Vector3(0.035f, BedY(0.035f, 0.145f) + 0.012f, 0.148f), Quaternion.Euler(0f, 28f, 6f), Vector3.one);
        }

        private static void Loop(Transform field, Material material, float radius, int seed, params Vector2[] points)
        {
            var controls = new Vector3[points.Length];
            for (int k = 0; k < points.Length; k++)
            {
                float lift = k == 0 || k == points.Length - 1 ? -radius * 0.9f : radius * 0.55f;
                controls[k] = new Vector3(points[k].x, BedY(points[k].x, points[k].y) + lift, points[k].y);
            }
            SurgicalField.Add(field, "Bowel loop", SurgicalField.Tube(controls, radius, 64, 28, seed), material, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        private void BuildBag(Transform field)
        {
            float bottom = BagBottom, top = BagBottom + BagRingHeight;
            Transform bag = new GameObject("Specimen retrieval bag").transform;
            bag.SetParent(field, false);
            bag.localPosition = new Vector3(BagCenter.x, 0f, BagCenter.z);
            Material film = Own(SurgicalField.ClearPlastic(new Color(0.86f, 0.9f, 0.92f, 0.22f), 0.93f));
            Material filmInside = Own(SurgicalField.ClearPlastic(new Color(0.8f, 0.84f, 0.86f, 0.16f), 0.9f));
            SurgicalField.Add(bag, "Pouch inside", SurgicalField.Pouch(BagRadius, top, bottom, true), filmInside, Vector3.zero, Quaternion.identity, Vector3.one, false);
            SurgicalField.Add(bag, "Pouch", SurgicalField.Pouch(BagRadius, top, bottom, false), film, Vector3.zero, Quaternion.identity, Vector3.one, false);
            // Spring ring that holds the mouth open, with a blue cuff.
            targetMaterial = targetMaterial != null ? targetMaterial : Own(SurgicalField.Standard(new Color(0.9f, 0.92f, 0.94f), 0.85f, 0.2f));
            Renderer ring = SurgicalField.Add(bag, "Bag ring", OrKit.Torus(1f, 0.055f, 72, 12), targetMaterial,
                new Vector3(0f, top, 0f), Quaternion.identity, Vector3.one * BagRadius);
            targets.Add(ring);
            Material cuff = Own(SurgicalField.Standard(new Color(0.16f, 0.36f, 0.66f), 0.6f));
            SurgicalField.Add(bag, "Bag cuff", OrKit.Torus(1f, 0.11f, 48, 12), cuff, new Vector3(0f, top - 0.004f, 0f), Quaternion.identity, Vector3.one * BagRadius * 1.005f);
            // Applicator shaft leaving towards the far-right port, and the drawstring.
            Material steel = Own(SurgicalField.Standard(new Color(0.75f, 0.77f, 0.8f), 0.8f, 0.9f));
            Vector3 rim = BagShaftStart - new Vector3(BagCenter.x, 0f, BagCenter.z);
            Vector3 end = BagShaftEnd - new Vector3(BagCenter.x, 0f, BagCenter.z);
            SurgicalField.Add(bag, "Applicator shaft", SurgicalField.Tube(new[] { rim, Vector3.Lerp(rim, end, 0.5f), end }, 0.0055f, 24, 16, 0, 0f), steel,
                Vector3.zero, Quaternion.identity, Vector3.one);
            Material thread = Own(SurgicalField.Standard(new Color(0.9f, 0.9f, 0.86f), 0.4f));
            SurgicalField.Add(bag, "Drawstring", SurgicalField.Tube(new[] { rim + Vector3.down * 0.003f, rim + new Vector3(0.01f, -0.012f, -0.03f), rim + new Vector3(0.0f, -0.02f, -0.07f) }, 0.0012f, 20, 8, 0, 0f),
                thread, Vector3.zero, Quaternion.identity, Vector3.one);
        }
        /// <summary>The bag applicator leaves the ring here (board-local) and runs out towards the far-right port.</summary>
        public static Vector3 BagShaftStart => BagCenter + new Vector3(Mathf.Cos(0.5f), 0f, Mathf.Sin(0.5f)) * BagRadius + Vector3.up * (BagBottom + BagRingHeight);
        public static Vector3 BagShaftEnd => BagShaftStart + new Vector3(0.62f, 0.5f, 0.38f).normalized * 0.42f;

        public void SetPegContentVisible(bool visible)
        {
            PegContentVisible = visible;
            if (!visible) ClearHighlights();
            foreach (GameObject item in pegContent) if (item != null) item.SetActive(visible);
            foreach (GameObject item in woundOnly) if (item != null) item.SetActive(!visible);
        }

        private GameObject Primitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject child = GameObject.CreatePrimitive(type);
            child.name = label;
            child.transform.SetParent(transform, false);
            child.transform.localPosition = position;
            child.transform.localScale = scale;
            child.GetComponent<Renderer>().sharedMaterial = material;
            return child;
        }
        private Material Own(Material material) { ownedMaterials.Add(material); return material; }
        public Material MakeMaterial(Color color)
        {
            // URP shaders draw magenta under the built-in renderer, so pick by the active pipeline.
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader shader = (urp ? Shader.Find("Universal Render Pipeline/Lit") : null) ?? Shader.Find("Standard");
            Material material = new Material(shader) { color = color };
            ownedMaterials.Add(material);
            return material;
        }
        public void ResetObjects() { foreach (PegObject peg in Objects) peg.ResetObject(); ClearHighlights(); }
        /// <summary>Released inside the bag mouth (any specimen, any resting spot). The index is kept for the API.</summary>
        public bool IsInsideTarget(int index, Vector3 local)
        {
            Vector2 offset = new Vector2(local.x - BagCenter.x, local.z - BagCenter.z);
            return offset.sqrMagnitude <= targetRadius * targetRadius
                && local.y >= BagBottom - 0.01f && local.y <= BagBottom + targetHeightTolerance;
        }
        public bool IsOverBoard(Vector3 local) => Mathf.Abs(local.x) <= 0.19f && Mathf.Abs(local.z) <= 0.22f && local.y >= -0.03f && local.y <= 0.12f;
        public bool TryTargetIndex(string targetId, out int index)
        {
            index = -1;
            if (targetId == null || !targetId.StartsWith("target_")) return false;
            if (!int.TryParse(targetId.Substring(7), out int number) || number < 1 || number > ObjectCount) return false;
            index = number - 1;
            return true;
        }
        // Every target_N lives in the one bag, so an instructor highlight lights up the bag ring.
        public void HighlightTarget(int index) { ClearHighlights(); if (index >= 0 && index < ObjectCount) foreach (Renderer target in targets) Tint(target, Color.yellow); }
        public void ClearHighlights() { foreach (Renderer target in targets) target.SetPropertyBlock(null); }
        public static void Tint(Renderer renderer, Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        // Board placement is locked during attempts so recentering cannot appear as tool movement.
        public bool Place(Vector3 position, Quaternion rotation)
        {
            if (exercise != null && (exercise.State == ExerciseState.Running || exercise.State == ExerciseState.Paused)) return false;
            transform.SetPositionAndRotation(position, rotation);
            return true;
        }
        public bool Recenter(Transform head)
        {
            if (head == null) return false;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            return Place(head.position + forward * 0.5f - Vector3.up * 0.35f, Quaternion.LookRotation(forward));
        }
        private void OnDestroy() { foreach (Material material in ownedMaterials) Destroy(material); }
    }
}
