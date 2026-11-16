using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controls the VR telescope functionality.
/// When the activate button is held on the grabbing controller:
/// 1. The physical telescope segments smoothly extend outward along the barrel axis.
/// 2. The optical zoom camera activates and renders a magnified view along the telescope's aim line.
/// 3. A circular spyglass aperture overlay smoothly fades into the player's view with zero feedback/recursion.
/// 4. Haptic feedback is delivered to the holding controller.
/// Releasing the button smoothly collapses the telescope and returns to normal view.
/// </summary>
[DisallowMultipleComponent]
public class TelescopeView : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("The XRGrabInteractable component on this telescope. If not assigned, will automatically retrieve from this GameObject.")]
    [SerializeField] private XRGrabInteractable m_GrabInteractable;

    [Header("Telescope Extension")]
    [Tooltip("Whether the physical telescope tubes extend when activated.")]
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

    [Header("Optical Zoom & Aim")]
    [Tooltip("Field of View for the magnified scope camera (lower = higher magnification). Standard is 12-18 degrees.")]
    [SerializeField] private float m_ZoomFOV = 15f;

    [Tooltip("Speed of fading the scope view in and out.")]
    [SerializeField] private float m_ZoomTransitionSpeed = 8f;

    [Tooltip("Resolution of the scope render texture.")]
    [SerializeField] private int m_RenderTextureResolution = 1024;

    [Tooltip("Base transform for telescope aiming. Defaults to LOD0 or this root transform.")]
    [SerializeField] private Transform m_AimTransform;

    [Tooltip("Local direction on the aim transform that points towards distant targets. Maritime telescope objective lens is along -X (Vector3.left).")]
    [SerializeField] private Vector3 m_AimAxis = Vector3.left;

    [Tooltip("Invert the aim direction 180 degrees if needed.")]
    [SerializeField] private bool m_InvertAimDirection = false;

    [Tooltip("Offset rotation applied to the scope camera aim (Euler angles in degrees).")]
    [SerializeField] private Vector3 m_AimOffsetRotation = Vector3.zero;

    [Header("Feedback")]
    [Tooltip("Enable haptic impulse when activating and deactivating.")]
    [SerializeField] private bool m_EnableHaptics = true;

    [Range(0f, 1f)]
    [SerializeField] private float m_HapticIntensity = 0.5f;

    [SerializeField] private float m_HapticDuration = 0.08f;

    [SerializeField] private AudioClip m_ExtendSound;
    [SerializeField] private AudioClip m_CollapseSound;

    // Runtime state
    private bool m_IsHoldingButton = false;
    private float m_ExtensionProgress = 0f;
    private float m_ScopeAlpha = 0f;

    // Default segment positions
    private Vector3 m_ThirdSegDefaultPos;
    private Vector3 m_SecondSegDefaultPos;
    private Vector3 m_FirstSegDefaultPos;
    private bool m_HasThirdSeg = false;
    private bool m_HasSecondSeg = false;
    private bool m_HasFirstSeg = false;

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

    public bool IsActive => m_IsHoldingButton;
    public float ZoomProgress => m_ScopeAlpha;
    public float ExtensionProgress => m_ExtensionProgress;

    private void Awake()
    {
        if (m_GrabInteractable == null)
            m_GrabInteractable = GetComponent<XRGrabInteractable>();

        m_AudioSource = GetComponent<AudioSource>();

        FindAndInitializeSegments();
        InitializeAimTransform();
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

    #region Physical Segments

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

    #endregion

    #region Scope Camera & Optics

    private void InitializeAimTransform()
    {
        if (m_AimTransform == null)
        {
            Transform lod0 = FindSegmentRecursive(transform, "LOD0");
            m_AimTransform = lod0 != null ? lod0 : transform;
        }
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
        // Offset forward along aim direction so camera is outside the front objective lens
        return aimBase.position + GetAimDirection() * 0.18f;
    }

    private void InitializeScopeCamera()
    {
        // Create scope RenderTexture with clamp mode so edges never repeat
        m_ScopeRT = new RenderTexture(m_RenderTextureResolution, m_RenderTextureResolution, 24, RenderTextureFormat.ARGB32)
        {
            name = "Telescope_RenderTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            autoGenerateMips = false
        };
        m_ScopeRT.Create();

        // Create dedicated Scope Camera (unparented to avoid hierarchy scale issues)
        GameObject camObj = new GameObject("TelescopeScopeCamera");
        m_ScopeCamTransform = camObj.transform;

        m_ScopeCamera = camObj.AddComponent<Camera>();
        m_ScopeCamera.fieldOfView = m_ZoomFOV;
        m_ScopeCamera.nearClipPlane = 0.2f;
        m_ScopeCamera.farClipPlane = 3000f;
        m_ScopeCamera.clearFlags = CameraClearFlags.Skybox;
        m_ScopeCamera.targetTexture = m_ScopeRT;

        // CRITICAL: Culling Mask configuration to prevent recursive feedback loops!
        // Never render the UI layer (where the scope overlay canvas lives)
        int uiLayer = LayerMask.NameToLayer("UI");
        int mask = Camera.main != null ? Camera.main.cullingMask : ~0;
        if (uiLayer >= 0)
        {
            mask &= ~(1 << uiLayer);
        }
        m_ScopeCamera.cullingMask = mask;

        // Configure Universal Additional Camera Data for URP
        var camData = camObj.AddComponent<UniversalAdditionalCameraData>();
        if (camData != null)
        {
            camData.renderShadows = true;
            camData.renderPostProcessing = true;
        }

        // Disabled initially until telescope is activated
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

        // Create WorldSpace Canvas childed to main camera at 1.0m distance (comfortable optical focus in VR)
        m_OverlayCanvasObj = new GameObject("TelescopeScopeOverlay");
        m_OverlayCanvasObj.layer = uiLayer;
        m_OverlayCanvasObj.transform.SetParent(mainCam.transform, false);
        m_OverlayCanvasObj.transform.localPosition = new Vector3(0f, 0f, 1.0f);
        m_OverlayCanvasObj.transform.localRotation = Quaternion.identity;
        m_OverlayCanvasObj.transform.localScale = Vector3.one * 0.001f;

        Canvas canvas = m_OverlayCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 999;

        m_OverlayCanvasGroup = m_OverlayCanvasObj.AddComponent<CanvasGroup>();
        m_OverlayCanvasGroup.alpha = 0f;
        m_OverlayCanvasGroup.interactable = false;
        m_OverlayCanvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = m_OverlayCanvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3000f, 3000f);

        // 1. Full-screen black background to mask peripheral vision comfortably in VR
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

        // 4. Antique Brass Eyepiece Rim on top of the circle for authentic maritime aesthetic
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

    /// <summary>
    /// Generates a smooth, anti-aliased circular mask sprite.
    /// </summary>
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

    /// <summary>
    /// Generates an antique brass bezel ring with inner shadow around the aperture.
    /// </summary>
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
        Color brassColor = new Color(0.40f, 0.30f, 0.12f, 1f); // Antique brass
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
                    // Soft inner shadow vignette
                    float t = Mathf.InverseLerp(innerRadius - 4f, innerRadius, dist);
                    colors[index] = new Color(0.05f, 0.05f, 0.05f, t * 0.8f);
                }
                else if (dist <= outerRadius)
                {
                    // Brass ring gradient
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

    #region Update Logic

    private void UpdateAnimationAndOptics()
    {
        float target = m_IsHoldingButton ? 1f : 0f;

        // 1. Animate physical telescope segments along the cylinder axis
        if (m_AnimateExtension)
        {
            m_ExtensionProgress = Mathf.MoveTowards(m_ExtensionProgress, target, Time.deltaTime * m_ExtensionSpeed);

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

        // 2. Animate scope view alpha
        m_ScopeAlpha = Mathf.MoveTowards(m_ScopeAlpha, target, Time.deltaTime * m_ZoomTransitionSpeed);
        bool shouldRender = m_ScopeAlpha > 0.001f;

        // 3. Update Scope Camera position & aim
        if (m_ScopeCamera != null)
        {
            m_ScopeCamera.enabled = shouldRender;
            if (shouldRender && m_ScopeCamTransform != null)
            {
                Vector3 aimDir = GetAimDirection();
                Vector3 frontPos = GetFrontPosition();

                m_ScopeCamTransform.position = frontPos;
                m_ScopeCamTransform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            }
        }

        // 4. Update Eye Overlay Canvas
        if (m_OverlayCanvasObj != null)
        {
            if (shouldRender)
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
        Vector3 front = GetFrontPosition();
        Vector3 aim = GetAimDirection();

        Gizmos.color = Color.green;
        Gizmos.DrawRay(front, aim * 2.0f);
        Gizmos.DrawWireSphere(front, 0.02f);

        Transform aimBase = m_AimTransform != null ? m_AimTransform : transform;
        Vector3 extAxis = aimBase.TransformDirection(m_InvertExtension ? -m_ExtensionAxis.normalized : m_ExtensionAxis.normalized);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(aimBase.position, extAxis * 0.2f);
    }

    [ContextMenu("Test Activate Telescope")]
    private void ContextMenuActivate() => ActivateTelescope();

    [ContextMenu("Test Deactivate Telescope")]
    private void ContextMenuDeactivate() => DeactivateTelescope();

    [ContextMenu("Toggle Extended State")]
    private void ContextMenuToggle() => ToggleTelescope();

    [ContextMenu("Invert Extension Direction")]
    private void ContextMenuInvertExt() => m_InvertExtension = !m_InvertExtension;

    [ContextMenu("Invert Aim Direction")]
    private void ContextMenuInvertAim() => m_InvertAimDirection = !m_InvertAimDirection;
    #endif

    #endregion
}
