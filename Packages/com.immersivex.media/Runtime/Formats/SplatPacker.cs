using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Packs a <see cref="SplatCloud"/> into the frame layout GenXR streams use (16-byte texels plus opacity bytes), so
    /// every splat format draws through the same renderer and shader as a 3.5D hologram.
    /// </summary>
    public static class SplatPacker
    {
        public const int TextureWidth = 1024;

        /// <summary>
        /// Pack <paramref name="cloud"/> (already y-down). Positions are quantised to 16 bits inside the bounds, log scales
        /// to 8 bits inside <paramref name="scaleRange"/>, rotation and colour to 8 bits.
        /// </summary>
        public static uint[] Pack(SplatCloud cloud, out HologramFrameHeader header, out Vector2 scaleRange)
        {
            var count = cloud.Count;
            var rows = Math.Max(1, (count + TextureWidth - 1) / TextureWidth);
            var data = new uint[HologramFrame.ExpectedBytes(rows, TextureWidth) / 4];

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            float lo = float.MaxValue, hi = float.MinValue;
            for (var i = 0; i < count; i++)
            {
                var p = new Vector3(cloud.Positions[i * 3], cloud.Positions[i * 3 + 1], cloud.Positions[i * 3 + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                for (var k = 0; k < 3; k++)
                {
                    var log = Mathf.Log(Mathf.Max(cloud.Scales[i * 3 + k], 1e-7f));
                    lo = Mathf.Min(lo, log);
                    hi = Mathf.Max(hi, log);
                }
            }

            if (count == 0)
            {
                min = max = Vector3.zero;
                lo = -9f;
                hi = 1f;
            }

            lo = Mathf.Max(lo, -16f);
            hi = Mathf.Clamp(hi, lo + 1e-3f, 8f);
            scaleRange = new Vector2(lo, hi);
            header = new HologramFrameHeader { Count = count, Min = min, Max = max, Rows = rows };

            data[0] = (uint)count;
            data[1] = Bits(min.x);
            data[2] = Bits(min.y);
            data[3] = Bits(min.z);
            data[4] = Bits(max.x);
            data[5] = Bits(max.y);
            data[6] = Bits(max.z);
            data[7] = (uint)rows;

            var alphaStart = HologramFrame.AlphaStart(header, TextureWidth);
            var extent = max - min;
            var scaleSpan = hi - lo;
            for (var i = 0; i < count; i++)
            {
                uint px = Quantize16(cloud.Positions[i * 3], min.x, extent.x);
                uint py = Quantize16(cloud.Positions[i * 3 + 1], min.y, extent.y);
                uint pz = Quantize16(cloud.Positions[i * 3 + 2], min.z, extent.z);
                uint s0 = QuantizeScale(cloud.Scales[i * 3], lo, scaleSpan);
                uint s1 = QuantizeScale(cloud.Scales[i * 3 + 1], lo, scaleSpan);
                uint s2 = QuantizeScale(cloud.Scales[i * 3 + 2], lo, scaleSpan);
                uint rw = QuantizeUnit(cloud.Rotations[i * 4]);
                uint rx = QuantizeUnit(cloud.Rotations[i * 4 + 1]);
                uint ry = QuantizeUnit(cloud.Rotations[i * 4 + 2]);
                uint rz = QuantizeUnit(cloud.Rotations[i * 4 + 3]);
                uint r = Byte(cloud.Colors[i * 4]);
                uint g = Byte(cloud.Colors[i * 4 + 1]);
                uint b = Byte(cloud.Colors[i * 4 + 2]);
                uint a = Byte(cloud.Colors[i * 4 + 3]);

                var t = HologramFrame.HeaderUints + i * 4;
                data[t] = px | (py << 16);
                data[t + 1] = pz | (s0 << 16) | (s1 << 24);
                data[t + 2] = s2 | (rw << 8) | (rx << 16) | (ry << 24);
                data[t + 3] = rz | (r << 8) | (g << 16) | (b << 24);
                data[alphaStart + (i >> 2)] |= a << ((i & 3) * 8);
            }

            return data;
        }

        /// <summary>Decode, select and pack on a worker thread.</summary>
        public static Task<PackedSplats> PackAsync(Func<SplatCloud> decode, UpAxis up, int maxCount) => Task.Run(() =>
        {
            var cloud = decode();
            var total = cloud.Count;
            cloud.ToYDown(up);
            cloud = cloud.Strongest(maxCount);
            var data = Pack(cloud, out var header, out var range);
            return new PackedSplats(data, header, range, cloud.Positions, total);
        });

        static uint Quantize16(float value, float min, float extent) =>
            extent <= 0f ? 0u : (uint)Mathf.Clamp(Mathf.RoundToInt((value - min) / extent * 65535f), 0, 65535);

        static uint QuantizeScale(float scale, float lo, float span) =>
            (uint)Mathf.Clamp(Mathf.RoundToInt((Mathf.Log(Mathf.Max(scale, 1e-7f)) - lo) / span * 255f), 0, 255);

        static uint QuantizeUnit(float value) => (uint)Mathf.Clamp(Mathf.RoundToInt(value * 128f + 128f), 0, 255);

        static uint Byte(float value) => (uint)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        static uint Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);
    }

    /// <summary>A packed frame plus what drawing and sorting need.</summary>
    public sealed class PackedSplats
    {
        public readonly uint[] Data;
        public readonly HologramFrameHeader Header;
        public readonly Vector2 ScaleRange;

        /// <summary>Unquantised positions (x, y, z) of the packed splats, in the same order, for sorting.</summary>
        public readonly float[] Positions;

        /// <summary>How many splats the file had before the most visible were kept.</summary>
        public readonly int SourceCount;

        public PackedSplats(uint[] data, HologramFrameHeader header, Vector2 scaleRange, float[] positions, int sourceCount)
        {
            Data = data;
            Header = header;
            ScaleRange = scaleRange;
            Positions = positions;
            SourceCount = sourceCount;
        }
    }

    /// <summary>
    /// Sorts splats back to front on a worker thread, so big static scenes don't hold up the frame. Start a sort when the
    /// viewer moves; collect it when it's done.
    /// </summary>
    sealed class SplatSorter
    {
        readonly float[] _positions;
        readonly int _count;
        readonly int[] _keys;
        readonly int[] _buckets = new int[65536];
        readonly uint[] _order;
        Task _task;

        public SplatSorter(float[] positions, int count)
        {
            _positions = positions;
            _count = count;
            _keys = new int[count];
            _order = new uint[count];
        }

        public bool Busy => _task != null && !_task.IsCompleted;

        public void Start(Vector3 viewer)
        {
            if (Busy)
                return;
            _task = Task.Run(() => HologramFrame.SortBackToFront(_positions, _count, viewer, _keys, _buckets, _order));
        }

        /// <summary>The finished order (valid until the next <see cref="Start"/>), or false while sorting.</summary>
        public bool TryFinish(out uint[] order)
        {
            order = null;
            if (_task == null || !_task.IsCompleted)
                return false;
            if (_task.IsFaulted)
                ImmersiveXLog.Warn($"Splat sort failed: {_task.Exception?.GetBaseException().Message}");
            _task = null;
            order = _order;
            return true;
        }
    }
}
