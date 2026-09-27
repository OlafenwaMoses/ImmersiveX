using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace ImmersiveX.Media
{
    public static partial class SplatDecoders
    {
        const float Sqrt2 = 1.41421356f;
        const float Sqrt1_2 = 0.70710678f;

        // ---------------------------------------------------------------- PlayCanvas / SuperSplat compressed PLY

        /// <summary>
        /// PlayCanvas / SuperSplat compressed PLY: a <c>chunk</c> element (per 256 splats: position and log-scale bounds,
        /// optionally colour bounds) and a <c>vertex</c> element of four packed uint32s — position and scale 11-10-11 bits,
        /// colour RGBA8, rotation "smallest three" (2-bit index of the largest of w, x, y, z; three 10-bit values).
        /// Same frame as the source PLY (y down).
        /// </summary>
        static SplatCloud CompressedPly(byte[] data, PlyFile ply)
        {
            var chunk = ply.Find("chunk");
            var vertex = ply.Find("vertex");
            var chunkFloats = chunk.Properties.Count;
            if (chunkFloats != 12 && chunkFloats != 18)
                throw new FormatException($"Compressed PLY chunks have 12 or 18 floats; this one has {chunkFloats}.");
            if (ply.Format != PlyFile.Encoding.BinaryLittleEndian)
                throw new FormatException("Compressed PLY files are binary.");

            var chunkStart = ply.StartOf(data, chunk);
            var vertexStart = ply.StartOf(data, vertex);
            int position = vertex.Find("packed_position").Offset, rotation = vertex.Find("packed_rotation").Offset;
            int scale = vertex.Find("packed_scale").Offset, colour = vertex.Find("packed_color").Offset;

            var cloud = new SplatCloud(vertex.Count) { Up = UpAxis.NegativeY };
            for (var i = 0; i < vertex.Count; i++)
            {
                var c = chunkStart + (i / 256) * chunk.Stride;
                float Chunk(int k) => BitConverter.ToSingle(data, c + k * 4);
                var at = vertexStart + i * vertex.Stride;

                var p = BitConverter.ToUInt32(data, at + position);
                cloud.Positions[i * 3] = Mathf.Lerp(Chunk(0), Chunk(3), Unpack(p >> 21, 11));
                cloud.Positions[i * 3 + 1] = Mathf.Lerp(Chunk(1), Chunk(4), Unpack((p >> 11) & 0x3ff, 10));
                cloud.Positions[i * 3 + 2] = Mathf.Lerp(Chunk(2), Chunk(5), Unpack(p & 0x7ff, 11));

                var s = BitConverter.ToUInt32(data, at + scale);
                cloud.Scales[i * 3] = Mathf.Exp(Mathf.Lerp(Chunk(6), Chunk(9), Unpack(s >> 21, 11)));
                cloud.Scales[i * 3 + 1] = Mathf.Exp(Mathf.Lerp(Chunk(7), Chunk(10), Unpack((s >> 11) & 0x3ff, 10)));
                cloud.Scales[i * 3 + 2] = Mathf.Exp(Mathf.Lerp(Chunk(8), Chunk(11), Unpack(s & 0x7ff, 11)));

                var rgba = BitConverter.ToUInt32(data, at + colour);
                for (var k = 0; k < 3; k++)
                {
                    var t = ((rgba >> (24 - 8 * k)) & 0xff) / 255f;
                    cloud.Colors[i * 4 + k] = Mathf.Clamp01(chunkFloats == 18 ? Mathf.Lerp(Chunk(12 + k), Chunk(15 + k), t) : t);
                }

                cloud.Colors[i * 4 + 3] = (rgba & 0xff) / 255f;

                // Smallest three: the largest component (index in w, x, y, z order) is rebuilt from the other three.
                var r = BitConverter.ToUInt32(data, at + rotation);
                var largest = (int)(r >> 30);
                float a = ((r >> 20) & 0x3ff) / 1023f - 0.5f, b = ((r >> 10) & 0x3ff) / 1023f - 0.5f, d = (r & 0x3ff) / 1023f - 0.5f;
                a *= Sqrt2;
                b *= Sqrt2;
                d *= Sqrt2;
                var m = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - d * d));
                switch (largest)
                {
                    case 0: cloud.SetRotation(i, m, a, b, d); break;
                    case 1: cloud.SetRotation(i, a, m, b, d); break;
                    case 2: cloud.SetRotation(i, a, b, m, d); break;
                    default: cloud.SetRotation(i, a, b, d, m); break;
                }
            }

            return cloud;
        }

        static float Unpack(uint value, int bits) => value / (float)((1 << bits) - 1);

        // ---------------------------------------------------------------- Niantic SPZ

        /// <summary>
        /// Niantic .spz. Versions 1–3 are one gzip stream (16-byte header, then positions, alphas, colours, scales,
        /// rotations, SH); version 4 has a plain 32-byte header, a table of contents and one ZSTD frame per block. Positions
        /// are 24-bit fixed point (float16 in v1), colours are SH DC × 0.15 + 0.5, scales are log × 16 + 10 × 16, rotations
        /// are xyz (v1–2) or "smallest three" in x, y, z, w order (v3+). The spec says SPZ is right-up-back (y up); some
        /// tools write it y-down instead, which the Up setting fixes.
        /// </summary>
        static SplatCloud Spz(byte[] data)
        {
            const uint Magic = 0x5053474e; // "NGSP"
            if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Magic)
                return SpzVersion4(data);
            if (data.Length < 2 || data[0] != 0x1f || data[1] != 0x8b)
                throw new FormatException("Not an SPZ file.");

            byte[] raw;
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                raw = output.ToArray();
            }

            if (raw.Length < 16 || BitConverter.ToUInt32(raw, 0) != Magic)
                throw new FormatException("The SPZ header is missing.");
            var version = (int)BitConverter.ToUInt32(raw, 4);
            var count = (int)BitConverter.ToUInt32(raw, 8);
            var fractionalBits = raw[13];
            if (version < 1 || version > 3)
                throw new FormatException($"SPZ version {version} isn't supported.");

            var positionBytes = version == 1 ? 6 : 9;
            var rotationBytes = version >= 3 ? 4 : 3;
            var at = 16;
            var positions = Slice(raw, ref at, count * positionBytes);
            var alphas = Slice(raw, ref at, count);
            var colours = Slice(raw, ref at, count * 3);
            var scales = Slice(raw, ref at, count * 3);
            var rotations = Slice(raw, ref at, count * rotationBytes);
            return SpzCloud(count, version, fractionalBits, positions, alphas, colours, scales, rotations);
        }

        static SplatCloud SpzVersion4(byte[] data)
        {
            var count = (int)BitConverter.ToUInt32(data, 8);
            var fractionalBits = data[13];
            var streams = data[15];
            var toc = (int)BitConverter.ToUInt32(data, 16);
            if (streams < 5)
                throw new FormatException($"SPZ v4 has {streams} streams; at least 5 are needed.");

            var blocks = new byte[5][];
            var at = toc + streams * 16;
            for (var s = 0; s < streams; s++)
            {
                var compressed = (int)BitConverter.ToUInt64(data, toc + s * 16);
                var uncompressed = (int)BitConverter.ToUInt64(data, toc + s * 16 + 8);
                if (s < 5) // positions, alphas, colours, scales, rotations; SH isn't used
                    blocks[s] = Zstd(data, at, compressed, uncompressed);
                at += compressed;
            }

            return SpzCloud(count, 4, fractionalBits, blocks[0], blocks[1], blocks[2], blocks[3], blocks[4]);
        }

        static byte[] Zstd(byte[] data, int offset, int length, int uncompressed)
        {
            var output = new byte[uncompressed];
            using (var input = new MemoryStream(data, offset, length))
            using (var zstd = new ZstdSharp.DecompressionStream(input))
            {
                var read = 0;
                while (read < uncompressed)
                {
                    var n = zstd.Read(output, read, uncompressed - read);
                    if (n <= 0)
                        break;
                    read += n;
                }

                if (read != uncompressed)
                    throw new FormatException($"An SPZ stream decompressed to {read} bytes; its table says {uncompressed}.");
            }

            return output;
        }

        static SplatCloud SpzCloud(int count, int version, int fractionalBits, byte[] positions, byte[] alphas, byte[] colours, byte[] scales, byte[] rotations)
        {
            const float ColourScale = 0.15f;
            var cloud = new SplatCloud(count) { Up = UpAxis.PositiveY }; // right, up, back
            var unit = 1f / (1 << fractionalBits);
            for (var i = 0; i < count; i++)
            {
                for (var k = 0; k < 3; k++)
                {
                    if (version == 1)
                    {
                        cloud.Positions[i * 3 + k] = HalfToFloat(BitConverter.ToUInt16(positions, (i * 3 + k) * 2));
                    }
                    else
                    {
                        var o = (i * 3 + k) * 3;
                        var fixedPoint = positions[o] | (positions[o + 1] << 8) | (positions[o + 2] << 16);
                        if ((fixedPoint & 0x800000) != 0)
                            fixedPoint |= unchecked((int)0xff000000); // sign-extend 24 bits
                        cloud.Positions[i * 3 + k] = fixedPoint * unit;
                    }

                    var dc = (colours[i * 3 + k] / 255f - 0.5f) / ColourScale;
                    cloud.Colors[i * 4 + k] = Mathf.Clamp01(0.5f + SplatCloud.ShC0 * dc);
                    cloud.Scales[i * 3 + k] = Mathf.Exp(scales[i * 3 + k] / 16f - 10f);
                }

                cloud.Colors[i * 4 + 3] = alphas[i] / 255f;

                if (version >= 3)
                {
                    // Smallest three in x, y, z, w order: 2-bit index of the largest, then three sign + 9-bit magnitudes.
                    var r = BitConverter.ToUInt32(rotations, i * 4);
                    var largest = (int)(r >> 30);
                    Span<float> q = stackalloc float[4];
                    var sum = 0f;
                    for (int k = 0, slot = 0; k < 4; k++)
                    {
                        if (k == largest)
                            continue;
                        var bits = (r >> (20 - 10 * slot)) & 0x3ff;
                        var value = (bits & 0x1ff) / 511f * Sqrt1_2;
                        q[k] = (bits & 0x200) != 0 ? -value : value;
                        sum += q[k] * q[k];
                        slot++;
                    }

                    q[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sum));
                    cloud.SetRotation(i, q[3], q[0], q[1], q[2]);
                }
                else
                {
                    var x = rotations[i * 3] / 127.5f - 1f;
                    var y = rotations[i * 3 + 1] / 127.5f - 1f;
                    var z = rotations[i * 3 + 2] / 127.5f - 1f;
                    cloud.SetRotation(i, Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y - z * z)), x, y, z);
                }
            }

            return cloud;
        }

        static byte[] Slice(byte[] data, ref int at, int length)
        {
            if (at + length > data.Length)
                throw new FormatException("The SPZ file is shorter than its header says.");
            var slice = new byte[length];
            Buffer.BlockCopy(data, at, slice, 0, length);
            at += length;
            return slice;
        }

        // ---------------------------------------------------------------- GaussianSplats3D .ksplat

        /// <summary>
        /// mkkellogg GaussianSplats3D .ksplat (version 0.1): a 4096-byte header, 1024-byte section headers, then per section
        /// the partial bucket lengths, bucket centres and splat records. Level 0 stores float32s; levels 1 and 2 store 16-bit
        /// positions relative to a bucket centre and float16 scale and rotation (w, x, y, z). Scales are linear; colours are
        /// display RGBA. Same frame as the source PLY (y down).
        /// </summary>
        static SplatCloud Ksplat(byte[] data)
        {
            if (data.Length < 4096 || data[0] != 0 || data[1] != 1)
                throw new FormatException($"Only .ksplat version 0.1 is supported (this file says {(data.Length > 1 ? $"{data[0]}.{data[1]}" : "nothing")}).");

            var maxSections = (int)BitConverter.ToUInt32(data, 4);
            var level = BitConverter.ToUInt16(data, 20);
            if (level > 2)
                throw new FormatException($".ksplat compression level {level} isn't supported.");

            // Count splats from the section headers (some writers leave the main header's counts at 0).
            var total = 0;
            for (var s = 0; s < maxSections; s++)
                total += (int)BitConverter.ToUInt32(data, 4096 + s * 1024);

            var cloud = new SplatCloud(total) { Up = UpAxis.NegativeY };
            var sectionData = 4096 + maxSections * 1024;
            var written = 0;
            for (var s = 0; s < maxSections; s++)
            {
                var header = 4096 + s * 1024;
                var splats = (int)BitConverter.ToUInt32(data, header);
                var stored = (int)BitConverter.ToUInt32(data, header + 4);
                var bucketSize = (int)BitConverter.ToUInt32(data, header + 8);
                var bucketCount = (int)BitConverter.ToUInt32(data, header + 12);
                var blockSize = BitConverter.ToSingle(data, header + 16);
                var scaleRange = (int)BitConverter.ToUInt32(data, header + 24);
                var fullBuckets = (int)BitConverter.ToUInt32(data, header + 32);
                var partialBuckets = (int)BitConverter.ToUInt32(data, header + 36);
                var shDegree = BitConverter.ToUInt16(data, header + 40);
                if (scaleRange == 0)
                    scaleRange = level == 0 ? 1 : 32767;
                var shCoefficients = shDegree == 0 ? 0 : shDegree == 1 ? 9 : 24;
                var stride = level == 0 ? 44 + shCoefficients * 4 : 24 + shCoefficients * (level == 1 ? 2 : 1);

                var partialLengths = sectionData;
                var centres = partialLengths + partialBuckets * 4;
                var records = centres + bucketCount * 12;

                // Which bucket each splat is in: full buckets first, then the partial ones in order.
                var partialIndex = 0;
                var partialEnd = fullBuckets * bucketSize;
                for (var i = 0; i < splats; i++)
                {
                    int bucket;
                    if (i < fullBuckets * bucketSize)
                    {
                        bucket = bucketSize > 0 ? i / bucketSize : 0;
                    }
                    else
                    {
                        while (partialIndex < partialBuckets && i >= partialEnd + (int)BitConverter.ToUInt32(data, partialLengths + partialIndex * 4))
                        {
                            partialEnd += (int)BitConverter.ToUInt32(data, partialLengths + partialIndex * 4);
                            partialIndex++;
                        }

                        bucket = fullBuckets + partialIndex;
                    }

                    var at = records + i * stride;
                    var j = written + i;
                    if (level == 0)
                    {
                        for (var k = 0; k < 3; k++)
                        {
                            cloud.Positions[j * 3 + k] = BitConverter.ToSingle(data, at + k * 4);
                            cloud.Scales[j * 3 + k] = BitConverter.ToSingle(data, at + 12 + k * 4);
                        }

                        cloud.SetRotation(j, BitConverter.ToSingle(data, at + 24), BitConverter.ToSingle(data, at + 28),
                            BitConverter.ToSingle(data, at + 32), BitConverter.ToSingle(data, at + 36));
                        CopyColour(data, at + 40, cloud, j);
                    }
                    else
                    {
                        var half = blockSize * 0.5f / scaleRange;
                        var centre = centres + bucket * 12;
                        for (var k = 0; k < 3; k++)
                        {
                            var u = BitConverter.ToUInt16(data, at + k * 2);
                            cloud.Positions[j * 3 + k] = (u - scaleRange) * half + (bucketCount > 0 ? BitConverter.ToSingle(data, centre + k * 4) : 0f);
                            cloud.Scales[j * 3 + k] = HalfToFloat(BitConverter.ToUInt16(data, at + 6 + k * 2));
                        }

                        cloud.SetRotation(j, HalfToFloat(BitConverter.ToUInt16(data, at + 12)), HalfToFloat(BitConverter.ToUInt16(data, at + 14)),
                            HalfToFloat(BitConverter.ToUInt16(data, at + 16)), HalfToFloat(BitConverter.ToUInt16(data, at + 18)));
                        CopyColour(data, at + 20, cloud, j);
                    }
                }

                written += splats;
                sectionData = records + stored * stride;
            }

            return cloud;
        }

        static void CopyColour(byte[] data, int at, SplatCloud cloud, int i)
        {
            for (var k = 0; k < 4; k++)
                cloud.Colors[i * 4 + k] = data[at + k] / 255f;
        }

        /// <summary>IEEE 754 half → float, in plain C# so it runs on worker threads.</summary>
        public static float HalfToFloat(ushort half)
        {
            var sign = (half >> 15) & 1;
            var exponent = (half >> 10) & 0x1f;
            var mantissa = half & 0x3ff;
            float value;
            if (exponent == 0)
                value = mantissa / 1024f / 16384f;                                   // subnormal: m × 2^-24
            else if (exponent == 31)
                value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
            else
                value = (1f + mantissa / 1024f) * Mathf.Pow(2f, exponent - 15);
            return sign == 1 ? -value : value;
        }
    }
}
