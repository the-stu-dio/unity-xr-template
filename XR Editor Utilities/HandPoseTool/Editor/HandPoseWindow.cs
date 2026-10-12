using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StudioXR.HandPose.Editor
{
    /// <summary>
    /// Editor window tool for automatically posing skeletal hand models to grasp interactable objects
    /// using physics raycasts and spherecasts.
    /// </summary>
    public class HandPoseWindow : EditorWindow
    {
        [SerializeField] private Transform m_WristRoot;
        [SerializeField] private GameObject m_TargetObject;
        [SerializeField] private HandPoseConfig m_Config = new HandPoseConfig();

        [SerializeField] private List<FingerChain> m_Chains = new List<FingerChain>();
        private Vector2 m_ScrollPosition;
        private bool m_ShowSettings = true;
        private bool m_ShowFingers = true;
        private string m_StatusMessage = "Assign Hand Root Bone and Target Interactable, then click 'Pose Hand'.";
        private MessageType m_StatusType = MessageType.Info;

        [MenuItem("Tools/StudioXR/Hand Pose Tool")]
        [MenuItem("Window/XR/Hand Pose Tool")]
        public static void Open()
        {
            var window = GetWindow<HandPoseWindow>("Hand Pose Tool");
            window.minSize = new Vector2(380, 520);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;

            if (m_Config == null)
            {
                m_Config = new HandPoseConfig();
            }

            // Attempt to infer from active selection
            if (m_WristRoot == null && Selection.activeTransform != null)
            {
                var sel = Selection.activeTransform;
                if (sel.name.IndexOf("Wrist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sel.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    m_WristRoot = sel;
                    DetectChains();
                }
            }
            else if (m_WristRoot != null)
            {
                if (m_Chains == null || m_Chains.Count == 0)
                {
                    DetectChains();
                }
                else
                {
                    m_Config.customChains = m_Chains;
                }
            }
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            DrawHeader();
            EditorGUILayout.Space(6);

            DrawAssignmentsSection();
            EditorGUILayout.Space(6);

            DrawSettingsSection();
            EditorGUILayout.Space(6);

            DrawFingersSection();
            EditorGUILayout.Space(10);

            DrawActionButtons();
            EditorGUILayout.Space(8);

            DrawStatusBox();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Studio XR Hand Poser", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Non-destructively poses skeletal hand fingers to grasp interactable colliders via physics spherecasts. " +
                "Modifies only bone localRotation, preserving affordance and renderer components.",
                MessageType.None);
        }

        private void DrawAssignmentsSection()
        {
            EditorGUILayout.LabelField("Hierarchy Assignments", EditorStyles.boldLabel);

            var newWrist = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Hand Root Bone", "Root wrist bone of the hand skeleton (e.g. Hand_L_Wrist or L_Wrist)."),
                m_WristRoot,
                typeof(Transform),
                true);

            if (newWrist != m_WristRoot)
            {
                m_WristRoot = newWrist;
                if (m_WristRoot != null)
                {
                    DetectChains();
                }
                else
                {
                    m_Chains.Clear();
                }
            }

            m_TargetObject = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target Interactable", "Target GameObject containing one or more Colliders to grasp."),
                m_TargetObject,
                typeof(GameObject),
                true);

            m_Config.palmReference = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Palm Reference", "Optional explicit Palm Transform. If empty, automatically inferred from knuckle bases."),
                m_Config.palmReference,
                typeof(Transform),
                true);

            if (m_WristRoot != null && PrefabUtility.IsPartOfPrefabAsset(m_WristRoot))
            {
                EditorGUILayout.HelpBox(
                    "The assigned Hand Root Bone is a Prefab Asset in Project files. " +
                    "To pose bones against colliders, instantiate the prefab in the Scene or open it in Prefab Stage.",
                    MessageType.Warning);
            }

            if (m_TargetObject != null && PrefabUtility.IsPartOfPrefabAsset(m_TargetObject))
            {
                EditorGUILayout.HelpBox(
                    "The assigned Target Interactable is a Prefab Asset in Project files. " +
                    "To pose bones against colliders, instantiate the target in the Scene or open it in Prefab Stage.",
                    MessageType.Warning);
            }
        }

        private void DrawSettingsSection()
        {
            m_ShowSettings = EditorGUILayout.Foldout(m_ShowSettings, "Posing & Raycast Settings", true);
            if (!m_ShowSettings) return;

            EditorGUI.indentLevel++;

            m_Config.fingerRadius = EditorGUILayout.Slider(
                new GUIContent("Finger Radius (m)", "Radius of the spherecast representing finger cushion thickness."),
                m_Config.fingerRadius, 0.002f, 0.025f);

            m_Config.stepAngle = EditorGUILayout.Slider(
                new GUIContent("Step Angle (deg)", "Rotation increment per iterative collision test step."),
                m_Config.stepAngle, 0.5f, 5.0f);

            m_Config.maxCurlAngle = EditorGUILayout.Slider(
                new GUIContent("Max Curl Angle (deg)", "Maximum allowable curl angle per joint."),
                m_Config.maxCurlAngle, 30f, 120f);

            m_Config.contactOffset = EditorGUILayout.Slider(
                new GUIContent("Contact Offset (m)", "Skin offset margin to prevent collider intersection."),
                m_Config.contactOffset, 0f, 0.005f);

            m_Config.collisionLayerMask = DrawLayerMaskField(
                new GUIContent("Collision Layers", "LayerMask for raycast/spherecast hits."),
                m_Config.collisionLayerMask);

            m_Config.ignoreTriggers = EditorGUILayout.Toggle(
                new GUIContent("Ignore Triggers", "Ignore trigger colliders so only physical geometry is grasped."),
                m_Config.ignoreTriggers);

            m_Config.curlMetacarpals = EditorGUILayout.Toggle(
                new GUIContent("Curl Metacarpals", "Whether finger metacarpals curl (usually false in realistic hands)."),
                m_Config.curlMetacarpals);

            m_Config.curlThumbMetacarpal = EditorGUILayout.Toggle(
                new GUIContent("Curl Thumb Base", "Whether thumb base / metacarpal joint curls."),
                m_Config.curlThumbMetacarpal);

            EditorGUI.indentLevel--;
        }

        private void DrawFingersSection()
        {
            m_ShowFingers = EditorGUILayout.Foldout(m_ShowFingers, $"Detected Fingers ({m_Chains.Count})", true);
            if (!m_ShowFingers) return;

            EditorGUI.indentLevel++;

            if (GUILayout.Button("Auto-Detect Finger Chains", GUILayout.Height(22)))
            {
                DetectChains();
            }

            if (m_Chains.Count == 0)
            {
                EditorGUILayout.HelpBox("No finger chains detected yet. Assign a Hand Root Bone above.", MessageType.Warning);
            }
            else
            {
                for (int i = 0; i < m_Chains.Count; i++)
                {
                    var chain = m_Chains[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"{chain.fingerName} ({chain.bones.Count} bones)" + (chain.isThumb ? " [Thumb]" : ""), EditorStyles.miniBoldLabel);

                    for (int b = 0; b < chain.bones.Count; b++)
                    {
                        chain.bones[b] = (Transform)EditorGUILayout.ObjectField(
                            $"  Bone {b + 1}", chain.bones[b], typeof(Transform), true);
                    }

                    chain.tip = (Transform)EditorGUILayout.ObjectField(
                        "  Tip", chain.tip, typeof(Transform), true);

                    chain.useAutoCurlAxis = EditorGUILayout.Toggle("  Auto Curl Axis", chain.useAutoCurlAxis);
                    if (!chain.useAutoCurlAxis)
                    {
                        chain.localCurlAxis = EditorGUILayout.Vector3Field("  Local Curl Axis", chain.localCurlAxis);
                    }

                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawActionButtons()
        {
            GUI.enabled = m_WristRoot != null && m_TargetObject != null;
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);

            if (GUILayout.Button("Pose Hand", GUILayout.Height(36)))
            {
                ExecutePose();
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = m_WristRoot != null;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset to Rest Pose", GUILayout.Height(26)))
            {
                ExecuteReset();
            }

            if (GUILayout.Button("Save Current as Rest Pose", GUILayout.Height(26)))
            {
                HandPoser.RecordDefaultPose(m_WristRoot, m_Config, true);
                SetStatus("Current pose saved as rest pose.", MessageType.Info);
            }

            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawStatusBox()
        {
            if (!string.IsNullOrEmpty(m_StatusMessage))
            {
                EditorGUILayout.HelpBox(m_StatusMessage, m_StatusType);
            }
        }

        private void DetectChains()
        {
            if (m_WristRoot == null)
            {
                m_Chains.Clear();
                return;
            }

            m_Chains = HandPoser.AutoDetectFingerChains(m_WristRoot, m_Config);
            m_Config.customChains = m_Chains;

            SetStatus($"Detected {m_Chains.Count} finger chains under '{m_WristRoot.name}'.", MessageType.Info);
        }

        private void ExecutePose()
        {
            if (m_WristRoot == null)
            {
                SetStatus("Cannot pose: Hand Root Bone is not assigned.", MessageType.Error);
                return;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(m_WristRoot))
            {
                SetStatus("Cannot pose a Prefab Asset directly. Please instantiate it in the Scene or open it in Prefab Stage.", MessageType.Error);
                return;
            }

            if (m_TargetObject == null)
            {
                SetStatus("Cannot pose: Target Interactable is not assigned.", MessageType.Error);
                return;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(m_TargetObject))
            {
                SetStatus("Cannot pose against a Prefab Asset directly. Please instantiate it in the Scene or open it in Prefab Stage.", MessageType.Error);
                return;
            }

            var colliders = m_TargetObject.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                SetStatus($"Target '{m_TargetObject.name}' does not contain any Colliders!", MessageType.Warning);
                return;
            }

            int solidCount = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col != null && col.enabled && (!m_Config.ignoreTriggers || !col.isTrigger))
                {
                    solidCount++;
                }
            }

            if (solidCount == 0)
            {
                SetStatus($"Target '{m_TargetObject.name}' has {colliders.Length} colliders, but all are triggers (ignoreTriggers enabled) or disabled. No solid colliders to grasp.", MessageType.Warning);
                return;
            }

            if (m_Chains == null || m_Chains.Count == 0)
            {
                DetectChains();
            }

            // Register undo for all bones before posing
            var allBones = m_WristRoot.GetComponentsInChildren<Transform>(true);
            Undo.RecordObjects(allBones, "Pose Hand Grasp");

            m_Config.customChains = m_Chains;
            HandPoser.PoseHand(m_WristRoot, colliders, m_Config);

            // Mark bones dirty for scene persistence
            for (int i = 0; i < allBones.Length; i++)
            {
                EditorUtility.SetDirty(allBones[i]);
                PrefabUtility.RecordPrefabInstancePropertyModifications(allBones[i]);
            }

            SetStatus($"Posed hand '{m_WristRoot.name}' against {solidCount} solid colliders on '{m_TargetObject.name}'.", MessageType.Info);
        }

        private void ExecuteReset()
        {
            if (m_WristRoot == null) return;

            var allBones = m_WristRoot.GetComponentsInChildren<Transform>(true);
            Undo.RecordObjects(allBones, "Reset Hand Pose");

            m_Config.customChains = m_Chains;
            HandPoser.ResetPose(m_WristRoot, m_Config);

            for (int i = 0; i < allBones.Length; i++)
            {
                EditorUtility.SetDirty(allBones[i]);
                PrefabUtility.RecordPrefabInstancePropertyModifications(allBones[i]);
            }

            SetStatus($"Hand '{m_WristRoot.name}' restored to rest pose.", MessageType.Info);
        }

        private void SetStatus(string message, MessageType type)
        {
            m_StatusMessage = message;
            m_StatusType = type;
            Repaint();
        }

        private static LayerMask DrawLayerMaskField(GUIContent label, LayerMask selected)
        {
            var layers = new List<string>();
            var layerNumbers = new List<int>();

            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(layerName);
                    layerNumbers.Add(i);
                }
            }

            int maskWithoutEmpty = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if (((1 << layerNumbers[i]) & selected.value) != 0)
                {
                    maskWithoutEmpty |= 1 << i;
                }
            }

            int newMaskVal = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());
            int mask = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if ((newMaskVal & (1 << i)) != 0)
                {
                    mask |= 1 << layerNumbers[i];
                }
            }

            return mask;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (m_WristRoot == null) return;

            Handles.color = new Color(0f, 0.85f, 1f, 0.7f);
            if (m_Chains != null)
            {
                for (int i = 0; i < m_Chains.Count; i++)
                {
                    var chain = m_Chains[i];
                    if (chain?.bones == null) continue;

                    for (int b = 0; b < chain.bones.Count; b++)
                    {
                        var bone = chain.bones[b];
                        if (bone == null) continue;

                        Transform next = (b + 1 < chain.bones.Count) ? chain.bones[b + 1] : chain.tip;
                        if (next != null)
                        {
                            Handles.DrawLine(bone.position, next.position);
                            Handles.SphereHandleCap(0, next.position, Quaternion.identity, m_Config.fingerRadius * 1.5f, EventType.Repaint);
                        }
                    }
                }
            }

            Vector3 palmPos = HandPoser.GetPalmPosition(m_WristRoot, m_Config);
            Handles.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Handles.SphereHandleCap(0, palmPos, Quaternion.identity, 0.01f, EventType.Repaint);
        }
    }
}
