using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>Where the real floor is, and the middle of the mapped room: content stands there, and VR mode's floor too.</summary>
    public static class RoomFloor
    {
        // the floor under someone standing or sitting is between these heights below their eyes
        const float NearestBelowEyes = 0.4f;
        const float FarthestBelowEyes = 2.3f;
        const float StandingEyeHeight = 1.5f;

        /// <summary>
        /// True when <paramref name="floorY"/> can be the floor under eyes at <paramref name="head"/>: between 0.4 and 2.3 m
        /// below them. A floor further down (or up) belongs to another storey, such as a room mapped downstairs.
        /// </summary>
        public static bool IsUnder(float floorY, Transform head)
        {
            if (head == null)
                return true;
            var below = head.position.y - floorY;
            return below >= NearestBelowEyes && below <= FarthestBelowEyes;
        }

        /// <summary>True when the mapped room is the one the user is in: it's mapped and its floor is under them.</summary>
        public static bool RoomIsHere(Transform head)
        {
            var session = ImmersiveXSession.Instance;
            return session != null && session.Walkable != null && IsUnder(session.FloorPose.position.y, head);
        }

        /// <summary>
        /// The floor under the user: the mapped room's floor when it's the room they're in; else the XR Origin when tracking
        /// starts at the floor; else the lowest upward-facing surface under them; else a standing eye height below the head.
        /// Every floor found must be under the user's eyes (see <see cref="IsUnder"/>), so a room on another storey never
        /// puts content through the floor or the ceiling.
        /// </summary>
        public static float Height(Transform head)
        {
            var session = ImmersiveXSession.Instance;
            if (RoomIsHere(head))
                return session.FloorPose.position.y;

            var origin = session != null ? session.Origin : null;
            if (origin == null)
                return head != null ? head.position.y - StandingEyeHeight : 0f;
            if (origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor && IsUnder(origin.transform.position.y, head))
                return origin.transform.position.y;

            var eye = head != null ? head.position.y : origin.transform.position.y + StandingEyeHeight;
            var floor = eye - StandingEyeHeight;
            var found = false;
            var planes = origin.GetComponent<ARPlaneManager>();
            if (planes != null)
            {
                foreach (var plane in planes.trackables)
                {
                    var y = plane.transform.position.y;
                    if (plane.alignment == PlaneAlignment.HorizontalUp && y < eye - 0.8f && y >= eye - FarthestBelowEyes && (!found || y < floor))
                    {
                        floor = y;
                        found = true;
                    }
                }
            }

            return floor;
        }

        /// <summary>The middle of the mapped room's walkable area, in world space.</summary>
        public static bool TryCentre(out Vector3 centre)
        {
            centre = default;
            var session = ImmersiveXSession.Instance;
            var middle = session != null && session.Walkable != null ? session.Walkable.Centre() : null;
            if (!middle.HasValue)
                return false;
            var floor = session.FloorPose;
            centre = floor.position + floor.rotation * new Vector3(middle.Value.x, 0f, middle.Value.y);
            return true;
        }
    }
}
