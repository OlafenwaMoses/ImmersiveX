using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A streamed hologram's <c>stream.json</c>: frame rate, frame count, audio, quantisation ranges and the quality
    /// tiers. Every tier holds the same frames; lower tiers merge neighbouring Gaussians so each frame is smaller.
    /// Frames live at <c>&lt;base&gt;/&lt;tier path&gt;/NNNNNN.bin</c>.
    /// </summary>
    public sealed class HologramManifest
    {
        public float Fps { get; private set; }
        public int FrameCount { get; private set; }
        public double Duration => FrameCount / (double)Fps;
        public int TextureWidth { get; private set; }
        public Vector2 ScaleRange { get; private set; }
        public string BaseUrl { get; private set; }
        public string AudioUrl { get; private set; }
        public IReadOnlyList<HologramTier> Tiers => _tiers;

        readonly List<HologramTier> _tiers = new List<HologramTier>();

        /// <summary>Parse <paramref name="json"/>, resolving relative paths against <paramref name="manifestUrl"/>.</summary>
        public static HologramManifest Parse(string json, string manifestUrl)
        {
            if (!(Json.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("stream.json isn't a JSON object.");
            if (!root.ContainsKey("tiers"))
                throw new FormatException("stream.json has no quality tiers (it was made for an older player).");

            var manifest = new HologramManifest
            {
                Fps = (float)Number(root, "fps"),
                FrameCount = (int)Number(root, "frames"),
                TextureWidth = (int)Number(root, "texture_width"),
            };
            var scale = (List<object>)root["scale_range"];
            manifest.ScaleRange = new Vector2((float)(double)scale[0], (float)(double)scale[1]);

            var manifestUri = new Uri(manifestUrl);
            var baseUrl = root.TryGetValue("base_url", out var value) ? value as string : null;
            var baseUri = new Uri(manifestUri, string.IsNullOrEmpty(baseUrl) ? "./" : baseUrl.TrimEnd('/') + "/");
            manifest.BaseUrl = baseUri.AbsoluteUri;

            if (root.TryGetValue("audio", out var audio) && audio is Dictionary<string, object> audioInfo &&
                audioInfo.TryGetValue("file", out var file) && file is string audioFile && audioFile.Length > 0)
                manifest.AudioUrl = new Uri(baseUri, audioFile).AbsoluteUri;

            foreach (var entry in (List<object>)root["tiers"])
                manifest._tiers.Add(HologramTier.Parse((Dictionary<string, object>)entry, manifest.FrameCount));
            manifest._tiers.Sort((a, b) => a.MeanBytes.CompareTo(b.MeanBytes)); // smallest first
            if (manifest.Fps <= 0f || manifest.FrameCount <= 0 || manifest.TextureWidth <= 0 || manifest._tiers.Count == 0)
                throw new FormatException("stream.json is missing its frame rate, frames or tiers.");
            return manifest;
        }

        /// <summary>The tier called <paramref name="name"/>, or the smallest tier when there's none by that name.</summary>
        public HologramTier FindTier(string name)
        {
            foreach (var tier in _tiers)
                if (string.Equals(tier.Name, name, StringComparison.OrdinalIgnoreCase))
                    return tier;
            return _tiers[0];
        }

        public string FrameUrl(HologramTier tier, int frame) =>
            BaseUrl + tier.Path.Trim('/') + "/" + frame.ToString("D6", CultureInfo.InvariantCulture) + ".bin";

        internal static double Number(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is double number ? number : 0d;
    }

    /// <summary>One quality tier: its folder and, per frame, the Gaussian count and file size.</summary>
    public sealed class HologramTier
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public int[] Counts { get; private set; }
        public int[] Bytes { get; private set; }
        public int MaxCount { get; private set; }
        public int MaxBytes { get; private set; }
        public double MeanBytes { get; private set; }

        /// <summary>Megabytes per second needed to stream this tier in real time.</summary>
        public double RateMegabytesPerSecond { get; private set; }

        internal static HologramTier Parse(Dictionary<string, object> json, int frameCount)
        {
            var frames = (List<object>)json["frames"];
            if (frames.Count != frameCount)
                throw new FormatException($"Tier '{json["name"]}' lists {frames.Count} frames; the stream has {frameCount}.");

            var tier = new HologramTier
            {
                Name = (string)json["name"],
                Path = (string)json["path"],
                Counts = new int[frameCount],
                Bytes = new int[frameCount],
                RateMegabytesPerSecond = HologramManifest.Number(json, "rate_mb_s"),
            };
            long total = 0;
            for (var i = 0; i < frameCount; i++)
            {
                var frame = (List<object>)frames[i];
                tier.Counts[i] = (int)(double)frame[0];
                tier.Bytes[i] = (int)(double)frame[1];
                tier.MaxCount = Mathf.Max(tier.MaxCount, tier.Counts[i]);
                tier.MaxBytes = Mathf.Max(tier.MaxBytes, tier.Bytes[i]);
                total += tier.Bytes[i];
            }

            tier.MeanBytes = total / (double)frameCount;
            return tier;
        }
    }
}
