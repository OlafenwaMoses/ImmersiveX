# Immersive Media: one component for every format

**Immersive Media** (package `com.immersivex.media`) plays immersive media in the user's room. Add the component, set **Source** to a URL, a file or a folder, and it:
1. detects the format;
2. attaches the right player;
3. shows the same acrylic media controls and side handles whatever the format.

A scene can hold as many as you like. Each has its own controls, and each stays where the user put it, held there by a spatial anchor.

## Adding content (no code)
**ImmersiveX ▸ Media ▸ Add Media…** takes a file, a folder of frames or a URL, and adds it to the open scene as Immersive Media:
- **A URL** plays from the internet (the headset needs a connection).
- **Anything else is copied into `Assets/StreamingAssets/ImmersiveXContent/<name>/`,** so it ships inside the app and plays offline. The files it needs come with it: an OBJ's MTL and textures, a glTF's buffers and images, a PLY mesh's texture.
- **A folder of splat frames** (a 4D capture) is packed into a hologram stream for the headset, at its real size. See [Bundled 4D content](#bundled-4d-content).

Save the scene afterwards.

**The build checks your content.** Every Immersive Media in the build's scenes is checked, and the build stops if:
- its content isn't in StreamingAssets (in this repository `ImmersiveXContent/` is git-ignored because of its size, so a fresh clone won't have it);
- a stream or sequence is missing frames;
- its Source is a path on your computer.

Plain `http://` URLs get a warning, because Android blocks them by default. **ImmersiveX ▸ Media ▸ Check Content in the Build** runs the same check at any time.

## Formats

