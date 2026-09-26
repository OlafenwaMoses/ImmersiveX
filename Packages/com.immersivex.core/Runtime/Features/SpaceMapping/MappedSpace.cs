using System;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// A room ImmersiveX has mapped and saved on the device. Geometry is kept in floor-plane space, so it stays
    /// valid across sessions even though world coordinates change every time the app starts.
    /// </summary>
    [Serializable]
    public sealed class MappedSpace
    {
        public string Id;
        public string Name;
        public string PlatformId;

        /// <summary>The platform's own identifier for the room (Quest: the Space Setup floor ID), or empty.</summary>
        public string NativeKey;

        public string CreatedUtc;
        public string UpdatedUtc;
        public SpaceSignature Signature;
        public Vector2[] FloorPolygon;
        public float WallMargin;
        public float WalkableArea;

        public static MappedSpace Create(string name, string platformId)
        {
            var now = DateTime.UtcNow.ToString("o");
            return new MappedSpace { Id = Guid.NewGuid().ToString("N"), Name = name, PlatformId = platformId, CreatedUtc = now, UpdatedUtc = now };
        }

        /// <summary>Replace the geometry with a fresh scan of the same room.</summary>
        public void Update(RoomSnapshot room, string nativeKey, float wallMargin, float walkableArea)
        {
            NativeKey = nativeKey ?? string.Empty;
            Signature = SpaceSignature.From(room);
            FloorPolygon = room.FloorPolygon;
            WallMargin = wallMargin;
            WalkableArea = walkableArea;
            UpdatedUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
