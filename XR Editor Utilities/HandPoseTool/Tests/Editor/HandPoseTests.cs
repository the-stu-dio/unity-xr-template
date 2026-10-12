using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StudioXR.HandPose.Tests
{
    [TestFixture]
    public class HandPoseTests
    {
        private GameObject m_WristGO;
        private GameObject m_TargetGO;
        private List<Transform> m_FingerBones;
        private Transform m_IndexProximal;
        private Transform m_IndexIntermediate;
        private Transform m_IndexDistal;
        private Transform m_ThumbProximal;
        private Transform m_ThumbDistal;

        [SetUp]
        public void SetUp()
        {
            m_FingerBones = new List<Transform>();

            // 1. Create root wrist bone
            m_WristGO = new GameObject("Wrist_Root");
            m_WristGO.transform.position = Vector3.zero;
            m_WristGO.transform.rotation = Quaternion.identity;

            // 2. Create Palm transform
            var palmGO = new GameObject("L_Palm");
            palmGO.transform.SetParent(m_WristGO.transform, false);
            palmGO.transform.localPosition = new Vector3(0f, -0.015f, 0.05f);

            // 3. Create Index finger hierarchy: Proximal -> Intermediate -> Distal -> Tip
            var idxProxGO = new GameObject("Index_Proximal");
            idxProxGO.transform.SetParent(m_WristGO.transform, false);
            idxProxGO.transform.localPosition = new Vector3(0.015f, 0f, 0.04f);
            m_IndexProximal = idxProxGO.transform;
            m_FingerBones.Add(m_IndexProximal);

            var idxInterGO = new GameObject("Index_Intermediate");
            idxInterGO.transform.SetParent(idxProxGO.transform, false);
            idxInterGO.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            m_IndexIntermediate = idxInterGO.transform;
            m_FingerBones.Add(m_IndexIntermediate);

            var idxDistGO = new GameObject("Index_Distal");
            idxDistGO.transform.SetParent(idxInterGO.transform, false);
            idxDistGO.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            m_IndexDistal = idxDistGO.transform;
            m_FingerBones.Add(m_IndexDistal);

            var idxTipGO = new GameObject("Index_Tip");
            idxTipGO.transform.SetParent(idxDistGO.transform, false);
            idxTipGO.transform.localPosition = new Vector3(0f, 0f, 0.015f);

            // 4. Create Thumb hierarchy: Proximal -> Distal -> Tip
            var thumbProxGO = new GameObject("Thumb_Proximal");
            thumbProxGO.transform.SetParent(m_WristGO.transform, false);
            thumbProxGO.transform.localPosition = new Vector3(-0.02f, -0.005f, 0.025f);
            m_ThumbProximal = thumbProxGO.transform;
            m_FingerBones.Add(m_ThumbProximal);

            var thumbDistGO = new GameObject("Thumb_Distal");
            thumbDistGO.transform.SetParent(thumbProxGO.transform, false);
            thumbDistGO.transform.localPosition = new Vector3(-0.01f, -0.005f, 0.025f);
            m_ThumbDistal = thumbDistGO.transform;
            m_FingerBones.Add(m_ThumbDistal);

            var thumbTipGO = new GameObject("Thumb_Tip");
            thumbTipGO.transform.SetParent(thumbDistGO.transform, false);
            thumbTipGO.transform.localPosition = new Vector3(-0.005f, -0.005f, 0.015f);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_WristGO != null)
            {
                UnityEngine.Object.DestroyImmediate(m_WristGO);
            }
            if (m_TargetGO != null)
            {
                UnityEngine.Object.DestroyImmediate(m_TargetGO);
            }
        }

        [Test]
        public void PoseHand_WithSphereInPalm_RotatesFingerBonesFromDefault()
        {
            // Place a primitive sphere collider in the palm
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.name = "Target_Sphere";
            m_TargetGO.transform.position = new Vector3(0.01f, -0.035f, 0.065f);
            m_TargetGO.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);

            var sphereCollider = m_TargetGO.GetComponent<SphereCollider>();
            Assert.IsNotNull(sphereCollider, "Primitive sphere must have a SphereCollider");

            // Cache initial rotations before posing
            var initialRotations = new Dictionary<Transform, Quaternion>();
            for (int i = 0; i < m_FingerBones.Count; i++)
            {
                initialRotations[m_FingerBones[i]] = m_FingerBones[i].localRotation;
            }

            // Call the posing API
            HandPoser.PoseHand(m_WristGO.transform, sphereCollider);

            // Assert that finger bones' local rotations have changed from their default rotations
            bool anyBoneRotated = false;
            for (int i = 0; i < m_FingerBones.Count; i++)
            {
                var bone = m_FingerBones[i];
                float angle = Quaternion.Angle(initialRotations[bone], bone.localRotation);
                if (angle > 0.5f)
                {
                    anyBoneRotated = true;
                }
            }

            Assert.IsTrue(anyBoneRotated, "At least one finger bone must have rotated from its default rotation.");

            float indexProxAngle = Quaternion.Angle(initialRotations[m_IndexProximal], m_IndexProximal.localRotation);
            Assert.Greater(indexProxAngle, 1f, "Index proximal bone should rotate inward toward the sphere collider in palm.");

            float indexInterAngle = Quaternion.Angle(initialRotations[m_IndexIntermediate], m_IndexIntermediate.localRotation);
            Assert.Greater(indexInterAngle, 1f, "Index intermediate bone should also rotate inward to conform to the sphere.");
        }

        [Test]
        public void PoseHand_NullWristRoot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                HandPoser.PoseHand(null, (Collider)null);
            });
        }

        [Test]
        public void PoseHand_NullTarget_DoesNotThrowAndDoesNotAlterBones()
        {
            var initialRot = m_IndexProximal.localRotation;
            Assert.DoesNotThrow(() =>
            {
                HandPoser.PoseHand(m_WristGO.transform, (Collider)null);
            });

            float angleDiff = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Less(angleDiff, 0.001f, "Bones must not rotate when target collider is null.");
        }

        [Test]
        public void ResetPose_RestoresDefaultRotations()
        {
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(0.01f, -0.035f, 0.065f);
            m_TargetGO.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);

            var initialRot = m_IndexProximal.localRotation;

            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>());
            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curledAngle, 1f, "Index proximal bone should curl during PoseHand.");

            HandPoser.ResetPose(m_WristGO.transform);
            float resetAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Less(resetAngle, 0.001f, "Index proximal bone must return to initial rest rotation after ResetPose.");
        }

        [Test]
        public void PoseHand_NonDestructive_DoesNotAddComponentsOrAlterMaterials()
        {
            int componentCountBefore = m_WristGO.GetComponentsInChildren<Component>(true).Length;
            var initialRot = m_IndexProximal.localRotation;

            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(0.01f, -0.035f, 0.065f);
            m_TargetGO.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);

            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>());

            int componentCountAfter = m_WristGO.GetComponentsInChildren<Component>(true).Length;
            Assert.AreEqual(componentCountBefore, componentCountAfter,
                "Hand hierarchy component count must remain identical (no added colliders or scripts).");

            float curlAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curlAngle, 1f, "Bones must actively pose around target while remaining non-destructive.");
        }

        [Test]
        public void PoseHand_TargetFarAway_BonesCurlToMaxAngle()
        {
            // Place target far away so raycasts don't hit
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(10f, 10f, 10f);

            var initialRot = m_IndexProximal.localRotation;
            var config = new HandPoseConfig
            {
                maxCurlAngle = 45f,
                stepAngle = 1.5f
            };

            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>(), config);

            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.GreaterOrEqual(curledAngle, 40f, "Bones should curl up to maxCurlAngle when no obstacle is contacted.");
        }

        [Test]
        public void PoseHand_TargetWithTriggerCollider_IgnoresTriggerAndPosesToSolidCollider()
        {
            m_TargetGO = new GameObject("CompoundTarget");

            // Large trigger surrounding everything (e.g. affordance or hover volume)
            var triggerSphere = m_TargetGO.AddComponent<SphereCollider>();
            triggerSphere.radius = 1.0f;
            triggerSphere.isTrigger = true;

            // Small solid collider placed in the palm
            var solidChild = new GameObject("SolidHandle");
            solidChild.transform.SetParent(m_TargetGO.transform, false);
            solidChild.transform.position = new Vector3(0.01f, -0.035f, 0.065f);
            var solidBox = solidChild.AddComponent<BoxCollider>();
            solidBox.size = new Vector3(0.03f, 0.03f, 0.03f);

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO);

            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curledAngle, 1f, "Hand should pose against solid collider even if a trigger collider encompasses the hand.");
        }

        [Test]
        public void PoseHand_FingertipHitsObstacle_PreventsBonePenetration()
        {
            // Place obstacle below the extended fingers so fingers start outside and curl down into contact
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_TargetGO.transform.position = new Vector3(0.015f, -0.05f, 0.10f);
            m_TargetGO.transform.localScale = new Vector3(0.05f, 0.04f, 0.05f);

            var boxCol = m_TargetGO.GetComponent<BoxCollider>();
            Transform tip = m_IndexDistal.GetChild(0);

            // Verify tip starts safely outside the obstacle bounds
            Assert.IsFalse(boxCol.bounds.Contains(tip.position), "Fingertip must start outside obstacle bounds at rest pose.");

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, boxCol);

            // Assert finger curled toward the obstacle
            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curledAngle, 1f, "Finger bones should curl toward the obstacle.");

            // Assert fingertip stopped at the obstacle surface and did not penetrate inside
            Bounds b = boxCol.bounds;
            b.Expand(-0.001f);
            bool tipInside = b.Contains(tip.position);
            Assert.IsFalse(tipInside, "Fingertip should not penetrate inside the target obstacle collider.");
        }

        [Test]
        public void PoseHand_NonConvexMeshCollider_StopsAtObstacleWithoutPenetration()
        {
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var meshCol = m_TargetGO.AddComponent<MeshCollider>();
            meshCol.sharedMesh = m_TargetGO.GetComponent<MeshFilter>().sharedMesh;
            meshCol.convex = false; // non-convex mesh collider
            UnityEngine.Object.DestroyImmediate(m_TargetGO.GetComponent<BoxCollider>());

            m_TargetGO.transform.position = new Vector3(0.015f, -0.05f, 0.10f);
            m_TargetGO.transform.localScale = new Vector3(0.05f, 0.04f, 0.05f);

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, meshCol);

            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curledAngle, 1f, "Finger bones should curl toward the mesh collider obstacle.");

            Transform tip = m_IndexDistal.GetChild(0);
            Bounds b = meshCol.bounds;
            b.Expand(-0.001f);
            Assert.IsFalse(b.Contains(tip.position), "Fingertip should not penetrate inside the mesh collider.");
        }

        [Test]
        public void PoseHand_TargetWithOnlyTrigger_DoesNotAlterBones()
        {
            m_TargetGO = new GameObject("TriggerOnlyTarget");
            var col = m_TargetGO.AddComponent<SphereCollider>();
            col.isTrigger = true;

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO);

            float diff = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Less(diff, 0.001f, "Hand bones should remain at rest pose when target only contains trigger colliders.");
        }

        [Test]
        public void PoseHand_CustomCurlAxis_RespectsManualAxis()
        {
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(10f, 10f, 10f);

            var chain = new FingerChain("Index", false)
            {
                bones = new List<Transform> { m_IndexProximal },
                useAutoCurlAxis = false,
                localCurlAxis = Vector3.forward // custom axis
            };

            var config = new HandPoseConfig
            {
                customChains = new List<FingerChain> { chain },
                maxCurlAngle = 30f,
                stepAngle = 10f
            };

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>(), config);

            // Vector3.forward rotation produces z component in Quaternion
            Quaternion diff = Quaternion.Inverse(initialRot) * m_IndexProximal.localRotation;
            Assert.Greater(Mathf.Abs(diff.z), 0.05f, "Bone should rotate around manual local curl axis (Vector3.forward).");
        }

        [Test]
        public void PoseHand_LeftHandAffordancePrefab_PosesAllChainsNonDestructively()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Affordances/Left Hand Affordance.prefab");
            Assert.IsNotNull(prefab, "Left Hand Affordance.prefab must exist in Assets/Affordances/");

            var instance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.IsNotNull(instance, "Failed to instantiate Left Hand Affordance prefab");

            try
            {
                var wrist = instance.transform.Find("L_Wrist");
                if (wrist == null)
                {
                    foreach (Transform child in instance.GetComponentsInChildren<Transform>())
                    {
                        if (child.name == "L_Wrist") { wrist = child; break; }
                    }
                }
                Assert.IsNotNull(wrist, "L_Wrist must exist in Left Hand Affordance");

                int compCountBefore = instance.GetComponentsInChildren<Component>(true).Length;

                // Place a target sphere near the palm
                Vector3 palmPos = HandPoser.GetPalmPosition(wrist);
                var targetSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                targetSphere.transform.position = palmPos + wrist.forward * 0.02f - wrist.up * 0.015f;
                targetSphere.transform.localScale = Vector3.one * 0.045f;

                try
                {
                    var chains = HandPoser.AutoDetectFingerChains(wrist);
                    Assert.AreEqual(5, chains.Count, "Left Hand Affordance should have 5 detected finger chains (Thumb, Index, Middle, Ring, Little).");

                    var rotBefore = new Dictionary<Transform, Quaternion>();
                    foreach (var chain in chains)
                    {
                        foreach (var b in chain.bones)
                        {
                            if (b != null) rotBefore[b] = b.localRotation;
                        }
                    }

                    HandPoser.PoseHand(wrist, targetSphere.GetComponent<Collider>());

                    int bonesRotated = 0;
                    foreach (var chain in chains)
                    {
                        foreach (var b in chain.bones)
                        {
                            if (b != null && Quaternion.Angle(rotBefore[b], b.localRotation) > 1f)
                            {
                                bonesRotated++;
                            }
                        }
                    }

                    Assert.GreaterOrEqual(bonesRotated, 3,
                        "Multiple finger bones across the affordance hand hierarchy must rotate to grasp the target collider.");

                    int compCountAfter = instance.GetComponentsInChildren<Component>(true).Length;
                    Assert.AreEqual(compCountBefore, compCountAfter, "Posing must not add components to the affordance prefab.");

                    // Verify ResetPose restores rest rotations exactly
                    HandPoser.ResetPose(wrist);
                    foreach (var chain in chains)
                    {
                        foreach (var b in chain.bones)
                        {
                            if (b != null)
                            {
                                float angleFromRest = Quaternion.Angle(rotBefore[b], b.localRotation);
                                Assert.Less(angleFromRest, 0.001f, $"Bone {b.name} must return to exact rest rotation after ResetPose.");
                            }
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(targetSphere);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PoseHand_DistalBoneWithoutTipChild_CurlsInwardTowardPalm()
        {
            // Destroy the tip transform so distal bone is a leaf with childCount == 0
            Transform tip = m_IndexDistal.GetChild(0);
            UnityEngine.Object.DestroyImmediate(tip.gameObject);

            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(10f, 10f, 10f); // far away to allow curl

            var chain = new FingerChain("Index", false)
            {
                bones = new List<Transform> { m_IndexDistal },
                tip = null
            };

            var config = new HandPoseConfig
            {
                customChains = new List<FingerChain> { chain },
                maxCurlAngle = 30f,
                stepAngle = 5f
            };

            Vector3 worldPosBefore = m_IndexDistal.position + m_IndexDistal.forward * 0.02f;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>(), config);
            Vector3 worldPosAfter = m_IndexDistal.position + m_IndexDistal.forward * 0.02f;

            // Inward curl toward palm means the bone forward direction pitches downward (-wristGO.up)
            float yBefore = m_WristGO.transform.InverseTransformPoint(worldPosBefore).y;
            float yAfter = m_WristGO.transform.InverseTransformPoint(worldPosAfter).y;
            Assert.Less(yAfter, yBefore, "Distal bone without tip child must curl inward toward palm (-wristRoot.up), not outward.");
        }

        [Test]
        public void PoseHand_CompoundColliderWithSkewedTransform_PosesCorrectly()
        {
            // Create compound target with non-uniform scaling and skewed rotation
            m_TargetGO = new GameObject("CompoundSkewedTarget");
            m_TargetGO.transform.position = new Vector3(0.015f, -0.05f, 0.08f);
            m_TargetGO.transform.rotation = Quaternion.Euler(25f, 35f, 15f);
            m_TargetGO.transform.localScale = new Vector3(1.4f, 0.8f, 1.6f);

            var boxChild = new GameObject("BoxPart");
            boxChild.transform.SetParent(m_TargetGO.transform, false);
            boxChild.transform.localPosition = Vector3.zero;
            var boxCol = boxChild.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(0.04f, 0.04f, 0.04f);

            var sphereChild = new GameObject("SpherePart");
            sphereChild.transform.SetParent(m_TargetGO.transform, false);
            sphereChild.transform.localPosition = new Vector3(0.02f, 0f, 0.01f);
            var sphereCol = sphereChild.AddComponent<SphereCollider>();
            sphereCol.radius = 0.02f;

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO);

            float curlAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curlAngle, 1f, "Finger bones must curl toward compound skewed colliders.");

            // Fingertip must not penetrate any of the child colliders
            Transform tip = m_IndexDistal.GetChild(0);
            Vector3 closestBox = boxCol.ClosestPoint(tip.position);
            Vector3 closestSphere = sphereCol.ClosestPoint(tip.position);

            Assert.GreaterOrEqual(Vector3.Distance(closestBox, tip.position), 0.0005f,
                "Fingertip must not penetrate compound box collider.");
            Assert.GreaterOrEqual(Vector3.Distance(closestSphere, tip.position), 0.0005f,
                "Fingertip must not penetrate compound sphere collider.");
        }

        [Test]
        public void PoseHand_NullGameObjectTarget_DoesNotThrowAndDoesNotAlterBones()
        {
            var initialRot = m_IndexProximal.localRotation;
            Assert.DoesNotThrow(() =>
            {
                HandPoser.PoseHand(m_WristGO.transform, (GameObject)null);
            });

            float angleDiff = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Less(angleDiff, 0.001f, "Bones must not rotate when target GameObject is null.");
        }

        [Test]
        public void SaveCurrentPoseAsRestPose_RestoresNewRestPose()
        {
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = new Vector3(0.01f, -0.035f, 0.065f);
            m_TargetGO.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);

            // 1. First pose against target
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>());
            Quaternion pose1Rot = m_IndexProximal.localRotation;

            // 2. Overwrite rest pose with current curled pose
            HandPoser.RecordDefaultPose(m_WristGO.transform, null, overwrite: true);

            // 3. Pose again with far target to curl further
            m_TargetGO.transform.position = new Vector3(10f, 10f, 10f);
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>());
            Quaternion pose2Rot = m_IndexProximal.localRotation;
            Assert.Greater(Quaternion.Angle(pose1Rot, pose2Rot), 1f, "Hand must curl further on second pose.");

            // 4. Reset pose: must return to pose1Rot (the newly saved rest pose), NOT initial zero rotation!
            HandPoser.ResetPose(m_WristGO.transform);
            float diffFromSaved = Quaternion.Angle(pose1Rot, m_IndexProximal.localRotation);
            Assert.Less(diffFromSaved, 0.001f, "ResetPose must restore the overwritten rest pose, not the original zero pose.");
        }

        [Test]
        public void AutoDetectFingerChains_MixedNaming_DetectsAllFingers()
        {
            var root = new GameObject("MixedRigRoot");
            try
            {
                // Thumb with standard naming
                var thumb = new GameObject("Thumb_Proximal");
                thumb.transform.SetParent(root.transform, false);
                var thumbDist = new GameObject("Thumb_Distal");
                thumbDist.transform.SetParent(thumb.transform, false);
                var thumbTip = new GameObject("Thumb_Tip");
                thumbTip.transform.SetParent(thumbDist.transform, false);

                // 4 fingers with non-standard naming (Digit1 to Digit4)
                for (int i = 1; i <= 4; i++)
                {
                    var dProx = new GameObject($"Digit{i}_Proximal");
                    dProx.transform.SetParent(root.transform, false);
                    var dDist = new GameObject($"Digit{i}_Distal");
                    dDist.transform.SetParent(dProx.transform, false);
                    var dTip = new GameObject($"Digit{i}_Tip");
                    dTip.transform.SetParent(dDist.transform, false);
                }

                var chains = HandPoser.AutoDetectFingerChains(root.transform);
                Assert.AreEqual(5, chains.Count, "AutoDetectFingerChains must detect all 5 fingers even when thumb uses standard keyword and other digits use non-standard naming.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AutoDetectFingerChains_DuplicateKeywordNames_DoesNotDuplicateChainsOrBones()
        {
            var root = new GameObject("DuplicateKeywordRoot");
            try
            {
                // Bone containing both "Little" and "Pinky" keywords
                var bone1 = new GameObject("Hand_Little_Pinky_Proximal");
                bone1.transform.SetParent(root.transform, false);
                var bone2 = new GameObject("Hand_Little_Pinky_Distal");
                bone2.transform.SetParent(bone1.transform, false);
                var boneTip = new GameObject("Hand_Little_Pinky_Tip");
                boneTip.transform.SetParent(bone2.transform, false);

                var chains = HandPoser.AutoDetectFingerChains(root.transform);
                Assert.AreEqual(1, chains.Count, "AutoDetectFingerChains must create exactly one chain for a finger whose bones contain multiple keywords.");
                Assert.AreEqual(2, chains[0].bones.Count, "Chain must contain exactly 2 non-tip bones without duplicating.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PoseHand_ArbitraryWorldPositionAndOrientation_PosesCorrectly()
        {
            // Position and rotate the wrist in world space
            m_WristGO.transform.position = new Vector3(15.2f, -8.4f, 25.7f);
            m_WristGO.transform.rotation = Quaternion.Euler(35f, 65f, 20f);

            // Compute relative target sphere position near the palm
            Vector3 palmPos = HandPoser.GetPalmPosition(m_WristGO.transform);
            m_TargetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_TargetGO.transform.position = palmPos + m_WristGO.transform.forward * 0.02f - m_WristGO.transform.up * 0.015f;
            m_TargetGO.transform.localScale = Vector3.one * 0.035f;

            var initialRot = m_IndexProximal.localRotation;
            HandPoser.PoseHand(m_WristGO.transform, m_TargetGO.GetComponent<Collider>());

            float curledAngle = Quaternion.Angle(initialRot, m_IndexProximal.localRotation);
            Assert.Greater(curledAngle, 1f, "Hand at arbitrary world translation and rotation must successfully pose against target.");
        }

        [Test]
        public void PoseHand_TargetInteractableIsPrefabAsset_HandPoseWindowValidates()
        {
            // Test that PrefabUtility.IsPartOfPrefabAsset detects project prefabs
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Affordances/Left Hand Affordance.prefab");
            Assert.IsNotNull(prefab, "Left Hand Affordance.prefab must exist in Assets/Affordances/");
            Assert.IsTrue(UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab),
                "Project asset must be identified as Prefab Asset to prevent invalid physics queries.");
        }
    }
}
