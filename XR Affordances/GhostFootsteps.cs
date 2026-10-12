#pragma warning disable CS0618 // Reactive mode uses the deprecated XRI AffordanceSystem — intentional.

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.XR.CoreUtils.Bindings;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;

/// <summary>
/// Spline-driven ghost footstep guide. Place on a GameObject that also has a SplineContainer.
/// Supports Static (always on), Reactive (gaze/proximity), and Dynamic (NavMesh pathfinding) modes.
/// 
/// RELEASE NOTES: 
/// - Uses Object Pooling for footsteps to prevent GC allocations during dynamic re-baking.
/// - Uses MaterialPropertyBlock for alpha fading to allow GPU instancing and prevent material leaks.
/// </summary>
[RequireComponent(typeof(SplineContainer))]
public class GhostFootsteps : MonoBehaviour
{
    public enum FootstepMode { Static, Reactive, Dynamic }

    private struct FootstepSlot
    {
        public Transform tr;
        public MeshRenderer mr;
    }

    // -------------------------------------------------------------------------
    // Inspector

    [Header("Mode")]
    [SerializeField] private FootstepMode m_Mode = FootstepMode.Static;

    [Header("References")]
    [Tooltip("If null, will automatically resolve to Camera.main on Start.")]
    [SerializeField] private Transform m_Player;

    [Header("Path Sampling")]
    [SerializeField] private int m_StepCount = 12;
    [SerializeField] private float m_StepWidth = 0.18f;           // lateral L/R offset from spline centre (metres)
    [SerializeField] private float m_FloorSnapDistance = 2f;      // max raycast length below sample point
    [SerializeField] private LayerMask m_FloorLayers = ~0;

    [Header("Footstep Visuals")]
    [Tooltip("Leave empty for default procedural quad.")]
    [SerializeField] private Mesh m_FootstepMesh;                 
    [SerializeField] private Vector3 m_StepScale = new Vector3(0.12f, 0.001f, 0.18f);

    [Header("Animation")]
    [Tooltip("When enabled, footsteps sweep along the path as a travelling wave. When disabled, all footsteps show at uniform opacity.")]
    [SerializeField] private bool m_Animate = true;
    [SerializeField] private float m_LoopDuration = 2.5f;         // seconds per full sweep
    [SerializeField] private float m_TrailFraction = 0.55f;       // fraction of path length simultaneously visible
    [SerializeField] private float m_FadeWindow = 0.15f;          // fraction-of-path size of the fade-in / fade-out zone
    [SerializeField, Range(0.05f, 2f)] private float m_FadeDuration = 0.3f; // global alpha transition duration

    public enum ReactiveTriggerMode
    {
        GazeAndProximity,
        GazeOrProximity,
        GazeOnly,
        ProximityOnly
    }

    public enum ReactiveTargetSource
    {
        SplineStart,
        ClosestStep,
        TransformPosition
    }

    [Header("Reactive Mode")]
    [Tooltip("How gaze and proximity combine to activate footsteps.")]
    [SerializeField] private ReactiveTriggerMode m_ReactiveTrigger = ReactiveTriggerMode.GazeOrProximity;
    [Tooltip("What point the player gazes toward or approaches to trigger reactivity.")]
    [SerializeField] private ReactiveTargetSource m_ReactiveTarget = ReactiveTargetSource.SplineStart;
    [Tooltip("Optional custom target transform. If null, uses Spline start or footsteps center/position.")]
    [SerializeField] private Transform m_CustomReactiveTarget;
    [Tooltip("Optional XRI affordance state provider for hover/focus state subscription.")]
    [SerializeField] private XRInteractableAffordanceStateProvider m_AffordanceProvider;
    [Tooltip("Distance threshold (meters) for proximity trigger.")]
    [SerializeField, Range(0.1f, 20f)] private float m_ProximityThreshold = 4f;
    [Tooltip("Dot product cone threshold (0.75 ≈ 41° cone, 0.5 ≈ 60° cone).")]
    [SerializeField, Range(0.3f, 0.99f)] private float m_GazeFacingThreshold = 0.7f;
    [Tooltip("When enabled, footsteps smoothly dim based on player distance instead of discrete on/off.")]
    [SerializeField] private bool m_ContinuousProximityFade = false;
    [Tooltip("Draw gaze cone and reactive target lines in scene view when selected.")]
    [SerializeField] private bool m_ShowReactiveGizmos = true;

