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
    /// <item>The stream gets one fit for the whole clip (<see cref="ClipFit"/>), so a character keeps its size when it raises an
    /// arm and a scene stands on its ground.</item>
    /// <item>What flies far out of the room (ten times the clip's height away) is left out.</item>
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

            /// <summary>Splats left out for being far outside the room, and how many frames had any.</summary>
            public int LeftOut;
            public int FramesLeftOutOf;
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

            // First where the capture stands in each frame, for the clip's one fit; then the frames themselves.
            var fits = new HologramFit?[frames.Count];
            ForEachFrame(frames.Count, 0f, progress, i =>
            {
                var cloud = Decode(frames[i], up).Strongest(maxGaussians);
                if (cloud.Count > 0)
                    fits[i] = FitOf(cloud);
            });
            var fit = ClipFit(fits);

            var centre = new Vector3(fit.CentreX, 0.5f * (fit.Top + fit.Bottom), fit.CentreZ);
            var reach = fit.Height > 0.05f ? FarAway * fit.Height : float.PositiveInfinity; // a flat clip has no height to judge by
            var counts = new int[frames.Count];
            var sizes = new int[frames.Count];
            var leftOut = new int[frames.Count];
            ForEachFrame(frames.Count, 0.5f, progress, i =>
            {
                var cloud = Decode(frames[i], up).Within(centre, reach, out leftOut[i]).Strongest(maxGaussians);
                var data = SplatPacker.Pack(cloud, range, out _, out _);
                var bytes = new byte[data.Length * 4];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(Path.Combine(tierFolder, i.ToString("D6", CultureInfo.InvariantCulture) + ".bin"), bytes);
                counts[i] = cloud.Count;
                sizes[i] = bytes.Length;
            });

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
            return new Result
            {
                Frames = frames.Count, MaxGaussians = counts.Max(), Bytes = total, MegabytesPerSecond = rate, Fit = fit, Manifest = manifest,
                LeftOut = leftOut.Sum(), FramesLeftOutOf = leftOut.Count(n => n > 0),
            };
        }

        /// <summary>
        /// The clip's one fit, from each frame's (null for an empty frame): the usual centre and height (medians), standing on
        /// the ground the clip comes down to (the 90th percentile of the frames' lowest points, y being down). A character
        /// keeps its size when it raises an arm or jumps, a scene whose parts lift off for a while still stands on its ground,
        /// and frames where something flies far away don't move it.
        /// </summary>
        public static HologramFit ClipFit(IReadOnlyList<HologramFit?> fits)
        {
            var fitted = fits.Where(f => f.HasValue).Select(f => f.Value).ToArray();
            if (fitted.Length == 0)
                throw new FormatException("Every frame is empty.");
            return new HologramFit
            {
                CentreX = Quantile(fitted.Select(f => f.CentreX), 0.5f),
                CentreZ = Quantile(fitted.Select(f => f.CentreZ), 0.5f),
                Top = Quantile(fitted.Select(f => f.Top), 0.5f), // the usual height, not the highest reach
                Bottom = Quantile(fitted.Select(f => f.Bottom), 0.9f),
            };
        }

        /// <summary>
        /// Splats farther than this many times the clip's height from its centre are left out: they'd be far outside the room
        /// at any size (something that flies kilometres away). Left in, they would spread each frame's 16-bit positions so thin
        /// that everything else shimmers.
        /// </summary>
        const float FarAway = 10f;

        static HologramFit FitOf(SplatCloud cloud)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < cloud.Count; i++)
            {
                minY = Mathf.Min(minY, cloud.Positions[i * 3 + 1]);
                maxY = Mathf.Max(maxY, cloud.Positions[i * 3 + 1]);
            }

            return HologramFrame.Fit(cloud.Positions, cloud.Count, minY, maxY, new int[256]);
        }

        /// <summary>
        /// Run <paramref name="work"/> for every frame in parallel, as half the packing: progress goes from
        /// <paramref name="start"/> to <paramref name="start"/> + 0.5.
        /// </summary>
        static void ForEachFrame(int count, float start, Func<float, bool> progress, Action<int> work)
        {
            var done = 0;
            var cancelled = false;
            var task = Task.Run(() => Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                (i, state) =>
                {
                    if (Volatile.Read(ref cancelled))
                    {
                        state.Stop();
                        return;
                    }

                    work(i);
                    Interlocked.Increment(ref done);
                }));
            while (!task.IsCompleted)
            {
                if (progress != null && !progress(start + 0.5f * Volatile.Read(ref done) / count))
                    Volatile.Write(ref cancelled, true);
                Thread.Sleep(50);
            }

            if (task.IsFaulted)
                throw task.Exception.GetBaseException();
            if (cancelled)
                throw new OperationCanceledException("Packing was cancelled.");
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

        /// <summary>The <paramref name="q"/> quantile, between the two nearest values (0.5 is the median).</summary>
        static float Quantile(IEnumerable<float> values, float q)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            var at = q * (sorted.Length - 1);
            var below = (int)Math.Floor(at);
            return below + 1 < sorted.Length ? Mathf.Lerp(sorted[below], sorted[below + 1], at - below) : sorted[below];
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
