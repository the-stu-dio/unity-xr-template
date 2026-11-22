using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StudioXR.Editor
{
    /// <summary>
    /// One-Click project setup and package installer for the stu/dio XR Template.
    /// Manages UPM packages, interaction layers, XRI Starter Assets sample import,
    /// and ensures full project compatibility without external starter project dependencies.
    /// </summary>
    [InitializeOnLoad]
    public class StudioXRCompatibilityInstaller : EditorWindow
    {
        // -----------------------------------------------------------------------------------------
        // Data Structures
        // -----------------------------------------------------------------------------------------

        public struct PackageRequirement
        {
            public string id;
            public string displayName;
            public string recommendedVersion;
            public string minimumVersion;
            public string description;
            public bool isCritical;

            public PackageRequirement(string id, string displayName, string recommendedVersion, string minimumVersion, string description, bool isCritical = true)
            {
                this.id = id;
                this.displayName = displayName;
                this.recommendedVersion = recommendedVersion;
                this.minimumVersion = minimumVersion;
                this.description = description;
                this.isCritical = isCritical;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Configuration & Requirements List
        // -----------------------------------------------------------------------------------------

        private static readonly PackageRequirement[] RequiredPackages = new[]
        {
            new PackageRequirement("com.unity.xr.interaction.toolkit", "XR Interaction Toolkit", "3.5.1", "3.0.0", "Core interaction framework, interactables, interactors, and locomotion.", true),
            new PackageRequirement("com.unity.xr.hands", "XR Hands", "1.8.1", "1.4.0", "Hand tracking gestures and subsystems for Finger Gun & affordances.", true),
            new PackageRequirement("com.unity.xr.core-utils", "XR Core Utilities", "2.6.0", "2.3.0", "XR Origin, coordinate transform anchors, and XR utility math.", true),
            new PackageRequirement("com.unity.splines", "Splines", "2.9.1", "2.6.0", "Spline path curves and ground snapping for Ghost Footsteps.", true),
            new PackageRequirement("com.unity.xr.openxr", "OpenXR Plugin", "1.17.1", "1.11.0", "Industry-standard OpenXR loader for PCVR, Quest Link, and standalone.", true),
            new PackageRequirement("com.unity.xr.meta-openxr", "Meta OpenXR", "2.5.1", "2.0.0", "Meta Quest features, hand-tracking extensions, and Android OpenXR.", true),
            new PackageRequirement("com.unity.xr.management", "XR Plug-in Management", "4.5.4", "4.4.0", "Platform loader manager for Standalone and Android XR.", false),
            new PackageRequirement("com.unity.render-pipelines.universal", "Universal Render Pipeline", "17.5.0", "17.0.0", "URP shaders, volumes, and graphics architecture.", false),
            new PackageRequirement("com.unity.inputsystem", "Input System", "1.20.0", "1.8.0", "Unity Input System action maps required by XRI behaviors.", true)
        };

        public const string MetaXrSdkPackageId = "com.meta.xr.sdk.core";
        public const string MetaXrSdkRecommendedVersion = "209.0.0";
        public const string MetaRegistryUrl = "https://npm.developer.oculus.com";
        public const string MetaRegistryName = "Meta";
        public const string MetaRegistryScope = "com.meta.xr";

        private const string PrefKeyAutoCheck = "StudioXR_AutoCheckCompatibility";
        private const string PrefKeyDismissPrompt = "StudioXR_DismissPrompt_";

        // -----------------------------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------------------------

        private Dictionary<string, string> m_InstalledManifestPackages = new Dictionary<string, string>();
        private bool m_StarterAssetsImported = false;
        private bool m_TmpImported = false;
        private bool m_TeleportLayerConfigured = false;
        private Vector2 m_ScrollPosition;
        private string m_StatusMessage = "";
        private MessageType m_StatusMessageType = MessageType.Info;

        // -----------------------------------------------------------------------------------------
        // Initialization & Menu Items
        // -----------------------------------------------------------------------------------------

        static StudioXRCompatibilityInstaller()
        {
            EditorApplication.delayCall += CheckOnStartup;
        }

        [MenuItem("Studio XR/Project Setup & Package Installer", false, 0)]
        public static void OpenWindow()
        {
            var window = GetWindow<StudioXRCompatibilityInstaller>("stu/dio XR Setup");
            window.minSize = new Vector2(620, 700);
            window.RefreshStatus();
            window.Show();
        }

        [MenuItem("Studio XR/Project Settings", false, 1)]
        public static void OpenProjectSettingsMenu()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
        }

        [MenuItem("Studio XR/Install Meta XR SDK (Optional)", false, 2)]
        public static void InstallMetaXrSdkMenuItem()
        {
            var window = GetWindow<StudioXRCompatibilityInstaller>("stu/dio XR Setup");
            window.minSize = new Vector2(620, 700);
            window.RefreshStatus();
            window.Show();

            if (window.IsMetaXrSdkInstalled())
            {
                bool reinstall = EditorUtility.DisplayDialog(
                    "Meta XR SDK",
                    $"Meta XR SDK is already installed ({window.GetInstalledMetaXrSdkVersion()}).\n\nWould you like to reinstall or update to v{MetaXrSdkRecommendedVersion}?",
                    "Reinstall / Update",
                    "Cancel"
                );
                if (reinstall)
                {
                    window.InstallMetaXrSdk(true);
                }
            }
            else
            {
                bool install = EditorUtility.DisplayDialog(
                    "Install Meta XR SDK",
                    $"This will configure Meta's official package registry ({MetaRegistryUrl}) and install '{MetaXrSdkPackageId}' (v{MetaXrSdkRecommendedVersion}) into Packages/manifest.json.\n\nProceed with installation?",
                    "Install Meta XR SDK",
                    "Cancel"
                );
                if (install)
                {
                    window.InstallMetaXrSdk(true);
                }
            }
        }

        private static void CheckOnStartup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorPrefs.GetBool(PrefKeyAutoCheck, true)) return;

            string projectHash = Application.dataPath.GetHashCode().ToString();
            if (EditorPrefs.GetBool(PrefKeyDismissPrompt + projectHash, false)) return;

            var installer = CreateInstance<StudioXRCompatibilityInstaller>();
            installer.RefreshStatus();

            bool hasMissingPackages = RequiredPackages.Any(p => p.isCritical && !installer.IsPackageInstalled(p.id));

            if (hasMissingPackages)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "stu/dio XR Template - Project Setup",
                    "The stu/dio XR Template requires XR packages to ensure compatibility with your project.\n\nWould you like to open the Project Setup utility now?",
                    "Open Setup Utility",
                    "Later",
                    "Don't Show Again"
                );

                if (choice == 0)
                {
                    OpenWindow();
                }
                else if (choice == 2)
                {
                    EditorPrefs.SetBool(PrefKeyDismissPrompt + projectHash, true);
                }
            }

            DestroyImmediate(installer);
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void OnFocus()
        {
            RefreshStatus();
        }

        // -----------------------------------------------------------------------------------------
        // Status Detection
        // -----------------------------------------------------------------------------------------

        public void RefreshStatus()
        {
            m_InstalledManifestPackages = ReadManifestDependencies();
            m_StarterAssetsImported = EvaluateStarterAssetsStatus();
            m_TmpImported = EvaluateTmpStatus();
            m_TeleportLayerConfigured = EvaluateTeleportLayerStatus();
            Repaint();
        }

        private bool IsPackageInstalled(string packageId)
        {
            return m_InstalledManifestPackages.ContainsKey(packageId);
        }

        public bool IsMetaXrSdkInstalled()
        {
            return m_InstalledManifestPackages.ContainsKey(MetaXrSdkPackageId) ||
                   m_InstalledManifestPackages.ContainsKey("com.meta.xr.sdk.all");
        }

        public string GetInstalledMetaXrSdkVersion()
        {
            if (m_InstalledManifestPackages.TryGetValue(MetaXrSdkPackageId, out string v1))
                return v1;
            if (m_InstalledManifestPackages.TryGetValue("com.meta.xr.sdk.all", out string v2))
                return v2;
            return null;
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string GetManifestPath()
        {
            return Path.Combine(GetProjectRoot(), "Packages", "manifest.json");
        }

        private static Dictionary<string, string> ReadManifestDependencies()
        {
            var result = new Dictionary<string, string>();
            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath)) return result;

            try
            {
                string text = File.ReadAllText(manifestPath);
                int depStart = text.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
                if (depStart < 0) return result;

                int braceOpen = text.IndexOf('{', depStart);
                if (braceOpen < 0) return result;

                int braceClose = FindMatchingClosingBrace(text, braceOpen);
                if (braceClose < 0) return result;

                string depBlock = text.Substring(braceOpen + 1, braceClose - braceOpen - 1);
                var matches = Regex.Matches(depBlock, @"""([^""]+)""\s*:\s*""([^""]+)""");
                foreach (Match match in matches)
                {
                    result[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio XR] Error reading manifest.json: {ex.Message}");
            }

            return result;
        }

        private static int FindMatchingClosingBrace(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private bool EvaluateStarterAssetsStatus()
        {
            string samplesDir = Path.Combine(Application.dataPath, "Samples", "XR Interaction Toolkit");
            if (!Directory.Exists(samplesDir)) return false;

            var starterDirs = Directory.GetDirectories(samplesDir, "Starter Assets", SearchOption.AllDirectories);
            return starterDirs.Length > 0;
        }

        private bool EvaluateTmpStatus()
        {
            string tmpDir = Path.Combine(Application.dataPath, "TextMesh Pro");
            if (Directory.Exists(tmpDir)) return true;
            return Resources.Load("TMP Settings") != null;
        }

        private bool EvaluateTeleportLayerStatus()
        {
            string assetPath = "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (settings == null)
            {
                settings = Resources.Load<ScriptableObject>("InteractionLayerSettings");
            }

            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var layerNamesProp = so.FindProperty("m_LayerNames");
                if (layerNamesProp != null && layerNamesProp.isArray)
                {
                    for (int i = 0; i < layerNamesProp.arraySize; i++)
                    {
                        if (string.Equals(layerNamesProp.GetArrayElementAtIndex(i).stringValue, "Teleport", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // -----------------------------------------------------------------------------------------
        // One-Click Setup & Actions
        // -----------------------------------------------------------------------------------------

        public void RunOneClickSetup()
        {
            EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Applying project package settings...", 0.1f);
            var log = new StringBuilder();
            log.AppendLine("--- stu/dio XR Template Setup Log ---");

            try
            {
                // 1. Packages
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Configuring UPM Packages in manifest.json...", 0.3f);
                int packagesAdded = InstallMissingPackages(false);
                if (packagesAdded > 0)
                    log.AppendLine($"Added {packagesAdded} missing package(s) to manifest.json.");
                else
                    log.AppendLine("All required packages are already in manifest.json.");

                // 2. Interaction Layer
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Configuring Teleport Interaction Layer...", 0.6f);
                if (ConfigureTeleportInteractionLayer())
                    log.AppendLine("Configured 'Teleport' interaction layer (Layer 31) in InteractionLayerSettings.");
                else
                    log.AppendLine("Teleport interaction layer already configured or initialized.");

                // 3. Starter Assets Sample
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Checking XRI Starter Assets Sample...", 0.8f);
                if (!m_StarterAssetsImported)
                {
                    if (ImportStarterAssetsSample())
                        log.AppendLine("Imported XRI Starter Assets sample.");
                }

                // 4. TMP Resources
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Importing TMP Essential Resources...", 0.9f);
                if (!m_TmpImported)
                {
                    ImportTmpEssentialResources();
                    log.AppendLine("Imported TextMesh Pro Essential Resources.");
                }

                // 5. Resolve UPM & Refresh
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Resolving packages and refreshing AssetDatabase...", 0.95f);
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                SetStatus("One-Click Setup finished successfully! Package manager is resolving dependencies.", MessageType.Info);
                Debug.Log(log.ToString());
                EditorUtility.DisplayDialog("Setup Complete", "The stu/dio XR package setup has finished!\n\n" + log.ToString(), "Awesome");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Studio XR] Setup failed: {ex}");
                SetStatus($"Setup encountered an error: {ex.Message}", MessageType.Error);
                EditorUtility.DisplayDialog("Setup Error", $"An error occurred during setup:\n\n{ex.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshStatus();
            }
        }

        private static bool WriteDependenciesToManifest(IDictionary<string, string> dependencies)
        {
            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath)) return false;

            string text = File.ReadAllText(manifestPath);
            int depStart = text.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
            if (depStart < 0) return false;

            int braceOpen = text.IndexOf('{', depStart);
            int braceClose = FindMatchingClosingBrace(text, braceOpen);
            if (braceOpen < 0 || braceClose < 0) return false;

            var sorted = new SortedDictionary<string, string>(dependencies, StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            int i = 0;
            foreach (var kvp in sorted)
            {
                i++;
                string comma = (i < sorted.Count) ? "," : "";
                sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\"{comma}");
            }
            sb.Append("  }");

            string newText = text.Substring(0, braceOpen) + sb.ToString() + text.Substring(braceClose + 1);
            File.WriteAllText(manifestPath, newText);
            return true;
        }

        private static bool EnsureMetaScopedRegistryInManifest()
        {
            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath)) return false;

            string text = File.ReadAllText(manifestPath);
            if (text.Contains(MetaRegistryUrl)) return true;

            int regStart = text.IndexOf("\"scopedRegistries\"", StringComparison.OrdinalIgnoreCase);
            if (regStart >= 0)
            {
                int bracketOpen = text.IndexOf('[', regStart);
                int bracketClose = text.IndexOf(']', bracketOpen);
                if (bracketOpen >= 0 && bracketClose > bracketOpen)
                {
                    string inner = text.Substring(bracketOpen + 1, bracketClose - bracketOpen - 1).Trim();
                    bool hasExistingEntries = !string.IsNullOrEmpty(inner);
                    string comma = hasExistingEntries ? "," : "";
                    string metaEntry = "\n    {\n      \"name\": \"" + MetaRegistryName + "\",\n      \"url\": \"" + MetaRegistryUrl + "\",\n      \"scopes\": [\n        \"" + MetaRegistryScope + "\"\n      ]\n    }" + comma;
                    string updated = text.Insert(bracketOpen + 1, metaEntry);
                    File.WriteAllText(manifestPath, updated);
                    return true;
                }
            }

            int rootOpen = text.IndexOf('{');
            if (rootOpen >= 0)
            {
                string scopedBlock = "\n  \"scopedRegistries\": [\n    {\n      \"name\": \"" + MetaRegistryName + "\",\n      \"url\": \"" + MetaRegistryUrl + "\",\n      \"scopes\": [\n        \"" + MetaRegistryScope + "\"\n      ]\n    }\n  ],";
                string updated = text.Insert(rootOpen + 1, scopedBlock);
                File.WriteAllText(manifestPath, updated);
                return true;
            }

            return false;
        }

        public int InstallMissingPackages(bool triggerResolve = true)
        {
            var existingDeps = ReadManifestDependencies();
            var toAdd = new List<PackageRequirement>();

            foreach (var req in RequiredPackages)
            {
                if (!existingDeps.ContainsKey(req.id))
                {
                    toAdd.Add(req);
                }
            }

            if (toAdd.Count == 0) return 0;

            foreach (var req in toAdd)
            {
                existingDeps[req.id] = req.recommendedVersion;
            }

            if (!WriteDependenciesToManifest(existingDeps)) return 0;

            if (triggerResolve)
            {
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.Refresh();
                SetStatus($"Added {toAdd.Count} package(s) to manifest.json. Package manager is resolving.", MessageType.Info);
            }

            return toAdd.Count;
        }

        public bool InstallMetaXrSdk(bool triggerResolve = true)
        {
            try
            {
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Configuring Meta Scoped Registry...", 0.3f);
                EnsureMetaScopedRegistryInManifest();

                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Adding Meta XR Core SDK to manifest.json...", 0.6f);
                var existingDeps = ReadManifestDependencies();
                existingDeps[MetaXrSdkPackageId] = MetaXrSdkRecommendedVersion;
                WriteDependenciesToManifest(existingDeps);

                if (triggerResolve)
                {
                    EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Resolving packages with Unity Package Manager...", 0.9f);
                    UnityEditor.PackageManager.Client.Resolve();
                    AssetDatabase.Refresh();
                    SetStatus($"Installed Meta XR Core SDK (v{MetaXrSdkRecommendedVersion}). Unity is resolving packages.", MessageType.Info);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Studio XR] Failed to install Meta XR SDK: {ex}");
                SetStatus($"Failed to install Meta XR SDK: {ex.Message}", MessageType.Error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshStatus();
            }
        }

        public bool UninstallMetaXrSdk(bool triggerResolve = true)
        {
            try
            {
                EditorUtility.DisplayProgressBar("stu/dio XR Setup", "Removing Meta XR SDK from manifest.json...", 0.5f);
                var existingDeps = ReadManifestDependencies();
                bool removed = false;
                if (existingDeps.Remove(MetaXrSdkPackageId))
                    removed = true;
                if (existingDeps.Remove("com.meta.xr.sdk.all"))
                    removed = true;

                if (removed)
                {
                    WriteDependenciesToManifest(existingDeps);
                    if (triggerResolve)
                    {
                        UnityEditor.PackageManager.Client.Resolve();
                        AssetDatabase.Refresh();
                        SetStatus("Removed Meta XR SDK from manifest.json. Package manager is resolving.", MessageType.Info);
                    }
                }
                return removed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Studio XR] Failed to remove Meta XR SDK: {ex}");
                SetStatus($"Failed to remove Meta XR SDK: {ex.Message}", MessageType.Error);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshStatus();
            }
        }

        public bool ConfigureTeleportInteractionLayer()
        {
            try
            {
                string assetFolder = "Assets/XRI/Settings/Resources";
                string assetPath = $"{assetFolder}/InteractionLayerSettings.asset";

                if (!Directory.Exists(Path.Combine(Application.dataPath, "XRI", "Settings", "Resources")))
                {
                    Directory.CreateDirectory(Path.Combine(Application.dataPath, "XRI", "Settings", "Resources"));
                    AssetDatabase.Refresh();
                }

                var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (settings == null)
                {
                    settings = Resources.Load<ScriptableObject>("InteractionLayerSettings");
                }

                if (settings == null)
                {
                    string templateYaml =
                        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n" +
                        "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n" +
                        "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n" +
                        "  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: 191492db6e452eb468b95433ec162164, type: 3}\n" +
                        "  m_Name: InteractionLayerSettings\n  m_EditorClassIdentifier: \n  m_LayerNames:\n" +
                        "  - Default\n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n" +
                        "  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - \n  - Teleport\n";

                    File.WriteAllText(Path.Combine(Application.dataPath, "XRI", "Settings", "Resources", "InteractionLayerSettings.asset"), templateYaml);
                    AssetDatabase.Refresh();
                    return true;
                }

                var so = new SerializedObject(settings);
                var layerNamesProp = so.FindProperty("m_LayerNames");
                if (layerNamesProp != null && layerNamesProp.isArray)
                {
                    bool alreadyHasTeleport = false;
                    for (int j = 0; j < layerNamesProp.arraySize; j++)
                    {
                        if (string.Equals(layerNamesProp.GetArrayElementAtIndex(j).stringValue, "Teleport", StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyHasTeleport = true;
                            break;
                        }
                    }

                    if (!alreadyHasTeleport)
                    {
                        while (layerNamesProp.arraySize < 32)
                        {
                            layerNamesProp.InsertArrayElementAtIndex(layerNamesProp.arraySize);
                        }
                        layerNamesProp.GetArrayElementAtIndex(31).stringValue = "Teleport";
                        so.ApplyModifiedProperties();
                        AssetDatabase.SaveAssets();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio XR] Could not configure InteractionLayerSettings automatically: {ex.Message}");
            }
            return false;
        }

        public bool ImportStarterAssetsSample()
        {
            try
            {
                string xriPackagePath = Path.Combine(GetProjectRoot(), "Packages", "com.unity.xr.interaction.toolkit");
                if (!Directory.Exists(xriPackagePath))
                {
                    SetStatus("Cannot import Starter Assets: XR Interaction Toolkit package is not yet installed.", MessageType.Warning);
                    return false;
                }

                string sourceSampleDir = Path.Combine(xriPackagePath, "Samples~", "Starter Assets");
                if (!Directory.Exists(sourceSampleDir))
                {
                    SetStatus("Samples~/Starter Assets not found inside com.unity.xr.interaction.toolkit.", MessageType.Warning);
                    return false;
                }

                string packageJsonPath = Path.Combine(xriPackagePath, "package.json");
                string version = "3.5.1";
                if (File.Exists(packageJsonPath))
                {
                    string pjson = File.ReadAllText(packageJsonPath);
                    var match = Regex.Match(pjson, @"""version""\s*:\s*""([^""]+)""");
                    if (match.Success) version = match.Groups[1].Value;
                }

                string targetSampleDir = Path.Combine(Application.dataPath, "Samples", "XR Interaction Toolkit", version, "Starter Assets");
                CopyDirectoryRecursive(sourceSampleDir, targetSampleDir);
                AssetDatabase.Refresh();
                SetStatus($"Imported XRI Starter Assets sample for v{version}.", MessageType.Info);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to import Starter Assets sample: {ex.Message}", MessageType.Error);
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        public void ImportTmpEssentialResources()
        {
            EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
        }

        private void SetStatus(string message, MessageType type)
        {
            m_StatusMessage = message;
            m_StatusMessageType = type;
        }

        // -----------------------------------------------------------------------------------------
        // GUI Drawing
        // -----------------------------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            DrawHeroSection();
            EditorGUILayout.Space(8);

            DrawPackagesCard();
            EditorGUILayout.Space(8);

            DrawOptionalPackagesCard();
            EditorGUILayout.Space(8);

            DrawSamplesAndResourcesCard();
            EditorGUILayout.Space(8);

            DrawProjectSettingsCard();
            EditorGUILayout.Space(8);

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.toolbar);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("stu/dio XR Toolkit & Template  |  Project Compatibility Installer", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshStatus();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawHeroSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label("1-Click Package Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Ensures this project has all required UPM packages, interaction layers, and " +
                "XRI Starter Assets dependencies installed for full compatibility.",
                EditorStyles.wordWrappedLabel
            );

            EditorGUILayout.Space(6);

            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.35f, 1f);
            if (GUILayout.Button("Run One-Click Package Setup (Recommended)", GUILayout.Height(38)))
            {
                RunOneClickSetup();
            }
            GUI.backgroundColor = Color.white;

            if (!string.IsNullOrEmpty(m_StatusMessage))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(m_StatusMessage, m_StatusMessageType);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPackagesCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int missingCount = RequiredPackages.Count(p => !IsPackageInstalled(p.id));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Required UPM Packages", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (missingCount == 0)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.2f, 0.7f, 0.2f);
                GUILayout.Label("All Packages Installed", EditorStyles.miniBoldLabel);
                GUI.color = prev;
            }
            else
            {
                var prev = GUI.color;
                GUI.color = new Color(0.9f, 0.5f, 0.1f);
                GUILayout.Label($"{missingCount} Missing", EditorStyles.miniBoldLabel);
                GUI.color = prev;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            foreach (var req in RequiredPackages)
            {
                bool installed = IsPackageInstalled(req.id);
                string installedVer = installed ? m_InstalledManifestPackages[req.id] : "Not Installed";

                EditorGUILayout.BeginHorizontal();

                var prevColor = GUI.color;
                GUI.color = installed ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
                GUILayout.Label(installed ? "\u2714" : "\u2716", EditorStyles.boldLabel, GUILayout.Width(20));
                GUI.color = prevColor;

                GUILayout.Label(req.displayName, GUILayout.Width(200));
                GUILayout.Label(installed ? installedVer : $"(Needs {req.recommendedVersion})", EditorStyles.miniLabel, GUILayout.Width(130));
                GUILayout.Label(req.id, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);

            if (missingCount > 0)
            {
                if (GUILayout.Button($"Install {missingCount} Missing Packages (manifest.json)"))
                {
                    InstallMissingPackages(true);
                    RefreshStatus();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawOptionalPackagesCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool isMetaInstalled = IsMetaXrSdkInstalled();
            string metaVersion = GetInstalledMetaXrSdkVersion();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Optional Platform SDKs", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (isMetaInstalled)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.2f, 0.7f, 0.2f);
                GUILayout.Label("Meta XR SDK Installed", EditorStyles.miniBoldLabel);
                GUI.color = prev;
            }
            else
            {
                var prev = GUI.color;
                GUI.color = new Color(0.4f, 0.7f, 1.0f);
                GUILayout.Label("Optional Extension", EditorStyles.miniLabel);
                GUI.color = prev;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(
                "The template uses Unity OpenXR by default for cross-platform compatibility. " +
                "You can optionally install the Meta XR Core SDK to access proprietary Quest APIs, OVRManager, " +
                "Building Blocks, Passthrough layers, and native Quest optimizations.",
                EditorStyles.wordWrappedLabel
            );

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();

            var prevColor = GUI.color;
            GUI.color = isMetaInstalled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(isMetaInstalled ? "\u2714" : "\u25CB", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = prevColor;

            GUILayout.Label("Meta XR Core SDK", GUILayout.Width(200));
            GUILayout.Label(isMetaInstalled ? $"v{metaVersion}" : $"(Optional v{MetaXrSdkRecommendedVersion})", EditorStyles.miniLabel, GUILayout.Width(130));
            GUILayout.Label(MetaXrSdkPackageId, EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            if (!isMetaInstalled)
            {
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
                if (GUILayout.Button("Install Meta XR SDK", GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Install Meta XR SDK",
                        $"This will configure the official Meta scoped package registry ({MetaRegistryUrl}) " +
                        $"and install '{MetaXrSdkPackageId}' (v{MetaXrSdkRecommendedVersion}) into manifest.json.\n\n" +
                        "Unity will then resolve and download the package in the background.\n\nProceed?",
                        "Install",
                        "Cancel"))
                    {
                        InstallMetaXrSdk(true);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
                if (GUILayout.Button("Reinstall / Update Meta XR SDK", GUILayout.Height(26)))
                {
                    InstallMetaXrSdk(true);
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Remove Meta XR SDK", GUILayout.Height(26), GUILayout.Width(180)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Remove Meta XR SDK",
                        $"Are you sure you want to remove '{MetaXrSdkPackageId}' from manifest.json?",
                        "Remove",
                        "Cancel"))
                    {
                        UninstallMetaXrSdk(true);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawSamplesAndResourcesCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Samples & Resources", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Starter Assets
            EditorGUILayout.BeginHorizontal();
            var prevCol = GUI.color;
            GUI.color = m_StarterAssetsImported ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
            GUILayout.Label(m_StarterAssetsImported ? "\u2714" : "\u2716", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = prevCol;
            GUILayout.Label("XRI Starter Assets Sample", GUILayout.Width(220));
            GUILayout.Label(m_StarterAssetsImported ? "Imported" : "Missing", EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (!m_StarterAssetsImported)
            {
                if (GUILayout.Button("Import Sample", GUILayout.Width(120)))
                {
                    ImportStarterAssetsSample();
                    RefreshStatus();
                }
            }
            EditorGUILayout.EndHorizontal();

            // TMP
            EditorGUILayout.BeginHorizontal();
            prevCol = GUI.color;
            GUI.color = m_TmpImported ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
            GUILayout.Label(m_TmpImported ? "\u2714" : "\u2716", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = prevCol;
            GUILayout.Label("TextMesh Pro Essential Resources", GUILayout.Width(220));
            GUILayout.Label(m_TmpImported ? "Imported" : "Missing", EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (!m_TmpImported)
            {
                if (GUILayout.Button("Import TMP", GUILayout.Width(120)))
                {
                    ImportTmpEssentialResources();
                    RefreshStatus();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawProjectSettingsCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Project Configuration & Layers", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Teleport interaction layer
            EditorGUILayout.BeginHorizontal();
            var prevCol = GUI.color;
            GUI.color = m_TeleportLayerConfigured ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
            GUILayout.Label(m_TeleportLayerConfigured ? "\u2714" : "\u2716", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = prevCol;
            GUILayout.Label("Interaction Layer 'Teleport' (Layer 31)", GUILayout.Width(240));
            GUILayout.Label(m_TeleportLayerConfigured ? "Configured" : "Missing", EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (!m_TeleportLayerConfigured)
            {
                if (GUILayout.Button("Configure Layer", GUILayout.Width(120)))
                {
                    ConfigureTeleportInteractionLayer();
                    RefreshStatus();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // XR Plug-in Management button
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("XR Plug-in Management (OpenXR)", GUILayout.Width(260));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Project Settings", GUILayout.Width(160)))
            {
                SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
            }
            EditorGUILayout.EndHorizontal();

            // OpenXR Validation shortcut
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("OpenXR Project Validation", GUILayout.Width(260));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Validation", GUILayout.Width(160)))
            {
                StudioXRMenuBridge.OpenOpenXRProjectValidation();
            }
            EditorGUILayout.EndHorizontal();

            // XR Interaction Debugger shortcut
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("XR Interaction Debugger", GUILayout.Width(260));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Debugger", GUILayout.Width(160)))
            {
                StudioXRMenuBridge.OpenXRInteractionDebugger();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool autoCheck = EditorPrefs.GetBool(PrefKeyAutoCheck, true);
            bool newAutoCheck = EditorGUILayout.ToggleLeft("Check project package compatibility automatically on startup", autoCheck);
            if (newAutoCheck != autoCheck)
            {
                EditorPrefs.SetBool(PrefKeyAutoCheck, newAutoCheck);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Bridges XR windows and tools normally nested under Window > XR, Window > Analysis,
    /// and Project Settings into the top-level "Studio XR" menu tab on Unity's main menu bar,
    /// and removes the redundant "XR" submenu from the Window tab.
    /// </summary>
    [InitializeOnLoad]
    public static class StudioXRMenuBridge
    {
        // -----------------------------------------------------------------------------------------
        // Core XR Windows & Tools (Window > XR / Window > Analysis)
        // -----------------------------------------------------------------------------------------

        [MenuItem("Studio XR/XR Interaction Debugger", false, 20)]
        public static void OpenXRInteractionDebugger()
        {
            if (!EditorApplication.ExecuteMenuItem("Window/Analysis/XR Interaction Debugger"))
            {
                if (!EditorApplication.ExecuteMenuItem("Window/XR/XR Interaction Debugger"))
                    Debug.LogWarning("[Studio XR] Could not locate XR Interaction Debugger window. Ensure XR Interaction Toolkit is installed.");
            }
        }

        [MenuItem("Studio XR/XR Hand Capture", false, 21)]
        public static void OpenXRHandCapture()
        {
            var type = Type.GetType("UnityEditor.XR.Hands.Capture.XRHandCaptureEditorWindow, Unity.XR.Hands.Editor");
            if (type != null)
            {
                EditorWindow.GetWindow(type, false, "XR Hand Capture");
            }
            else if (!EditorApplication.ExecuteMenuItem("Window/XR/XR Hand Capture"))
            {
                Debug.LogWarning("[Studio XR] Could not locate XR Hand Capture window. Ensure XR Hands package is installed.");
            }
        }

        [MenuItem("Studio XR/AR Foundation/XR Environment", false, 22)]
        public static void OpenAREnvironmentView()
        {
            var type = Type.GetType("UnityEditor.XR.Simulation.XREnvironmentViewManager, Unity.XR.ARFoundation.Editor");
            var method = type?.GetMethod("GetXREnvironmentView", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null)
            {
                method.Invoke(null, null);
            }
            else if (!EditorApplication.ExecuteMenuItem("Window/XR/AR Foundation/XR Environment"))
            {
                Debug.LogWarning("[Studio XR] Could not locate AR Foundation XR Environment. Ensure AR Foundation package is installed.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // OpenXR (Window > XR > OpenXR)
        // -----------------------------------------------------------------------------------------

        [MenuItem("Studio XR/OpenXR/Project Validation", false, 23)]
        public static void OpenOpenXRProjectValidation()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management/Project Validation");
        }

        [MenuItem("Studio XR/OpenXR/Runtime Debugger", false, 24)]
        public static void OpenOpenXRRuntimeDebugger()
        {
            var type = Type.GetType("UnityEditor.XR.OpenXR.Features.RuntimeDebugger.RuntimeDebuggerWindow, Unity.XR.OpenXR.Editor");
            if (type != null)
            {
                var window = EditorWindow.GetWindow(type, false, "OpenXR Runtime Debugger");
                window?.Show();
            }
            else if (!EditorApplication.ExecuteMenuItem("Window/Analysis/OpenXR Runtime Debugger"))
            {
                if (!EditorApplication.ExecuteMenuItem("Window/XR/OpenXR/Runtime Debugger"))
                    Debug.LogWarning("[Studio XR] Could not locate OpenXR Runtime Debugger. Ensure OpenXR Plugin is installed.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Composition Layers (Window > XR > Composition Layers)
        // -----------------------------------------------------------------------------------------

        [MenuItem("Studio XR/Composition Layers/Composition Layers Window", false, 25)]
        public static void OpenCompositionLayersWindow()
        {
            var type = Type.GetType("UnityEditor.XR.CompositionLayers.CompositionLayersWindow, Unity.XR.CompositionLayers.Editor");
            if (type != null)
            {
                var window = EditorWindow.GetWindow(type, false, "Composition Layers");
                window?.Show();
            }
            else if (!EditorApplication.ExecuteMenuItem("Window/XR/Composition Layers"))
            {
                Debug.LogWarning("[Studio XR] Could not locate Composition Layers window. Ensure Composition Layers package is installed.");
            }
        }

        [MenuItem("Studio XR/Composition Layers/Project Validation", false, 26)]
        public static void OpenCompositionLayersValidation()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management/Project Validation");
        }

        // -----------------------------------------------------------------------------------------
        // Remove Redundant Window > XR Submenu
        // -----------------------------------------------------------------------------------------

        static StudioXRMenuBridge()
        {
            EditorApplication.delayCall += RemoveLegacyWindowXRMenuItems;
        }

        [InitializeOnLoadMethod]
        private static void ScheduleMenuCleanup()
        {
            EditorApplication.delayCall += RemoveLegacyWindowXRMenuItems;
        }

        private static void RemoveLegacyWindowXRMenuItems()
        {
            try
            {
                Type menuType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Menu");
                if (menuType == null) return;

                MethodInfo removeMethod = menuType.GetMethod("RemoveMenuItem", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (removeMethod == null) return;

                string[] pathsToRemove = new[]
                {
                    "Window/XR/XR Hand Capture",
                    "Window/XR/AR Foundation/XR Environment",
                    "Window/XR/AR Foundation",
                    "Window/XR/OpenXR/Project Validation",
                    "Window/XR/OpenXR/Runtime Debugger",
                    "Window/XR/OpenXR",
                    "Window/XR/Composition Layers/Project Validation",
                    "Window/XR/Composition Layers",
                    "Window/XR"
                };

                foreach (var path in pathsToRemove)
                {
                    try
                    {
                        removeMethod.Invoke(null, new object[] { path });
                    }
                    catch
                    {
                        // Ignore individual path failures if package is not present
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio XR Bridge] Notice: Could not remove legacy Window/XR menu items: {ex.Message}");
            }
        }
    }
}
