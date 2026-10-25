using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace GallbladderTrainer
{
    public enum HighlightState { None, Hover, Instructor, Selected }

    /// <summary>
    /// One selectable anatomical structure: ID, display name, collider, renderers, label anchor.
    /// Highlighting swaps cached material arrays; the original materials are never modified and are restored on None.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureView : MonoBehaviour
    {
        [SerializeField] string structureId;
        [SerializeField] string displayName;
        [TextArea, SerializeField] string description;
        [SerializeField] Collider pickCollider;
        [SerializeField] Renderer[] renderers;
        [SerializeField] Transform labelAnchor;
        [SerializeField] Material hoverMaterial;
        [SerializeField] Material selectedMaterial;
        [SerializeField] Material instructorMaterial;
        [SerializeField] XRSimpleInteractable interactable;

        public event Action<StructureView> Hovered;
        public event Action<StructureView> Unhovered;
        public event Action<StructureView> Picked;

        Material[][] baseSets, hoverSets, selectedSets, instructorSets;
        HighlightState state = HighlightState.None;
        Material currentBase;
        bool cached;

        public string Id { get { return structureId; } }
        public string DisplayName { get { return displayName; } }
        public string Description { get { return description; } }
        public Transform LabelAnchor { get { return labelAnchor != null ? labelAnchor : transform; } }
        public bool IsPickable { get { return pickCollider != null && pickCollider.enabled; } }

        public void Configure(string id, string name, string desc, Collider col, Renderer[] rs, Transform anchor,
                              Material hover, Material selected, Material instructor, XRSimpleInteractable xr)
        {
            structureId = id; displayName = name; description = desc; pickCollider = col; renderers = rs;
            labelAnchor = anchor; hoverMaterial = hover; selectedMaterial = selected; instructorMaterial = instructor;
            interactable = xr;
        }

        void Awake()
        {
            EnsureCaches();
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null)
            {
                interactable.hoverEntered.AddListener(_ => { if (Hovered != null) Hovered(this); });
                interactable.hoverExited.AddListener(_ => { if (Unhovered != null) Unhovered(this); });
                interactable.selectEntered.AddListener(_ => { if (Picked != null) Picked(this); });
            }
            else
            {
                Debug.LogWarning("[GallbladderTrainer] " + name + " has no XRSimpleInteractable; ray selection disabled for it.", this);
            }
        }

        void EnsureCaches()
        {
            if (cached || renderers == null) return;
            int n = renderers.Length;
            baseSets = new Material[n][]; hoverSets = new Material[n][];
            selectedSets = new Material[n][]; instructorSets = new Material[n][];
            for (int i = 0; i < n; i++)
            {
                Material[] src = renderers[i].sharedMaterials;
                baseSets[i] = src;
                hoverSets[i] = Fill(src.Length, hoverMaterial);
                selectedSets[i] = Fill(src.Length, selectedMaterial);
                instructorSets[i] = Fill(src.Length, instructorMaterial);
            }
            if (n > 0 && baseSets[0].Length > 0) currentBase = baseSets[0][0];
            cached = true;
        }

        static Material[] Fill(int length, Material m)
        {
            var a = new Material[length];
            for (int i = 0; i < length; i++) a[i] = m;
            return a;
        }

        public void SetHighlight(HighlightState s)
        {
            EnsureCaches();
            if (s == state) return;
            state = s;
            Apply();
        }

        /// <summary>Replaces the un-highlighted look (used for the cystic-plate overlay colour).</summary>
        public void OverrideBaseMaterial(Material m)
        {
            EnsureCaches();
            if (m == null || m == currentBase) return;
            currentBase = m;
            for (int i = 0; i < baseSets.Length; i++) baseSets[i] = Fill(baseSets[i].Length, m);
            Apply();
        }

        void Apply()
        {
            Material[][] sets;
            switch (state)
            {
                case HighlightState.Hover: sets = hoverSets; break;
                case HighlightState.Selected: sets = selectedSets; break;
                case HighlightState.Instructor: sets = instructorSets; break;
                default: sets = baseSets; break;
            }
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = sets[i];
        }

        public void SetVisible(bool visible)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
        }

        public void SetPickable(bool pickable)
        {
            if (pickCollider != null) pickCollider.enabled = pickable;
        }
    }
}
