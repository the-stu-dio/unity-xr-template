using UnityEngine; 

public class RainValve : MonoBehaviour
{
    [SerializeField] private ParticleSystem rainParticleSystem;
    [SerializeField] private Rigidbody rainValve;

    [SerializeField] private AudioSource valveSource;

    [SerializeField] private AudioSource rainAudioSource;

    [SerializeField] private AudioSource creakingAudioSource;

    [SerializeField] private AudioClip rainSound;
    [SerializeField] private AudioClip oceanSound;

    private bool clunkedUpper = false;
    private bool clunkedLower = false;

    public ClampGrabRotation clampGrabRotation;

    void Update()
    {
        UpdateRainIntensity();
    }

    public void RandomlyCreak()
    {
        // Randomly play creak at a 25% chance (executed on grab)
        if (Random.value < 0.25f)
        {
            creakingAudioSource.PlayOneShot(creakingAudioSource.clip);
        }
    }

    public void UpdateRainIntensity()
    {

        if (clampGrabRotation.totalRot >= clampGrabRotation.rotationBounds)
        {
            if (!clunkedUpper)
            {
                valveSource.PlayOneShot(valveSource.clip);
                clunkedUpper = true;
            }
        }

        if (clampGrabRotation.totalRot <= -clampGrabRotation.rotationBounds)
        {
            if (!clunkedLower)
            {
                valveSource.PlayOneShot(valveSource.clip);
                clunkedLower = true;

                rainAudioSource.clip = oceanSound;
                rainAudioSource.Play();
            }
        }
        
        if (clampGrabRotation.totalRot < clampGrabRotation.rotationBounds && clampGrabRotation.totalRot > -clampGrabRotation.rotationBounds)
        {
            if (clunkedUpper || clunkedLower)
            {
                clunkedUpper = false;
                clunkedLower = false;
            }

            if (rainAudioSource.clip != rainSound)
            {
                rainAudioSource.clip = rainSound;
                rainAudioSource.Play();
            }
        }
        
        if (!clunkedLower) {
            rainAudioSource.volume = Mathf.InverseLerp(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds, clampGrabRotation.totalRot);
        } else
        {
            rainAudioSource.volume = 0.1f;
        }

        var em = rainParticleSystem.emission;
        var splash = rainParticleSystem.subEmitters.GetSubEmitterSystem(0).emission;
        var ring = rainParticleSystem.subEmitters.GetSubEmitterSystem(1).emission;
        em.rateOverTime = Mathf.InverseLerp(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds, clampGrabRotation.totalRot) * 1000f;
        splash.rateOverTime = Mathf.InverseLerp(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds, clampGrabRotation.totalRot) * 1000f;
        ring.rateOverTime = Mathf.InverseLerp(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds, clampGrabRotation.totalRot) * 1000f;
        RenderSettings.fogDensity = (Mathf.InverseLerp(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds, clampGrabRotation.totalRot) * 0.05f) + 0.01f;
    
        // Randomly play creak at a 25% chance when the wheel is turned
        if (clampGrabRotation.totalRot == Random.Range(-clampGrabRotation.rotationBounds, clampGrabRotation.rotationBounds) && clampGrabRotation.velMagnitude > 0.1f)
        {
            RandomlyCreak();
        }

    }
}
