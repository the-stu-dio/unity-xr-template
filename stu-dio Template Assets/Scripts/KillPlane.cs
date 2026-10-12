using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class KillPlane : MonoBehaviour
{
    [SerializeField]
    private GameObject player;

    [SerializeField]
    private Transform respawnLocation;

    [SerializeField]
    private float killYOffset = 0f;

    [SerializeField]
    private float fadeDuration = 1f;

    [SerializeField]
    private AudioClip deathSound;

    [SerializeField]
    private AudioSource audioSource;
    private Camera mainCamera;
    private bool isKilling;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (player != null && transform != null)
        {
            if (!isKilling && player.transform.position.y < transform.position.y + killYOffset)
            {
                KillPlayer();
            }
        }
    }

    private void KillPlayer()
    {
        if (!isKilling)
        {
            StartCoroutine(KillRoutine());
        }
    }

    private IEnumerator KillRoutine()
    {
        isKilling = true;
        KillEffect();

        // Hide player renderers and canvases to prevent them from clipping through the fade quad
        var rendererStates = new Dictionary<Renderer, bool>();
        foreach (var r in player.GetComponentsInChildren<Renderer>())
        {
            rendererStates[r] = r.enabled;
            r.enabled = false;
        }

        var canvasStates = new Dictionary<Canvas, bool>();
        foreach (var c in player.GetComponentsInChildren<Canvas>())
        {
            canvasStates[c] = c.enabled;
            c.enabled = false;
        }

        // Create a temporary canvas for the fade effect
        GameObject fadeObj = new GameObject("KillPlaneFader");
        fadeObj.transform.SetParent(mainCamera.transform, false);
        // Position it slightly in front of the camera
        fadeObj.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        fadeObj.transform.localRotation = Quaternion.identity;

        Canvas canvas = fadeObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 999;

        RectTransform rt = fadeObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(10f, 10f); // Large enough to cover the viewport

        Image fadeImage = fadeObj.AddComponent<Image>();
        fadeImage.color = new Color(0.04f, 0.07f, 0.17f, 0f);

        // Fade Out (to near-black)
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeImage.color = new Color(0.04f, 0.07f, 0.17f, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        fadeImage.color = new Color(0.04f, 0.07f, 0.17f, 1f);
        
        // Respawn player
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        
        player.transform.position = respawnLocation.position;
        
        if (cc != null) cc.enabled = true;

        // Fade In (from near-black)
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeImage.color = new Color(0.04f, 0.07f, 0.17f, 1f - Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        // Restore visibility
        foreach (var kvp in rendererStates)
        {
            if (kvp.Key != null) kvp.Key.enabled = kvp.Value;
        }
        foreach (var kvp in canvasStates)
        {
            if (kvp.Key != null) kvp.Key.enabled = kvp.Value;
        }

        Destroy(fadeObj);
        isKilling = false;
    }

    private void KillEffect()
    {
        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }
    }
}
