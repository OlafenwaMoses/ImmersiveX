# ImmersiveX content guide

Every kind of content ImmersiveX plays, and the shape it must be in so it goes into the app and plays with no code and no further changes to the content. If content follows the structure here, you add it in the Unity editor and build; the build tells you if anything is missing.

Everything below was tested on 2026-09-28 with 88 real files downloaded from public sources, in the Unity editor and in the Meta XR Simulator (Quest 3). Hologram streams, 4D Gaussian splats and 4D meshes have also played on a Quest 3; the other types in this guide still need a headset check. The [test results](#tested-with) are at the end.

## Adding content

1. **Get the content into one of the structures below.**
2. **In Unity, open ImmersiveX ▸ Media ▸ Add Media…** and pick the file or folder, or paste a URL. Then press **Add to Scene**.
   - Local content is copied into `Assets/StreamingAssets/ImmersiveXContent/<Title>/`, with the files it needs. It ships inside the app and plays offline.
   - A folder of splat frames (a 4D capture) is packed for the headset while it's copied.
   - A URL isn't copied: it plays from the internet, so the headset needs a connection.
   - The content is added to the open scene as an **Immersive Media** object. Save the scene.
3. **Build.** The build checks every Immersive Media in the build's scenes and stops with a clear message when content is missing or can't ship. **ImmersiveX ▸ Media ▸ Check Content in the Build** runs the same check at any time.

**Doing it by hand instead:** copy the content into `Assets/StreamingAssets/` yourself, add an **Immersive Media** component to an object, and set its **Source** to the path inside StreamingAssets (for example `ImmersiveXContent/Chair/chair.glb`) or to a URL. Add Media does exactly this, plus packing and copying companion files.

**Good to know:**
- **The app gets bigger by the size of its content.** The APK is about 60 MB plus the content, and a 4D capture can add several hundred MB.
- **In this repository `ImmersiveXContent/` is git-ignored,** so content isn't committed. After a fresh clone, add the content again; the build check reminds you.
- **Licences:** make sure you're allowed to ship it. Many free models and captures are non-commercial (CC BY-NC).

## At a glance

| Content | Bring | Structure | How it shows |
|---|---|---|---|
| [Gaussian splats](#1-gaussian-splats) | `.ply`, `.spz`, `.splat`, `.ksplat` | One file | Standing in the room, fitted to Height |
| [4D Gaussian splats](#2-4d-gaussian-splats) | A folder of splat frames | Numbered frames, one format, optional sound | Standing, animated; packed for the headset |
| [Hologram streams](#3-hologram-streams) | `stream.json` + frames | A URL, or a stream folder | Standing, streamed |
| [Point clouds](#4-point-clouds) | `.ply` with points | One file | Standing, points drawn as small splats |
| [Volumetric video and sequences](#5-volumetric-video-and-frame-sequences) | Mesh, point or splat frames | Numbered frames in a folder, or a `sequence.json` | Standing, animated |
| [3D models](#6-3d-models-gltf-and-glb) | `.glb`, or `.gltf` + `.bin` + textures | One file, or a folder | Standing, lit, first animation playable |
| [Meshes](#7-meshes-obj-ply-stl) | `.obj` (+ `.mtl` + textures), `.ply`, `.stl` | One file with its companions | Standing |
| [Video](#8-video) | `.mp4`, `.mov`, `.m4v` (H.264/H.265), `.webm` (VP8) | One file | Flat screen, or 360°/180° around you |
| [Photos](#9-photos) | `.jpg`, `.png` | One file | Flat screen, or 360°/180° around you |
| [Unity assets](#10-unity-assets-fbx-blend-usdz-prefabs) | `.fbx`, `.blend`, `.usdz`, prefabs | Imported into Unity | A grabbable, anchored object |
| [Vendor volumetric formats](#11-vendor-volumetric-formats) | `.4ds`, `.oms`, `.vols` | — | Need an adapter (code) |
| [Web experiences](#12-web-experiences-threejs-pages) | A three.js page: `.html`, a folder with `index.html`, or a URL | One self-contained file, or a folder | Its 3D scene in the room, its own controls on a panel |
| [4D meshes](#13-4d-meshes-sp4m) | A `.sp4m` container, with its soundtrack | A folder | Standing, animated, narrated: captions and cards that point at its parts |

Every Immersive Media gets the same acrylic controls (play/pause, seek, time, speaker, volume) and handles around it (a move bar, turn bars and a turn ring, a resize corner). Each stays where you put it, held by a spatial anchor. Web experiences are the exception for controls: they bring their own player (section 12).

## Using content in the headset

The same rules apply to every kind of content. **Only controllers have a pointer (ray).** Tracked hands have none: they press buttons with the index finger and pinch or grab things within reach (Settings ▸ Hand Pointers gives hands a ray too).

| | A controller's ray | A hand, or a controller at it |
|---|---|---|
| Panel buttons and sliders | Click and drag them | Press them with your index finger, or pinch |
| Handles: move bar, turn bars, turn ring, resize corner | Drag them | Pinch or grab and drag them |
| The content itself | Nothing: the ray passes through it | Grab it to move it |
| A part of a model | Click selects it; click again for the assembly it's in | Grab it to pick it up and move it |

- **Turning:** drag the ring around the content's base, or a bar at its side. It turns about the up axis, as a whole (parts never turn with it).
- **Parts:** a model with two or more meshes has selectable parts (Immersive Media ▸ Selectable Parts, on by default). The selected part's label has **Put back** and **Put all back**. It shows over the part when that's within reach; otherwise it comes half a metre in front of you, with a line to the part, so a finger can always press it (and a controller's ray click it). The handles move the whole model. Unity objects get parts with Immersive Content ▸ Selectable Parts.
- **Panels win near them:** with your hand at a panel, nothing nearby can be grabbed, so your finger always reaches the buttons.

### The content menu

- **Open it** with a fast sideways swipe in front of you: the right hand from right to left, or the left hand from left to right. It opens in front of your eyes, wherever you are; if it's open somewhere else, the swipe brings it to you.
- **Close it with the reverse swipe:** the right hand from left to right, or the left hand from right to left.
  - Your hand's way back right after an opening swipe doesn't close it (nothing counts for 0.8 s after a swipe).
  - Both hands at once (a clap, or turning your body) does nothing.
- **It lists the scene's content.** One shows at a time: pick another to show it instead. The one showing is highlighted. The app remembers it.
- **Quest menu** shows the headset's own menu; **Close app** quits.
- **Move it** by the gold bars at its sides (ray or hand) or by its frame (hand). It stays where you put it until your next swipe, which brings it back in front of you.
- Turn it off in the ImmersiveX Settings asset (Content Menu) to show every piece of content at once.

---

## 1. Gaussian splats
Static 3D captures made with Gaussian splatting (Polycam, Luma, Scaniverse, Postshot, SuperSplat, KIRI, Nerfstudio and others).

**Bring one file:**

| Format | What's supported |
|---|---|
| `.ply` (3DGS) | Needs `x y z`, `f_dc_0..2`, `opacity`, `scale_0..2` and `rot_0..3` (any order, any SH degree; extra properties such as normals are ignored). Binary little-endian or ASCII. |
| Compressed `.ply` | SuperSplat / PlayCanvas (a `chunk` element and `packed_*` vertices), with or without its `sh` element. |
| `.spz` | Niantic, versions 1–4 (gzip or ZSTD). |
| `.splat` | antimatter15's 32-byte format. |
| `.ksplat` | GaussianSplats3D version 0.1, compression levels 0–2, any SH degree. |

**Before adding:**
- **Orientation:** `.ply`, `.splat` and `.ksplat` are read as y-down (the 3DGS convention); `.spz` as y-up (its spec).
  - If yours shows upside down or on its side, set **Up** on the Immersive Media (±Y or ±Z).
  - A scan that was never levelled (many COLMAP scenes are tilted by tens of degrees) shows tilted. Level it in SuperSplat, then export.
- **Object versus scene:** it's fitted to **Height** (1.6 m by default) and stands on the floor, so a room or plaza scan becomes a diorama. Resize it with the corner handle, up to 4 m.
- **Background:** a distant background, like an outdoor scan's sky shell, is left out automatically so the capture stands in the room.
- **Size on the headset:**
  - At most 400,000 splats are drawn at once on Quest; bigger files keep their most visible ones (the **Max Gaussians** setting). A 1,015,577-splat file loaded fine.
  - `.spz` is the smallest format, about a tenth of a 3DGS `.ply`.
- **Colour:** view-dependent colour (spherical harmonics beyond the base colour) isn't drawn.

**Won't play:** PlayCanvas `.sog` and splats inside glTF (`KHR_gaussian_splatting`, which loads as plain points). Export `.ply`, compressed `.ply` or `.spz` from SuperSplat instead.

## 2. 4D Gaussian splats
Animated captures (4DGS): one splat file per frame.

**Bring a folder of frames:**
```
MyCapture/
  Frame_0000.ply
  Frame_0001.ply
  …
  soundtrack.m4a        (optional: .m4a, .mp3, .ogg or .wav)
```
- **Frames:**
  - Any splat format, but only one per folder: 3DGS or compressed `.ply`, `.spz`, `.splat`, `.ksplat`, or point `.ply`.
  - Numbered in order. Natural order is used, so `frame_2` comes before `frame_10`; zero-padding isn't required.
  - In **metres**, since the packed capture keeps its real size.
- **Frame rate:** know it, because captures rarely say. Add Media asks for it (30 by default).

**What Add Media does:**
- **Packs it into a hologram stream** (`stream.json` + `tiers/base/NNNNNN.bin`) at 17 bytes per Gaussian, about a sixth of a 3DGS `.ply`. Frames upload to the GPU as they are, so playback is light on the headset.
- **One fit for the whole clip,** so nothing changes size while it plays:
  - **Size:** the clip's usual height. A character doesn't shrink when it raises an arm.
  - **Floor:** the ground the clip comes down to (the 90th percentile of the frames' lowest points). A scene whose parts rise off the ground for most of the clip still stands on its ground.
  - **Centre:** the middle of most frames (medians), so frames where something flies far away don't move it.
- **Leaves out what flies far out of the room:** splats more than 10 times the clip's height from its centre, such as something that flies kilometres away. They'd be far outside any room, and leaving them in would make everything else shimmer.
- **Sets Height to the capture's real size** when that's between 0.2 and 4 m; a bigger scene keeps the default 1.6 m.
- **Uses the folder's sound file** as the soundtrack. It's the clock, and it plays on the headset from inside the app (silent in the editor, which can't decode `.m4a`).

**Budget:** 100,000 Gaussians per frame × 164 frames at 30 fps is 267 MB, read at 51 MB/s, and plays on a Quest 3; 30,000 per frame × 751 frames at 6 fps is 369 MB, read at 3 MB/s. Use **Max Gaussians a frame** to shrink bigger captures, and keep clips short, since every second at that density adds about 50 MB to the app.

**Won't play:** splaTV `.splatv`, and other single-file 4D formats with per-Gaussian motion. Export per-frame `.ply` files and pack them.

## 3. Hologram streams
GenXR 3.5D streams, or a stream you packed.

- **A URL** to its `stream.json` streams over the internet. The base tier needs about 24 MB/s. Set **Quality** to choose the tier.
- **A local stream folder:**
  ```
  Show/
    stream.json
    audio.m4a                 (optional soundtrack; it's the clock)
    tiers/base/000000.bin …   (one file per frame, per tier)
  ```
  Point the Source at `stream.json` or at the folder.

## 4. Point clouds
Scans stored as coloured points.

- **Bring a `.ply`** with `x y z` and optional colour (`red green blue`, as 0–255 or 0–1). Normals and extra elements are ignored. It must have no faces; a PLY with faces is read as a mesh.
- **Orientation:** read as y-up. Many LiDAR and CAD clouds are z-up (set **Up** to +Z). Camera-frame scans are y-down (set −Y).
- **How it shows:** each point is drawn as a small round splat sized from the point spacing, fitted to Height. A scene's far outliers are left out.
- **Won't play:** `.las`, `.laz`, `.e57`, `.pcd`, `.xyz`. Convert them to PLY, for example with CloudCompare (File ▸ Save as ▸ PLY).

## 5. Volumetric video and frame sequences
Performances captured as one mesh, point cloud or splat file per frame.

**A folder of numbered frames** is the simplest:
```
Performance/
  frame_0001.obj
  frame_0001.mtl        (OBJ frames: their MTL and texture, per frame or shared)
  frame_0001.jpg
  frame_0002.obj
  …
  audio.mp3             (optional)
```
- **Frame formats:** `.obj`, `.ply` (mesh, points or splats), `.stl`, `.splat`, `.spz` or `.ksplat`, one kind per folder.
- **Frame rate:** set **Frame Rate** (Add Media asks; 30 by default).
- **On the headset too:** the Android build writes an index of StreamingAssets folders, so folder sequences work there.
- **Splat frames** are packed automatically by Add Media (section 2). Mesh and point frames are decoded on the headset a second or two ahead, so keep each frame light and check on the headset.

**Or a sequence file,** when frames live in subfolders or you want to set the frame rate and up axis in the content itself:
```json
{ "type": "sequence", "fps": 30, "frames": ["frames/f_0001.ply", "frames/f_0002.ply"], "audio": "audio.mp3", "up": "+y" }
{ "type": "sequence", "fps": 30, "pattern": "frames/frame_{0:D4}.obj", "start": 1, "count": 300 }
```
Paths are relative to the JSON; `up` is `+y`, `-y`, `+z` or `-z`.

**Won't play:** vendor formats (section 11), and sequences of `.glb` frames. Export OBJ or PLY frames instead.

## 6. 3D models (glTF and GLB)
The standard format for 3D models on the web (Sketchfab, Poly Haven, Khronos, most 3D tools).

- **Bring a `.glb`** (everything in one file) **or a `.gltf`** with its `.bin` and texture files. Add Media copies them keeping their relative paths.
- **What's supported:**
  - PBR materials and skinned characters.
  - Morph targets.
  - Animations: the first one is the timeline the controls play.
  - Lights, unlit materials and texture transforms.
  - Compressed meshes (Draco, `EXT_meshopt_compression`, quantisation) and KTX2 / Basis Universal textures.
- **Size:** fitted to Height whatever its units: a 79-unit Fox and a 6 cm avocado both stand 1.6 m tall. Up is +Y, as glTF defines it.
- **Budget:** game-ready models play smoothly (tested up to 71 meshes with an animation). For the headset, prefer GLB with KTX2 textures and textures of 2K or less.
- **Won't play:**
  - `EXT_texture_webp`: re-export with PNG, JPG or KTX2.
  - `KHR_meshopt_compression`: the new Khronos name; export with `EXT_meshopt_compression` or Draco.

## 7. Meshes (OBJ, PLY, STL)
Scans, photogrammetry and simple models.

| Format | Bring | Notes |
|---|---|---|
| `.obj` | The `.obj`, its `.mtl`, and the textures the MTL names, with their relative paths | Several materials work: each gets its own colour (`Kd`) and texture (`map_Kd`). Also vertex colours (`v x y z r g b`), quads and polygons, and names with spaces. An OBJ without `usemtl` uses its MTL's first material. |
| `.ply` mesh | The `.ply` (and its texture, named in a `comment TextureFile …` line) | Vertex colours, texture coordinates (`s t` or `u v`, or per-face `texcoord`). |
| `.stl` | The `.stl` | Binary or ASCII; no colour. Read as z-up, as 3D-printing files are. |

- **How it shows:** textured and vertex-coloured meshes draw **unlit**, so a scan keeps its captured lighting. Plain colours, or no colour at all (STL), draw **lit**, so the shape shows. Fitted to Height.
- **Orientation:** OBJ and PLY are read as y-up, STL as z-up. Set **Up** for a mesh that differs (many PLY scans are z-up).
- **Won't play:** FBX, Blend, USDZ, DAE, 3DS (section 10), and big-endian PLY.

## 8. Video

**Files:**

| Container | Codec | Notes |
|---|---|---|
| `.mp4`, `.m4v`, `.mov` | H.264 | Plays everywhere: the safest choice |
| `.mp4`, `.m4v`, `.mov` | H.265 / HEVC | Tag it `hvc1` so it also plays in the Mac editor: `ffmpeg -i in.mp4 -c copy -tag:v hvc1 out.mp4` |
| `.webm` | VP8 | VP9 and AV1 didn't play in the editor |

- **Sound:** the video's own AAC track plays (a 5.1 soundtrack loaded too).
- **From the internet:** use a direct HTTPS link to the file (progressive download). Live HLS (`.m3u8`) and DASH (`.mpd`) aren't supported.

**Kinds:**

| Kind | Frame | Detected automatically when… |
|---|---|---|
| Flat | Any shape | It's not 2:1 and the name doesn't say 360 or 180 |
| 360° mono | 2:1 equirectangular | The frame is 2:1, or the name says `360` |
| 360° stereo, top-bottom | 1:1 (left eye on top) | The name says `360` and the frame is square, or the name says `tb`, `ou` or `top-bottom` |
| 360° stereo, side-by-side | 4:1 (left eye on the left) | The name says `360` and `sbs`, `lr` or `side-by-side` |
| 180° stereo, side-by-side (VR180) | 2:1 | The name says `180`: a 2:1 180° frame is side-by-side |
| 180° mono | 1:1 | The name says `180` and the frame is square |

- **Name your files to make it automatic,** for example `concert_360.mp4`, `concert_360_tb.mp4`, `hike_180_sbs.mp4`. Hints count only as whole words, so `IMG_1805` isn't mistaken for 180°.
- **Otherwise set Format** (Flat, 360° or 180° video or photo) **and Stereo** on the Immersive Media. For example, a 1920×1024 side-by-side 360° video needs Format 360° and Stereo side-by-side.
- **Spherical metadata inside the file isn't read,** so the name, the shape or the settings decide.
- **How it shows:** flat video is an upright screen of **Screen Height** (1 m by default). 360° and 180° video surrounds you, facing where you looked when it opened.
- **Resolution:** 2048×2048 stereo was tested. 4K is the safe choice for the headset; check higher resolutions on it.
- **Won't play:**
  - Cubemap or EAC 360° video, and fisheye or mesh-projected VR180 straight from some cameras (`ytmp`): convert them to equirectangular.
  - Flat side-by-side 3D films play as the full frame.
  - MKV, AVI and WMV: re-encode them, for example `ffmpeg -i in.mkv -c:v libx264 -c:a aac out.mp4`.

## 9. Photos
- **Bring a `.jpg` or `.png`.** A PNG's transparency is kept on a flat photo.
- **Kinds:** the same as video: flat, 360° or 180°, mono or top-bottom/side-by-side stereo, detected the same way. A 2:1 photo is a 360° panorama; phone photospheres are 2:1. File names with any characters work: spaces, commas and Cyrillic were tested.
- **Size:** 4608×2304 and 4096×4096 were tested. Stay at or below 8192×4096 for the headset.
- **Before adding:** photos show as stored, and EXIF rotation isn't applied. Rotate them first if your viewer relies on it.
- **Won't play:** HEIC (including Apple Vision Pro spatial photos), WebP, AVIF, HDR, EXR, TIFF, BMP and GIF. Convert them to JPG or PNG.
  - A Google VR180 `.vr.jpg` shows only its left eye, because the right eye is hidden in its metadata.

## 10. Unity assets (FBX, Blend, USDZ, prefabs)
These formats can't load while the app runs, but Unity imports them in the editor.

1. Import the file into `Assets/` (Unity imports FBX, OBJ, DAE, 3DS and DXF natively, Blend if Blender is installed, and USD with Unity's USD package).
2. Drag it into the scene.
3. Right-click it ▸ **ImmersiveX ▸ Make Immersive.** It becomes grabbable with hands and controllers, floats where it's released and is held by a spatial anchor. Its animations play through its Animator.

To get Immersive Media's playback controls instead, export the model as GLB (for example from Blender) and add that.

## 11. Vendor volumetric formats
- **4DViews (`.4ds`), Arcturus HoloSuite (`.oms`) and Volograms (`.vols`)** are closed formats that only their own SDKs decode.
- **Playing them takes a small adapter** (an `IMediaCodec`) around the vendor's SDK. It then gets the same controls, handles and anchors.
- **Without one,** export the capture as an OBJ or PLY frame sequence (section 5) where the vendor's tools allow it.

## 12. Web experiences (three.js pages)
Interactive 3D web pages made with three.js, such as GenXR pipeline experiences.
- **Bring** one of these:
  - one `.html` file with everything inside it (scripts, sound and images embedded);
  - a folder whose page is `index.html`, with its scripts, models and sounds beside it and linked by relative paths;
  - a web address (`https://…`), which needs a connection on the headset.
- **How it shows:**
  - The page runs out of sight, as it is: its timeline, narration and captions stay in charge.
  - Its 3D scene is rebuilt in the room and moves as the page plays it.
  - The page's own controls, captions and cards show on a panel to your left, within reach. The ray clicks them and fingers press them. Move the panel by its gold side bars (ray or hand) or its frame (hand); it stays where you put it.
  - Immersive Media's own controls don't show, because the page is its own player.
  - When the content menu shows something else, the page waits where it is, with its sound paused. Picked again, it carries on.
- **Where it stands** (the same rule for every page, set once when the page has finished loading; nothing shows before):
  - **The page's ground is the room's floor.** A wide ground or studio floor is hidden so the real floor shows, and its level stays the floor even when the page hides it later. A page with no ground stands on the level most of its scene rests on.
  - **What the page shows first** (what its camera looks at) stands at the content's place. The scene is as tall as **Height**.
  - **Then it plays as written.** Things that fly away leave the room, and anything below the floor is hidden.
- **Parts:** the page's actors (GenXR pipeline pages), or else its top-level things, are selectable parts.
- **Sound** comes from the page; it isn't placed in the room.
- **Needs:** three.js's WebGLRenderer, version r170 or later (tested). Pages on other engines (Babylon.js, PlayCanvas, Unity WebGL) or on three.js's WebGPU renderer show on the panel only.
- **Not reproduced:**
  - custom shaders, which are drawn in their main colour;
  - post-processing such as bloom;
  - points and sprites;
  - the page's own "Enter VR/AR" button (ImmersiveX is the XR here).
- **On the headset** the page runs in Android's WebView. Local pages work offline.

## 13. 4D meshes (.sp4m)
A scene baked over time: the scene's own triangles part by part, every part's pose 30 times a second, and its colours (lighting, shadows and surface detail) twice a second. The GenXR 4D pipeline writes it from a three.js scene.

**Bring a folder:**
```
MyScene/
  scene.sp4m            (the container: gzipped inside, but never named .gz)
  soundtrack.m4a        (optional: the soundtrack the container names)
```
- **The name ends in `.sp4m`, not `.gz`.** Android's APK packer unzips `.gz` files and drops the extension, so the app couldn't find them. Add Media renames `name.sp4m.gz` to `name.sp4m` when it copies the folder (the player reads gzip either way), and the build check refuses a `.gz` name.
- **How it shows:**
  - Standing in the room: the scene's ground on the floor, its stage as tall as **Height**, facing you.
  - It plays on its soundtrack's clock with Immersive Media's controls (play/pause, seek, time, speaker). Captions show above the controls, and cards point at the parts when the content says.
  - Its parts are selectable: a ray click selects one, a hand picks it up, and it keeps moving with the scene.
  - The colours are baked, so nothing is relit and it looks the same in every room. Soft effects (steam, smoke, fire) are baked Gaussians, drawn like a hologram.
  - Things that fly away leave the room.
- **Budget:** a two-minute scene of about 160 objects and 400,000 triangles is a container of about 50 MB. It loads in under a second on a Quest 3 and runs at 72 fps; the moments with the most effects close to the viewer are the heaviest.
- **Won't play:** an animated GLB or a folder of GLB frames (section 5). Export the 4D mesh container from the pipeline instead.

---

## Checklist before you add content
- [ ] The format and structure match a section above.
- [ ] Companion files are next to the main file, with the relative paths the file uses (MTL and textures; `.bin` and images).
- [ ] You know which way is up: splats y-down, SPZ y-up, meshes and models y-up, STL z-up. Otherwise set **Up**.
- [ ] Captures you want at real size are in metres (4D packs keep their real size).
- [ ] Sequences: frames are numbered, one format per folder, and you know the frame rate.
- [ ] 360°/180° video and photos: the name says `360`/`180` (and `tb`/`sbs` for stereo), or you'll set Format and Stereo.
- [ ] Video is H.264 (or H.265 tagged `hvc1`) in MP4/MOV, or VP8 in WebM. Photos are JPG or PNG.
- [ ] Web pages: made with three.js, and either one self-contained `.html` or a folder with `index.html` whose files are linked by relative paths.
- [ ] 4D meshes: a folder with the `.sp4m` container (not named `.gz`) and its soundtrack.
- [ ] The size is reasonable for the app, and you're allowed to ship it.
- [ ] Added with **Add Media**, the scene saved, and the build's content check passes.

## Tested with
Each kind of content in this guide was tested with real files from public sources (2026-09-28 to 2026-10-15) in the Unity editor, and most kinds also in the Meta XR Simulator (Quest 3). Hologram streams, 4D Gaussian splats and 4D meshes have also played on a Quest 3. What testing showed:
- **Splats:** `.ply` files with misspelled or reordered properties, SH0 and compressed `.ply`, `.splat`, `.spz` v2–v4 and `.ksplat` levels 1–2 (up to about a million splats) all load. A scan's distant sky shell is left out; scans show tilted when they were captured that way.
- **Point clouds:** camera-frame captures need **Up** set to −Y.
- **Models:** glTF with animations, multi-file `.gltf`, Draco, KTX2, meshopt and morph targets all load.
- **Meshes:** ASCII and binary PLY, binary STL, multi-material OBJ and OBJ without `usemtl` all load.
- **Video and photos:** H.264, VP8 and MOV (5.1 audio), 2:1 360°, top-bottom and side-by-side stereo, and 16:9 panoramas with Format 360°. A side-by-side 360° file whose name doesn't say so needs **Format** and **Stereo** set.
- **Not supported** (each gives a message saying what to do): `.sog`, `.splatv`, `.pcd`, `.hdr`, `.exr`, `.heic`, `.usdz`, `.fbx`; glTF with WebP textures or `KHR_meshopt_compression`; H.265 `hev1`, VP9 and AV1 in the editor; cubemap and fisheye VR180 video.
