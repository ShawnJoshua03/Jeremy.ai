using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GallbladderTrainer.EditorTools
{
    /// <summary>
    /// Tools > Gallbladder Trainer > Build Scene
    /// Idempotent: regenerates only this package's own meshes, materials, prefab and scene (GUIDs are preserved on re-run).
    /// Never touches your other scenes or assets, and asks to save unsaved scene changes before opening the new scene.
    /// </summary>
    public static class TrainingSceneBuilder
    {
        const string Root = "Assets/GallbladderTrainer";
        const string Models = Root + "/Models";
        const string Materials = Root + "/Materials";
        const string Prefabs = Root + "/Prefabs";
        const string Scenes = Root + "/Scenes";
        const string PrefabPath = Prefabs + "/GallbladderWorkstation.prefab";
        const string ScenePath = Scenes + "/GallbladderTraining.unity";
        const string Lit = "Universal Render Pipeline/Lit";
        const string Unlit = "Universal Render Pipeline/Unlit";

        [MenuItem("Tools/Gallbladder Trainer/Build Scene")]
        public static void Build()
        {
            if (Shader.Find(Lit) == null || Shader.Find(Unlit) == null)
            {
                EditorUtility.DisplayDialog("Gallbladder Trainer",
                    "URP Lit/Unlit shaders were not found. This package targets the Universal Render Pipeline.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Gallbladder Trainer",
                    "Build (or rebuild) the trainer?\n\nThis regenerates only the assets inside " + Root +
                    " and overwrites " + ScenePath + ". Your other scenes and assets are not touched.",
                    "Build", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Folder(Root, "Models"); Folder(Root, "Materials"); Folder(Root, "Prefabs"); Folder(Root, "Scenes"); Folder(Root, "Documentation");

            var a = new WorkstationAssets();
            a.liver = SaveMesh("GT_Liver", ProceduralAnatomy.CreateLiver());
            a.gallbladder = SaveMesh("GT_Gallbladder", ProceduralAnatomy.CreateGallbladder());
            a.cysticDuct = SaveMesh("GT_CysticDuct", ProceduralAnatomy.CreateTube(ProceduralAnatomy.CysticDuctPath, 0.0085f, 40, 14, "GT_CysticDuct"));
            a.cysticDuctPick = SaveMesh("GT_CysticDuct_Pick", ProceduralAnatomy.CreateTube(ProceduralAnatomy.CysticDuctPath, 0.020f, 14, 6, "GT_CysticDuct_Pick"));
            a.cysticArtery = SaveMesh("GT_CysticArtery", ProceduralAnatomy.CreateTube(ProceduralAnatomy.CysticArteryPath, 0.0052f, 40, 12, "GT_CysticArtery"));
            a.cysticArteryPick = SaveMesh("GT_CysticArtery_Pick", ProceduralAnatomy.CreateTube(ProceduralAnatomy.CysticArteryPath, 0.016f, 14, 6, "GT_CysticArtery_Pick"));
            a.commonDuct = SaveMesh("GT_CommonHepaticDuct", ProceduralAnatomy.CreateTube(ProceduralAnatomy.CommonDuctPath, 0.011f, 32, 14, "GT_CommonHepaticDuct"));
            a.hepaticArtery = SaveMesh("GT_HepaticArtery", ProceduralAnatomy.CreateTube(ProceduralAnatomy.HepaticArteryPath, 0.006f, 32, 10, "GT_HepaticArtery"));
            a.plate = SaveMesh("GT_CysticPlateRegion", ProceduralAnatomy.CreatePlate());
            a.triangle = SaveMesh("GT_HepatocysticTriangleOutline", ProceduralAnatomy.CreateTriangleOutline());

            a.liverMat = Mat("M_Liver", Lit, C("#7A3B2E"), 0.30f, Color.black, false);
            a.gallbladderMat = Mat("M_Gallbladder", Lit, C("#6E9A52"), 0.40f, Color.black, false);
            a.cysticDuctMat = Mat("M_CysticDuct", Lit, C("#D5CF86"), 0.35f, Color.black, false);
            a.cysticArteryMat = Mat("M_CysticArtery", Lit, C("#B3322B"), 0.35f, Color.black, false);
            a.commonDuctMat = Mat("M_CommonHepaticDuct", Lit, C("#B8B58F"), 0.35f, Color.black, false);
            a.hepaticArteryMat = Mat("M_HepaticArtery", Lit, C("#8F2A25"), 0.35f, Color.black, false);
            a.pedestalMat = Mat("M_Pedestal", Lit, C("#23262B"), 0.25f, Color.black, false);
            a.pedestalTopMat = Mat("M_PedestalTop", Lit, C("#2F353C"), 0.35f, Color.black, false);
            a.hoverMat = Mat("M_Highlight_Hover", Lit, C("#8FEAF8"), 0.3f, C("#8FEAF8") * 0.35f, false);
            a.selectedMat = Mat("M_Highlight_Selected", Lit, C("#18D3FF"), 0.3f, C("#18D3FF") * 0.9f, false);
            a.instructorMat = Mat("M_Highlight_InstructorDemo", Lit, C("#B388FF"), 0.3f, C("#B388FF") * 0.7f, false);
            a.plateShownMat = Mat("M_Overlay_PlateShown", Unlit, C("#5FCB8E"), 0f, Color.black, true);
            a.plateMissingMat = Mat("M_Overlay_PlateNotExposed", Unlit, C("#E8743B"), 0f, Color.black, true);
            a.triangleMat = Mat("M_Overlay_Triangle", Unlit, C("#B9A7F2"), 0f, Color.black, true);
            a.lineMat = Mat("M_LabelLine", Unlit, C("#3DDCFF"), 0f, Color.black, true);

            GameObject temp = WorkstationFactory.Create(a);
            PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Desktop Test Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.085f, 0.10f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 62f;
            camGo.transform.position = new Vector3(0f, 1.55f, -1.7f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.3f, 0f) - camGo.transform.position);

            WorkstationFactory.CreateEventSystem();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            PrefabUtility.InstantiatePrefab(prefab, scene);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.50f);

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(saved
                ? "[GallbladderTrainer] Built " + ScenePath + " and " + PrefabPath + ". Press Play for desktop testing."
                : "[GallbladderTrainer] Scene save failed; see the Console.");
            var sceneAsset = AssetDatabase.LoadAssetAtPath<Object>(ScenePath);
            if (sceneAsset != null) EditorGUIUtility.PingObject(sceneAsset);
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static Color C(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = Models + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Mat(string name, string shaderName, Color color, float smoothness, Color emission, bool cullOff)
        {
            string path = Materials + "/" + name + ".mat";
            Shader sh = Shader.Find(shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh);
            m.shader = sh;
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_EmissionColor"))
            {
                if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
                else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            }
            if (cullOff && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
