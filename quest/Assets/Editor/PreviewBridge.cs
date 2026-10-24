using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Lets a terminal drive the editor for testing the desktop preview without clicking.
// Write commands, one per line, to %TEMP%/jeremy-bridge/editor.txt:
//   refresh            reimport and recompile changed scripts
//   play               open Assets/Scenes/PegTransfer.unity and enter Play mode
//   stop               leave Play mode
// Runtime commands (autopilot, exercise 1|2, start, reset, coach, screenshot <path>, status) go to
// runtime.txt and are read by PreviewAutopilot while playing. The editor writes editor-status.txt.
[InitializeOnLoad]
static class PreviewBridge
{
    const string Scene = "Assets/Scenes/PegTransfer.unity";
    static double nextPoll;
    static string Folder => Path.Combine(Path.GetTempPath(), "jeremy-bridge");

    static PreviewBridge()
    {
        EditorApplication.update += Poll;
        WriteStatus();
    }

    static void Poll()
    {
        if (SessionState.GetBool("JeremyPendingBuild", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            SessionState.SetBool("JeremyPendingBuild", false);
            File.WriteAllText(Path.Combine(Folder, "build-status.txt"), "started " + System.DateTime.Now.ToString("HH:mm:ss"));
            try { Jeremy.EditorTools.QuestBuild.BuildApk(); File.WriteAllText(Path.Combine(Folder, "build-status.txt"), "done " + System.DateTime.Now.ToString("HH:mm:ss") + " exists=" + File.Exists("Builds/Jeremy.apk")); }
            catch (System.Exception e) { File.WriteAllText(Path.Combine(Folder, "build-status.txt"), "failed: " + e.Message); }
            return;
        }
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.5;
        string file = Path.Combine(Folder, "editor.txt");
        if (!File.Exists(file)) return;
        string[] lines;
        try { lines = File.ReadAllLines(file); File.Delete(file); } catch (IOException) { return; }
        foreach (string raw in lines)
        {
            switch (raw.Trim())
            {
                case "refresh": AssetDatabase.Refresh(); break;
                case "play":
                    if (EditorApplication.isPlaying) break;
                    if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
                    EditorApplication.isPlaying = true;
                    break;
                case "stop": EditorApplication.isPlaying = false; break;
                case "build":
                    // Builds Builds/Jeremy.apk (regenerates the scene) as soon as the editor is out of Play mode.
                    SessionState.SetBool("JeremyPendingBuild", true);
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        WriteStatus();
    }

    static void WriteStatus()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "editor-status.txt"),
                "playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling
                + " scene=" + EditorSceneManager.GetActiveScene().path + " time=" + System.DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (IOException) { }
    }
}
