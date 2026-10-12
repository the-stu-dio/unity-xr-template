using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioXR.HandPose
{
    /// <summary>
    /// Represents a chain of skeletal bones forming a single finger or thumb.
    /// </summary>
    [Serializable]
    public class FingerChain
    {
        public string fingerName = "Finger";
        public List<Transform> bones = new List<Transform>();
        public Transform tip;
        public bool isThumb;
        public Vector3 localCurlAxis = Vector3.right;
        public bool useAutoCurlAxis = true;

        [SerializeField]
        private List<Quaternion> m_CachedDefaultRotations = new List<Quaternion>();

        public FingerChain() { }

        public FingerChain(string name, bool thumb = false)
        {
            fingerName = name;
            isThumb = thumb;
        }

        /// <summary>
        /// Caches the current local rotations of all bones in this chain as the default/rest pose.
        /// </summary>
        public void CacheDefaultRotations()
        {
            m_CachedDefaultRotations.Clear();
            if (bones == null) return;

            for (int i = 0; i < bones.Count; i++)
            {
                m_CachedDefaultRotations.Add(bones[i] != null ? bones[i].localRotation : Quaternion.identity);
            }
        }

        /// <summary>
        /// Restores all bones in this chain to their cached default rotations.
        /// </summary>
        public void RestoreDefaultRotations()
        {
            if (bones == null || m_CachedDefaultRotations.Count == 0) return;

            int count = Mathf.Min(bones.Count, m_CachedDefaultRotations.Count);
            for (int i = 0; i < count; i++)
            {
                if (bones[i] != null)
                {
                    bones[i].localRotation = m_CachedDefaultRotations[i];
                }
            }
        }
    }
}
