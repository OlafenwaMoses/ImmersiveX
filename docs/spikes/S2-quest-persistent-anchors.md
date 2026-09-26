# S2 · Quest persistent anchors

**Question.** Can content anchors be saved and restored on Quest across an app restart and a headset off/on, using AR Foundation?

**Answer.** Yes, as far as the API goes. Unity OpenXR: Meta 2.3.2 implements AR Foundation's persistent-anchor API. Behaviour on a real headset still needs checking.

## Findings
- `ARAnchorManager` (AR Foundation 6.3.5) provides `TrySaveAnchorAsync`, `TryLoadAnchorAsync`, `TryEraseAnchorAsync` and `TryGetSavedAnchorIdsAsync`.
- Meta's provider overrides the batch versions so a whole batch is saved, loaded or erased in one request. `IAnchorStore` should batch where it can.
- Saved anchors are identified by a `SerializableGuid`. The ImmersiveX anchor index maps `contentId → SerializableGuid`.
- The package adds `com.oculus.permission.USE_ANCHOR_API` automatically when the anchor feature is enabled.
- **Shared anchors** exist for colocated users (group IDs, Enhanced Spatial Services). They are outside v1 and noted for the post-v1 multi-user work.

## Evidence
- `Runtime/ARFoundation/ARAnchorManager.cs` in AR Foundation 6.3.5 (public async API)
- `Documentation~/features/anchors/anchors-feature.md` and `shared-anchors.md` in Unity OpenXR: Meta 2.3.2
- `Editor/ModifyAndroidManifest.cs` (`USE_ANCHOR_API`)

## Still open
**Device check (M1):**
1. Place an anchor and save it.
2. Take the headset off and put it back on → the anchor is still in the same place.
3. Force-quit and relaunch the app → the anchor loads in the same place.
4. Erase the anchor → it no longer appears in the saved IDs.
