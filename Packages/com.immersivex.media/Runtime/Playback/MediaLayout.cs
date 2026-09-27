using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Where several pieces of media stand when none has a saved spot: the first at the anchor (the room's centre), the
    /// others on a ring around it — beside it first, then behind — never directly between the first one and the user.
    /// </summary>
    static class MediaLayout
    {
        const float Spacing = 1.3f;

        /// <summary>Degrees around the anchor, measured from the direction towards the user.</summary>
        static readonly float[] Angles = { 90f, -90f, 135f, -135f, 180f, 45f, -45f };

        static readonly List<ImmersiveMedia> Released = new List<ImmersiveMedia>();

        /// <summary>
        /// The spot number for <paramref name="media"/>: its place in the scene's hierarchy among the media that take a
        /// spot, so the first one in the hierarchy stands at the centre whatever order they load in.
        /// </summary>
        public static int Claim(ImmersiveMedia media)
        {
            Released.Remove(media);
            var all = new List<ImmersiveMedia>(Object.FindObjectsByType<ImmersiveMedia>(FindObjectsSortMode.None));
            all.RemoveAll(m => Released.Contains(m));
            all.Sort((a, b) => Compare(HierarchyPath(a.transform), HierarchyPath(b.transform)));
            return Mathf.Max(0, all.IndexOf(media));
        }

        /// <summary>Surround media (360° video) gives its spot up.</summary>
        public static void Release(ImmersiveMedia media)
        {
            if (!Released.Contains(media))
                Released.Add(media);
        }

        static List<int> HierarchyPath(Transform transform)
        {
            var path = new List<int>();
            for (var t = transform; t != null; t = t.parent)
                path.Insert(0, t.GetSiblingIndex());
            return path;
        }

        static int Compare(List<int> a, List<int> b)
        {
            for (var i = 0; i < Mathf.Min(a.Count, b.Count); i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }

        /// <summary>Offset of spot <paramref name="slot"/> from the anchor; <paramref name="towardUser"/> is level and normalised.</summary>
        public static Vector3 Offset(int slot, Vector3 towardUser)
        {
            if (slot <= 0)
                return Vector3.zero;
            var ring = (slot - 1) / Angles.Length;
            var angle = Angles[(slot - 1) % Angles.Length];
            return Quaternion.AngleAxis(angle, Vector3.up) * towardUser * (Spacing * (ring + 1));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Released.Clear();
    }
}
