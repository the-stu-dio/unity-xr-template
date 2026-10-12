using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioXR.HandPose
{
    /// <summary>
    /// Core non-destructive hand posing utility that iteratively rotates finger bones
    /// inward toward the palm using physics raycasts and spherecasts until contact is made.
    /// </summary>
    public static class HandPoser
    {
        private static readonly string[] s_FingerKeywords = new[]
        {
            "Thumb", "Index", "Middle", "Ring", "Little", "Pinky", "Pinkie"
        };

        // Cache of default rotations by root transform for non-destructive resetting
        private static readonly Dictionary<Transform, Dictionary<Transform, Quaternion>> s_DefaultRotationsCache =
            new Dictionary<Transform, Dictionary<Transform, Quaternion>>();

        #region Public API

        /// <summary>
        /// Poses the skeletal hand rooted at wristRoot to grasp the target collider.
        /// </summary>
        public static void PoseHand(Transform wristRoot, Collider target)
        {
            PoseHand(wristRoot, target != null ? new[] { target } : Array.Empty<Collider>(), null);
        }

        /// <summary>
        /// Poses the skeletal hand rooted at wristRoot to grasp the target collider with custom config.
        /// </summary>
        public static void PoseHand(Transform wristRoot, Collider target, HandPoseConfig config)
        {
            PoseHand(wristRoot, target != null ? new[] { target } : Array.Empty<Collider>(), config);
        }

        /// <summary>
        /// Poses the skeletal hand rooted at wristRoot to grasp the target GameObject's colliders.
        /// </summary>
        public static void PoseHand(Transform wristRoot, GameObject target)
        {
            PoseHand(wristRoot, target, null);
        }

        /// <summary>
        /// Poses the skeletal hand rooted at wristRoot to grasp the target GameObject's colliders with custom config.
        /// </summary>
        public static void PoseHand(Transform wristRoot, GameObject target, HandPoseConfig config)
        {
            if (wristRoot == null) throw new ArgumentNullException(nameof(wristRoot));
            var colliders = target != null ? target.GetComponentsInChildren<Collider>() : Array.Empty<Collider>();
            PoseHand(wristRoot, colliders, config);
        }

        /// <summary>
        /// Poses the skeletal hand rooted at wristRoot to grasp the collection of target colliders.
        /// </summary>
        public static void PoseHand(Transform wristRoot, IList<Collider> targetColliders, HandPoseConfig config = null)
        {
            if (wristRoot == null)
                throw new ArgumentNullException(nameof(wristRoot));

            config ??= new HandPoseConfig();

            // Cache initial rotations for resetting and non-destructive operations
            RecordDefaultPose(wristRoot, config);

            // Synchronize physics engine transforms (especially important in edit mode)
            if (config.syncPhysicsTransforms)
            {
                Physics.SyncTransforms();
            }

            // Filter active colliders
            var activeTargets = new List<Collider>();
            if (targetColliders != null)
            {
                for (int i = 0; i < targetColliders.Count; i++)
                {
                    var col = targetColliders[i];
                    if (col != null && col.enabled && col.gameObject.activeInHierarchy)
                    {
                        if (config.ignoreTriggers && col.isTrigger)
                        {
                            continue;
                        }
                        if ((config.collisionLayerMask.value & (1 << col.gameObject.layer)) == 0)
                        {
                            continue;
                        }
                        activeTargets.Add(col);
                    }
                }
            }

            if (activeTargets.Count == 0)
            {
                // No valid physical colliders to grasp; do not curl into an unintended fist
                return;
            }

            var chains = (config.customChains != null && config.customChains.Count > 0)
                ? config.customChains
                : AutoDetectFingerChains(wristRoot, config);

            if (chains == null || chains.Count == 0)
            {
                Debug.LogWarning($"[HandPoser] No finger chains found under '{wristRoot.name}'.", wristRoot);
                return;
            }

            Vector3 palmPos = GetPalmPosition(wristRoot, config);

            // Pose each finger chain with synergistic progressive curl
            for (int c = 0; c < chains.Count; c++)
            {
                var chain = chains[c];
                PoseChain(chain, wristRoot, palmPos, activeTargets, config);
            }

            if (config.syncPhysicsTransforms)
            {
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Caches the current local rotations of all finger bones under wristRoot as the rest pose.
        /// </summary>
        public static void RecordDefaultPose(Transform wristRoot, HandPoseConfig config = null, bool overwrite = false)
        {
            if (wristRoot == null) return;

            if (!s_DefaultRotationsCache.TryGetValue(wristRoot, out var dict) || overwrite)
            {
                dict = new Dictionary<Transform, Quaternion>();
                s_DefaultRotationsCache[wristRoot] = dict;
            }

            var chains = (config?.customChains != null && config.customChains.Count > 0)
                ? config.customChains
                : AutoDetectFingerChains(wristRoot, config);

            for (int c = 0; c < chains.Count; c++)
            {
                var chain = chains[c];
                if (chain == null) continue;
                chain.CacheDefaultRotations();
                for (int b = 0; b < chain.bones.Count; b++)
                {
                    var bone = chain.bones[b];
                    if (bone != null && (!dict.ContainsKey(bone) || overwrite))
                    {
                        dict[bone] = bone.localRotation;
                    }
                }
            }
        }

        /// <summary>
        /// Restores all finger bones under wristRoot to their cached default/rest pose.
        /// </summary>
        public static void ResetPose(Transform wristRoot, HandPoseConfig config = null)
        {
            if (wristRoot == null) return;

            if (s_DefaultRotationsCache.TryGetValue(wristRoot, out var dict))
            {
                foreach (var kvp in dict)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.localRotation = kvp.Value;
                    }
                }
            }

            if (config?.customChains != null)
            {
                for (int i = 0; i < config.customChains.Count; i++)
                {
                    config.customChains[i]?.RestoreDefaultRotations();
                }
            }

            if (config == null || config.syncPhysicsTransforms)
            {
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Automatically discovers finger bone chains under wristRoot.
        /// </summary>
        public static List<FingerChain> AutoDetectFingerChains(Transform wristRoot, HandPoseConfig config = null)
        {
            var result = new List<FingerChain>();
            if (wristRoot == null) return result;

            bool curlMetacarpals = config != null && config.curlMetacarpals;
            bool curlThumbMetacarpal = config == null || config.curlThumbMetacarpal;

            var allDescendants = wristRoot.GetComponentsInChildren<Transform>(true);
            var assignedBones = new HashSet<Transform>();

            // Strategy 1: Match known finger names (XR Hands / OpenXR / Humanoid standard)
            for (int k = 0; k < s_FingerKeywords.Length; k++)
            {
                string keyword = s_FingerKeywords[k];
                var matching = new List<Transform>();

                for (int d = 0; d < allDescendants.Length; d++)
                {
                    var t = allDescendants[d];
                    if (t == wristRoot || assignedBones.Contains(t)) continue;

                    if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matching.Add(t);
                    }
                }

                if (matching.Count > 0)
                {
                    // Sort by hierarchy depth
                    matching.Sort((a, b) => GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b)));

                    bool isThumb = string.Equals(keyword, "Thumb", StringComparison.OrdinalIgnoreCase);
                    var chain = new FingerChain(keyword, isThumb);

                    Transform tip = null;
                    for (int m = 0; m < matching.Count; m++)
                    {
                        var t = matching[m];
                        string lower = t.name.ToLowerInvariant();

                        if (lower.Contains("tip") || lower.Contains("end"))
                        {
                            tip = t;
                            continue;
                        }

                        if (lower.Contains("metacarpal"))
                        {
                            if (isThumb && curlThumbMetacarpal)
                            {
                                chain.bones.Add(t);
                            }
                            else if (!isThumb && curlMetacarpals)
                            {
                                chain.bones.Add(t);
                            }
                            continue;
                        }

                        chain.bones.Add(t);
                    }

                    // If tip not explicitly named, use child of last bone if available
                    if (tip == null && matching.Count > 0)
                    {
                        var last = matching[matching.Count - 1];
                        if (last.childCount > 0)
                        {
                            tip = last.GetChild(0);
                        }
                    }
                    chain.tip = tip;

                    if (chain.bones.Count > 0)
                    {
                        for (int b = 0; b < chain.bones.Count; b++)
                        {
                            if (chain.bones[b] != null) assignedBones.Add(chain.bones[b]);
                        }
                        if (chain.tip != null) assignedBones.Add(chain.tip);

                        result.Add(chain);
                    }
                }
            }

            // Strategy 2: If fewer than 5 fingers found, discover remaining chains from leaf bones
            if (result.Count < 5)
            {
                var leaves = new List<Transform>();
                for (int i = 0; i < allDescendants.Length; i++)
                {
                    var t = allDescendants[i];
                    if (t == wristRoot || assignedBones.Contains(t)) continue;
                    if (t.childCount == 0)
                    {
                        string nameLower = t.name.ToLowerInvariant();
                        if (!nameLower.Contains("palm") && !nameLower.Contains("skin") && !nameLower.Contains("mesh") &&
                            !nameLower.Contains("attach") && !nameLower.Contains("socket") && !nameLower.Contains("grip") &&
                            !nameLower.Contains("anchor") && !nameLower.Contains("indicator") && !nameLower.Contains("collider"))
                        {
                            leaves.Add(t);
                        }
                    }
                }

                for (int l = 0; l < leaves.Count; l++)
                {
                    var leaf = leaves[l];
                    if (assignedBones.Contains(leaf)) continue;

                    var path = new List<Transform>();
                    var curr = leaf.parent;

                    while (curr != null && curr != wristRoot && !assignedBones.Contains(curr))
                    {
                        string currLower = curr.name.ToLowerInvariant();
                        if (currLower.Contains("metacarpal") && !curlMetacarpals)
                        {
                            curr = curr.parent;
                            continue;
                        }

                        path.Add(curr);
                        curr = curr.parent;
                    }

                    if (path.Count > 0)
                    {
                        path.Reverse(); // root to leaf order
                        bool isThumb = leaf.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0;
                        var chain = new FingerChain($"Finger_{result.Count + 1}", isThumb)
                        {
                            bones = path,
                            tip = leaf
                        };

                        for (int p = 0; p < path.Count; p++)
                        {
                            assignedBones.Add(path[p]);
                        }
                        assignedBones.Add(leaf);

                        result.Add(chain);
                        if (result.Count >= 5) break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Calculates or retrieves the palm position for inward raycasting and curl axis calculation.
        /// </summary>
        public static Vector3 GetPalmPosition(Transform wristRoot, HandPoseConfig config = null)
        {
            if (config?.palmReference != null)
                return config.palmReference.position;

            if (wristRoot == null)
                return Vector3.zero;

            // Search for bone named Palm
            var all = wristRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.IndexOf("Palm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i].position;
                }
            }

            // Infer from base finger bones
            var chains = AutoDetectFingerChains(wristRoot, config);
            if (chains.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                for (int c = 0; c < chains.Count; c++)
                {
                    if (chains[c].bones.Count > 0 && chains[c].bones[0] != null)
                    {
                        sum += chains[c].bones[0].position;
                        count++;
                    }
                }

                if (count > 0)
                {
                    Vector3 baseCenter = sum / count;
                    Vector3 wristPos = wristRoot.position;
                    // Palm is roughly between wrist and knuckle bases, slightly shifted inward (-up)
                    Vector3 estimatedPalm = Vector3.Lerp(wristPos, baseCenter, 0.6f);
                    estimatedPalm -= wristRoot.up * 0.01f;
                    return estimatedPalm;
                }
            }

            return wristRoot.position + wristRoot.forward * 0.05f - wristRoot.up * 0.01f;
        }

        #endregion

        #region Internal Posing Engine

        private struct SegmentData
        {
            public Vector3 start;
            public Vector3 end;
            public Vector3 mid;
        }

        private static Vector3 GetTipPosition(FingerChain chain)
        {
            if (chain.tip != null)
                return chain.tip.position;

            if (chain.bones == null || chain.bones.Count == 0)
                return Vector3.zero;

            Transform last = chain.bones[chain.bones.Count - 1];
            if (last == null)
                return Vector3.zero;

            if (last.childCount > 0)
                return last.GetChild(0).position;

            if (last.parent != null)
            {
                Vector3 parentDir = last.position - last.parent.position;
                if (parentDir.sqrMagnitude > 1e-6f)
                {
                    float length = Mathf.Max(0.015f, parentDir.magnitude * 0.75f);
                    Vector3 localBoneDir = last.InverseTransformDirection(parentDir.normalized);
                    return last.position + last.TransformDirection(localBoneDir) * length;
                }
            }

            return last.position + last.forward * 0.02f;
        }

        private static void GetSubchainGeometry(
            FingerChain chain,
            int startBoneIndex,
            List<SegmentData> outSegments,
            List<Vector3> outMovingPoints)
        {
            outSegments.Clear();
            outMovingPoints.Clear();

            if (chain.bones == null || startBoneIndex >= chain.bones.Count)
                return;

            Vector3 tipPos = GetTipPosition(chain);

            for (int i = startBoneIndex; i < chain.bones.Count; i++)
            {
                var b = chain.bones[i];
                if (b == null) continue;

                Vector3 startPos = b.position;
                Vector3 endPos = (i + 1 < chain.bones.Count && chain.bones[i + 1] != null)
                    ? chain.bones[i + 1].position
                    : tipPos;

                Vector3 midPos = (startPos + endPos) * 0.5f;

                outSegments.Add(new SegmentData { start = startPos, end = endPos, mid = midPos });

                outMovingPoints.Add(midPos);
                outMovingPoints.Add(endPos);
            }
        }

        private static void PoseChain(
            FingerChain chain,
            Transform wristRoot,
            Vector3 palmPos,
            List<Collider> targets,
            HandPoseConfig config)
        {
            if (chain == null || chain.bones == null || chain.bones.Count == 0 || targets == null || targets.Count == 0)
                return;

            int boneCount = chain.bones.Count;
            var localAxes = new Vector3[boneCount];
            var accumulatedAngle = new float[boneCount];
            var stopped = new bool[boneCount];
            var segments = new List<SegmentData>();
            var currMovingPoints = new List<Vector3>();
            var prevMovingPoints = new List<Vector3>();

            for (int b = 0; b < boneCount; b++)
            {
                var bone = chain.bones[b];
                if (bone == null)
                {
                    stopped[b] = true;
                    continue;
                }

                Transform child = (b + 1 < boneCount) ? chain.bones[b + 1] : chain.tip;
                localAxes[b] = ResolveLocalCurlAxis(bone, child, chain, palmPos, wristRoot, config);
            }

            // Initial contact check: if any bone already contacts targets at rest pose, mark stopped
            for (int b = 0; b < boneCount; b++)
            {
                if (stopped[b]) continue;
                GetSubchainGeometry(chain, b, segments, currMovingPoints);
                if (CheckSubchainContactOrMotion(segments, currMovingPoints, currMovingPoints, palmPos, targets, config))
                {
                    stopped[b] = true;
                }
            }

            int totalSteps = 0;
            int stepsNeededPerBone = Mathf.CeilToInt(config.maxCurlAngle / Mathf.Max(0.1f, config.stepAngle)) + 10;
            int maxTotalSteps = Mathf.Max(config.maxStepsPerBone, stepsNeededPerBone) * boneCount;

            // Synergistic curl loop: all unblocked bones curl incrementally inward toward the palm,
            // wrapping around object colliders smoothly and stopping upon collision.
            while (totalSteps < maxTotalSteps)
            {
                bool anyBoneMoved = false;

                for (int b = 0; b < boneCount; b++)
                {
                    if (stopped[b]) continue;

                    var bone = chain.bones[b];
                    if (bone == null || accumulatedAngle[b] >= config.maxCurlAngle)
                    {
                        stopped[b] = true;
                        continue;
                    }

                    Quaternion prevRot = bone.localRotation;
                    GetSubchainGeometry(chain, b, segments, prevMovingPoints);

                    Quaternion stepDelta = Quaternion.AngleAxis(config.stepAngle, localAxes[b]);
                    bone.localRotation = prevRot * stepDelta;
                    accumulatedAngle[b] += config.stepAngle;

                    if (config.syncPhysicsTransforms)
                    {
                        Physics.SyncTransforms();
                    }

                    GetSubchainGeometry(chain, b, segments, currMovingPoints);

                    if (CheckSubchainContactOrMotion(segments, prevMovingPoints, currMovingPoints, palmPos, targets, config))
                    {
                        // Bisection refinement: find exact contact boundary between prevRot and current localRotation
                        Quaternion goodRot = prevRot;
                        Quaternion badRot = bone.localRotation;

                        for (int bisect = 0; bisect < 4; bisect++)
                        {
                            Quaternion midRot = Quaternion.Slerp(goodRot, badRot, 0.5f);
                            bone.localRotation = midRot;
                            if (config.syncPhysicsTransforms) Physics.SyncTransforms();

                            GetSubchainGeometry(chain, b, segments, currMovingPoints);
                            if (CheckSubchainContactOrMotion(segments, prevMovingPoints, currMovingPoints, palmPos, targets, config))
                            {
                                badRot = midRot;
                            }
                            else
                            {
                                goodRot = midRot;
                            }
                        }

                        bone.localRotation = goodRot;
                        if (config.syncPhysicsTransforms)
                        {
                            Physics.SyncTransforms();
                        }

                        stopped[b] = true;
                    }
                    else
                    {
                        anyBoneMoved = true;
                    }
                }

                if (!anyBoneMoved)
                {
                    break;
                }

                totalSteps++;
            }
        }

        private static Vector3 ResolveLocalCurlAxis(
            Transform bone,
            Transform child,
            FingerChain chain,
            Vector3 palmPos,
            Transform wristRoot,
            HandPoseConfig config)
        {
            if (!chain.useAutoCurlAxis)
            {
                return chain.localCurlAxis.normalized;
            }

            Vector3 segmentDir;
            if (child != null)
            {
                segmentDir = (child.position - bone.position).normalized;
            }
            else if (bone.parent != null)
            {
                segmentDir = (bone.position - bone.parent.position).normalized;
            }
            else
            {
                segmentDir = bone.forward;
            }

            if (segmentDir.sqrMagnitude < 1e-4f)
            {
                segmentDir = bone.forward;
            }

            Vector3 palmInward = (config?.palmReference != null) ? -config.palmReference.up : -wristRoot.up;

            Vector3 curlTargetDir;
            if (chain.isThumb)
            {
                Vector3 toPalm = palmPos - bone.position;
                if (toPalm.sqrMagnitude < 1e-6f)
                {
                    toPalm = palmInward;
                }
                else
                {
                    toPalm = toPalm.normalized;
                }

                // Project toPalm orthogonal to segmentDir for thumb opposition
                curlTargetDir = toPalm - Vector3.Dot(toPalm, segmentDir) * segmentDir;
                if (curlTargetDir.sqrMagnitude < 1e-4f)
                {
                    curlTargetDir = palmInward;
                }
                else
                {
                    curlTargetDir = curlTargetDir.normalized;
                }
            }
            else
            {
                // Inward flexion for fingers is perpendicular to segmentDir toward palm surface
                curlTargetDir = palmInward - Vector3.Dot(palmInward, segmentDir) * segmentDir;
                if (curlTargetDir.sqrMagnitude < 1e-4f)
                {
                    curlTargetDir = palmInward;
                }
                else
                {
                    curlTargetDir = curlTargetDir.normalized;
                }
            }

            Vector3 worldCurlAxis = Vector3.Cross(segmentDir, curlTargetDir);
            if (worldCurlAxis.sqrMagnitude < 1e-4f)
            {
                worldCurlAxis = wristRoot.right;
            }
            else
            {
                worldCurlAxis = worldCurlAxis.normalized;
            }

            Vector3 localAxis = bone.InverseTransformDirection(worldCurlAxis).normalized;

            // Verify rotation direction analytically: rotating by +1 deg around localAxis must move segment closer to curlTargetDir
            Vector3 testTargetPoint = (child != null && (child.position - bone.position).sqrMagnitude > 1e-6f)
                ? child.position
                : bone.position + segmentDir * 0.02f;

            Vector3 origPointLocal = bone.InverseTransformPoint(testTargetPoint);
            Vector3 rotatedPointLocal = Quaternion.AngleAxis(1f, localAxis) * origPointLocal;
            Vector3 testPointWorld = bone.TransformPoint(rotatedPointLocal);

            Vector3 origDelta = testTargetPoint - bone.position;
            Vector3 testDelta = testPointWorld - bone.position;

            float origDot = Vector3.Dot(origDelta.normalized, curlTargetDir);
            float testDot = Vector3.Dot(testDelta.normalized, curlTargetDir);

            if (testDot < origDot)
            {
                localAxis = -localAxis;
            }

            return localAxis;
        }

        private static bool CheckSubchainContactOrMotion(
            List<SegmentData> segments,
            List<Vector3> prevPoints,
            List<Vector3> currPoints,
            Vector3 palmPos,
            List<Collider> targets,
            HandPoseConfig config)
        {
            if (targets == null || targets.Count == 0) return false;

            float threshold = config.fingerRadius + config.contactOffset;

            // 1. Dynamic motion sweeps for each moving point along sweep path
            int count = Mathf.Min(prevPoints.Count, currPoints.Count);
            for (int p = 0; p < count; p++)
            {
                Vector3 pPrev = prevPoints[p];
                Vector3 pCurr = currPoints[p];
                Vector3 sweepDelta = pCurr - pPrev;
                float sweepDist = sweepDelta.magnitude;

                if (sweepDist > 1e-5f)
                {
                    Vector3 sweepDir = sweepDelta / sweepDist;

                    for (int t = 0; t < targets.Count; t++)
                    {
                        var target = targets[t];
                        if (target == null) continue;

                        if (target.Raycast(new Ray(pPrev, sweepDir), out _, sweepDist + config.contactOffset))
                        {
                            return true;
                        }
                    }

                    if (Physics.SphereCast(
                        pPrev,
                        config.fingerRadius,
                        sweepDir,
                        out RaycastHit sweepHit,
                        sweepDist + config.contactOffset,
                        config.collisionLayerMask,
                        QueryTriggerInteraction.Ignore))
                    {
                        if (IsTargetCollider(sweepHit.collider, targets))
                        {
                            return true;
                        }
                    }
                }
            }

            // 2. Overlap Capsule tests for each bone segment in the subchain
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                Vector3 segDelta = seg.end - seg.start;
                float segLen = segDelta.magnitude;
                if (segLen > 1e-4f)
                {
                    var cols = Physics.OverlapCapsule(
                        seg.start,
                        seg.end,
                        threshold,
                        config.collisionLayerMask,
                        QueryTriggerInteraction.Ignore);

                    for (int c = 0; c < cols.Length; c++)
                    {
                        if (IsTargetCollider(cols[c], targets))
                        {
                            return true;
                        }
                    }

                    // Direct segment raycasts against targets (crucial for catching triangle crossings on non-convex mesh colliders)
                    Vector3 segDir = segDelta / segLen;
                    Ray segRayForward = new Ray(seg.start, segDir);
                    Ray segRayReverse = new Ray(seg.end, -segDir);

                    Vector3 segOrthoA = Vector3.Cross(segDir, Vector3.up);
                    if (segOrthoA.sqrMagnitude < 1e-4f) segOrthoA = Vector3.Cross(segDir, Vector3.right);
                    segOrthoA = segOrthoA.normalized * config.fingerRadius;
                    Vector3 segOrthoB = Vector3.Cross(segDir, segOrthoA).normalized * config.fingerRadius;

                    for (int t = 0; t < targets.Count; t++)
                    {
                        var target = targets[t];
                        if (target == null) continue;

                        if (target.Raycast(segRayForward, out _, segLen) || target.Raycast(segRayReverse, out _, segLen))
                        {
                            return true;
                        }

                        // Offset cushion rays around segment cylinder for non-convex mesh colliders
                        if (target is MeshCollider mc && !mc.convex)
                        {
                            if (target.Raycast(new Ray(seg.start + segOrthoA, segDir), out _, segLen) ||
                                target.Raycast(new Ray(seg.start - segOrthoA, segDir), out _, segLen) ||
                                target.Raycast(new Ray(seg.start + segOrthoB, segDir), out _, segLen) ||
                                target.Raycast(new Ray(seg.start - segOrthoB, segDir), out _, segLen))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // 3. Overlap Sphere & ClosestPoint tests for each point in currPoints
            for (int p = 0; p < currPoints.Count; p++)
            {
                Vector3 pt = currPoints[p];

                var cols = Physics.OverlapSphere(
                    pt,
                    threshold,
                    config.collisionLayerMask,
                    QueryTriggerInteraction.Ignore);

                for (int c = 0; c < cols.Length; c++)
                {
                    if (IsTargetCollider(cols[c], targets))
                    {
                        return true;
                    }
                }

                for (int t = 0; t < targets.Count; t++)
                {
                    var target = targets[t];
                    if (target == null) continue;

                    if (!(target is MeshCollider mc && !mc.convex))
                    {
                        Vector3 cp = target.ClosestPoint(pt);
                        if (Vector3.Distance(cp, pt) <= threshold)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // For non-convex mesh colliders where ClosestPoint is unsupported by PhysX:
                        // Raycast toward target center
                        Vector3 toCenter = target.bounds.center - pt;
                        float centerDist = toCenter.magnitude;
                        if (centerDist > 1e-4f)
                        {
                            Vector3 dirCenter = toCenter / centerDist;
                            if (target.Raycast(new Ray(pt, dirCenter), out RaycastHit rHit, threshold + 0.05f))
                            {
                                if (rHit.distance <= threshold)
                                {
                                    return true;
                                }
                            }

                            // Reverse ray from outside toward pt to detect penetration/backfaces
                            if (target.Raycast(new Ray(pt + dirCenter * (threshold + 0.02f), -dirCenter), out RaycastHit bHit, threshold + 0.02f))
                            {
                                if (Vector3.Distance(bHit.point, pt) <= threshold)
                                {
                                    return true;
                                }
                            }
                        }

                        // Raycast toward palm
                        Vector3 toPalm = palmPos - pt;
                        float palmDist = toPalm.magnitude;
                        if (palmDist > 1e-4f)
                        {
                            Vector3 dirPalm = toPalm / palmDist;
                            Ray inwardRay = new Ray(pt - dirPalm * 0.02f, dirPalm);
                            if (target.Raycast(inwardRay, out RaycastHit inHit, 0.02f + threshold))
                            {
                                if (Vector3.Distance(inHit.point, pt) <= threshold)
                                {
                                    return true;
                                }
                            }

                            // Multi-directional cushion rays around pt
                            Vector3 ortho1 = Vector3.Cross(dirPalm, Vector3.up);
                            if (ortho1.sqrMagnitude < 1e-4f) ortho1 = Vector3.Cross(dirPalm, Vector3.right);
                            ortho1 = ortho1.normalized;
                            Vector3 ortho2 = Vector3.Cross(dirPalm, ortho1).normalized;

                            if ((target.Raycast(new Ray(pt, ortho1), out RaycastHit oHit1, threshold) && oHit1.distance <= threshold) ||
                                (target.Raycast(new Ray(pt, -ortho1), out RaycastHit oHit2, threshold) && oHit2.distance <= threshold) ||
                                (target.Raycast(new Ray(pt, ortho2), out RaycastHit oHit3, threshold) && oHit3.distance <= threshold) ||
                                (target.Raycast(new Ray(pt, -ortho2), out RaycastHit oHit4, threshold) && oHit4.distance <= threshold))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsTargetCollider(Collider col, List<Collider> targets)
        {
            if (col == null || targets == null) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == col) return true;
            }
            return false;
        }

        private static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }

        #endregion
    }
}
