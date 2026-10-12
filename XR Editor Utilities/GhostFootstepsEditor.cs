#pragma warning disable CS0618
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostFootsteps))]
[CanEditMultipleObjects]
public class GhostFootstepsEditor : Editor
{
    private SerializedProperty m_ModeProp;

    // Visuals & Core Sampling
    private SerializedProperty m_StepCountProp;
    private SerializedProperty m_StepWidthProp;
    private SerializedProperty m_StepScaleProp;
    private SerializedProperty m_FootstepMeshProp;

    // Animation
    private SerializedProperty m_AnimateProp;
    private SerializedProperty m_LoopDurationProp;

    // Reactive Mode
    private SerializedProperty m_ReactiveTriggerProp;
    private SerializedProperty m_ReactiveTargetProp;
    private SerializedProperty m_CustomReactiveTargetProp;
    private SerializedProperty m_AffordanceProviderProp;
    private SerializedProperty m_ProximityThresholdProp;
    private SerializedProperty m_GazeFacingThresholdProp;

    // Dynamic Mode
    private SerializedProperty m_DestinationProp;
    private SerializedProperty m_RecomputeIntervalProp;
    private SerializedProperty m_DestinationReachedRadiusProp;

    // Events
    private SerializedProperty m_OnActivatedProp;
    private SerializedProperty m_OnDeactivatedProp;
    private SerializedProperty m_OnDestinationReachedProp;

    // Advanced / Internal Fine-Tuning
    private SerializedProperty m_PlayerProp;
    private SerializedProperty m_FloorSnapDistanceProp;
    private SerializedProperty m_FloorLayersProp;
    private SerializedProperty m_FadeDurationProp;
    private SerializedProperty m_TrailFractionProp;
    private SerializedProperty m_FadeWindowProp;
    private SerializedProperty m_CheapRecomputeProp;
    private SerializedProperty m_RecomputeMoveThresholdProp;
    private SerializedProperty m_ContinuousProximityFadeProp;
    private SerializedProperty m_ShowReactiveGizmosProp;
    private SerializedProperty m_GlobalAlphaProp;
    private SerializedProperty m_IsGazingProp;

    private bool m_ShowAdvancedFoldout;

