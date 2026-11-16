using UnityEngine;

public class ExperienceManager : MonoBehaviour
{
    public Transform[] objectives;
    public GameObject[] anchors;

    public GhostFootsteps f;

    public int curDestination = 0;

    public void ToggleRockAnchors()
    {
        foreach (GameObject o in anchors)
        {
            o.SetActive(!o.activeSelf);
        }
    } 

    public void SetFootstepsDestination(Transform t)
    {
        if (f != null)
        {
            f.SetDestination(t);
        }
    }

    public void NextDestination()
    {
        if (objectives == null || objectives.Length == 0) return;
        curDestination++;
        if (curDestination < objectives.Length)
        {
            SetFootstepsDestination(objectives[curDestination]);
        }
    }
}
