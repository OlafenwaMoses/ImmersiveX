using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The content menu's MR and VR icons, drawn once in code (white on clear, tinted by the image): VR is a headset whose
    /// lenses are solid, a world of its own; MR is a headset drawn in outline, see-through, with a small cube beside it, a
    /// virtual thing in the real view.
    /// </summary>
    public static class ModeIcons
    {
        const int Size = 128;

        static Sprite _mixed, _virtual;

        public static Sprite For(ViewMode mode) => mode == ViewMode.Virtual
            ? _virtual != null ? _virtual : _virtual = Draw(Virtual, "VR icon")
            : _mixed != null ? _mixed : _mixed = Draw(Mixed, "MR icon");

        // signed distances in icon units (-1..1, y up); inside is negative
        static float Box(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float Headset(Vector2 p) => Mathf.Max(Box(p, new Vector2(0.84f, 0.44f), 0.24f), -((p - new Vector2(0f, -0.5f)).magnitude - 0.24f));

        static float Lenses(Vector2 p) => Mathf.Min(Box(p - new Vector2(-0.39f, 0.03f), new Vector2(0.27f, 0.21f), 0.15f),
                                                   Box(p - new Vector2(0.39f, 0.03f), new Vector2(0.27f, 0.21f), 0.15f));

        static float Virtual(Vector2 p) => Mathf.Max(Headset(p), -Lenses(p));

        static float Mixed(Vector2 p)
        {
            var h = (p - new Vector2(-0.1f, -0.14f)) / 0.86f; // the headset a little smaller, down and left
            var outline = Mathf.Min(Mathf.Abs(Headset(h)) - 0.07f, Mathf.Abs(Lenses(h)) - 0.055f) * 0.86f;
            // the cube: a hexagon at the top right with three seams from its centre (the top face and two sides)
            var c = (p - new Vector2(0.62f, 0.6f)) / 0.33f;
            var hex = Mathf.Max(Mathf.Abs(c.x), 0.866f * Mathf.Abs(c.y) + 0.5f * Mathf.Abs(c.x)) - 0.866f;
            var seams = Mathf.Min(Segment(c, new Vector2(0.866f, 0.5f)), Mathf.Min(Segment(c, new Vector2(-0.866f, 0.5f)), Segment(c, new Vector2(0f, -1f))));
            var cube = Mathf.Max(hex, -(seams - 0.08f)) * 0.33f;
            return Mathf.Min(outline, cube);
        }

        /// <summary>Distance from p to the segment from the origin to b.</summary>
        static float Segment(Vector2 p, Vector2 b)
        {
            var h = Mathf.Clamp01(Vector2.Dot(p, b) / b.sqrMagnitude);
            return (p - b * h).magnitude;
        }

        static Sprite Draw(System.Func<Vector2, float> shape, string name)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            var pixel = 2f / Size;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                var d = shape(p);
                var a = Mathf.Clamp01(0.5f - d / (pixel * 1.2f)); // anti-aliased edge about a pixel wide
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _mixed = _virtual = null;
    }
}