    [Header("Dynamic Mode")]
    [SerializeField] private Transform m_Destination;
    [Tooltip("Automatically activate and bake path on Start when in Dynamic mode.")]
    [SerializeField] private bool m_AutoActivate = true;
    [SerializeField] private float m_RecomputeInterval = 2f;      // seconds between path re-bakes
    [Tooltip("True: skip re-bake when destination hasn't moved. False: always re-bake.")]
    [SerializeField] private bool m_CheapRecompute = true;
    [SerializeField] private float m_RecomputeMoveThreshold = 0.5f; // metres destination must move to trigger a cheap re-bake
    [SerializeField] private float m_DestinationReachedRadius = 0.8f; // metres — fires OnDestinationReached

    [Header("Events")]
    [SerializeField] private UnityEvent m_OnActivated;            // Reactive: on gaze/proximity enter. Dynamic: on IsActive→true.
    [SerializeField] private UnityEvent m_OnDeactivated;          // Reactive: on gaze/proximity exit.  Dynamic: on IsActive→false.
    [SerializeField] private UnityEvent m_OnDestinationReached;   // Dynamic only: player enters destination radius.

    [Header("Debug")]
    [SerializeField, Range(0f, 1f)] private float m_GlobalAlpha;
    [SerializeField] private bool m_IsGazing;

    // -------------------------------------------------------------------------
    // Private State

    private SplineContainer m_Spline;
    private FootstepSlot[] m_Steps;
    private float m_WaveCursor;
    private Coroutine m_FadeRoutine;

    // Affordance / Reactivity
    private readonly BindingsGroup m_Bindings = new BindingsGroup();
    private byte m_AffordanceState = AffordanceStateShortcuts.idle;

    // Dynamic Tracking
    private bool m_IsActive;
    private NavMeshPath m_NavPath;
    private Vector3 m_LastPlayerPos;
    private Vector3 m_LastDestPos;
    private Coroutine m_RecomputeRoutine;
    private bool m_DestinationReachedFired;

    // Visual Rendering Optimisations (Object Pooling / MaterialPropertyBlock)
    private MaterialPropertyBlock m_PropBlock;
    private int m_ColorPropId = -1;
    private bool m_ColorPropResolved;
    private Color m_BaseColor = Color.white;
    private Mesh m_FallbackMesh;
    private Material m_FallbackMaterial;

    // -------------------------------------------------------------------------
    // Public API

    /// <summary>
    /// Controls Dynamic mode activation. Setting true bakes the NavMesh path, 
    /// begins periodic updates, and fades the footsteps in.
    /// </summary>
    public bool IsActive
    {
        get => m_IsActive;
        set
        {
            if (m_IsActive == value) return;
            m_IsActive = value;

            if (m_IsActive)
            {
                m_OnActivated?.Invoke();
                m_DestinationReachedFired = false;
                if (m_Mode == FootstepMode.Dynamic)
                {
                    BakeNavPath();
                    if (m_RecomputeRoutine != null) StopCoroutine(m_RecomputeRoutine);
                    m_RecomputeRoutine = StartCoroutine(PeriodicRecompute());
                }
                StartFade(1f);
            }
            else
            {
                m_OnDeactivated?.Invoke();
                if (m_RecomputeRoutine != null) { StopCoroutine(m_RecomputeRoutine); m_RecomputeRoutine = null; }
                StartFade(0f);
            }
        }
    }

    // Zero-argument wrappers for UnityEvent inspector binding.
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void QuickFadeIn() => StartFade(1f);
    public void QuickFadeOut() => StartFade(0f);

