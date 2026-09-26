# ImmersiveX · Meta Quest adapter

Adds Meta Quest support to ImmersiveX. Install it from **ImmersiveX ▸ Platform Setup**; the build guide is in
[`docs/platforms/meta-quest.md`](../../docs/platforms/meta-quest.md).

| Folder | Contents |
|---|---|
| `Runtime/` | `MetaQuestAdapter` (capabilities and detection) plus providers for permissions, Space Setup and boundary visibility |
| `Editor/` | `MetaQuestBuildConfigurator`: Android player settings, OpenXR loader and features, URP settings for passthrough |
| `package.json` | Pinned SDKs: OpenXR 1.18.0, Unity OpenXR: Meta 2.3.2 |
| `platform.json` | Everything else a build needs: Unity modules, Android API levels, permissions, device requirements |
