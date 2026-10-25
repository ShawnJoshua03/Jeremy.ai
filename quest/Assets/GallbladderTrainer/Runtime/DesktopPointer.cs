using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GallbladderTrainer
{
    /// <summary>
    /// Editor/desktop mouse hover and click on structures. Inert when no mouse exists (e.g. on Quest).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopPointer : MonoBehaviour
    {
        [SerializeField] internal TrainingController controller;
        [SerializeField] bool enablePointer = true;
        [SerializeField] float maxDistance = 10f;
        [SerializeField] LayerMask layerMask = ~0;

        Camera cam;
        StructureView hover;

        void Update()
        {
            if (!enablePointer || controller == null) return;
            Vector2 pos; bool clicked;
            if (!TryReadMouse(out pos, out clicked)) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            StructureView hit = null;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUi)
            {
                Ray ray = cam.ScreenPointToRay(pos);
                RaycastHit info;
                if (Physics.Raycast(ray, out info, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
                    hit = info.collider.GetComponentInParent<StructureView>();
            }

            if (hit != hover)
            {
                if (hover != null) controller.NotifyHover(hover, false);
                hover = hit;
                if (hover != null) controller.NotifyHover(hover, true);
            }
            if (clicked && hit != null) controller.NotifyPick(hit);
        }

        static bool TryReadMouse(out Vector2 pos, out bool clicked)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null)
            {
                pos = m.position.ReadValue();
                clicked = m.leftButton.wasPressedThisFrame;
                return true;
            }
            pos = default(Vector2); clicked = false;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pos = Input.mousePosition;
            clicked = Input.GetMouseButtonDown(0);
            return true;
#else
            pos = default(Vector2); clicked = false;
            return false;
#endif
        }
    }
}
