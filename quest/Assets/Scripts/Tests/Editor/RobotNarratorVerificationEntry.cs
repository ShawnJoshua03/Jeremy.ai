using UnityEditor;
using UnityEngine;

namespace Jeremy.Tests
{
    [InitializeOnLoad]
    public static class RobotNarratorVerificationEntry
    {
        static RobotNarratorVerificationEntry() { EditorApplication.playModeStateChanged += OnPlayMode; }
        public static void Run()
        {
            SessionState.SetBool("Jeremy.VerifyNarrator", true);
            EditorApplication.EnterPlaymode();
        }
        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Jeremy.VerifyNarrator", false)) return;
            SessionState.SetBool("Jeremy.VerifyNarrator", false);
            var runner = new GameObject("Narrator verification").AddComponent<RobotNarratorVerification>();
            runner.Finished = success => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
