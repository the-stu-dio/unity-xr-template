# unity-xr-template

The stu/dio's Unity XRI template, meant to work alongside Unity's pre-existing XRI Starter Pack.

Developed by the **Game Studies and Design stu/dio (GSD stu/dio)** at the University of Illinois Urbana-Champaign (UIUC).

---

## Preamble

The goal of this template is to help YOU get launched into the development of a VR application quickly and frictionlessly. It provides pre-built components for various XR interactions, and sits on top of what Unity already provides.

One thing to note - as you are developing the VR features necessary for your game, in most cases, Unity expects you to *extend* their systems, rather than providing out-of-the-box solutions. That being said, Unity's XRI framework is, in places, spotty and inconsistent. Major systems are deprecated, and some systems are outright broken. This template accepts that, and attempts to push through these barriers. In several cases, I have vibecoded my way through them.

My sincere apologies for any code/documentation that has been obscured/made unclear by the use of agentic AI. **Please exercise your own judgement as you put weight on these systems.** If you are looking for robustness, err on the side of writing (or rewriting) code where you see limitations, rather than accepting what is on the page... However! I have done my best to make the provided assets as plug-and-play as possible.

As a final note, I have provided both a generic and situated template scene in this toolkit. Please, take a moment to play through the experience before you start your project. As you do, reflect on which systems, interactions, or areas were most impactful on you as a player. Specifically, try to examine the areas that *do not have any additional XR features* but are still impactful. This is where you can make a real impact on a player. You do not need much to make VR impressive, innovative, or transformative. If you are slamming your head against a wall trying to develop a complicated system, consider how you could achieve the same outcome with a little bit of clever design.

As Dan Cermak, the stu-dio's Director, taught me, in game development, where you *can* cheat, you *must* cheat. If something works, regardless of how elegant it is, that is much, much better than having a half-baked version of the most perfect infrastructure known to man. Before you spend time, compute, or brain power on something, *try to find out if someone has already done it* whether that is on Github, the Unity Asset Store, or even in another project that the stu-dio has worked on. And if you find something, let me know, and I'll make sure it gets packaged in with the toolkit in the next version.

If you have any questions about the toolkit, or would like to contribute pre-existing assets to the stu-dio's repertoire, feel free to email me at davidah3@illinois.edu, and I'll get back to you as soon as possible.

\- David

---

## General Layout
---
All Template files live in the "stu-dio XR Template folder." Most actual XRI functionality is derived from Unity's XRI toolkit assets (which can be installed with the "One Click" installer in the Studio XR tab in the Editor.) Several tools from Unity's VRTemplate Starter Project have been included alongside the project, but the VRTemplate itself is not a dependency. If you would like to utilize more of Unity's template's features, you, of course, can start a project with the Template before installing the stu-dio Template.

* **`Scenes/`:** stu-dio Template and Generic Template scenes, for viewing an "in-context" example vs. generic asset usage
* **`XR Affordances/`:** In-world indicators for desired XR interaction
* **`XR Controller Extensions/`:** Optional XR interaction and locomotion extensions
* **`XR Editor Utilities/`:** Unity Editor tools and custom inspectors for configuring toolkit features
* **`XR Prefabs/`:** Ready-to-use XR interaction prefabs, including a stu-dio XR Rig
* **`XR Assets/`:** Useful scripts, prefabs, and assets/dependencies for XR Prefabs (trim assets that are unneeded for your project!)
* **`Template Assets/`:** Supporting environment assets and materials for our "in-context" VR example experience

---

## Major Features
---
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

### 3. XRI Interactables

#### Dynamic Climb Grab (`StudioClimbGrabInteractable.cs`)
Modular climbing locomotion extension built directly on `XRGrabInteractable`.
* **Dynamic Attach Points:** Uses dynamic grab point attachment and collider snapping so players can climb bars, ladders, and surfaces using controllers or natural optical hand tracking.
* **Locomotion Coordination:** Automatically locks `GravityProvider` while climbing, suppresses competing locomotion providers (walk, teleport, turn), and drives the player's `CharacterController` / `XROrigin`.
* **Modular Subclass:** Inherits from `XRGrabInteractable` cleanly without modifying core XRI package code, ensuring 100% compatibility when imported into any project.

