using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GallbladderTrainer
{
    /// <summary>Asset bundle handed to the factory by the Editor builder (meshes and materials saved as project assets).</summary>
    public sealed class WorkstationAssets
    {
        public Mesh liver, gallbladder, cysticDuct, cysticDuctPick, cysticArtery, cysticArteryPick,
                    commonDuct, hepaticArtery, plate, triangle;
        public Material liverMat, gallbladderMat, cysticDuctMat, cysticArteryMat, commonDuctMat, hepaticArteryMat,
                        pedestalMat, pedestalTopMat, hoverMat, selectedMat, instructorMat,
                        plateShownMat, plateMissingMat, triangleMat, lineMat;
    }

    /// <summary>Builds the complete workstation hierarchy (anatomy, pedestal, UI panels, light) with every reference wired.</summary>
    public static class WorkstationFactory
    {
        static readonly Color Border = new Color(0.20f, 0.255f, 0.30f);
        static readonly Color Panel = new Color(0.09f, 0.11f, 0.13f);
        static readonly Color TextMain = new Color(0.95f, 0.96f, 0.97f);
        static readonly Color TextBody = new Color(0.85f, 0.88f, 0.91f);
        static readonly Color TextDim = new Color(0.62f, 0.69f, 0.76f);
        static readonly Color Cyan = new Color(0.24f, 0.86f, 1f);
        static readonly Color Amber = new Color(0.95f, 0.72f, 0.29f);
        static readonly Color BtnNormal = new Color(0.17f, 0.23f, 0.275f);
        static readonly Color BtnHover = new Color(0.245f, 0.33f, 0.40f);
        static readonly Color BtnPressed = new Color(0.12f, 0.56f, 0.66f);
        static readonly Color BtnDisabled = new Color(0.115f, 0.14f, 0.16f);

        public static GameObject CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<XRUIInputModule>();
            return go;
        }

        public static GameObject Create(WorkstationAssets a)
        {
            var root = new GameObject("GallbladderWorkstation");
            var ctrl = root.AddComponent<TrainingController>();
            var desktop = root.AddComponent<DesktopPointer>();
            desktop.controller = ctrl;
            root.AddComponent<NeutralBackdrop>();

            BuildPedestal(root.transform, a);
            Transform anatomy = BuildAnatomy(root.transform, a, ctrl);
            BuildLight(root.transform, anatomy.localPosition);
            BuildPanels(root.transform, a, ctrl);
            return root;
        }

        // ------------------------------------------------------------------ 3D

        static void BuildPedestal(Transform parent, WorkstationAssets a)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Pedestal";
            Strip(body);
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            body.transform.localScale = new Vector3(0.5f, 0.42f, 0.5f);
            Style(body.GetComponent<Renderer>(), a.pedestalMat);

            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            top.name = "PedestalTop";
            Strip(top);
            top.transform.SetParent(parent, false);
            top.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            top.transform.localScale = new Vector3(0.58f, 0.012f, 0.58f);
            Style(top.GetComponent<Renderer>(), a.pedestalTopMat);
        }

        static Transform BuildAnatomy(Transform parent, WorkstationAssets a, TrainingController ctrl)
        {
            Transform anatomy = NewChild(parent, "Anatomy", new Vector3(0f, 1.30f, 0f));
            ctrl.anatomyRoot = anatomy;

            MeshObject(anatomy, "Liver (schematic)", a.liver, a.liverMat, Vector3.zero);
            MeshObject(anatomy, "CommonHepaticDuct (context)", a.commonDuct, a.commonDuctMat, Vector3.zero);
            MeshObject(anatomy, "HepaticArtery (context)", a.hepaticArtery, a.hepaticArteryMat, Vector3.zero);

            Transform pivot = NewChild(anatomy, "GallbladderPivot", ProceduralAnatomy.GallbladderNeck);
            ctrl.gallbladderPivot = pivot;

            StructureView gb = MakeStructure(pivot, "Gallbladder", StructureIds.Gallbladder, "Gallbladder",
                "Schematic gallbladder. Its lower third (fundus end) lies on the liver; the neck narrows toward the cystic duct.",
                a.gallbladder, a.gallbladder, a.gallbladderMat, Vector3.zero, new Vector3(0.02f, -0.10f, -0.05f), a);
            StructureView duct = MakeStructure(anatomy, "CysticDuct", StructureIds.CysticDuct, "Cystic duct",
                "Schematic duct leaving the gallbladder neck and joining the common hepatic duct. One of the two structures that should be seen entering the gallbladder.",
                a.cysticDuct, a.cysticDuctPick, a.cysticDuctMat, Vector3.zero, new Vector3(-0.02f, 0.122f, -0.09f), a);
            StructureView artery = MakeStructure(anatomy, "CysticArtery", StructureIds.CysticArtery, "Cystic artery",
                "Schematic artery reaching the gallbladder neck. With the cystic duct, it is the second of the two structures that should be seen entering the gallbladder.",
                a.cysticArtery, a.cysticArteryPick, a.cysticArteryMat, Vector3.zero, new Vector3(-0.04f, 0.172f, -0.105f), a);
            Vector3 plateAnchor = new Vector3(ProceduralAnatomy.PlateCenter.x, ProceduralAnatomy.PlateCenter.y,
                ProceduralAnatomy.RightLobeFrontZ(ProceduralAnatomy.PlateCenter.x, ProceduralAnatomy.PlateCenter.y) - 0.01f);
            StructureView plate = MakeStructure(anatomy, "CysticPlate (overlay)", StructureIds.CysticPlate, "Cystic plate (schematic region)",
                "Teaching overlay on the liver surface beneath the gallbladder. Exposing it by separating the lower third of the gallbladder is CVS criterion 2. Approximated region, not a separate organ.",
                a.plate, a.plate, a.plateShownMat, Vector3.zero, plateAnchor, a);

            GameObject tri = MeshObject(anatomy, "HepatocysticTriangle (overlay)", a.triangle, a.triangleMat, Vector3.zero);

            ctrl.structures = new[] { gb, duct, artery, plate };
            ctrl.plateView = plate;
            ctrl.triangleOverlay = tri.GetComponent<Renderer>();
            ctrl.plateShownMaterial = a.plateShownMat;
            ctrl.plateMissingMaterial = a.plateMissingMat;
            return anatomy;
        }

        static StructureView MakeStructure(Transform parent, string goName, string id, string display, string desc,
                                           Mesh mesh, Mesh pick, Material mat, Vector3 pos, Vector3 anchorLocal, WorkstationAssets a)
        {
            GameObject go = MeshObject(parent, goName, mesh, mat, pos);
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = pick;
            var anchor = new GameObject("LabelAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.localPosition = anchorLocal;
            var xr = go.AddComponent<XRSimpleInteractable>();
            var view = go.AddComponent<StructureView>();
            view.Configure(id, display, desc, col, new[] { go.GetComponent<Renderer>() }, anchor.transform,
                           a.hoverMat, a.selectedMat, a.instructorMat, xr);
            return view;
        }

        static void BuildLight(Transform parent, Vector3 target)
        {
            var go = new GameObject("KeyLight");
            go.transform.SetParent(parent, false);
            Vector3 p = new Vector3(0.35f, 2.4f, -1.4f);
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.LookRotation((target - p).normalized, Vector3.up);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 70f; l.innerSpotAngle = 40f; l.range = 5f; l.intensity = 6f;
            l.color = new Color(1f, 0.97f, 0.92f);
            l.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ UI

        static void BuildPanels(Transform parent, WorkstationAssets a, TrainingController c)
        {
            Image border;

            // ---- Task panel (left)
            Canvas task = MakeCanvas(parent, "TaskPanel", 620, 560, new Vector3(-0.80f, 1.42f, -0.22f), new Vector3(0f, -25f, 0f), 0.001f, true, Border, Panel, out border);
            Transform t = task.transform;
            c.taskHeader = MakeText(t, "Header", "TASK", 24, TextDim, TextAnchor.UpperLeft, 28, 20, 564, 36, FontStyle.Bold);
            c.taskTitle = MakeText(t, "Title", "", 36, TextMain, TextAnchor.UpperLeft, 28, 58, 564, 92, FontStyle.Bold);
            c.modeExplore = MakeButton(t, "ModeExplore", "Explore", 26, 28, 160, 180, 64);
            c.modeAssess = MakeButton(t, "ModeAssess", "Assess", 26, 220, 160, 180, 64);
            c.modeReview = MakeButton(t, "ModeReview", "Review", 26, 412, 160, 180, 64);
            c.taskBody = MakeText(t, "Body", "", 28, TextBody, TextAnchor.UpperLeft, 28, 240, 564, 236, FontStyle.Normal);
            c.prevButton = MakeButton(t, "Prev", "< Previous", 24, 28, 488, 270, 56);
            c.nextButton = MakeButton(t, "Next", "Next exercise >", 24, 322, 488, 270, 56);

            // ---- Answers panel (right)
            Canvas ans = MakeCanvas(parent, "AnswersPanel", 620, 560, new Vector3(0.80f, 1.42f, -0.22f), new Vector3(0f, 25f, 0f), 0.001f, true, Border, Panel, out border);
            t = ans.transform;
            MakeText(t, "Header", "ANSWERS", 24, TextDim, TextAnchor.UpperLeft, 28, 20, 564, 36, FontStyle.Bold);
            c.answersStatus = MakeText(t, "Status", "", 28, TextBody, TextAnchor.UpperLeft, 28, 62, 564, 150, FontStyle.Normal);
            c.submitButton = MakeButton(t, "Submit", "Submit selected structure", 26, 28, 224, 564, 84);
            c.demonstratedButton = MakeButton(t, "Demonstrated", ExerciseLibrary.LabelDemonstrated, 26, 28, 224, 564, 84);
            c.notDemonstratedButton = MakeButton(t, "NotDemonstrated", ExerciseLibrary.LabelNotDemonstrated, 26, 28, 324, 564, 84);
            c.requestButton = MakeButton(t, "RequestInstructor", ExerciseLibrary.LabelUnclear, 26, 28, 424, 564, 84);
            c.answersNote = MakeText(t, "Note", "", 20, TextDim, TextAnchor.UpperLeft, 28, 516, 564, 32, FontStyle.Normal);

            // ---- Feedback panel (below the anatomy)
            Canvas fb = MakeCanvas(parent, "FeedbackPanel", 1000, 420, new Vector3(0f, 1.02f, -0.46f), new Vector3(30f, 0f, 0f), 0.001f, true, Border, Panel, out border);
            t = fb.transform;
            c.feedbackHeader = MakeText(t, "Header", "FEEDBACK", 24, TextDim, TextAnchor.UpperLeft, 28, 16, 944, 34, FontStyle.Bold);
            c.feedbackBody = MakeText(t, "Body", "", 25, TextMain, TextAnchor.UpperLeft, 28, 56, 944, 272, FontStyle.Normal);
            c.feedbackFooter = MakeText(t, "Footer", "", 19, TextDim, TextAnchor.UpperLeft, 28, 338, 740, 74, FontStyle.Normal);
            c.retryButton = MakeButton(t, "Retry", "Retry", 28, 800, 346, 172, 62);

            // ---- Controls (compact, bottom)
            Canvas ctl = MakeCanvas(parent, "ControlsPanel", 1118, 112, new Vector3(0f, 0.746f, -0.618f), new Vector3(30f, 0f, 0f), 0.001f, true, Border, Panel, out border);
            t = ctl.transform;
            c.rotateLeft = MakeButton(t, "RotateLeft", "Rotate L", 24, 16, 24, 110, 64);
            c.rotateRight = MakeButton(t, "RotateRight", "Rotate R", 24, 134, 24, 110, 64);
            c.larger = MakeButton(t, "Larger", "Larger", 24, 252, 24, 100, 64);
            c.smaller = MakeButton(t, "Smaller", "Smaller", 24, 360, 24, 100, 64);
            c.resetButton = MakeButton(t, "Reset", "Reset", 24, 468, 24, 90, 64);
            c.labelsButton = MakeButton(t, "Labels", "Labels: On", 24, 566, 24, 140, 64);
            c.overlaysButton = MakeButton(t, "Overlays", "Overlays: On", 24, 714, 24, 160, 64);
            c.demoButton = MakeButton(t, "DemoInstructor", "Demo instructor highlight", 20, 882, 24, 220, 64);

            // ---- Instructor-status / mode badge
            Canvas badge = MakeCanvas(parent, "StatusBadge", 760, 100, new Vector3(0f, 1.93f, -0.10f), Vector3.zero, 0.001f, false, Border, Panel, out border);
            c.badgeBorder = border;
            c.badgeText = MakeText(badge.transform, "Text", "", 28, TextMain, TextAnchor.MiddleCenter, 20, 8, 720, 84, FontStyle.Bold);

            // ---- Integration warning banner (hidden unless something is missing)
            Canvas warn = MakeCanvas(parent, "IntegrationWarning", 1000, 150, new Vector3(0f, 2.10f, -0.10f), Vector3.zero, 0.001f, false, new Color(0.55f, 0.36f, 0.05f), Amber, out border);
            c.warningText = MakeText(warn.transform, "Text", "", 24, new Color(0.10f, 0.07f, 0.0f), TextAnchor.MiddleLeft, 24, 12, 952, 126, FontStyle.Bold);
            c.warningRoot = warn.gameObject;
            warn.gameObject.SetActive(false);

            // ---- Structure label + leader line
            Canvas label = MakeCanvas(parent, "StructureLabel", 330, 84, Vector3.zero, Vector3.zero, 0.0007f, false, Cyan, new Color(0.06f, 0.075f, 0.09f), out border);
            c.labelText = MakeText(label.transform, "Text", "", 34, TextMain, TextAnchor.MiddleCenter, 12, 8, 306, 68, FontStyle.Bold);
            c.labelRoot = label.transform;
            label.gameObject.SetActive(false);

            var lineGo = new GameObject("LabelLine");
            lineGo.transform.SetParent(parent, false);
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.003f;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = a.lineMat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            c.labelLine = lr;
        }

        static Canvas MakeCanvas(Transform parent, string name, float w, float h, Vector3 pos, Vector3 euler, float scale,
                                 bool interactive, Color borderColor, Color bgColor, out Image borderImage)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = Vector3.one * scale;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                go.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            borderImage = MakeImage(go.transform, "Border", borderColor);
            Stretch(borderImage.rectTransform, 0f);
            Image bg = MakeImage(go.transform, "Background", bgColor);
            Stretch(bg.rectTransform, 6f);
            return canvas;
        }

        static UiButton MakeButton(Transform parent, string name, string label, int size, float x, float y, float w, float h)
        {
            RectTransform r = Rect(parent, name);
            TopLeft(r, x, y, w, h);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = ColorBlock.defaultColorBlock;
            cb.normalColor = BtnNormal; cb.highlightedColor = BtnHover; cb.pressedColor = BtnPressed;
            cb.selectedColor = BtnNormal; cb.disabledColor = BtnDisabled;
            cb.colorMultiplier = 1f; cb.fadeDuration = 0.05f;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            Text t = MakeText(r, "Label", label, size, TextMain, TextAnchor.MiddleCenter, 0, 0, w, h, FontStyle.Bold);
            Stretch(t.rectTransform, 8f);
            return new UiButton { button = btn, background = img, label = t };
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor,
                             float x, float y, float w, float h, FontStyle style)
        {
            RectTransform r = Rect(parent, name);
            TopLeft(r, x, y, w, h);
            var t = r.gameObject.AddComponent<Text>();
            t.font = GetFont();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.lineSpacing = 1.05f;
            t.text = content;
            return t;
        }

        static Font GetFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return f;
        }

        static Image MakeImage(Transform parent, string name, Color c)
        {
            RectTransform r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void TopLeft(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform r, float inset)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }

        // ------------------------------------------------------------------ small helpers

        static Transform NewChild(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Style(go.AddComponent<MeshRenderer>(), mat);
            return go;
        }

        static void Style(Renderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
        }
    }
}
