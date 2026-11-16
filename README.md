# unity-xr-template

The stu/dio's Unity XRI template, meant to work alongside Unity's pre-existing XRI Starter Pack.

Developed by the **Game Studies and Design stu/dio (GSD stu/dio)** at the University of Illinois Urbana-Champaign (UIUC).

---

## General Layout

* **`XR Affordances/`:** Reusable XR interaction guidance.
* **`XR Controller Extensions/`:** Optional XR interaction and locomotion extensions.
* **`XR Editor Utilities/`:** Unity Editor tools and custom inspectors for configuring XR toolkit features.
* **`XR Prefabs/`:** Ready-to-use XR interaction prefabs.
* **`Scenes/`:** Example and template scenes.
* **`Template Assets/`:** Supporting environment assets and materials for building a sample XR experience.

---

## Major Features

### 1. XR Affordances

#### Ghost Hands (`HandIndicator.cs`)
Contextual holographic hand previews indicate how a player should interact with something.

* **Affordance Modes:**
  * **Focused (`showWhenFocused`):** Hand fades in when the interactable is hovered or focused by an interactor (direct hand or ray).
  * **Gazed (`showWhenGazed`):** Hand fades in when the player's head looks directly at the object within a configurable angle threshold (`gazeFacingThreshold`).
  * **In Proximity (`showWhenInProximity`):** Hand fades in smoothly based on player distance (`proximityThreshold`).
* **Release Cooldown Protection (`releaseCooldown`):** Automatically hides upon grab and pauses for a configurable duration (default `0.5s`) upon release so the ghost hand doesn't snap back inside the player's physical hand.

#### Ghost Footsteps (`GhostFootsteps.cs`)
Ground-level footstep trails projected along terrain splines to guide player navigation.
* **Affordance Modes:**
  * **`Static`:** Continuous looping ambient trail.
  * **`Reactive`:** Activates contextual guidance when the player gazes toward or approaches the path start point.
  * **`Dynamic`:** Generates on-the-fly wayfinding paths to arbitrary target transforms using static `NavMesh.CalculatePath()`.

---

### 2. XR Controller Extensions

#### "Finger Gun" XR Hand Teleporting
A natural, tactile hand-tracking locomotion method for optical hand tracking (Quest Link / PCVR OpenXR).
* **Gesture Recognition (`FingerGunTeleportGesture.cs`):**
  * Uses `XRHandSubsystem` to detect an extended index finger, curled middle/ring/pinky fingers, and thumb cock/fire motions.
  * Thumb hammer transitions trigger the teleport action upon release/flick.
* **Stabilized Aiming:** Uses native XRI `XRTransformStabilizer` anchored to the index poke point (`targetTransform = pokeOrigin`), preventing natural hand tremor from destabilizing distant raycast targets.
* **Three-State Visual Feedback:**
  * **Cyan (Valid):** Aiming at an active `BaseTeleportationInteractable` within slope tolerance. Displays directional reticle aligned with the finger barrel.
  * **Red (Invalid Obstacle):** Aiming at non-teleport geometry (props, walls). Ray stops cleanly at the obstacle (`stopLineAtFirstRaycastHit = true`) with no reticle.
  * **Yellow (Blocked Slope):** Aiming at teleport surfaces exceeding `upNormalToleranceDegrees`. Displays blocking reticle and rejects teleportation requests.
* **Strict Raycast Detection:** Uses `HitDetectionType.Raycast` (never `SphereCast`) to eliminate the known XRI `TeleportationArea` bug where zero-distance sphere hits trap rays in a permanent yellow/blocked state.
* **One-Click Setup (`FingerGunSetupTool.cs`):** Run `Studio XR > Setup Finger Gun System` from the menu bar to configure the full rig, locomotion provider, stabilizer, and interactors automatically.

#### Dynamic Climb Grab (`StudioClimbGrabInteractable.cs`)
Modular climbing locomotion extension built directly on `XRGrabInteractable`.
* **Dynamic Attach Points:** Uses dynamic grab point attachment and collider snapping so players can climb bars, ladders, and surfaces using controllers or natural optical hand tracking.
* **Locomotion Coordination:** Automatically locks `GravityProvider` while climbing, suppresses competing locomotion providers (walk, teleport, turn), and drives the player's `CharacterController` / `XROrigin`.
* **Modular Subclass:** Inherits from `XRGrabInteractable` cleanly without modifying core XRI package code, ensuring 100% compatibility when imported into any project.

### 3. Unity Editor Utilities

#### Affordance Setup Tools
Editor utilities to improve the experience of implementing affordances into an XR level.
* **Finger Gun Setup (FingerGunSetupTool.cs):**
  * Adds or configures the gesture component, poke origin, stabilized origin, teleport ray interactor, and gesture teleportation provider for the selected hand.
