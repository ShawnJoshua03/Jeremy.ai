using UnityEngine;
using GallbladderTrainer.Backend;

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Adds the Jeremy.ai layer (backend hook, movement recorder, robot guide + crew, report panel, replay) to a
    /// workstation. Used by TrainingSceneBuilder at build time; at runtime it also auto-installs on any
    /// TrainingController that lacks it, so older scenes/prefabs work without a rebuild.
    /// </summary>
    public static class JeremyInstaller
    {
        public static readonly Vector3 GuidePosition = new Vector3(0.55f, 2.00f, -0.30f);

        public static JeremyBackendHook Install(TrainingController ctrl)
        {
            if (ctrl == null) return null;
            var existing = ctrl.GetComponentInChildren<JeremyBackendHook>(true);
            if (existing != null) return existing;

            var go = new GameObject("Jeremy");
            go.transform.SetParent(ctrl.transform, false);
            var hook = go.AddComponent<JeremyBackendHook>();
            var rec = go.AddComponent<MovementRecorder>();
            rec.hook = hook;
            rec.workstationRoot = ctrl.transform;

            var guideGo = new GameObject("Jeremy RobotGuide");
            guideGo.transform.SetParent(ctrl.transform, false);
            guideGo.transform.localPosition = GuidePosition;
            guideGo.AddComponent<AudioSource>();
            var guide = guideGo.AddComponent<RobotGuide>();
            guide.hook = hook;

            var replay = go.AddComponent<ReplayView>();
            var panel = go.AddComponent<SessionReportPanel>();
            panel.hook = hook;
            panel.guide = guide;
            panel.replay = replay;
            panel.workstationRoot = ctrl.transform;
            return hook;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            AutoInstall();
            // Scenes loaded later (scene switches, tests) get the same treatment.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) { AutoInstall(); }

        static void AutoInstall()
        {
            foreach (var ctrl in Object.FindObjectsByType<TrainingController>(FindObjectsInactive.Exclude))
                if (ctrl.GetComponentInChildren<JeremyBackendHook>(true) == null)
                {
                    Install(ctrl);
                    Debug.Log("[Jeremy] Installed backend hook, recorder, guide and report on " + ctrl.name + " at runtime. Rebuild the scene to bake it in.");
                }
        }
    }
}
