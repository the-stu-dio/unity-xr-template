using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Utilities;

namespace StudioXR.Interaction
{
    /// <summary>
    /// Interactable component extending <see cref="XRGrabInteractable"/> that provides climbing locomotion
    /// using dynamic grab attach points. Compatible with controllers and hand tracking.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("Studio XR/Studio Climb Grab Interactable")]
    public class StudioClimbGrabInteractable : XRGrabInteractable, IGravityController
    {
        [Header("Climb Locomotion")]
        [SerializeField]
        [Tooltip("Controls whether selecting this interactable initiates climb locomotion instead of moving the object.")]
        bool m_Climbable = true;

        /// <summary>
        /// Controls whether selecting this interactable initiates climb locomotion instead of moving the object.
        /// </summary>
        public bool climbable
        {
            get => m_Climbable;
            set => m_Climbable = value;
        }

        [SerializeField]
        [Tooltip("Transform that defines the coordinate space for climb locomotion. Will use this GameObject's Transform by default.")]
        Transform m_ClimbTransform;

        /// <summary>
        /// Transform that defines the coordinate space for climb locomotion. Will use this GameObject's Transform by default.
        /// </summary>
        public Transform climbTransform
        {
            get
            {
                if (m_ClimbTransform == null)
                    m_ClimbTransform = transform;
                return m_ClimbTransform;
            }
            set => m_ClimbTransform = value;
        }

        [SerializeField]
        [Tooltip("The maximum distance from the collider surface an interactor can be to begin climb interaction.")]
        float m_MaxClimbInteractionDistance = 0.6f;

        /// <summary>
        /// The maximum distance from the collider surface an interactor can be to begin climb interaction.
        /// </summary>
        public float maxClimbInteractionDistance
        {
            get => m_MaxClimbInteractionDistance;
            set => m_MaxClimbInteractionDistance = value;
        }

        [SerializeField]
        [Tooltip("Optional override of climb locomotion settings (such as restricting movement along specific axes).")]
        ClimbSettingsDatumProperty m_ClimbSettingsOverride;

        /// <summary>
        /// Optional override of climb locomotion settings.
        /// </summary>
        public ClimbSettingsDatumProperty climbSettingsOverride
        {
            get => m_ClimbSettingsOverride;
            set => m_ClimbSettingsOverride = value;
        }

        struct ActiveClimbGrab
        {
            public StudioClimbGrabInteractable interactable;
            public IXRSelectInteractor interactor;
            public Vector3 anchorClimbSpace;
        }

        static readonly List<ActiveClimbGrab> s_ActiveClimbGrabs = new List<ActiveClimbGrab>();
        static readonly List<LocomotionProvider> s_ClimbEnabledProvidersToDisable = new List<LocomotionProvider>();
        static int s_LastClimbUpdateFrame = -1;

        readonly List<IXRSelectInteractor> m_ClimbInteractors = new List<IXRSelectInteractor>();
        readonly List<Vector3> m_ClimbAnchorClimbSpacePositions = new List<Vector3>();
        readonly XROriginMovement m_ClimbTransformation = new XROriginMovement();
        readonly List<Collider> m_IgnoredColliders = new List<Collider>();

        GravityProvider m_GravityProvider;
        ClimbProvider m_ClimbProvider;
        LocomotionMediator m_LocomotionMediator;
        XROrigin m_XROrigin;
        CharacterController m_SelectingCharacterController;

        /// <inheritdoc />
        public bool canProcess => isActiveAndEnabled;

        /// <inheritdoc />
        public bool gravityPaused { get; protected set; }

        /// <inheritdoc />
        static bool TryFindComponent<T>(out T component) where T : Object
        {
            component = Object.FindAnyObjectByType<T>();
            return component != null;
        }

        public bool TryLockGravity(GravityOverride gravityOverride)
        {
            if (m_GravityProvider == null)
                TryFindComponent(out m_GravityProvider);
            return m_GravityProvider != null && m_GravityProvider.TryLockGravity(this, gravityOverride);
        }

        /// <inheritdoc />
        public void RemoveGravityLock()
        {
            if (m_GravityProvider != null)
                m_GravityProvider.UnlockGravity(this);
        }

        void IGravityController.OnGravityLockChanged(GravityOverride gravityOverride)
        {
            if (gravityOverride == GravityOverride.ForcedOn)
                gravityPaused = false;
        }

