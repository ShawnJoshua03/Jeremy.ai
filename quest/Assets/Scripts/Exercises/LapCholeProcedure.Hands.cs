using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Jeremy
{
    public sealed partial class LapCholeProcedure
    {
        const float PickupRadius = 0.06f, HandoverRadius = 0.07f;
        readonly LapTool[] heldTools = new LapTool[2];
        readonly bool[] handWasClosed = new bool[2], handWasTracked = new bool[2];
        readonly bool[] handPressed = new bool[2], inputConsumed = new bool[2];
        readonly Vector3?[] parkedTips = new Vector3?[2];
        int actionHand = 1, entryOwner = -1, umbilicalHand = 1;
        [System.NonSerialized] public Vector3 simLeftGrip, simLeftForward = Vector3.down;
        [System.NonSerialized] public bool simLeftTracked;
        OVRInput.Controller hapticController = OVRInput.Controller.RTouch;
        Transform toolTray, toolTrayGlow;
        readonly Dictionary<LapTool, Vector3> toolSlots = new Dictionary<LapTool, Vector3>();
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        string HandName(int hand) => InstrumentFor(hand)?.instrument ?? (hand == 0 ? "left" : "right");
        string ActionHand => HandName(actionHand);
        string ToolPort(LapTool tool) => tool == grasper ? "rsc_medial" : "epigastric";
        bool HandEmpty(int hand) => heldTools[hand] == null && entryOwner != hand;

        InstrumentController InstrumentFor(int hand)
        {
            if (rig.Instruments == null) return null;
            string side = hand == 0 ? "left" : "right";
            foreach (InstrumentController instrument in rig.Instruments)
                if (instrument != null && instrument.instrument == side) return instrument;
            return null;
        }

        bool HandPoseFor(int hand, out Vector3 grip, out Vector3 tip, out Quaternion rotation, out bool closed)
        {
            grip = tip = Vector3.zero; rotation = Quaternion.identity; closed = false;
            if (simHands)
            {
                Vector3 forward = hand == 0 ? simLeftForward : simForward;
                Vector3 f = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.down;
                grip = hand == 0 ? simLeftGrip : simGrip; tip = grip + f * 0.12f;
                closed = hand == 0 ? simLeftPinch : simPinch;
                rotation = Board.rotation * Quaternion.LookRotation(f, Mathf.Abs(f.y) > 0.95f ? Vector3.right : Vector3.up);
                return hand == 0 ? simLeftTracked : simTracked;
            }
            InstrumentController instrument = InstrumentFor(hand);
            if (instrument == null || instrument.tip == null || !instrument.TrackingValid) return false;
            grip = Board.InverseTransformPoint(instrument.GripPosition);
            tip = Board.InverseTransformPoint(instrument.tip.position);
            rotation = instrument.GripRotation; closed = instrument.Closed;
            return true;
        }

        void ResetToolOwnership()
        {
            entryOwner = -1; actionHand = 1;
            for (int h = 0; h < 2; h++)
            {
                heldTools[h] = null; parkedTips[h] = null;
                handWasClosed[h] = handPressed[h] = inputConsumed[h] = handWasTracked[h] = false;
            }
        }

        void BuildToolTray()
        {
            toolSlots.Clear();
            toolTray = new GameObject("Laparoscopic instrument tray").transform;
            toolTray.SetParent(props, false);
            toolTray.localPosition = TrayOrigin + new Vector3(0f, 0f, -0.25f);
            SurgicalField.Add(toolTray, "Tray", SurgicalField.RoundedBox(new Vector3(0.39f, 0.012f, 0.3f), 0.006f, 3),
                kit.steel, Vector3.zero, Quaternion.identity, Vector3.one);
            LapTool[] set = { grasper, dissector, clipper, scissors, catheter };
            for (int i = 0; i < set.Length; i++)
                toolSlots[set[i]] = toolTray.localPosition + new Vector3(0.14f, 0.04f, -0.11f + i * 0.055f);
            toolTrayGlow = new GameObject("Next instrument glow").transform;
            toolTrayGlow.SetParent(props, false);
            SurgicalField.Add(toolTrayGlow, "Pad", SurgicalField.RoundedBox(new Vector3(0.31f, 0.003f, 0.035f), 0.01f, 3),
                trayGlowMat, new Vector3(-0.13f, -0.02f, 0f), Quaternion.identity, Vector3.one, false);
            toolTray.gameObject.SetActive(false); toolTrayGlow.gameObject.SetActive(false);
        }

        void UpdateToolOwnership()
        {
            bool desktop = rig.desktopPreview && !simHands;
            if (desktop && preview != null) actionHand = preview.ActiveHand;
            for (int h = 0; h < 2; h++)
            {
                bool tracked = HandPoseFor(h, out Vector3 grip, out _, out _, out bool closed);
                if (desktop) { tracked = true; closed = h == actionHand && Mouse.current != null && Mouse.current.leftButton.isPressed; }
                handPressed[h] = tracked && handWasTracked[h] && closed && !handWasClosed[h];
                handWasTracked[h] = tracked; handWasClosed[h] = closed;
                if (!closed) inputConsumed[h] = false;
                if (!tracked) { inputConsumed[h] = true; continue; }
                if (InputOverUi(h)) { if (closed) inputConsumed[h] = true; continue; }
                if (desktop || !handPressed[h]) continue;
                int other = 1 - h;
                // Pickup is latched. Release opens the jaws; a fresh pinch near the tray returns a tool.
                // An empty other hand can take it at the holder's grip; consume both pinches until release.
                if (HandEmpty(h) && !HandEmpty(other)
                    && HandPoseFor(other, out Vector3 otherGrip, out _, out _, out _)
                    && Vector3.Distance(grip, otherGrip) <= HandoverRadius)
                {
                    if (entryOwner == other) entryOwner = h;
                    else { heldTools[h] = heldTools[other]; heldTools[other] = null; }
                    inputConsumed[h] = inputConsumed[other] = true;
                    actionHand = h;
                    Say("Tool handed to the " + (h == 0 ? "left" : "right") + " hand.");
                    continue;
                }
                if (entryOwner == h)
                {
                    if (!entry.Inserted && Vector3.Distance(grip, TraySlot(StepId)) <= PickupRadius) ReturnTool(h);
                }
                else if (heldTools[h] != null)
                {
                    if (Vector3.Distance(grip, toolSlots[heldTools[h]]) <= PickupRadius) ReturnTool(h);
                }
                else if (EntryStepNow() && entryOwner < 0 && Vector3.Distance(grip, TraySlot(StepId)) <= PickupRadius)
                    TakeEntry(h);
                else if (StepIndex >= 6)
                {
                    LapTool nearest = null; float distance = PickupRadius;
                    foreach (var slot in toolSlots)
                    {
                        if (heldTools[other] == slot.Key) continue;
                        float d = Vector3.Distance(grip, slot.Value);
                        if (d < distance) { nearest = slot.Key; distance = d; }
                    }
                    if (nearest != null) TakeTool(h, nearest);
                }
                else if (!EntryStepNow()) actionHand = h; // either hand can run CO2 / inflate the balloon
            }
            if (entryOwner >= 0) actionHand = entryOwner;
            if (!desktop && StepIndex >= 6)
                for (int h = 0; h < 2; h++) if (heldTools[h] == ToolFor(StepId)) actionHand = h;
            equippedTool = heldTools[actionHand]; equippedPortId = equippedTool != null ? ToolPort(equippedTool) : null;
            exchangePending = false;
        }

        void TakeEntry(int hand)
        {
            if (!HandEmpty(hand) || entryOwner >= 0) return;
            entryOwner = actionHand = hand; toolInHand = true; inputConsumed[hand] = true;
            OnPop(0); Say("Picked up the " + entry.Current.name + ". Take it to the purple cross.");
        }

        void TakeTool(int hand, LapTool tool)
        {
            if (!HandEmpty(hand)) { Say("Return your tool to its tray slot before picking up another."); return; }
            int other = 1 - hand;
            if (heldTools[other] == tool) { heldTools[other] = null; inputConsumed[other] = true; }
            heldTools[hand] = tool; parkedTips[hand] = null; actionHand = hand; inputConsumed[hand] = true;
            Say("Picked up the " + ToolLabel(tool) + ". Use the " + PortLabel(ToolPort(tool)) + ".");
        }

        void ReturnTool(int hand)
        {
            if (entryOwner == hand)
            {
                if (entry.Inserted) { Say("Withdraw the entry tool before returning it."); return; }
                entryOwner = -1; toolInHand = false;
            }
            heldTools[hand] = null; parkedTips[hand] = null; inputConsumed[hand] = true;
            Say("Tool returned to the tray.");
        }

        // Ask-coach reserves only a fresh left pinch made with an empty hand, away from all tools and UI.
        public bool CanAskCoach()
        {
            if (!HandEmpty(0) || InputOverUi(0)) return false;
            if (simHands && !simLeftTracked) return true; // legacy simulated coach-only pinch
            if (!HandPoseFor(0, out Vector3 grip, out _, out _, out _)) return false;
            if (StepIndex < 6)
                foreach (string slot in TraySlots) if (Vector3.Distance(grip, TraySlot(slot)) < 0.1f) return false;
            if (StepIndex >= 6)
                foreach (Vector3 slot in toolSlots.Values) if (Vector3.Distance(grip, slot) < 0.1f) return false;
            if (!HandEmpty(1) && HandPoseFor(1, out Vector3 other, out _, out _, out _)
                && Vector3.Distance(grip, other) < 0.1f) return false;
            return true;
        }

        bool InputOverUi(int hand)
        {
            if (simHands) return false;
            if (!rig.desktopPreview) return HandRayPointer.IsOverButton(hand == 0 ? rig.leftHand : rig.rightHand);
            Mouse mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 p = mouse.position.ReadValue();
            if (DesktopToolRect.Contains(new Vector2(p.x, Screen.height - p.y))) return true;
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = p };
            uiHits.Clear(); EventSystem.current.RaycastAll(data, uiHits);
            return uiHits.Count > 0;
        }

        void RenderOwnedTools()
        {
            bool show = Active && StepIndex >= 6;
            toolTray.gameObject.SetActive(show);
            toolTrayGlow.gameObject.SetActive(show && heldTools[0] != ToolFor(StepId) && heldTools[1] != ToolFor(StepId));
            if (!show) return;
            toolTrayGlow.localPosition = toolSlots[ToolFor(StepId)];
            foreach (var slot in toolSlots)
            {
                LapTool tool = slot.Key;
                int owner = heldTools[0] == tool ? 0 : heldTools[1] == tool ? 1 : -1;
                if (owner < 0)
                {
                    tool.SetVisible(true);
                    Vector3 grip = slot.Value;
                    tool.Pose(Board, grip + Vector3.left * 0.26f, grip + Vector3.left * 0.31f, false);
                    continue;
                }
                if (rig.desktopPreview && !simHands)
                {
                    if (owner == actionHand && toolTip.HasValue) parkedTips[owner] = toolTip;
                    Vector3 port = PortPoint(ToolPort(tool));
                    tool.SetVisible(true);
                    tool.Pose(Board, port, parkedTips[owner] ?? port - PortOut(ToolPort(tool)) * 0.018f,
                        tool == grasper && fundusHeld || owner == actionHand && prevAct);
                    continue;
                }
                bool tracked = HandPoseFor(owner, out Vector3 rawGrip, out Vector3 rawTip, out Quaternion rotation, out bool closed);
                tool.SetVisible(tracked);
                if (!tracked) continue;
                Vector3 pivot = PortPoint(ToolPort(tool));
                Vector3 handle = rawGrip - pivot;
                float skin = dressing.Skin(rawGrip.x, rawGrip.z);
                if (!float.IsNaN(skin)) rawGrip.y = Mathf.Max(rawGrip.y, skin + 0.03f);
                handle = rawGrip - pivot;
                Vector3 dir = handle.sqrMagnitude > 1e-6f ? -handle.normalized : -PortOut(ToolPort(tool));
                bool atPort = handle.magnitude > 0.09f / LapDifficulty.Scale && handle.magnitude <= 0.31f + 0.03f * LapDifficulty.Scale
                    && Vector3.Angle(PortOut(ToolPort(tool)), handle) <= 70f * LapDifficulty.Scale;
                if (atPort)
                {
                    Vector3 tip = pivot + dir * Mathf.Clamp(0.31f - handle.magnitude, 0.018f, 0.19f);
                    tool.Pose(Board, pivot, tip, closed || tool == grasper && fundusHeld,
                        Mathf.Clamp(handle.magnitude, 0.03f, 0.55f), rotation);
                }
                else
                {
                    dir = (rawTip - rawGrip).normalized;
                    tool.Pose(Board, rawGrip + dir * 0.26f, rawGrip + dir * 0.31f, closed, 0.26f, rotation);
                }
            }
            // The assistant retains retraction when the trainee returns the grasper to exchange tools.
            if (fundusHeld && heldTools[0] != grasper && heldTools[1] != grasper && anatomy.Fundus != null)
            {
                assistantGrasper.SetVisible(true);
                assistantGrasper.Pose(Board, PortPoint("rsc_lateral"), Board.InverseTransformPoint(anatomy.Fundus.position), true);
            }
        }

        Rect DesktopToolRect => new Rect(12, Screen.height - 92, Mathf.Min(Screen.width - 24, 850), 80);
        void DrawDesktopTools()
        {
            if (rig.Exercise.State != ExerciseState.Running || autopilot) return;
            Rect r = DesktopToolRect;
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Active: " + (preview != null && preview.ActiveHand == 0 ? "LEFT" : "RIGHT")
                + " (Tab) | Left: " + HeldLabel(0) + " | Right: " + HeldLabel(1));
            GUILayout.BeginHorizontal();
            int hand = preview != null ? preview.ActiveHand : actionHand;
            if (EntryStepNow())
            {
                if (GUILayout.Button("Take " + TrayToolName))
                {
                    if (entryOwner >= 0 && entryOwner != hand && HandEmpty(hand)) { entryOwner = hand; inputConsumed[0] = inputConsumed[1] = true; }
                    else TakeEntry(hand);
                }
            }
            else if (StepIndex >= 6)
                foreach (LapTool tool in new[] { grasper, dissector, clipper, scissors, catheter })
                    if (GUILayout.Button(ToolLabel(tool))) TakeTool(hand, tool);
            if (GUILayout.Button("Return tool")) ReturnTool(hand);
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }
        string HeldLabel(int hand) => entryOwner == hand ? TrayToolName : heldTools[hand] != null ? ToolLabel(heldTools[hand]) : "empty";

        IEnumerator DemoPickInstrument(LapTool tool)
        {
            if (heldTools[1] == tool) yield break;
            if (heldTools[1] != null)
            {
                yield return DemoMove(toolSlots[heldTools[1]], Vector3.down, 0.6f);
                yield return DemoPulse();
            }
            yield return DemoMove(toolSlots[tool], Vector3.down, 0.6f);
            yield return DemoPulse();
            yield return DemoMove(toolSlots[tool] + Vector3.up * 0.2f, Vector3.down, 0.4f);
        }
    }
}
