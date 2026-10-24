using System.IO;
using UnityEditor.Android;

namespace Jeremy.EditorTools
{
    // Unity's OpenXR package and Meta's OVRPlugin both ship libopenxr_loader.so; Gradle fails on the duplicate.
    // Keep the first copy. Verify on device that OpenXR starts (see docs/QUEST_BUILD.md).
    public sealed class GradlePackagingFix : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // The duplicate fails in the launcher module, a sibling of the unityLibrary folder passed in.
            var launcher = Path.Combine(Directory.GetParent(path).FullName, "launcher");
            foreach (var name in new[] { "build.gradle", "build.gradle.kts" })
            {
                var file = Path.Combine(launcher, name);
                if (!File.Exists(file)) continue;
                var text = File.ReadAllText(file);
                if (text.Contains("libopenxr_loader.so")) return;
                var block = name.EndsWith(".kts")
                    ? "\nandroid {\n    packaging {\n        jniLibs {\n            pickFirsts += \"**/libopenxr_loader.so\"\n        }\n    }\n}\n"
                    : "\nandroid {\n    packaging {\n        jniLibs {\n            pickFirsts.add('**/libopenxr_loader.so')\n        }\n    }\n}\n";
                File.AppendAllText(file, block);
                return;
            }
        }
    }
}
