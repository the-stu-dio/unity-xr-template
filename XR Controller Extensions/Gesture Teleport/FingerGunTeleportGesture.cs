using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

namespace StudioXR.Interaction
{
    [DisallowMultipleComponent]
    public class FingerGunTeleportGesture : MonoBehaviour
    {
        [Header("Hand Selection")]
        [SerializeField] private Handedness m_Handedness = Handedness.Right;

        [Header("Strict Finger Gun Thresholds")]
        [SerializeField, Range(0.70f, 0.98f), Tooltip("How straight the index finger must be (1.0 = laser straight). Default is 0.86.")]
        private float m_IndexStraightness = 0.86f;

        [SerializeField, Range(0.50f, 0.95f), Tooltip("How tightly curled other fingers must be (lower = tighter fist). Default is 0.80.")]
        private float m_CurlStrictness = 0.80f;

        [SerializeField, Tooltip("Require thumb to be cocked up (not already pressed) to acquire the aim pose.")]
        private bool m_RequireThumbCockedToAim = true;

        [SerializeField, Tooltip("Maximum distance from thumb tip to index knuckle to fire.")]
        private float m_ThumbFireDistance = 0.040f;

        [Header("Debug")]
        [SerializeField, Tooltip("Enable to print debug logs when gestures are recognized or lost.")]
        private bool m_DebugMode = true;

        [Header("Events")]
        public UnityEvent onTeleportFired;
        [Tooltip("Fired when the user forms a strict finger gun pose.")]
        public UnityEvent onAimAcquired;
        [Tooltip("Fired when the user breaks the finger gun pose.")]
        public UnityEvent onAimLost;

        private XRHandSubsystem m_HandSubsystem;
        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        private bool m_WasPressed;
        private bool m_WasAiming;

        private void OnEnable()
        {
            AcquireSubsystem();
        }

        private void OnDisable()
        {
            if (m_WasAiming)
            {
                m_WasAiming = false;
                onAimLost?.Invoke();
            }
            m_WasPressed = false;
            m_HandSubsystem = null;
        }

        private void Update()
        {
            if (m_HandSubsystem == null || !m_HandSubsystem.running)
            {
                AcquireSubsystem();
                if (m_HandSubsystem == null)
                    return;
            }

            XRHand hand = m_Handedness == Handedness.Left ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
            if (!hand.isTracked)
            {
                if (m_WasAiming)
                {
                    m_WasAiming = false;
                    onAimLost?.Invoke();
                }
                m_WasPressed = false;
                return;
            }

            try
            {
                EvaluateGesture(hand);
            }
            catch (System.ObjectDisposedException)
            {
                m_HandSubsystem = null;
            }
        }

        private void AcquireSubsystem()
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            if (s_Subsystems.Count > 0)
            {
                m_HandSubsystem = s_Subsystems[0];
            }
        }

        private void EvaluateGesture(XRHand hand)
        {
            bool indexValid = IsIndexStraightAndExtended(hand);
            bool middleCurled = IsFingerCurledStrict(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleProximal);
            bool ringCurled = IsFingerCurledStrict(hand, XRHandJointID.RingTip, XRHandJointID.RingIntermediate, XRHandJointID.RingProximal);
            bool littleCurled = IsFingerCurledStrict(hand, XRHandJointID.LittleTip, XRHandJointID.LittleIntermediate, XRHandJointID.LittleProximal);
            bool thumbCocked = !m_RequireThumbCockedToAim || IsThumbCocked(hand);

            // To enter aim, thumb must be cocked. Once aiming, thumb can move to fire without breaking aim pose.
            bool validAimPose = indexValid && middleCurled && ringCurled && littleCurled && (m_WasAiming || thumbCocked);

            if (validAimPose && !m_WasAiming)
            {
                if (m_DebugMode) Debug.Log($"[{m_Handedness} Gesture] Aim pose ACQUIRED! Teleport arc active.");
                m_WasAiming = true;
                onAimAcquired?.Invoke();
            }
            else if (!validAimPose && m_WasAiming)
            {
                if (m_DebugMode) Debug.Log($"[{m_Handedness} Gesture] Aim pose LOST.");
                m_WasAiming = false;
                onAimLost?.Invoke();
            }

            if (validAimPose)
            {
                bool isThumbPressed = IsThumbPressed(hand, out float currentThumbDist);

                if (isThumbPressed && !m_WasPressed)
                {
                    if (m_DebugMode) Debug.Log($"[{m_Handedness} Gesture] Thumb FIRED! (dist: {currentThumbDist:F3}m) Invoking teleport.");
                    onTeleportFired?.Invoke();
                    m_WasPressed = true;
                }
                else if (!isThumbPressed)
                {
                    m_WasPressed = false;
                }
            }
            else
            {
                m_WasPressed = false;
            }
        }

