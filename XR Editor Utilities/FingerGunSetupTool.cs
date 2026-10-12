using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using StudioXR.Interaction;

namespace StudioXR.Editor
{
    public static class FingerGunSetupTool
    {
        [MenuItem("Studio XR/Setup Finger Gun System", false, 10)]
        [MenuItem("GameObject/Studio XR/Setup Finger Gun System", false, 10)]
        [MenuItem("Tools/Studio XR/Setup Finger Gun System", false, 10)]
        public static void CreateFingerGunSystem(MenuCommand menuCommand)
        {
            GameObject contextObj = menuCommand?.context as GameObject ?? Selection.activeGameObject;
            GameObject targetHand = ResolveTargetHand(contextObj);

            if (targetHand == null)
            {
                EditorUtility.DisplayDialog(
                    "Setup Error",
                    "Could not find the 'Right Hand' GameObject in the current scene.\n\nPlease make sure your XR Origin Hands rig is present in the scene, or select your Hand GameObject in the Hierarchy.",
                    "OK"
                );
                return;
            }

            bool isLeft = IsLeftHand(targetHand);
            string handLabel = isLeft ? "Left Hand" : "Right Hand";

            // Locate or construct Locomotion and the Gesture Teleportation Provider
            GameObject locomotionObj = ResolveLocomotion(targetHand);
            GameObject providerObj = ResolveGestureTeleportationProvider(locomotionObj);

            // Clean up misplaced components, legacy rays, and orphaned stabilizers across the scene
            CleanUpMisplacedInstances(targetHand, providerObj);

            // Configure gesture detection directly on the hand
            var gesture = ConfigureGesture(targetHand, isLeft);

            // Locate the Poke Interactor (strictly avoiding pinch points)
            Transform pokeOrigin = FindPokeOrigin(targetHand);
            if (pokeOrigin == null)
            {
                Debug.LogWarning($"[Studio XR] No Poke Interactor found on '{targetHand.name}'. Creating a dedicated Poke Interactor child.");
                var pokeObj = new GameObject("Poke Interactor");
                Undo.RegisterCreatedObjectUndo(pokeObj, "Create Poke Interactor");
                pokeObj.transform.SetParent(targetHand.transform, false);
                pokeOrigin = pokeObj.transform;
            }

            // Create or resolve the Stabilized Origin under the hand
            GameObject stabObj = ResolveStabilizedOrigin(targetHand, isLeft);

            // Create or resolve the Finger Gun Teleport Interactor under the hand
            XRRayInteractor rayInteractor = ResolveTeleportInteractor(targetHand, stabObj.transform);

            // Configure the XRTransformStabilizer to smooth the poke origin relative to the ray
            ConfigureStabilizer(stabObj, pokeOrigin, rayInteractor);

            // Wire the GestureTeleportDriver on the Locomotion/Gesture Teleportation Provider
            ConfigureDriver(providerObj, gesture, rayInteractor, stabObj.transform, locomotionObj);

            EditorUtility.SetDirty(providerObj);
            EditorUtility.SetDirty(targetHand);
            EditorUtility.SetDirty(locomotionObj);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(targetHand.scene);
            }

            Selection.activeGameObject = providerObj;
            Debug.Log($"[Studio XR] Standardized Finger Gun Teleport setup complete!\n- Locomotion Provider: '{providerObj.name}' under '{locomotionObj.name}'\n- Interactor: '{rayInteractor.name}' on '{targetHand.name}' ({handLabel})\n- Origin: '{stabObj.name}' with XRTransformStabilizer tracking '{pokeOrigin.name}'");
        }

