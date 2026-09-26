# S4 · Testing in the editor without a headset

**Question.** Can developers run the ImmersiveX flow (see-through, room mapping, interaction, persistence) in Play mode without a device?

**Answer.** Mostly. AR Foundation's **XR Simulation** and XRI's **XR Interaction Simulator** cover everything except native anchor persistence.

## Findings
- AR Foundation 6.3.5 includes XR Simulation. It simulates the camera, planes, bounding boxes, meshing, raycasts, occlusion, image tracking and anchors inside a simulated room environment.
- **Simulated anchors can't be saved:** `SimulationAnchorSubsystem` reports `supportsSaveAnchor`, `supportsLoadAnchor`, `supportsEraseAnchor` and `supportsGetSavedAnchorIds` as `false`. In the editor, persistence therefore runs ImmersiveX's **room-relative fallback**. That is useful: it tests the fallback path on every Play. Native persistence is covered by device checks (S2).
- XRI 3.3.2 ships an **XR Interaction Simulator** sample (keyboard and mouse drive a simulated headset, controllers and hands). There are also Hands Interaction Demo, AR Starter Assets, World Space UI, Spatial Keyboard and visionOS samples.
- The Meta XR Simulator is optional and Quest-specific. ImmersiveX doesn't need it.

## Evidence
- `Runtime/Simulation/Subsystems/*` in AR Foundation 6.3.5, especially `Anchors/SimulationAnchorSubsystem.cs`
- `package.json` samples list in XR Interaction Toolkit 3.3.2

## What this means for the plan
- M1: the built-in Simulation adapter enables the XR Simulation loader for the editor, and its capabilities declare `AnchorPersistence = RoomRelative`.
- M2: room-mapping PlayMode tests run against XR Simulation environments.