        void IGravityController.OnGroundedChanged(bool isGrounded)
        {
            gravityPaused = false;
        }

        protected override void Awake()
        {
            base.Awake();

            if (m_Climbable)
            {
                useDynamicAttach = true;
                snapToColliderVolume = true;
                matchAttachPosition = true;
                matchAttachRotation = true;
                selectMode = InteractableSelectMode.Multiple;

                EnsureCharacterControllerAndCollision();

                if (m_GravityProvider == null)
                    TryFindComponent(out m_GravityProvider);
                if (m_ClimbProvider == null)
                    TryFindComponent(out m_ClimbProvider);
                if (m_LocomotionMediator == null)
                    TryFindComponent(out m_LocomotionMediator);
                if (m_XROrigin == null)
                    TryFindComponent(out m_XROrigin);

                // Ensure mesh colliders are convex so NearFarInteractor's SphereInteractionCaster (hands) can overlap them
                foreach (var col in colliders)
                {
                    if (col is MeshCollider mc && !mc.convex)
                        mc.convex = true;
                }
            }
        }

        protected override void OnDisable()
        {
            if (m_Climbable)
            {
                for (int i = s_ActiveClimbGrabs.Count - 1; i >= 0; i--)
                {
                    if (s_ActiveClimbGrabs[i].interactable == this)
                        s_ActiveClimbGrabs.RemoveAt(i);
                }

                if (s_ActiveClimbGrabs.Count > 0)
                {
                    var lastIdx = s_ActiveClimbGrabs.Count - 1;
                    var active = s_ActiveClimbGrabs[lastIdx];
                    if (active.interactable != null && active.interactor != null)
                    {
                        var currentPos = GetInteractorClimbPosition(active.interactor, active.interactable);
                        active.anchorClimbSpace = active.interactable.climbTransform.InverseTransformPoint(currentPos);
                        s_ActiveClimbGrabs[lastIdx] = active;
                    }
                }
                else
                {
                    FinishClimbLocomotion();
                }
            }

            base.OnDisable();
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            if (!base.IsHoverableBy(interactor))
                return false;

            if (m_Climbable)
            {
                if (IsHovered(interactor))
                    return true;

                var interactorPos = GetInteractorClimbPosition(interactor, this);
                if (XRInteractableUtility.TryGetClosestPointOnCollider(this, interactorPos, out var distanceInfo))
                {
                    if (distanceInfo.distanceSqr > m_MaxClimbInteractionDistance * m_MaxClimbInteractionDistance)
                        return false;
                }
            }

            return true;
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor))
                return false;

            if (m_Climbable)
            {
                if (IsSelected(interactor))
                    return true;

                var interactorPos = GetInteractorClimbPosition(interactor, this);
                if (XRInteractableUtility.TryGetClosestPointOnCollider(this, interactorPos, out var distanceInfo))
                {
                    if (distanceInfo.distanceSqr > m_MaxClimbInteractionDistance * m_MaxClimbInteractionDistance)
                        return false;
                }
            }

