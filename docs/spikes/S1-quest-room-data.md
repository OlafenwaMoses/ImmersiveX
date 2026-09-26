# S1 · Quest room data and Space Setup

**Question.** Can the Quest adapter trigger Space Setup and read the room (walls, floor, furniture, mesh) using only Unity OpenXR: Meta, or do we need the Meta XR Core SDK or MR Utility Kit?

**Answer.** Unity OpenXR: Meta 2.3.2 covers it. [ADR-0007](../adr/0007-quest-on-unity-openxr-meta.md) is accepted on this basis.

## Findings
- Quest doesn't detect planes live. AR Foundation's plane, bounding-box and meshing subsystems return the **Scene Model** that Space Setup saved. It persists across apps and sessions.
- The app can start Space Setup ("scene capture") itself with `MetaOpenXRSessionSubsystem.TryRequestSceneCapture()`.
- Scene capture pauses the app: Unity calls `OnApplicationPause(true)`, the user completes Space Setup, then `OnApplicationPause(false)`. The session flow must treat this as a normal round trip, not as the user leaving.
- If Space Setup was never completed, planes, bounding boxes and meshes come back empty. ImmersiveX treats that as "room not known" and starts the guided flow.
- On Quest 3 / 3S, Space Setup scans automatically as the user looks around. That matches the brief's "look around slowly" step, so ImmersiveX wraps it with its own briefing and summary screens.
- The package adds `com.oculus.permission.USE_SCENE` to the Android manifest when plane, bounding-box or meshing features are enabled. It is a runtime permission, so the app must request it before reading room data.

## Evidence
- `Documentation~/features/session.md`, section "Scene capture" (life cycle and code sample)
- `Runtime/Subsystems/Session/MetaOpenXRSessionSubsystem.cs` (`TryRequestSceneCapture`)
- `Documentation~/features/bounding-boxes.md` and `meshing.md`, section "Space Setup"
- `Editor/ModifyAndroidManifest.cs` (`USE_SCENE`)

## Still open
- **Device check (M1):** call `TryRequestSceneCapture` on a Quest 3, confirm the pause/resume round trip, and confirm planes and bounding boxes arrive afterwards.
- **For M2:** check whether Scene Model trackable IDs stay the same across sessions. If they do, they become the native "same room" key in `ISpaceProvider.TryRecognizeAsync`.
