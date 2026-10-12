using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioXR.HandPose
{
    /// <summary>
    /// Configuration settings for the iterative raycast/spherecast hand poser.
    /// </summary>
    [Serializable]
    public class HandPoseConfig
    {
        [Tooltip("Radius of the spherecast/contact checks representing finger thickness in meters (default 8mm).")]
        [Range(0.001f, 0.03f)]
        public float fingerRadius = 0.008f;

        [Tooltip("Angle step in degrees for each rotation increment.")]
        [Range(0.2f, 5f)]
        public float stepAngle = 1.5f;

        [Tooltip("Maximum curl angle allowed per bone in degrees.")]
        [Range(20f, 130f)]
        public float maxCurlAngle = 90f;

        [Tooltip("Stop margin / contact offset in meters.")]
        [Range(0f, 0.01f)]
        public float contactOffset = 0.001f;

        [Tooltip("Layer mask for physics spherecast/raycast queries.")]
        public LayerMask collisionLayerMask = ~0;

        [Tooltip("Whether to ignore trigger colliders (grasping physical geometry only).")]
        public bool ignoreTriggers = true;

        [Tooltip("Whether to curl non-thumb metacarpal bones.")]
        public bool curlMetacarpals = false;

        [Tooltip("Whether the thumb metacarpal / base can curl.")]
        public bool curlThumbMetacarpal = true;

        [Tooltip("Optional custom palm reference transform. If null, automatically inferred.")]
        public Transform palmReference;

        [Tooltip("Optional custom finger chains. If null or empty, automatically detected from wrist root.")]
        public List<FingerChain> customChains;

        [Tooltip("Whether to synchronize physics transforms during posing iterations (crucial for edit-mode accuracy).")]
        public bool syncPhysicsTransforms = true;

        [Tooltip("Maximum rotation steps per bone to prevent infinite loops.")]
        public int maxStepsPerBone = 120;
    }
}