| Kind | Files | How it shows |
|---|---|---|
| Hologram stream | `stream.json` + `tiers/<tier>/NNNNNN.bin` (+ soundtrack): GenXR 3.5D streams, and 4D captures [packed by Add Media](#bundled-4d-content) | Standing, fitted to a fixed height |
| Gaussian splats | `.ply` (3DGS, any SH degree and property order), compressed `.ply` (PlayCanvas / SuperSplat), `.splat`, `.spz` (v1–v4), `.ksplat` (levels 0–2) | Standing; big scenes keep their most visible splats, and a scan's far background (sky shell) is left out |
| Point cloud | `.ply` with x, y, z (and red, green, blue) | Standing, each point drawn as a small splat |
| Model | `.glb`, `.gltf` (glTFast), with animation and morph targets; Draco and `EXT_meshopt_compression` meshes, KTX2 (Basis Universal) textures | Standing, lit; the first animation is the timeline |
| Mesh | `.obj` (several materials from its `.mtl`: colours and textures), `.ply` mesh, `.stl` | Standing. Textures and vertex colours draw unlit (scans keep their lighting); plain colours draw lit |
| Sequence | A folder of numbered frames, or a sequence `.json`, of splats, points or meshes | Standing, played at its frame rate, with optional sound |
| Video | `.mp4`, `.m4v`, `.mov` (H.264, or H.265 tagged `hvc1`), `.webm` (VP8) | Flat: an upright screen. 360° and 180°, mono or stereo: a sphere around the user |
| Photo | `.jpg`, `.jpeg`, `.png` | Flat: an upright screen (a PNG's transparency is kept). 360° and 180°, mono or stereo: a sphere around the user |
| Web experience | A three.js page: `.html`, a folder with `index.html`, or an `https` URL | The page runs out of sight as its own player; its 3D scene is drawn natively in the room, its controls on a panel beside it (see [Web experiences](#web-experiences)) |
| 4D mesh | A folder with a `.sp4m` container and its soundtrack | Standing, animated on its soundtrack's clock, with captions and cards that point at its parts (see [4D meshes](#4d-meshes)) |

**Detection:**
- **By extension**, mostly. A `.ply` file's header decides between splats, points and a mesh.
- **A `.json`:** its contents decide between a hologram stream and a sequence.
- **A folder** with a `stream.json` or `sequence.json` plays that file; otherwise its numbered frames play as a sequence.
- **Video and photos:**
  - *360° or 180°:* 360° if the file name says so (`360`, `equirect`) or the frame is 2:1; 180° if the name says `180`.
  - *Stereo layout* (for 360° and 180°): the **Stereo** setting decides, else the name (`_tb`, `_ou`, `_sbs`, `_lr`…), else the shape. A square 360° frame is top-bottom; a 2:1 180° frame is side-by-side (VR180).
- **When detection can't tell,** for example a square 360° video with no hint in its name, set **Format** (Flat, 360° or 180° video or photo), and **Stereo** if needed.

**Not supported:**
- **Live HLS/DASH streams:** Unity's video player can't play them. Use a progressive MP4 or WebM over HTTPS.
- **Video codecs:**
  - WebM must be VP8; VP9 and AV1 didn't play in the editor.
  - H.265 tagged `hev1` rather than `hvc1` doesn't play in the Mac editor; Android reads both. Re-tag it with `ffmpeg -i in.mp4 -c copy -tag:v hvc1 out.mp4`.
- **Spherical metadata isn't read.** The kind comes from the name and shape, or the Format and Stereo settings.
- **Unsupported 360/180 layouts:** cubemap (EAC or 3×2) 360° video and fisheye or mesh-projected VR180 (`ytmp`, straight from some cameras) aren't supported. Use equirectangular.
- **Flat stereo 3D** (side-by-side 3D films): the whole frame is shown.
- **Images:** WebP, AVIF, HEIC, HDR and EXR. Convert them to JPG or PNG. Photos show as stored; EXIF rotation isn't applied.
- **glTF:**
  - `EXT_texture_webp`: WebP textures aren't supported. Re-export with PNG, JPG or KTX2.
  - `KHR_meshopt_compression`: the newer Khronos name for meshopt isn't read yet; `EXT_meshopt_compression` is.
  - `KHR_gaussian_splatting` (splats inside glTF) loads as plain points. Use a splat file instead.
- **Splat formats:** PlayCanvas `.sog` and splaTV `.splatv`. Export `.ply`, compressed `.ply` or `.spz` instead.
- **Spatial photos:** Apple HEIC stereo photos and Google VR180 `.vr.jpg` photos (whose second eye hides in the metadata).
- **Point clouds other than PLY** (`.las`, `.laz`, `.e57`, `.pcd`, `.xyz`): convert them to PLY, for example with CloudCompare.
- **Big-endian PLY.**
- **FBX, USDZ, Blend and other Unity-only formats:** they can't load at runtime. Import them into Unity and use **Make Immersive** (right-click ▸ ImmersiveX), which makes any scene object grabbable and anchored.
- **Proprietary volumetric formats:** see [Vendor formats](#vendor-formats).

## Where it stands
- **The first media in the scene hierarchy** stands at the **centre of the mapped room**; the others stand around it. With no mapped room, as in the editor simulators, the group centres 2 m in front of the user.
  - **Placement:** *Room Centre* (default) or *In Front Of User*, at **Distance From User** (2 m).
  - Set **Spot** to choose: 0 is the centre and 1 and up stand around it.
  - **With the content menu on** (the default), one piece shows at a time, and the menu lists them in hierarchy order. See [CONTENT_GUIDE.md](../CONTENT_GUIDE.md#placing-content-in-the-app) for placing each kind of content.
- **Standing media** is fitted to **Height** (1.60 m by default) with its feet on the floor, facing the user. Add Media sets Height to a packed capture's real size.
- **Hologram streams** are fitted frame by frame and jump at camera cuts. A stream with a `fit` in its `stream.json` keeps that one fit for the whole clip, so a character keeps its size when it raises an arm.
- **Sequences** keep the first frame's scale, so a performer can move.
- **Up axis:** splats default to y-down (the 3DGS convention), SPZ to y-up, points, meshes and models to y-up, STL to z-up. Set **Up** when a file differs.
  - Z-up meshes (CAD, many PLYs) need +Z, and camera-frame point clouds need −Y.
  - Scans that weren't levelled when they were made (COLMAP scenes are often tilted) show tilted. Level them in SuperSplat before exporting.
- **Far background:** a splat scan's distant background (a sky shell far outside the scene) is left out, so the capture stands in the room. It keeps everything within three times the distance of its nearest 90 % of splats.
- **Scene-scale captures** (a room, a plaza) are fitted to Height like any other standing media, so they show as a diorama. Resize them with the corner handle.
- **It moves only when the user moves it, and it stays where it's put:**
  - The first time, it's anchored where it's placed.
  - When the user lets go after moving, turning or resizing it, it's anchored again half a second later. The anchor is saved on the device, and the old one is erased.
  - Next launch, it comes back at its **spatial anchor**: the same real-world spot, even if the headset re-centred.
  - While the app runs, it **follows its anchor**. When the headset is taken off and put back on, or tracking is corrected, it stays in the same real spot. Until the device finds the anchor again, it's hidden rather than shown in the wrong place.
  - **Fallback:** its place is also saved relative to the recognised room, or to the tracking space when no room is mapped. That's used when the anchor can't be loaded, and on platforms that can't save anchors (XR Simulation).
  - "Forget room" forgets those places and erases their anchors.

## What the user can do
- **Controls** (an acrylic-glass panel, 36 cm wide, that stays with the user, `WithinReach`):
  - Play/Pause (the icon shows what it will do), seek bar (seeks on release), time, speaker (mute) and volume, with the media's name on a tab above.
  - For stills (splats, meshes, photos, unanimated models), Play and the seek bar are greyed out and the panel shows what the file is.
  - Whatever the media (standing, a screen, 360°/180°), the panel sits **low on the user's right at elbow level**: 0.55 m below the eyes (about 1 m above the floor standing, lower sitting), 0.24 m to the right and 0.38 m in front, tilted to face the eyes.
  - It keeps still while the user looks around, looks down at it or leans in to press it, and while its sliders or the move bar are dragged. When they walk more than 0.35 m, sit down or stand up, or turn more than 40° while not looking at it, it glides after them (about 0.3 s) and settles at their side again.
  - When the content menu switches pieces, the new piece's panel appears straight at the user's side. Several pieces shown at once stack their panels upwards.
  - **Captions** (narrated media) stay with the media, where the user watches: in front of standing media on the user's side (coming round as they walk around it), under a screen, or in front of the user for 360°/180° media.
- **Handles** (a controller's ray, or a hand at them):
  - **Move bar**, under the controls (so it's within reach too): drag to slide the media across the floor, by the same amount the hand moves. A hand on it carries it along. The media stays inside the room.
  - **Turn bars**, at its left and right, and a **turn ring** around its base: drag around it like a turntable. It only turns about the up axis.
  - **Resize corner**, top right: drag up to make it bigger, down to make it smaller.
- **Grab the media itself** with a hand or controller at it: one hand slides it and a wrist twist turns it; two hands turn it like a wheel. A controller's ray only clicks and drags handles; it never picks content up.
- **Hands have no pointer:** tracked hands press buttons with a finger and pinch or grab what's within reach. Controllers in hand keep their rays (Settings ▸ Hand Pointers gives hands rays too).
- **Parts:** models with two or more meshes, web scenes and 4D meshes have selectable parts. A ray click selects a part (click again for the whole), a hand picks it up, and its label's **Put back** returns it. Parts move only while the media is paused. Turn it off with **Selectable Parts**.
- **Content menu:** lists every piece of content in the scene and shows one at a time (the choice is remembered), with **Quest menu** and **Close app**. It opens in front of the user when the app starts; after that a quick sideways swipe of either hand opens it: towards the middle of the body (right hand right to left, left hand left to right); the reverse closes it. With more than six pieces, up and down buttons on the right of the list scroll it a row a click. See the [README](../README.md#in-the-headset) for the picture. Turn it off in Settings ▸ Content Menu.
- **MR and VR** (the content menu's header, MR at every start): **MR** shows the real room with content standing in it. **VR** hides the real room and puts the user in a quiet, low-lit virtual world (a night sky with faint stars over a dark floor at the real floor's height) where any content stands out. Content keeps its place in the real room either way, the Quest's boundary comes back in VR, and the real walls and furniture show as a soft grid when the user comes within about a metre of them.

It waits, paused, for Play unless **Play On Start** is set. With **Loop** off, it stops at the end and Play starts it again.

## Sequences
**A folder of numbered frames** is the simplest sequence:
- **Frames** are `.ply` (splats, points or meshes), `.obj`, `.stl`, `.splat`, `.spz` or `.ksplat`, one kind per folder.
- **Order** is natural: `frame_2` comes before `frame_10`.
- **Sound:** the folder's first sound file (`.mp3`, `.m4a`, `.ogg`, `.wav`) is the soundtrack.
- **Frame rate:** set it with **Frame Rate** (30 when it's 0).

Folders work on the headset too: StreamingAssets is inside the APK there and can't be listed, so the Android build writes an index of its folders.

A **sequence file** is small JSON, with paths relative to it:

```json
{ "type": "sequence", "fps": 30, "frames": ["frame_0001.ply", "frame_0002.ply"], "audio": "audio.mp3", "up": "+y" }
{ "type": "sequence", "fps": 30, "pattern": "frames/frame_{0:D4}.obj", "start": 1, "count": 300 }
```

- **`up`** can be `+y`, `-y`, `+z` or `-z`. The defaults are y-down for splats and y-up for points and meshes.
- **Frames** download in parallel and decode on worker threads, a second or two ahead.
- **Heavy frames:** raw 4D splat frames are too heavy to decode live on a standalone headset. Pack them instead (next section).

## Bundled 4D content
A 4D Gaussian-splat capture (4DGS) is a folder of splat frames, usually standard 3DGS `.ply` files, one per frame. **Add Media** packs it into a hologram stream under `Assets/StreamingAssets/ImmersiveXContent/<name>/`, which ships **inside the app** and plays offline. The automation command is `media pack [fps] [title=Name] <folder>`.

- **What it writes:** `stream.json` and one frame file per frame, at 17 bytes a Gaussian (a sixth of a 3DGS `.ply`). The frames upload to the GPU as they are, so playback costs almost no CPU.
- **Frame formats:** 3DGS or compressed `.ply`, point `.ply`, `.splat`, `.spz` or `.ksplat`, one kind per folder, in natural order.
- **What it keeps:** the view-independent colour (the SH DC term). The higher spherical-harmonic bands are dropped; on a typical capture they change the colour by about 4 %.
- **One fit for the clip,** written as `fit` in `stream.json`, so nothing changes size while it plays:
  - **Height:** the clip's usual height (the median over its frames), so a character doesn't shrink when it raises an arm.
  - **Floor:** the ground the clip comes down to (the 90th percentile of the frames' lowest points), so a scene whose parts rise for most of the clip still stands on its ground.
  - **Centre:** the medians of the frames' centres, so frames where something flies far away don't move it.
  - Add Media sets the media's Height to the clip's real size, so a capture in metres keeps it.
- **What flies far out of the room is left out:** splats more than 10 times the clip's height from its centre. Left in, they'd spread each frame's 16-bit positions so thin that everything else shimmers.
- **Options:**
  - **Frame rate:** captures don't always say; 30 is common.
  - **Max Gaussians a frame:** keeps the most visible ones, for fewer bytes and faster frames.
  - **Up axis.**
  - **Soundtrack:** a sound file in the folder becomes the soundtrack.
- **Size:** 164 frames × 100,000 Gaussians is 267 MB in the APK, read at 51 MB/s while it plays, and packs in about 10 s.
- **Not committed:** `Assets/StreamingAssets/ImmersiveXContent/` is git-ignored because of its size. Pack the capture again after a fresh clone; the build stops until you do.

## Streaming and sound
- **Hologram streams:**
  - They buffer 3 s ahead with 24 parallel downloads, because one connection is limited by latency.
  - The base tier needs about 24 MB/s.
  - The soundtrack is the clock.
- **Sound on the Quest** plays through Android's MediaPlayer, which handles AAC/M4A, MP3, OGG and WAV. Soundtracks bundled in the app open straight from inside the APK (MediaPlayer can't open `jar:` URLs, so they go through a file descriptor).
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

**ImmersiveX ▸ Demos** builds the demo scene, the first scene in the build: a streamed hologram at the centre of the room. Add bundled content to it with Add Media; that content stays local (it's git-ignored), so keep those scene changes out of commits.

## Web experiences
A three.js page (WebGLRenderer, r170 or later) as one self-contained `.html`, a folder with `index.html`, or an `https` URL.
- **The page runs out of sight** in a web view (headless Chrome in the editor, Android's WebView on Quest), as it is: its own timeline, sound and captions stay in charge.
- **Its 3D scene is drawn natively in the room:** geometry, materials, textures and every object's movement are streamed from the page and rebuilt with glTF-style PBR materials, instanced, skinned and line meshes. Its actors (or its top-level things) become selectable parts.
- **Its own controls** (buttons, captions, cards) show on a panel to the user's left, within reach. Immersive Media's controls don't show.
- **Where it stands,** set once when the page has loaded and stands still: the page's ground is the room's floor (kept even when the page hides it; without one, the level most of the scene rests on), and what the page's camera looks at stands at the content's place, as tall as **Height**. Then it plays as written; what sinks below the floor is hidden.
- **Hidden** (the content menu shows something else), the page waits where it is, with its sound paused, and carries on when it's shown again.
- **Not reproduced:** custom shaders (drawn in their main colour), post-processing, points and sprites. Pages on other engines or on three.js's WebGPU renderer show on the panel only.

## 4D meshes
A `.sp4m` container is a scene baked over time: its triangles part by part, every part's pose 30 times a second, its colours twice a second, the rules it applies per pixel (a cutaway, a door, a dissolving set, highlights) and its soft effects as Gaussians.
- **Bring a folder** with the container and its soundtrack. The container is gzipped inside but never named `.gz`: Android's APK packer unzips `.gz` files and drops the extension. Add Media renames `name.sp4m.gz` to `name.sp4m`, and the build check refuses a `.gz` name.
- **How it plays:** standing on the floor, as tall as **Height**, on its soundtrack's clock with the standard controls. Its colours are baked, so nothing is relit. Captions show in front of it on the user's side, and cards point at the parts the content names. Its parts are selectable.
- **When its subject drives or flies away** (a rocket climbing, cars driving off), the stage follows it: the subject stays at the media's place at the same size, and what it leaves behind slides away and dissolves at the edge of the room or sinks below the floor. The content can set how (`follow`: a radius and a dead zone); otherwise a subject that ends up more than one and a half stage heights from its start is held within a quarter of a stage height of it, and the set dissolves two stage heights from it.
- **Versions:** version 2 colours through a texture atlas with one cutaway wedge, a door and a set dissolving outward. Version 3 adds scenes coloured per vertex (every object a one-member family), each object's own cutaway (a plane or a wedge in its frame, with a level per frame), colour keyframes at listed times decoded on demand, a sphere dissolve with the scene's own noise, `follow`, and `stage.footprint` / `stage.roomHeight`: how wide the piece gets over its whole run, and how tall it should stand so it fits (Add Media sets **Height** from it). Its captions stand clear of the footprint's front edge. Version 4 is the **hologram look** (`look: "hologram"`): no colour probes; every object is light worked out per pixel (`Mesh4DHologram.shader`) from its hue, gains (body, scan band), contour spacing and height span: a rim glow, contour lines fixed to its body, a scan band climbing it, screen scanlines, a flicker, and a front where it materialises, all added over the room. Per frame the highlight bytes are its glow, the per-object level bytes how far it has materialised, and float 7 a global fade; `holo` carries the look's constants (the web player's maths) and `roomGain` its brightness in the room.
- **Budget:** a two-minute scene of about 160 objects and 400,000 triangles is a container of about 50 MB that loads in under a second on a Quest 3. A five-minute version 3 scene (162 objects, 482,000 triangles, 396,000 per-vertex probes, 1,846 colour keys) is 45 MB and loads in about a second in the editor. A 33-second version 4 hologram (224 objects, 413,000 triangles, up to 19,400 effect Gaussians a frame) is 62 MB and loads in under a second; a four-minute one (254 objects, 614,000 triangles, 303 MB) loads in about 4 s on a Quest 3 and plays at 72 fps. Large containers are copied out of the APK once per build and unpacked in one pass, so loading needs about the container's own size in memory.

## Automation
With the editor open (see [CONTRIBUTING](../CONTRIBUTING.md#driving-the-open-editor-automation)):

```
media list                                   # every Immersive Media in the scene, numbered (Play mode)
media [@<n>|@<name>] status|play|pause|toggle|seek <s>|mute|unmute|speaker|volume <0-1>|turn <deg>|move <x> <z>|height <m>
media [@<n>|@<name>] open <source> [format]  # switch what it plays (format: Auto, GaussianSplats, Video360, …)
media add [fps] <file, folder or URL>        # Add Media, then save the scene (Edit mode)
media pack [fps] [title=Name] <folder>       # pack splat frames into StreamingAssets, scene untouched (Edit mode)
media check                                  # the build's content check (Edit mode)
media scene | remove <name> | first <name>   # list, remove or reorder the scene's Immersive Media (Edit mode)
media [@<n>] parts | inspect | node <name> | click <x> <y> | js <code>   # parts and web pages (Play mode)
contentmenu status|open|close|select <n>|move <x> <y> <z>|swipe <right|left|both> [back]
demo                                         # build the demo scene
```
