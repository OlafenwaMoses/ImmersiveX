# ImmersiveX content guide

Every kind of content ImmersiveX plays, and the shape it must be in so it goes into the app and plays with no code and no further changes to the content. If content follows the structure here, you add it in the Unity editor and build; the build tells you if anything is missing.

Everything below was tested from 2026-09-28 to 2026-10-15 with real files from public sources, in the Unity editor and in the Meta XR Simulator (Quest 3). Hologram streams, 4D Gaussian splats and 4D meshes have also played on a Quest 3; the other types in this guide still need a headset check. The [test results](#tested-with) are at the end.

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

## Placing content in the app

Every kind of content is placed the same way. Each section below adds what's specific to that kind under **Placing it**.

1. **Add it to a scene in the build.** Media goes in as **Immersive Media** (Add Media, or the component by hand); your own Unity objects go in with **Make Immersive** (section 10). The first scene in **File ▸ Build Profiles** is the one the app opens. Save the scene.
2. **Put it in order.** The content menu lists the scene's content in hierarchy order, top first: drag objects in the Hierarchy to reorder them (automation: `media first <name>`). With the menu on, one piece shows at a time: the first, until the user picks another (the app remembers the choice).
3. **Choose how it stands** (Immersive Media ▸ *Size and placement*). It always starts in front of the user, on the floor under them and facing them, when the app starts and whenever it's picked in the content menu:
   - **Distance From User:** how far ahead (2 m by default).
   - **Height:** how tall standing content is, in metres (1.6 by default); its width follows. Flat video and photos use **Screen Height** (1 m) instead, and 360°/180° media surrounds the user.
   - **Up:** which way is up in the file, when it differs from the format's convention.
   - **Spot:** only when several pieces show at once (content menu off): 0 is the centre and 1 and up stand around it; −1 follows the hierarchy order.
   - Standing content stands on the floor and faces the user.
4. **Choose how it plays** (*Playback*): **Play On Start** (off: it waits, paused, for Play), **Loop**, **Volume**, and **Frame Rate** for frame folders.
5. **Choose what the user can do:** **Show Controls**, **Show Handles** and **Selectable Parts**.
6. **Then the user places it.** It's anchored where it first stands. The user moves, turns and resizes it with its handles, or a hand, and it stays there through headset off/on, held by a spatial anchor. Nothing moves it by itself after that, until the next launch or pick in the content menu puts it in front of the user again.
7. **Test it** in the Meta XR Simulator (**ImmersiveX ▸ Play Mode ▸ Meta XR Simulator (Quest)**, then Play): pick it from the content menu, play it, move it. Then build; the build's content check stops if anything is missing.

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
| [Bare metal content](#14-bare-metal-content-your-own-app) | Your own app: Unity code on a **Bare Metal Content** object | A scene from **New Bare Metal Scene** | However the app decides: ImmersiveX only hosts it |

Every Immersive Media gets the same acrylic controls (play/pause, seek, time, Replay, speaker, volume) and handles around it (a move bar, turn bars and a turn ring, a resize corner). Each stays where you put it, held by a spatial anchor. Web experiences are the exception for controls: they bring their own player (section 12).

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

<p align="center"><img src="docs/images/content-menu-swipes.svg" alt="Content menu swipes: open with the right hand right to left or the left hand left to right; close with the reverse" width="720"></p>
<p align="center"><img src="docs/images/content-menu.png" alt="The content menu: the scene's content with the one showing highlighted, plus Quest menu and Close app" width="320"></p>

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

**Placing it:** Add Media with the file. It stands on the floor at **Height** whatever the capture's own size (a room scan becomes a diorama the user can enlarge up to 4 m with the resize corner). Set **Up** if it shows upside down or on its side, and **Max Gaussians** to trade detail for frame rate. A splat file is a still: Play is greyed out.

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

**Placing it:** Add Media with the folder; it asks for the frame rate and the most Gaussians a frame. The packed clip keeps its real size (Height is set from it when it's between 0.2 and 4 m) and stands on the ground the clip comes down to. Its soundtrack is the clock, so **Play On Start** and **Loop** work as for video.

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

**Placing it:** Add Media with the URL or the folder. It stands on the floor at **Height**. A stream with a `fit` in its `stream.json` keeps that one fit for the whole clip; otherwise it's fitted frame by frame and jumps at camera cuts. **Quality** picks the tier, and **Buffer Seconds** and **Preroll Seconds** tune streaming on slower Wi-Fi.

## 4. Point clouds
Scans stored as coloured points.

- **Bring a `.ply`** with `x y z` and optional colour (`red green blue`, as 0–255 or 0–1). Normals and extra elements are ignored. It must have no faces; a PLY with faces is read as a mesh.
- **Orientation:** read as y-up. Many LiDAR and CAD clouds are z-up (set **Up** to +Z). Camera-frame scans are y-down (set −Y).
- **How it shows:** each point is drawn as a small round splat sized from the point spacing, fitted to Height. A scene's far outliers are left out.
- **Placing it:** Add Media with the file. **Height** and **Up** as above; **Max Gaussians** caps how many points are drawn.
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

**Placing it:** Add Media with the folder or the sequence file, and set **Frame Rate**. **Height** fits the first frame, and that scale is kept, so a performer can walk around. A sound file in the folder (or `audio` in the JSON) is the clock.

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
- **Placing it:** Add Media with the `.glb`, or the folder holding the `.gltf`. It's fitted to **Height** and lit. **Selectable Parts** is on, so a model with two or more meshes has parts the user can pick up and put back. The first animation is what Play plays; a model without one is a still.
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
- **Placing it:** Add Media with the main file; its companions come with it. Set **Height** and **Up**. A mesh is a still: Play is greyed out.
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
- **Placing it:** Add Media with the file or a direct HTTPS link. A flat video is an upright screen of **Screen Height**, placed like any other content. A 360° or 180° video surrounds the user instead, facing where they looked when it opened, with its controls in front of them. Set **Format** and **Stereo** when detection can't tell; **Play On Start**, **Loop** and **Volume** as usual.
- **Won't play:**
  - Cubemap or EAC 360° video, and fisheye or mesh-projected VR180 straight from some cameras (`ytmp`): convert them to equirectangular.
  - Flat side-by-side 3D films play as the full frame.
  - MKV, AVI and WMV: re-encode them, for example `ffmpeg -i in.mkv -c:v libx264 -c:a aac out.mp4`.

## 9. Photos
- **Bring a `.jpg` or `.png`.** A PNG's transparency is kept on a flat photo.
- **Kinds:** the same as video: flat, 360° or 180°, mono or top-bottom/side-by-side stereo, detected the same way. A 2:1 photo is a 360° panorama; phone photospheres are 2:1. File names with any characters work: spaces, commas and Cyrillic were tested.
- **Size:** 4608×2304 and 4096×4096 were tested. Stay at or below 8192×4096 for the headset.
- **Before adding:** photos show as stored, and EXIF rotation isn't applied. Rotate them first if your viewer relies on it.
- **Placing it:** as for video (Add Media; **Screen Height** for a flat photo, a sphere around the user for 360°/180°). A photo is a still: Play is greyed out.
- **Won't play:** HEIC (including Apple Vision Pro spatial photos), WebP, AVIF, HDR, EXR, TIFF, BMP and GIF. Convert them to JPG or PNG.
  - A Google VR180 `.vr.jpg` shows only its left eye, because the right eye is hidden in its metadata.

## 10. Unity assets (FBX, Blend, USDZ, prefabs)
These formats can't load while the app runs, but Unity imports them in the editor.

1. Import the file into `Assets/` (Unity imports FBX, OBJ, DAE, 3DS and DXF natively, Blend if Blender is installed, and USD with Unity's USD package).
2. Drag it into the scene.
3. Right-click it ▸ **ImmersiveX ▸ Make Immersive.** It becomes grabbable with hands and controllers, floats where it's released and is held by a spatial anchor. Its animations play through its Animator.

**Placing it** (the **Immersive Content** component Make Immersive adds):
- **Placement:** *In Front Of User* (default) puts it at **Offset From User** the first time: 0.8 m ahead and 15 cm below the eyes. *Keep Scene Position* starts it where it is in the scene.
- Then the user moves it, and it stays where they leave it, held by a spatial anchor (its size too), until the next launch or pick in the content menu puts it in front of them again.
- It gets the handles and is listed in the content menu like media. **Selectable Parts** is off for Unity objects: turn it on when the object's meshes should be parts.

To get Immersive Media's playback controls instead, export the model as GLB (for example from Blender) and add that.

## 11. Vendor volumetric formats
- **4DViews (`.4ds`), Arcturus HoloSuite (`.oms`) and Volograms (`.vols`)** are closed formats that only their own SDKs decode.
- **Playing them takes a small adapter** (an `IMediaCodec`) around the vendor's SDK. It then gets the same controls, handles and anchors.
- **Without one,** export the capture as an OBJ or PLY frame sequence (section 5) where the vendor's tools allow it.
- **Placing it:** with an adapter, it's placed like any Immersive Media.

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
- **Placing it:** Add Media with the `.html`, the folder or the URL. The rule above decides where its scene stands relative to the content's place, and **Height** sets how tall it is. Its panel appears to the user's left. **Selectable Parts** turns its actors (or top-level things) into parts.

### Adding a web experience with an AI coding agent (recommended)
Web pages differ far more than files of one format do: how they load, what their ground is, how they draw and what their buttons do. So the best way to bring one in is to have an **AI coding agent** (such as Claude Code) add it and test it in the open editor, then adjust the page or its settings until it fits. The agent drives the editor through `EditorAutomation` (see [CONTRIBUTING](CONTRIBUTING.md#driving-the-open-editor-automation)): it adds the page, plays it in the Meta XR Simulator, looks at what the headset would show, presses the page's buttons, and reads the log.

**What to ask the agent** (adapt the paths and names):
> Add the web experience at `<file, folder or URL>` to the scene `<scene>` with Add Media, titled `<title>`. Test it in the Meta XR Simulator through the content menu: select it from the menu and start it from its own panel. Check that:
> - its scene stands on the floor at the content's place;
> - what moves on the page moves in the room;
> - its panel's buttons and captions work;
> - its parts can be selected, picked up and put back;
> - it pauses and resumes when the menu shows something else.
>
> Use the screenshots and the log. Fix what doesn't fit, in the page where you can, otherwise in the Immersive Media settings, and test again. Then run the build's content check and build the APK. Don't install it on a headset unless I ask.

**What the agent works with:**
- Edit mode: `media add [title=Name] <file, folder or URL>`, `media scene`, `media check`, `build metaquest`.
- Play mode: `playmode metaquest-simulator`, `play 0`, `contentmenu select <n>`, `capture`, `stop`, and the play log.
- The page itself: `media @<n> inspect` (the mirrored scene), `media @<n> node <name>` (where its objects with that name are), `media @<n> click <x> <y>` (press on its panel), `media @<n> js <code>` (run JavaScript in the page), `media @<n> parts`.

**What makes a page fit best** (what the agent checks for, or adds to the page):
- **It says when it's ready:** `window.__ready = true` once its scene is built and at its first moment, so it's placed then. Without it, ImmersiveX waits for the load event plus 2 s of stillness (20 s at most).
- **A real ground:** a wide flat mesh the scene stands on becomes the room's floor.
- **Standard three.js materials and transforms** for anything that moves or matters: custom shaders are drawn in their main colour, and motion done inside a shader doesn't reach the room.
- **Named parts:** a page that exposes a player with actors (`window.__player.actors`, each with a `root` object and an `info.title`, as GenXR pipeline pages do) gets those as titled parts; otherwise its top-level objects are the parts.
- **Its own controls in the page's HTML,** which show on the panel. It needs no "Enter VR/AR" button.
- **Self-contained, with relative paths,** on three.js's WebGLRenderer (r170 or later).

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
  - It plays on its soundtrack's clock with Immersive Media's controls (play/pause, seek, time, Replay, speaker). Captions show in front of the content, and cards point at the parts when the content says.
  - Its parts are selectable: a ray click selects one, a hand picks it up, and it keeps moving with the scene.
  - The colours are baked, so nothing is relit and it looks the same in every room. Soft effects (steam, smoke, fire) are baked Gaussians, drawn like a hologram.
  - **When its subject drives or flies away, the stage follows it:** the subject (a rocket climbing, cars driving off) stays at the content's place at the same size, and what it leaves behind slides away and dissolves at the edge of the room, or sinks below the floor.
- **Placing it:** Add Media with the folder. Its stage stands on the floor at **Height**, facing the user (the container says which side is its front). A container that says how tall it should stand (`stage.roomHeight`) sets **Height** for you. **Selectable Parts** gives the parts the container names. Its soundtrack is the clock.
- **Versions:** version 2 is coloured through a texture atlas. Version 3 is coloured per vertex, gives each object its own cutaway, lists its colour keyframes' times, and can say how the stage follows its subject and how wide the piece gets. Version 4 is the hologram look: no colours at all, just each object's hue, gains, contour spacing and height span, drawn as light added over the room with a rim glow, contour lines, a scan band and a materialise front; its glow and how far it has materialised come per frame.
- **Budget:** a two-minute scene of about 160 objects and 400,000 triangles is a container of about 50 MB. It loads in under a second on a Quest 3 and runs at 72 fps; the moments with the most effects close to the viewer are the heaviest.
- **Won't play:** an animated GLB or a folder of GLB frames (section 5). Export the 4D mesh container from the pipeline instead.

## 14. Bare metal content (your own app)
An app that owns everything, with ImmersiveX only as its host: its own menus, what it shows and where, and how the user moves about. ImmersiveX connects it to the headset and stays out of the way. [docs/bare-metal.md](docs/bare-metal.md) covers the host in full.
- **Bring** Unity code: components on an object with **Bare Metal Content**. **ImmersiveX ▸ New Bare Metal Scene** makes the scene, with that object ready. The component that starts the app implements `IBareMetalApp`, whose `OnHostReady` runs once the headset is ready.
- **What the host still does:** permissions, the see-through view, hiding the boundary, hands and controllers.
- **What it leaves out:** the content menu, the room-mapping prompt at start, placing content in front of the user and the MR/VR switch. Nothing ties the app to a mapped room, so the user can walk anywhere. The app turns any of these on when it wants them: **Content Menu** and **Map Room At Start** on the component, or `ImmersiveHost.MapRoom()` and `ImmersiveHost.SetView()` from code.
- **What the app gets** (`ImmersiveHost`): the head, the user and the floor; tracked hands and the content menu's swipe gestures; ray casts against the real world (depth on Quest 3 and 3S, the room's planes and furniture) and the app's colliders; the headset's camera, with rays through any point of a picture; spatial anchors; the view mode; the system menu.
- **The camera** (Quest 3 and 3S, Horizon OS v74 or later): turn on **Passthrough Camera** on the component. The build then gets the camera permissions, and the user is asked at start. Apps without it get no camera permission at all. In the editor a simulated camera sees the app's own scene.
- **Placing it:** the app places everything itself. `ImmersiveHost.UserPose` and `FloorHeight` say where the user is, and `ImmersiveHost.KeepInPlace` keeps an object where it's put, held by a spatial anchor.
- **Test it** in the Meta XR Simulator as usual. The automation's `host` commands drive it (`host status`, `host capture`, `host raycast`, `host press <button>` …). **Host Check** (`Assets/Demos/BareMetal`) is a small example app.

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
- [ ] Bare metal apps: a scene from **New Bare Metal Scene**, an `IBareMetalApp` on the app object, and **Passthrough Camera** on if the app reads the cameras.
- [ ] The size is reasonable for the app, and you're allowed to ship it.
- [ ] Placed as you want: its order in the scene, and its **Placement**, **Height** and **Up** (section [Placing content in the app](#placing-content-in-the-app)).
- [ ] Web experiences: added and tested by a coding agent in the Meta XR Simulator (section 12).
- [ ] Added with **Add Media**, the scene saved, tested in the Meta XR Simulator, and the build's content check passes.

## Tested with
Each kind of content in this guide was tested with real files from public sources (2026-09-28 to 2026-10-15) in the Unity editor, and most kinds also in the Meta XR Simulator (Quest 3). Hologram streams, 4D Gaussian splats and 4D meshes have also played on a Quest 3. What testing showed:
- **Splats:** `.ply` files with misspelled or reordered properties, SH0 and compressed `.ply`, `.splat`, `.spz` v2–v4 and `.ksplat` levels 1–2 (up to about a million splats) all load. A scan's distant sky shell is left out; scans show tilted when they were captured that way.
- **Point clouds:** camera-frame captures need **Up** set to −Y.
- **Models:** glTF with animations, multi-file `.gltf`, Draco, KTX2, meshopt and morph targets all load.
- **Meshes:** ASCII and binary PLY, binary STL, multi-material OBJ and OBJ without `usemtl` all load.
- **Video and photos:** H.264, VP8 and MOV (5.1 audio), 2:1 360°, top-bottom and side-by-side stereo, and 16:9 panoramas with Format 360°. A side-by-side 360° file whose name doesn't say so needs **Format** and **Stereo** set.
- **Not supported** (each gives a message saying what to do): `.sog`, `.splatv`, `.pcd`, `.hdr`, `.exr`, `.heic`, `.usdz`, `.fbx`; glTF with WebP textures or `KHR_meshopt_compression`; H.265 `hev1`, VP9 and AV1 in the editor; cubemap and fisheye VR180 video.
