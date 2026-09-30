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

- **M2 (in progress): room mapping and saved rooms.**
  - `RoomMappingFlow` in the session's Space step:
    - a known room loads silently;
    - a room the platform already knows is saved automatically;
    - an unmapped room gets a briefing with an auto-start countdown, then Space Setup (Quest) or a guided look-around, then an automatic save.
  - `RoomSnapshot`, `SpaceSignature`, `SpaceMatcher`, `SpaceLibrary` and `WalkableArea` (floor inside the walls, minus a 0.3 m margin).
  - `CoverageTracker`, `PromptPanel`, `WorldUi` and `WalkableAreaView`.
  - Device panel: **Rescan room**, **Forget room**, **Show area**, plus a room status line.
  - Room tracking pauses once the room is mapped.
  - Automation commands: `invoke rescan|forget|showarea|scandone|scanskip`, `prompt primary|secondary`, `turn <deg> <s>`.

- **ImmersiveX Media** (`com.immersivex.media`, [guide](docs/media.md)): one **Immersive Media** component plays any supported media in the room.
  - **Detection and players:** it detects the format and attaches the right player:
    - GenXR 3.5D hologram streams;
    - Gaussian splats: 3DGS `.ply`, compressed `.ply`, `.splat`, `.spz` v1–v4 and `.ksplat` levels 0–2;
    - point clouds;
    - glTF/GLB models with animation, through glTFast 6.20.0;
    - OBJ and PLY meshes;
    - frame sequences of any of these (a sequence `.json` or a folder);
    - flat, 180° and 360° video, mono or stereo.
  - **The same acrylic controls on every format:**
    - play/pause, seek, time, speaker and volume;
    - greyed out for stills;
    - the panel follows the media's position but not its rotation.
  - **Side handles:** a move bar under the controls, turn bars at the sides (about the up axis only) and a resize corner. They work with hands or controllers, by ray or pinch.
  - **Several pieces of media per scene:**
    - the first stands at the centre of the mapped room and the rest around it, each facing the user;
    - each stays where it was put, held by a spatial anchor (see M3 below).
  - **Rendering:**
    - splats of every format go through one URP splat shader (maths matched to GenXR's web player), sorted on worker threads;
    - big scenes keep their most visible splats (400,000 on standalone headsets);
    - meshes draw unlit;
    - 360° video draws on an inside-out sphere, with per-eye stereo halves.
  - **Sound:** Android MediaPlayer on Meta Quest (`IStreamAudioProvider`). It's the clock for streams and sequences.
  - **Vendor formats:** `IMediaCodec` is a plug-in point for them (4DViews, Arcturus), which need their own SDKs.
  - **Samples:** `tools/samples/make_media_samples.py` generates a 5 MB test sample of every format (git-ignored, not committed), all showing one asymmetric test figure. EditMode tests decode each one and check its orientation; they're skipped when the samples haven't been made.
  - **Demo scene:** **ImmersiveX ▸ Demos ▸ 3.5D Xperience** (GenXR's streamed hologram), the first scene in the build. Bundled content such as a 4D capture is added locally with Add Media; it isn't committed.
  - **Bundled 4D content:**
    - A 4D Gaussian-splat capture (a folder of 3DGS `.ply` frames) is packed into a hologram stream that ships inside the app and plays offline, at 17 bytes a Gaussian (now by Add Media; see D4).
    - A stream's `stream.json` can carry one `fit` for the whole clip, so a character keeps its size when it raises an arm.
    - The generated content (`Assets/StreamingAssets/ImmersiveXContent/`) is git-ignored because of its size.
  - **Automation:**
    - `media list`, and `media [@n|@name] status|play|pause|toggle|seek|mute|unmute|speaker|volume|turn|move|height|open`;
    - `demo`;
    - `resolve` (re-resolve packages).
- `WalkableArea.Centre()` and `FloorGrabTransformer` (slide across the floor, turn about the up axis only) in core.

- **Content from the internet, with no code (D4).** Checked against 88 real files from Khronos, PlayCanvas, Niantic, BabylonJS, GaussianSplats3D, Open3D, Stanford, 8i, three.js, videojs, Wikimedia Commons and others, in XR Simulation and the Meta XR Simulator. 20 EditMode tests decode them when the local corpus (`research/web-corpus`, git-ignored) is present.
  - **ImmersiveX ▸ Media ▸ Add Media…** (and automation `media add`): a file, a folder of frames or a URL is added to the open scene as Immersive Media.
    - Local content is copied into `StreamingAssets/ImmersiveXContent/<name>/` with the files it needs: an OBJ's MTL and textures, a glTF's buffers and images, a PLY's texture.
    - A folder of splat frames is packed into a hologram stream. The packer runs in the editor, reuses the player's decoders, handles every splat format and replaces the Python converter; its output is identical.
  - **Build check:** a build stops when a scene's Immersive Media content isn't in StreamingAssets, has missing frames, or points at a path on this computer. Plain http gets a warning. **ImmersiveX ▸ Media ▸ Check Content in the Build** and `media check` run it on demand.
  - **Photos:** `.jpg`/`.png`, flat on a screen (PNG transparency kept) or 360°/180° around the user, mono or stereo.
  - **Stereo setting** on Immersive Media. Once a video or photo is known to be 360° or 180°, its layout is inferred from its shape: a square 360° frame is top-bottom, a 2:1 180° frame is side-by-side (VR180).
  - **glTF compression:** Draco meshes, `EXT_meshopt_compression` and KTX2 (Basis Universal) textures, through Draco for Unity 5.4.3, KTX for Unity 3.7.0 and Unity's meshopt decompression 0.2.0-exp.1.
  - **Splat scans:** a scan's far background (an outdoor capture's sky shell) is left out, so the capture stands in the room instead of drawing as a bubble.
  - **Unsupported formats say what to do:** HDR/EXR/HEIC/WebP, FBX/USDZ/Blend, PCD/LAS, SOG, splatv and MKV/AVI each get a conversion hint. A video that won't decode names the codecs that do.
  - **Meshes:**
    - OBJ with several materials: one submesh each, with its MTL colour and texture; texture options and file names with spaces work.
    - `.stl`, binary and ASCII, z-up.
    - Parts with only a colour, or none, are lit, so plain models show their shape; textures and vertex colours stay unlit.
  - **Folder sources everywhere:** a folder of numbered frames plays as a sequence on the headset too, because Android builds get an index of StreamingAssets folders. A folder holding `stream.json` or `sequence.json` plays that file. A **Frame Rate** setting covers captures that don't say.

- **M3 (in progress): content stays put with spatial anchors.** It moves only when the user moves it.
  - `ContentAnchor`, added by Immersive Media and Immersive Content:
    - at start-up it restores the saved spatial anchor, else the pose saved with the room;
    - when the user lets go it saves the pose at once, then 0.5 s later anchors it there, saves the anchor and erases the old one;
    - first placements are anchored too;
    - content follows its anchor through tracking corrections and headset off/on, and hides while the anchor is lost.
  - Anchor saves, loads and erases run one at a time, and a failed save is retried once.
  - `PlacementIndex` (one JSON file per room, version 2): pose relative to the floor, size, anchor id and save time. Version-1 files still load. With no mapped room, placements are saved relative to the tracking space.
  - "Forget room" erases the room's saved anchors.
  - Automation's `media turn|move|height` save the placement as a release would.

### Fixed
- SPZ v3/v4 files with more than about 60,000 splats crashed the app. The decoder allocated 16 bytes of stack per splat and overflowed the worker thread's stack, which Mono reports as SIGILL; that includes Quest. It was found with PlayCanvas's `biker.spz`.
- Splat renderers released their GPU buffers while still drawing that frame; they now stop drawing first. Video does the same with its texture.
- An OBJ with a material library but no `usemtl` drew untextured; it uses the library's material again.
- Automation's `media open` takes a quoted source with spaces.
- The automation bridge could read `command.txt` between its creation and its first write, and drop the command. It now waits for content; writers should write `command.tmp` and rename it.
- Shapes created at runtime (anchor marker, panel grab bar, walkable-area outline) rendered pink on device. They now use a bundled URP Unlit material (`ImmersiveXRuntime.mat`), referenced from the settings asset so it's always in the build.
- The walkable-area outline was drawn in the wrong place on Quest. On see-through and room-aware platforms the session now zeroes the rig's camera height offset, so the camera and the room's planes share one space. The outline follows the XR Origin's trackables.
- XR Simulation showed a flat yellow background. The baseline now adds AR Foundation's **AR Background** URP renderer feature.
- Play mode waited 10 s for head tracking in XR Simulation. Tracking is now also confirmed by `ARSession.state`.
- In the editor, Play mode waited for the Game view to have focus before placing content, which stalled automation runs. Only devices wait for focus now.
- Quest passthrough didn't draw. The configurator now enables the Meta Quest OpenXR feature set, including its required Composition Layers Support, and `Validate()` reports if it's off.
- Quest wasn't detected on device. `MetaQuestAdapter` now recognises the running Meta session and logs why when it doesn't.
- Room scanning no longer starts on platforms without room data.
- Meta's start-up boundary request (which failed with `XR_ERROR_HANDLE_INVALID`) is off; ImmersiveX hides the boundary once passthrough is showing.
- The starter scene's camera is saved with a transparent clear and HDR off.
- Quest builds use native render pass and GPU skinning, as in the Quest-proven VizionEnterprise project.
