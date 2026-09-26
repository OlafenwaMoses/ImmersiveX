# Changelog

All notable changes to ImmersiveX are recorded here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Unity 6000.3.23f1 project at the repository root. Application ID `genxr.immersivex.app`; product name `ImmersiveX`.
- `com.immersivex.core` package, pinning AR Foundation 6.3.5, XR Interaction Toolkit 3.3.2, XR Hands 1.7.3, XR Core Utilities 2.6.0, XR Plug-in Management 4.6.1, Input System 1.20.0 and URP 17.3.0.
- **ImmersiveX ▸ Maintenance ▸ Apply Project Baseline**: URP with XR-friendly defaults, linear colour, Input System only.
- Architecture decision records 0001–0010 in `docs/adr/`.
- M0 spike findings (Quest room data, persistent anchors, boundaryless MR, editor testing) in `docs/spikes/`.
- CI:
  - repository checks (`tools/ci/check_repo.py`)
  - Unity EditMode test job, which runs once licence secrets are configured
- **M1 (in progress):**
  - Core runtime:
    - `ImmersiveXSession` start-up flow
    - platform contract (`PlatformAdapter`, `PlatformCapabilities`, `PlatformRegistry`, permission, space and boundary providers)
    - `Services` registry and `ImmersiveXSettings`
    - `ImmersiveContent` (grab with hands and controllers, floating, placed in front of the user)
    - see-through setup
    - editor XR Simulation adapter
    - `DeviceCheckPanel`
  - Core editor:
    - **ImmersiveX ▸ Platform Setup** and **ImmersiveX ▸ New Scene**
    - **Make Immersive** context menu and **Recreate Starter Scene**
    - `BuildCli` command-line configure/build, and `IBuildConfigurator`
  - `Platforms/MetaQuest` adapter (OpenXR 1.18.0, Unity OpenXR: Meta 2.3.2) with `MetaQuestBuildConfigurator` and `platform.json`.
  - `Assets/Scenes/Main.unity` starter scene with the Hello Hologram cube and the device-check panel.
  - Meta Quest guide: `docs/platforms/meta-quest.md`, including what the Meta XR Simulator can and can't check.
  - **ImmersiveX ▸ Play Mode:** XR Simulation (any device) or Meta XR Simulator (Quest).
  - **Build And Run** in Platform Setup and `BuildCli.BuildAndRun`.
  - `EditorAutomation`: drive an open editor through `Library/ImmersiveX/Automation/command.txt`. Commands include play, stop, capture, invoke, configure, build, buildrun, open and menu.

- EditMode tests for the core (`Packages/com.immersivex.core/Tests/EditMode`); the package is listed in `testables`.
- `Platforms/_Template` and `docs/adding-a-platform.md`.
- `PanelGrabHandle`: a grab bar that moves world-space panels (used by the device-check panel).
- Quest 3 device validation for M1 in `docs/validation/`.
- Community files:
  - `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`
  - issue and pull request templates
  - `.gitattributes` (Unity Smart Merge, binary types) and `.editorconfig`

### Fixed
- Quest passthrough didn't draw. The configurator now enables the Meta Quest OpenXR feature set, including its required Composition Layers Support, and `Validate()` reports if it's off.
- Quest wasn't detected on device. `MetaQuestAdapter` now recognises the running Meta session and logs why when it doesn't.
- Room scanning no longer starts on platforms without room data.
- Meta's start-up boundary request (which failed with `XR_ERROR_HANDLE_INVALID`) is off; ImmersiveX hides the boundary once passthrough is showing.
- The starter scene's camera is saved with a transparent clear and HDR off.
- Quest builds use native render pass and GPU skinning, as in the Quest-proven VizionEnterprise project.
