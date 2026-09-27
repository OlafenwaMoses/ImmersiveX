using System;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>The 32-byte header at the start of every frame file.</summary>
    public struct HologramFrameHeader
    {
        /// <summary>Number of Gaussians in the frame.</summary>
        public int Count;

        /// <summary>Bounding box the quantised positions span, in the source camera's frame (x right, y down, z forward).</summary>
        public Vector3 Min;
        public Vector3 Max;

        /// <summary>Texel rows; every row holds <see cref="HologramManifest.TextureWidth"/> Gaussians.</summary>
        public int Rows;
    }

    /// <summary>Where the performer stands in one frame, in the source frame: used to fit it to a fixed height.</summary>
    public struct HologramFit
    {
        public float CentreX;
        public float CentreZ;

        /// <summary>Lowest point (largest y, since y points down in the source frame).</summary>
        public float Bottom;

        public float Top;

        public float Height => Bottom - Top;

        /// <summary>
        /// True when <paramref name="next"/> is too different to glide to: a camera cut in the source video.
        /// </summary>
        public static bool IsCut(HologramFit previous, HologramFit next)
        {
            var ratio = next.Height / Mathf.Max(previous.Height, 1e-3f);
            var shift = new Vector2(next.CentreX - previous.CentreX, next.CentreZ - previous.CentreZ).magnitude;
            return ratio < 0.7f || ratio > 1.43f || shift > 0.35f * Mathf.Max(previous.Height, next.Height);
        }

        public static HologramFit Lerp(HologramFit from, HologramFit to, float t) => new HologramFit
        {
            CentreX = Mathf.Lerp(from.CentreX, to.CentreX, t),
            CentreZ = Mathf.Lerp(from.CentreZ, to.CentreZ, t),
            Bottom = Mathf.Lerp(from.Bottom, to.Bottom, t),
            Top = Mathf.Lerp(from.Top, to.Top, t),
        };
    }

    /// <summary>
    /// Reads GenXR hologram frame files: a 32-byte header, then one 16-byte texel per Gaussian padded to whole rows,
    /// then one opacity byte per texel. Frames are held as <c>uint[]</c> so they upload to the GPU unchanged.
    /// </summary>
    public static class HologramFrame
    {
        /// <summary>Header size in uints.</summary>
        public const int HeaderUints = 8;

        /// <summary>Bytes per Gaussian in the file: a 16-byte texel plus an opacity byte.</summary>
        public const int BytesPerGaussian = 17;

        public static HologramFrameHeader ReadHeader(uint[] data) => new HologramFrameHeader
        {
            Count = (int)data[0],
            Min = new Vector3(Float(data[1]), Float(data[2]), Float(data[3])),
            Max = new Vector3(Float(data[4]), Float(data[5]), Float(data[6])),
            Rows = (int)data[7],
        };

        /// <summary>File size for <paramref name="rows"/> rows of <paramref name="textureWidth"/> texels.</summary>
        public static int ExpectedBytes(int rows, int textureWidth) => 32 + rows * textureWidth * BytesPerGaussian;

        /// <summary>Index of the first opacity byte, in uints.</summary>
        public static int AlphaStart(HologramFrameHeader header, int textureWidth) => HeaderUints + header.Rows * textureWidth * 4;

        /// <summary>True when the header agrees with the size the manifest gave for the file.</summary>
        public static bool IsValid(HologramFrameHeader header, int textureWidth, int bytes) =>
            header.Rows >= 0 && header.Count >= 0 && header.Count <= header.Rows * textureWidth &&
            ExpectedBytes(header.Rows, textureWidth) == bytes;

        /// <summary>Decode each Gaussian's position into <paramref name="xyz"/> (3 floats each).</summary>
        public static void DecodePositions(uint[] data, HologramFrameHeader header, float[] xyz)
        {
            var min = header.Min;
            var step = (header.Max - header.Min) / 65535f;
            for (int i = 0, t = HeaderUints, j = 0; i < header.Count; i++, t += 4, j += 3)
            {
                uint a = data[t], b = data[t + 1];
                xyz[j] = min.x + (a & 0xffff) * step.x;
                xyz[j + 1] = min.y + (a >> 16) * step.y;
                xyz[j + 2] = min.z + (b & 0xffff) * step.z;
            }
        }

        /// <summary>
        /// Where the performer stands: the centroid across the floor, and the 0.5th–99.5th percentile of height so a few
        /// stray Gaussians don't change the fit. <paramref name="histogram"/> is scratch space of any length (256 is plenty).
        /// </summary>
        public static HologramFit Fit(float[] xyz, int count, float minY, float maxY, int[] histogram)
        {
            if (count <= 0)
                return new HologramFit { Top = minY, Bottom = maxY };

            Array.Clear(histogram, 0, histogram.Length);
            var bins = histogram.Length;
            var span = Mathf.Max(maxY - minY, 1e-6f);
            double sumX = 0, sumZ = 0;
            for (int i = 0, j = 0; i < count; i++, j += 3)
            {
                sumX += xyz[j];
                sumZ += xyz[j + 2];
                var bin = (int)((xyz[j + 1] - minY) / span * bins);
                histogram[Mathf.Clamp(bin, 0, bins - 1)]++;
            }

            var tail = Mathf.Max(1, count / 200); // 0.5 %
            return new HologramFit
            {
                CentreX = (float)(sumX / count),
                CentreZ = (float)(sumZ / count),
                Top = minY + Quantile(histogram, tail, fromStart: true) * span / bins,
                Bottom = minY + Quantile(histogram, tail, fromStart: false) * span / bins,
            };
        }

        static float Quantile(int[] histogram, int tail, bool fromStart)
        {
            var seen = 0;
            for (var k = 0; k < histogram.Length; k++)
            {
                var bin = fromStart ? k : histogram.Length - 1 - k;
                seen += histogram[bin];
                if (seen >= tail)
                    return fromStart ? bin : bin + 1;
            }

            return fromStart ? 0 : histogram.Length;
        }

        /// <summary>
        /// Order Gaussians farthest from <paramref name="viewer"/> first (a 16-bit counting sort, linear time), so they
        /// blend correctly. <paramref name="keys"/> holds <paramref name="count"/> ints; <paramref name="buckets"/> holds 65 536.
        /// </summary>
        public static void SortBackToFront(float[] xyz, int count, Vector3 viewer, int[] keys, int[] buckets, uint[] order)
        {
            if (count <= 0)
                return;

            float near = float.MaxValue, far = float.MinValue;
            for (int i = 0, j = 0; i < count; i++, j += 3)
            {
                float dx = xyz[j] - viewer.x, dy = xyz[j + 1] - viewer.y, dz = xyz[j + 2] - viewer.z;
                var distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                keys[i] = BitConverter.SingleToInt32Bits(distance);
                if (distance < near)
                    near = distance;
                if (distance > far)
                    far = distance;
            }

            var scale = 65535f / Mathf.Max(far - near, 1e-6f);
            Array.Clear(buckets, 0, 65536);
            for (var i = 0; i < count; i++)
            {
                var bucket = (int)((far - BitConverter.Int32BitsToSingle(keys[i])) * scale); // farthest → bucket 0
                keys[i] = bucket;
                buckets[bucket]++;
            }

            for (int b = 0, sum = 0; b < 65536; b++)
            {
                var n = buckets[b];
                buckets[b] = sum;
                sum += n;
            }

            for (var i = 0; i < count; i++)
                order[buckets[keys[i]]++] = (uint)i;
        }

        static float Float(uint bits) => BitConverter.Int32BitsToSingle((int)bits);
    }
}
