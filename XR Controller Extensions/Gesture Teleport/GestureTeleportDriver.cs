using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Utilities;

namespace StudioXR.Interaction
{
    public class GestureTeleportDriver : MonoBehaviour
    {
        [Header("Gesture Detection")]
        [SerializeField, Tooltip("FingerGunTeleportGesture instance to listen to. If null, automatically resolves from the hand or scene.")]
        private FingerGunTeleportGesture m_Gesture;

        [Header("Teleport Dependencies")]
        [SerializeField, Tooltip("Drag either an XRRayInteractor or a NearFarInteractor here.")]
        private XRBaseInteractor m_RayInteractor;
        
        [SerializeField] private TeleportationProvider m_TeleportProvider;

        [Header("Ray Origin")]
        [SerializeField, Tooltip("Transform to use as the ray origin (e.g. Right Hand Stabilized Origin or Poke Interactor). If null or pointing to a pinch point, automatically binds to a child stabilized origin or poke interactor.")]
        private Transform m_RayOrigin;

        [Header("Ray Smoothing (Fallback)")]
        [SerializeField, Tooltip("Eliminates hand tracking jitter when not using XRTransformStabilizer.")]
        private bool m_SmoothRayOrigin = false;

        [SerializeField, Range(5f, 35f), Tooltip("Position smoothing speed. Higher is more responsive, lower filters more tremor.")]
        private float m_PositionSmoothingSpeed = 20f;

        [SerializeField, Range(5f, 35f), Tooltip("Angular smoothing speed. Eliminates twitchy ray direction wobble.")]
        private float m_RotationSmoothingSpeed = 16f;

        [Header("Behavior")]
        [SerializeField, Tooltip("If true, automatically enables the Ray Interactor GameObject when aiming, and disables it when not aiming.")]
        private bool m_ToggleInteractorObject = true;

        [Header("Debug")]
        [SerializeField] private bool m_DebugMode = true;

        private Transform m_DynamicPokeOrigin;
        private Transform m_SmoothedOrigin;
        private XRInteractorLineVisual m_LineVisual;
        private bool m_OriginInitialized;

