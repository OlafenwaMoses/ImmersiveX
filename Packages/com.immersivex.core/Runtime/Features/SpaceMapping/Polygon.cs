using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Small 2D polygon helpers (x, y in the plane's own space).</summary>
    public static class Polygon
    {
        /// <summary>Area of a simple polygon (shoelace formula), in square metres.</summary>
        public static float Area(Vector2[] points)
        {
            var sum = 0f;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                sum += points[j].x * points[i].y - points[i].x * points[j].y;
            return Mathf.Abs(sum) * 0.5f;
        }

        public static Rect Bounds(Vector2[] points)
        {
            var min = points[0];
            var max = points[0];
            foreach (var p in points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Even-odd point-in-polygon test.</summary>
        public static bool Contains(Vector2[] points, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var a = points[i];
                var b = points[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>Shortest distance from <paramref name="p"/> to the polygon's edges.</summary>
        public static float DistanceToEdges(Vector2[] points, Vector2 p)
        {
            var best = float.MaxValue;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var a = points[j];
                var ab = points[i] - a;
                var t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }

            return Mathf.Sqrt(best);
        }
    }
}
