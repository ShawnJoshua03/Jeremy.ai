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
            pegRig.head = head;
            pegRig.leftController = rig.transform.Find("TrackingSpace/LeftControllerAnchor");
            pegRig.rightController = rig.transform.Find("TrackingSpace/RightControllerAnchor");
            pegRig.backendOrigin = ""; // set to http://<pc-lan-ip>:8000 for a connected test

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[QuestBuild] Scene saved: " + ScenePath);
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
