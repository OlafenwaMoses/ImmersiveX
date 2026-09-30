using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The saved placements of one room as plain data. For each piece of content it holds the pose relative to the room's
    /// floor, the size and the id of its saved spatial anchor. It's stored as JSON with a version number, so older files
    /// still load.
    /// </summary>
    public sealed class PlacementIndex
    {
        /// <summary>Version 2 added the anchor, full poses (for floating content) and the save time. Version 1 held x, z and yaw.</summary>
        public const int CurrentVersion = 2;

        [Serializable]
        public sealed class Entry
        {
            public string key;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public float size;
            public string anchor = string.Empty;
            public string savedAtUtc = string.Empty;

            /// <summary>The pose relative to the room's floor.</summary>
            public Pose LocalPose => new Pose(position, rotation);
        }

        [Serializable]
        sealed class FileData
        {
            public int version;
            public List<Entry> placements = new List<Entry>();
        }

        [Serializable]
        sealed class Version1File
        {
            public List<Version1Entry> placements = new List<Version1Entry>();
        }

        [Serializable]
        sealed class Version1Entry
        {
            public string key;
            public float x;
            public float z;
            public float yaw;
            public float size;
        }

        readonly List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(string key) => _entries.Find(e => e.key == key);

        /// <summary>
        /// Record where <paramref name="key"/> is now (relative to the floor). Its saved anchor no longer matches, so it's
        /// cleared; the old anchor id is returned so it can be erased.
        /// </summary>
        public string Set(string key, Pose local, float size, DateTime savedAtUtc)
        {
            var entry = Find(key);
            if (entry == null)
                _entries.Add(entry = new Entry { key = key });
            var previous = entry.anchor;
            entry.position = local.position;
            entry.rotation = local.rotation;
            entry.size = size;
            entry.anchor = string.Empty;
            entry.savedAtUtc = savedAtUtc.ToString("o", CultureInfo.InvariantCulture);
            return previous;
        }

        /// <summary>Attach the saved anchor for <paramref name="key"/>'s current placement. False when nothing is saved for it.</summary>
        public bool SetAnchor(string key, string anchorId)
        {
            var entry = Find(key);
            if (entry == null)
                return false;
            entry.anchor = anchorId ?? string.Empty;
            return true;
        }

        public string ToJson() => JsonUtility.ToJson(new FileData { version = CurrentVersion, placements = _entries }, true);

        public static PlacementIndex FromJson(string json)
        {
            var index = new PlacementIndex();
            if (string.IsNullOrWhiteSpace(json))
                return index;

            var file = JsonUtility.FromJson<FileData>(json);
            if (file != null && file.version >= 2)
            {
                foreach (var entry in file.placements)
                {
                    if (string.IsNullOrEmpty(entry.key))
                        continue;
                    entry.anchor ??= string.Empty;
                    var q = entry.rotation;
                    entry.rotation = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 1e-6f ? q.normalized : Quaternion.identity;
                    index._entries.Add(entry);
                }

                return index;
            }

            // Version 1: content standing on the floor, as x, z and a heading.
            foreach (var old in JsonUtility.FromJson<Version1File>(json)?.placements ?? new List<Version1Entry>())
            {
                if (!string.IsNullOrEmpty(old.key))
                    index._entries.Add(new Entry
                    {
                        key = old.key,
                        position = new Vector3(old.x, 0f, old.z),
                        rotation = Quaternion.AngleAxis(old.yaw, Vector3.up),
                        size = old.size,
                    });
            }

            return index;
        }
    }
}
