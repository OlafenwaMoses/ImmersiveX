using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;

namespace ImmersiveX
{
    /// <summary>What a world ray cast can hit.</summary>
    [Flags]
    public enum WorldSurfaces
    {
        None = 0,

        /// <summary>The real world as the headset's depth sensing sees it (Quest 3 and 3S): any surface, furniture or object.</summary>
        Depth = 1 << 0,

        /// <summary>The room's planes and furniture boxes (Quest: Space Setup).</summary>
        Room = 1 << 1,

        /// <summary>The app's own colliders.</summary>
        Virtual = 1 << 2,

        Real = Depth | Room,
        All = Real | Virtual,
    }

    /// <summary>Where a world ray cast hit.</summary>
    public struct WorldHit
    {
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public WorldSurfaces Surface;

        /// <summary>The collider hit, for <see cref="WorldSurfaces.Virtual"/> hits.</summary>
        public Collider Collider;

        public override string ToString() => $"{Surface} at {Point:F2}, {Distance:0.00} m away";
    }

    /// <summary>
    /// The headset, for bare metal content (see <see cref="BareMetalContent"/>): everything ImmersiveX connects to, with
    /// nothing of ImmersiveX's own in the way. Use it once <see cref="IsReady"/> (or from <see cref="IBareMetalApp.OnHostReady"/>).
    /// <list type="bullet">
    /// <item>The user: <see cref="Head"/>, <see cref="UserPose"/>, <see cref="FloorHeight"/>, <see cref="Hands"/>, and
    /// <see cref="CreateSwipes"/> for the content menu's swipe gestures.</item>
    /// <item>The real world: <see cref="Raycast"/> against depth, the room and the app's colliders; <see cref="PassthroughCamera"/>
    /// for pictures of the room; <see cref="MapRoom"/>, <see cref="Room"/>, <see cref="Planes"/> and <see cref="Furniture"/>.</item>
    /// <item>Staying put: <see cref="Anchors"/> and <see cref="KeepInPlace"/>.</item>
    /// <item>The view: <see cref="SetView"/> (MR or VR), <see cref="HideBoundary"/> and <see cref="ShowBoundary"/>.</item>
    /// <item>The system: <see cref="OpenSystemMenu"/> and <see cref="Quit"/>.</item>
    /// </list>
    /// ImmersiveX's UI kit (<see cref="WorldUi"/>, <see cref="Brand"/>, <see cref="PanelMover"/>, <see cref="WithinReach"/>,
    /// <see cref="PromptPanel"/>) is there to build the app's own panels with.
    /// </summary>
    public static class ImmersiveHost
    {
        static readonly List<ARRaycastHit> s_hits = new List<ARRaycastHit>();
        static readonly List<XRHandSubsystem> s_handSubsystems = new List<XRHandSubsystem>();
        static HostCamera s_camera;
        static HostAnchors s_anchors;

        public static ImmersiveXSession Session => ImmersiveXSession.Instance;

        public static bool IsReady => Session != null && Session.State == SessionState.Ready;

        /// <summary>The scene's bare metal content, or null.</summary>
        public static BareMetalContent Content => Session != null ? Session.BareMetal : null;

        /// <summary>Raised when the session is ready (bare metal or not).</summary>
        public static event Action Ready;

        /// <summary>Run <paramref name="action"/> now if the host is ready, else once it is.</summary>
        public static void WhenReady(Action action)
        {
            if (action == null)
                return;
            if (IsReady)
            {
                action();
                return;
            }

            void Once()
            {
                Ready -= Once;
                action();
            }

            Ready += Once;
        }

        internal static void NotifyReady() => Ready?.Invoke();

        public static PlatformAdapter Platform => Session != null ? Session.Platform : null;
        public static PlatformCapabilities Capabilities => Platform != null ? Platform.Capabilities : new PlatformCapabilities();
        public static XROrigin Origin => Session != null ? Session.Origin : null;
        public static Camera Camera => Origin != null && Origin.Camera != null ? Origin.Camera : Camera.main;
        public static Transform Head => Camera != null ? Camera.transform : null;

        /// <summary>The head's pose in the world.</summary>
        public static Pose HeadPose => Head != null ? new Pose(Head.position, Head.rotation) : Pose.identity;

        /// <summary>Where the user stands and faces: the head's position, turned level.</summary>
        public static Pose UserPose => Head != null ? UserRelativePlacement.UserPose(Head) : Pose.identity;

        /// <summary>The height of the floor under the user (see <see cref="RoomFloor.Height"/>).</summary>
        public static float FloorHeight => RoomFloor.Height(Head);

        /// <summary>True when the user allowed access to the room's data (needed for depth ray casts and the room).</summary>
        public static bool HasRoomAccess => Session != null && Session.HasRoomAccess;

        /// <summary>The tracked hands, or null when hand tracking isn't running.</summary>
        public static XRHandSubsystem Hands
        {
            get
            {
                SubsystemManager.GetSubsystems(s_handSubsystems);
                foreach (var hands in s_handSubsystems)
                    if (hands.running)
                        return hands;
                return null;
            }
        }

