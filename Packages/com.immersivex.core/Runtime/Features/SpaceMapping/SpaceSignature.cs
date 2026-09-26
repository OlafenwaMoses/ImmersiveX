using System;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// A room's layout fingerprint: floor size, ceiling height and wall lengths. Used to recognise a room
    /// when the platform can't identify it itself (for example after it has been scanned again).
    /// </summary>
    [Serializable]
    public sealed class SpaceSignature
    {
        const int MaxWalls = 8;

        public float FloorArea;
        public float FloorWidth;   // shorter side of the floor's bounding rectangle
        public float FloorLength;  // longer side
        public float CeilingHeight;
        public float[] WallLengths = new float[0];

        public static SpaceSignature From(RoomSnapshot room)
        {
            var signature = new SpaceSignature { CeilingHeight = room.CeilingHeight };
            if (room.HasFloor)
            {
                signature.FloorArea = Polygon.Area(room.FloorPolygon);
                var size = Polygon.Bounds(room.FloorPolygon).size;
                signature.FloorWidth = Mathf.Min(size.x, size.y);
                signature.FloorLength = Mathf.Max(size.x, size.y);
            }

            var count = Mathf.Min(MaxWalls, room.WallLengths.Count);
            signature.WallLengths = room.WallLengths.GetRange(0, count).ToArray();
            return signature;
        }

        /// <summary>How alike two rooms are, from 0 (nothing in common) to 1 (identical layout).</summary>
        public float Similarity(SpaceSignature other)
        {
            var area = Ratio(FloorArea, other.FloorArea);
            var dimensions = (Ratio(FloorWidth, other.FloorWidth) + Ratio(FloorLength, other.FloorLength)) * 0.5f;
            var ceiling = CeilingHeight > 0f && other.CeilingHeight > 0f
                ? 1f - Mathf.Clamp01(Mathf.Abs(CeilingHeight - other.CeilingHeight) / 0.5f)
                : 1f; // unknown on one side: neither for nor against
            return 0.35f * area + 0.25f * dimensions + 0.15f * ceiling + 0.25f * WallSimilarity(other);
        }

        float WallSimilarity(SpaceSignature other)
        {
            var mine = WallLengths ?? new float[0];
            var theirs = other.WallLengths ?? new float[0];
            if (mine.Length == 0 && theirs.Length == 0)
                return 1f;
            if (mine.Length == 0 || theirs.Length == 0)
                return 0.5f;

            var count = Mathf.Min(mine.Length, theirs.Length);
            var lengths = 0f;
            for (var i = 0; i < count; i++)
                lengths += Ratio(mine[i], theirs[i]);
            lengths /= count;
            var countMatch = 1f - Mathf.Clamp01(Mathf.Abs(mine.Length - theirs.Length) / (float)Mathf.Max(mine.Length, theirs.Length));
            return 0.5f * lengths + 0.5f * countMatch;
        }

        /// <summary>1 when equal, falling towards 0 as the relative difference grows.</summary>
        static float Ratio(float a, float b)
        {
            var largest = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
            return largest < 1e-4f ? 1f : 1f - Mathf.Clamp01(Mathf.Abs(a - b) / largest);
        }
    }
}