* **Ghost Footsteps Setup (GhostFootstepsEditor.cs):**
  * Provides a custom Inspector for selecting guidance modes, configuring reactive or dynamic navigation settings, rebuilding static paths, and tuning visuals and animation.

---

## Getting Started

### System Requirements
* **Unity Version:** Unity 6 (6000.x LTS)
* **Render Pipeline:** Universal Render Pipeline (URP 17.5.0+)
* **Core Packages:**
  * `com.unity.xr.interaction.toolkit` (3.5.1+)
  * `com.unity.xr.hands` (1.8.1+)
  * `com.unity.xr.core-utils` (2.6.0+)
  * `com.unity.splines` (2.9.1+ embedded)
  * `com.unity.xr.openxr` & `com.unity.xr.meta-openxr`
* **Hardware:** Meta Quest 2 / 3 / 3S / Pro (via Quest Link, Air Link, or standalone OpenXR) or OpenXR-compliant PCVR headsets with motion controllers and optical hand tracking.

---

## How-To Guides

### 1. Importing the Unity XRI Toolkit Assets into an Existing Project

1. Open the existing project in Unity 6.
2. In **Window > Package Manager**, install or verify these packages:
   * **XR Interaction Toolkit** (`3.5.1` or later)
   * **XR Hands** (`1.8.1` or later)
   * **XR Core Utilities** (`2.6.0` or later)
   * **Splines** (`2.9.1` or later)
   * **OpenXR Plugin** and **Meta OpenXR**
3. In the XR Interaction Toolkit package details, open **Samples** and import **Starter Assets**.
4. Let Unity finish compiling, then resolve any input actions or scene references before using the toolkit components.

### 2. Installing the XRI Character Controller Simulator

1. Open **Window > Package Manager** and select the **XR Interaction Toolkit** package.
2. In the package's **Samples** section, click **Import** next to **XR Interaction Simulator**.
3. In the Project window, open `Assets/Samples/XR Interaction Toolkit/<version>/XR Interaction Simulator/`.
4. Add the **XR Interaction Simulator** prefab to any scene where you want to test XR input with a keyboard and mouse.
5. Make sure the scene also contains an `XR Origin` prefab from the imported **Starter Assets** sample.
6. Enter Play mode and use the simulator's on-screen key bindings to test headset, controller, and hand interactions.

Remove the simulator prefab before building for an XR device, or enable **Use XR Interaction Simulator in scenes** under **Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit** for Editor-only automatic setup.

### 3. Adding a Ghost Hand Affordance to an Interactable

1. Locate `Left Hand Affordance.prefab` in `XR Affordances/Hand Indicator/`.
2. Drag the prefab into your scene as a child (or sibling) of your interactable object (e.g. a valve, handle, or tool).
3. Orient and position the ghost hand to show the desired grasp posture.
4. On the `HandIndicator` component:
   * **Interactable:** Leave blank to auto-detect the nearest `XRBaseInteractable`, or manually assign.
   * **Conditional Fade:** Check `Show When Focused`, `Show When Gazed`, and/or `Show When In Proximity`.
   * **Thresholds:** Adjust `Proximity Threshold` (default `1.5m`) and `Gaze Facing Threshold` (default `0.85` dot product).

### 4. Setting Up Finger Gun Teleportation (1-Click)

1. Open your scene with an `XR Origin (XR Rig)` containing hand tracking.
2. In the Unity menu, select:
   ```text
   Studio XR > Setup Finger Gun System
   ```
   *(or `Tools > Studio XR > Setup Finger Gun System`)*
3. The tool automatically:
   * Locates `Right Hand` (or selected hand) and attaches `FingerGunTeleportGesture`.
   * Locates or adds the index `Poke Interactor`.
   * Creates `Right Hand Stabilized Origin` with `XRTransformStabilizer`.
   * Configures `Finger Gun Teleport Interactor` (`XRRayInteractor` with strict raycast settings and teleport layers).
   * Creates `Gesture Teleportation Provider` under `Locomotion` with `GestureTeleportDriver`.

### 5. Adding Spline Wayfinding Footsteps

1. Create an empty GameObject and add a `SplineContainer` component (`GameObject > Spline > Draw Spline`).
2. Add the `GhostFootsteps` component to the same GameObject.
3. Select your **Footstep Mode**:
   * **`Static`:** Ambient looping path.
   * **`Reactive`:** Wayfinding trail that activates when the player gazes toward or approaches the start point.
   * **`Dynamic`:** Real-time pathfinding to an assigned destination transform using Unity NavMesh.
4. Tune **Step Count**, **Step Width**, and **Loop Duration** in the inspector.

---

## Contributing & Credits

* **Managing Entity:** Game Studies and Design stu/dio (GSD stu/dio)
* **Institution:** University of Illinois Urbana-Champaign (UIUC)
* **Repository:** [https://github.com/the-stu-dio/unity-xr-template](https://github.com/the-stu-dio/unity-xr-template)