        /// <summary>
        /// The content menu's sideways swipes, for the app's own menu: <see cref="HandSwipes.Swiped"/> says which hand swiped
        /// and which way.
        /// </summary>
        public static HandSwipes CreateSwipes() => Origin != null ? HandSwipes.Create(Origin) : null;

        // ---------------------------------------------------------------- the view

        public static ViewMode View => ViewModes.Instance != null ? ViewModes.Instance.Mode : ViewMode.Mixed;

        /// <summary>MR (the room shows through) or VR (a virtual world, with the system's boundary back).</summary>
        public static void SetView(ViewMode mode)
        {
            var modes = ViewModes.Instance;
            if (modes == null && Session != null && Capabilities.SeeThrough != SeeThroughMode.None)
                modes = ViewModes.Create(Session);
            if (modes != null)
                modes.Set(mode);
        }

        public static void HideBoundary()
        {
            if (Capabilities.CanHideBoundary && Services.TryGet<IBoundaryProvider>(out var boundary))
                boundary.RequestHide();
        }

        public static void ShowBoundary()
        {
            if (Services.TryGet<IBoundaryProvider>(out var boundary))
                boundary.RequestShow();
        }

        // ---------------------------------------------------------------- the room

        /// <summary>The mapped room, or null.</summary>
        public static MappedSpace Room => Session != null ? Session.CurrentSpace : null;

        /// <summary>
        /// Run ImmersiveX's room mapping now: a known room loads, a new one is mapped (Quest: Space Setup). Needs room access.
        /// <see cref="ImmersiveXSession.SpaceReady"/> is raised when it's done. Returns true when mapping started.
        /// </summary>
        public static bool MapRoom() => Session != null && Session.MapRoom();

        /// <summary>The room's walls, floor, ceiling and tables (Quest: from Space Setup), once the room is tracked.</summary>
        public static ARPlaneManager Planes => Origin != null ? Origin.GetComponent<ARPlaneManager>() : null;

        /// <summary>The room's furniture as boxes (Quest: from Space Setup), once the room is tracked.</summary>
        public static ARBoundingBoxManager Furniture => Origin != null ? Origin.GetComponent<ARBoundingBoxManager>() : null;

        /// <summary>Keep the room's planes and furniture up to date (on), or stop polling them (off: they stay as they are).</summary>
        public static void TrackRoom(bool on)
        {
            if (on && !HasRoomAccess)
            {
                ImmersiveXLog.Warn("Room: tracking needs room access, which wasn't granted.");
                return;
            }

            if (Planes != null)
                Planes.enabled = on;
            if (Furniture != null)
                Furniture.enabled = on;
        }

        // ---------------------------------------------------------------- ray casts

        /// <summary>
        /// The nearest surface along <paramref name="ray"/> within <paramref name="maxDistance"/> metres, among
        /// <paramref name="surfaces"/>. Depth needs room access and a headset with depth sensing; the room needs it mapped or
        /// tracked; virtual hits use <paramref name="layers"/> (triggers are ignored).
        /// </summary>
        public static bool Raycast(Ray ray, out WorldHit hit, float maxDistance = 10f, WorldSurfaces surfaces = WorldSurfaces.Real,
            int layers = Physics.DefaultRaycastLayers)
        {
            hit = default;
            var found = false;
            var nearest = maxDistance;

            if ((surfaces & WorldSurfaces.Depth) != 0)
            {
                var manager = RaycastManager();
                if (manager != null && manager.Raycast(ray, s_hits, TrackableType.Depth))
                    foreach (var depth in s_hits)
                        if (depth.distance > 0f && depth.distance < nearest)
                        {
                            nearest = depth.distance;
                            hit = new WorldHit { Point = depth.pose.position, Normal = depth.pose.up, Distance = depth.distance, Surface = WorldSurfaces.Depth };
                            found = true;
                        }
            }

            if ((surfaces & WorldSurfaces.Room) != 0)
            {
                if (Planes != null)
                    foreach (var plane in Planes.trackables)
                        if (RaycastPlane(ray, plane, nearest, out var onPlane))
                        {
                            nearest = onPlane.Distance;
                            hit = onPlane;
                            found = true;
                        }

                if (Furniture != null)
                    foreach (var box in Furniture.trackables)
                        if (RaycastBox(ray, new Pose(box.transform.position, box.transform.rotation), box.size, nearest, out var onBox))
                        {
                            nearest = onBox.Distance;
                            hit = onBox;
                            found = true;
                        }
            }

            if ((surfaces & WorldSurfaces.Virtual) != 0 && Physics.Raycast(ray, out var physics, nearest, layers, QueryTriggerInteraction.Ignore))
            {
                hit = new WorldHit { Point = physics.point, Normal = physics.normal, Distance = physics.distance, Surface = WorldSurfaces.Virtual, Collider = physics.collider };
                found = true;
            }

            return found;
        }

