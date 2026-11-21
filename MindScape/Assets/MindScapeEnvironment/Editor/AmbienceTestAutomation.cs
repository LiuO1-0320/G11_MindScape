using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MindScape.Environment.Editor
{
    // Only accepts one fixed action, for tests requested by the project owner.
    [InitializeOnLoad] public static class AmbienceTestAutomation
    {
        static string ReportDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/MindScapeEnvironment"));
        static string RequestPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/MindScapeEnvironment.request"));
        static TestRunnerApi api;
        static double nextPoll;
        [Serializable] sealed class Result
        {
            public string status, projectPath, unityVersion, time, message;
            public int passed, failed, skipped;
        }
        static AmbienceTestAutomation()
        {
            EditorApplication.delayCall += Register;
            EditorApplication.update += Poll;
        }
        static void Register()
        {
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "compiled.json"), JsonUtility.ToJson(new Result {
                status = "editor-domain-loaded", projectPath = Application.dataPath,
                unityVersion = Application.unityVersion, message = "task2-v2-final", time = DateTime.UtcNow.ToString("o") }, true));
            api = ScriptableObject.CreateInstance<TestRunnerApi>(); api.RegisterCallbacks(new Callbacks());
        }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (api == null || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            string request = File.ReadAllText(RequestPath).Trim();
            if (request == "open-environment-room")
            {
                File.Delete(RequestPath);
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return;
                EditorSceneManager.OpenScene("Assets/Scenes/BasicScene.unity");
                return;
            }
            if (request != "run-environment-tests" && request != "run-environment-tests-v2-final") return;
            File.Delete(RequestPath); RunTests();
        }
        [MenuItem("MindScape/Run Environment Play Mode Tests")]
        public static void RunTests()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                { SaveResult("needs-scene-save", "Save your current scenes before running tests.", 0, 0, 0); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (api == null) Register();
            SessionState.SetBool("MindScape.Environment.TestsRunning", true);
            SaveResult("running", "Play Mode tests started. Fixtures are not live LLM outputs.", 0, 0, 0);
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode,
                assemblyNames = new[] { "MindScape.Environment.PlayModeTests" } }));
        }
        static void SaveResult(string status, string message, int passed, int failed, int skipped)
        {
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "playmode.json"), JsonUtility.ToJson(new Result {
                status = status, message = message, passed = passed, failed = failed, skipped = skipped,
                projectPath = Application.dataPath, unityVersion = Application.unityVersion,
                time = DateTime.UtcNow.ToString("o") }, true));
        }
        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (SessionState.GetBool("MindScape.Environment.TestsRunning", false))
                    File.AppendAllText(Path.Combine(ReportDirectory, "test-results.txt"), result.FullName + " : " + result.ResultState + "\n" + result.Message + "\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool("MindScape.Environment.TestsRunning", false)) return;
                SaveResult(result.FailCount == 0 && result.PassCount >= 6 ? "passed" : "failed",
                    result.Message, result.PassCount, result.FailCount, result.SkipCount);
                SessionState.SetBool("MindScape.Environment.TestsRunning", false);
            }
        }
        [MenuItem("MindScape/Open Environment Room")]
        public static void OpenRoom()
        {
            if (EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/BasicScene.unity");
        }
    }
}
