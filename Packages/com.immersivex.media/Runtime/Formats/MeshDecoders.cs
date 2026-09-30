using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A decoded mesh frame in Unity's coordinates (y up, left-handed), ready to upload: positions, triangles, and
    /// optional texture coordinates and vertex colours. Built on worker threads; holds no Unity objects.
    /// </summary>
    public sealed class MeshFrame
    {
        public Vector3[] Positions;
        public int[] Triangles;
        public Vector2[] Uvs;
        public Color32[] Colors;

        /// <summary>Texture file the frame uses (relative to the frame's URL), or null.</summary>
        public string Texture;

        /// <summary>OBJ: the material library (mtllib) and the material (usemtl) whose map_Kd is the texture.</summary>
        public string MaterialLibrary;

        public string Material;

        /// <summary>
        /// OBJ with several materials: the triangles of each (usemtl groups, in order of first use). Null when the whole
        /// mesh uses one material; <see cref="Triangles"/> always holds every triangle.
        /// </summary>
        public MeshPart[] Parts;

        public Bounds Bounds;
    }

    /// <summary>The triangles that use one material.</summary>
    public sealed class MeshPart
    {
        public string Material;
        public int[] Triangles;
    }

    /// <summary>One MTL material: diffuse colour (Kd), diffuse texture (map_Kd) and opacity (d, or 1 − Tr).</summary>
    public sealed class MtlMaterial
    {
        public Color Diffuse = Color.white;
        public string Texture;
        public float Opacity = 1f;
    }

    /// <summary>
    /// Decodes OBJ (several materials, MTL colours and textures, and the "v x y z r g b" vertex-colour extension), PLY meshes
    /// (vertex colours, per-vertex s/t or u/v, per-face texcoords, MeshLab "TextureFile" comments) and STL (binary and
    /// ASCII). Source files are right-handed; they're converted to Unity's left-handed frame with the model's front (+Z)
    /// facing the viewer.
    /// </summary>
    public static class MeshDecoders
    {
        public static MeshFrame Decode(byte[] data, string extension, UpAxis up)
        {
            switch (extension)
            {
                case ".obj": return Obj(Encoding.UTF8.GetString(data), up);
                case ".ply": return Ply(data, PlyFile.Parse(data), up);
                case ".stl": return Stl(data, up);
                default: throw new FormatException($"'{extension}' isn't a mesh format.");
            }
        }

        // ---------------------------------------------------------------- OBJ

        /// <summary>Wavefront OBJ: v (optionally with r g b), vt, f (triangles, quads and polygons; negative indices), usemtl.</summary>
        public static MeshFrame Obj(string text, UpAxis up)
        {
            var positions = new List<Vector3>();
            var colours = new List<Color32>();
            var uvs = new List<Vector2>();
            var outPositions = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var outColours = new List<Color32>();
            var triangles = new List<int>();
            var corners = new Dictionary<long, int>();
            var polygon = new List<int>(8);
            string material = null;
            var hasColour = false;
            var partNames = new List<string>();
            var groups = new Dictionary<string, List<int>>();
            var current = string.Empty;
            List<int> currentTriangles;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 2 || line[0] == '#')
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        positions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                        if (parts.Length >= 7)
                        {
                            hasColour = true;
                            colours.Add(new Color32(Byte(F(parts[4])), Byte(F(parts[5])), Byte(F(parts[6])), 255));
                        }
                        else
                        {
                            colours.Add(new Color32(255, 255, 255, 255));
                        }

                        break;
                    case "vt":
                        uvs.Add(new Vector2(F(parts[1]), parts.Length > 2 ? F(parts[2]) : 0f));
                        break;
                    case "usemtl":
                        var name = parts.Length > 1 ? line.Substring(parts[0].Length).Trim() : string.Empty;
                        material ??= name.Length > 0 ? name : null;
                        current = name;
                        break;
                    case "f":
                        polygon.Clear();
                        for (var k = 1; k < parts.Length; k++)
                        {
                            var indices = parts[k].Split('/');
                            var v = Index(indices[0], positions.Count);
                            var t = indices.Length > 1 && indices[1].Length > 0 ? Index(indices[1], uvs.Count) : -1;
                            var key = ((long)v << 32) | (uint)(t + 1);
                            if (!corners.TryGetValue(key, out var corner))
                            {
                                corner = outPositions.Count;
                                corners.Add(key, corner);
                                outPositions.Add(positions[v]);
                                outColours.Add(colours[v]);
                                outUvs.Add(t >= 0 ? uvs[t] : Vector2.zero);
                            }

                            polygon.Add(corner);
                        }

                        if (!groups.TryGetValue(current, out currentTriangles))
                        {
                            groups.Add(current, currentTriangles = new List<int>());
                            partNames.Add(current);
                        }

                        for (var k = 1; k + 1 < polygon.Count; k++) // fan
                        {
                            currentTriangles.Add(polygon[0]);
                            currentTriangles.Add(polygon[k]);
                            currentTriangles.Add(polygon[k + 1]);
                        }

                        break;
                }
            }

            foreach (var name in partNames)
                triangles.AddRange(groups[name]);
            if (triangles.Count == 0 && positions.Count > 0)
                throw new FormatException("The OBJ file has vertices but no faces.");

            var frame = new MeshFrame
            {
                Positions = outPositions.ToArray(),
                Triangles = triangles.ToArray(),
                Uvs = uvs.Count > 0 ? outUvs.ToArray() : null,
                Colors = hasColour ? outColours.ToArray() : null,
                Material = material,
                MaterialLibrary = MtlLibrary(text),
                Parts = partNames.Count > 1 ? partNames.ConvertAll(name => new MeshPart { Material = name, Triangles = groups[name].ToArray() }).ToArray() : null,
            };
            ToUnity(frame, up);
            return frame;
        }

        /// <summary>The diffuse texture (map_Kd) of <paramref name="material"/> in MTL text, or the first one.</summary>
        public static string MtlTexture(string mtl, string material)
        {
            string first = null;
            foreach (var pair in Mtl(mtl))
            {
                first ??= pair.Value.Texture;
                if (material != null && pair.Key == material && pair.Value.Texture != null)
                    return pair.Value.Texture;
            }

            return first;
        }

        /// <summary>The materials in MTL text, by name, in the order they're defined.</summary>
        public static List<KeyValuePair<string, MtlMaterial>> Mtl(string mtl)
        {
            var materials = new List<KeyValuePair<string, MtlMaterial>>();
            MtlMaterial current = null;
            foreach (var raw in mtl.Split('\n'))
            {
                var line = raw.Trim();
                var space = line.IndexOfAny(new[] { ' ', '\t' });
                if (space < 0)
                    continue;
                var keyword = line.Substring(0, space);
                var rest = line.Substring(space + 1).Trim();
                if (keyword == "newmtl")
                {
                    current = new MtlMaterial();
                    materials.Add(new KeyValuePair<string, MtlMaterial>(rest, current));
                    continue;
                }

                if (current == null)
                    continue;
                var values = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (keyword)
                {
                    case "Kd" when values.Length >= 3:
                        current.Diffuse = new Color(F(values[0]), F(values[1]), F(values[2]), 1f);
                        break;
                    case "d" when values.Length >= 1 && values[0] != "-halo":
                        current.Opacity = Mathf.Clamp01(F(values[0]));
                        break;
                    case "Tr" when values.Length >= 1:
                        current.Opacity = Mathf.Clamp01(1f - F(values[0]));
                        break;
                    case "map_Kd":
                        current.Texture = TextureFile(values);
                        break;
                }
            }

            return materials;
        }

        /// <summary>Every texture file an MTL names (diffuse, bump, normal, opacity…), for copying a model with its files.</summary>
        public static List<string> MtlTextures(string mtl)
        {
            var files = new List<string>();
            foreach (var raw in mtl.Split('\n'))
            {
                var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                var keyword = parts[0].ToLowerInvariant();
                if (!keyword.StartsWith("map_", StringComparison.Ordinal) && keyword != "bump" && keyword != "norm" && keyword != "disp" &&
                    keyword != "decal" && keyword != "refl")
                    continue;
                var file = TextureFile(parts.Skip(1).ToArray());
                if (!string.IsNullOrEmpty(file) && !files.Contains(file))
                    files.Add(file);
            }

            return files;
        }

        /// <summary>The file name at the end of an MTL texture statement, after its options (-s 1 1 1, -clamp on…).</summary>
        static string TextureFile(string[] values)
        {
            var at = 0;
            while (at < values.Length && values[at].StartsWith("-", StringComparison.Ordinal) && values[at].Length > 1)
            {
                var arguments = values[at] == "-o" || values[at] == "-s" || values[at] == "-t" ? 3
                    : values[at] == "-mm" ? 2
                    : 1; // -blendu, -blendv, -cc, -clamp, -bm, -boost, -texres, -imfchan
                at += 1 + arguments;
            }

            return at < values.Length ? string.Join(" ", values, at, values.Length - at) : null;
        }

        /// <summary>The MTL library an OBJ names (mtllib), or null.</summary>
        public static string MtlLibrary(string obj)
        {
            foreach (var raw in obj.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("mtllib ", StringComparison.Ordinal))
                    return line.Substring(7).Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal))
                    break; // mtllib comes before the geometry
            }

            return null;
        }

        static int Index(string token, int count)
        {
            var index = int.Parse(token, CultureInfo.InvariantCulture);
            return index < 0 ? count + index : index - 1;
        }

        // ---------------------------------------------------------------- STL

        /// <summary>
        /// STL (binary or ASCII): triangles only, no colours or textures. Usually z-up, in millimetres; the media is fitted
        /// to its height anyway.
        /// </summary>
        public static MeshFrame Stl(byte[] data, UpAxis up)
        {
            var positions = new List<Vector3>();
            var binaryCount = data.Length >= 84 ? BitConverter.ToUInt32(data, 80) : 0u;
            if (data.Length >= 84 && 84L + binaryCount * 50L == data.Length)
            {
                for (var i = 0; i < binaryCount; i++)
                {
                    var at = 84 + i * 50 + 12; // after the facet normal
                    for (var corner = 0; corner < 3; corner++, at += 12)
                        positions.Add(new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8)));
                }
            }
            else
            {
                foreach (var raw in Encoding.ASCII.GetString(data).Split('\n'))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                        positions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                }
            }

            if (positions.Count < 3)
                throw new FormatException("The STL file has no triangles.");
            var triangles = new int[positions.Count / 3 * 3];
            for (var i = 0; i < triangles.Length; i++)
                triangles[i] = i;
            var frame = new MeshFrame { Positions = positions.ToArray(), Triangles = triangles };
            ToUnity(frame, up);
            return frame;
        }

        // ---------------------------------------------------------------- PLY

        public static MeshFrame Ply(byte[] data, PlyFile ply, UpAxis up)
        {
            var vertex = ply.Find("vertex");
            var face = ply.Find("face");
            if (vertex == null || face == null)
                throw new FormatException("The PLY file has no faces.");

            int X = vertex.Properties.FindIndex(p => p.Name == "x"), Y = vertex.Properties.FindIndex(p => p.Name == "y"), Z = vertex.Properties.FindIndex(p => p.Name == "z");
            var red = FirstIndex(vertex, "red", "diffuse_red");
            var u = FirstIndex(vertex, "s", "u", "texture_u", "texture_s");
            var v = FirstIndex(vertex, "t", "v", "texture_v", "texture_t");
            var colourScale = red >= 0 && vertex.Properties[red].Type != PlyFile.ValueType.Float32 && vertex.Properties[red].Type != PlyFile.ValueType.Float64 ? 1f : 255f;

            var positions = new Vector3[vertex.Count];
            var colours = red >= 0 ? new Color32[vertex.Count] : null;
            var uvs = u >= 0 && v >= 0 ? new Vector2[vertex.Count] : null;
            float Value(byte[] d, int at, float[] values, int index) =>
                values != null ? values[index] : PlyFile.Read(d, at + vertex.Properties[index].Offset, vertex.Properties[index].Type);

            var next = ply.ReadElement(data, vertex, ply.StartOf(data, vertex), (i, at, values) =>
            {
                positions[i] = new Vector3(Value(data, at, values, X), Value(data, at, values, Y), Value(data, at, values, Z));
                if (colours != null)
                    colours[i] = new Color32(Byte(Value(data, at, values, red) * colourScale / 255f), Byte(Value(data, at, values, red + 1) * colourScale / 255f),
                        Byte(Value(data, at, values, red + 2) * colourScale / 255f), 255);
                if (uvs != null)
                    uvs[i] = new Vector2(Value(data, at, values, u), Value(data, at, values, v));
            });

            // Faces: a vertex_indices list, and optionally a per-corner texcoord list (MeshLab).
            var indicesProperty = face.Properties.Find(p => p.IsList && (p.Name == "vertex_indices" || p.Name == "vertex_index"));
            var texcoordProperty = face.Properties.Find(p => p.IsList && p.Name == "texcoord");
            if (indicesProperty == null)
                throw new FormatException("The PLY faces have no vertex_indices.");

            var triangles = new List<int>(face.Count * 3);
            var cornerPositions = texcoordProperty != null ? new List<Vector3>(face.Count * 3) : null;
            var cornerUvs = texcoordProperty != null ? new List<Vector2>(face.Count * 3) : null;
            var cornerColours = texcoordProperty != null && colours != null ? new List<Color32>(face.Count * 3) : null;
            var polygon = new List<int>(8);
            var polygonUv = new List<Vector2>(8);

            if (ply.Format == PlyFile.Encoding.Ascii)
            {
                var faceStart = face == ply.Elements[ply.Elements.IndexOf(vertex) + 1] ? next : ply.StartOf(data, face);
                ply.ReadElement(data, face, faceStart, (i, at, values) =>
                {
                    var k = 0;
                    polygon.Clear();
                    polygonUv.Clear();
                    foreach (var property in face.Properties)
                    {
                        if (!property.IsList)
                        {
                            k++;
                            continue;
                        }

                        var count = (int)values[k++];
                        for (var c = 0; c < count; c++, k++)
                        {
                            if (property == indicesProperty)
                                polygon.Add((int)values[k]);
                            else if (property == texcoordProperty && (c & 1) == 1)
                                polygonUv.Add(new Vector2(values[k - 1], values[k]));
                        }
                    }

                    AddPolygon(polygon, polygonUv, positions, colours, triangles, cornerPositions, cornerUvs, cornerColours);
                });
            }
            else
            {
                var at = face == ply.Elements[ply.Elements.IndexOf(vertex) + 1] ? next : ply.StartOf(data, face);
                for (var i = 0; i < face.Count; i++)
                {
                    polygon.Clear();
                    polygonUv.Clear();
                    foreach (var property in face.Properties)
                    {
                        if (!property.IsList)
                        {
                            at += PlyFile.Size(property.Type);
                            continue;
                        }

                        var count = PlyFile.ReadInt(data, at, property.CountType);
                        at += PlyFile.Size(property.CountType);
                        var size = PlyFile.Size(property.Type);
                        for (var c = 0; c < count; c++, at += size)
                        {
                            if (property == indicesProperty)
                                polygon.Add(PlyFile.ReadInt(data, at, property.Type));
                            else if (property == texcoordProperty && (c & 1) == 1)
                                polygonUv.Add(new Vector2(PlyFile.Read(data, at - size, property.Type), PlyFile.Read(data, at, property.Type)));
                        }
                    }

                    AddPolygon(polygon, polygonUv, positions, colours, triangles, cornerPositions, cornerUvs, cornerColours);
                }
            }

            var frame = cornerPositions != null
                ? new MeshFrame { Positions = cornerPositions.ToArray(), Uvs = cornerUvs.ToArray(), Colors = cornerColours?.ToArray(), Triangles = triangles.ToArray() }
                : new MeshFrame { Positions = positions, Uvs = uvs, Colors = colours, Triangles = triangles.ToArray() };
            frame.Texture = ply.TextureFile;
            ToUnity(frame, up);
            return frame;
        }

        static void AddPolygon(List<int> polygon, List<Vector2> polygonUv, Vector3[] positions, Color32[] colours, List<int> triangles,
            List<Vector3> cornerPositions, List<Vector2> cornerUvs, List<Color32> cornerColours)
        {
            if (cornerPositions == null)
            {
                for (var k = 1; k + 1 < polygon.Count; k++)
                {
                    triangles.Add(polygon[0]);
                    triangles.Add(polygon[k]);
                    triangles.Add(polygon[k + 1]);
                }

                return;
            }

            // Per-corner texcoords: every corner gets its own vertex.
            var first = cornerPositions.Count;
            for (var c = 0; c < polygon.Count; c++)
            {
                cornerPositions.Add(positions[polygon[c]]);
                cornerUvs.Add(c < polygonUv.Count ? polygonUv[c] : Vector2.zero);
                cornerColours?.Add(colours[polygon[c]]);
            }

            for (var k = 1; k + 1 < polygon.Count; k++)
            {
                triangles.Add(first);
                triangles.Add(first + k);
                triangles.Add(first + k + 1);
            }
        }

        static int FirstIndex(PlyFile.Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var index = element.Properties.FindIndex(p => p.Name == name && !p.IsList);
                if (index >= 0)
                    return index;
            }

            return -1;
        }

        // ---------------------------------------------------------------- coordinates

        /// <summary>
        /// Right-handed source (with the given up axis) → Unity: a reflection, so the triangle winding is reversed too. The
        /// model's front ends up facing −Z, towards the viewer standing in front of it.
        /// </summary>
        public static void ToUnity(MeshFrame frame, UpAxis up)
        {
            var p = frame.Positions;
            for (var i = 0; i < p.Length; i++)
            {
                var v = p[i];
                switch (up)
                {
                    case UpAxis.NegativeY: p[i] = new Vector3(v.x, -v.y, v.z); break;
                    case UpAxis.PositiveZ: p[i] = new Vector3(v.x, v.z, v.y); break;
                    case UpAxis.NegativeZ: p[i] = new Vector3(v.x, -v.z, -v.y); break;
                    default: p[i] = new Vector3(v.x, v.y, -v.z); break; // +Y up (the usual)
                }
            }

            FlipWinding(frame.Triangles);
            if (frame.Parts != null)
                foreach (var part in frame.Parts)
                    FlipWinding(part.Triangles);

            var bounds = new Bounds(p.Length > 0 ? p[0] : Vector3.zero, Vector3.zero);
            for (var i = 1; i < p.Length; i++)
                bounds.Encapsulate(p[i]);
            frame.Bounds = bounds;
        }

        static void FlipWinding(int[] t)
        {
            for (var i = 0; i + 2 < t.Length; i += 3)
                (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        }

        static float F(string token) => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);

        static byte Byte(float unit) => (byte)Mathf.Clamp(Mathf.RoundToInt(unit * 255f), 0, 255);
    }
}
