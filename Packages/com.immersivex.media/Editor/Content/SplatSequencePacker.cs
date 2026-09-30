using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Packs a 4D Gaussian-splat capture into a hologram stream that plays on a headset from inside the app. The capture is
    /// a folder of splat frames: 3DGS or compressed <c>.ply</c>, <c>.splat</c>, <c>.spz</c> or <c>.ksplat</c>.
    /// <list type="bullet">
    /// <item>Each Gaussian takes 17 bytes, and frames upload to the GPU as they are.</item>
    /// <item>Colour is the view-independent part; spherical harmonics beyond the DC term aren't kept.</item>
    /// <item>The stream gets one fit for the whole clip, so a character keeps its size when it raises an arm.</item>
    /// <item>A sound file in the folder becomes the soundtrack.</item>
    /// </list>
    /// </summary>
    public static class SplatSequencePacker
    {
        public sealed class Result
        {
            public int Frames;
            public int MaxGaussians;
            public long Bytes;
            public double MegabytesPerSecond;
            public HologramFit Fit;

            /// <summary>The stream's <c>stream.json</c>.</summary>
            public string Manifest;
        }

        /// <summary>Formats the frames can be in.</summary>
        public static readonly string[] FrameExtensions = { ".ply", ".splat", ".spz", ".ksplat" };

        static readonly string[] AudioExtensions = { ".m4a", ".mp3", ".ogg", ".wav", ".aac" };

        /// <summary>
        /// The splat frames in <paramref name="folder"/>, in natural order, if it holds at least two of one splat format
        /// (a PLY must be Gaussian splats or points, not a mesh); otherwise empty.
        /// </summary>
        public static string[] Frames(string folder)
        {
            if (!Directory.Exists(folder))
                return Array.Empty<string>();
            var frames = Directory.GetFiles(folder)
                .Where(f => FrameExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => NaturalKey(Path.GetFileName(f)), StringComparer.Ordinal)
                .ToArray();
            if (frames.Length < 2 || frames.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().Count() != 1)
                return Array.Empty<string>();
            if (Path.GetExtension(frames[0]).ToLowerInvariant() == ".ply" && PlyContentOf(frames[0]) == PlyContent.Mesh)
                return Array.Empty<string>();
            return frames;
        }

        /// <summary>
        /// Pack <paramref name="frames"/> into a stream in <paramref name="output"/> (replacing an earlier pack there).
        /// <paramref name="progress"/> gets 0–1 and returns false to cancel.
        /// </summary>
        public static Result Pack(IReadOnlyList<string> frames, string output, float fps, UpAxis up, int maxGaussians, string title,
            Func<float, bool> progress = null)
        {
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("There are no frames to pack.");
            if (fps <= 0f)
                throw new ArgumentException("The frame rate must be more than 0.");

            var tierFolder = Path.Combine(output, "tiers", "base");
            Directory.CreateDirectory(tierFolder);
            foreach (var old in Directory.GetFiles(tierFolder, "*.bin"))
                File.Delete(old); // an earlier pack's frames

            var range = ScaleRange(frames);
            var counts = new int[frames.Count];
            var sizes = new int[frames.Count];
            var fits = new HologramFit?[frames.Count];
            var done = 0;
            var cancelled = false;
            var work = Task.Run(() => Parallel.For(0, frames.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                (i, state) =>
                {
                    if (Volatile.Read(ref cancelled))
                    {
                        state.Stop();
                        return;
                    }

                    var cloud = Decode(frames[i], up);
                    if (maxGaussians > 0 && cloud.Count > maxGaussians)
                        cloud = cloud.Strongest(maxGaussians);
                    var data = SplatPacker.Pack(cloud, range, out var header, out _);
                    var bytes = new byte[data.Length * 4];
                    Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                    File.WriteAllBytes(Path.Combine(tierFolder, i.ToString("D6", CultureInfo.InvariantCulture) + ".bin"), bytes);
                    counts[i] = cloud.Count;
                    sizes[i] = bytes.Length;
                    if (cloud.Count > 0)
                        fits[i] = HologramFrame.Fit(cloud.Positions, cloud.Count, header.Min.y, header.Max.y, new int[256]);
                    Interlocked.Increment(ref done);
                }));
            while (!work.IsCompleted)
            {
                if (progress != null && !progress(Volatile.Read(ref done) / (float)frames.Count))
                    Volatile.Write(ref cancelled, true);
                Thread.Sleep(50);
            }

            if (work.IsFaulted)
                throw work.Exception.GetBaseException();
            if (cancelled)
                throw new OperationCanceledException("Packing was cancelled.");

            var fitted = fits.Where(f => f.HasValue).Select(f => f.Value).ToArray();
            if (fitted.Length == 0)
                throw new FormatException("Every frame is empty.");
            var fit = new HologramFit
            {
                CentreX = fitted.Average(f => f.CentreX),
                CentreZ = fitted.Average(f => f.CentreZ),
                Top = Median(fitted.Select(f => f.Top)), // the usual standing height, not the highest reach
                Bottom = Median(fitted.Select(f => f.Bottom)),
            };

            var audio = Directory.GetFiles(Path.GetDirectoryName(frames[0]) ?? ".")
                .FirstOrDefault(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            string audioFile = null;
            if (audio != null)
            {
                audioFile = "audio" + Path.GetExtension(audio).ToLowerInvariant();
                File.Copy(audio, Path.Combine(output, audioFile), overwrite: true);
            }

            var total = sizes.Sum(s => (long)s);
            var rate = total / (double)frames.Count * fps / 1e6;
            var manifest = Path.Combine(output, "stream.json");
            File.WriteAllText(manifest, Manifest(title, fps, range, fit, audioFile, counts, sizes, rate));
            return new Result { Frames = frames.Count, MaxGaussians = counts.Max(), Bytes = total, MegabytesPerSecond = rate, Fit = fit, Manifest = manifest };
        }

        static SplatCloud Decode(string file, UpAxis up)
        {
            var cloud = SplatDecoders.Decode(File.ReadAllBytes(file), Path.GetExtension(file).ToLowerInvariant());
            cloud.ToYDown(up);
            return cloud;
        }

        /// <summary>One log-scale range for every frame: the 0.01 % tails of a sample of frames.</summary>
        static Vector2 ScaleRange(IReadOnlyList<string> frames)
        {
            const int Bins = 3000;
            const float Low = -20f, High = 10f;
            var histogram = new long[Bins];
            long total = 0;
            for (var i = 0; i < frames.Count; i += Math.Max(1, frames.Count / 12))
            {
                var cloud = SplatDecoders.Decode(File.ReadAllBytes(frames[i]), Path.GetExtension(frames[i]).ToLowerInvariant());
                for (var k = 0; k < cloud.Count * 3; k++)
                {
                    var log = Mathf.Log(Mathf.Max(cloud.Scales[k], 1e-9f));
                    histogram[Mathf.Clamp((int)((log - Low) / (High - Low) * Bins), 0, Bins - 1)]++;
                    total++;
                }
            }

            if (total == 0)
                return new Vector2(-9f, 1f);
            float Quantile(double q)
            {
                long seen = 0;
                for (var b = 0; b < Bins; b++)
                    if ((seen += histogram[b]) >= q * total)
                        return Low + (b + 0.5f) * (High - Low) / Bins;
                return High;
            }

            var lo = Mathf.Floor(Quantile(1e-4) * 2f) / 2f;
            var hi = Mathf.Ceil(Quantile(1 - 1e-4) * 2f) / 2f;
            return new Vector2(lo, Mathf.Max(hi, lo + 0.5f));
        }

        static string Manifest(string title, float fps, Vector2 range, HologramFit fit, string audio, int[] counts, int[] sizes, double rate)
        {
            string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
            var json = new StringBuilder();
            json.Append("{\"version\":3,\"title\":").Append(MediaFolders.Quote(title ?? string.Empty))
                .Append(",\"fps\":").Append(F(fps))
                .Append(",\"frames\":").Append(counts.Length)
                .Append(",\"duration_seconds\":").Append(F(counts.Length / fps))
                .Append(",\"base_url\":null,\"texture_width\":").Append(SplatPacker.TextureWidth)
                .Append(",\"gaussian_bytes\":17,\"scale_range\":[").Append(F(range.x)).Append(',').Append(F(range.y)).Append(']')
                .Append(",\"audio\":").Append(audio != null ? "{\"file\":" + MediaFolders.Quote(audio) + "}" : "null")
                .Append(",\"fit\":{\"centre_x\":").Append(F(fit.CentreX)).Append(",\"centre_z\":").Append(F(fit.CentreZ))
                .Append(",\"top\":").Append(F(fit.Top)).Append(",\"bottom\":").Append(F(fit.Bottom)).Append('}')
                .Append(",\"tiers\":[{\"name\":\"base\",\"path\":\"tiers/base\",\"rate_mb_s\":").Append(F(Math.Round(rate, 2))).Append(",\"frames\":[");
            for (var i = 0; i < counts.Length; i++)
                json.Append(i > 0 ? "," : string.Empty).Append('[').Append(counts[i]).Append(',').Append(sizes[i]).Append(']');
            return json.Append("]}]}").ToString();
        }

        static float Median(IEnumerable<float> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : 0.5f * (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]);
        }

        static PlyContent PlyContentOf(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                var header = new byte[(int)Math.Min(stream.Length, 64 * 1024)];
                stream.Read(header, 0, header.Length);
                try
                {
                    return PlyFile.Parse(header).Content;
                }
                catch (FormatException)
                {
                    return PlyContent.Unknown;
                }
            }
        }

        /// <summary>Natural order: frame_2 before frame_10.</summary>
        static string NaturalKey(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\d+", m => m.Value.PadLeft(12, '0'));
    }
}
