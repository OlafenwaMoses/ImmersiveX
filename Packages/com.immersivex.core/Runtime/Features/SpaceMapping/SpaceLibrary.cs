using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Saved rooms, one JSON file each under <c>persistentDataPath/ImmersiveX/spaces/</c>. Local only: room data
    /// never leaves the device (ADR-0006). Writes go to a temporary file first, so a crash can't corrupt a room.
    /// </summary>
    public sealed class SpaceLibrary
    {
        readonly string _folder;

        public SpaceLibrary(string folder = null)
        {
            _folder = folder ?? Path.Combine(Application.persistentDataPath, "ImmersiveX", "spaces");
        }

        public string Folder => _folder;

        public List<MappedSpace> LoadAll()
        {
            var spaces = new List<MappedSpace>();
            if (!Directory.Exists(_folder))
                return spaces;

            foreach (var file in Directory.GetFiles(_folder, "*.json"))
            {
                try
                {
                    var space = JsonUtility.FromJson<MappedSpace>(File.ReadAllText(file));
                    if (space != null && !string.IsNullOrEmpty(space.Id))
                        spaces.Add(space);
                }
                catch (Exception exception)
                {
                    ImmersiveXLog.Warn($"Skipped unreadable saved room {Path.GetFileName(file)}: {exception.Message}");
                }
            }

            spaces.Sort((a, b) => string.CompareOrdinal(a.CreatedUtc, b.CreatedUtc));
            return spaces;
        }

        public void Save(MappedSpace space)
        {
            Directory.CreateDirectory(_folder);
            var path = PathFor(space.Id);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(space, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }

        public bool Delete(string id)
        {
            var path = PathFor(id);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>"Room 1", "Room 2", … the first name not already used.</summary>
        public string NextName()
        {
            var used = new HashSet<string>();
            foreach (var space in LoadAll())
                used.Add(space.Name);
            for (var i = 1; ; i++)
            {
                var name = $"Room {i}";
                if (!used.Contains(name))
                    return name;
            }
        }

        string PathFor(string id) => Path.Combine(_folder, id + ".json");
    }
}
