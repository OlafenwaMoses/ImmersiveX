using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ImmersiveX
{
    /// <summary>How the user sees content: in their room, or in a virtual world.</summary>
    public enum ViewMode
    {
        /// <summary>Mixed reality: the real room shows through and content stands in it (the default).</summary>
        Mixed,

        /// <summary>Virtual reality: the real room is hidden and content stands in <see cref="VirtualWorld"/>.</summary>
        Virtual,
    }

    /// <summary>
    /// Switches between mixed reality and virtual reality (the content menu's MR and VR buttons). Every start is in MR.
    /// <list type="bullet">
    /// <item>VR turns the passthrough off, gives the system's boundary back (a Quest shows its guardian when the real world
    /// is hidden), and puts the user in <see cref="VirtualWorld"/>, with the real room's walls and furniture shown near them
    /// by <see cref="RoomGuard"/>.</item>
    /// <item>Nothing else changes: content keeps its place in the real room (its anchors, the mapped floor and walls), so
    /// switching back and forth never moves it.</item>
    /// <item>MR turns the passthrough on again and hides the boundary once the passthrough is showing.</item>
    /// </list>
    /// <see cref="ImmersiveXSession"/> adds it when the session is ready, on platforms that show the real world.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ViewModes : MonoBehaviour
    {
        const float PassthroughWarmUpSeconds = 0.5f;

        public static ViewModes Instance { get; private set; }

        ImmersiveXSession _session;
        Camera _camera;
        VirtualWorld _world;
        RoomGuard _guard;
        Coroutine _hideBoundary;

        public ViewMode Mode { get; private set; } = ViewMode.Mixed;

        /// <summary>The mode changed (the menu repaints its buttons).</summary>
        public event Action<ViewMode> Changed;

        public VirtualWorld World => _world;
        public RoomGuard Guard => _guard;

        public static ViewModes Create(ImmersiveXSession session)
        {
            var modes = new GameObject("View Modes").AddComponent<ViewModes>();
            Instance = modes;
            modes._session = session;
            modes._camera = session != null && session.Origin != null ? session.Origin.Camera : Camera.main;
            modes._world = VirtualWorld.Create(modes.transform, modes._camera != null ? modes._camera.transform : null);
            modes._guard = RoomGuard.Create(modes.transform);
            if (session != null)
                session.SpaceReady += _ => { if (modes != null && modes.Mode == ViewMode.Virtual) modes.EnterVirtual(); }; // a re-created room: its new walls
            return modes;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Toggle() => Set(Mode == ViewMode.Mixed ? ViewMode.Virtual : ViewMode.Mixed);

        public void Set(ViewMode mode)
        {
            if (mode == Mode)
                return;
            Mode = mode;
            if (mode == ViewMode.Virtual)
                EnterVirtual();
            else
                EnterMixed();
            Changed?.Invoke(mode);
        }

        void EnterVirtual()
        {
            if (_hideBoundary != null)
            {
                StopCoroutine(_hideBoundary);
                _hideBoundary = null;
            }

            if (Services.TryGet<IBoundaryProvider>(out var boundary))
                boundary.RequestShow(); // the real world is about to be hidden: the system's boundary keeps the user safe
            if (_camera != null)
                SeeThrough.Remove(_camera, VirtualWorld.Horizon);

            // the virtual floor lies on the real one, its pool of light where content stands (as content finds them)
            var head = _camera != null ? _camera.transform : null;
            var floorHeight = RoomFloor.Height(head);
            var here = RoomFloor.RoomIsHere(head); // the mapped room is the one the user is in (not another storey)
            if (!here || !RoomFloor.TryCentre(out var centre))
                centre = head != null ? head.position + Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized * 1.5f : Vector3.zero;
            centre.y = floorHeight;
            _world.Show(floorHeight, centre);
            var floor = here ? _session.FloorPose : new Pose(new Vector3(0f, floorHeight, 0f), Quaternion.identity);
            var boxes = _session != null && _session.Origin != null ? _session.Origin.GetComponentInChildren<ARBoundingBoxManager>(true) : null;
            var furniture = new List<(Pose, Vector3)>();
            if (boxes != null)
                foreach (var box in boxes.trackables)
                {
                    var y = box.transform.position.y - floorHeight;
                    if (y > -0.3f && y < 2.5f) // on this floor, not another storey's
                        furniture.Add((new Pose(box.transform.position, box.transform.rotation), box.size));
                }
            _guard.Show(here ? _session.CurrentSpace?.FloorPolygon : null, floor, here ? _session.CeilingHeight : 0f, furniture);
            ImmersiveXLog.Info($"View: VR · the real world is hidden; the virtual world's floor at {floorHeight:0.00} m around {centre:F2} · " +
                               $"the room shown near the user: {_guard.Walls} wall(s), {_guard.Furniture} piece(s) of furniture" +
                               (_guard.Furniture > 0 ? $" (at {string.Join(", ", _guard.FurnitureAt.ConvertAll(p => $"{p.x:0.0}, {p.z:0.0}"))})." : "."));
        }

        void EnterMixed()
        {
            _world.Hide();
            _guard.Hide();
            var seeThrough = _session != null && _session.Platform != null ? _session.Platform.Capabilities.SeeThrough : SeeThroughMode.VideoPassthrough;
            if (_camera != null)
                SeeThrough.Apply(_camera, seeThrough);
            if (_session != null && _session.Platform != null && _session.Platform.Capabilities.CanHideBoundary)
                _hideBoundary = StartCoroutine(HideBoundaryOnceShowing());
            ImmersiveXLog.Info("View: MR · the real room shows through.");
        }

        IEnumerator HideBoundaryOnceShowing()
        {
            // the boundary can only be hidden once the passthrough is actually on screen
            yield return new WaitForSecondsRealtime(PassthroughWarmUpSeconds);
            if (Mode == ViewMode.Mixed && Services.TryGet<IBoundaryProvider>(out var boundary))
                boundary.RequestHide();
            _hideBoundary = null;
        }

        /// <summary>One line for logs and automation.</summary>
        public string Describe() =>
            Mode == ViewMode.Virtual
                ? $"VR · virtual world {(_world.IsShowing ? "showing" : "hidden")} · room guard {_guard.Walls} wall(s), {_guard.Furniture} furniture"
                : "MR · the real room shows through";
    }
}
