# Immersive Media: one component for every format

**Immersive Media** (package `com.immersivex.media`) plays immersive media in the user's room. Add the component, set **Source** to a URL or file, and it:
1. detects the format;
2. attaches the right player;
3. shows the same acrylic media controls and side handles whatever the format.

A scene can hold as many as you like. Each has its own controls, and each stays where the user put it, held there by a spatial anchor.

## Formats

| Kind | Files | How it shows |
|---|---|---|
| GenXR 3.5D hologram stream | `stream.json` + `tiers/<tier>/NNNNNN.bin` (+ soundtrack), streamed or [bundled in the app](#bundled-4d-content) | Standing, fitted to a fixed height, at the chosen quality |
| Gaussian splats | `.ply` (3DGS), compressed `.ply` (PlayCanvas / SuperSplat), `.splat`, `.spz` (v1–v4), `.ksplat` (levels 0–2) | Standing; big scenes keep their most visible splats |
| Point cloud | `.ply` with x, y, z (and red, green, blue) | Standing, each point drawn as a small splat |
| Model | `.glb`, `.gltf` (glTFast), with animation | Standing; the first animation is the timeline |
| Mesh | `.obj` (with `.mtl` texture or vertex colours), `.ply` mesh | Standing, unlit |
| Sequence | A sequence `.json`, or a folder of numbered frames on desktop, of any of the above except streams | Standing, played at its frame rate, with optional sound |
| Video | `.mp4`, `.m4v`, `.mov`, `.webm` | Flat: an upright screen. 360° and 180°, mono, top-bottom or side-by-side: a sphere around the user |

**Detection** goes by extension. For a `.ply`, the file's header decides between splats, points and a mesh. For a `.json`, its contents decide between a hologram stream and a sequence. Video is 360° or 180° if the file name says so (`360`, `180`, `_tb`, `_sbs`, `_lr`…) or its shape does (2:1 means 360°). Set **Format** to override detection.

**Not supported:**
- **Live HLS/DASH streams:** Unity's video player can't play them.
- **Proprietary volumetric formats:** see [Vendor formats](#vendor-formats).

## Where it stands
- **The first media in the scene hierarchy** stands at the **centre of the mapped room**; the others stand around it. With no mapped room, as in the editor simulators, the group centres 2 m in front of the user.
  - Set **Spot** to choose: 0 is the centre and 1 and up stand around it.
- **Standing media** is fitted to **Height** (1.60 m by default) with its feet on the floor, facing the user.
- **Hologram streams** are fitted frame by frame and jump at camera cuts. A stream with a `fit` in its `stream.json` keeps that one fit for the whole clip, so a character keeps its size when it raises an arm.
- **Sequences** keep the first frame's scale, so a performer can move.
- **It moves only when the user moves it, and it stays where it's put:**
  - The first time, it's anchored where it's placed.
  - When the user lets go after moving, turning or resizing it, it's anchored again half a second later. The anchor is saved on the device, and the old one is erased.
  - Next launch, it comes back at its **spatial anchor**: the same real-world spot, even if the headset re-centred.
  - While the app runs, it **follows its anchor**. When the headset is taken off and put back on, or tracking is corrected, it stays in the same real spot. Until the device finds the anchor again, it's hidden rather than shown in the wrong place.
  - **Fallback:** its place is also saved relative to the recognised room, or to the tracking space when no room is mapped. That's used when the anchor can't be loaded, and on platforms that can't save anchors (XR Simulation).
  - "Forget room" forgets those places and erases their anchors.

## What the user can do
- **Controls** (an acrylic-glass panel in front of each piece of media):
  - Play/Pause (the icon shows what it will do), seek bar (seeks on release), time, speaker (mute) and volume.
  - For stills (splats, meshes, unanimated models), Play and the seek bar are greyed out and the panel shows what the file is.
  - The panel follows the media when it moves but not when it turns.
  - 360° video puts the panel in front of the user.
- **Handles** (grab with a controller or hand, by ray or pinch):
  - **Move bar**, under the controls: drag to slide the media across the floor. It stays inside the room.
  - **Turn bars**, at its left and right: drag around it like a turntable. It only turns about the up axis.
  - **Resize corner**, top right: drag up to make it bigger, down to make it smaller.
- **Grab the media itself:** one hand slides it and a wrist twist turns it; two hands turn it like a wheel.

It waits, paused, for Play unless **Play On Start** is set. With **Loop** off, it stops at the end and Play starts it again.

## Sequences
A sequence file is small JSON, with paths relative to it:

```json
{ "type": "sequence", "fps": 30, "frames": ["frame_0001.ply", "frame_0002.ply"], "audio": "audio.mp3", "up": "+y" }
{ "type": "sequence", "fps": 30, "pattern": "frames/frame_{0:D4}.obj", "start": 1, "count": 300 }
```

- **`up`** can be `+y`, `-y`, `+z` or `-z`. The defaults are y-down for splats and y-up for points and meshes.
- **Frames** download in parallel and decode on worker threads, a second or two ahead.

## Bundled 4D content
A 4D Gaussian-splat capture (4DGS) is a folder of standard 3DGS `.ply` files, one per frame. Pack it into a hologram stream under `Assets/StreamingAssets` and it ships **inside the app**: it plays from local storage with no network.

```
python3 tools/content/splat_sequence_to_stream.py research/atlux_ue_zebra_4dgs_ply \
    Assets/StreamingAssets/ImmersiveXContent/Zebra --fps 30 --title Zebra
```

- **What it writes:** `stream.json` and one frame file per `.ply`, at 17 bytes a Gaussian (a sixth of the `.ply`). The frames upload to the GPU as they are, so playback costs almost no CPU.
- **What it keeps:** the view-independent colour (the SH DC term). The higher spherical-harmonic bands are dropped; on the zebra they change the colour by about 4 %.
- **One fit for the clip:** the character's usual height, feet and centre over the whole clip, written as `fit` in `stream.json`.
- **Options:** `--max-splats` keeps the most visible Gaussians per frame (fewer bytes, less detail); `--up +y` handles y-up sources.
- **Size:** the zebra is 164 frames × 100,000 Gaussians = 267 MB in the APK, read at 51 MB/s while it plays.
- **Not committed:** `Assets/StreamingAssets/ImmersiveXContent/` is git-ignored because of its size. Run the command above before building.
- **Playing it:** set an Immersive Media's **Source** to the path inside StreamingAssets, e.g. `ImmersiveXContent/Zebra/stream.json`. A short buffer is enough for local frames (the demo uses 2 s, with 0.5 s of preroll).

## Streaming and sound
- **Hologram streams:**
  - They buffer 3 s ahead with 24 parallel downloads, because one connection is limited by latency.
  - The base tier needs about 24 MB/s.
  - The soundtrack is the clock.
- **Sound on the Quest** plays through Android's MediaPlayer, which handles AAC/M4A, MP3, OGG and WAV.
- **Sound in the editor:** Unity can't decode audio-only AAC, so a hologram's `.m4a` plays silently there. Video soundtracks always play.

## Vendor formats
- **4DViews (`.4ds`) and Arcturus (`.oms`, HoloStream)** are closed formats that only their own Unity SDKs decode. They need an account and a licence, and Arcturus's streaming requires OpenGL ES rather than Vulkan.
- **How to add one:** write an `IMediaCodec` adapter that wraps the vendor's player and register it with `MediaCodecs.Register`. It gets the same controls, handles and placement.
- **Without an adapter,** these files show a message saying what's needed.

## Test samples
- **Making them:** `python3 tools/samples/make_media_samples.py` writes a small sample of every format (about 5 MB) to `Assets/StreamingAssets/ImmersiveXSamples/`. They're generated when needed and never committed (the folder is git-ignored).
- **What they show:** the same asymmetric test figure in every format: a red head, blue feet, a green arm on the viewer's left and an orange nose pointing at the viewer. A decoding or axis mistake is obvious at a glance.
- **Tests:** the format EditMode tests decode each sample and check that orientation. Without the samples, those tests are skipped.
- **Trying one:** in Play mode, send `media open ImmersiveXSamples/<file>` to play it, or set it as an Immersive Media's Source.

**ImmersiveX ▸ Demos ▸ 3.5D Xperience** builds the demo scene, the first scene in the build:
- GenXR's streamed hologram, at the centre of the room;
- the bundled 4D zebra beside it, at its captured size (1.35 m).

## Automation
With the editor open (see [CONTRIBUTING](../CONTRIBUTING.md#driving-the-open-editor-automation)):

```
media list                                   # every Immersive Media in the scene, numbered
media [@<n>|@<name>] status|play|pause|toggle|seek <s>|mute|unmute|speaker|volume <0-1>|turn <deg>|move <x> <z>|height <m>
media [@<n>|@<name>] open <source> [format]  # switch what it plays (format: Auto, GaussianSplats, Video360, …)
demo                                         # build the 3.5D Xperience demo scene
```
