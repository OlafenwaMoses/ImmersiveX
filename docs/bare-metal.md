# Bare metal content: your app, with ImmersiveX as its host

Every other kind of content plays inside ImmersiveX: ImmersiveX places it, lists it in its content menu, gives it controls and handles, and maps the room first. **Bare metal content** is the other way round. Your app owns everything: its menus, what it shows and where, how the user moves about. ImmersiveX only connects it to the headset.

Use it for a specialised app that needs the headset, not a player: an assistant that recognises what the user looks at, a training app, a tool.

## What the host does, and what it leaves to the app

| The host does | The host leaves out |
|---|---|
| Picks the platform adapter and its providers | The content menu (unless the app asks for it) |
| Asks for permissions: room data, and the cameras if the app reads them | The room-mapping briefing and Space Setup at start (unless the app asks) |
| Turns on the see-through view and hides the boundary, so the user can walk anywhere | Placing content in front of the user |
| Sets up hands and controllers (rays click, hands press and grab) | The MR/VR switch (the app switches with `ImmersiveHost.SetView`) |
| Tells the app when it's ready | Anything tied to a mapped room: the app works wherever the user goes |

## Making an app
1. **ImmersiveX ▸ New Bare Metal Scene.** The scene has the XR rig, the AR session, the ImmersiveX Session, and an app object with **Bare Metal Content**.
2. Put your components on the app object (or under it). Implement `IBareMetalApp` on the one that starts the app:
   ```csharp
   public sealed class MyApp : MonoBehaviour, IBareMetalApp
   {
       public void OnHostReady()
       {
           // see-through is on, permissions were asked, hands and controllers work
           var floor = ImmersiveHost.FloorHeight;
           var user = ImmersiveHost.UserPose;
           // build the app's own panels and objects here
       }
   }
   ```
3. Set the **Bare Metal Content** options:

   | Option | Default | What it does |
   |---|---|---|
   | Room Access | on | Asks for the room's data at start (Quest: spatial data). Depth ray casts, walls, floor and furniture need it. |
   | Map Room At Start | off | Runs ImmersiveX's room mapping at start. Off: nothing asks the user to map a room; call `ImmersiveHost.MapRoom()` when the app wants it. |
   | Passthrough Camera | off | The app reads the headset's cameras (Quest 3 and 3S, Horizon OS v74 or later). The build gets the camera permissions and the user is asked at start. |
   | Hide Boundary | on | Hides the system boundary while the room shows through. |
   | Content Menu | off | Shows ImmersiveX's content menu too, for Immersive Media and Immersive Content in the app's scene. |
   | Host Ready | — | A UnityEvent that runs after every `IBareMetalApp.OnHostReady`. |

4. Test it in the Meta XR Simulator (**ImmersiveX ▸ Play Mode ▸ Meta XR Simulator (Quest)**, then Play), and build as usual.

[`Assets/Demos/BareMetal/HostCheck.cs`](../Assets/Demos/BareMetal/HostCheck.cs) is a small, complete example: its own panel, toggled with the content menu's swipe, with a button for each host feature.

## The host: `ImmersiveHost`

Everything is static and usable from `OnHostReady` on.

### The user
| Member | What it gives |
|---|---|
| `Head`, `HeadPose`, `Camera` | The headset's camera, and its pose in the world |
| `UserPose` | Where the user stands and faces: the head's position, turned level |
| `FloorHeight` | The floor under the user: the mapped room's floor when they're in it, else the tracking origin's floor |
| `Hands` | The running `XRHandSubsystem` (joints of both hands), or null |
| `CreateSwipes()` | The content menu's sideways swipes, for the app's own menu: `Swiped(hand, direction)` |

