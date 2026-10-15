# ImmersiveX Core

The platform-agnostic part of ImmersiveX. It builds on AR Foundation, the XR Interaction Toolkit and XR Hands,
and talks to devices only through the platform contract. Vendor SDKs live in the platform adapter packages under
`Platforms/` at the repository root; media formats live in `com.immersivex.media`.

| Folder | Contents |
|---|---|
| `Runtime/Session/` | `ImmersiveXSession`: boot → platform → permissions → see-through → room → content → ready |
| `Runtime/Platform/` | The platform contract (`PlatformAdapter`, `PlatformCapabilities`, providers) and the editor simulation adapter |
| `Runtime/Features/SeeThrough/` | The see-through view |
| `Runtime/Features/SpaceMapping/` | Room mapping: snapshots, signatures, the saved-room library and matcher, the walkable area, the guided scan |
| `Runtime/Features/Anchoring/` | `ContentAnchor` and the placement index: content held in place by spatial anchors |
| `Runtime/Features/ContentMenu/` | The content menu and its hand swipes (`ContentMenu`, `HandSwipes`, `SwipeDetector`, `SwipeGate`) |
| `Runtime/Content/` | `ImmersiveContent` and selectable parts (`Parts/`: `ContentParts`, part labels with Put back) |
| `Runtime/Interaction/` | The ray only clicks (`DirectGrabOnly`, `ContentClickRaycaster`), hands without pointers |
| `Runtime/UI/` | World-space panels and controls: handles (move, turn, turn ring, resize), panel zones, prompts, grab bars |
| `Runtime/Diagnostics/` | The device-check panel |
| `Editor/` | Platform Setup, `BuildCli`, Make Immersive, New Scene, the project baseline, Play Mode switching, `EditorAutomation` |
| `Tests/` | EditMode tests |

Use it from another project by Git URL:

```
https://github.com/OlafenwaMoses/ImmersiveX.git?path=/Packages/com.immersivex.core
```
