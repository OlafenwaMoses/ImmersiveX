using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>A copy of one detected plane: everything room mapping needs, without holding on to AR Foundation objects.</summary>
    public struct PlaneSample
    {
        public string Id;
        public PlaneAlignment Alignment;
        public PlaneClassifications Classifications;

        /// <summary>Plane pose in world space. Its local X/Z axes span the plane.</summary>
        public Pose Pose;

        /// <summary>Boundary polygon in plane space (x, z), as AR Foundation reports it.</summary>
        public Vector2[] Boundary;

        /// <summary>Width and height of the plane's bounding rectangle, in metres.</summary>
        public Vector2 Size;

        public float Area => Size.x * Size.y;
        public bool Is(PlaneClassifications classification) => (Classifications & classification) != 0;
    }
}
