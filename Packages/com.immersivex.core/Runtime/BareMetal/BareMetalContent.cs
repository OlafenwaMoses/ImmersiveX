using System;
using UnityEngine;
using UnityEngine.Events;

namespace ImmersiveX
{
    /// <summary>
    /// Bare metal content: an app that owns everything (its menus, what it shows and where, how the user moves about), with
    /// ImmersiveX only as its host. Put one on the app's root object. ImmersiveX then:
    /// <list type="bullet">
    /// <item>still does the headset's groundwork: picks the platform, asks for permissions, turns on the see-through view,
    /// hides the boundary, and sets up hands and controllers;</item>
    /// <item>leaves out everything of its own: no content menu, no MR/VR switch, no room-mapping prompt, no placing in front
    /// of the user. Nothing ties the app to a mapped room: the user can walk anywhere;</item>
    /// <item>gives the app the headset through <see cref="ImmersiveHost"/>: head and floor, the cameras, depth and room
    /// ray casts, spatial anchors, the room's planes and furniture, the view mode, the system menu.</item>
    /// </list>
    /// When the host is ready, every <see cref="IBareMetalApp"/> under this object gets <see cref="IBareMetalApp.OnHostReady"/>,
    /// then <c>Host Ready</c> runs.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/Bare Metal Content")]
    public sealed class BareMetalContent : MonoBehaviour
    {
        [SerializeField, Tooltip("Ask for the room's data at start (Quest: spatial data). Needed for depth ray casts, walls, floor and furniture.")]
        bool _roomAccess = true;

        [SerializeField, Tooltip("Run ImmersiveX's room mapping at start: a known room loads, a new one is mapped (Quest: Space Setup). " +
                                 "Off: nothing asks the user to map a room; call ImmersiveHost.MapRoom when the app wants it.")]
        bool _mapRoomAtStart;

        [SerializeField, Tooltip("The app reads the headset's cameras (Quest 3 and 3S, Horizon OS v74 or later). The build gets the camera " +
                                 "permissions and the user is asked at start.")]
        bool _passthroughCamera;

        [SerializeField, Tooltip("Hide the system boundary while the room shows through, so the user can walk anywhere.")]
        bool _hideBoundary = true;

        [SerializeField, Tooltip("Show ImmersiveX's content menu too (the swipe menu listing Immersive Media and Immersive Content). " +
                                 "Off: the app brings its own menu.")]
        bool _contentMenu;

        [SerializeField, Tooltip("Runs once the host is ready, after every IBareMetalApp under this object.")]
        UnityEvent _hostReady = new UnityEvent();

        public bool RoomAccess { get => _roomAccess; set => _roomAccess = value; }
        public bool MapRoomAtStart { get => _mapRoomAtStart; set => _mapRoomAtStart = value; }
        public bool PassthroughCamera { get => _passthroughCamera; set => _passthroughCamera = value; }
        public bool HideBoundary { get => _hideBoundary; set => _hideBoundary = value; }
        public bool ContentMenu { get => _contentMenu; set => _contentMenu = value; }

        /// <summary>True once the host is ready.</summary>
        public bool IsHostReady { get; private set; }

        /// <summary>Raised once the host is ready, after <see cref="IBareMetalApp.OnHostReady"/>.</summary>
        public event Action HostReady;

        /// <summary>Called by <see cref="ImmersiveXSession"/> when it's ready.</summary>
        internal void NotifyHostReady()
        {
            if (IsHostReady)
                return;
            IsHostReady = true;
            foreach (var app in GetComponentsInChildren<IBareMetalApp>(true))
            {
                try
                {
                    app.OnHostReady();
                }
                catch (Exception exception)
                {
                    ImmersiveXLog.Error($"Bare metal: {app.GetType().Name}.OnHostReady failed ({exception.GetBaseException().Message}).");
                    Debug.LogException(exception);
                }
            }

            _hostReady.Invoke();
            HostReady?.Invoke();
        }

        /// <summary>One line for logs: what the host does for the app.</summary>
        public string Describe() =>
            $"'{name}' owns the menus and the layout; the host gives it " +
            $"room data {(_roomAccess ? "on" : "off")}, room mapping {(!_roomAccess ? "off (no room data)" : _mapRoomAtStart ? "at start" : "on request")}, " +
            $"cameras {(_passthroughCamera ? "on" : "off")}, boundary {(_hideBoundary ? "hidden" : "the system's")}, " +
            $"content menu {(_contentMenu ? "on" : "off")}.";
    }
}