        private void Awake()
        {
            if (m_Gesture == null)
            {
                m_Gesture = GetComponent<FingerGunTeleportGesture>();
                if (m_Gesture == null && m_RayInteractor != null)
                    m_Gesture = m_RayInteractor.GetComponentInParent<FingerGunTeleportGesture>();
                if (m_Gesture == null)
                    m_Gesture = FindAnyObjectByType<FingerGunTeleportGesture>();
            }

            if (m_Gesture != null)
            {
                m_Gesture.onTeleportFired.AddListener(ExecuteTeleport);
                m_Gesture.onAimAcquired.AddListener(OnAimAcquired);
                m_Gesture.onAimLost.AddListener(OnAimLost);
            }

            if (m_TeleportProvider == null)
            {
                m_TeleportProvider = GetComponentInParent<TeleportationProvider>() ?? FindAnyObjectByType<TeleportationProvider>();
            }

            SetupRayOrigin();

            if (m_ToggleInteractorObject && m_RayInteractor != null)
            {
                m_RayInteractor.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        private void SetupRayOrigin()
        {
            // If m_RayOrigin is unassigned or accidentally pointing to a pinch point, resolve the best origin
            if (m_RayOrigin == null || m_RayOrigin.name.IndexOf("pinch", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Transform handTransform = m_Gesture != null ? m_Gesture.transform : (m_RayInteractor != null ? m_RayInteractor.transform.parent : null);

                if (handTransform != null)
                {
                    // Priority 1: Child with XRTransformStabilizer (e.g. Right Hand Stabilized Origin)
                    var stabilizerChild = handTransform.GetComponentInChildren<XRTransformStabilizer>(true);
                    if (stabilizerChild != null)
                    {
                        m_RayOrigin = stabilizerChild.transform;
                    }
                    else
                    {
                        // Priority 2: Named "[Hand] Stabilized Origin"
                        var children = handTransform.GetComponentsInChildren<Transform>(true);
                        var exactStab = children.FirstOrDefault(t => t.name.IndexOf("Stabilized Origin", System.StringComparison.OrdinalIgnoreCase) >= 0);
                        if (exactStab != null)
                        {
                            m_RayOrigin = exactStab;
                        }
                        else
                        {
                            // Priority 3: Component XRPokeInteractor
                            var pokeComponent = handTransform.GetComponentInChildren<XRPokeInteractor>(true);
                            if (pokeComponent != null && pokeComponent.name.IndexOf("pinch", System.StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                m_RayOrigin = pokeComponent.transform;
                            }
                            else
                            {
                                // Priority 4: Named "Poke Interactor" or "Poke Point"
                                var exactPoke = children.FirstOrDefault(t => 
                                    (t.name.Equals("Poke Interactor", System.StringComparison.OrdinalIgnoreCase) ||
                                     t.name.Equals("Poke Point", System.StringComparison.OrdinalIgnoreCase)) &&
                                    t.name.IndexOf("pinch", System.StringComparison.OrdinalIgnoreCase) < 0);

                                if (exactPoke != null)
                                {
                                    m_RayOrigin = exactPoke;
                                }
                            }
                        }
                    }
                }

                if (m_RayOrigin == null)
                {
                    var dynObj = new GameObject("Dynamic Poke Origin");
                    dynObj.transform.SetParent(handTransform != null ? handTransform : transform, false);
                    m_DynamicPokeOrigin = dynObj.transform;
                    m_RayOrigin = m_DynamicPokeOrigin;
                }
            }

            // If XRTransformStabilizer is present on m_RayOrigin, native XRI handles stabilization
            bool hasStabilizer = m_RayOrigin != null && m_RayOrigin.GetComponent<XRTransformStabilizer>() != null;

            if (!hasStabilizer && m_SmoothRayOrigin && m_RayOrigin != null)
            {
                // Create a smoothed origin proxy so the physical ray arc never twitches
                var smoothObj = new GameObject("Smoothed Teleport Ray Origin");
                smoothObj.transform.SetParent(m_RayOrigin.parent != null ? m_RayOrigin.parent : transform, false);
                m_SmoothedOrigin = smoothObj.transform;
            }

            if (m_RayInteractor is XRRayInteractor ray)
            {
                var targetOrigin = (m_SmoothedOrigin != null && m_SmoothRayOrigin) ? m_SmoothedOrigin : m_RayOrigin;
                ray.rayOriginTransform = targetOrigin;
                ray.attachTransform = targetOrigin;

                if (ray.hitDetectionType == XRRayInteractor.HitDetectionType.SphereCast)
                {
                    ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
                }
                ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
                ray.hitClosestOnly = true;

                var sceneTeleportables = FindObjectsByType<BaseTeleportationInteractable>(FindObjectsInactive.Include);
                int extraLayers = 0;
                foreach (var t in sceneTeleportables)
                {
                    extraLayers |= t.interactionLayers.value;
                }
                if (extraLayers != 0)
                {
                    ray.interactionLayers |= extraLayers;
                }
            }

            if (m_RayInteractor != null)
            {
                m_LineVisual = m_RayInteractor.GetComponent<XRInteractorLineVisual>();
                if (m_LineVisual != null)
                {
                    m_LineVisual.stopLineAtFirstRaycastHit = true;
                }
            }
        }

        private void Update()
        {
            try
            {
                // Continuously update dynamic tip pose if following tracked hand joints
                if (m_DynamicPokeOrigin != null && m_Gesture != null)
                {
                    if (m_Gesture.TryGetIndexTipPose(out Pose tipPose))
                    {
                        m_DynamicPokeOrigin.position = tipPose.position;
                        m_DynamicPokeOrigin.rotation = tipPose.rotation;
                    }
                }

                // Apply frame-rate independent exponential smoothing to eliminate optical jitter
                if (m_SmoothedOrigin != null && m_RayOrigin != null)
                {
                    if (!m_OriginInitialized)
                    {
                        m_SmoothedOrigin.position = m_RayOrigin.position;
                        m_SmoothedOrigin.rotation = m_RayOrigin.rotation;
                        m_OriginInitialized = true;
                    }
                    else if (m_SmoothRayOrigin)
                    {
                        float posT = 1f - Mathf.Exp(-m_PositionSmoothingSpeed * Time.deltaTime);
                        float rotT = 1f - Mathf.Exp(-m_RotationSmoothingSpeed * Time.deltaTime);

                        m_SmoothedOrigin.position = Vector3.Lerp(m_SmoothedOrigin.position, m_RayOrigin.position, posT);
                        m_SmoothedOrigin.rotation = Quaternion.Slerp(m_SmoothedOrigin.rotation, m_RayOrigin.rotation, rotT);
                    }
                    else
                    {
                        m_SmoothedOrigin.position = m_RayOrigin.position;
                        m_SmoothedOrigin.rotation = m_RayOrigin.rotation;
                    }

                    // Keep the interactor GameObject and its attach transform synced with the smoothed index aim
                    if (m_RayInteractor != null)
                    {
                        m_RayInteractor.transform.position = m_SmoothedOrigin.position;
                        m_RayInteractor.transform.rotation = m_SmoothedOrigin.rotation;

                        if (m_RayInteractor.attachTransform != m_SmoothedOrigin && m_SmoothRayOrigin)
                        {
                            m_RayInteractor.attachTransform = m_SmoothedOrigin;
                        }
                    }
                }
            }
            catch (System.ObjectDisposedException)
            {
                // Subsystem deallocated on play mode exit
            }
        }

        private void OnDestroy()
        {
            if (m_Gesture != null)
            {
                m_Gesture.onTeleportFired.RemoveListener(ExecuteTeleport);
                m_Gesture.onAimAcquired.RemoveListener(OnAimAcquired);
                m_Gesture.onAimLost.RemoveListener(OnAimLost);
            }

            if (m_DynamicPokeOrigin != null)
            {
                Destroy(m_DynamicPokeOrigin.gameObject);
            }

            if (m_SmoothedOrigin != null)
            {
                Destroy(m_SmoothedOrigin.gameObject);
            }
        }

        private void OnAimAcquired()
        {
            m_OriginInitialized = false;
            if (m_ToggleInteractorObject && m_RayInteractor != null)
                m_RayInteractor.gameObject.SetActive(true);
        }

        private void OnAimLost()
        {
            if (m_ToggleInteractorObject && m_RayInteractor != null)
                m_RayInteractor.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            AlignReticleRotation();
        }

        [BeforeRenderOrder(XRInteractionUpdateOrder.k_BeforeRenderLineVisual + 1)]
        private void OnBeforeRender()
        {
            AlignReticleRotation();
        }

        private void AlignReticleRotation()
        {
            if (m_RayInteractor == null || !m_RayInteractor.gameObject.activeInHierarchy)
                return;

            if (m_LineVisual == null)
                m_LineVisual = m_RayInteractor.GetComponent<XRInteractorLineVisual>();

            if (m_LineVisual == null)
                return;

            GameObject activeReticle = null;
            if (m_LineVisual.reticle != null && m_LineVisual.reticle.activeInHierarchy)
                activeReticle = m_LineVisual.reticle;
            else if (m_LineVisual.blockedReticle != null && m_LineVisual.blockedReticle.activeInHierarchy)
                activeReticle = m_LineVisual.blockedReticle;

            if (activeReticle == null)
                return;

            Vector3 surfaceNormal = Vector3.up;
            if (m_RayInteractor is XRRayInteractor ray && ray.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                surfaceNormal = hit.normal;
            }
            else
            {
                surfaceNormal = activeReticle.transform.up;
            }

            Vector3 aimForward = m_SmoothRayOrigin && m_SmoothedOrigin != null 
                ? m_SmoothedOrigin.forward 
                : (m_RayOrigin != null ? m_RayOrigin.forward : transform.forward);

            Vector3 projectedForward = Vector3.ProjectOnPlane(aimForward, surfaceNormal).normalized;
            if (projectedForward.sqrMagnitude > 0.001f)
            {
                activeReticle.transform.rotation = Quaternion.LookRotation(projectedForward, surfaceNormal);
            }
        }

        public void ExecuteTeleport()
        {
            if (m_DebugMode) Debug.Log("[TeleportDriver] ExecuteTeleport called by gesture event.");

            if (m_RayInteractor == null || m_TeleportProvider == null)
            {
                if (m_DebugMode) Debug.LogWarning("[TeleportDriver] Aborting: Missing RayInteractor or TeleportProvider assignment.");
                return;
            }

            BaseTeleportationInteractable hoverTarget = m_RayInteractor.interactablesHovered.FirstOrDefault(i => i is BaseTeleportationInteractable) as BaseTeleportationInteractable;

            RaycastHit hit = default;
            bool hasHit = false;

            if (m_RayInteractor is XRRayInteractor ray)
            {
                hasHit = ray.TryGetCurrent3DRaycastHit(out hit);
            }

            if (hoverTarget == null && hasHit && hit.collider != null)
            {
                hoverTarget = hit.collider.GetComponentInParent<BaseTeleportationInteractable>();
            }

            if (hoverTarget == null)
            {
                if (m_DebugMode) Debug.LogWarning($"[TeleportDriver] Aborting: No valid BaseTeleportationInteractable currently hovered by {m_RayInteractor.name}. Make sure the ray curve hits the teleport area on the floor.");
                return;
            }

            if (m_RayInteractor is IXRSelectInteractor selectInteractor)
            {
                var interactionManager = m_RayInteractor.interactionManager;
                if (interactionManager != null && !interactionManager.IsSelectPossible(selectInteractor, hoverTarget))
                {
                    if (m_DebugMode) Debug.LogWarning($"[TeleportDriver] Aborting: Teleportation target '{hoverTarget.name}' is currently blocked or unselectable.");
                    return;
                }
            }

            if (!hoverTarget.IsSelectableBy(m_RayInteractor as IXRSelectInteractor))
            {
                if (m_DebugMode) Debug.LogWarning($"[TeleportDriver] Aborting: Teleportation target '{hoverTarget.name}' rejected selection via IsSelectableBy.");
                return;
            }

            if (hoverTarget.filterSelectionByHitNormal && hasHit)
            {
                float normalAngle = Vector3.Angle(hoverTarget.transform.up, hit.normal);
                if (normalAngle > hoverTarget.upNormalToleranceDegrees)
                {
                    if (m_DebugMode) Debug.LogWarning($"[TeleportDriver] Aborting: Surface normal angle ({normalAngle:F1}°) exceeds tolerance ({hoverTarget.upNormalToleranceDegrees}°).");
                    return;
                }
            }

            if (TryGetHit(out Vector3 point, out Transform targetTransform))
            {
                if (m_DebugMode) Debug.Log($"[TeleportDriver] SUCCESS! Queueing teleport to point: {point}");

                // Determine directional facing orientation matching as-is teleport provider
                Vector3 forwardDirection = Vector3.forward;
                Vector3 aimForward = m_SmoothRayOrigin && m_SmoothedOrigin != null
                    ? m_SmoothedOrigin.forward
                    : (m_RayOrigin != null ? m_RayOrigin.forward : (m_RayInteractor != null ? m_RayInteractor.transform.forward : transform.forward));

                forwardDirection = Vector3.ProjectOnPlane(aimForward, Vector3.up).normalized;

                if (forwardDirection.sqrMagnitude < 0.001f)
                {
                    forwardDirection = Vector3.ProjectOnPlane(point - transform.position, Vector3.up).normalized;
                }

                Quaternion targetRotation = Quaternion.LookRotation(forwardDirection, Vector3.up);
                MatchOrientation matchMode = hoverTarget.matchOrientation;

                // Match as-is XRI Teleport provider: if target supports directional input, rotate player to face aiming direction
                if (hoverTarget.matchDirectionalInput)
                {
                    matchMode = MatchOrientation.TargetUpAndForward;
                }
                else if (hoverTarget is TeleportationAnchor anchor && anchor.teleportAnchorTransform != null)
                {
                    targetRotation = anchor.teleportAnchorTransform.rotation;
                }

                TeleportRequest request = new TeleportRequest
                {
                    destinationPosition = point,
                    destinationRotation = targetRotation,
                    matchOrientation = matchMode
                };
                
                m_TeleportProvider.QueueTeleportRequest(request);
            }
            else
            {
                if (m_DebugMode) Debug.LogWarning("[TeleportDriver] Aborting: Target found, but failed to extract ray endpoint from the interactor.");
            }
        }

        private bool TryGetHit(out Vector3 point, out Transform targetTransform)
        {
            if (m_RayInteractor is XRRayInteractor ray)
            {
                if (ray.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                {
                    point = hit.point;
                    targetTransform = hit.transform;
                    return true;
                }
            }
            else if (m_RayInteractor is IXRRayProvider rayProvider)
            {
                targetTransform = rayProvider.rayEndTransform;
                if (targetTransform != null)
                {
                    point = rayProvider.rayEndPoint;
                    return true;
                }
            }

            point = default;
            targetTransform = null;
            return false;
        }
    }
}
