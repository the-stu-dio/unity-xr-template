using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.Rendering.Universal;
using Unity.XR.CoreUtils;

/// <summary>
/// Controls the VR telescope functionality.
/// 
/// Interaction Flow:
/// 1. Holding the activate/trigger button on the grabbing controller smoothly extends the physical telescope segments.
/// 2. Bringing the extended telescope eyepiece up to the player's eye/headset engages Scope Mode:
///    - The 3D telescope model smoothly fades out so it does not clip through the player's face/eyes.
///    - The controller or hand display is disabled while looking through the scope so nothing blocks the viewport.
///    - The magnified circular spyglass viewport smoothly fades into view.
///    - The magnified direction is SOLELY dictated by the player's head gaze for stable, natural viewing.
/// 3. Pulling the head away from the eyepiece, or releasing the extension button at any time, exits Scope Mode:
///    - The viewport fades out.
///    - The physical telescope model and controller/hand display are restored.
///    - If the button was released, the segments collapse back into compact form.
/// </summary>
[DisallowMultipleComponent]
public class TelescopeView : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("The XRGrabInteractable component on this telescope. If not assigned, will automatically retrieve from this GameObject.")]
    [SerializeField] private XRGrabInteractable m_GrabInteractable;

    [Header("Telescope Extension")]
    [Tooltip("Whether the physical telescope tubes extend when activate button is held.")]
    [SerializeField] private bool m_AnimateExtension = true;

    [Tooltip("Local direction the segments extend along the telescope barrel. Maritime telescope cylinder axis is along X (Vector3.right).")]
    [SerializeField] private Vector3 m_ExtensionAxis = Vector3.right;

    [Tooltip("Invert extension direction (extend towards -X instead of +X).")]
    [SerializeField] private bool m_InvertExtension = false;

    [Tooltip("Distance each segment extends along the barrel axis in meters.")]
    [SerializeField] private float m_ExtensionDistance = 0.07f;

    [Tooltip("Speed of extending and collapsing animation.")]
    [SerializeField] private float m_ExtensionSpeed = 8f;

    [Tooltip("Segment transforms. If unassigned, automatically detected from child hierarchy.")]
    [SerializeField] private Transform m_ThirdSegment;
    [SerializeField] private Transform m_SecondSegment;
    [SerializeField] private Transform m_FirstSegment;

    [Header("Eyepiece & Head Proximity")]
    [Tooltip("Transform representing the eyepiece where the player looks in. If not assigned, automatically detected as first_segment.")]
    [SerializeField] private Transform m_EyepieceTransform;

    [Tooltip("Local offset from the eyepiece transform to the eye contact point in meters.")]
    [SerializeField] private Vector3 m_EyepieceLocalOffset = Vector3.zero;

    [Tooltip("Distance (in meters) between player's head/camera and eyepiece to enter scope view (default 0.18m).")]
    [SerializeField] private float m_EyeEnterDistance = 0.18f;

    [Tooltip("Distance (in meters) between player's head/camera and eyepiece to exit scope view (default 0.26m). Provides a comfortable hysteresis margin.")]
    [SerializeField] private float m_EyeExitDistance = 0.26f;

    [Tooltip("Minimum extension progress required before scope view can be entered (0 to 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float m_MinExtensionForScope = 0.5f;

    [Header("Model Fade")]
    [Tooltip("Whether to fade out and hide the telescope 3D model when the eye is at the eyepiece.")]
    [SerializeField] private bool m_FadeModelNearEye = true;

    [Tooltip("Speed of fading the physical telescope model out/in.")]
    [SerializeField] private float m_ModelFadeSpeed = 10f;

    [Header("Controller & Hand Display")]
    [Tooltip("Whether to disable the controller or hand display while looking through the telescope.")]
    [SerializeField] private bool m_HideControllerOrHand = true;

    [Tooltip("If true, hides both hands/controllers while looking through the telescope. If false, hides only the hand/controller holding the telescope.")]
    [SerializeField] private bool m_HideBothHands = true;

    [Header("Optical Zoom & Gaze Aim")]
    [Tooltip("When enabled, the magnified scope camera points solely in the direction of the player's head gaze for stable, natural viewing.")]
    [SerializeField] private bool m_GazeDictatedAim = true;

    [Tooltip("Forward offset of the gaze scope camera from the main camera (in meters) to prevent clipping player avatar/rig.")]
    [SerializeField] private float m_GazeCamForwardOffset = 0.08f;

    [Tooltip("Field of View for the magnified scope camera (lower = higher magnification). Standard is 12-16 degrees.")]
    [SerializeField] private float m_ZoomFOV = 15f;

    [Tooltip("Speed of fading the scope viewport in and out.")]
    [SerializeField] private float m_ZoomTransitionSpeed = 10f;

    [Tooltip("Resolution of the scope render texture.")]
    [SerializeField] private int m_RenderTextureResolution = 1024;

    [Tooltip("Base transform for telescope aiming when not using gaze aim (fallback).")]
    [SerializeField] private Transform m_AimTransform;

    [Tooltip("Local direction on the aim transform that points towards distant targets (fallback). Maritime telescope objective lens is along -X (Vector3.left).")]
    [SerializeField] private Vector3 m_AimAxis = Vector3.left;

    [Tooltip("Invert the aim direction 180 degrees if needed (fallback).")]
    [SerializeField] private bool m_InvertAimDirection = false;

    [Tooltip("Offset rotation applied to the scope camera aim (Euler angles in degrees).")]
    [SerializeField] private Vector3 m_AimOffsetRotation = Vector3.zero;

    [Header("Feedback")]
    [Tooltip("Enable haptic impulse when activating, deactivating, and entering scope.")]
    [SerializeField] private bool m_EnableHaptics = true;

    [Range(0f, 1f)]
    [SerializeField] private float m_HapticIntensity = 0.5f;

    [SerializeField] private float m_HapticDuration = 0.08f;

    [SerializeField] private AudioClip m_ExtendSound;
    [SerializeField] private AudioClip m_CollapseSound;
    [SerializeField] private AudioClip m_ScopeEnterSound;
    [SerializeField] private AudioClip m_ScopeExitSound;

    // Runtime state
    private bool m_IsHoldingButton = false;
    private bool m_IsEyeInScope = false;
    private float m_ExtensionProgress = 0f;
    private float m_ScopeAlpha = 0f;
    private float m_ModelAlpha = 1f;

    // Default segment positions
    private Vector3 m_ThirdSegDefaultPos;
    private Vector3 m_SecondSegDefaultPos;
    private Vector3 m_FirstSegDefaultPos;
    private bool m_HasThirdSeg = false;
    private bool m_HasSecondSeg = false;
    private bool m_HasFirstSeg = false;

    // Model Renderers & Material instances for clean runtime fading
    private Renderer[] m_ModelRenderers;
    private Material[][] m_InstantiatedMaterials;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // Hidden controller/hand visuals
    private readonly List<Renderer> m_HiddenRenderers = new List<Renderer>();
    private readonly List<GameObject> m_HiddenVisualObjects = new List<GameObject>();
    private bool m_HandsAreHidden = false;

    // Scope Camera & Render Texture
    private Camera m_ScopeCamera;
    private RenderTexture m_ScopeRT;
    private Transform m_ScopeCamTransform;

    // Eye Overlay
    private GameObject m_OverlayCanvasObj;
    private CanvasGroup m_OverlayCanvasGroup;
    private RawImage m_ScopeRawImage;

    // Audio & Interaction
    private AudioSource m_AudioSource;
    private IXRInteractor m_CurrentInteractor;

    // Public properties
    public bool IsHoldingButton => m_IsHoldingButton;
    public bool IsEyeInScope => m_IsEyeInScope;
    public float ZoomProgress => m_ScopeAlpha;
    public float ModelAlpha => m_ModelAlpha;
    public float ExtensionProgress => m_ExtensionProgress;

    private void Awake()
    {
        if (m_GrabInteractable == null)
            m_GrabInteractable = GetComponent<XRGrabInteractable>();

        m_AudioSource = GetComponent<AudioSource>();

        FindAndInitializeSegments();
        InitializeAimAndEyepiece();
        InitializeModelRenderers();
        InitializeScopeCamera();
    }

    private void OnEnable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.activated.AddListener(OnActivated);
            m_GrabInteractable.deactivated.AddListener(OnDeactivated);
            m_GrabInteractable.selectExited.AddListener(OnSelectExited);
        }
    }

    private void OnDisable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.activated.RemoveListener(OnActivated);
            m_GrabInteractable.deactivated.RemoveListener(OnDeactivated);
            m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
        }

        RestoreHandAndControllerDisplay();
        DeactivateTelescope();
    }

    private void Start()
    {
        InitializeScopeOverlay();
    }

    private void Update()
    {
        UpdateAnimationAndOptics();
    }

    private void OnDestroy()
    {
        RestoreHandAndControllerDisplay();

        if (m_ScopeRT != null)
        {
            if (m_ScopeCamera != null)
                m_ScopeCamera.targetTexture = null;

            m_ScopeRT.Release();
            Destroy(m_ScopeRT);
            m_ScopeRT = null;
        }

        if (m_OverlayCanvasObj != null)
        {
            Destroy(m_OverlayCanvasObj);
            m_OverlayCanvasObj = null;
        }
    }

    #region Public Activate / Deactivate Methods

    /// <summary>
    /// Invoked when controller activate button is pressed down (XRGrabInteractable.activated).
    /// </summary>
    public void OnActivated(ActivateEventArgs args)
    {
        if (args != null && args.interactorObject != null)
            m_CurrentInteractor = args.interactorObject;

        ActivateTelescope();
    }

    /// <summary>
    /// Parameterless overload for UnityEvent persistent calls.
    /// </summary>
    public void OnActivated()
    {
        ActivateTelescope();
    }

    /// <summary>
    /// Invoked when controller activate button is released (XRGrabInteractable.deactivated).
    /// </summary>
    public void OnDeactivated(DeactivateEventArgs args)
    {
        DeactivateTelescope();
    }

    /// <summary>
    /// Parameterless overload for UnityEvent persistent calls.
    /// </summary>
    public void OnDeactivated()
    {
        DeactivateTelescope();
    }

    /// <summary>
    /// Invoked if the telescope is dropped while grabbed.
    /// </summary>
    public void OnSelectExited(SelectExitEventArgs args)
    {
        m_CurrentInteractor = null;
        RestoreHandAndControllerDisplay();
        DeactivateTelescope();
    }

    public void ActivateTelescope()
    {
        if (m_IsHoldingButton) return;
        m_IsHoldingButton = true;

        PlaySound(m_ExtendSound);
        TriggerHaptics(m_CurrentInteractor, m_HapticIntensity, m_HapticDuration);
    }

    public void DeactivateTelescope()
    {
        if (!m_IsHoldingButton) return;
        m_IsHoldingButton = false;
        m_IsEyeInScope = false;

        RestoreHandAndControllerDisplay();

        PlaySound(m_CollapseSound);
        TriggerHaptics(m_CurrentInteractor, m_HapticIntensity * 0.7f, m_HapticDuration * 0.8f);
    }

    public void ToggleTelescope()
    {
        if (m_IsHoldingButton)
            DeactivateTelescope();
        else
            ActivateTelescope();
    }

    #endregion

    #region Physical Segments & Eyepiece Setup

    private void FindAndInitializeSegments()
    {
        if (m_ThirdSegment == null)
            m_ThirdSegment = FindSegmentRecursive(transform, "third_segment");
        if (m_SecondSegment == null)
            m_SecondSegment = FindSegmentRecursive(transform, "second_segment");
        if (m_FirstSegment == null)
            m_FirstSegment = FindSegmentRecursive(transform, "first_segment");

        if (m_ThirdSegment != null)
        {
            m_ThirdSegDefaultPos = m_ThirdSegment.localPosition;
            m_HasThirdSeg = true;
        }

        if (m_SecondSegment != null)
        {
            m_SecondSegDefaultPos = m_SecondSegment.localPosition;
            m_HasSecondSeg = true;
        }

        if (m_FirstSegment != null)
        {
            m_FirstSegDefaultPos = m_FirstSegment.localPosition;
            m_HasFirstSeg = true;
        }
    }

    private void InitializeAimAndEyepiece()
    {
        if (m_AimTransform == null)
        {
            Transform lod0 = FindSegmentRecursive(transform, "LOD0");
            m_AimTransform = lod0 != null ? lod0 : transform;
        }

        if (m_EyepieceTransform == null)
        {
            m_EyepieceTransform = m_FirstSegment != null ? m_FirstSegment : transform;
        }
    }

    private Transform FindSegmentRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return child;

            Transform found = FindSegmentRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Calculates the world position of the telescope eyepiece opening where the player looks in.
    /// </summary>
    public Vector3 GetEyepiecePosition()
    {
        if (m_EyepieceTransform != null)
        {
            Vector3 extAxis = m_ExtensionAxis.normalized;
            if (m_InvertExtension) extAxis = -extAxis;

            // In the maritime telescope model, the narrow eyepiece rim sits at the outer end along the extension axis (+X)
            Vector3 axialOffset = extAxis * 0.08f;
            return m_EyepieceTransform.TransformPoint(m_EyepieceLocalOffset + axialOffset);
        }

        Transform aimBase = m_AimTransform != null ? m_AimTransform : transform;
        return aimBase.position - GetAimDirection() * 0.15f;
    }

    public Vector3 GetAimDirection()
    {
        Transform aimBase = m_AimTransform != null ? m_AimTransform : transform;
        Vector3 localDir = m_AimAxis.normalized;
        if (m_InvertAimDirection) localDir = -localDir;

        Vector3 worldDir = aimBase.TransformDirection(localDir);
        if (m_AimOffsetRotation != Vector3.zero)
        {
            worldDir = Quaternion.Euler(m_AimOffsetRotation) * worldDir;
        }
        return worldDir.normalized;
    }

    public Vector3 GetFrontPosition()
    {
        Transform aimBase = m_AimTransform != null ? m_AimTransform : transform;
        return aimBase.position + GetAimDirection() * 0.18f;
    }

    #endregion

    #region Model Fading

    private void InitializeModelRenderers()
    {
        m_ModelRenderers = GetComponentsInChildren<Renderer>(true);
        m_InstantiatedMaterials = new Material[m_ModelRenderers.Length][];

        for (int i = 0; i < m_ModelRenderers.Length; i++)
        {
            Renderer r = m_ModelRenderers[i];
            if (r != null)
            {
                // Material instances allow per-telescope fading without mutating asset files on disk
                m_InstantiatedMaterials[i] = r.materials;
            }
        }
    }

    private void SetModelAlpha(float alpha)
    {
        if (!m_FadeModelNearEye || m_ModelRenderers == null) return;

        bool isFullyHidden = alpha <= 0.01f;
        bool isFullyOpaque = alpha >= 0.99f;

        for (int i = 0; i < m_ModelRenderers.Length; i++)
        {
            Renderer r = m_ModelRenderers[i];
            if (r == null) continue;

            if (isFullyHidden)
            {
                if (r.enabled) r.enabled = false;
                continue;
            }

            if (!r.enabled) r.enabled = true;

            Material[] mats = m_InstantiatedMaterials[i];
            if (mats == null) continue;

            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null) continue;

                if (isFullyOpaque)
                {
                    if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") != 0f)
                    {
                        mat.SetFloat("_Surface", 0f);
                        mat.SetOverrideTag("RenderType", "Opaque");
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.renderQueue = -1;
                    }

                    if (mat.HasProperty(BaseColorId))
                    {
                        Color c = mat.GetColor(BaseColorId);
                        c.a = 1f;
                        mat.SetColor(BaseColorId, c);
                    }
                    else if (mat.HasProperty(ColorId))
                    {
                        Color c = mat.GetColor(ColorId);
                        c.a = 1f;
                        mat.SetColor(ColorId, c);
                    }
                }
                else
                {
                    if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") != 1f)
                    {
                        mat.SetFloat("_Surface", 1f);
                        mat.SetFloat("_Blend", 0f);
                        mat.SetOverrideTag("RenderType", "Transparent");
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    }

                    if (mat.HasProperty(BaseColorId))
                    {
                        Color c = mat.GetColor(BaseColorId);
                        c.a = alpha;
                        mat.SetColor(BaseColorId, c);
                    }
                    else if (mat.HasProperty(ColorId))
                    {
                        Color c = mat.GetColor(ColorId);
                        c.a = alpha;
                        mat.SetColor(ColorId, c);
                    }
                }
            }
        }
    }

    #endregion

    #region Controller & Hand Display

    private void HideHandAndControllerDisplay()
    {
        if (!m_HideControllerOrHand || m_HandsAreHidden) return;

        HashSet<Renderer> foundRenderers = new HashSet<Renderer>();
        HashSet<GameObject> foundVisualObjects = new HashSet<GameObject>();

        if (m_HideBothHands)
        {
            Camera mainCam = Camera.main;
            if (mainCam == null)
                mainCam = FindAnyObjectByType<Camera>();

            Transform rigRoot = null;
            if (mainCam != null)
            {
                var origin = mainCam.GetComponentInParent<XROrigin>();
                if (origin != null)
                    rigRoot = origin.transform;
                else
                    rigRoot = mainCam.transform.root;
            }

            if (rigRoot != null)
            {
                CollectRigVisuals(rigRoot, foundVisualObjects, foundRenderers);
            }
        }
        else
        {
            IXRInteractor interactor = m_CurrentInteractor;
            if (interactor == null && m_GrabInteractable != null && m_GrabInteractable.interactorsSelecting.Count > 0)
            {
                interactor = m_GrabInteractable.interactorsSelecting[0];
            }

            if (interactor != null)
            {
                CollectInteractorVisuals(interactor.transform, foundVisualObjects, foundRenderers);
            }
        }

        m_HiddenVisualObjects.Clear();
        foreach (var obj in foundVisualObjects)
        {
            if (obj != null && obj.activeSelf)
            {
                m_HiddenVisualObjects.Add(obj);
                obj.SetActive(false);
            }
        }

        m_HiddenRenderers.Clear();
        foreach (var r in foundRenderers)
        {
            if (r != null && r.enabled)
            {
                m_HiddenRenderers.Add(r);
                r.enabled = false;
            }
        }

        m_HandsAreHidden = true;
    }

    private void EnforceHiddenDisplay()
    {
        for (int i = 0; i < m_HiddenVisualObjects.Count; i++)
        {
            if (m_HiddenVisualObjects[i] != null && m_HiddenVisualObjects[i].activeSelf)
            {
                m_HiddenVisualObjects[i].SetActive(false);
            }
        }

        for (int i = 0; i < m_HiddenRenderers.Count; i++)
        {
            if (m_HiddenRenderers[i] != null && m_HiddenRenderers[i].enabled)
            {
                m_HiddenRenderers[i].enabled = false;
            }
        }
    }

    private void RestoreHandAndControllerDisplay()
    {
        if (!m_HandsAreHidden) return;

        for (int i = 0; i < m_HiddenVisualObjects.Count; i++)
        {
            if (m_HiddenVisualObjects[i] != null)
            {
                m_HiddenVisualObjects[i].SetActive(true);
            }
        }
        m_HiddenVisualObjects.Clear();

        for (int i = 0; i < m_HiddenRenderers.Count; i++)
        {
            if (m_HiddenRenderers[i] != null)
            {
                m_HiddenRenderers[i].enabled = true;
            }
        }
        m_HiddenRenderers.Clear();

        m_HandsAreHidden = false;
    }

    private void CollectRigVisuals(Transform rigRoot, HashSet<GameObject> visualObjects, HashSet<Renderer> renderers)
    {
        Transform[] allTransforms = rigRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("controller visual") || n.Contains("handquestvisual") ||
                n.Contains("handandroidxrvisual") || n.Contains("hand visualizer") ||
                n.Contains("handvisual") || n.Contains("controller_base") ||
                n.Contains("universalcontroller"))
            {
                visualObjects.Add(t.gameObject);
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsTelescopeRenderer(r)) continue;
                    renderers.Add(r);
                }
            }
        }

        var skinned = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (IsTelescopeRenderer(smr)) continue;
            string n = smr.gameObject.name.ToLowerInvariant();
            Transform p = smr.transform.parent;
            string parentName = p != null ? p.name.ToLowerInvariant() : "";

            if (n.Contains("hand") || n.Contains("skin") || n.Contains("controller") ||
                parentName.Contains("hand") || parentName.Contains("controller"))
            {
                renderers.Add(smr);
            }
        }

        var lines = rigRoot.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lines)
        {
            if (IsTelescopeRenderer(lr)) continue;
            renderers.Add(lr);
        }
    }

    private void CollectInteractorVisuals(Transform interactorTransform, HashSet<GameObject> visualObjects, HashSet<Renderer> renderers)
    {
        Transform handOrControllerRoot = interactorTransform;
        Transform current = interactorTransform;

        while (current != null && current != current.root)
        {
            string n = current.name.ToLowerInvariant();
            if (n.Contains("controller") || n.Contains("hand"))
            {
                handOrControllerRoot = current;
            }

            if (current.GetComponent<XROrigin>() != null)
                break;

            current = current.parent;
        }

        if (handOrControllerRoot == null)
            handOrControllerRoot = interactorTransform;

        Transform[] children = handOrControllerRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in children)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("visual") || n.Contains("model") || n.Contains("mesh"))
            {
                visualObjects.Add(t.gameObject);
            }
        }

        foreach (var r in handOrControllerRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (IsTelescopeRenderer(r)) continue;
            renderers.Add(r);
        }
    }

    private bool IsTelescopeRenderer(Renderer r)
    {
        if (r == null) return false;
        return r.transform == transform || r.transform.IsChildOf(transform);
    }

    #endregion

    #region Scope Camera & Optics

    private void InitializeScopeCamera()
    {
        m_ScopeRT = new RenderTexture(m_RenderTextureResolution, m_RenderTextureResolution, 24, RenderTextureFormat.ARGB32)
        {
            name = "Telescope_RenderTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            autoGenerateMips = false
        };
        m_ScopeRT.Create();

        GameObject camObj = new GameObject("TelescopeScopeCamera");
        m_ScopeCamTransform = camObj.transform;

        m_ScopeCamera = camObj.AddComponent<Camera>();
        m_ScopeCamera.fieldOfView = m_ZoomFOV;
        m_ScopeCamera.nearClipPlane = 0.2f;
        m_ScopeCamera.farClipPlane = 3000f;
        m_ScopeCamera.clearFlags = CameraClearFlags.Skybox;
        m_ScopeCamera.targetTexture = m_ScopeRT;

        // Mask out the UI layer to prevent recursive rendering
        int uiLayer = LayerMask.NameToLayer("UI");
        int mask = Camera.main != null ? Camera.main.cullingMask : ~0;
        if (uiLayer >= 0)
        {
            mask &= ~(1 << uiLayer);
        }
        m_ScopeCamera.cullingMask = mask;

        var camData = camObj.AddComponent<UniversalAdditionalCameraData>();
        if (camData != null)
        {
            camData.renderShadows = true;
            camData.renderPostProcessing = true;
        }

        m_ScopeCamera.enabled = false;
    }

    #endregion

    #region Scope Overlay (Eye / Headset View)

    private void InitializeScopeOverlay()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
            mainCam = FindAnyObjectByType<Camera>();

        if (mainCam == null) return;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5;

        // WorldSpace Canvas attached to player's main camera at 0.5m distance (comfortable VR focus distance)
        m_OverlayCanvasObj = new GameObject("TelescopeScopeOverlay");
        m_OverlayCanvasObj.layer = uiLayer;
        m_OverlayCanvasObj.transform.SetParent(mainCam.transform, false);
        m_OverlayCanvasObj.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        m_OverlayCanvasObj.transform.localRotation = Quaternion.identity;
        m_OverlayCanvasObj.transform.localScale = Vector3.one * 0.0005f;

        Canvas canvas = m_OverlayCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 999;

        m_OverlayCanvasGroup = m_OverlayCanvasObj.AddComponent<CanvasGroup>();
        m_OverlayCanvasGroup.alpha = 0f;
        m_OverlayCanvasGroup.interactable = false;
        m_OverlayCanvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = m_OverlayCanvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3000f, 3000f);

        // 1. Dark vignette background to mask peripheral vision in VR
        GameObject darkBgObj = new GameObject("DarkVignetteSurround");
        darkBgObj.layer = uiLayer;
        darkBgObj.transform.SetParent(m_OverlayCanvasObj.transform, false);
        RectTransform bgRect = darkBgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        Image bgImage = darkBgObj.AddComponent<Image>();
        bgImage.color = new Color(0.02f, 0.02f, 0.02f, 1f);
        bgImage.raycastTarget = false;

        // 2. Circular Mask Container in center (punches a round hole in the overlay)
        GameObject maskObj = new GameObject("CircularScopeMask");
        maskObj.layer = uiLayer;
        maskObj.transform.SetParent(m_OverlayCanvasObj.transform, false);
        RectTransform maskRect = maskObj.AddComponent<RectTransform>();
        maskRect.sizeDelta = new Vector2(950f, 950f);
        maskRect.anchoredPosition = Vector2.zero;

        Image maskImage = maskObj.AddComponent<Image>();
        maskImage.sprite = CreateCircularMaskSprite(512);
        maskImage.raycastTarget = false;

        Mask uiMask = maskObj.AddComponent<Mask>();
        uiMask.showMaskGraphic = false;

        // 3. RawImage displaying the zoomed Scope Camera render texture (inside mask)
        GameObject rawImageObj = new GameObject("ScopeRenderView");
        rawImageObj.layer = uiLayer;
        rawImageObj.transform.SetParent(maskObj.transform, false);
        RectTransform rawRect = rawImageObj.AddComponent<RectTransform>();
        rawRect.sizeDelta = new Vector2(950f, 950f);
        rawRect.anchoredPosition = Vector2.zero;

        m_ScopeRawImage = rawImageObj.AddComponent<RawImage>();
        m_ScopeRawImage.texture = m_ScopeRT;
        m_ScopeRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        m_ScopeRawImage.raycastTarget = false;

        // 4. Antique Brass Eyepiece Rim on top of the circle
        GameObject rimObj = new GameObject("BrassEyepieceRim");
        rimObj.layer = uiLayer;
        rimObj.transform.SetParent(m_OverlayCanvasObj.transform, false);
        RectTransform rimRect = rimObj.AddComponent<RectTransform>();
        rimRect.sizeDelta = new Vector2(1000f, 1000f);
        rimRect.anchoredPosition = Vector2.zero;

        Image rimImage = rimObj.AddComponent<Image>();
        rimImage.sprite = CreateEyepieceRimSprite(512);
        rimImage.raycastTarget = false;

        m_OverlayCanvasObj.SetActive(false);
    }

    private Sprite CreateCircularMaskSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Procedural_CircleMask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                int index = y * size + x;

                if (dist <= radius - 1.5f)
                {
                    colors[index] = Color.white;
                }
                else if (dist <= radius)
                {
                    float alpha = Mathf.Clamp01(radius - dist);
                    colors[index] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[index] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private Sprite CreateEyepieceRimSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Procedural_EyepieceRim",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float innerRadius = size * 0.44f;
        float outerRadius = size * 0.49f;
        Color brassColor = new Color(0.40f, 0.30f, 0.12f, 1f);
        Color darkRim = new Color(0.12f, 0.08f, 0.03f, 1f);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                int index = y * size + x;

                if (dist < innerRadius - 4f)
                {
                    colors[index] = Color.clear;
                }
                else if (dist < innerRadius)
                {
                    float t = Mathf.InverseLerp(innerRadius - 4f, innerRadius, dist);
                    colors[index] = new Color(0.05f, 0.05f, 0.05f, t * 0.8f);
                }
                else if (dist <= outerRadius)
                {
                    float t = Mathf.InverseLerp(innerRadius, outerRadius, dist);
                    colors[index] = Color.Lerp(brassColor, darkRim, t);
                }
                else
                {
                    colors[index] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    #endregion

    #region Update Logic & Gaze Tracking

    private void UpdateAnimationAndOptics()
    {
        // 1. Animate physical telescope segments based on button hold
        float extTarget = m_IsHoldingButton ? 1f : 0f;
        if (m_AnimateExtension)
        {
            m_ExtensionProgress = Mathf.MoveTowards(m_ExtensionProgress, extTarget, Time.deltaTime * m_ExtensionSpeed);

            Vector3 axis = m_ExtensionAxis.normalized;
            if (m_InvertExtension) axis = -axis;

            Vector3 offset = axis * (m_ExtensionDistance * m_ExtensionProgress);

            if (m_HasThirdSeg)
                m_ThirdSegment.localPosition = m_ThirdSegDefaultPos + offset;
            if (m_HasSecondSeg)
                m_SecondSegment.localPosition = m_SecondSegDefaultPos + offset;
            if (m_HasFirstSeg)
                m_FirstSegment.localPosition = m_FirstSegDefaultPos + offset;
        }

        // 2. Evaluate Eyepiece Proximity to player's head
        Camera mainCam = Camera.main;
        if (mainCam == null)
            mainCam = FindAnyObjectByType<Camera>();

        bool canScope = m_IsHoldingButton && (m_ExtensionProgress >= m_MinExtensionForScope);

        if (canScope && mainCam != null)
        {
            Vector3 eyePos = GetEyepiecePosition();
            float headDistance = Vector3.Distance(mainCam.transform.position, eyePos);

            if (!m_IsEyeInScope)
            {
                // Move head up to eyepiece lens
                if (headDistance <= m_EyeEnterDistance)
                {
                    m_IsEyeInScope = true;
                    PlaySound(m_ScopeEnterSound);
                    TriggerHaptics(m_CurrentInteractor, m_HapticIntensity * 0.4f, 0.04f);
                }
            }
            else
            {
                // Pull head away from eyepiece lens
                if (headDistance > m_EyeExitDistance)
                {
                    m_IsEyeInScope = false;
                    PlaySound(m_ScopeExitSound);
                    TriggerHaptics(m_CurrentInteractor, m_HapticIntensity * 0.3f, 0.04f);
                }
            }
        }
        else
        {
            if (m_IsEyeInScope)
            {
                m_IsEyeInScope = false;
                PlaySound(m_ScopeExitSound);
            }
        }

        // 3. Smoothly fade the Scope Viewport in and out
        float targetScopeAlpha = m_IsEyeInScope ? 1f : 0f;
        m_ScopeAlpha = Mathf.MoveTowards(m_ScopeAlpha, targetScopeAlpha, Time.deltaTime * m_ZoomTransitionSpeed);
        bool shouldRenderScope = m_ScopeAlpha > 0.001f;

        // 4. Smoothly fade the physical 3D Model out and in (fades out when eye is near lens)
        float targetModelAlpha = m_IsEyeInScope ? 0f : 1f;
        m_ModelAlpha = Mathf.MoveTowards(m_ModelAlpha, targetModelAlpha, Time.deltaTime * m_ModelFadeSpeed);
        SetModelAlpha(m_ModelAlpha);

        // 4b. Controller & Hand Display Hiding
        if (m_IsEyeInScope)
        {
            if (!m_HandsAreHidden)
            {
                HideHandAndControllerDisplay();
            }
            else
            {
                EnforceHiddenDisplay();
            }
        }
        else
        {
            if (m_HandsAreHidden && m_ScopeAlpha <= 0.1f)
            {
                RestoreHandAndControllerDisplay();
            }
        }

        // 5. Update Scope Camera position & orientation
        if (m_ScopeCamera != null)
        {
            m_ScopeCamera.enabled = shouldRenderScope;
            if (shouldRenderScope && m_ScopeCamTransform != null)
            {
                if (m_GazeDictatedAim && mainCam != null)
                {
                    // Direction is SOLELY dictated by player's head gaze!
                    m_ScopeCamTransform.rotation = mainCam.transform.rotation;
                    m_ScopeCamTransform.position = mainCam.transform.position + mainCam.transform.forward * m_GazeCamForwardOffset;
                }
                else
                {
                    // Fallback to telescope barrel orientation
                    Vector3 aimDir = GetAimDirection();
                    Vector3 frontPos = GetFrontPosition();
                    m_ScopeCamTransform.position = frontPos;
                    m_ScopeCamTransform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
                }
            }
        }

        // 6. Update Eye Overlay Canvas visibility
        if (m_OverlayCanvasObj != null)
        {
            if (shouldRenderScope)
            {
                if (!m_OverlayCanvasObj.activeSelf)
                    m_OverlayCanvasObj.SetActive(true);

                if (m_OverlayCanvasGroup != null)
                    m_OverlayCanvasGroup.alpha = m_ScopeAlpha;
            }
            else if (m_OverlayCanvasObj.activeSelf)
            {
                m_OverlayCanvasObj.SetActive(false);
            }
        }
    }

    #endregion

    #region Utilities & Feedback

    private void TriggerHaptics(IXRInteractor interactor, float amplitude, float duration)
    {
        if (!m_EnableHaptics || interactor == null) return;

        if (interactor is XRBaseInputInteractor inputInteractor)
        {
            inputInteractor.SendHapticImpulse(amplitude, duration);
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;

        if (m_AudioSource != null)
        {
            m_AudioSource.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }

    #if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw Eyepiece position & proximity volumes
        Vector3 eyePos = GetEyepiecePosition();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(eyePos, 0.02f);

        // Enter volume (green)
        Gizmos.color = new Color(0f, 1f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(eyePos, m_EyeEnterDistance);

        // Exit volume (red)
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.3f);
        Gizmos.DrawWireSphere(eyePos, m_EyeExitDistance);

        // Draw Front barrel ray (cyan)
        Vector3 front = GetFrontPosition();
        Vector3 aim = GetAimDirection();
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(front, aim * 1.5f);
        Gizmos.DrawWireSphere(front, 0.02f);
    }

    [ContextMenu("Test Activate Button (Extend)")]
    private void ContextMenuActivate() => ActivateTelescope();

    [ContextMenu("Test Deactivate Button (Collapse)")]
    private void ContextMenuDeactivate() => DeactivateTelescope();

    [ContextMenu("Toggle Extended State")]
    private void ContextMenuToggle() => ToggleTelescope();

    [ContextMenu("Simulate Eye In Scope")]
    private void ContextMenuSimulateScope()
    {
        m_IsHoldingButton = true;
        m_ExtensionProgress = 1f;
        m_IsEyeInScope = true;
    }

    [ContextMenu("Invert Extension Direction")]
    private void ContextMenuInvertExt() => m_InvertExtension = !m_InvertExtension;

    [ContextMenu("Invert Aim Direction")]
    private void ContextMenuInvertAim() => m_InvertAimDirection = !m_InvertAimDirection;
    #endif

    #endregion
}
