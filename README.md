# ImmersiveX

**The Awakening of true Immersive Experience.**

ImmersiveX is an open Unity foundation for mixed, augmented and virtual reality apps.
You bring the content. ImmersiveX handles the rest:
- the see-through view
- room mapping
- saved placement
- hand and controller interaction
- media controls
- physics
- the build setup for each headset

All of it runs from one codebase.

> **Status: pre-alpha.** M0 (foundation) and M1 ("Hello Hologram" on Meta Quest) are done and verified on a Quest 3:
> passthrough, no boundary, grabbing with hands and controllers, Space Setup, and anchors that survive restarts.
> M2 (automatic room mapping) is next. See [Roadmap](#roadmap).

## What you get

| | Functionality | Default behaviour |
|---|---|---|
| F1 | See-through view | Mixed/augmented reality on every device except VR-only headsets |
| F2 | Automatic room mapping | A guided 360° look-around and floor pass the first time; known rooms load with no prompt; no boundary drawing |
| F3 | Persistent anchors | Objects stay where you left them after the headset comes off or the app restarts |
| F4 | Hands and controllers | Grab, move, rotate and press everything with hands, controllers, touch or gaze + pinch |
| F5 | Motion restrictions | Allow rotation around any combination of X, Y and Z per object |
| F6 | Media dashboard | Attach play/pause/seek/volume/rotate to any object; built-in, streamed or downloaded media |
| F7 | Gravity | Objects can fall and rest on the real floor and furniture |
| F8 | Transparency | Every object is transparent by default (opacity 0.5), overridable per object |
| F9 | Platform adapters | One contract per device, each with a build guide |
| F10 | Pinned versions | Exact, tested SDK versions per platform, installed with its adapter |

## Platforms

| Wave | Platforms | Status |
|---|---|---|
| 1 | Meta Quest 3 / 3S, Quest Pro / 2 | Working (preview), verified on Quest 3 |
| 2 | Apple Vision Pro, iPhone / iPad (ARKit), Android phones (ARCore), Android XR | Planned |
| 3 | HoloLens 2, Magic Leap 2, PC VR via SteamVR, HTC VIVE | Planned |
| 4 | PlayStation VR2 (native PS5) | Stub only — needs a Sony licence ([ADR-0008](docs/adr/0008-nda-platforms-as-stubs.md)) |

## Get started

### Requirements
- **Unity 6000.3.23f1**, installed through Unity Hub.
- For Meta Quest, add the **Android Build Support** module, including **Android SDK & NDK Tools** and **OpenJDK**.
- Git.

### Open the project
1. Clone the repository:
   ```sh
   git clone https://github.com/OlafenwaMoses/ImmersiveX.git
   ```
2. In Unity Hub, choose **Add ▸ Add project from disk** and select the cloned `ImmersiveX` folder.
3. Open it with Unity 6000.3.23f1. The first import downloads the pinned packages and takes a few minutes.

The project opens with URP configured for XR, linear colour and the Input System.
If settings ever drift, run **ImmersiveX ▸ Maintenance ▸ Apply Project Baseline**.

### Build your first experience
1. **ImmersiveX ▸ Platform Setup ▸ Meta Quest ▸ Install**, then **Configure**.
2. Put your models and media in `Assets/Content/`, drag them into `Assets/Scenes/Main.unity`, then right-click each one ▸ **ImmersiveX ▸ Make Immersive**. (New scenes: **ImmersiveX ▸ New Scene**.)
3. Try it without a headset:
   - **ImmersiveX ▸ Play Mode ▸ Meta XR Simulator (Quest)** or **XR Simulation (any device)**
   - then press **Play**.
4. Connect the headset, then **Platform Setup ▸ Build And Run**.

The [Meta Quest guide](docs/platforms/meta-quest.md) covers requirements, the simulator and troubleshooting.

## Repository layout

```
ImmersiveX/                     ← open this folder in Unity Hub
├─ Assets/
│  ├─ Content/                  your experiences
│  └─ Settings/Rendering/       URP assets
├─ Packages/
│  ├─ manifest.json
│  └─ com.immersivex.core/      shared, platform-agnostic package
├─ Platforms/                   one opt-in adapter package per device (MetaQuest, _Template)
├─ ProjectSettings/
├─ docs/                        decisions (adr/), spike findings (spikes/), guides
├─ tools/ci/                    checks that CI runs; run them locally too
└─ .github/                     CI workflow and issue/PR templates
```

## How it's built
- **One shared core, one seam.** `com.immersivex.core` depends only on AR Foundation, XR Interaction Toolkit and XR Hands. It reaches devices through a single platform contract ([ADR-0002](docs/adr/0002-core-on-unity-xr-stack.md)).
- **Thin, opt-in adapters.** Each platform is a package that pins its own SDK versions ([ADR-0003](docs/adr/0003-opt-in-platform-adapter-packages.md)).
- **Degrade, never crash.** Features choose a strategy from what the device can do and fall back when it can't ([ADR-0005](docs/adr/0005-capability-driven-fallbacks.md)).
- **Private by default.** Room data stays on the device, and placements use the platform's own anchor store ([ADR-0006](docs/adr/0006-local-first-system-anchors.md)).

All decisions are in [`docs/adr/`](docs/adr/README.md). Findings from the investigation spikes are in [`docs/spikes/`](docs/spikes/README.md).

## Roadmap

| Milestone | Goal |
|---|---|
| **M0** ✅ | Foundation: project, pinned packages, decisions, Quest spikes, CI |
| **M1** ✅ | Core skeleton + "Hello Hologram" on Quest (see-through, hands and controllers) |
| **M2** | Automatic room mapping and saved rooms (next) |
| M3 | Persistent content anchors |
| M4 | Motion restrictions, gravity, transparency |
| M5 | Media dashboard |
| M6 | Platform Setup window, samples and docs |
| M7 | Wave 2: visionOS, ARKit, ARCore, Android XR |
| M8 | Wave 3: SteamVR, VIVE, Magic Leap 2, HoloLens 2, PS VR2 stub |
| M9 | 1.0 release |

## Contributing
Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md). Everyone is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
Please report security issues privately, as described in [SECURITY.md](SECURITY.md).

## License
[MIT](LICENSE) © 2026 Moses Olafenwa