        private static GameObject ResolveLocomotion(GameObject targetHand)
        {
            Transform rigRoot = null;
            Transform current = targetHand.transform.parent;
            while (current != null)
            {
                if (current.GetComponent<XROrigin>() != null ||
                    current.name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("XR Rig", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rigRoot = current;
                    break;
                }
                current = current.parent;
            }

            if (rigRoot == null)
            {
                var xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                if (xrOrigin != null)
                    rigRoot = xrOrigin.transform;
            }

            if (rigRoot != null)
            {
                for (int i = 0; i < rigRoot.childCount; i++)
                {
                    var child = rigRoot.GetChild(i);
                    if (child.name.Equals("Locomotion", StringComparison.OrdinalIgnoreCase))
                        return child.gameObject;
                }
            }

            var tp = UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>();
            if (tp != null && tp.transform.parent != null && tp.transform.parent.name.Equals("Locomotion", StringComparison.OrdinalIgnoreCase))
            {
                return tp.transform.parent.gameObject;
            }

            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var locomotion = allTransforms.FirstOrDefault(t => t.name.Equals("Locomotion", StringComparison.OrdinalIgnoreCase));
            if (locomotion != null)
                return locomotion.gameObject;

            GameObject newLoco = new GameObject("Locomotion");
            Undo.RegisterCreatedObjectUndo(newLoco, "Create Locomotion");
            if (rigRoot != null)
            {
                newLoco.transform.SetParent(rigRoot, false);
            }
            return newLoco;
        }

        private static GameObject ResolveGestureTeleportationProvider(GameObject locomotionObj)
        {
            var existing = locomotionObj.transform.Find("Gesture Teleportation Provider");
            if (existing != null)
                return existing.gameObject;

            GameObject providerObj = new GameObject("Gesture Teleportation Provider");
            Undo.RegisterCreatedObjectUndo(providerObj, "Create Gesture Teleportation Provider");
            providerObj.transform.SetParent(locomotionObj.transform, false);
            providerObj.transform.localPosition = Vector3.zero;
            providerObj.transform.localRotation = Quaternion.identity;
            return providerObj;
        }

        private static FingerGunTeleportGesture ConfigureGesture(GameObject targetHand, bool isLeft)
        {
            var gesture = targetHand.GetComponent<FingerGunTeleportGesture>();
            if (gesture == null)
                gesture = Undo.AddComponent<FingerGunTeleportGesture>(targetHand);

            SerializedObject gestureSO = new SerializedObject(gesture);
            gestureSO.FindProperty("m_Handedness").enumValueIndex = isLeft ? (int)Handedness.Left : (int)Handedness.Right;
            gestureSO.FindProperty("m_IndexStraightness").floatValue = 0.86f;
            gestureSO.FindProperty("m_CurlStrictness").floatValue = 0.80f;
            gestureSO.FindProperty("m_RequireThumbCockedToAim").boolValue = true;
            gestureSO.FindProperty("m_ThumbFireDistance").floatValue = 0.040f;
            gestureSO.ApplyModifiedProperties();

            return gesture;
        }

        private static GameObject ResolveStabilizedOrigin(GameObject targetHand, bool isLeft)
        {
            string originName = isLeft ? "Left Hand Stabilized Origin" : "Right Hand Stabilized Origin";
            Transform existing = targetHand.transform.Find(originName);
            if (existing != null)
                return existing.gameObject;

            GameObject stabObj = new GameObject(originName);
            Undo.RegisterCreatedObjectUndo(stabObj, "Create Stabilized Origin");
            stabObj.transform.SetParent(targetHand.transform, false);
            stabObj.transform.localPosition = Vector3.zero;
            stabObj.transform.localRotation = Quaternion.identity;
            return stabObj;
        }

        private static void ConfigureStabilizer(GameObject stabObj, Transform pokeOrigin, XRRayInteractor rayInteractor)
        {
            var stabilizer = stabObj.GetComponent<XRTransformStabilizer>();
            if (stabilizer == null)
                stabilizer = Undo.AddComponent<XRTransformStabilizer>(stabObj);

            stabilizer.targetTransform = pokeOrigin;
            stabilizer.aimTarget = rayInteractor;
            stabilizer.useLocalSpace = false;
            stabilizer.angleStabilization = 20f;
            stabilizer.positionStabilization = 0.25f;

            SerializedObject stabSO = new SerializedObject(stabilizer);
            var targetProp = stabSO.FindProperty("m_Target");
            if (targetProp != null) targetProp.objectReferenceValue = pokeOrigin;
            var aimProp = stabSO.FindProperty("m_AimTargetObject");
            if (aimProp != null) aimProp.objectReferenceValue = rayInteractor;
            var localProp = stabSO.FindProperty("m_UseLocalSpace");
            if (localProp != null) localProp.boolValue = false;
            var angleProp = stabSO.FindProperty("m_AngleStabilization");
            if (angleProp != null) angleProp.floatValue = 20f;
            var posProp = stabSO.FindProperty("m_PositionStabilization");
            if (posProp != null) posProp.floatValue = 0.25f;
            stabSO.ApplyModifiedProperties();
        }

        private static XRRayInteractor ResolveTeleportInteractor(GameObject targetHand, Transform stabilizedOrigin)
        {
            var legacyRay = targetHand.transform.Find("Finger Gun Teleport Ray");
            if (legacyRay != null)
            {
                Undo.DestroyObjectImmediate(legacyRay.gameObject);
            }

            var existing = targetHand.transform.Find("Finger Gun Teleport Interactor");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject rayObj = new GameObject("Finger Gun Teleport Interactor");
            Undo.RegisterCreatedObjectUndo(rayObj, "Create Teleport Interactor");
            rayObj.transform.SetParent(targetHand.transform, false);
            rayObj.transform.localPosition = Vector3.zero;
            rayObj.transform.localRotation = Quaternion.identity;

            var rayInteractor = rayObj.AddComponent<XRRayInteractor>();
            rayInteractor.lineType = XRRayInteractor.LineType.ProjectileCurve;
            rayInteractor.velocity = 16f;
            rayInteractor.acceleration = 9.8f;
            rayInteractor.additionalFlightTime = 0.5f;
            rayInteractor.sampleFrequency = 50;
            rayInteractor.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            rayInteractor.sphereCastRadius = 0.05f;
            rayInteractor.maxRaycastDistance = 30f;
            rayInteractor.blendVisualLinePoints = true;
            rayInteractor.enableUIInteraction = false;
            rayInteractor.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
            rayInteractor.hitClosestOnly = true;
            rayInteractor.raycastMask = ~0;

            rayInteractor.rayOriginTransform = stabilizedOrigin;
            rayInteractor.attachTransform = stabilizedOrigin;

            var manager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            if (manager != null)
            {
                rayInteractor.interactionManager = manager;
            }

            int targetLayers = 0;
            int teleportMask = InteractionLayerMask.GetMask("Teleport");
            if (teleportMask != 0)
                targetLayers |= teleportMask;
            else
                targetLayers |= (1 << 31);

            var sceneTeleportables = UnityEngine.Object.FindObjectsByType<BaseTeleportationInteractable>(FindObjectsInactive.Include);
            foreach (var teleportable in sceneTeleportables)
            {
                targetLayers |= teleportable.interactionLayers.value;
            }

            rayInteractor.interactionLayers = targetLayers;

            var lineRenderer = rayObj.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = 0.02f;

            var blinkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/VRTemplateAssets/Materials/Locomotion/BlinkLine.mat");
            if (blinkMat != null)
            {
                lineRenderer.sharedMaterial = blinkMat;
            }
            else
            {
                var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader != null)
                {
                    var mat = new Material(unlitShader);
                    mat.color = new Color(0.125f, 0.588f, 0.953f, 0.8f);
                    lineRenderer.sharedMaterial = mat;
                }
            }

            var lineVisual = rayObj.AddComponent<XRInteractorLineVisual>();
            lineVisual.lineWidth = 0.02f;
            lineVisual.overrideInteractorLineLength = false;
            lineVisual.useDistanceToHitAsMaxLineLength = true;
            lineVisual.lineRetractionDelay = 0.25f;
            lineVisual.lineLengthChangeSpeed = 15f;
            lineVisual.smoothMovement = true;
            lineVisual.followTightness = 12f;
            lineVisual.snapThresholdDistance = 1.5f;
            lineVisual.stopLineAtFirstRaycastHit = true;
            lineVisual.snapEndpointIfAvailable = true;
            lineVisual.lineBendRatio = 0.5f;

            var reticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Teleport/Directional Teleport Reticle.prefab");
            if (reticlePrefab != null)
                lineVisual.reticle = reticlePrefab;

            var blockedReticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Teleport/Blocking Teleport Reticle.prefab");
            if (blockedReticlePrefab != null)
                lineVisual.blockedReticle = blockedReticlePrefab;

            Gradient validGrad = new Gradient();
            validGrad.SetKeys(
                new[] { new GradientColorKey(new Color(0.125f, 0.588f, 0.953f), 0f), new GradientColorKey(new Color(0.125f, 0.588f, 0.953f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 1f) }
            );
            lineVisual.validColorGradient = validGrad;

            Gradient invalidGrad = new Gradient();
            invalidGrad.SetKeys(
                new[] { new GradientColorKey(new Color(1.0f, 0.15f, 0.15f), 0f), new GradientColorKey(new Color(1.0f, 0.15f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.5f, 1f) }
            );
            lineVisual.invalidColorGradient = invalidGrad;

            Gradient blockedGrad = new Gradient();
            blockedGrad.SetKeys(
                new[] { new GradientColorKey(new Color(1.0f, 0.85f, 0.05f), 0f), new GradientColorKey(new Color(1.0f, 0.85f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 1f) }
            );
            lineVisual.blockedColorGradient = blockedGrad;

            rayObj.SetActive(false);

            return rayInteractor;
        }

        private static void ConfigureDriver(GameObject providerObj, FingerGunTeleportGesture gesture, XRRayInteractor rayInteractor, Transform stabOrigin, GameObject locomotionObj)
        {
            var driver = providerObj.GetComponent<GestureTeleportDriver>();
            if (driver == null)
                driver = Undo.AddComponent<GestureTeleportDriver>(providerObj);

            SerializedObject driverSO = new SerializedObject(driver);
            var gestureProp = driverSO.FindProperty("m_Gesture");
            if (gestureProp != null) gestureProp.objectReferenceValue = gesture;

            var rayProp = driverSO.FindProperty("m_RayInteractor");
            if (rayProp != null) rayProp.objectReferenceValue = rayInteractor;

            var originProp = driverSO.FindProperty("m_RayOrigin");
            if (originProp != null) originProp.objectReferenceValue = stabOrigin;

            var smoothProp = driverSO.FindProperty("m_SmoothRayOrigin");
            if (smoothProp != null) smoothProp.boolValue = false;

            var toggleProp = driverSO.FindProperty("m_ToggleInteractorObject");
            if (toggleProp != null) toggleProp.boolValue = true;

            var teleportProvider = locomotionObj.GetComponentInChildren<TeleportationProvider>(true) ?? UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>();
            if (teleportProvider != null)
            {
                var tpProp = driverSO.FindProperty("m_TeleportProvider");
                if (tpProp != null) tpProp.objectReferenceValue = teleportProvider;
            }

            driverSO.ApplyModifiedProperties();
        }

        private static GameObject ResolveTargetHand(GameObject contextObj)
        {
            if (contextObj != null && IsLeftHand(contextObj))
                return contextObj;

            if (contextObj != null && IsRightHand(contextObj))
                return contextObj;

            if (contextObj != null)
            {
                var rightChild = contextObj.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => IsRightHand(t.gameObject));
                if (rightChild != null)
                    return rightChild.gameObject;
            }

            var foundRight = FindRightHandInScene();
            if (foundRight != null)
                return foundRight;

            var foundLeft = FindLeftHandInScene();
            if (foundLeft != null)
                return foundLeft;

            return null;
        }

        private static bool IsRightHand(GameObject go)
        {
            if (go == null) return false;
            string n = go.name.ToLowerInvariant();
            return (n == "right hand" || n == "righthand" || n.EndsWith("right hand") || n.StartsWith("right hand"))
                   && !n.Contains("affordance") && !n.Contains("visual");
        }

        private static bool IsLeftHand(GameObject go)
        {
            if (go == null) return false;
            string n = go.name.ToLowerInvariant();
            return (n == "left hand" || n == "lefthand" || n.EndsWith("left hand") || n.StartsWith("left hand"))
                   && !n.Contains("affordance") && !n.Contains("visual");
        }

        private static GameObject FindRightHandInScene()
        {
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var match = all.FirstOrDefault(t => IsRightHand(t.gameObject));
            if (match != null) return match.gameObject;

            var pokes = UnityEngine.Object.FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include);
            foreach (var poke in pokes)
            {
                if (poke.handedness == InteractorHandedness.Right && poke.transform.parent != null)
                    return poke.transform.parent.gameObject;
            }

            return null;
        }

        private static GameObject FindLeftHandInScene()
        {
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var match = all.FirstOrDefault(t => IsLeftHand(t.gameObject));
            if (match != null) return match.gameObject;
            return null;
        }

        private static Transform FindPokeOrigin(GameObject hand)
        {
            var pokeComponent = hand.GetComponentInChildren<XRPokeInteractor>(true);
            if (pokeComponent != null && pokeComponent.name.IndexOf("pinch", StringComparison.OrdinalIgnoreCase) < 0)
                return pokeComponent.transform;

            var children = hand.GetComponentsInChildren<Transform>(true);

            var exact = children.FirstOrDefault(t => 
                (t.name.Equals("Poke Interactor", StringComparison.OrdinalIgnoreCase) ||
                 t.name.Equals("Poke Point", StringComparison.OrdinalIgnoreCase)) &&
                t.name.IndexOf("pinch", StringComparison.OrdinalIgnoreCase) < 0);
            if (exact != null)
                return exact;

            var containsPoke = children.FirstOrDefault(t => 
                t.name.IndexOf("poke", StringComparison.OrdinalIgnoreCase) >= 0 &&
                t.name.IndexOf("pinch", StringComparison.OrdinalIgnoreCase) < 0);
            if (containsPoke != null)
                return containsPoke;

            return null;
        }

        private static void CleanUpMisplacedInstances(GameObject targetHand, GameObject providerObj)
        {
            var allDrivers = UnityEngine.Object.FindObjectsByType<GestureTeleportDriver>(FindObjectsInactive.Include);
            foreach (var d in allDrivers)
            {
                if (d.gameObject != providerObj)
                {
                    Debug.Log($"[Studio XR] Cleaning up misplaced GestureTeleportDriver from '{d.gameObject.name}'.");
                    Undo.DestroyObjectImmediate(d);
                }
            }

            var allGestures = UnityEngine.Object.FindObjectsByType<FingerGunTeleportGesture>(FindObjectsInactive.Include);
            foreach (var g in allGestures)
            {
                if (g.gameObject != targetHand)
                {
                    Debug.Log($"[Studio XR] Cleaning up misplaced FingerGunTeleportGesture from '{g.gameObject.name}'.");
                    Undo.DestroyObjectImmediate(g);
                }
            }

            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            foreach (var t in allTransforms)
            {
                if (t != null && t.name == "Finger Gun Teleport Ray")
                {
                    Debug.Log($"[Studio XR] Cleaning up legacy 'Finger Gun Teleport Ray' from '{t.parent?.name}'.");
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }

            foreach (var t in allTransforms)
            {
                if (t != null && t.name == "Finger Gun Teleport Interactor" && t.parent != targetHand.transform)
                {
                    Debug.Log($"[Studio XR] Cleaning up misplaced 'Finger Gun Teleport Interactor' from '{t.parent?.name}'.");
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }

            foreach (var t in allTransforms)
            {
                if (t != null && (t.name == "Right Hand Stabilized Origin" || t.name == "Left Hand Stabilized Origin") && t.parent != targetHand.transform)
                {
                    Debug.Log($"[Studio XR] Cleaning up misplaced '{t.name}' from '{t.parent?.name}'.");
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }
        }
    }
}