            return true;
        }

        protected override void InitializeDynamicAttachPose(IXRSelectInteractor interactor, Transform dynamicAttachTransform)
        {
            if (!m_Climbable)
            {
                base.InitializeDynamicAttachPose(interactor, dynamicAttachTransform);
                return;
            }

            var matchPosition = ShouldMatchAttachPosition(interactor);
            var matchRotation = ShouldMatchAttachRotation(interactor);
            if (!matchPosition && !matchRotation)
                return;

            var interactorAttachTransform = interactor.GetAttachTransform(this);
            var attachPose = interactorAttachTransform.GetWorldPose();

            var interactorPos = GetInteractorClimbPosition(interactor, this);
            if (interactorPos != Vector3.zero)
                attachPose.position = interactorPos;

            if (matchPosition && ShouldSnapToColliderVolume(interactor) &&
                XRInteractableUtility.TryGetClosestPointOnCollider(this, attachPose.position, out var distanceInfo))
            {
                attachPose.position = distanceInfo.point;
            }

            if (matchPosition && matchRotation)
                dynamicAttachTransform.SetWorldPose(attachPose);
            else if (matchPosition)
                dynamicAttachTransform.position = attachPose.position;
            else
                dynamicAttachTransform.rotation = attachPose.rotation;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            if (m_Climbable)
            {
                if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic && isSelected)
                {
                    StepClimbLocomotion();
                }
                return;
            }

            base.ProcessInteractable(updatePhase);
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            base.OnSelectEntering(args);

            if (m_Climbable)
                StartClimbGrabInternal(args.interactorObject);
        }

        protected override void OnSelectExiting(SelectExitEventArgs args)
        {
            base.OnSelectExiting(args);

            if (m_Climbable)
                FinishClimbGrabInternal(args.interactorObject);
        }

        protected override void Grab()
        {
            if (m_Climbable)
                return;
            base.Grab();
        }

        protected override void Drop()
        {
            if (m_Climbable)
                return;
            base.Drop();
        }

        protected override void Detach()
        {
            if (m_Climbable)
                return;
            base.Detach();
        }

        protected override void SetupRigidbodyGrab(Rigidbody rigidbody)
        {
            if (m_Climbable)
                return;
            base.SetupRigidbodyGrab(rigidbody);
        }

        protected override void SetupRigidbodyDrop(Rigidbody rigidbody)
        {
            if (m_Climbable)
                return;
            base.SetupRigidbodyDrop(rigidbody);
        }

        #region Climb Locomotion Logic

        static Vector3 GetInteractorClimbPosition(IXRInteractor interactor, IXRInteractable contextInteractable = null)
        {
            if (interactor == null)
                return Vector3.zero;

            if (interactor is NearFarInteractor nearFar)
            {
                if (nearFar.interactionAttachController != null && nearFar.interactionAttachController.transformToFollow != null)
                    return nearFar.interactionAttachController.transformToFollow.position;

                if (nearFar.nearInteractionCaster != null && nearFar.nearInteractionCaster.effectiveCastOrigin != null)
                    return nearFar.nearInteractionCaster.effectiveCastOrigin.position;

                if (nearFar.attachTransform != null && nearFar.attachTransform != nearFar.transform)
                    return nearFar.attachTransform.position;
            }

            if (contextInteractable != null)
            {
                var attach = interactor.GetAttachTransform(contextInteractable);
                if (attach != null && attach != interactor.transform)
                    return attach.position;
            }

            if (interactor is XRBaseInteractor baseInteractor && baseInteractor.attachTransform != null && baseInteractor.attachTransform != baseInteractor.transform)
            {
                return baseInteractor.attachTransform.position;
            }

            return interactor.transform.position;
        }

        void EnsureCharacterControllerAndCollision(IXRInteractor interactor = null)
        {
            if (m_XROrigin == null)
                TryFindComponent(out m_XROrigin);
            if (m_LocomotionMediator == null)
                TryFindComponent(out m_LocomotionMediator);

            CharacterController characterController = null;

            if (m_XROrigin != null)
            {
                var originGo = m_XROrigin.Origin != null ? m_XROrigin.Origin : m_XROrigin.gameObject;
                if (!originGo.TryGetComponent(out characterController))
                {
                    if (!m_XROrigin.TryGetComponent(out characterController))
                    {
                        characterController = originGo.AddComponent<CharacterController>();
                        characterController.radius = 0.25f;
                        characterController.height = 1.8f;
                        characterController.stepOffset = 0.3f;
                        characterController.skinWidth = 0.02f;
                        characterController.minMoveDistance = 0f;
                    }
                }
            }

            if (characterController == null && interactor != null)
            {
                characterController = interactor.transform.GetComponentInParent<CharacterController>();
            }

            if (characterController != null)
            {
                if (m_SelectingCharacterController == null)
                    m_SelectingCharacterController = characterController;

                StartIgnoringCharacterCollision(characterController);

                if (m_LocomotionMediator != null && m_LocomotionMediator.bodyTransformer != null)
                {
                    var bodyTransformer = m_LocomotionMediator.bodyTransformer;
                    if (bodyTransformer.constrainedBodyManipulator == null)
                    {
                        bodyTransformer.constrainedBodyManipulator = ScriptableObject.CreateInstance<CharacterControllerBodyManipulator>();
                    }
                }
            }
        }

        void StartIgnoringCharacterCollision(Collider characterCollider)
        {
            if (characterCollider == null) return;
            foreach (var c in colliders)
            {
                if (c != null && !Physics.GetIgnoreCollision(characterCollider, c))
                {
                    Physics.IgnoreCollision(characterCollider, c, true);
                    m_IgnoredColliders.Add(c);
                }
            }
        }

        void StopIgnoringCharacterCollision(Collider characterCollider)
        {
            if (characterCollider == null) return;
            foreach (var c in m_IgnoredColliders)
            {
                if (c != null)
                    Physics.IgnoreCollision(characterCollider, c, false);
            }
            m_IgnoredColliders.Clear();
        }

        void StartClimbGrabInternal(IXRSelectInteractor interactor)
        {
            EnsureCharacterControllerAndCollision(interactor);

            if (m_GravityProvider == null)
                TryFindComponent(out m_GravityProvider);
            if (m_ClimbProvider == null)
                TryFindComponent(out m_ClimbProvider);
            if (m_LocomotionMediator == null)
                TryFindComponent(out m_LocomotionMediator);
            if (m_XROrigin == null)
                TryFindComponent(out m_XROrigin);

            var climbTrans = climbTransform;
            var interactorPos = GetInteractorClimbPosition(interactor, this);
            var anchorClimbSpace = climbTrans.InverseTransformPoint(interactorPos);

            var existingIdx = m_ClimbInteractors.IndexOf(interactor);
            if (existingIdx >= 0)
            {
                m_ClimbAnchorClimbSpacePositions[existingIdx] = anchorClimbSpace;
            }
            else
            {
                m_ClimbInteractors.Add(interactor);
                m_ClimbAnchorClimbSpacePositions.Add(anchorClimbSpace);
            }

            bool wasAlreadyClimbingGlobally = s_ActiveClimbGrabs.Count > 0;

            for (int i = s_ActiveClimbGrabs.Count - 1; i >= 0; i--)
            {
                if (s_ActiveClimbGrabs[i].interactor == interactor)
                    s_ActiveClimbGrabs.RemoveAt(i);
            }

            s_ActiveClimbGrabs.Add(new ActiveClimbGrab
            {
                interactable = this,
                interactor = interactor,
                anchorClimbSpace = anchorClimbSpace
            });

            if (!wasAlreadyClimbingGlobally)
            {
                TryLockGravity(GravityOverride.ForcedOff);
                gravityPaused = true;

                s_ClimbEnabledProvidersToDisable.Clear();
                if (m_ClimbProvider != null && m_ClimbProvider.providersToDisable != null)
                {
                    foreach (var provider in m_ClimbProvider.providersToDisable)
                    {
                        if (provider != null && provider.enabled)
                        {
                            provider.enabled = false;
                            s_ClimbEnabledProvidersToDisable.Add(provider);
                        }
                    }
                }

                if (s_ClimbEnabledProvidersToDisable.Count == 0)
                {
                    var allProviders = Object.FindObjectsByType<LocomotionProvider>();
                    if (allProviders != null)
                    {
                        foreach (var provider in allProviders)
                        {
                            if (provider != null && provider != m_GravityProvider && provider.enabled)
                            {
                                var typeName = provider.GetType().Name;
                                if (typeName.Contains("Teleport") || typeName.Contains("Move") || typeName.Contains("Turn"))
                                {
                                    provider.enabled = false;
                                    s_ClimbEnabledProvidersToDisable.Add(provider);
                                }
                            }
                        }
                    }
                }
            }
        }

        void FinishClimbGrabInternal(IXRSelectInteractor interactor)
        {
            var index = m_ClimbInteractors.IndexOf(interactor);
            if (index >= 0)
            {
                m_ClimbInteractors.RemoveAt(index);
                m_ClimbAnchorClimbSpacePositions.RemoveAt(index);
            }

            for (int i = s_ActiveClimbGrabs.Count - 1; i >= 0; i--)
            {
                if (s_ActiveClimbGrabs[i].interactable == this && s_ActiveClimbGrabs[i].interactor == interactor)
                {
                    s_ActiveClimbGrabs.RemoveAt(i);
                    break;
                }
            }

            if (s_ActiveClimbGrabs.Count > 0)
            {
                var lastIdx = s_ActiveClimbGrabs.Count - 1;
                var active = s_ActiveClimbGrabs[lastIdx];
                if (active.interactable != null && active.interactor != null)
                {
                    var currentPos = GetInteractorClimbPosition(active.interactor, active.interactable);
                    active.anchorClimbSpace = active.interactable.climbTransform.InverseTransformPoint(currentPos);
                    s_ActiveClimbGrabs[lastIdx] = active;

                    if (active.interactable == this)
                    {
                        var localIdx = m_ClimbInteractors.IndexOf(active.interactor);
                        if (localIdx >= 0)
                            m_ClimbAnchorClimbSpacePositions[localIdx] = active.anchorClimbSpace;
                    }
                }
            }
            else
            {
                FinishClimbLocomotion();
            }
        }

        void FinishClimbLocomotion()
        {
            m_ClimbInteractors.Clear();
            m_ClimbAnchorClimbSpacePositions.Clear();

            if (s_ActiveClimbGrabs.Count == 0)
            {
                RemoveGravityLock();
                gravityPaused = false;

                if (m_SelectingCharacterController != null)
                {
                    StopIgnoringCharacterCollision(m_SelectingCharacterController);
                    m_SelectingCharacterController = null;
                }

                foreach (var provider in s_ClimbEnabledProvidersToDisable)
                {
                    if (provider != null)
                        provider.enabled = true;
                }
                s_ClimbEnabledProvidersToDisable.Clear();
            }
        }

        void StepClimbLocomotion()
        {
            if (s_ActiveClimbGrabs.Count == 0)
                return;

            var activeGrab = s_ActiveClimbGrabs[s_ActiveClimbGrabs.Count - 1];
            if (activeGrab.interactable != this)
                return;

            if (s_LastClimbUpdateFrame == Time.frameCount)
                return;
            s_LastClimbUpdateFrame = Time.frameCount;

            if (activeGrab.interactor == null || activeGrab.interactable == null)
            {
                s_ActiveClimbGrabs.RemoveAt(s_ActiveClimbGrabs.Count - 1);
                return;
            }

            var climbTrans = activeGrab.interactable.climbTransform;
            var anchorClimbSpace = activeGrab.anchorClimbSpace;
            var currentInteractorPos = GetInteractorClimbPosition(activeGrab.interactor, activeGrab.interactable);
            var currentInteractorClimbSpace = climbTrans.InverseTransformPoint(currentInteractorPos);

            bool allowFreeX = true;
            bool allowFreeY = true;
            bool allowFreeZ = true;

            var settingsOverride = activeGrab.interactable.m_ClimbSettingsOverride;
            if (settingsOverride != null && settingsOverride.Value != null)
            {
                allowFreeX = settingsOverride.Value.allowFreeXMovement;
                allowFreeY = settingsOverride.Value.allowFreeYMovement;
                allowFreeZ = settingsOverride.Value.allowFreeZMovement;
            }
            else if (m_ClimbProvider != null && m_ClimbProvider.climbSettings != null && m_ClimbProvider.climbSettings.Value != null)
            {
                allowFreeX = m_ClimbProvider.climbSettings.Value.allowFreeXMovement;
                allowFreeY = m_ClimbProvider.climbSettings.Value.allowFreeYMovement;
                allowFreeZ = m_ClimbProvider.climbSettings.Value.allowFreeZMovement;
            }

            Vector3 movementInClimbSpace = anchorClimbSpace - currentInteractorClimbSpace;
            if (!allowFreeX) movementInClimbSpace.x = 0f;
            if (!allowFreeY) movementInClimbSpace.y = 0f;
            if (!allowFreeZ) movementInClimbSpace.z = 0f;

            Vector3 movement = climbTrans.TransformVector(movementInClimbSpace);

            if (movement.sqrMagnitude > 0f)
            {
                EnsureCharacterControllerAndCollision(activeGrab.interactor);

                if (m_LocomotionMediator != null && m_LocomotionMediator.bodyTransformer != null)
                {
                    m_ClimbTransformation.motion = movement;
                    m_LocomotionMediator.bodyTransformer.QueueTransformation(m_ClimbTransformation);
                }
                else if (m_SelectingCharacterController != null && m_SelectingCharacterController.enabled)
                {
                    m_SelectingCharacterController.Move(movement);
                }
                else if (m_XROrigin != null && m_XROrigin.Origin != null)
                {
                    if (m_XROrigin.Origin.TryGetComponent<CharacterController>(out var cc) && cc.enabled)
                        cc.Move(movement);
                    else
                        m_XROrigin.Origin.transform.position += movement;
                }
            }
        }

        #endregion
    }
}