#### Pluckable Interactables (`XRPluckInteractable.cs`)
Converts rooted or anchored props (flowers, mushrooms, tools) into dynamic physics bodies once grabbed.
* **Elastic Stem Wobble (`m_AllowWobble`):** Allows anchored objects to tilt and flex elastically when pushed by hands or colliders before plucking, restoring upright via a configurable spring-damper system (`m_SpringStiffness`, `m_Damper`) clamped to a max angle (`m_MaxTiltAngle`).
* **Automatic Base Pivot Alignment:** Detects the bottom of colliders (or accepts a custom `m_StemPivot`) to bend naturally from the root rather than rotating about its geometric center.
* **Pluck Transition:** Unfreezes constraints, restores gravity, and disables the script component upon grab (`selectEntered`) for zero runtime overhead once plucked.
* **Feedback & Hooks:** Delivers haptic pulses (`m_HapticIntensity`), audio cues (`m_PluckSound`), and fires `m_OnPlucked` UnityEvents for inventory or gameplay scripting.

#### Maritime Telescope & Spyglass View (`TelescopeView.cs`)
A fully-featured maritime spyglass featuring physical segment extension, dual-threshold eye proximity scoping, and stable gaze-driven optics.
* **Physical Segment Extension:** Holding the activate button smoothly expands telescope segments along the barrel axis; requires reaching a configurable extension threshold (`m_MinExtensionForScope`) before scope mode can engage.
* **Proximity Hysteresis Scoping:** Engages scope view when the eyepiece reaches the headset (`m_EyeEnterDistance = 0.18m`) and exits cleanly when pulled away (`m_EyeExitDistance = 0.26m`), preventing jitter near the face.
* **Gaze-Dictated Aiming (`m_GazeDictatedAim`):** Magnified scope camera locks rotation to the player's head gaze rather than hand tremors, eliminating VR motion sickness and micro-jitter.
* **Procedural Overlay & Model Fading:** Renders a 1024x1024 circular viewport with an antique brass rim and peripheral vignette while smoothly fading out the 3D telescope mesh and hiding controller/hand visuals to prevent face clipping.

### 4. Unity Editor Utilities

#### Affordance Setup Tools
Editor utilities to improve the experience of implementing affordances into an XR level.
* **Finger Gun Setup (FingerGunSetupTool.cs):**
  * Adds or configures the gesture component, poke origin, stabilized origin, teleport ray interactor, and gesture teleportation provider for the selected hand.
* **Ghost Footsteps Setup (GhostFootstepsEditor.cs):**
  * Provides a custom Inspector for selecting guidance modes, configuring reactive or dynamic navigation settings, rebuilding static paths, and tuning visuals and animation.

### 5. XR Package Installer
An Editor utility for quickly configuring new or existing Unity projects for the toolkit.
* **One-Click Setup:** Installs or updates required Unity packages, configures the `Teleport` interaction layer, imports XRI Starter Assets and TextMesh Pro essentials, and refreshes package dependencies.
* **Meta XR Support:** Optionally configures Meta's package registry and installs the Meta XR SDK Core package.
* **Compatibility Checks:** Detects missing packages and starter assets, reports setup status in the Editor, and prompts to open the setup utility when the project is opened.

---
## Getting Started
---
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

### Installation
Choose your preferred installation method:

* **Option A: In-Editor Setup (Self-Healing Assembly Isolation)**
  1. Copy or clone the `stu-dio XR Template` folder into your project's `Assets/` folder.
  2. Open your project in Unity 6.
  3. Thanks to isolated Assembly Definitions (`.asmdef`) and `defineConstraints`, `StudioXR.Installer.Editor` compiles independently without errors even if packages are missing. Unity will automatically prompt you on launch to run the 1-Click Setup, or you can select **Studio XR > Project Setup & Package Installer** from the top menu.
  4. Click **Run One-Click Package Setup (Recommended)**.

* **Option B: Pre-Flight Command-Line Setup (Zero Compile Wait)**
  1. In terminal or PowerShell, run:
     ```powershell
     ./Setup-StudioXR.ps1
     ```
  2. Opens and configures `Packages/manifest.json` with all required dependencies and the Meta scoped registry before launch.

* **Option C: UPM Package Distribution (`package.json`)**
  1. In `Packages/manifest.json` (or via Unity Package Manager ➔ *Add package from disk/git*), reference the package:
     ```json
     "com.thestudioatuiuc.studioxritoolkit": "file:Assets/stu-dio XR Template"
     ```
  2. Unity automatically resolves and imports all dependencies declared in `package.json` before compiling. 

