# S3 · Boundaryless mixed reality on Quest

**Question.** Can an ImmersiveX app run with passthrough and no boundary prompt, so the user never draws a play area?

**Answer.** Yes. Unity OpenXR: Meta 2.3.2 has a **Meta Quest: Boundary Visibility** OpenXR feature.

## Findings
- With the feature's **Suppress Visibility** setting on, the boundary is hidden from startup for the lifetime of the OpenXR loader.
- Suppression only works **while passthrough is rendered**. ImmersiveX turns on see-through (F1) before the space step, so the order in the session flow is already right.
- `BoundaryVisibilityFeature` also has a runtime API with change notifications, for apps that turn passthrough on later.
- The package adds `com.oculus.permission.BOUNDARY_VISIBILITY` to the manifest automatically.
- The Quest adapter's build configurator enables the feature. It **keeps Suppress Visibility off**: on Quest 3 (Horizon OS v207) the automatic request fires at XR start, before the session exists, and fails with `XR_ERROR_HANDLE_INVALID` (seen on device, 2026-09-24). ImmersiveX calls `TryRequestBoundaryVisibility` itself once passthrough is showing (`MetaQuestBoundary`).

## Evidence
- `Documentation~/features/boundary-visibility.md`
- `Runtime/Features/BoundaryVisibility/BoundaryVisibilityFeature.cs`
- `Editor/ModifyAndroidManifest.cs` (`BOUNDARY_VISIBILITY`)

## Still open
- **Device check (M1):** launch on a Quest 3 with passthrough on and confirm that no boundary prompt or "return to your boundary" warning appears while walking around the room.
