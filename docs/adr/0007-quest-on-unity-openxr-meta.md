# 0007. Build the Quest adapter on Unity OpenXR: Meta

- **Status:** Accepted
- **Date:** 2026-09-24

## Context
Quest support can come from Unity's AR Foundation provider (`com.unity.xr.meta-openxr`) or from Meta's own SDKs (Meta XR Core SDK, MR Utility Kit).
Spike S1 had to find out whether the Unity package alone covers room data and the Space Setup trigger.

## Decision
Use **Unity OpenXR: Meta 2.3.2** (with OpenXR 1.18.0) as the Quest provider. Don't add the Meta XR Core SDK or MRUK for M1–M3.
Spike S1 found everything needed in the Unity package:
- `MetaOpenXRSessionSubsystem.TryRequestSceneCapture()` starts Space Setup from inside the app.
- Planes, bounding boxes and meshes come from the Space Setup Scene Model.
- Persistent anchors use AR Foundation's `TrySaveAnchorAsync` / `TryLoadAnchorAsync` / `TryEraseAnchorAsync`, including batch versions.
- The Boundary Visibility feature can suppress the boundary while passthrough is showing.

## Consequences
- Quest stays inside the shared AR Foundation abstraction; no Meta-specific types reach core.
- Scene capture pauses the app (`OnApplicationPause(true)` → `false`), so the session flow must survive that round trip.
- A Meta SDK can still be added later behind the adapter if a feature needs it; that would be a new ADR.
- Passthrough is drawn through Unity's XR Composition Layers. The **Meta Quest feature set** lists **Composition Layers Support** as a required feature, so the configurator enables the whole feature set (as the Project Settings checkbox does), not individual features. The first device run missed this and showed no passthrough (2026-09-26).
- This differs from VizionEnterprise-MixedReality, which draws passthrough with Meta's SDK (`OVRManager` + `OVRPassthroughLayer`).
- Device confirmation is part of the M1 device check.