    public void StartFade(float targetAlpha)
    {
        if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
        m_FadeRoutine = StartCoroutine(FadeCoroutine(targetAlpha));
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle

    private void Awake()
    {
        m_Spline = GetComponent<SplineContainer>();
        m_GlobalAlpha = m_Mode == FootstepMode.Static ? 1f : 0f;

        m_AffordanceProvider ??= GetComponent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInParent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInChildren<XRInteractableAffordanceStateProvider>();
    }

    private void Start()
    {
        m_Player ??= Camera.main?.transform;
        if (m_Mode == FootstepMode.Dynamic)
        {
            m_NavPath = new NavMeshPath();
            if (m_AutoActivate)
                IsActive = true;
        }
        else
        {
            RebuildSteps();
        }
    }

    private void OnEnable()
    {
        if (m_Mode != FootstepMode.Reactive) return;
        m_Bindings.Clear();

        m_AffordanceProvider ??= GetComponent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInParent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInChildren<XRInteractableAffordanceStateProvider>();

        if (m_AffordanceProvider != null)
        {
            m_AffordanceProvider.ignoreFocusEvents = false;
            m_Bindings.AddBinding(
                m_AffordanceProvider.currentAffordanceStateData.SubscribeAndUpdate(OnAffordanceStateChanged));
        }
    }

    private void OnDisable()
    {
        m_Bindings.Clear();
        if (m_FadeRoutine != null) { StopCoroutine(m_FadeRoutine); m_FadeRoutine = null; }
        m_IsGazing = false;
        if (m_RecomputeRoutine != null) { StopCoroutine(m_RecomputeRoutine); m_RecomputeRoutine = null; }
    }

    private void OnDestroy()
    {
        // Prevent memory leaks from procedurally generated fallback assets
        if (m_FallbackMesh != null)
        {
            if (Application.isPlaying) Destroy(m_FallbackMesh);
            else DestroyImmediate(m_FallbackMesh);
        }
        if (m_FallbackMaterial != null)
        {
            if (Application.isPlaying) Destroy(m_FallbackMaterial);
            else DestroyImmediate(m_FallbackMaterial);
        }
    }

    private void Update()
    {
        AdvanceWave(Time.deltaTime);

        switch (m_Mode)
        {
            case FootstepMode.Reactive: UpdateReactive(); break;
            case FootstepMode.Dynamic:  UpdateDynamic(); break;
        }
    }

    // -------------------------------------------------------------------------
    // Step Construction & Object Pooling

    /// <summary>
    /// Re-evaluates positions along the spline. Reuses GameObjects from the pool 
    /// instead of destroying and instantiating to prevent GC spikes in Dynamic mode.
    /// </summary>
    public void RebuildSteps()
    {
        if (m_Spline == null) m_Spline = GetComponent<SplineContainer>();
        if (m_Spline == null || m_Spline.Splines.Count == 0) return;
        var spline = m_Spline.Splines[0];
        if (spline.Count < 2) return;

        int count = Mathf.Max(2, m_StepCount);

        // Resize array and destroy leftovers if step count changed
        if (m_Steps == null || m_Steps.Length != count)
        {
            if (m_Steps != null)
            {
                foreach (var s in m_Steps)
                {
                    if (s.tr != null)
                    {
                        if (Application.isPlaying) Destroy(s.tr.gameObject);
                        else DestroyImmediate(s.tr.gameObject);
                    }
                }
            }
            else
            {
                for (int c = transform.childCount - 1; c >= 0; c--)
                {
                    var child = transform.GetChild(c);
                    if (child.name.StartsWith("Step_"))
                    {
                        if (Application.isPlaying) Destroy(child.gameObject);
                        else DestroyImmediate(child.gameObject);
                    }
                }
            }
            m_Steps = new FootstepSlot[count];
        }

        // Cache rendering parameters
        if (m_PropBlock == null) m_PropBlock = new MaterialPropertyBlock();
        if (m_FallbackMaterial == null) m_FallbackMaterial = CreateDefaultMaterial();
        Material sharedMat = m_FallbackMaterial;

        if (!m_ColorPropResolved)
        {
            m_ColorPropId = FindColorPropId(sharedMat);
            m_ColorPropResolved = true;
        }
        m_BaseColor = sharedMat.HasProperty(m_ColorPropId) ? sharedMat.GetColor(m_ColorPropId) : Color.white;

        Mesh sharedMesh = m_FootstepMesh;
        if (sharedMesh == null)
        {
            if (m_FallbackMesh == null) m_FallbackMesh = BuildQuadMesh();
            sharedMesh = m_FallbackMesh;
        }

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);    // 0..1 normalised along spline
            SplineUtility.Evaluate(spline, t, out float3 localPos, out float3 localTan, out float3 _);

            Vector3 worldPos = m_Spline.transform.TransformPoint(localPos);
            Vector3 worldTan = m_Spline.transform.TransformDirection((Vector3)localTan).normalized;
            if (worldTan.sqrMagnitude < 0.0001f) worldTan = Vector3.forward;

            bool isLeft = (i % 2) == 0;
            Vector3 lateral = Vector3.Cross(worldTan, Vector3.up).normalized * (isLeft ? -m_StepWidth : m_StepWidth);
            Vector3 stepPos = worldPos + lateral;

            // Snap to floor surface
            if (Physics.Raycast(stepPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, m_FloorSnapDistance + 0.5f, m_FloorLayers))
                stepPos = hit.point + Vector3.up * 0.002f; // Slight offset to prevent Z-fighting

            // Object Pooling: Create new step only if it doesn't exist
            if (m_Steps[i].tr == null)
            {
                var go = new GameObject($"Step_{i:D2}_{(isLeft ? "L" : "R")}");
                go.transform.SetParent(transform, false); // false preserves local scale
                
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = sharedMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = sharedMat;

                m_Steps[i] = new FootstepSlot { tr = go.transform, mr = mr };
            }

            // Update transform
            m_Steps[i].tr.position = stepPos;
            m_Steps[i].tr.rotation = Quaternion.LookRotation(worldTan, Vector3.up);
            m_Steps[i].tr.localScale = isLeft ? m_StepScale : new Vector3(-m_StepScale.x, m_StepScale.y, m_StepScale.z);

            // Hide immediately to prevent frame-1 flicker when playing; show in editor
            UpdateStepAlpha(i, Application.isPlaying ? 0f : 1f);
        }
    }

    // -------------------------------------------------------------------------
    // Wave Animation

    private void AdvanceWave(float dt)
    {
        if (m_Steps == null || m_Steps.Length == 0) return;

        if (!m_Animate)
        {
            for (int i = 0; i < m_Steps.Length; i++)
                UpdateStepAlpha(i, m_GlobalAlpha);
            return;
        }

        if (m_LoopDuration <= 0f) return;
        m_WaveCursor = (m_WaveCursor + dt / m_LoopDuration) % 1f;

        float fadeW = Mathf.Max(0.001f, m_FadeWindow);

        for (int i = 0; i < m_Steps.Length; i++)
        {
            // 'behind': 0 = cursor just arrived; 1 = full loop since cursor was here
            float behind = (m_WaveCursor - (float)i / m_Steps.Length + 1f) % 1f;

            float alpha;
            if (behind < fadeW)
                alpha = Mathf.SmoothStep(0f, 1f, behind / fadeW);
            else if (behind < m_TrailFraction)
                alpha = 1f;
            else if (behind < m_TrailFraction + fadeW)
                alpha = Mathf.SmoothStep(1f, 0f, (behind - m_TrailFraction) / fadeW);
            else
                alpha = 0f;

            UpdateStepAlpha(i, alpha * m_GlobalAlpha);
        }
    }

    private void UpdateStepAlpha(int index, float alphaMultiplier)
    {
        if (m_Steps[index].mr == null || !m_ColorPropResolved || m_ColorPropId < 0) return;
        
        m_Steps[index].mr.GetPropertyBlock(m_PropBlock);
        Color c = m_BaseColor;
        c.a *= alphaMultiplier;
        m_PropBlock.SetColor(m_ColorPropId, c);
        m_Steps[index].mr.SetPropertyBlock(m_PropBlock);
    }

    // -------------------------------------------------------------------------
    // Fading

    private IEnumerator FadeCoroutine(float target)
    {
        float start = m_GlobalAlpha, elapsed = 0f;
        while (elapsed < m_FadeDuration)
        {
            elapsed += Time.deltaTime;
            m_GlobalAlpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / m_FadeDuration));
            yield return null;
        }
        m_GlobalAlpha = target;
        m_FadeRoutine = null;
    }

    // -------------------------------------------------------------------------
    // Reactive Mode

    private Vector3 GetReactiveTargetPosition()
    {
        if (m_CustomReactiveTarget != null)
            return m_CustomReactiveTarget.position;

        switch (m_ReactiveTarget)
        {
            case ReactiveTargetSource.SplineStart:
                if (m_Spline != null && m_Spline.Splines.Count > 0 && m_Spline.Splines[0].Count > 0)
                {
                    float3 knotPos = m_Spline.Splines[0][0].Position;
                    return m_Spline.transform.TransformPoint(knotPos);
                }
                break;

            case ReactiveTargetSource.ClosestStep:
                if (m_Steps != null && m_Steps.Length > 0 && m_Player != null)
                {
                    Vector3 closest = m_Steps[0].tr != null ? m_Steps[0].tr.position : transform.position;
                    float minSqr = float.MaxValue;
                    for (int i = 0; i < m_Steps.Length; i++)
                    {
                        if (m_Steps[i].tr == null) continue;
                        float sqr = (m_Steps[i].tr.position - m_Player.position).sqrMagnitude;
                        if (sqr < minSqr)
                        {
                            minSqr = sqr;
                            closest = m_Steps[i].tr.position;
                        }
                    }
                    return closest;
                }
                break;

            case ReactiveTargetSource.TransformPosition:
            default:
                break;
        }

        return transform.position;
    }

    private void UpdateReactive()
    {
        m_Player ??= Camera.main?.transform;
        if (m_Player == null) return;

        Vector3 targetPos = GetReactiveTargetPosition();
        Vector3 toTarget = targetPos - m_Player.position;
        float dist = toTarget.magnitude;

        // XRI Affordance provider state (focused or hovered)
        bool affordanceFocused = m_AffordanceState == AffordanceStateShortcuts.focused;
        bool affordanceHovered = m_AffordanceState == AffordanceStateShortcuts.hovered || m_AffordanceState == AffordanceStateShortcuts.hoveredPriority;

        // Head-facing gaze test
        bool gazeMet = affordanceFocused || (dist > 0.01f && Vector3.Dot(m_Player.forward, toTarget / dist) >= m_GazeFacingThreshold);
        // Proximity test
        bool proxMet = dist <= m_ProximityThreshold;

        bool activeNow = false;
        switch (m_ReactiveTrigger)
        {
            case ReactiveTriggerMode.GazeAndProximity:
                activeNow = gazeMet && proxMet;
                break;
            case ReactiveTriggerMode.GazeOrProximity:
                activeNow = gazeMet || proxMet || affordanceHovered;
                break;
            case ReactiveTriggerMode.GazeOnly:
                activeNow = gazeMet;
                break;
            case ReactiveTriggerMode.ProximityOnly:
                activeNow = proxMet || affordanceHovered;
                break;
        }

        if (m_ContinuousProximityFade)
        {
            // Smoothly track distance gradient when in proximity range
            float targetAlpha = 0f;
            if (activeNow)
            {
                targetAlpha = m_ProximityThreshold > 0.001f
                    ? Mathf.Clamp01(1f - (dist / m_ProximityThreshold))
                    : 1f;
                if (gazeMet) targetAlpha = Mathf.Max(targetAlpha, 0.7f);
            }

            if (m_FadeRoutine != null) { StopCoroutine(m_FadeRoutine); m_FadeRoutine = null; }
            m_GlobalAlpha = Mathf.MoveTowards(m_GlobalAlpha, targetAlpha, Time.deltaTime * (1f / Mathf.Max(0.05f, m_FadeDuration)));

            if (activeNow != m_IsGazing)
            {
                m_IsGazing = activeNow;
                if (m_IsGazing) m_OnActivated?.Invoke();
                else m_OnDeactivated?.Invoke();
            }
        }
        else
        {
            if (activeNow != m_IsGazing)
            {
                m_IsGazing = activeNow;
                if (m_IsGazing)
                {
                    m_OnActivated?.Invoke();
                    StartFade(1f);
                }
                else
                {
                    m_OnDeactivated?.Invoke();
                    StartFade(0f);
                }
            }
        }
    }

    private void OnAffordanceStateChanged(AffordanceStateData data) => m_AffordanceState = data.stateIndex;

    private void OnDrawGizmosSelected()
    {
        if (m_Mode != FootstepMode.Reactive || !m_ShowReactiveGizmos) return;

        Vector3 targetPos = GetReactiveTargetPosition();

        // Target marker and proximity sphere
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(targetPos, m_ProximityThreshold);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(targetPos, 0.08f);

        Transform playerTr = m_Player != null ? m_Player : Camera.main?.transform;
        if (playerTr == null) return;

        Vector3 toTarget = targetPos - playerTr.position;
        float dist = toTarget.magnitude;
        bool gazeMet = dist > 0.01f && Vector3.Dot(playerTr.forward, toTarget / dist) >= m_GazeFacingThreshold;
        bool proxMet = dist <= m_ProximityThreshold;

        // Line to target (green when criteria met, orange/gray otherwise)
        Gizmos.color = (gazeMet && proxMet) ? Color.green : (proxMet ? Color.yellow : Color.gray);
        Gizmos.DrawLine(playerTr.position, targetPos);
    }

    // -------------------------------------------------------------------------
    // Dynamic Mode

    private void UpdateDynamic()
    {
        if (!m_IsActive || m_Destination == null || m_DestinationReachedFired) return;
        m_Player ??= Camera.main?.transform;
        if (m_Player == null) return;

        if (Vector3.Distance(m_Player.position, m_Destination.position) <= m_DestinationReachedRadius)
        {
            m_DestinationReachedFired = true;
            m_OnDestinationReached?.Invoke();
        }
    }

    private IEnumerator PeriodicRecompute()
    {
        while (m_IsActive)
        {
            yield return new WaitForSeconds(m_RecomputeInterval);
            if (!m_IsActive) break;

            Transform playerTr = m_Player != null ? m_Player : Camera.main?.transform;
            Vector3 currentPlayerPos = playerTr != null ? playerTr.position : transform.position;

            bool destMoved = m_Destination != null
                          && Vector3.Distance(m_Destination.position, m_LastDestPos) >= m_RecomputeMoveThreshold;
            bool playerMoved = Vector3.Distance(currentPlayerPos, m_LastPlayerPos) >= m_RecomputeMoveThreshold;

            if (!m_CheapRecompute || destMoved || playerMoved)
            {
                BakeNavPath();
            }
        }
        m_RecomputeRoutine = null;
    }

    public Transform Destination => m_Destination;

    private static NavMeshData s_RuntimeNavMeshData;
    private static NavMeshDataInstance s_RuntimeNavMeshInstance;

    /// <summary>
    /// Verifies that NavMesh coverage exists between start and target. If the scene lacks a baked NavMesh,
    /// dynamically builds and registers a runtime NavMeshData from scene colliders/geometry.
    /// </summary>
    public static bool EnsureNavMesh(Vector3 startPos, Vector3 targetPos, bool forceRebuild = false)
    {
        if (!forceRebuild &&
            NavMesh.SamplePosition(startPos, out _, 5f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(targetPos, out _, 5f, NavMesh.AllAreas))
        {
            return true;
        }

        if (s_RuntimeNavMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(s_RuntimeNavMeshInstance);
        }

        Vector3 center = (startPos + targetPos) * 0.5f;
        Vector3 size = new Vector3(
            Mathf.Max(120f, Mathf.Abs(targetPos.x - startPos.x) + 80f),
            Mathf.Max(40f, Mathf.Abs(targetPos.y - startPos.y) + 40f),
            Mathf.Max(120f, Mathf.Abs(targetPos.z - startPos.z) + 80f)
        );
        Bounds bounds = new Bounds(center, size);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        if (settings.agentRadius <= 0.01f) settings.agentRadius = 0.4f;
        if (settings.agentHeight <= 0.01f) settings.agentHeight = 1.8f;
        if (settings.agentSlope <= 0.01f) settings.agentSlope = 45f;
        if (settings.agentClimb <= 0.01f) settings.agentClimb = 0.4f;

        var sources = new System.Collections.Generic.List<NavMeshBuildSource>();
        var markups = new System.Collections.Generic.List<NavMeshBuildMarkup>();

        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
        if (sources.Count == 0)
        {
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.RenderMeshes, 0, markups, sources);
        }

        if (sources.Count == 0)
            return false;

        s_RuntimeNavMeshData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (s_RuntimeNavMeshData != null)
        {
            s_RuntimeNavMeshInstance = NavMesh.AddNavMeshData(s_RuntimeNavMeshData);
            return s_RuntimeNavMeshInstance.valid;
        }

        return false;
    }

    public void BakeNavPath(bool forceNavRebuild = false)
    {
        if (m_Spline == null) m_Spline = GetComponent<SplineContainer>();
        if (m_Spline == null) return;

        if (m_Destination == null)
        {
            Debug.LogWarning("[GhostFootsteps] Cannot bake path: Destination Transform is not assigned.", this);
            return;
        }

        if (m_NavPath == null) m_NavPath = new NavMeshPath();

        Transform playerTr = m_Player != null ? m_Player : Camera.main?.transform;
        Vector3 origin = playerTr != null ? playerTr.position : transform.position;

        // Auto-bake runtime NavMesh if missing or forced
        EnsureNavMesh(origin, m_Destination.position, forceNavRebuild);

        Vector3 startPos = origin;
        if (NavMesh.SamplePosition(origin, out NavMeshHit hitOrigin, 10f, NavMesh.AllAreas))
        {
            startPos = hitOrigin.position;
        }

        Vector3 targetPos = m_Destination.position;
        if (NavMesh.SamplePosition(m_Destination.position, out NavMeshHit hitTarget, 10f, NavMesh.AllAreas))
        {
            targetPos = hitTarget.position;
        }

        Vector3[] corners;
        if (NavMesh.CalculatePath(startPos, targetPos, NavMesh.AllAreas, m_NavPath)
            && m_NavPath.status != NavMeshPathStatus.PathInvalid
            && m_NavPath.corners.Length >= 2)
        {
            corners = m_NavPath.corners;
        }
        else
        {
            Debug.LogWarning("[GhostFootsteps] NavMesh path could not find a traversable route. Using direct fallback path.", this);
            corners = new[] { startPos, targetPos };
        }

        m_LastDestPos = m_Destination.position;
        m_LastPlayerPos = origin;

        if (m_Spline.Splines.Count == 0)
        {
            m_Spline.AddSpline();
        }

        var spline = m_Spline.Splines[0];
        spline.Clear();
        foreach (var corner in corners)
        {
            float3 local = (float3)m_Spline.transform.InverseTransformPoint(corner);
            spline.Add(new BezierKnot(local), TangentMode.AutoSmooth);
        }

        RebuildSteps();
    }

    // -------------------------------------------------------------------------
    // Utilities

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "FootstepQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f), new Vector3(-0.5f, 0f,  0.5f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Material CreateDefaultMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            name = "GhostFootstep_Default",
            color = new Color(1f, 1f, 1f, 0.6f)
        };
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);

        // URP compiles separate shader variants for opaque vs transparent.
        // Without the keyword the opaque variant is used and alpha is ignored.
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = 3000;

        // Standard alpha-blend factors for URP Unlit
        if (mat.HasProperty("_SrcBlend"))    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite"))      mat.SetFloat("_ZWrite", 0f);

        return mat;
    }

    private static int FindColorPropId(Material mat)
    {
        foreach (var name in new[] { "_BaseColor", "_Color", "_MainColor" })
            if (mat.HasProperty(name)) return Shader.PropertyToID(name);
        return -1;
    }
}
