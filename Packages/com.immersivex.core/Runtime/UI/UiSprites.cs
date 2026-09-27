using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Sprites for world-space UI, drawn once in code (anti-aliased signed-distance shapes), so ImmersiveX ships no
    /// image assets: rounded panels, circles, and media icons.
    /// </summary>
    public static class UiSprites
    {
        const int PanelSize = 128;
        const int PanelRadius = 40;
        const int IconSize = 128;

        static Sprite _panel, _panelRim, _circle, _play, _pause, _speaker, _muted;

        /// <summary>A rounded rectangle for 9-slicing; the corner radius is 40 px.</summary>
        public static Sprite Panel => _panel != null ? _panel : _panel = Sliced("Panel", (x, y) => Fill(RoundedBox(x, y)));

        /// <summary>The outline of <see cref="Panel"/>: a 3 px rim.</summary>
        public static Sprite PanelRim => _panelRim != null ? _panelRim : _panelRim = Sliced("Panel Rim", (x, y) => Fill(Mathf.Abs(RoundedBox(x, y) + 1.5f) - 1.5f));

        public static Sprite Circle => _circle != null ? _circle : _circle = Icon("Circle", (x, y) => Fill(Length(x - 64f, y - 64f) - 62f));

        public static Sprite Play => _play != null ? _play : _play = Icon("Play", (x, y) =>
            Fill(RoundCorners(Triangle(x, y, 44f, 32f, 44f, 96f, 100f, 64f), 4f)));

        public static Sprite Pause => _pause != null ? _pause : _pause = Icon("Pause", (x, y) =>
            Fill(Mathf.Min(Box(x - 46f, y - 64f, 9f, 30f), Box(x - 82f, y - 64f, 9f, 30f)) - 4f));

        public static Sprite Speaker => _speaker != null ? _speaker : _speaker = Icon("Speaker", (x, y) =>
            Fill(Mathf.Min(SpeakerBody(x, y), Mathf.Min(Arc(x, y, 18f), Arc(x, y, 34f)))));

        public static Sprite Muted => _muted != null ? _muted : _muted = Icon("Muted", (x, y) =>
            Fill(Mathf.Min(SpeakerBody(x, y), Cross(x - 94f, y - 64f))));

        // ---------------------------------------------------------------- shapes (distances in pixels, < 0 inside)

        static float RoundedBox(float x, float y)
        {
            const float half = PanelSize * 0.5f - 1f;
            return Box(x - PanelSize * 0.5f, y - PanelSize * 0.5f, half - PanelRadius, half - PanelRadius) - PanelRadius;
        }

        static float Box(float x, float y, float halfWidth, float halfHeight)
        {
            float dx = Mathf.Abs(x) - halfWidth, dy = Mathf.Abs(y) - halfHeight;
            return Length(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)) + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        static float SpeakerBody(float x, float y)
        {
            var box = Box(x - 34f, y - 64f, 10f, 14f) - 3f;
            var cone = RoundCorners(Quad(x, y, 40f, 50f, 40f, 78f, 64f, 100f, 64f, 28f), 3f);
            return Mathf.Min(box, cone);
        }

        /// <summary>A sound wave: part of a ring around the speaker, 7 px thick.</summary>
        static float Arc(float x, float y, float radius)
        {
            float dx = x - 64f, dy = y - 64f;
            var ring = Mathf.Abs(Length(dx, dy) - radius) - 3.5f;
            var angle = Mathf.Abs(Mathf.Atan2(dy, dx)) * Mathf.Rad2Deg;
            return angle < 50f ? ring : Mathf.Max(ring, (angle - 50f) * radius * Mathf.Deg2Rad);
        }

        static float Cross(float x, float y)
        {
            var a = Segment(x, y, -14f, -14f, 14f, 14f);
            var b = Segment(x, y, -14f, 14f, 14f, -14f);
            return Mathf.Min(a, b) - 4f;
        }

        static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float px = x - ax, py = y - ay, ex = bx - ax, ey = by - ay;
            var t = Mathf.Clamp01((px * ex + py * ey) / (ex * ex + ey * ey));
            return Length(px - ex * t, py - ey * t);
        }

        static float Triangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy) =>
            Mathf.Max(Edge(x, y, ax, ay, bx, by), Mathf.Max(Edge(x, y, bx, by, cx, cy), Edge(x, y, cx, cy, ax, ay)));

        static float Quad(float x, float y, float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy) =>
            Mathf.Max(Mathf.Max(Edge(x, y, ax, ay, bx, by), Edge(x, y, bx, by, cx, cy)),
                Mathf.Max(Edge(x, y, cx, cy, dx, dy), Edge(x, y, dx, dy, ax, ay)));

        /// <summary>Signed distance to the line through a→b; negative on the inside for the corner order used above.</summary>
        static float Edge(float x, float y, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax, ey = by - ay;
            var length = Length(ex, ey);
            return ((x - ax) * ey - (y - ay) * ex) / length * -1f;
        }

        static float RoundCorners(float distance, float radius) => distance + radius;

        static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);

        static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

        // ---------------------------------------------------------------- textures

        static Sprite Sliced(string name, System.Func<float, float, float> alpha)
        {
            var texture = Draw(name, PanelSize, alpha);
            const float border = PanelRadius + 4f;
            return Sprite.Create(texture, new Rect(0, 0, PanelSize, PanelSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static Sprite Icon(string name, System.Func<float, float, float> alpha)
        {
            var texture = Draw(name, IconSize, alpha);
            return Sprite.Create(texture, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D Draw(string name, int size, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "ImmersiveX " + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // sample at the pixel centre; y grows upwards in texture space, flip so shapes are authored top-down
                var a = alpha(x + 0.5f, size - (y + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }
    }
}