    private void OnEnable()
    {
        m_ModeProp = serializedObject.FindProperty("m_Mode");

        m_StepCountProp = serializedObject.FindProperty("m_StepCount");
        m_StepWidthProp = serializedObject.FindProperty("m_StepWidth");
        m_StepScaleProp = serializedObject.FindProperty("m_StepScale");
        m_FootstepMeshProp = serializedObject.FindProperty("m_FootstepMesh");

        m_AnimateProp = serializedObject.FindProperty("m_Animate");
        m_LoopDurationProp = serializedObject.FindProperty("m_LoopDuration");

        m_ReactiveTriggerProp = serializedObject.FindProperty("m_ReactiveTrigger");
        m_ReactiveTargetProp = serializedObject.FindProperty("m_ReactiveTarget");
        m_CustomReactiveTargetProp = serializedObject.FindProperty("m_CustomReactiveTarget");
        m_AffordanceProviderProp = serializedObject.FindProperty("m_AffordanceProvider");
        m_ProximityThresholdProp = serializedObject.FindProperty("m_ProximityThreshold");
        m_GazeFacingThresholdProp = serializedObject.FindProperty("m_GazeFacingThreshold");

        m_DestinationProp = serializedObject.FindProperty("m_Destination");
        m_RecomputeIntervalProp = serializedObject.FindProperty("m_RecomputeInterval");
        m_DestinationReachedRadiusProp = serializedObject.FindProperty("m_DestinationReachedRadius");

        m_OnActivatedProp = serializedObject.FindProperty("m_OnActivated");
        m_OnDeactivatedProp = serializedObject.FindProperty("m_OnDeactivated");
        m_OnDestinationReachedProp = serializedObject.FindProperty("m_OnDestinationReached");

        m_PlayerProp = serializedObject.FindProperty("m_Player");
        m_FloorSnapDistanceProp = serializedObject.FindProperty("m_FloorSnapDistance");
        m_FloorLayersProp = serializedObject.FindProperty("m_FloorLayers");
        m_FadeDurationProp = serializedObject.FindProperty("m_FadeDuration");
        m_TrailFractionProp = serializedObject.FindProperty("m_TrailFraction");
        m_FadeWindowProp = serializedObject.FindProperty("m_FadeWindow");
        m_CheapRecomputeProp = serializedObject.FindProperty("m_CheapRecompute");
        m_RecomputeMoveThresholdProp = serializedObject.FindProperty("m_RecomputeMoveThreshold");
        m_ContinuousProximityFadeProp = serializedObject.FindProperty("m_ContinuousProximityFade");
        m_ShowReactiveGizmosProp = serializedObject.FindProperty("m_ShowReactiveGizmos");
        m_GlobalAlphaProp = serializedObject.FindProperty("m_GlobalAlpha");
        m_IsGazingProp = serializedObject.FindProperty("m_IsGazing");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(m_ModeProp, new GUIContent("Guidance Mode"));
        EditorGUILayout.Space(4);

        if (m_ModeProp.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("Selected footstep guides have different modes.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        var mode = (GhostFootsteps.FootstepMode)m_ModeProp.enumValueIndex;

        // Mode-Specific Core Settings
        switch (mode)
        {
            case GhostFootsteps.FootstepMode.Static:
                DrawStaticSection();
                break;
            case GhostFootsteps.FootstepMode.Reactive:
                DrawReactiveSection();
                break;
            case GhostFootsteps.FootstepMode.Dynamic:
                DrawDynamicSection();
                break;
        }

        // Mode-Specific Events
        DrawEventsSection(mode);

        // Core Visuals and Animation
        DrawVisualsAndAnimation();

        // Advanced / Internal Fine-Tuning
        DrawAdvancedSection(mode);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawStaticSection()
    {
        EditorGUILayout.LabelField("Static Path", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Footsteps are permanently visible along the SplineContainer knots.",
            MessageType.Info);

        if (GUILayout.Button("Rebuild Footsteps"))
        {
            foreach (var t in targets)
            {
                if (t is GhostFootsteps gf)
                {
                    Undo.RecordObject(gf, "Rebuild Footsteps");
                    gf.RebuildSteps();
                    EditorUtility.SetDirty(gf);
                }
            }
        }
        EditorGUILayout.Space(6);
    }

    private void DrawReactiveSection()
    {
        EditorGUILayout.LabelField("Reactive Triggers", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(m_ReactiveTriggerProp, new GUIContent("Trigger Mode"));
        EditorGUILayout.PropertyField(m_ReactiveTargetProp, new GUIContent("Target Reference"));

        var targetSource = (GhostFootsteps.ReactiveTargetSource)m_ReactiveTargetProp.enumValueIndex;
        if (targetSource == GhostFootsteps.ReactiveTargetSource.TransformPosition || m_CustomReactiveTargetProp.objectReferenceValue != null)
        {
            EditorGUILayout.PropertyField(m_CustomReactiveTargetProp, new GUIContent("Target Transform"));
        }

        EditorGUILayout.PropertyField(m_AffordanceProviderProp, new GUIContent("Affordance Provider"));

        var triggerMode = (GhostFootsteps.ReactiveTriggerMode)m_ReactiveTriggerProp.enumValueIndex;
        if (triggerMode != GhostFootsteps.ReactiveTriggerMode.GazeOnly)
        {
            EditorGUILayout.PropertyField(m_ProximityThresholdProp, new GUIContent("Proximity Range (m)"));
        }

        if (triggerMode != GhostFootsteps.ReactiveTriggerMode.ProximityOnly)
        {
            EditorGUILayout.PropertyField(m_GazeFacingThresholdProp, new GUIContent("Gaze Facing Angle"));
        }

        EditorGUILayout.Space(6);
    }

    private void DrawDynamicSection()
    {
        EditorGUILayout.LabelField("Dynamic Navigation", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(m_DestinationProp, new GUIContent("Destination"));
        if (m_DestinationProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "A Destination Transform is required for dynamic navigation.",
                MessageType.Warning);
        }

        EditorGUILayout.PropertyField(m_RecomputeIntervalProp, new GUIContent("Update Interval (s)"));
        EditorGUILayout.PropertyField(m_DestinationReachedRadiusProp, new GUIContent("Arrival Radius (m)"));

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bake Nav Path Now"))
            {
                foreach (var t in targets)
                {
                    if (t is GhostFootsteps gf)
                    {
                        Undo.RecordObject(gf, "Bake Nav Path");
                        gf.BakeNavPath();
                        EditorUtility.SetDirty(gf);
                    }
                }
            }

            if (GUILayout.Button("Auto-Bake Scene NavMesh"))
            {
                AutoBakeSceneNavMesh();
            }
        }

        if (Application.isPlaying && targets.Length == 1 && target is GhostFootsteps runtimeGf)
        {
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (runtimeGf.IsActive)
                {
                    if (GUILayout.Button("Deactivate Path"))
                        runtimeGf.IsActive = false;
                }
                else
                {
                    if (GUILayout.Button("Activate Path"))
                        runtimeGf.IsActive = true;
                }
            }
            EditorGUILayout.LabelField("Runtime Status", runtimeGf.IsActive ? "Active" : "Inactive", EditorStyles.miniBoldLabel);
        }

        EditorGUILayout.Space(6);
    }

    private void AutoBakeSceneNavMesh()
    {
        foreach (var t in targets)
        {
            if (t is GhostFootsteps gf)
            {
                Vector3 origin = gf.transform.position;
                Vector3 dest = gf.Destination != null ? gf.Destination.position : origin + Vector3.forward * 10f;
                GhostFootsteps.EnsureNavMesh(origin, dest, forceRebuild: true);
                gf.BakeNavPath(forceNavRebuild: true);
                EditorUtility.SetDirty(gf);
            }
        }
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
    }

    private void DrawEventsSection(GhostFootsteps.FootstepMode mode)
    {
        if (mode == GhostFootsteps.FootstepMode.Static)
            return;

        EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
        if (mode == GhostFootsteps.FootstepMode.Dynamic)
        {
            EditorGUILayout.PropertyField(m_OnDestinationReachedProp, new GUIContent("On Arrival Reached"));
        }

        EditorGUILayout.PropertyField(m_OnActivatedProp, new GUIContent("On Activated"));
        EditorGUILayout.PropertyField(m_OnDeactivatedProp, new GUIContent("On Deactivated"));
        EditorGUILayout.Space(6);
    }

    private void DrawVisualsAndAnimation()
    {
        EditorGUILayout.LabelField("Footstep Appearance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_StepCountProp, new GUIContent("Step Count"));
        EditorGUILayout.PropertyField(m_StepWidthProp, new GUIContent("Step Width (m)"));
        EditorGUILayout.PropertyField(m_StepScaleProp, new GUIContent("Step Scale"));
        EditorGUILayout.PropertyField(m_FootstepMeshProp, new GUIContent("Custom Mesh (Optional)"));
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_AnimateProp, new GUIContent("Animate Sweep"));
        if (m_AnimateProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(m_LoopDurationProp, new GUIContent("Sweep Duration (s)"));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space(6);
    }

    private void DrawAdvancedSection(GhostFootsteps.FootstepMode mode)
    {
        m_ShowAdvancedFoldout = EditorGUILayout.Foldout(m_ShowAdvancedFoldout, "Advanced Settings", true);
        if (!m_ShowAdvancedFoldout) return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(m_PlayerProp, new GUIContent("Player / HMD Override"));
        EditorGUILayout.PropertyField(m_FloorSnapDistanceProp, new GUIContent("Floor Snap Distance"));
        EditorGUILayout.PropertyField(m_FloorLayersProp, new GUIContent("Floor LayerMask"));
        EditorGUILayout.PropertyField(m_FadeDurationProp, new GUIContent("Alpha Transition Time"));

        if (m_AnimateProp.boolValue)
        {
            EditorGUILayout.PropertyField(m_TrailFractionProp, new GUIContent("Trail Fraction"));
            EditorGUILayout.PropertyField(m_FadeWindowProp, new GUIContent("Fade Window"));
        }

        if (mode == GhostFootsteps.FootstepMode.Reactive)
        {
            EditorGUILayout.PropertyField(m_ContinuousProximityFadeProp, new GUIContent("Continuous Proximity Fade"));
            EditorGUILayout.PropertyField(m_ShowReactiveGizmosProp, new GUIContent("Show Scene Gizmos"));
        }
        else if (mode == GhostFootsteps.FootstepMode.Dynamic)
        {
            EditorGUILayout.PropertyField(m_CheapRecomputeProp, new GUIContent("Skip Unmoved Updates"));
            if (m_CheapRecomputeProp.boolValue)
            {
                EditorGUILayout.PropertyField(m_RecomputeMoveThresholdProp, new GUIContent("Distance Move Threshold"));
            }
        }

        EditorGUILayout.Space(2);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(m_GlobalAlphaProp, new GUIContent("Debug Alpha"));
            if (mode == GhostFootsteps.FootstepMode.Reactive)
            {
                EditorGUILayout.PropertyField(m_IsGazingProp, new GUIContent("Debug Is Gazing"));
            }
            if (mode == GhostFootsteps.FootstepMode.Dynamic && target is GhostFootsteps gf)
            {
                EditorGUILayout.Toggle("Debug Is Active", gf.IsActive);
            }
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }
}
