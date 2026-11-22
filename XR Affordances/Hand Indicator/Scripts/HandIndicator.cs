#pragma warning disable CS0618 // AffordanceSystem is deprecated in XRI 3.x — intentional fallback support.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.XR.CoreUtils;
using Unity.XR.CoreUtils.Bindings;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;

/// <summary>
/// Controls hand indicator affordance visibility with smooth fading, gaze, proximity, focus, and handedness-aware grab conditions.
/// Supports both Off-by-default (shows under conditions) and On-by-default (hides under conditions).
/// </summary>
public class HandIndicator : MonoBehaviour
{
    public enum HandSelectionMode
    {
        [InspectorName("None")]
        None = 0,

        [InspectorName("Left Hand")]
        LeftHand = 1,

        [InspectorName("Right Hand")]
        RightHand = 2,

        [InspectorName("Either Hand (L/R)")]
        EitherHand = 3,

        [InspectorName("Any Selection")]
        AnySelection = 4
    }

    // -------------------------------------------------------------------------
    // Serialized Fields

    [Header("Visibility")]
    [SerializeField, Tooltip("When false (default), the indicator is off by default and SHOWS when conditions are met.\nWhen true, the indicator is on by default and HIDES when conditions are met.")]
    private bool showByDefault = false;

    [Header("Conditions")]
    [FormerlySerializedAs("showWhenFocused")]
    [SerializeField, Tooltip("Condition active when the interactable is focused or hovered by an interactor.")]
    private bool whenFocused = true;

    [FormerlySerializedAs("showWhenGazed")]
    [SerializeField, Tooltip("Condition active when the player is gazing at the interactable.")]
    private bool whenGazed = true;

    [FormerlySerializedAs("showWhenInProximity")]
    [SerializeField, Tooltip("Condition active when the player is within the proximity threshold.")]
    private bool whenInProximity = true;

    [FormerlySerializedAs("showWhenSelectedWithLRHand")]
    [SerializeField, Tooltip("Condition active when the interactable is selected by the specified hand.")]
    private HandSelectionMode whenSelected = HandSelectionMode.None;

    [Header("Hand Settings")]
    [SerializeField, Tooltip("The hand this indicator represents (Left or Right). If None, auto-detects from GameObject or mesh name.")]
    private InteractorHandedness indicatorHand = InteractorHandedness.None;

    [SerializeField, Tooltip("Hide indicator if this indicator's own hand is gripping the interactable (prevents ghost hand from clipping through real hand).")]
    private bool hideIfOwnHandGrabbing = true;

    [Header("Thresholds")]
    [SerializeField, Range(0.5f, 0.99f), Tooltip("Dot product threshold for gaze detection (0.75 ≈ 41° cone).")]
    private float gazeFacingThreshold = 0.75f;

    [SerializeField, Range(0f, 10f), Tooltip("Distance threshold in meters for proximity detection.")]
    private float proximityThreshold  = 1.5f;

    [Header("Timing")]
    [SerializeField, Range(0.05f, 2f), Tooltip("Fade transition duration in seconds.")]
    private float fadeDuration    = 0.25f;

    [SerializeField, Range(0f, 2f), Tooltip("Delay before re-showing after grab release (prevents hover from immediately re-triggering indicator).")]
    private float releaseCooldown = 0.5f;

    [Header("References")]
    [SerializeField] private SkinnedMeshRenderer meshRenderer;
    [SerializeField] private Material material;               // Matched by name to the renderer's runtime instance
    [SerializeField, Tooltip("Camera/head transform — falls back to Camera.main or the XR Rig (XROrigin).")]
    private Transform headTransform;
    [SerializeField] private XRBaseInteractable interactable;
    [SerializeField] private XRInteractableAffordanceStateProvider affordanceProvider;

    [Header("Material Shader Properties")]
    [SerializeField] private string mainColorPropertyName = "_Color";
    [SerializeField] private string edgeColorPropertyName = "_EdgeColor";

    // -------------------------------------------------------------------------
    // Public Properties

    public bool ShowByDefault
    {
        get => showByDefault;
        set => showByDefault = value;
    }

    public bool WhenFocused
    {
        get => whenFocused;
        set => whenFocused = value;
    }

    public bool WhenGazed
    {
        get => whenGazed;
        set => whenGazed = value;
    }

    public bool WhenInProximity
    {
        get => whenInProximity;
        set => whenInProximity = value;
    }

