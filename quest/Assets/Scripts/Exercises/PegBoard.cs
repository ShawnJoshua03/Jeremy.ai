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
        public ExerciseStateMachine exercise;
        public readonly List<PegObject> Objects = new List<PegObject>();
        private readonly List<Renderer> targets = new List<Renderer>();
        private readonly List<Material> ownedMaterials = new List<Material>();

        public Vector3 Source(int index) => new Vector3(-0.10f, 0.025f, (index - 2.5f) * 0.065f);
        public Vector3 Target(int index) => new Vector3(0.10f, 0.025f, (index - 2.5f) * 0.065f);

        public void Build()
        {
            if (Objects.Count > 0) return;
            boardMaterial = boardMaterial != null ? boardMaterial : MakeMaterial(new Color(0.08f, 0.15f, 0.20f));
            pegMaterial = pegMaterial != null ? pegMaterial : MakeMaterial(new Color(0.1f, 0.8f, 0.85f));
            targetMaterial = targetMaterial != null ? targetMaterial : MakeMaterial(new Color(0.35f, 0.55f, 0.7f));
            Primitive("Board", PrimitiveType.Cube, new Vector3(0, -0.012f, 0), new Vector3(0.38f, 0.024f, 0.44f), boardMaterial);
            for (int i = 0; i < ObjectCount; i++)
            {
                GameObject marker = Primitive("target_" + (i + 1), PrimitiveType.Cylinder,
                    Target(i) - Vector3.up * 0.022f, new Vector3(targetRadius * 2, 0.002f, targetRadius * 2), targetMaterial);
                marker.GetComponent<Collider>().enabled = false;
                targets.Add(marker.GetComponent<Renderer>());
                GameObject item = Primitive("peg_" + (i + 1), PrimitiveType.Sphere, Source(i), Vector3.one * 0.035f, pegMaterial);
                PegObject peg = item.AddComponent<PegObject>();
                peg.Initialize(this, i);
                Objects.Add(peg);
            }
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
        private Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
