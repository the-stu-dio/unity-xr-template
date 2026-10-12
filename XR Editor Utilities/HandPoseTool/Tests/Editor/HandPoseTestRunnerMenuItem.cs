using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StudioXR.HandPose.Tests
{
    /// <summary>
    /// Utility to run the Hand Pose test suite on demand from the Unity Editor menu.
    /// Results are logged to the Unity Console and project Editor.log.
    /// </summary>
    public static class HandPoseTestRunnerMenuItem
    {
        [MenuItem("Tools/StudioXR/Run Hand Pose Tests")]
        public static void RunTests()
        {
            var fixture = new HandPoseTests();
            int passed = 0;
            int failed = 0;

            var methods = typeof(HandPoseTests).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<TestAttribute>() == null)
                    continue;

                fixture.SetUp();
                try
                {
                    method.Invoke(fixture, null);
                    passed++;
                    Debug.Log($"[HandPoseTests PASS] {method.Name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                    Debug.LogError($"[HandPoseTests FAIL] {method.Name}: {inner.Message}\n{inner.StackTrace}");
                }
                finally
                {
                    fixture.TearDown();
                }
            }

            if (failed == 0)
            {
                Debug.Log($"[HandPoseTests Complete] ALL {passed} TESTS PASSED SUCCESSFULLY.");
            }
            else
            {
                Debug.LogError($"[HandPoseTests Complete] {failed} TESTS FAILED out of {passed + failed}.");
            }
        }
    }
}
