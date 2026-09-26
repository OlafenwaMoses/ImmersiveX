using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>What is known about the room right now: its floor, walls, ceiling height and furniture count.</summary>
    public sealed class RoomSnapshot
    {
        const float MinimumFloorArea = 1f;   // m²; smaller horizontal planes are tables and shelves
        const float MinimumWallArea = 0.5f;  // m²

        public string FloorId { get; private set; }
        public Pose FloorPose { get; private set; } = Pose.identity;

        /// <summary>Floor outline in floor-plane space (x, z). On Quest this follows the walls from Space Setup.</summary>
        public Vector2[] FloorPolygon { get; private set; }

        /// <summary>Lengths of the detected walls in metres.</summary>
        public List<float> WallLengths { get; } = new List<float>();

        /// <summary>Floor-to-ceiling height in metres, or 0 when no ceiling was detected.</summary>
        public float CeilingHeight { get; private set; }

        public int FurnitureCount { get; private set; }
        public int PlaneCount { get; private set; }

        public bool HasFloor => FloorPolygon != null && FloorPolygon.Length >= 3;

        /// <summary>Read the room from the AR Foundation managers.</summary>
        public static RoomSnapshot Capture(ARPlaneManager planes, ARBoundingBoxManager boxes)
        {
            var samples = new List<PlaneSample>();
            if (planes != null)
            {
                foreach (var plane in planes.trackables)
                {
                    var boundary = plane.boundary;
                    samples.Add(new PlaneSample
                    {
                        Id = plane.trackableId.ToString(),
                        Alignment = plane.alignment,
                        Classifications = plane.classifications,
                        Pose = new Pose(plane.transform.position, plane.transform.rotation),
                        Boundary = boundary.IsCreated ? boundary.ToArray() : new Vector2[0],
                        Size = plane.size,
                    });
                }
            }

            return From(samples, boxes != null ? boxes.trackables.count : 0);
        }

        /// <summary>Build a snapshot from plane copies. Pure logic, so it can be tested without a device.</summary>
        public static RoomSnapshot From(IReadOnlyList<PlaneSample> planes, int furnitureCount)
        {
            var snapshot = new RoomSnapshot { FurnitureCount = furnitureCount, PlaneCount = planes.Count };
            var floor = PickFloor(planes);
            if (floor.HasValue)
            {
                snapshot.FloorId = floor.Value.Id;
                snapshot.FloorPose = floor.Value.Pose;
                snapshot.FloorPolygon = floor.Value.Boundary;
            }

            float ceilingY = float.NegativeInfinity;
            foreach (var plane in planes)
            {
                var isWall = plane.Is(PlaneClassifications.WallFace) || plane.Is(PlaneClassifications.InvisibleWallFace) ||
                             (plane.Alignment == PlaneAlignment.Vertical && plane.Classifications == PlaneClassifications.None);
                if (isWall && plane.Area >= MinimumWallArea)
                    snapshot.WallLengths.Add(plane.Size.x);

                var isCeiling = plane.Is(PlaneClassifications.Ceiling) ||
                                (plane.Alignment == PlaneAlignment.HorizontalDown && plane.Classifications == PlaneClassifications.None);
                if (isCeiling && plane.Pose.position.y > ceilingY)
                    ceilingY = plane.Pose.position.y;
            }

            if (floor.HasValue && !float.IsNegativeInfinity(ceilingY))
                snapshot.CeilingHeight = Mathf.Max(0f, ceilingY - floor.Value.Pose.position.y);
            snapshot.WallLengths.Sort((a, b) => b.CompareTo(a));
            return snapshot;
        }

        /// <summary>A plane labelled Floor (the largest), otherwise the lowest large upward-facing plane.</summary>
        static PlaneSample? PickFloor(IReadOnlyList<PlaneSample> planes)
        {
            PlaneSample? labelled = null;
            PlaneSample? lowest = null;
            foreach (var plane in planes)
            {
                if (plane.Boundary == null || plane.Boundary.Length < 3)
                    continue;
                if (plane.Is(PlaneClassifications.Floor))
                {
                    if (!labelled.HasValue || plane.Area > labelled.Value.Area)
                        labelled = plane;
                }
                else if (plane.Alignment == PlaneAlignment.HorizontalUp && plane.Area >= MinimumFloorArea)
                {
                    if (!lowest.HasValue || plane.Pose.position.y < lowest.Value.Pose.position.y - 0.05f ||
                        (Mathf.Abs(plane.Pose.position.y - lowest.Value.Pose.position.y) <= 0.05f && plane.Area > lowest.Value.Area))
                        lowest = plane;
                }
            }

            return labelled ?? lowest;
        }
    }
}
