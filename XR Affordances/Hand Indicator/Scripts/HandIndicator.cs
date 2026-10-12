#pragma warning disable CS0618 // AffordanceSystem is deprecated in XRI 3.x — intentional fallback support.

using System.Collections;
using UnityEngine;
using Unity.XR.CoreUtils.Bindings;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;

public class HandIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SkinnedMeshRenderer meshRenderer;
    [SerializeField] private Material material;               // Matched by name to the renderer's runtime instance
    [SerializeField] private Transform headTransform;         // Camera/head transform — falls back to Camera.main
    [SerializeField] private XRBaseInteractable interactable;
    [SerializeField] private XRInteractableAffordanceStateProvider affordanceProvider;

    [Header("Settings")]
    [SerializeField, Range(0.05f, 2f)] private float fadeDuration    = 0.25f;
    [SerializeField, Range(0f, 2f)]    private float releaseCooldown = 0.5f;   // Delay before re-showing after grab release
    [SerializeField] private string mainColorPropertyName = "_Color";
    [SerializeField] private string edgeColorPropertyName = "_EdgeColor";

    [Header("Conditional Fade")]
    [SerializeField] private bool showWhenFocused     = true; // Appear when the interactable is focused or targeted by an interactor
    [SerializeField] private bool showWhenGazed       = true; // Appear when the player's view is directed at the object
    [SerializeField] private bool showWhenInProximity = true; // Fade in gradually as the player approaches
    [Space(6)]
    [SerializeField, Range(0.5f, 0.99f)] private float gazeFacingThreshold = 0.75f; // Dot product threshold — 0.75 ≈ 41° cone
    [SerializeField, Range(0f, 10f)]     private float proximityThreshold  = 1.5f;

    // --- Private state ---
    private readonly BindingsGroup m_Bindings = new BindingsGroup();
    private Material m_Mat;
    private int m_MainColorId = -1;
    private int m_EdgeColorId = -1;
    private byte m_AffordanceState = AffordanceStateShortcuts.idle;
    private Coroutine m_Fade;
    private float m_LastReleaseTime = float.MinValue;
    private float m_Alpha;
    private bool m_HighPriorityActive;
    private bool m_WasGrabbed;

    private bool IsGrabbed => (interactable != null && interactable.isSelected)
                           || (affordanceProvider != null && (m_AffordanceState == AffordanceStateShortcuts.selected
                                                           || m_AffordanceState == AffordanceStateShortcuts.activated));

    private bool IsInteractableFocused => (interactable != null && (interactable.isFocused || interactable.isHovered))
                                       || (affordanceProvider != null && (m_AffordanceState == AffordanceStateShortcuts.focused
                                                                       || m_AffordanceState == AffordanceStateShortcuts.hovered
                                                                       || m_AffordanceState == AffordanceStateShortcuts.hoveredPriority));

    private bool InCooldown => Time.time - m_LastReleaseTime < releaseCooldown;

    // -------------------------------------------------------------------------

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

        headTransform ??= Camera.main?.transform;

        ResolveInteractable();
    }

    private void Start()
    {
        headTransform ??= Camera.main?.transform;
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
        m_HighPriorityActive = false;
        m_WasGrabbed = false;
    }

    private void Update()
    {
        headTransform ??= Camera.main?.transform;

        if (interactable == null)
            ResolveInteractable();

        bool grabbed = IsGrabbed;
        if (grabbed)
        {
            m_WasGrabbed = true;
            m_HighPriorityActive = false;
            if (m_Alpha > 0f) { StopFade(); SetProximity(0f); }
            return;
        }

        if (m_WasGrabbed)
        {
            m_WasGrabbed = false;
            m_LastReleaseTime = Time.time;
            m_HighPriorityActive = false;
            if (m_Alpha > 0f) { StopFade(); SetProximity(0f); }
            return;
        }

        if (InCooldown)
        {
            m_HighPriorityActive = false;
            if (m_Alpha > 0f) { StopFade(); SetProximity(0f); }
            return;
        }

        EvaluateState(instant: false);
    }

    private void EvaluateState(bool instant)
    {
        bool focusMet = showWhenFocused && IsInteractableFocused;

        bool gazeMet = false;
        if (showWhenGazed && headTransform != null)
        {
            Vector3 center   = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
            Vector3 toTarget = center - headTransform.position;
            float   dist     = toTarget.magnitude;
            gazeMet = dist > 0.01f && Vector3.Dot(headTransform.forward, toTarget / dist) >= gazeFacingThreshold;
        }

        float proximityAlpha = 0f;
        if (showWhenInProximity && headTransform != null && proximityThreshold > 0f)
        {
            Vector3 center = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
            float   dist   = Vector3.Distance(headTransform.position, center);
            proximityAlpha = Mathf.Clamp01(1f - dist / proximityThreshold);
        }

        bool highPriorityNow = focusMet || gazeMet;

        if (instant)
        {
            m_HighPriorityActive = highPriorityNow;
            StopFade();
            float target = m_HighPriorityActive ? 1f : (showWhenInProximity ? proximityAlpha : 0f);
            SetProximity(target);
            return;
        }

        if (highPriorityNow != m_HighPriorityActive)
        {
            m_HighPriorityActive = highPriorityNow;
            float target = m_HighPriorityActive ? 1f : (showWhenInProximity ? proximityAlpha : 0f);
            StartFade(target);
        }
        else if (!m_HighPriorityActive && m_Fade == null)
        {
            SetProximity(showWhenInProximity ? proximityAlpha : 0f);
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        m_WasGrabbed = true;
        m_HighPriorityActive = false;
        StopFade();
        SetProximity(0f);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        m_WasGrabbed = false;
        m_LastReleaseTime = Time.time;
        m_HighPriorityActive = false;
        StopFade();
        SetProximity(0f);
    }

    private void OnAffordanceStateChanged(AffordanceStateData data)
    {
        m_AffordanceState = data.stateIndex;
    }

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

    // -------------------------------------------------------------------------
    // Fade

    /// <summary> Fade indicator to full visibility. Safe to call from UnityEvent or Invoke. </summary>
    public void QuickFadeIn()  => StartFade(1f);

    /// <summary> Fade indicator to ambient proximity level (or 0 if proximity disabled). Safe to call from UnityEvent or Invoke. </summary>
    public void QuickFadeOut() => StartFade(showWhenInProximity ? ComputeProximityAlpha() : 0f);

    public void StartFade(float target)
    {
        StopFade();
        m_Fade = StartCoroutine(FadeRoutine(target));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float start   = m_Alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            SetProximity(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / fadeDuration)));
            yield return null;
        }
        SetProximity(target);
        m_Fade = null;
    }

    private void StopFade()
    {
        if (m_Fade != null) { StopCoroutine(m_Fade); m_Fade = null; }
    }

    // -------------------------------------------------------------------------
    // Alpha / Material

    private float ComputeProximityAlpha()
    {
        if (headTransform == null || proximityThreshold <= 0f) return 0f;
        Vector3 center = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
        return Mathf.Clamp01(1f - Vector3.Distance(headTransform.position, center) / proximityThreshold);
    }

    public void SetProximity(float value)
    {
        m_Alpha = Mathf.Clamp01(value);
        ApplyMaterial();
    }

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