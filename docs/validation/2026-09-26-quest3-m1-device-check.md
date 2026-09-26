# Quest 3 device check: M1 "Hello Hologram" (2026-09-26)

**Device:** Meta Quest 3, Horizon OS v207 (Android 14, API 34), OpenXR runtime Oculus 207.297.0
**Build:** `genxr.immersivex.app` 0.1.0, Unity 6000.3.23f1, IL2CPP ARM64, Vulkan. Installed with **ImmersiveX ▸ Platform Setup ▸ Build And Run**.
**Adapter:** Meta Quest (Unity OpenXR: Meta 2.3.2, OpenXR 1.18.0)

| Check | Result | Evidence |
|---|---|---|
| Quest detected by ImmersiveX | ✅ | `Platform: Meta Quest (metaquest)` |
| Passthrough: real room visible behind content | ✅ | Tester confirmed in the headset |
| No boundary while walking around (S3) | ✅ | `Requested boundary suppression.` + tester confirmed |
| Space Setup from the app: walk-around, scan, save, return (S1) | ✅ | `Space Setup: requested` → app resumed; done twice |
| Room data matches the furniture marked in Space Setup | ✅ | Tester confirmed the panel counts |
| Anchor save + load in the same session (S2) | ✅ | `saved 66cc47b8` → `loaded 66cc47b8` |
| Anchor after headset off/on and full app restarts (S2) | ✅ | Reloaded automatically in 4 sessions in a row; marker in the same real-world spot (tester confirmed) |
| Grab with controllers | ✅ | Tester confirmed |
| Grab with hands | ✅ | Tester confirmed |
| Panel buttons with controller ray and hand pinch | ✅ | Tester confirmed |
| Crashes / not-responding errors | none | Device log |

## Issues found and fixed during the check
- **No passthrough:** Composition Layers Support wasn't enabled. The configurator now enables the Meta Quest feature set, like the Project Settings checkbox (ADR-0007).
- **Quest not detected on device:** IL2CPP stripping removed the adapter. Adapters now use `[assembly: AlwaysLinkAssembly]` + `[Preserve]` (ADR-0003).

## Known, harmless log messages
- `[AnchorProvider] No instance of AnchorProvider exists…` appears once while the app quits (anchor clean-up runs after XR has stopped).
- `Bounding box discovery dropped a bounding box … PENDING` is a timing race inside Unity OpenXR: Meta; the box arrives on a later query.
- Unity OpenXR: Meta queries room data every frame (hundreds of log lines per second). Throttling is an M2 task.
