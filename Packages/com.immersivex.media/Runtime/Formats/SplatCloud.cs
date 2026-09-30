using System;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Decoded Gaussian splats (or points), one array entry per splat: position, linear scale, rotation quaternion
    /// (w, x, y, z), colour (0–1, as displayed) and opacity (0–1). Every splat format decodes into this.
    /// </summary>
    public sealed class SplatCloud
    {
        public readonly int Count;
        public readonly float[] Positions; // x, y, z
        public readonly float[] Scales;    // x, y, z (linear)
        public readonly float[] Rotations; // w, x, y, z
        public readonly float[] Colors;    // r, g, b, opacity

        /// <summary>The axis that points up in these coordinates (the format's convention).</summary>
        public UpAxis Up = UpAxis.NegativeY;

        public SplatCloud(int count)
        {
            Count = count;
            Positions = new float[count * 3];
            Scales = new float[count * 3];
            Rotations = new float[count * 4];
            Colors = new float[count * 4];
        }

        /// <summary>SH band 0 constant: colour = 0.5 + C0 × f_dc.</summary>
        public const float ShC0 = 0.28209479177387814f;

        public static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        public void SetRotation(int i, float w, float x, float y, float z)
        {
            var length = Mathf.Sqrt(w * w + x * x + y * y + z * z);
            if (length < 1e-12f)
            {
                w = 1f;
                length = 1f;
            }

            Rotations[i * 4] = w / length;
            Rotations[i * 4 + 1] = x / length;
            Rotations[i * 4 + 2] = y / length;
            Rotations[i * 4 + 3] = z / length;
        }

        /// <summary>
        /// Rotate everything into the y-down frame the renderer uses (x right, y down, z forward, like OpenCV and GenXR
        /// streams). It's a proper rotation about X, so nothing is mirrored and splat orientations stay correct.
        /// </summary>
        public void ToYDown(UpAxis up)
        {
            if (up == UpAxis.Auto)
                up = Up;
            if (up == UpAxis.NegativeY || up == UpAxis.Auto)
                return;

            var (m, q) = TurnToYDown(up);
            for (var i = 0; i < Count; i++)
            {
                float x = Positions[i * 3], y = Positions[i * 3 + 1], z = Positions[i * 3 + 2];
                Positions[i * 3] = m[0] * x + m[1] * y + m[2] * z;
                Positions[i * 3 + 1] = m[3] * x + m[4] * y + m[5] * z;
                Positions[i * 3 + 2] = m[6] * x + m[7] * y + m[8] * z;

                // q' = turn ⊗ q (Hamilton product, w first): the splat turns with the scene.
                float bw = Rotations[i * 4], bx = Rotations[i * 4 + 1], by = Rotations[i * 4 + 2], bz = Rotations[i * 4 + 3];
                Rotations[i * 4] = q.w * bw - q.x * bx - q.y * by - q.z * bz;
                Rotations[i * 4 + 1] = q.w * bx + q.x * bw + q.y * bz - q.z * by;
                Rotations[i * 4 + 2] = q.w * by - q.x * bz + q.y * bw + q.z * bx;
                Rotations[i * 4 + 3] = q.w * bz + q.x * by - q.y * bx + q.z * bw;
            }

            Up = UpAxis.NegativeY;
        }

        /// <summary>
        /// The right-handed rotation about X taking <paramref name="up"/> to −Y, as a row-major matrix and a quaternion
        /// (w, x, y, z): +Y → 180°, +Z → +90°, −Z → −90°.
        /// </summary>
        public static (float[] matrix, (float w, float x, float y, float z) quaternion) TurnToYDown(UpAxis up)
        {
            const float h = 0.70710678f;
            switch (up)
            {
                case UpAxis.PositiveY: return (new[] { 1f, 0f, 0f, 0f, -1f, 0f, 0f, 0f, -1f }, (0f, 1f, 0f, 0f));
                case UpAxis.PositiveZ: return (new[] { 1f, 0f, 0f, 0f, 0f, -1f, 0f, 1f, 0f }, (h, h, 0f, 0f));
                case UpAxis.NegativeZ: return (new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f }, (h, -h, 0f, 0f));
                default: return (new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f }, (1f, 0f, 0f, 0f));
            }
        }

        /// <summary>A copy of only the <paramref name="keep"/> most visible splats (opacity × size), in their original order.</summary>
        /// <summary>
        /// Without the far background: splats more than three times the 90th-percentile distance from the centre, like an
        /// outdoor scan's sky shell. The capture then stands in the room like an object instead of being drawn as a bubble
        /// around it. Returns this cloud when nothing is that far out.
        /// </summary>
        public SplatCloud WithoutFarBackground(out int dropped)
        {
            dropped = 0;
            if (Count < 1000)
                return this;

            // The median centre and the 90th-percentile distance, from a sample.
            var step = Mathf.Max(1, Count / 100_000);
            var samples = (Count + step - 1) / step;
            var axis = new float[samples];
            var centre = Vector3.zero;
            for (var k = 0; k < 3; k++)
            {
                for (int i = 0, s = 0; i < Count; i += step, s++)
                    axis[s] = Positions[i * 3 + k];
                System.Array.Sort(axis);
                centre[k] = axis[samples / 2];
            }

            for (int i = 0, s = 0; i < Count; i += step, s++)
                axis[s] = (new Vector3(Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]) - centre).sqrMagnitude;
            System.Array.Sort(axis);
            var cutoff = 9f * axis[(int)(samples * 0.9f)]; // (3 × the 90th-percentile distance)², in squared units

            var keep = new bool[Count];
            var kept = 0;
            for (var i = 0; i < Count; i++)
            {
                keep[i] = (new Vector3(Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]) - centre).sqrMagnitude <= cutoff;
                if (keep[i])
                    kept++;
            }

            dropped = Count - kept;
            if (dropped == 0)
                return this;
            var result = new SplatCloud(kept) { Up = Up };
            for (int i = 0, j = 0; i < Count; i++)
                if (keep[i])
                    CopyTo(i, result, j++);
            return result;
        }

        public SplatCloud Strongest(int keep)
        {
            if (keep <= 0 || keep >= Count)
                return this;

            // Histogram of log importance, then keep everything above the cut (a linear-time selection).
            const int Bins = 4096;
            var importance = new float[Count];
            float lo = float.MaxValue, hi = float.MinValue;
            for (var i = 0; i < Count; i++)
            {
                var size = Scales[i * 3] * Scales[i * 3 + 1] * Scales[i * 3 + 2];
                var value = Mathf.Log(Mathf.Max(Colors[i * 4 + 3] * Mathf.Pow(Mathf.Max(size, 1e-30f), 2f / 3f), 1e-30f));
                importance[i] = value;
                lo = Mathf.Min(lo, value);
                hi = Mathf.Max(hi, value);
            }

            var histogram = new int[Bins];
            var scale = (Bins - 1) / Mathf.Max(hi - lo, 1e-6f);
            for (var i = 0; i < Count; i++)
                histogram[(int)((importance[i] - lo) * scale)]++;

            int cut = Bins - 1, taken = 0;
            for (; cut >= 0; cut--)
            {
                if (taken + histogram[cut] > keep)
                    break;
                taken += histogram[cut];
            }

            var partial = keep - taken; // how many to take from the cut bin itself
            var result = new SplatCloud(keep) { Up = Up };
            var j = 0;
            for (var i = 0; i < Count && j < keep; i++)
            {
                var bin = (int)((importance[i] - lo) * scale);
                if (bin > cut || (bin == cut && partial-- > 0))
                    CopyTo(i, result, j++);
            }

            return result;
        }

        void CopyTo(int from, SplatCloud target, int to)
        {
            Array.Copy(Positions, from * 3, target.Positions, to * 3, 3);
            Array.Copy(Scales, from * 3, target.Scales, to * 3, 3);
            Array.Copy(Rotations, from * 4, target.Rotations, to * 4, 4);
            Array.Copy(Colors, from * 4, target.Colors, to * 4, 4);
        }
    }
}