        /// <summary>A ray against a box of <paramref name="size"/> centred at <paramref name="pose"/>.</summary>
        public static bool RaycastBox(Ray ray, Pose pose, Vector3 size, float maxDistance, out WorldHit hit)
        {
            hit = default;
            var inverse = Quaternion.Inverse(pose.rotation);
            var origin = inverse * (ray.origin - pose.position);
            var direction = inverse * ray.direction;
            var half = size * 0.5f;
            var near = float.NegativeInfinity;
            var far = float.PositiveInfinity;
            var axis = 0;
            for (var i = 0; i < 3; i++)
            {
                if (Mathf.Abs(direction[i]) < 1e-6f)
                {
                    if (origin[i] < -half[i] || origin[i] > half[i])
                        return false;
                    continue;
                }

                var t1 = (-half[i] - origin[i]) / direction[i];
                var t2 = (half[i] - origin[i]) / direction[i];
                if (t1 > t2)
                    (t1, t2) = (t2, t1);
                if (t1 > near)
                {
                    near = t1;
                    axis = i;
                }

                far = Mathf.Min(far, t2);
                if (near > far)
                    return false;
            }

            if (near < 0f || near > maxDistance)
                return false;
            var normal = Vector3.zero;
            normal[axis] = direction[axis] > 0f ? -1f : 1f;
            hit = new WorldHit { Point = ray.GetPoint(near), Normal = pose.rotation * normal, Distance = near, Surface = WorldSurfaces.Room };
            return true;
        }

        static bool RaycastPlane(Ray ray, ARPlane plane, float maxDistance, out WorldHit hit)
        {
            hit = default;
            var surface = new Plane(plane.transform.up, plane.transform.position);
            if (!surface.Raycast(ray, out var distance) || distance <= 0f || distance > maxDistance)
                return false;
            var point = ray.GetPoint(distance);
            var local = plane.transform.InverseTransformPoint(point);
            var boundary = plane.boundary;
            if (boundary.Length >= 3 && !Polygon.Contains(boundary.ToArray(), new Vector2(local.x, local.z)))
                return false;
            hit = new WorldHit { Point = point, Normal = plane.transform.up, Distance = distance, Surface = WorldSurfaces.Room };
            return true;
        }

        /// <summary>The origin's ray cast manager, added once room access is granted (Quest's depth ray casts need it).</summary>
        static ARRaycastManager RaycastManager()
        {
            if (Origin == null || !HasRoomAccess)
                return null;
            var manager = Origin.GetComponent<ARRaycastManager>();
            if (manager == null)
                manager = Origin.gameObject.AddComponent<ARRaycastManager>();
            return manager.enabled && manager.subsystem != null && manager.subsystem.running ? manager : null;
        }

        // ---------------------------------------------------------------- anchors and the camera

        /// <summary>The platform's spatial anchors.</summary>
        public static HostAnchors Anchors => s_anchors ??= new HostAnchors();

        /// <summary>
        /// Keep <paramref name="target"/> where it's put in the real world, saved under <paramref name="key"/>: call
        /// <see cref="ContentAnchor.Restore"/> to find where it was left, and <see cref="ContentAnchor.Commit"/> whenever it's
        /// put somewhere new. It follows its anchor through tracking corrections and the headset coming off.
        /// </summary>
        public static ContentAnchor KeepInPlace(GameObject target, string key)
        {
            var anchor = target.GetComponent<ContentAnchor>();
            if (anchor == null)
                anchor = target.AddComponent<ContentAnchor>();
            anchor.Key = key;
            return anchor;
        }

        /// <summary>The headset's camera: pictures of the room, and rays through them.</summary>
        public static HostCamera PassthroughCamera => s_camera ??= new HostCamera();

        // ---------------------------------------------------------------- the system

        /// <summary>Show the system's own menu (Quest: the universal menu's dock). False when it couldn't be opened.</summary>
        public static bool OpenSystemMenu() => Services.TryGet<IDeviceMenuProvider>(out var device) && device.Open() != null;

        public static void Quit()
        {
            ImmersiveXLog.Info("Host: closing the app.");
            Application.Quit(); // ignored in the editor's Play mode
        }

        /// <summary>A folder for the app's own saved data (created if needed).</summary>
        public static string DataFolder(string app)
        {
            var folder = System.IO.Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(app) ? "App" : app);
            System.IO.Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>One line for logs and automation.</summary>
        public static string Describe() =>
            Session == null ? "No ImmersiveX Session."
            : $"session {Session.State} · {(Content != null ? "bare metal: " + Content.Describe() : "ImmersiveX content (no bare metal)")} · " +
              $"plan: {Session.Plan} · room access {(HasRoomAccess ? "yes" : "no")} · room {(Room != null ? Room.Name : "not mapped")} · " +
              $"floor {FloorHeight:0.00} m · camera: {PassthroughCamera.Status} · anchors {(Anchors.IsAvailable ? Anchors.CanPersist ? "saved between runs" : "this run only" : "none")} · " +
              $"depth ray casts {(Origin != null && Origin.GetComponent<ARRaycastManager>() != null ? "on" : "off")} · view {View}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Ready = null;
            s_camera = null;
            s_anchors = null;
        }
    }
}
