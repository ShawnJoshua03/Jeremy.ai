using UnityEngine;

namespace GallbladderTrainer
{
    /// <summary>
    /// Optional neutral VR background: sets the main camera to a solid dark colour and restores it on disable.
    /// Untick applyNeutralBackground if your rig uses passthrough or its own environment.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeutralBackdrop : MonoBehaviour
    {
        [SerializeField] bool applyNeutralBackground = true;
        [SerializeField] Color backgroundColor = new Color(0.07f, 0.085f, 0.10f, 1f);

        Camera cam;
        CameraClearFlags previousFlags;
        Color previousColor;
        bool applied;

        void OnEnable() { TryApply(); }

        void Update()
        {
            if (!applied && applyNeutralBackground) TryApply();
        }

        void TryApply()
        {
            if (!applyNeutralBackground || applied) return;
            cam = Camera.main;
            if (cam == null) return;
            previousFlags = cam.clearFlags;
            previousColor = cam.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            applied = true;
        }

        void OnDisable()
        {
            if (applied && cam != null)
            {
                cam.clearFlags = previousFlags;
                cam.backgroundColor = previousColor;
            }
            applied = false;
        }
    }
}
