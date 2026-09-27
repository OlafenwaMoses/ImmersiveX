using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImmersiveX.Media
{
    /// <summary>What a PLY file holds, judged from its header.</summary>
    public enum PlyContent
    {
        Unknown,

        /// <summary>3D Gaussian Splatting (INRIA layout: f_dc, opacity, scale, rot).</summary>
        GaussianSplats,

        /// <summary>PlayCanvas / SuperSplat compressed splats (a chunk element and packed vertex fields).</summary>
        CompressedSplats,

        /// <summary>Points with optional colours and no faces.</summary>
        PointCloud,

        /// <summary>A triangle or polygon mesh.</summary>
        Mesh,
    }

    /// <summary>
    /// Reads PLY files (ASCII and binary little-endian): the header, then each element's records. Used for Gaussian
    /// splats, point clouds and meshes.
    /// </summary>
    public sealed class PlyFile
    {
        public enum Encoding { Ascii, BinaryLittleEndian, BinaryBigEndian }

        public enum ValueType { Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64 }

        public sealed class Property
        {
            public string Name;
            public ValueType Type;
            public bool IsList;
            public ValueType CountType;

            /// <summary>Byte offset inside a record, for elements without lists.</summary>
            public int Offset;
        }

        public sealed class Element
        {
            public string Name;
            public int Count;
            public readonly List<Property> Properties = new List<Property>();

            /// <summary>Bytes per record when there are no list properties, else 0.</summary>
            public int Stride;

            /// <summary>Where the element's data starts in the file (binary), or its first line (ASCII).</summary>
            public int DataStart;

            public Property Find(string name) => Properties.Find(p => p.Name == name);
            public bool Has(string name) => Find(name) != null;
        }

        public Encoding Format { get; private set; }
        public int HeaderLength { get; private set; }
        public readonly List<Element> Elements = new List<Element>();
        public readonly List<string> Comments = new List<string>();

        public Element Find(string name) => Elements.Find(e => e.Name == name);

        /// <summary>Texture file named in a MeshLab-style "comment TextureFile name.png" line, or null.</summary>
        public string TextureFile
        {
            get
            {
                foreach (var comment in Comments)
                    if (comment.StartsWith("TextureFile ", StringComparison.OrdinalIgnoreCase))
                        return comment.Substring(12).Trim();
                return null;
            }
        }

        public PlyContent Content
        {
            get
            {
                var vertex = Find("vertex");
                if (vertex == null)
                    return PlyContent.Unknown;
                if (Find("chunk") != null && vertex.Has("packed_position"))
                    return PlyContent.CompressedSplats;
                if (vertex.Has("f_dc_0") && vertex.Has("opacity") && vertex.Has("scale_0") && vertex.Has("rot_0"))
                    return PlyContent.GaussianSplats;
                var face = Find("face");
                return face != null && face.Count > 0 ? PlyContent.Mesh : PlyContent.PointCloud;
            }
        }

        /// <summary>Parse the header of <paramref name="data"/> (the whole file, or at least its header).</summary>
        public static PlyFile Parse(byte[] data)
        {
            var end = IndexOf(data, "end_header");
            if (data.Length < 4 || data[0] != 'p' || data[1] != 'l' || data[2] != 'y' || end < 0)
                throw new FormatException("Not a PLY file.");

            var headerEnd = end + "end_header".Length;
            while (headerEnd < data.Length && data[headerEnd] != '\n')
                headerEnd++;
            headerEnd++; // past the newline

            var ply = new PlyFile { HeaderLength = headerEnd };
            var text = System.Text.Encoding.ASCII.GetString(data, 0, headerEnd);
            Element current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "format":
                        ply.Format = parts[1] == "ascii" ? Encoding.Ascii
                            : parts[1] == "binary_big_endian" ? Encoding.BinaryBigEndian
                            : Encoding.BinaryLittleEndian;
                        break;
                    case "comment":
                    case "obj_info":
                        ply.Comments.Add(line.Length > parts[0].Length ? line.Substring(parts[0].Length + 1) : string.Empty);
                        break;
                    case "element":
                        current = new Element { Name = parts[1], Count = int.Parse(parts[2], CultureInfo.InvariantCulture) };
                        ply.Elements.Add(current);
                        break;
                    case "property" when current != null:
                        var property = parts[1] == "list"
                            ? new Property { IsList = true, CountType = ParseType(parts[2]), Type = ParseType(parts[3]), Name = parts[4] }
                            : new Property { Type = ParseType(parts[1]), Name = parts[2] };
                        current.Properties.Add(property);
                        break;
                }
            }

            if (ply.Format == Encoding.BinaryBigEndian)
                throw new FormatException("Big-endian PLY files aren't supported; re-save as binary little-endian.");

            // Record layout and where each element's data starts (binary elements without lists have a fixed stride).
            var offset = headerEnd;
            foreach (var element in ply.Elements)
            {
                element.DataStart = offset;
                var stride = 0;
                var fixedSize = true;
                foreach (var property in element.Properties)
                {
                    if (property.IsList)
                    {
                        fixedSize = false;
                        continue;
                    }

                    property.Offset = stride;
                    stride += Size(property.Type);
                }

                element.Stride = fixedSize ? stride : 0;
                if (ply.Format == Encoding.Ascii || !fixedSize)
                    offset = -1; // later elements' offsets are only known once this one is read
                else if (offset >= 0)
                    offset += stride * element.Count;
            }

            return ply;
        }

        /// <summary>Where <paramref name="target"/>'s data starts, reading past any elements before it.</summary>
        public int StartOf(byte[] data, Element target)
        {
            var at = HeaderLength;
            foreach (var element in Elements)
            {
                if (element == target)
                    return at;
                at = ReadElement(data, element, at, (i, offset, values) => { });
            }

            throw new FormatException($"The PLY file has no '{target.Name}' element.");
        }

        /// <summary>Read one scalar of any PLY type at <paramref name="at"/> as a float (binary little-endian).</summary>
        public static float Read(byte[] data, int at, ValueType type)
        {
            switch (type)
            {
                case ValueType.Float32: return BitConverter.ToSingle(data, at);
                case ValueType.UInt8: return data[at];
                case ValueType.Int8: return (sbyte)data[at];
                case ValueType.Int16: return BitConverter.ToInt16(data, at);
                case ValueType.UInt16: return BitConverter.ToUInt16(data, at);
                case ValueType.Int32: return BitConverter.ToInt32(data, at);
                case ValueType.UInt32: return BitConverter.ToUInt32(data, at);
                case ValueType.Float64: return (float)BitConverter.ToDouble(data, at);
                default: return 0f;
            }
        }

        /// <summary>Read an integer of any PLY type (list counts and indices).</summary>
        public static int ReadInt(byte[] data, int at, ValueType type)
        {
            switch (type)
            {
                case ValueType.UInt8: return data[at];
                case ValueType.Int8: return (sbyte)data[at];
                case ValueType.Int16: return BitConverter.ToInt16(data, at);
                case ValueType.UInt16: return BitConverter.ToUInt16(data, at);
                case ValueType.Int32: return BitConverter.ToInt32(data, at);
                case ValueType.UInt32: return (int)BitConverter.ToUInt32(data, at);
                case ValueType.Float32: return (int)BitConverter.ToSingle(data, at);
                case ValueType.Float64: return (int)BitConverter.ToDouble(data, at);
                default: return 0;
            }
        }

        public static int Size(ValueType type)
        {
            switch (type)
            {
                case ValueType.Int8:
                case ValueType.UInt8: return 1;
                case ValueType.Int16:
                case ValueType.UInt16: return 2;
                case ValueType.Float64: return 8;
                default: return 4;
            }
        }

        /// <summary>
        /// Visit every record of <paramref name="element"/> in order. For binary data the callback gets the record's byte
        /// offset; for ASCII it gets the record's numbers in <c>values</c> (lists expanded in place, count first).
        /// Returns the offset just past the element (binary) so the next element can be read.
        /// </summary>
        public int ReadElement(byte[] data, Element element, int start, Action<int, int, float[]> record)
        {
            if (Format == Encoding.Ascii)
                return ReadAscii(data, element, start, record);

            var at = start;
            for (var i = 0; i < element.Count; i++)
            {
                record(i, at, null);
                if (element.Stride > 0)
                {
                    at += element.Stride;
                    continue;
                }

                foreach (var property in element.Properties)
                {
                    if (!property.IsList)
                    {
                        at += Size(property.Type);
                        continue;
                    }

                    var count = ReadInt(data, at, property.CountType);
                    at += Size(property.CountType) + count * Size(property.Type);
                }
            }

            return at;
        }

        int ReadAscii(byte[] data, Element element, int start, Action<int, int, float[]> record)
        {
            var at = start;
            var values = new List<float>(16);
            var buffer = new float[64];
            for (var i = 0; i < element.Count; i++)
            {
                var lineEnd = at;
                while (lineEnd < data.Length && data[lineEnd] != '\n')
                    lineEnd++;
                var line = System.Text.Encoding.ASCII.GetString(data, at, lineEnd - at);
                values.Clear();
                foreach (var token in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    values.Add(float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                if (buffer.Length < values.Count)
                    buffer = new float[values.Count * 2];
                values.CopyTo(buffer);
                record(i, -1, buffer);
                at = lineEnd + 1;
            }

            return at;
        }

        static ValueType ParseType(string name)
        {
            switch (name)
            {
                case "char":
                case "int8": return ValueType.Int8;
                case "uchar":
                case "uint8": return ValueType.UInt8;
                case "short":
                case "int16": return ValueType.Int16;
                case "ushort":
                case "uint16": return ValueType.UInt16;
                case "int":
                case "int32": return ValueType.Int32;
                case "uint":
                case "uint32": return ValueType.UInt32;
                case "float":
                case "float32": return ValueType.Float32;
                case "double":
                case "float64": return ValueType.Float64;
                default: throw new FormatException($"Unknown PLY type '{name}'.");
            }
        }

        static int IndexOf(byte[] data, string text)
        {
            var pattern = System.Text.Encoding.ASCII.GetBytes(text);
            var limit = Math.Min(data.Length - pattern.Length, 1 << 20); // headers are small
            for (var i = 0; i <= limit; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length && match; j++)
                    match = data[i + j] == pattern[j];
                if (match)
                    return i;
            }

            return -1;
        }
    }
}