#### "How do I add VR to my project?"
You can configure all required XR packages, interaction layers, and template assets automatically using the built-in One-Click utility:

1. Copy or clone the `stu-dio XR Template` folder into your project's `Assets/` folder.
2. Open your project in Unity 6.
3. In the top menu bar, select:
   ```text
   Studio XR > Project Setup & Package Installer
   ```
4. Click **Run One-Click Package Setup (Recommended)**.
   * Automatically adds missing UPM packages (`XR Interaction Toolkit`, `XR Hands`, `Splines`, `OpenXR`, `Meta OpenXR`, etc.) to `Packages/manifest.json`.
   * Imports the XRI **Starter Assets** sample and **TextMesh Pro Essential Resources**.
   * Automatically configures the `Teleport` interaction layer (Layer 31) in `InteractionLayerSettings`.
5. *(Optional)* Click **Install Meta XR SDK** under the **Optional Platform SDKs** card (or select `Studio XR > Install Meta XR SDK (Optional)` from the top menu bar) to configure Meta's official package registry and install the Meta XR Core SDK (`com.meta.xr.sdk.core`).

#### "How do I add a VR player character to my scene?"
1. In the Project window, locate `Studio XR Rig.prefab` in `Assets/stu-dio XR Template/XR Prefabs/`.
2. Drag `Studio XR Rig` into your active scene hierarchy.
3. Delete or disable any default scene `Main Camera` to avoid duplicate camera or `AudioListener` conflicts.
4. Position and rotate the rig at your desired player spawn location.
   * *Pre-configured out of the box:* `XROrigin`, `CharacterController`, complete locomotion suite (smooth movement, snap/continuous turn, teleportation, climbing, gravity), near-far and poke interactors, and dual motion controller / optical hand tracking support.

#### "How do I test my project without a VR headset?"
1. Open **Window > Package Manager** and select the **XR Interaction Toolkit** package.
2. In the package's **Samples** section, click **Import** next to **XR Interaction Simulator**.
3. In the Project window, open `Assets/Samples/XR Interaction Toolkit/<version>/XR Interaction Simulator/`.
4. Add the **XR Interaction Simulator** prefab to any scene where you want to test XR input with a keyboard and mouse.
5. Make sure the scene also contains an `XR Origin` prefab from the imported **Starter Assets** sample.
6. Enter Play mode and use the simulator's on-screen key bindings to test headset, controller, and hand interactions.

Remove the simulator prefab before building for an XR device, or enable **Use XR Interaction Simulator in scenes** under **Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit** for Editor-only automatic setup.

#### "How do I add interaction indicators to my scene?"
1. Locate `Left Hand Affordance.prefab` in `XR Affordances/Hand Indicator/`.
2. Drag the prefab into your scene as a child (or sibling) of your interactable object (e.g. a valve, handle, or tool).
3. Orient and position the ghost hand to show the desired grasp posture.
4. On the `HandIndicator` component:
   * **Interactable:** Leave blank to auto-detect the nearest `XRBaseInteractable`, or manually assign.
   * **Conditional Fade:** Check `Show When Focused`, `Show When Gazed`, and/or `Show When In Proximity`.
   * **Thresholds:** Adjust `Proximity Threshold` (default `1.5m`) and `Gaze Facing Threshold` (default `0.85` dot product).

#### "How do I add footstep indicators to my scene?"
1. Create an empty GameObject and add a `SplineContainer` component (`GameObject > Spline > Draw Spline`).
2. Add the `GhostFootsteps` component to the same GameObject.
3. Select your **Footstep Mode**:
   * **`Static`:** Ambient looping path.
   * **`Reactive`:** Wayfinding trail that activates when the player gazes toward or approaches the start point.
   * **`Dynamic`:** Real-time pathfinding to an assigned destination transform using Unity NavMesh.
4. Tune **Step Count**, **Step Width**, and **Loop Duration** in the inspector.


#### "How do I move without my controllers?"
1. Open your scene with an `XR Origin (XR Rig)` containing hand tracking.
2. In the Unity menu, select:
   ```text
   Studio XR > Setup Finger Gun System
   ```
