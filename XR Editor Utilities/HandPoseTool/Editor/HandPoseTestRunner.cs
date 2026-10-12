using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace StudioXR.HandPose.Editor
{
    /// <summary>
    /// Programmatic callbacks for TestRunnerApi execution.
    /// </summary>
    public class HandPoseTestCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            Debug.Log($"[TestRunnerApi] Execution Started: {testsToRun.Name} ({testsToRun.TestCaseCount} test cases)");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary = $"[TestRunnerApi COMPLETE] Status: {result.TestStatus} | Passed: {result.PassCount} | Failed: {result.FailCount} | Skipped: {result.SkipCount} | Duration: {result.Duration:F3}s";
            if (result.FailCount == 0)
            {
                Debug.Log(summary);
            }
            else
            {
                Debug.LogError(summary);
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.HasChildren) return;

            if (result.TestStatus == TestStatus.Passed)
            {
                Debug.Log($"[TestRunnerApi PASS] {result.Test.FullName} ({result.Duration:F3}s)");
            }
            else if (result.TestStatus == TestStatus.Failed)
            {
                Debug.LogError($"[TestRunnerApi FAIL] {result.Test.FullName}: {result.Message}\n{result.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Invokes Unity Test Runner's official TestRunnerApi for EditMode tests.
    /// </summary>
    [InitializeOnLoad]
    public static class HandPoseTestRunner
    {
        private const string TriggerFilePath = "Temp/RunHandPoseTests.trigger";
        private const string PendingKey = "HandPoseTestRunner_Pending";

        static HandPoseTestRunner()
        {
            EditorApplication.update += CheckAndRun;
        }

        private static void CheckAndRun()
        {
            if (File.Exists(TriggerFilePath))
            {
                try
                {
                    File.Delete(TriggerFilePath);
                }
                catch { }

                SessionState.SetBool(PendingKey, true);

                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.Log("[HandPoseTestRunner] Exiting play mode before running EditMode tests...");
                    EditorApplication.ExitPlaymode();
                    return;
                }

                AssetDatabase.Refresh();
                return;
            }

            if (SessionState.GetBool(PendingKey, false))
            {
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                if (EditorApplication.isCompiling)
                {
                    return;
                }

                SessionState.EraseBool(PendingKey);
                RunEditModeTests();
            }
        }

        [MenuItem("Tools/StudioXR/Run Hand Pose Tests (TestRunnerApi)")]
        public static void RunEditModeTests()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[HandPoseTestRunner] Cannot launch TestRunnerApi while Unity is compiling.");
                return;
            }

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new HandPoseTestCallbacks());

            var filter = new Filter()
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "StudioXR.HandPoseTool.Editor.Tests" }
            };

            Debug.Log("[HandPoseTestRunner] Launching EditMode tests via TestRunnerApi...");
            api.Execute(new ExecutionSettings(filter));
        }
    }
}