### The real world
| Member | What it gives |
|---|---|
| `Raycast(ray, out hit, maxDistance, surfaces, layers)` | The nearest surface along a ray: `Depth` (Quest 3 and 3S depth sensing: any real surface or object), `Room` (planes and furniture), `Virtual` (the app's colliders). `WorldHit` has the point, normal, distance and what was hit. |
| `PassthroughCamera` | The headset's camera (below) |
| `MapRoom()` | Runs ImmersiveX's room mapping now; true when it started. `ImmersiveXSession.SpaceReady` says when it's done. |
| `Room`, `Planes`, `Furniture`, `TrackRoom(on)` | The mapped room, the AR Foundation managers for its planes and furniture, and keeping them up to date |
| `HasRoomAccess` | Whether the user allowed room data |

### The camera: `ImmersiveHost.PassthroughCamera`
```csharp
var camera = ImmersiveHost.PassthroughCamera;
if (await camera.StartAsync())                     // asks for access if needed, waits for the first frame
{
    using var frame = await camera.CaptureAsync(1024); // a CPU-readable picture, without stalling a frame
    byte[] jpg = frame.EncodeToJpg();                  // e.g. for a vision model
    Ray ray = frame.ImagePointToRay(new Vector2(0.4f, 0.3f)); // a point from the top left, 0 to 1
    if (ImmersiveHost.Raycast(ray, out var hit))
        Debug.Log($"That's {hit.Distance:0.00} m away, at {hit.Point}");
}
```
- Each `CameraFrame` keeps the camera's pose and intrinsics from when it was taken, so its rays stay right after the user moves. `WidthAt(fraction, distance)` turns a width in the picture into metres.
- **On Quest 3 and 3S** it's the Passthrough Camera API, read as Meta's samples do: Android's camera service finds the passthrough cameras, and their lens poses and intrinsics come from Camera2. Turn on **Passthrough Camera** on the Bare Metal Content, or the build won't have the permissions.
- **In the editor** it's a simulated camera that renders the app's own scene from the user's head (the simulators draw their passthrough themselves, so the real room isn't in it). Test with virtual objects in front of the user, and leave the app's own panels out with `SimulatedCullingMask`.
- Other headsets have no camera the app may read: `IsSupported` is false.

### Staying put
| Member | What it gives |
|---|---|
| `KeepInPlace(gameObject, key)` | Keeps an object where it's put, saved under a key: `Restore` finds where it was left, `Commit(pose, size)` saves where it is now. It follows its anchor through tracking corrections and the headset coming off. |
| `Anchors` | The platform's anchors directly: `AddAsync`, `SaveAsync` (an id), `LoadAsync(id)` (waits for the device to find it), `EraseAsync(id)`, `Remove` |

### The view and the system
| Member | What it does |
|---|---|
| `SetView(ViewMode.Mixed / Virtual)`, `View` | MR or VR. VR gives the system's boundary back and shows the real room's walls near the user. |
| `HideBoundary()`, `ShowBoundary()` | The system boundary |
| `OpenSystemMenu()` | The system's menu (Quest: the universal menu's dock); false where there's none |
| `Quit()` | Closes the app |
| `DataFolder(app)` | A folder for the app's own saved data |
| `Describe()` | One line on the host's state, for logs |

ImmersiveX's UI kit is there to build the app's panels with, in the same look: `WorldUi` (canvases, labels, buttons in millimetres), `Brand` (colours, rounded panels, pill buttons), `PanelMover` (side bars and frame to move a panel), `WithinReach` (keeps a panel at the user's side), `PromptPanel` (a message with buttons).

## Testing with automation
The editor's automation (see [CONTRIBUTING](../CONTRIBUTING.md#driving-the-open-editor-automation)) has `host` commands:
- Edit mode: `host scene <path> [component …] [+camera] [+map] [+menu] [-room] [name=App name]` creates a bare metal scene.
- Play mode: `host status`, `host camera start|stop`, `host capture [size]` (saves `host-camera.jpg`), `host raycast [u v] [real|virtual|all]`, `host target x y z [size]` (a test cube), `host anchors` (add, save, load and erase one), `host map`, `host view mr|vr`, `host swipe right|left [back]` and `host press <label>` (an app panel's button).

## Limits
- **The Meta XR Simulator** gave no depth ray-cast hits and can't run Space Setup; anchors add, save and erase there, but loads fail. Check those on a headset.
- **The Quest camera's pose** comes from the lens's place on the headset and the head's pose when the picture is read back, a few tens of milliseconds after the camera took it. Turning the head fast while taking a picture shifts its rays slightly.
