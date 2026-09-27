using System;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Decodes Gaussian-splat and point-cloud files into a <see cref="SplatCloud"/>: 3DGS <c>.ply</c> (INRIA and
    /// PlayCanvas compressed), point-cloud <c>.ply</c>, <c>.splat</c>, <c>.spz</c> and <c>.ksplat</c>.
    /// Runs on a worker thread; uses no Unity objects.
    /// </summary>
    public static partial class SplatDecoders
    {
        /// <summary>Decode by file extension (".ply", ".splat", ".spz", ".ksplat").</summary>
        public static SplatCloud Decode(byte[] data, string extension)
        {
            switch (extension)
            {
                case ".ply": return Ply(data, PlyFile.Parse(data));
                case ".splat": return Splat(data);
                case ".spz": return Spz(data);
                case ".ksplat": return Ksplat(data);
                default: throw new FormatException($"'{extension}' isn't a splat format.");
            }
        }

        public static SplatCloud Ply(byte[] data, PlyFile ply)
        {
            switch (ply.Content)
            {
                case PlyContent.GaussianSplats: return InriaPly(data, ply);
                case PlyContent.CompressedSplats: return CompressedPly(data, ply);
                case PlyContent.PointCloud: return PointCloud(data, ply);
                default: throw new FormatException("This PLY file holds a mesh, not splats or points.");
            }
        }

        /// <summary>
        /// 3D Gaussian Splatting PLY (graphdeco-inria): position, f_dc_0..2 (SH band 0), opacity (logit), scale_0..2 (log),
        /// rot_0..3 (w, x, y, z, not normalised). Usually in COLMAP's frame: x right, y down, z forward.
        /// </summary>
        static SplatCloud InriaPly(byte[] data, PlyFile ply)
        {
            var vertex = ply.Find("vertex");
            var cloud = new SplatCloud(vertex.Count) { Up = UpAxis.NegativeY };
            var p = Properties(vertex, "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2", "opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3");
            ply.ReadElement(data, vertex, ply.StartOf(data, vertex), (i, at, values) =>
            {
                float V(int k) => values != null ? values[p[k].Index] : PlyFile.Read(data, at + p[k].Property.Offset, p[k].Property.Type);
                cloud.Positions[i * 3] = V(0);
                cloud.Positions[i * 3 + 1] = V(1);
                cloud.Positions[i * 3 + 2] = V(2);
                cloud.Colors[i * 4] = Mathf.Clamp01(0.5f + SplatCloud.ShC0 * V(3));
                cloud.Colors[i * 4 + 1] = Mathf.Clamp01(0.5f + SplatCloud.ShC0 * V(4));
                cloud.Colors[i * 4 + 2] = Mathf.Clamp01(0.5f + SplatCloud.ShC0 * V(5));
                cloud.Colors[i * 4 + 3] = SplatCloud.Sigmoid(V(6));
                cloud.Scales[i * 3] = Mathf.Exp(V(7));
                cloud.Scales[i * 3 + 1] = Mathf.Exp(V(8));
                cloud.Scales[i * 3 + 2] = Mathf.Exp(V(9));
                cloud.SetRotation(i, V(10), V(11), V(12), V(13));
            });
            return cloud;
        }

        /// <summary>
        /// A coloured point cloud (x, y, z and optional red/green/blue): each point becomes a small round splat sized from
        /// the point spacing. Point clouds are usually y-up.
        /// </summary>
        static SplatCloud PointCloud(byte[] data, PlyFile ply)
        {
            var vertex = ply.Find("vertex");
            var cloud = new SplatCloud(vertex.Count) { Up = UpAxis.PositiveY };
            var p = Properties(vertex, "x", "y", "z");
            var colour = FirstPresent(vertex, "red", "diffuse_red", "r");
            var hasColour = colour != null;
            var c = colour == "red" ? Properties(vertex, "red", "green", "blue")
                : colour == "diffuse_red" ? Properties(vertex, "diffuse_red", "diffuse_green", "diffuse_blue")
                : colour == "r" ? Properties(vertex, "r", "g", "b")
                : null;
            var colourScale = hasColour && c[0].Property.Type != PlyFile.ValueType.Float32 && c[0].Property.Type != PlyFile.ValueType.Float64
                ? 1f / 255f
                : 1f;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            ply.ReadElement(data, vertex, ply.StartOf(data, vertex), (i, at, values) =>
            {
                float V(Field[] f, int k) => values != null ? values[f[k].Index] : PlyFile.Read(data, at + f[k].Property.Offset, f[k].Property.Type);
                var x = V(p, 0);
                var y = V(p, 1);
                var z = V(p, 2);
                cloud.Positions[i * 3] = x;
                cloud.Positions[i * 3 + 1] = y;
                cloud.Positions[i * 3 + 2] = z;
                min = Vector3.Min(min, new Vector3(x, y, z));
                max = Vector3.Max(max, new Vector3(x, y, z));
                cloud.Colors[i * 4] = hasColour ? Mathf.Clamp01(V(c, 0) * colourScale) : 0.8f;
                cloud.Colors[i * 4 + 1] = hasColour ? Mathf.Clamp01(V(c, 1) * colourScale) : 0.8f;
                cloud.Colors[i * 4 + 2] = hasColour ? Mathf.Clamp01(V(c, 2) * colourScale) : 0.8f;
                cloud.Colors[i * 4 + 3] = 1f;
                cloud.Rotations[i * 4] = 1f;
            });

            // Points on a surface: spacing ≈ √(area / count); approximate the area by the bounding box's largest faces.
            var size = max - min;
            var faces = new[] { size.x * size.y, size.y * size.z, size.x * size.z };
            Array.Sort(faces);
            var spacing = Mathf.Sqrt(Mathf.Max(faces[2] + faces[1], 1e-8f) / Mathf.Max(1, vertex.Count));
            var radius = Mathf.Max(spacing * 0.7f, 1e-5f);
            for (var i = 0; i < cloud.Count * 3; i++)
                cloud.Scales[i] = radius;
            return cloud;
        }

        /// <summary>
        /// antimatter15 .splat: 32 bytes per splat — position (float32 × 3), linear scale (float32 × 3), colour and
        /// opacity (uint8 × 4), rotation (uint8 × 4: w, x, y, z as value × 128 + 128). Same frame as the PLY it came from.
        /// </summary>
        static SplatCloud Splat(byte[] data)
        {
            const int Stride = 32;
            if (data.Length % Stride != 0)
                throw new FormatException($".splat files are a multiple of 32 bytes; this one is {data.Length}.");
            var count = data.Length / Stride;
            var cloud = new SplatCloud(count) { Up = UpAxis.NegativeY };
            for (var i = 0; i < count; i++)
            {
                var at = i * Stride;
                cloud.Positions[i * 3] = BitConverter.ToSingle(data, at);
                cloud.Positions[i * 3 + 1] = BitConverter.ToSingle(data, at + 4);
                cloud.Positions[i * 3 + 2] = BitConverter.ToSingle(data, at + 8);
                cloud.Scales[i * 3] = BitConverter.ToSingle(data, at + 12);
                cloud.Scales[i * 3 + 1] = BitConverter.ToSingle(data, at + 16);
                cloud.Scales[i * 3 + 2] = BitConverter.ToSingle(data, at + 20);
                cloud.Colors[i * 4] = data[at + 24] / 255f;
                cloud.Colors[i * 4 + 1] = data[at + 25] / 255f;
                cloud.Colors[i * 4 + 2] = data[at + 26] / 255f;
                cloud.Colors[i * 4 + 3] = data[at + 27] / 255f;
                cloud.SetRotation(i, (data[at + 28] - 128) / 128f, (data[at + 29] - 128) / 128f, (data[at + 30] - 128) / 128f, (data[at + 31] - 128) / 128f);
            }

            return cloud;
        }

        // ---------------------------------------------------------------- PLY helpers

        struct Field
        {
            public PlyFile.Property Property;

            /// <summary>Position among the record's values (ASCII).</summary>
            public int Index;
        }

        static Field[] Properties(PlyFile.Element element, params string[] names)
        {
            var fields = new Field[names.Length];
            for (var k = 0; k < names.Length; k++)
            {
                var index = element.Properties.FindIndex(p => p.Name == names[k]);
                if (index < 0)
                    throw new FormatException($"The PLY file has no '{names[k]}' property.");
                fields[k] = new Field { Property = element.Properties[index], Index = index };
            }

            return fields;
        }

        static string FirstPresent(PlyFile.Element element, params string[] names)
        {
            foreach (var name in names)
                if (element.Has(name))
                    return name;
            return null;
        }
    }
}