    public HandSelectionMode WhenSelected
    {
        get => whenSelected;
        set => whenSelected = value;
    }

    // Backward-compatible properties
    public bool ShowWhenFocused { get => whenFocused; set => whenFocused = value; }
    public bool ShowWhenGazed { get => whenGazed; set => whenGazed = value; }
    public bool ShowWhenInProximity { get => whenInProximity; set => whenInProximity = value; }
    public HandSelectionMode ShowWhenSelectedWithLRHand { get => whenSelected; set => whenSelected = value; }
    public HandSelectionMode ShowWhenSelectedWithHand { get => whenSelected; set => whenSelected = value; }

    public InteractorHandedness IndicatorHand
    {
        get => indicatorHand;
        set => indicatorHand = value;
    }

    public Transform HeadTransform
    {
        get => headTransform;
        set => headTransform = value;
    }

    public bool HideIfOwnHandGrabbing
    {
        get => hideIfOwnHandGrabbing;
        set => hideIfOwnHandGrabbing = value;
    }

    public float CurrentAlpha => m_Alpha;

    // -------------------------------------------------------------------------
    // Private State

    private readonly BindingsGroup m_Bindings = new BindingsGroup();
    private Material m_Mat;
    private int m_MainColorId = -1;
    private int m_EdgeColorId = -1;
    private byte m_AffordanceState = AffordanceStateShortcuts.idle;
    private Coroutine m_Fade;
    private float m_LastReleaseTime = float.MinValue;
    private float m_Alpha;
    private float m_TargetAlpha;

    private bool IsInteractableFocused =>
        (interactable != null && (interactable.isFocused || interactable.isHovered))
        || (affordanceProvider != null && (m_AffordanceState == AffordanceStateShortcuts.focused
                                        || m_AffordanceState == AffordanceStateShortcuts.hovered
                                        || m_AffordanceState == AffordanceStateShortcuts.hoveredPriority));

    private bool InCooldown => Time.time - m_LastReleaseTime < releaseCooldown;

    // -------------------------------------------------------------------------
    // Lifecycle

    private void Awake()
    {
        meshRenderer ??= GetComponent<SkinnedMeshRenderer>() ?? GetComponentInChildren<SkinnedMeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("HandIndicator: no SkinnedMeshRenderer found.", this);
            enabled = false;
            return;
        }

