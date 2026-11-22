using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Lightweight plug-and-play script for objects that start anchored/planted (like a flower or stemmed prop)
/// and become standard dynamic physics objects once grabbed ("plucked").
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class XRPluckInteractable : MonoBehaviour
{
    [Header("Wobble Settings (Stretch Goal)")]
    [Tooltip("Allow the object to tilt/bend when pushed before being plucked, like a flower stem.")]
    [SerializeField] bool m_AllowWobble = true;

    [Tooltip("Maximum tilt angle in degrees from resting orientation before plucking.")]
    [SerializeField, Range(1f, 45f)] float m_MaxTiltAngle = 15f;

    [Tooltip("Strength of the spring pulling the stem back upright.")]
    [SerializeField] float m_SpringStiffness = 80f;

    [Tooltip("Damping to settle oscillation quickly.")]
    [SerializeField] float m_Damper = 8f;

    [Tooltip("Optional transform representing the stem base. If null, automatically uses the bottom of attached colliders or the object origin.")]
    [SerializeField] Transform m_StemPivot;

    [Header("Pluck Feedback (Optional)")]
    [Tooltip("Haptic vibration pulse strength (0 to 1) sent to the controller on pluck.")]
    [SerializeField, Range(0f, 1f)] float m_HapticIntensity = 0.6f;

    [Tooltip("Haptic duration in seconds.")]
    [SerializeField] float m_HapticDuration = 0.08f;

    [Tooltip("Audio clip played when plucked.")]
    [SerializeField] AudioClip m_PluckSound;

    [Tooltip("Event fired when the object is plucked.")]
    [SerializeField] UnityEvent m_OnPlucked;

    Rigidbody m_Rigidbody;
    XRGrabInteractable m_GrabInteractable;

    Quaternion m_InitialRotation;
    Vector3 m_InitialPosition;
    bool m_IsPlucked;

    public bool isPlucked => m_IsPlucked;
    public UnityEvent onPlucked => m_OnPlucked;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        m_GrabInteractable = GetComponent<XRGrabInteractable>();
    }

    void Start()
    {
        m_InitialPosition = transform.position;
        m_InitialRotation = transform.rotation;

        SetupPrePluckState();
    }

    void OnEnable()
    {
        if (m_GrabInteractable != null)
            m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        if (m_GrabInteractable != null)
            m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void SetupPrePluckState()
    {
        m_IsPlucked = false;

        // Keep kinematic false so physics collisions register and hands/controllers can push it
        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = false; // Prevents gravity droop while rooted

        if (m_AllowWobble)
        {
            // Lock position only; rotation is free to respond to pushes
            m_Rigidbody.constraints = RigidbodyConstraints.FreezePosition;

            // Align rotation center to the bottom stem pivot so it bends from the base
            ConfigureStemPivot();
        }
        else
        {
            // Completely frozen in place
            m_Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    void ConfigureStemPivot()
    {
        if (m_StemPivot != null)
        {
            m_Rigidbody.centerOfMass = transform.InverseTransformPoint(m_StemPivot.position);
            return;
        }

        // Auto-find bottom of colliders in local space
        var colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            float lowestY = float.MaxValue;
            Vector3 lowestPoint = transform.position;

            foreach (var col in colliders)
            {
                if (col.isTrigger) continue;
                var b = col.bounds;
                if (b.min.y < lowestY)
                {
                    lowestY = b.min.y;
                    lowestPoint = new Vector3(b.center.x, b.min.y, b.center.z);
                }
            }

            m_Rigidbody.centerOfMass = transform.InverseTransformPoint(lowestPoint);
        }
    }

    void FixedUpdate()
    {
        if (m_IsPlucked || !m_AllowWobble) return;

        // Keep position pinned to initial origin
        m_Rigidbody.position = m_InitialPosition;

        // Calculate angular displacement from resting orientation
        Quaternion deltaRot = m_InitialRotation * Quaternion.Inverse(m_Rigidbody.rotation);
        deltaRot.ToAngleAxis(out float angleDegrees, out Vector3 axis);

        if (angleDegrees > 180f)
            angleDegrees -= 360f;

        // Clamp to max tilt angle
        float currentTilt = Quaternion.Angle(m_InitialRotation, m_Rigidbody.rotation);
        if (currentTilt > m_MaxTiltAngle)
        {
            m_Rigidbody.rotation = Quaternion.RotateTowards(m_InitialRotation, m_Rigidbody.rotation, m_MaxTiltAngle);
            m_Rigidbody.angularVelocity *= 0.5f;
            return;
        }

        // Apply angular spring-damper torque back to upright resting position
        if (Mathf.Abs(angleDegrees) > 0.01f && axis.sqrMagnitude > 0.0001f)
        {
            Vector3 springTorque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * m_SpringStiffness);
            Vector3 dampingTorque = -m_Rigidbody.angularVelocity * m_Damper;
            m_Rigidbody.AddTorque(springTorque + dampingTorque, ForceMode.Acceleration);
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!m_IsPlucked)
        {
            Pluck(args.interactorObject);
        }
    }

    /// <summary>
    /// Plucks the object from the ground, restoring standard dynamic physics.
    /// </summary>
    public void Pluck(IXRInteractor interactor = null)
    {
        if (m_IsPlucked) return;

        m_IsPlucked = true;

        // Restore generic dynamic physics body
        m_Rigidbody.constraints = RigidbodyConstraints.None;
        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;
        m_Rigidbody.ResetCenterOfMass();

        // Optional haptic pulse on grabbing controller
        if (m_HapticIntensity > 0f && interactor != null)
        {
            SendHapticPulse(interactor, m_HapticIntensity, m_HapticDuration);
        }

        // Optional sound effect
        if (m_PluckSound != null)
        {
            AudioSource.PlayClipAtPoint(m_PluckSound, transform.position);
        }

        m_OnPlucked?.Invoke();

        // Disable this script to have zero runtime overhead once plucked
        enabled = false;
    }

    void SendHapticPulse(IXRInteractor interactor, float intensity, float duration)
    {
        if (interactor is XRBaseInputInteractor inputInteractor)
        {
            inputInteractor.SendHapticImpulse(intensity, duration);
        }
    }

    /// <summary>
    /// Resets the object back to its pre-plucked, anchored state.
    /// </summary>
    public void ResetPluck()
    {
        enabled = true;
        transform.position = m_InitialPosition;
        transform.rotation = m_InitialRotation;
        m_Rigidbody.linearVelocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        SetupPrePluckState();
    }
}