        private bool IsIndexStraightAndExtended(XRHand hand)
        {
            if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose) &&
                hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose proxPose) &&
                hand.GetJoint(XRHandJointID.IndexIntermediate).TryGetPose(out Pose intPose) &&
                hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tipPose))
            {
                // Tip must be extended further from wrist than intermediate joint
                float wristToTip = (tipPose.position - wristPose.position).sqrMagnitude;
                float wristToInt = (intPose.position - wristPose.position).sqrMagnitude;
                if (wristToTip <= wristToInt)
                    return false;

                // Segments must align in a straight line (low angle)
                Vector3 baseSegment = (intPose.position - proxPose.position).normalized;
                Vector3 tipSegment = (tipPose.position - intPose.position).normalized;
                float straightness = Vector3.Dot(baseSegment, tipSegment);

                return straightness >= m_IndexStraightness;
            }
            return false;
        }

        private bool IsFingerCurledStrict(XRHand hand, XRHandJointID tipJoint, XRHandJointID intJoint, XRHandJointID proxJoint)
        {
            if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose) &&
                hand.GetJoint(proxJoint).TryGetPose(out Pose proxPose) &&
                hand.GetJoint(tipJoint).TryGetPose(out Pose tipPose))
            {
                float wristToTip = (tipPose.position - wristPose.position).sqrMagnitude;
                float wristToProx = (proxPose.position - wristPose.position).sqrMagnitude;

                // Tip must be significantly closer to wrist than knuckle
                if (wristToTip > wristToProx * m_CurlStrictness)
                    return false;

                // Check curl bend angle if intermediate joint is tracked
                if (hand.GetJoint(intJoint).TryGetPose(out Pose intPose))
                {
                    Vector3 seg1 = (intPose.position - proxPose.position).normalized;
                    Vector3 seg2 = (tipPose.position - intPose.position).normalized;
                    if (Vector3.Dot(seg1, seg2) > 0.65f)
                        return false;
                }

                return true;
            }
            return false;
        }

        private bool IsThumbCocked(XRHand hand)
        {
            if (hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTipPose) &&
                hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexProxPose))
            {
                float dist = Vector3.Distance(thumbTipPose.position, indexProxPose.position);
                return dist > m_ThumbFireDistance * 1.25f;
            }
            return true;
        }

        private bool IsThumbPressed(XRHand hand, out float distance)
        {
            distance = float.MaxValue;
            if (hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTipPose) &&
                hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexProximalPose))
            {
                distance = Vector3.Distance(thumbTipPose.position, indexProximalPose.position);
                return distance < m_ThumbFireDistance;
            }
            return false;
        }

        /// <summary>
        /// Attempts to get the current world pose of the index fingertip, aligned with the whole finger barrel.
        /// </summary>
        public bool TryGetIndexTipPose(out Pose pose)
        {
            pose = default;
            if (m_HandSubsystem != null && m_HandSubsystem.running)
            {
                try
                {
                    XRHand hand = m_Handedness == Handedness.Left ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
                    if (hand.isTracked)
                    {
                        if (hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tipPose) &&
                            hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose proxPose))
                        {
                            // Knuckle to tip defines the entire straight barrel
                            Vector3 forward = (tipPose.position - proxPose.position).normalized;
                            if (forward.sqrMagnitude > 0.001f)
                            {
                                pose = new Pose(tipPose.position, Quaternion.LookRotation(forward));
                                return true;
                            }
                        }
                    }
                }
                catch (System.ObjectDisposedException)
                {
                    m_HandSubsystem = null;
                    return false;
                }
            }
            return false;
        }
    }
}
