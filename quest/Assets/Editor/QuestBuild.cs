using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace Jeremy.EditorTools
{
    // One-shot Quest 3 setup: Android + OpenXR + passthrough scene + APK. Run from the menu or
    // Unity -batchMode -executeMethod Jeremy.EditorTools.QuestBuild.BuildApk
    public static class QuestBuild
    {
        const string ScenePath = "Assets/Scenes/PegTransfer.unity";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("Jeremy/Configure Quest project")]
        public static void Configure()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Jeremy";
            PlayerSettings.productName = "Jeremy.ai";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "ai.jeremy.pegtransfer");
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; // LAN backend during testing

            EnableOpenXR();
            ConfigurePassthroughSupport();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBuild] Android, IL2CPP, ARM64, Vulkan, OpenXR configured.");
        }

        static void EnableOpenXR()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }
            const BuildTargetGroup group = BuildTargetGroup.Android;
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(perTarget);

            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null) { Debug.LogWarning("[QuestBuild] OpenXR settings missing"); return; }
            foreach (var f in openxr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                if (f is MetaQuestFeature || f is OculusTouchControllerProfile) { f.enabled = true; EditorUtility.SetDirty(f); }
            EditorUtility.SetDirty(openxr);
        }

        static void ConfigurePassthroughSupport()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            cfg.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType>
                { OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
            OVRProjectConfig.CommitProjectConfig(cfg);
        }

        [MenuItem("Jeremy/Create PegTransfer scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (prefab == null) throw new FileNotFoundException(RigPrefab);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "OVRCameraRig";

            var mgr = rig.GetComponent<OVRManager>();
            mgr.isInsightPassthroughEnabled = true;
            mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            if (rig.GetComponent<OVRPassthroughLayer>() == null) rig.AddComponent<OVRPassthroughLayer>();

            var head = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var cam = head.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); // transparent so passthrough shows

            var holder = new GameObject("PegTransferRig");
            var pegRig = holder.AddComponent<PegTransferRig>();
            AddHand(rig, "LeftHandAnchor", "OpenXRCustomHandPrefab_L", out pegRig.leftHand, out pegRig.leftSkeleton);
            AddHand(rig, "RightHandAnchor", "OpenXRCustomHandPrefab_R", out pegRig.rightHand, out pegRig.rightSkeleton);
            pegRig.head = head;
            pegRig.leftController = rig.transform.Find("TrackingSpace/LeftControllerAnchor");
            pegRig.rightController = rig.transform.Find("TrackingSpace/RightControllerAnchor");
            pegRig.backendOrigin = ""; // set to http://<pc-lan-ip>:8000 for a connected test
            // Optional: %TEMP%/jeremy-bridge/backend.txt holds a backend origin for a connected headset test
            // (e.g. http://192.168.1.20:8000); LAN HTTP is then allowed for that build only.
            string backendFile = Path.Combine(Path.GetTempPath(), "jeremy-bridge", "backend.txt");
            if (File.Exists(backendFile))
            {
                pegRig.backendOrigin = File.ReadAllText(backendFile).Trim();
                pegRig.allowLanHttp = pegRig.backendOrigin.StartsWith("http://");
            }
            // The headset opens straight into the laparoscopic cholecystectomy.
            var rigSettings = new SerializedObject(pegRig);
            SerializedProperty exerciseField = rigSettings.FindProperty("exercise");
            if (exerciseField != null) { exerciseField.stringValue = "lap_chole"; rigSettings.ApplyModifiedPropertiesWithoutUndo(); }

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[QuestBuild] Scene saved: " + ScenePath);
        }

        // Tracked hand under the rig's hand anchor. The prefab carries OVRHand, OVRSkeleton and the hand mesh.
        static void AddHand(GameObject rig, string anchorName, string prefabName, out OVRHand hand, out OVRSkeleton skeleton)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/" + prefabName + ".prefab");
            if (prefab == null) throw new FileNotFoundException(prefabName);
            var anchor = rig.transform.Find("TrackingSpace/" + anchorName);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            hand = instance.GetComponent<OVRHand>();
            skeleton = instance.GetComponent<OVRSkeleton>();
            // The runtime sends the OpenXR hand skeleton (OculusRuntimeSettings.handSkeletonVersion = OpenXR). The prefab
            // still says the legacy OVR type, which the editor only converts when you open it in the inspector; a
            // mismatch renders the hand rotated 90 degrees with joints off. Set the OpenXR types explicitly.
            bool left = prefabName.EndsWith("_L");
            int xrType = left ? 4 : 5; // OVRPlugin.SkeletonType / MeshType XRHandLeft = 4, XRHandRight = 5
            foreach (Component c in instance.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                SerializedProperty sk = so.FindProperty("_skeletonType"), mesh = so.FindProperty("_meshType");
                bool changed = false;
                if (sk != null) { sk.intValue = xrType; changed = true; }
                if (mesh != null) { mesh.intValue = xrType; changed = true; }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            // Saved inactive: OVRSkeleton looks for its tracking data provider only in Awake, and only one whose
            // skeleton type matches. PegTransferRig corrects the type (OVRHand.OnValidate) and then activates the hand,
            // so Awake runs with the OpenXR type and the skeleton actually follows the tracked hand.
            instance.SetActive(false);
        }

        [MenuItem("Jeremy/Build APK")]
        public static void BuildApk()
        {
            Configure();
            CreateScene();
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Jeremy.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            Debug.Log("[QuestBuild] Build result: " + report.summary.result + " errors=" + report.summary.totalErrors);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
