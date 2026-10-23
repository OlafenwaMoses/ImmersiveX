namespace ImmersiveX
{
    /// <summary>
    /// What <see cref="ImmersiveXSession"/> sets up at start: everything for ImmersiveX's own content, and only what it asks
    /// for when the scene has <see cref="BareMetalContent"/>.
    /// </summary>
    public readonly struct SessionPlan
    {
        public readonly bool RoomAccess;
        public readonly bool MapRoomAtStart;
        public readonly bool CameraAccess;
        public readonly bool HideBoundary;
        public readonly bool ContentMenu;

        /// <summary>The MR/VR switch at start (bare metal content gets it on request, from <see cref="ImmersiveHost.SetView"/>).</summary>
        public readonly bool ViewModes;

        /// <summary>Immersive Content is placed in front of the user at start.</summary>
        public readonly bool PlaceContent;

        public readonly bool IsBareMetal;

        SessionPlan(bool roomAccess, bool mapRoom, bool camera, bool hideBoundary, bool contentMenu, bool viewModes, bool bareMetal)
        {
            RoomAccess = roomAccess;
            MapRoomAtStart = mapRoom;
            CameraAccess = camera;
            HideBoundary = hideBoundary;
            ContentMenu = contentMenu;
            ViewModes = viewModes;
            PlaceContent = true;
            IsBareMetal = bareMetal;
        }

        public static SessionPlan For(BareMetalContent bareMetal, ImmersiveXSettings settings)
        {
            var menu = settings == null || settings.ContentMenu;
            if (bareMetal == null)
                return new SessionPlan(true, true, false, true, menu, true, false);
            return new SessionPlan(bareMetal.RoomAccess, bareMetal.RoomAccess && bareMetal.MapRoomAtStart, bareMetal.PassthroughCamera,
                bareMetal.HideBoundary, menu && bareMetal.ContentMenu, false, true);
        }

        public override string ToString() =>
            $"room data {(RoomAccess ? "yes" : "no")} · map at start {(MapRoomAtStart ? "yes" : "no")} · cameras {(CameraAccess ? "yes" : "no")} · " +
            $"hide boundary {(HideBoundary ? "yes" : "no")} · content menu {(ContentMenu ? "yes" : "no")} · MR/VR switch {(ViewModes ? "yes" : "on request")}";
    }
}
