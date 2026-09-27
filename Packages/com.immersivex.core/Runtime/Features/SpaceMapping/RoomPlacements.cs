using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Remembers where content was put in each mapped room: position and heading on the floor, and size, saved relative to
    /// the room's floor, so the next launch in the same room puts it back in the same real-world spot. One small JSON
    /// file per room in <c>persistentDataPath/ImmersiveX/placements/</c>.
    /// </summary>
    public static class RoomPlacements
    {
        [Serializable]
        sealed class Entry
        {
            public string key;
            public float x;
            public float z;
            public float yaw;
            public float size;
        }

        [Serializable]
        sealed class RoomFile
        {
            public List<Entry> placements = new List<Entry>();
        }

        static readonly Dictionary<string, RoomFile> Cache = new Dictionary<string, RoomFile>();

        static string Folder => Path.Combine(Application.persistentDataPath, "ImmersiveX", "placements");

        /// <summary>
        /// Where <paramref name="key"/> was left in the current room, as a world pose on the floor and its size, if it was
        /// saved and the room is mapped.
        /// </summary>
        public static bool TryLoad(string key, out Pose pose, out float size)
        {
            pose = default;
            size = 0f;
            if (!TryRoom(out var roomId, out var floor))
                return false;
            var entry = Load(roomId).placements.Find(e => e.key == key);
            if (entry == null)
                return false;
            var position = floor.position + floor.rotation * new Vector3(entry.x, 0f, entry.z);
            var forward = floor.rotation * (Quaternion.AngleAxis(entry.yaw, Vector3.up) * Vector3.forward);
            pose = new Pose(new Vector3(position.x, floor.position.y, position.z), Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up), Vector3.up));
            size = entry.size;
            return true;
        }

        /// <summary>Save where <paramref name="key"/> is now (world pose) and its size, in the current room.</summary>
        public static void Save(string key, Pose pose, float size)
        {
            if (!TryRoom(out var roomId, out var floor))
                return;
            var local = Quaternion.Inverse(floor.rotation) * (pose.position - floor.position);
            var forward = Quaternion.Inverse(floor.rotation) * (pose.rotation * Vector3.forward);
            var file = Load(roomId);
            var entry = file.placements.Find(e => e.key == key);
            if (entry == null)
                file.placements.Add(entry = new Entry { key = key });
            entry.x = local.x;
            entry.z = local.z;
            entry.yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            entry.size = size;
            Write(roomId, file);
        }

        /// <summary>Forget every placement in the current room (for example when the room is forgotten).</summary>
        public static void ForgetRoom()
        {
            if (!TryRoom(out var roomId, out _))
                return;
            Cache.Remove(roomId);
            var path = PathFor(roomId);
            if (File.Exists(path))
                File.Delete(path);
        }

        static bool TryRoom(out string roomId, out Pose floor)
        {
            roomId = null;
            floor = default;
            var session = ImmersiveXSession.Instance;
            if (session == null || session.CurrentSpace == null || session.Walkable == null)
                return false;
            roomId = session.CurrentSpace.Id;
            floor = session.FloorPose;
            return !string.IsNullOrEmpty(roomId);
        }

        static RoomFile Load(string roomId)
        {
            if (Cache.TryGetValue(roomId, out var file))
                return file;
            file = new RoomFile();
            var path = PathFor(roomId);
            try
            {
                if (File.Exists(path))
                    file = JsonUtility.FromJson<RoomFile>(File.ReadAllText(path)) ?? new RoomFile();
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Couldn't read saved placements ({exception.Message}); starting fresh.");
            }

            Cache[roomId] = file;
            return file;
        }

        static void Write(string roomId, RoomFile file)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = PathFor(roomId);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(file, true));
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
