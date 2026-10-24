# gemini tasks (edit only quest/Assets/Scripts/ and quest/Assets/Scripts/Tests*; report anything else)
Commit after each task.
1. Compile check. Make sure no Unity Editor has the project open (tasklist | grep Unity.exe), then run:
   "C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe" -batchMode -quit -nographics -projectPath <abs path to quest> -logFile <abs path outside repo>/unity.log
   Read the log for `error CS` lines. Fix compile errors in our scripts only. Do not touch Packages/ or ProjectSettings/.
2. Read quest/HANDOFF_UNITY.md. Check each script against the API in backend/main.py and docs/API.md: field names, enums, status codes, idempotent IDs. Write mismatches and fixes to docs/reviews/unity-api-contract.md, fix them in C# if small.
3. Run the Unity EditMode tests if the tests asmdef exists (-runTests -testPlatform EditMode -testResults <path>). Report results honestly.
