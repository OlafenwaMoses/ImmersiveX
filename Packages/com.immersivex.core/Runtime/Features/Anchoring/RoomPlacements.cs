using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Remembers where content was put (F3), with the id of each piece's saved spatial anchor (see
    /// <see cref="ContentAnchor"/>). Poses are kept relative to the mapped room's floor, or to the tracking space when no
    /// room is mapped. They're the fallback when an anchor can't be found. One small JSON file per room is kept in
    /// <c>persistentDataPath/ImmersiveX/placements/</c>.
    /// </summary>
    public static class RoomPlacements
    {
        /// <summary>A saved placement: its world pose worked out from the room, its size (0 = none) and its saved anchor ("" = none).</summary>
        public struct Saved
        {
            public Pose Pose;
            public float Size;
            public string AnchorId;
        }

        /// <summary>The file for placements made while no room is mapped.</summary>
        const string Unmapped = "unmapped";

        static readonly Dictionary<string, PlacementIndex> Cache = new Dictionary<string, PlacementIndex>();

        static string Folder => Path.Combine(Application.persistentDataPath, "ImmersiveX", "placements");

        /// <summary>Where <paramref name="key"/> was left in the current room (or tracking space), if it was saved.</summary>
        public static bool TryLoad(string key, out Saved saved)
        {
            saved = default;
            if (!TryRoom(out var roomId, out var floor))
                return false;
            var entry = Load(roomId).Find(key);
            if (entry == null)
                return false;
            saved = new Saved { Pose = entry.LocalPose.GetTransformedBy(floor), Size = entry.size, AnchorId = entry.anchor };
            return true;
        }

        /// <summary>
        /// Save where <paramref name="key"/> is now (world pose) and its size (0 = none). The placement's saved anchor is
        /// cleared until <see cref="SetAnchor"/> records a new one; the old anchor's id is returned so it can be erased.
        /// </summary>
        public static string Save(string key, Pose pose, float size)
        {
            if (!TryRoom(out var roomId, out var floor))
                return string.Empty;
            var index = Load(roomId);
            var inverse = Quaternion.Inverse(floor.rotation);
            var local = new Pose(inverse * (pose.position - floor.position), inverse * pose.rotation);
            var previous = index.Set(key, local, size, DateTime.UtcNow);
            Write(roomId, index);
            return previous;
        }

        /// <summary>Record the saved anchor for <paramref name="key"/>'s current placement.</summary>
        public static void SetAnchor(string key, string anchorId)
        {
            if (TryRoom(out var roomId, out _) && Load(roomId).SetAnchor(key, anchorId))
                Write(roomId, Load(roomId));
        }

        /// <summary>
        /// Forget every placement in the current room (for example when the room is forgotten). Returns the ids of their
        /// saved anchors, to erase.
        /// </summary>
        public static List<string> ForgetRoom()
        {
            var anchors = new List<string>();
            if (!TryRoom(out var roomId, out _))
                return anchors;
            foreach (var entry in Load(roomId).Entries)
                if (!string.IsNullOrEmpty(entry.anchor))
                    anchors.Add(entry.anchor);
            Cache.Remove(roomId);
            var path = PathFor(roomId);
            if (File.Exists(path))
                File.Delete(path);
            return anchors;
        }

        /// <summary>The current room and its floor; with no mapped room, the tracking space.</summary>
        static bool TryRoom(out string roomId, out Pose floor)
        {
            roomId = null;
            floor = Pose.identity;
            var session = ImmersiveXSession.Instance;
            if (session == null)
                return false;
            if (session.CurrentSpace != null && session.Walkable != null && !string.IsNullOrEmpty(session.CurrentSpace.Id))
            {
                roomId = session.CurrentSpace.Id;
                floor = session.FloorPose;
                return true;
            }

            var trackables = session.Origin != null ? session.Origin.TrackablesParent : null;
            roomId = Unmapped;
            if (trackables != null)
                floor = new Pose(trackables.position, trackables.rotation);
            return true;
        }

        static PlacementIndex Load(string roomId)
        {
            if (Cache.TryGetValue(roomId, out var index))
                return index;
            index = new PlacementIndex();
            var path = PathFor(roomId);
            try
            {
                if (File.Exists(path))
                    index = PlacementIndex.FromJson(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Couldn't read saved placements ({exception.Message}); starting fresh.");
            }

            Cache[roomId] = index;
            return index;
        }

        static void Write(string roomId, PlacementIndex index)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = PathFor(roomId);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, index.ToJson());
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path); // written whole or not at all
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Couldn't save placements ({exception.Message}).");
            }
        }

        static string PathFor(string roomId) => Path.Combine(Folder, roomId + ".json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Cache.Clear();
    }
}
