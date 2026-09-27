using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ImmersiveX.Media
{
    /// <summary>What detection decided a source is.</summary>
    public enum MediaKind
    {
        HologramStream,
        Splats,
        PointCloud,
        Model,
        Mesh,
        Sequence,
        Video,
        Codec,
    }

    /// <summary>The result of <see cref="MediaDetector.Detect"/>.</summary>
    public sealed class MediaDetection
    {
        public string Url;

        /// <summary>Lower-case file extension, e.g. ".spz".</summary>
        public string Extension;

        public MediaKind Kind;

        /// <summary>What the user asked for (Auto unless they overrode detection).</summary>
        public MediaFormat Requested;

        /// <summary>The file's bytes when detection had to read it (PLY and JSON), or null.</summary>
        public byte[] Bytes;

        /// <summary>For <see cref="MediaKind.Codec"/>: the registered codec that plays it.</summary>
        public IMediaCodec Codec;

        public string Error;
    }

    /// <summary>
    /// Plays a format ImmersiveX doesn't decode itself, usually through a vendor SDK (4DViews, Arcturus). Register one with
    /// <see cref="MediaCodecs.Register"/> from an adapter package; it's asked before the built-in formats.
    /// </summary>
    public interface IMediaCodec
    {
        string Name { get; }

        /// <summary>True if this codec plays <paramref name="url"/> (usually judged by its extension).</summary>
        bool CanPlay(string url, string extension);

        IMediaPlayable Create(MediaDetection detection);
    }

    /// <summary>Registered <see cref="IMediaCodec"/>s.</summary>
    public static class MediaCodecs
    {
        static readonly List<IMediaCodec> Registered = new List<IMediaCodec>();

        public static void Register(IMediaCodec codec)
        {
            if (codec != null && !Registered.Contains(codec))
                Registered.Add(codec);
        }

        public static IMediaCodec Find(string url, string extension) =>
            Registered.Find(codec => codec.CanPlay(url, extension));
    }

    /// <summary>
    /// Works out what a source is: by extension, reading the file where the extension isn't enough (a PLY can be splats,
    /// points or a mesh; a JSON can be a hologram stream or a sequence).
    /// </summary>
    public static class MediaDetector
    {
        /// <summary>Formats that need a vendor plug-in, and which one.</summary>
        static readonly Dictionary<string, string> VendorFormats = new Dictionary<string, string>
        {
            { ".4ds", "4DViews (.4ds) needs the 4DViews Unity plugin and an ImmersiveX codec adapter for it." },
            { ".oms", "Arcturus (.oms) needs the Arcturus HoloSuite Unity player and an ImmersiveX codec adapter for it." },
        };

        public static IEnumerator Detect(string url, MediaFormat requested, Action<MediaDetection> done)
        {
            var detection = new MediaDetection { Url = url, Extension = MediaSource.Extension(url), Requested = requested };

            var codec = MediaCodecs.Find(url, detection.Extension);
            if (codec != null)
            {
                detection.Kind = MediaKind.Codec;
                detection.Codec = codec;
                done(detection);
                yield break;
            }

            if (requested != MediaFormat.Auto)
            {
                detection.Kind = KindOf(requested);
                done(detection);
                yield break;
            }

            if (MediaSource.TryLocalFolder(url, out _))
            {
                detection.Kind = MediaKind.Sequence;
                done(detection);
                yield break;
            }

            switch (detection.Extension)
            {
                case ".json":
                    yield return MediaSource.Fetch(url, (bytes, error) =>
                    {
                        detection.Bytes = bytes;
                        detection.Error = error;
                    });
                    if (detection.Error == null)
                        ClassifyJson(detection);
                    break;

                case ".ply":
                    yield return MediaSource.Fetch(url, (bytes, error) =>
                    {
                        detection.Bytes = bytes;
                        detection.Error = error;
                    });
                    if (detection.Error == null)
                        ClassifyPly(detection);
                    break;

                case ".splat":
                case ".spz":
                case ".ksplat":
                    detection.Kind = MediaKind.Splats;
                    break;

                case ".glb":
                case ".gltf":
                    detection.Kind = MediaKind.Model;
                    break;

                case ".obj":
                    detection.Kind = MediaKind.Mesh;
                    break;

                case ".mp4":
                case ".m4v":
                case ".mov":
                case ".webm":
                    detection.Kind = MediaKind.Video;
                    break;

                case ".m3u8":
                case ".mpd":
                    detection.Error = "Live streaming (HLS/DASH) isn't supported by Unity's video player; use a progressive MP4 or WebM over HTTPS.";
                    break;

                default:
                    detection.Error = VendorFormats.TryGetValue(detection.Extension, out var vendor)
                        ? vendor
                        : $"'{MediaSource.FileName(url)}' isn't a format ImmersiveX Media plays.";
                    break;
            }

            done(detection);
        }

        static MediaKind KindOf(MediaFormat format)
        {
            switch (format)
            {
                case MediaFormat.HologramStream: return MediaKind.HologramStream;
                case MediaFormat.GaussianSplats: return MediaKind.Splats;
                case MediaFormat.PointCloud: return MediaKind.PointCloud;
                case MediaFormat.Model: return MediaKind.Model;
                case MediaFormat.Mesh: return MediaKind.Mesh;
                case MediaFormat.Sequence: return MediaKind.Sequence;
                default: return MediaKind.Video;
            }
        }

        /// <summary>A GenXR stream manifest has quality tiers; an ImmersiveX sequence says "type": "sequence".</summary>
        public static void ClassifyJson(MediaDetection detection)
        {
            try
            {
                var root = Json.Parse(Encoding.UTF8.GetString(detection.Bytes)) as Dictionary<string, object>;
                if (root != null && root.ContainsKey("tiers"))
                    detection.Kind = MediaKind.HologramStream;
                else if (root != null && root.TryGetValue("type", out var type) && "sequence".Equals(type as string, StringComparison.OrdinalIgnoreCase))
                    detection.Kind = MediaKind.Sequence;
                else
                    detection.Error = $"'{MediaSource.FileName(detection.Url)}' is neither a hologram stream (stream.json) nor a sequence.";
            }
            catch (FormatException exception)
            {
                detection.Error = $"'{MediaSource.FileName(detection.Url)}' isn't valid JSON ({exception.Message}).";
            }
        }

        public static void ClassifyPly(MediaDetection detection)
        {
            try
            {
                switch (PlyFile.Parse(detection.Bytes).Content)
                {
                    case PlyContent.GaussianSplats:
                    case PlyContent.CompressedSplats:
                        detection.Kind = MediaKind.Splats;
                        break;
                    case PlyContent.PointCloud:
                        detection.Kind = MediaKind.PointCloud;
                        break;
                    case PlyContent.Mesh:
                        detection.Kind = MediaKind.Mesh;
                        break;
                    default:
                        detection.Error = $"'{MediaSource.FileName(detection.Url)}' has no vertices.";
                        break;
                }
            }
            catch (FormatException exception)
            {
                detection.Error = $"'{MediaSource.FileName(detection.Url)}': {exception.Message}";
            }
        }
    }
}