3. The tool automatically:
   * Locates `Right Hand` (or selected hand) and attaches `FingerGunTeleportGesture`.
   * Locates or adds the index `Poke Interactor`.
   * Creates `Right Hand Stabilized Origin` with `XRTransformStabilizer`.
   * Configures `Finger Gun Teleport Interactor` (`XRRayInteractor` with strict raycast settings and teleport layers).
   * Creates `Gesture Teleportation Provider` under `Locomotion` with `GestureTeleportDriver`.

#### "How do I make an object pluckable?"
1. Add a `Rigidbody` and an `XRGrabInteractable` to your object (e.g. flower, mushroom, or tool).
2. Add the `XRPluckInteractable` component to the same GameObject.
3. Configure the **Wobble Settings**:
   * Enable **Allow Wobble** if you want the object to bend and tilt realistically when nudged before being plucked.
   * Adjust **Max Tilt Angle** (e.g. `15°`) and **Spring Stiffness** / **Damper** to achieve the desired stem elasticity.
   * *(Optional)* Assign **Stem Pivot** to define the exact base rotation point; if left unassigned, it will automatically find the lowest point of the attached colliders.
4. *(Optional)* Assign a **Pluck Sound**, set **Haptic Intensity**, and hook up any custom gameplay events to **On Plucked**.

#### "How do I add a functional spyglass/telescope to my scene?"
1. Drag the `Telescope.prefab` from `Assets/stu-dio XR Template/XR Prefabs/` into your scene.
2. The prefab comes preconfigured with:
   * `XRGrabInteractable` configured for single or two-handed pickup.
   * `TelescopeView` with auto-detected segment transforms (`third_segment`, `second_segment`, `first_segment`), procedural circular scope overlay, and brass eyepiece rim.
   * Extension and collapse sound effects (`telescope extend.wav`).
3. To customize:
   * **Zoom Level:** Adjust **Zoom FOV** on `TelescopeView` (default `15°`; lower values increase magnification).
   * **Scope Proximity:** Tune **Eye Enter Distance** (default `0.18m`) and **Eye Exit Distance** (default `0.26m`) to adjust how close to your face the eyepiece must be to engage scope view.
   * **Aim Mode:** Keep **Gaze Dictated Aim** enabled for comfortable, jitter-free viewing, or disable it to aim directly along the physical telescope barrel.
4. In play mode, grab the telescope, hold the controller activate/trigger button to extend the segments, and bring the eyepiece up to your headset to look through the scope.

---

## Development Guidelines & Quirks (Here be GenAI...)

These are quirks that AI had trouble with while developing. They might be useful for debugging (or for your own agent's use.) When extending or modifying the toolkit, keep these architectural conventions in mind:

* **XRI Affordance Deprecation:** The XRI 3.x affordance classes (`XRInteractableAffordanceStateProvider`, `AffordanceStateShortcuts`) are marked `[Obsolete]` by Unity. We intentionally use them for reliability. Suppress compiler warnings in relevant scripts with:
  ```csharp
  #pragma warning disable CS0618
  ```
* **Multi-Material Hand Renderers:** The ghost hand uses a multi-material setup (`DepthOnly.mat` in slot 0, `Unity_Hand_Medium.mat` in slot 1). **Never assign `meshRenderer.material = ...`**, as Unity will wipe slot 0. Always clone and edit `meshRenderer.materials[1]`.
* **Teleport Hit Detection (Raycast vs. SphereCast):** Always keep `rayInteractor.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast`. Setting this to `SphereCast` triggers XRI's `IsSphereCastOverlap` check at distance 0, locking the ray in a permanent yellow/blocked state.
* **CLI Compilation Verification:** You can verify scripts compile without waiting for the Unity Editor to reload:
  ```powershell
  dotnet build "studio XR Template/Assembly-CSharp.csproj" --no-dependencies
  ```

---

## Contributing & Credits

* **Managing Entity:** Game Studies and Design stu/dio (GSD stu/dio)
* **Institution:** University of Illinois Urbana-Champaign (UIUC)
* **Repository:** [https://github.com/the-stu-dio/unity-xr-template](https://github.com/the-stu-dio/unity-xr-template)
* **Programming Lead:** David A. Hopping (davidah3@illinois.edu)

TBD (Credit for assets utilized in Template Scene - all free from the Asset Store, aside from one or two models)