using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    public sealed class PegBoard : MonoBehaviour
    {
        public const int ObjectCount = 6;
        public float targetRadius = 0.027f;
        public float targetHeightTolerance = 0.045f;
        public Material boardMaterial;
        public Material pegMaterial;
        public Material targetMaterial;
        public Material toolMaterial;
        public ExerciseStateMachine exercise;
        public readonly List<PegObject> Objects = new List<PegObject>();
        private readonly List<Renderer> targets = new List<Renderer>();
        private readonly List<Material> ownedMaterials = new List<Material>();

        public Vector3 Source(int index) => new Vector3(-0.10f, 0.025f, (index - 2.5f) * 0.065f);
        public Vector3 Target(int index) => new Vector3(0.10f, 0.025f, (index - 2.5f) * 0.065f);

        public void Build()
        {
            if (Objects.Count > 0) return;
            // FLS-style pegboard: light board, metal posts, rings that sit on the posts.
            boardMaterial = boardMaterial != null ? boardMaterial : MakeMaterial(new Color(0.86f, 0.86f, 0.83f));
            pegMaterial = pegMaterial != null ? pegMaterial : MakeMaterial(new Color(0.92f, 0.32f, 0.18f));
            targetMaterial = targetMaterial != null ? targetMaterial : MakeMaterial(new Color(0.25f, 0.55f, 0.85f));
            Material postMaterial = MakeMaterial(new Color(0.55f, 0.57f, 0.6f));
            toolMaterial = toolMaterial != null ? toolMaterial : MakeMaterial(new Color(0.62f, 0.65f, 0.7f));
            Material baseMaterial = MakeMaterial(new Color(0.18f, 0.19f, 0.21f));
            Primitive("Board base", PrimitiveType.Cube, new Vector3(0, -0.03f, 0), new Vector3(0.40f, 0.012f, 0.46f), baseMaterial);
            Primitive("Board", PrimitiveType.Cube, new Vector3(0, -0.012f, 0), new Vector3(0.38f, 0.024f, 0.44f), boardMaterial);
            Mesh ring = RingMesh(0.016f, 0.005f, 28, 10);
            for (int i = 0; i < ObjectCount; i++)
            {
                GameObject marker = Primitive("target_" + (i + 1), PrimitiveType.Cylinder,
                    Target(i) - Vector3.up * 0.022f, new Vector3(targetRadius * 2, 0.002f, targetRadius * 2), targetMaterial);
                marker.GetComponent<Collider>().enabled = false;
                targets.Add(marker.GetComponent<Renderer>());
                Post(Source(i), postMaterial);
                Post(Target(i), postMaterial);
                var item = new GameObject("peg_" + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
                item.transform.SetParent(transform, false);
                item.transform.localPosition = Source(i);
                item.GetComponent<MeshFilter>().sharedMesh = ring;
                item.GetComponent<MeshRenderer>().sharedMaterial = pegMaterial;
                PegObject peg = item.AddComponent<PegObject>();
                peg.Initialize(this, i);
                Objects.Add(peg);
            }
        }

        private void Post(Vector3 center, Material material)
        {
            GameObject post = Primitive("Post", PrimitiveType.Cylinder, new Vector3(center.x, 0.022f, center.z),
                new Vector3(0.007f, 0.022f, 0.007f), material);
            Object.Destroy(post.GetComponent<Collider>());
        }
        // Flat ring (torus) in the board plane: major radius r, tube radius t.
        private static Mesh RingMesh(float r, float t, int segments, int sides)
        {
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var center = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    Vector3 n = center.normalized * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    vertices[i * (sides + 1) + j] = center + n * t;
                    normals[i * (sides + 1) + j] = n;
                }
            }
            var triangles = new int[segments * sides * 6];
            int k = 0;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    triangles[k++] = a0; triangles[k++] = a1; triangles[k++] = b0;
                    triangles[k++] = b0; triangles[k++] = a1; triangles[k++] = b1;
                }
            var mesh = new Mesh { name = "Ring", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
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
        public bool IsInsideTarget(int index, Vector3 local)
        {
            Vector3 offset = local - Target(index);
            return new Vector2(offset.x, offset.z).sqrMagnitude <= targetRadius * targetRadius
                && Mathf.Abs(offset.y) <= targetHeightTolerance;
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
        public void HighlightTarget(int index) { ClearHighlights(); if (index >= 0 && index < targets.Count) Tint(targets[index], Color.yellow); }
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
