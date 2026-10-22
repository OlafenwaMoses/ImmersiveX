using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>Where the real floor is, and the middle of the mapped room: content stands there, and VR mode's floor too.</summary>
    public static class RoomFloor
    {
        /// <summary>
        /// The floor under the user: the mapped room's floor; else the XR Origin when tracking starts at the floor; else the
        /// lowest upward-facing surface found so far; else a standing eye height below the head.
        /// </summary>
        public static float Height(Transform head)
        {
            const float StandingEyeHeight = 1.5f;
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Walkable != null)
                return session.FloorPose.position.y;

            var origin = session != null ? session.Origin : null;
            if (origin == null)
                return head != null ? head.position.y - StandingEyeHeight : 0f;
            if (origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor)
                return origin.transform.position.y;

            var eye = head != null ? head.position.y : origin.transform.position.y;
            var floor = eye - StandingEyeHeight;
            var found = false;
            var planes = origin.GetComponent<ARPlaneManager>();
            if (planes != null)
            {
                foreach (var plane in planes.trackables)
                {
                    var y = plane.transform.position.y;
                    if (plane.alignment == PlaneAlignment.HorizontalUp && y < eye - 0.8f && (!found || y < floor))
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
