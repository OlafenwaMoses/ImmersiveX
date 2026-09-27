using System;
using System.Collections.Generic;
using System.Globalization;
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

        public Bounds Bounds;
    }

    /// <summary>
    /// Decodes OBJ (with MTL texture names and the "v x y z r g b" vertex-colour extension) and PLY meshes (vertex colours,
    /// per-vertex s/t or u/v, per-face texcoords, MeshLab "TextureFile" comments). Source files are right-handed; they're
    /// converted to Unity's left-handed frame with the model's front (+Z) facing the viewer.
    /// </summary>
    public static class MeshDecoders
    {
        public static MeshFrame Decode(byte[] data, string extension, UpAxis up)
        {
            switch (extension)
            {
                case ".obj": return Obj(Encoding.UTF8.GetString(data), up);
                case ".ply": return Ply(data, PlyFile.Parse(data), up);
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
                        material ??= parts.Length > 1 ? parts[1] : null;
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

                        for (var k = 1; k + 1 < polygon.Count; k++) // fan
                        {
                            triangles.Add(polygon[0]);
                            triangles.Add(polygon[k]);
                            triangles.Add(polygon[k + 1]);
                        }

                        break;
                }
            }

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
            };
            ToUnity(frame, up);
            return frame;
        }

        /// <summary>The diffuse texture (map_Kd) of <paramref name="material"/> in MTL text, or the first one.</summary>
        public static string MtlTexture(string mtl, string material)
        {
            string current = null, first = null;
            foreach (var raw in mtl.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("newmtl ", StringComparison.Ordinal))
                    current = line.Substring(7).Trim();
                else if (line.StartsWith("map_Kd ", StringComparison.Ordinal))
                {
                    var parts = line.Substring(7).Trim().Split(' ');
                    var file = parts[parts.Length - 1]; // options like -s come first
                    first ??= file;
                    if (material == null || current == material)
                        return file;
                }
            }

            return first;
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

            var t = frame.Triangles;
            for (var i = 0; i + 2 < t.Length; i += 3)
                (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);

            var bounds = new Bounds(p.Length > 0 ? p[0] : Vector3.zero, Vector3.zero);
            for (var i = 1; i < p.Length; i++)
                bounds.Encapsulate(p[i]);
            frame.Bounds = bounds;
        }

        static float F(string token) => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);

        static byte Byte(float unit) => (byte)Mathf.Clamp(Mathf.RoundToInt(unit * 255f), 0, 255);
    }
}