        SetupMaterial();
        ResolveHeadTransform();
        ResolveInteractable();
    }

    private void Start()
    {
        ResolveHeadTransform();
        EvaluateState(instant: true);
    }

    private void OnEnable()
    {
        m_Bindings.Clear();

        if (interactable == null)
            ResolveInteractable();

        if (interactable != null)
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        if (affordanceProvider != null)
        {
            affordanceProvider.ignoreFocusEvents = false;
            m_Bindings.AddBinding(affordanceProvider.currentAffordanceStateData.SubscribeAndUpdate(OnAffordanceStateChanged));
        }

        EvaluateState(instant: true);
    }

    private void OnDisable()
    {
        m_Bindings.Clear();

        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }

        StopFade();
    }

    private void Update()
    {
        ResolveHeadTransform();

        if (interactable == null)
            ResolveInteractable();

        EvaluateState(instant: false);
    }

    // -------------------------------------------------------------------------
    // State Evaluation

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        EvaluateState(instant: false);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        m_LastReleaseTime = Time.time;
        EvaluateState(instant: false);
    }

    private void OnAffordanceStateChanged(AffordanceStateData data)
    {
        m_AffordanceState = data.stateIndex;
        EvaluateState(instant: false);
    }

    public void EvaluateState(bool instant)
    {
        float target = CalculateTargetAlpha();

        if (instant || fadeDuration <= 0.001f)
        {
            StopFade();
            m_TargetAlpha = target;
            SetAlpha(target);
            return;
        }

        // Discrete target changed -> initiate smooth fade
        if (Mathf.Abs(target - m_TargetAlpha) > 0.01f)
        {
            StartFade(target);
        }
        else if (m_Fade == null && whenInProximity)
        {
            // When proximity distance changes continuously and no discrete fade is active
            SetAlpha(target);
        }
    }

    public float CalculateTargetAlpha()
    {
        CheckSelectionState(out bool isLeftSelecting, out bool isRightSelecting, out bool isAnySelecting);

        bool isOwnHandGrabbing = false;
        if (hideIfOwnHandGrabbing)
        {
            var ownHand = ResolveOwnHandedness();
            if (ownHand == InteractorHandedness.Left && isLeftSelecting)
                isOwnHandGrabbing = true;
            else if (ownHand == InteractorHandedness.Right && isRightSelecting)
                isOwnHandGrabbing = true;
        }

        bool selectionConditionMet = false;
        switch (whenSelected)
        {
            case HandSelectionMode.LeftHand:
                selectionConditionMet = isLeftSelecting;
                break;
            case HandSelectionMode.RightHand:
                selectionConditionMet = isRightSelecting;
                break;
            case HandSelectionMode.EitherHand:
                selectionConditionMet = isLeftSelecting || isRightSelecting;
                break;
            case HandSelectionMode.AnySelection:
                selectionConditionMet = isAnySelecting;
                break;
            case HandSelectionMode.None:
            default:
                selectionConditionMet = false;
                break;
        }

        bool inCooldown = InCooldown;
        bool focusConditionMet = whenFocused && IsInteractableFocused && !inCooldown;
        bool gazeConditionMet = whenGazed && IsGazing();
        float proximityFactor = whenInProximity ? ComputeProximityFactor() : 0f;

        if (!showByDefault)
        {
            // === OFF BY DEFAULT (SHOW WHEN CONDITIONS ARE MET) ===

            // 1. If own hand is gripping this affordance, suppress
            if (isOwnHandGrabbing)
                return 0f;

            // 2. If object is grabbed
            if (isAnySelecting)
            {
                // Shows only if a matching hand selection condition is met
                if (whenSelected != HandSelectionMode.None && selectionConditionMet)
                    return 1f;

                // Grabbed by non-matching hand or selection is None -> hidden
                return 0f;
            }

            // 3. During release cooldown, suppress hover/focus re-trigger
            if (inCooldown)
                return 0f;

            // 4. Discrete trigger conditions (Focus / Gaze)
            if (focusConditionMet || gazeConditionMet)
                return 1f;

            // 5. Proximity gradient
            if (whenInProximity && proximityFactor > 0f)
                return proximityFactor;

            // 6. Default off
            return 0f;
        }
        else
        {
            // === ON BY DEFAULT (HIDE WHEN CONDITIONS ARE MET) ===

            // 1. If own hand is gripping this affordance, hide
            if (isOwnHandGrabbing)
                return 0f;

            // 2. If object is grabbed
            if (isAnySelecting)
            {
                if (whenSelected != HandSelectionMode.None)
                {
                    if (selectionConditionMet)
                        return 0f;
                }
                else
                {
                    // By default when selection is None, any grab hides the affordance
                    return 0f;
                }
            }

            // 3. Discrete conditions that hide the indicator
            if (focusConditionMet || gazeConditionMet)
                return 0f;

            // 4. Proximity gradient: fades out as player approaches
            if (whenInProximity && proximityFactor > 0f)
                return 1f - proximityFactor;

            // 5. Default on
            return 1f;
        }
    }

    // -------------------------------------------------------------------------
    // Helper Checks

    public bool IsSelectedWithMatchingHand()
    {
        CheckSelectionState(out bool isLeft, out bool isRight, out bool isAny);

        if (hideIfOwnHandGrabbing)
        {
            var own = ResolveOwnHandedness();
            if (own == InteractorHandedness.Left && isLeft) return false;
            if (own == InteractorHandedness.Right && isRight) return false;
        }

        switch (whenSelected)
        {
            case HandSelectionMode.LeftHand: return isLeft;
            case HandSelectionMode.RightHand: return isRight;
            case HandSelectionMode.EitherHand: return isLeft || isRight;
            case HandSelectionMode.AnySelection: return isAny;
            default: return false;
        }
    }

    private void CheckSelectionState(out bool isLeftSelecting, out bool isRightSelecting, out bool isAnySelecting)
    {
        isLeftSelecting = false;
        isRightSelecting = false;
        isAnySelecting = false;

        if (interactable != null && interactable.isSelected)
        {
            isAnySelecting = true;
            var selectingList = interactable.interactorsSelecting;
            for (int i = 0; i < selectingList.Count; i++)
            {
                var h = ResolveHandedness(selectingList[i]);
                if (h == InteractorHandedness.Left)
                    isLeftSelecting = true;
                else if (h == InteractorHandedness.Right)
                    isRightSelecting = true;
            }
        }
        else if (affordanceProvider != null && (m_AffordanceState == AffordanceStateShortcuts.selected || m_AffordanceState == AffordanceStateShortcuts.activated))
        {
            isAnySelecting = true;
        }
    }

    private bool IsGazing()
    {
        if (headTransform == null)
            ResolveHeadTransform();

        if (headTransform == null)
            return false;

        Vector3 center = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
        Vector3 toTarget = center - headTransform.position;
        float dist = toTarget.magnitude;
        if (dist <= 0.01f)
            return false;

        return Vector3.Dot(headTransform.forward, toTarget / dist) >= gazeFacingThreshold;
    }

    private float ComputeProximityFactor()
    {
        if (proximityThreshold <= 0f)
            return 0f;

        if (headTransform == null)
            ResolveHeadTransform();

        if (headTransform == null)
            return 0f;

        Vector3 center = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
        float dist = Vector3.Distance(headTransform.position, center);
        return Mathf.Clamp01(1f - dist / proximityThreshold);
    }

    public InteractorHandedness ResolveOwnHandedness()
    {
        if (indicatorHand != InteractorHandedness.None)
            return indicatorHand;

        string objName = gameObject.name;
        if (objName.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return InteractorHandedness.Left;
        if (objName.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return InteractorHandedness.Right;

        if (meshRenderer != null)
        {
            string meshName = meshRenderer.name;
            if (meshName.IndexOf("_L_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                meshName.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractorHandedness.Left;

            if (meshName.IndexOf("_R_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                meshName.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractorHandedness.Right;
        }

        return InteractorHandedness.None;
    }

    public static InteractorHandedness ResolveHandedness(IXRInteractor interactor)
    {
        if (interactor == null)
            return InteractorHandedness.None;

        if (interactor.handedness != InteractorHandedness.None)
            return interactor.handedness;

        Transform t = interactor.transform;
        while (t != null)
        {
            string name = t.name;
            if (name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractorHandedness.Left;
            if (name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractorHandedness.Right;
            t = t.parent;
        }

        return InteractorHandedness.None;
    }

    // -------------------------------------------------------------------------
    // Resolvers

    private void ResolveInteractable()
    {
        if (interactable == null && affordanceProvider != null && affordanceProvider.interactableSource is XRBaseInteractable baseInteractable)
            interactable = baseInteractable;

        interactable ??= GetComponent<XRBaseInteractable>()
            ?? GetComponentInParent<XRBaseInteractable>()
            ?? GetComponentInChildren<XRBaseInteractable>()
            ?? (transform.parent != null ? transform.parent.GetComponentInChildren<XRBaseInteractable>() : null);

        affordanceProvider ??= GetComponent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInParent<XRInteractableAffordanceStateProvider>()
            ?? GetComponentInChildren<XRInteractableAffordanceStateProvider>()
            ?? (transform.parent != null ? transform.parent.GetComponentInChildren<XRInteractableAffordanceStateProvider>() : null);
    }

    private void ResolveHeadTransform()
    {
        if (headTransform != null)
            return;

        // 1. Primary: Camera.main
        if (Camera.main != null)
        {
            headTransform = Camera.main.transform;
            return;
        }

        // 2. Fallback: Find XR Rig in scene via XROrigin
        var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
        if (xrOrigin != null)
        {
            if (xrOrigin.Camera != null)
            {
                headTransform = xrOrigin.Camera.transform;
                return;
            }

            if (xrOrigin.CameraFloorOffsetObject != null)
            {
                var offsetCam = xrOrigin.CameraFloorOffsetObject.GetComponentInChildren<Camera>();
                if (offsetCam != null)
                {
                    headTransform = offsetCam.transform;
                    return;
                }
            }

            var originCam = xrOrigin.GetComponentInChildren<Camera>();
            if (originCam != null)
            {
                headTransform = originCam.transform;
                return;
            }
        }

        // 3. Fallback: Search for any TrackedPoseDriver
        var trackedPose = Object.FindAnyObjectByType<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (trackedPose != null)
        {
            headTransform = trackedPose.transform;
            return;
        }

        // 4. Fallback: Search by XR Rig GameObject name
        var rigGo = GameObject.Find("Studio XR Rig") ?? GameObject.Find("XR Rig") ?? GameObject.Find("XR Origin");
        if (rigGo != null)
        {
            var rigCam = rigGo.GetComponentInChildren<Camera>();
            if (rigCam != null)
            {
                headTransform = rigCam.transform;
                return;
            }
        }

        // 5. Fallback: Any active Camera in scene
        var anyCam = Object.FindAnyObjectByType<Camera>();
        if (anyCam != null)
        {
            headTransform = anyCam.transform;
        }
    }

    // -------------------------------------------------------------------------
    // Fade System

    /// <summary> Fade indicator to full visibility (1.0). Safe to call from UnityEvent or Invoke. </summary>
    public void QuickFadeIn()  => StartFade(1f);

    /// <summary> Fade indicator to default/ambient level. Safe to call from UnityEvent or Invoke. </summary>
    public void QuickFadeOut() => StartFade(showByDefault ? 1f : (whenInProximity ? ComputeProximityFactor() : 0f));

    public void StartFade(float target)
    {
        target = Mathf.Clamp01(target);
        m_TargetAlpha = target;

        if (fadeDuration <= 0.001f)
        {
            StopFade();
            SetAlpha(target);
            return;
        }

        StopFade();
        m_Fade = StartCoroutine(FadeRoutine(target));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float start = m_Alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            SetAlpha(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t)));
            yield return null;
        }
        SetAlpha(target);
        m_Fade = null;
    }

    private void StopFade()
    {
        if (m_Fade != null)
        {
            StopCoroutine(m_Fade);
            m_Fade = null;
        }
    }

    // -------------------------------------------------------------------------
    // Material & Alpha

    public void SetAlpha(float value)
    {
        m_Alpha = Mathf.Clamp01(value);
        ApplyMaterial();
    }

    /// <summary> Backward-compatible alias for SetAlpha. </summary>
    public void SetProximity(float value) => SetAlpha(value);

    private void ApplyMaterial()
    {
        if (m_Mat == null) return;
        if (m_MainColorId != -1) { var c = m_Mat.GetColor(m_MainColorId); c.a = m_Alpha; m_Mat.SetColor(m_MainColorId, c); }
        if (m_EdgeColorId != -1) { var c = m_Mat.GetColor(m_EdgeColorId); c.a = m_Alpha; m_Mat.SetColor(m_EdgeColorId, c); }
    }

    private void SetupMaterial()
    {
        var mats = meshRenderer.materials; // .materials returns runtime instances, safe to modify
        if (mats.Length == 0) return;

        // Prefer the material slot that matches the assigned template by name.
        if (material != null)
            foreach (var m in mats)
                if (m != null && m.name.StartsWith(material.name, System.StringComparison.OrdinalIgnoreCase))
                    { m_Mat = m; break; }

        // Otherwise, find the first slot that has either target color property.
        if (m_Mat == null)
            foreach (var m in mats)
                if (m != null && (FindPropId(m, mainColorPropertyName) != -1 || FindPropId(m, edgeColorPropertyName) != -1))
                    { m_Mat = m; break; }

        m_Mat ??= mats[^1]; // Last slot fallback (typically the hand visual material in multi-pass setups)

        m_MainColorId = FindPropId(m_Mat, mainColorPropertyName);
        m_EdgeColorId = FindPropId(m_Mat, edgeColorPropertyName);

        if (m_MainColorId == -1) Debug.LogWarning($"HandIndicator: '{mainColorPropertyName}' not found on '{m_Mat.name}'.", this);
        if (m_EdgeColorId == -1) Debug.LogWarning($"HandIndicator: '{edgeColorPropertyName}' not found on '{m_Mat.name}'.", this);
    }

    // Tries several name variants to bridge the gap between Shader Graph display names and reference names.
    private static int FindPropId(Material mat, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        string noSpaces = name.Replace(" ", "");
        foreach (var candidate in new[] { name, noSpaces, "_" + noSpaces, "_" + name })
            if (mat.HasProperty(candidate)) return Shader.PropertyToID(candidate);

        if (noSpaces.IndexOf("main",    System.StringComparison.OrdinalIgnoreCase) >= 0
        ||  noSpaces.IndexOf("color",   System.StringComparison.OrdinalIgnoreCase) >= 0)
            foreach (var f in new[] { "_Color", "_MainColor", "_BaseColor" })
                if (mat.HasProperty(f)) return Shader.PropertyToID(f);

        if (noSpaces.IndexOf("edge",    System.StringComparison.OrdinalIgnoreCase) >= 0
        ||  noSpaces.IndexOf("outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
            foreach (var f in new[] { "_EdgeColor", "_OutlineColor" })
                if (mat.HasProperty(f)) return Shader.PropertyToID(f);

        return -1;
    }
}
