using UnityEngine;


public class ClampGrabRotation : MonoBehaviour
{
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private Rigidbody rb;
    public float totalRot = 0f;
    public float rotationBounds = 10f;
    public float velMagnitude = 0f;

    void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate(){
        totalRot += rb.angularVelocity.z * Time.fixedDeltaTime;
        totalRot = Mathf.Clamp(totalRot, -rotationBounds, rotationBounds);

        if (totalRot >= rotationBounds || totalRot <= -rotationBounds)
        {
            rb.angularVelocity = Vector3.zero;
        }

        velMagnitude = rb.angularVelocity.magnitude;
    }
}