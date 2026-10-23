using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A 4D mesh container (<c>.sp4m</c>, usually gzipped as <c>.sp4m.gz</c>): a scene baked over time. It holds the
    /// scene's own triangles object by object, every object's pose 30 times a second, colour probes on every surface shaded
    /// twice a second (painted through a texture atlas, or blended per vertex for repeated fine meshes), the per-pixel rules
    /// the scene applies (a cutaway wedge, a door, a dissolving set, highlights), soft effects as Gaussians per frame,
    /// captions and callouts. The layout is the format the GenXR 4D pipeline writes: <c>'SP4M'</c>, a version, a JSON
    /// header, then 4-byte-aligned binary sections named in the header. Coordinates are three.js's: right-handed, y up,
    /// metres.
    /// <para>Version 3 adds, for scenes coloured per vertex: each object's own cutaway (a plane or a wedge in its frame)
    /// with a level per frame, colour keyframes at listed times (decoded on demand rather than all at once), a sphere
    /// dissolve, and how the stage follows its subject when it drives or flies away.</para>
    /// <para>Version 4 adds the hologram look (<c>look: "hologram"</c>): no colour probes; every object is drawn as light
    /// computed per pixel from its hue, gains, contour spacing and height span (<see cref="IsHologram"/>), with a glow
    /// (the highlight bytes) and how far it has materialised (the per-object level bytes) per frame, and a global fade
    /// (per-frame float 7). Its triangles come as one mesh.</para>
    /// </summary>
    public sealed class Mesh4DContainer
    {
        public const int FloatsPerFrame = 44; // globals 8, camera 8, effect parameters 25, subject 3
        public const int ProbeTextureWidth = 2048;

        public sealed class ObjectInfo
        {
            public string Name;
            public string Part;
            public string Kind;

            /// <summary>Bits: 1 cutaway, 2 door, 4 dissolve, 8 liquid, 16 part of the set.</summary>
            public int Flags;

            /// <summary>0 front faces, 1 back faces, 2 both.</summary>
            public int Side;

            /// <summary>Its triangles wind against its normals: the probes' front layer is on the other side.</summary>
            public bool Flip;

            public int ProbeStart;
            public int ProbeCount;

            /// <summary>Index into <see cref="Families"/> (repeated fine meshes, coloured per vertex), or -1 (atlas).</summary>
            public int Family = -1;

            /// <summary>Its range in the atlas mesh: first vertex, vertex count, first index, index count (or null).</summary>
            public int[] AtlasRange;

            /// <summary>The radius a door cuts beyond (0.3 m inside it); 0 when the content gives none, so a door cuts at any radius.</summary>
            public float HullRadius;
            public Vector2 DoorHeights;
            public Vector2 DoorAngles;
            public bool HasDoor;

            /// <summary>Its own cutaway (version 3, flag 64): 1 a plane (discard where dot(p, <see cref="CutA"/>) is beyond a
            /// distance that sweeps from <see cref="CutP0"/> to <see cref="CutP1"/>), 2 a wedge about axis <see cref="CutA"/>
            /// centred on <see cref="CutRef"/> opening to <see cref="CutP1"/> radians; 0 none.</summary>
            public int CutType;
            public Vector3 CutA, CutRef;
            public float CutP0, CutP1, CutGlowWidth = 0.02f;

            /// <summary>The hologram look (version 4): its hue (display colour), gains (body, scan band), contour spacing and
            /// height span in its own frame, and its range in the mesh (first vertex, vertex count, first index, index count).</summary>
            public Color Hue = Color.white;
            public Vector2 Gain = Vector2.one;
            public float LineSpacing = 0.03f;
            public Vector2 Span = new Vector2(0f, 1f);
            public int[] MeshRange;
        }

        public sealed class Family
        {
            public string Name;
            public int Vertices;
            public int Triangles;
            public int[] Members;
            public float[] Positions;
            public uint[] Indices;

            /// <summary>Per vertex: 3 probe indices (front, then back) relative to the object's first probe, and their weights.</summary>
            public ushort[] ProbesFront, ProbesBack;
            public byte[] WeightsFront, WeightsBack;
        }

        public sealed class Part
        {
            public string Name;
            public string Title;
            public string Description;
            public string Parent;

            /// <summary>The object its anchor rides on, and the anchor in that object's frame.</summary>
            public int AnchorObject = -1;
            public Vector3 AnchorLocal;
        }

        public sealed class Line
        {
            public double Start, End;
            public string Caption;
        }

        public sealed class Callout
        {
            public string Id, Title, Body, Stat, Part;
            public double In, Out;
        }

        public sealed class Chapter
        {
            public string Name;
            public double Start;
        }

        public int Version;
        public string Title;

        /// <summary>Version 4's hologram look: every object drawn as light computed per pixel (see <see cref="Holo"/>).</summary>
        public bool IsHologram;

        /// <summary>The hologram look's constants: base, rim, rim power, line; line width, sweep, sweep width, sweep speed;
        /// sweep start, sweep span, scanlines, flicker; glow, front, front width, front noise.</summary>
        /// <summary>The hologram's brightness in the room (light added over a lit room, not a dark void; 1 when not given).</summary>
        public float HoloGain = 1f;

        public Vector4[] Holo = { new Vector4(0.075f, 1.15f, 2.5f, 0.5f), new Vector4(0.0032f, 0.7f, 0.075f, 0.62f), new Vector4(-0.6f, 6.4f, 0.14f, 0.06f), new Vector4(0.85f, 1.7f, 0.05f, 0.16f) };
        public double Duration;
        public float Fps;
        public int Frames;
        public float ColorRate;
        public int ColorKeys;
        public int Probes;
        public int Triangles;
        public int AtlasWidth, AtlasHeight;
        public ObjectInfo[] Objects;
        public Family[] Families;
        public Part[] Parts;
        public Line[] Lines;
        public Callout[] Callouts;
        public Chapter[] Chapters;

        /// <summary>The content's own clock as samples (scene time, clock time, rate), when it has one.</summary>
        public double[] Clock;

        /// <summary>The stage: ground height, the subject's centre (x, z), its height at the start, and the side the viewer stands on.</summary>
        public float Ground;
        public Vector2 Centre;
        public float StageHeight;
        public Vector3 Front = new Vector3(0f, 0f, 1f);

        /// <summary>How tall the stage should stand in the room by default (metres; 0 when the content doesn't say), chosen by
        /// the content so the whole piece fits a table. Add Media sets the media's Height from it.</summary>
        public float RoomHeight;

        /// <summary>The widest the scene gets around its centre over the whole piece (x, z, content units; 0 when not given).</summary>
        public Vector2 Footprint;

        /// <summary>Version 3: the time of each colour keyframe (null: one every 1 / <see cref="ColorRate"/> seconds).</summary>
        public float[] ColorKeyTimes;

        /// <summary>Version 3: each object's own cutaway level per frame (F × O, 0–255).</summary>
        public byte[] CutLevels;

        /// <summary>The set dissolves inside a sphere (version 3) instead of a cylinder, with the scene's own noise.</summary>
        public bool DissolveSphere;
        public float DissolveCentreY, DissolveNoiseScale = 0.55f, DissolveNoiseAmplitude = 2.2f, DissolveGlowWidth = 0.45f;

        /// <summary>
        /// How the stage follows its subject (the per-frame subject floats) once it leaves (content units; 0: not given).
        /// The subject is held within <see cref="FollowDeadZone"/> of where it started, and the set dissolves beyond
        /// <see cref="FollowRadius"/> of it.
        /// </summary>
        public float FollowRadius, FollowDeadZone;

        /// <summary>A soundtrack next to the container, named in its header (or null).</summary>
        public string AudioFile;

        public Color32 Void = new Color32(5, 8, 13, 255);
        public Color CutGlow = new Color(1f, 0.62f, 0.3f);
        public Color DissolveGlow = new Color(0.75f, 0.88f, 1f);
        public Vector2 SigmaLogRange;

        // decoded data
        public float[] AtlasPositions;
        public Vector2[] AtlasUVs;
        public ushort[] AtlasObjects;
        public uint[] AtlasIndices;
        public uint[] TexelFront, TexelBack;

        /// <summary>Version 4: the hologram's triangles, object by object (positions, normals as signed 16-bit, object index).</summary>
        public float[] MeshPositions;
        public short[] MeshNormals;
        public ushort[] MeshObjects;
        public uint[] MeshIndices;

        /// <summary>F × O × 12: columns 0–2 then the translation, per object per frame.</summary>
        public float[] Matrices;
        public byte[] Visible;
        public byte[] Highlights;
        public float[] FrameFloats;

        /// <summary>K × N × 3 sRGB bytes: every probe's colour at every colour keyframe.</summary>
        public byte[] Colors;

        byte[] _fx;
        int _fxBase;
        uint[] _fxOffsets;

        public int ObjectCount => Objects.Length;
        public bool HasEffects => _fx != null;

        /// <summary>Read a container (gzipped or not).</summary>
        public static Mesh4DContainer Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new FormatException("The file is empty.");
            if (bytes[0] == 0x1f && bytes[1] == 0x8b)
                bytes = Gunzip(bytes);
            return Parse(bytes);
        }

        /// <summary>
        /// Read a container file from disk, unpacking a gzipped one straight into a buffer of its final size (the size its
        /// gzip trailer gives), so a large container needs about its own size in memory while loading, not several copies.
        /// </summary>
        public static Mesh4DContainer ReadFile(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                if (file.Length < 12)
                    throw new FormatException("The file is empty.");
                var probe = new byte[2];
                file.Read(probe, 0, 2);
                if (probe[0] != 0x1f || probe[1] != 0x8b)
                {
                    file.Position = 0;
                    return Parse(ReadAll(file, (int)file.Length));
                }

                var size = GzipSize(file);
                file.Position = 0;
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                    return Parse(ReadAll(gzip, size));
            }
        }

        static Mesh4DContainer Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new FormatException("The file is empty.");
            if (bytes[0] != (byte)'S' || bytes[1] != (byte)'P' || bytes[2] != (byte)'4' || bytes[3] != (byte)'M')
                throw new FormatException("It isn't a 4D mesh container (no 'SP4M' at the start).");
            var container = new Mesh4DContainer { Version = BitConverter.ToInt32(bytes, 4) };
            if (container.Version < 2 || container.Version > 4)
                throw new FormatException($"4D mesh container version {container.Version} isn't supported (versions 2 to 4 are).");
            var headerLength = BitConverter.ToInt32(bytes, 8);
            var header = Json.Parse(Encoding.UTF8.GetString(bytes, 12, headerLength)) as Dictionary<string, object>;
            if (header == null)
                throw new FormatException("The container's header isn't a JSON object.");
            var sectionsBase = 12 + headerLength;
            sectionsBase += (4 - sectionsBase % 4) % 4;
            container.ReadHeader(header);
            container.ReadSections(bytes, sectionsBase, (Dictionary<string, object>)header["sections"]);
            return container;
        }

        /// <summary>The room height a container file suggests (its <c>stage.roomHeight</c>), reading only its header; 0 when it
        /// doesn't say or can't be read.</summary>
        public static float SuggestedRoomHeight(string path)
        {
            try
            {
                using (var file = File.OpenRead(path))
                {
                    var probe = new byte[2];
                    var gz = file.Read(probe, 0, 2) == 2 && probe[0] == 0x1f && probe[1] == 0x8b;
                    file.Position = 0;
                    using (var stream = gz ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
                    {
                        var start = ReadExactly(stream, 12);
                        if (start == null || start[0] != (byte)'S' || start[1] != (byte)'P' || start[2] != (byte)'4' || start[3] != (byte)'M')
                            return 0f;
                        var json = ReadExactly(stream, BitConverter.ToInt32(start, 8));
                        if (!(Json.Parse(Encoding.UTF8.GetString(json)) is Dictionary<string, object> header))
                            return 0f;
                        return (float)Num(Obj(header, "stage"), "roomHeight");
                    }
                }
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        static byte[] ReadExactly(Stream stream, int count)
        {
            if (count <= 0 || count > 64 << 20)
                return null;
            var buffer = new byte[count];
            for (var read = 0; read < count;)
            {
                var n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                    return null;
                read += n;
            }

            return buffer;
        }

        /// <summary>True for the file names a 4D mesh container uses.</summary>
        public static bool IsContainerName(string name)
        {
            name = (name ?? string.Empty).ToLowerInvariant();
            return name.EndsWith(".sp4m", StringComparison.Ordinal) || name.EndsWith(".sp4m.gz", StringComparison.Ordinal);
        }

        static byte[] Gunzip(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            {
                var size = GzipSize(input);
                input.Position = 0;
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                    return ReadAll(gzip, size);
            }
        }

        /// <summary>The unpacked size a gzip stream's trailer gives (its last four bytes; containers stay under 2 GB).</summary>
        static int GzipSize(Stream stream)
        {
            var trailer = new byte[4];
            stream.Position = stream.Length - 4;
            if (stream.Read(trailer, 0, 4) != 4)
                throw new FormatException("The gzip stream is cut short.");
            var size = BitConverter.ToUInt32(trailer, 0);
            if (size < 12 || size > int.MaxValue)
                throw new FormatException("The gzip stream's size is out of range (containers must be under 2 GB unpacked).");
            return (int)size;
        }

        /// <summary>Exactly <paramref name="size"/> bytes from <paramref name="stream"/>, which must hold no more.</summary>
        static byte[] ReadAll(Stream stream, int size)
        {
            var buffer = new byte[size];
            for (var read = 0; read < size;)
            {
                var n = stream.Read(buffer, read, Math.Min(1 << 20, size - read));
                if (n <= 0)
                    throw new FormatException("The file is shorter than its gzip trailer says.");
                read += n;
            }

            if (stream.ReadByte() >= 0)
                throw new FormatException("The file is longer than its gzip trailer says.");
            return buffer;
        }

        // ---------------------------------------------------------------- header

        static double Num(Dictionary<string, object> d, string key, double fallback = 0d) =>
            d != null && d.TryGetValue(key, out var v) && v is double n ? n : fallback;

        static string Str(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as string : null;

        static List<object> List(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as List<object> : null;

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        static float At(List<object> list, int i, float fallback = 0f) => list != null && i < list.Count && list[i] is double n ? (float)n : fallback;

        void ReadHeader(Dictionary<string, object> h)
        {
            Title = Str(h, "title");
            Duration = Num(h, "duration");
            Fps = (float)Num(h, "fps", 30d);
            Frames = (int)Num(h, "frames");
            ColorRate = (float)Num(h, "colorRate", 2d);
            ColorKeys = (int)Num(h, "colorKeys");
            Probes = (int)Num(h, "probes");
            Triangles = (int)Num(h, "triangles");
            AudioFile = Str(h, "audio");
            IsHologram = Version >= 4 && Str(h, "look") == "hologram";
            var atlas = Obj(h, "atlas");
            AtlasWidth = (int)Num(atlas, "width");
            AtlasHeight = (int)Num(atlas, "height");
            if (IsHologram)
                ReadHolo(Obj(h, "holo"));
            else if (Frames <= 0 || ColorKeys <= 0 || Probes <= 0 || AtlasWidth <= 0 || AtlasHeight <= 0)
                throw new FormatException("The container's header is missing its frames, colours or atlas.");
            if (Frames <= 0)
                throw new FormatException("The container's header has no frames.");

            var objects = List(h, "objects") ?? throw new FormatException("The container has no objects.");
            Objects = new ObjectInfo[objects.Count];
            for (var i = 0; i < objects.Count; i++)
            {
                var o = (Dictionary<string, object>)objects[i];
                var info = new ObjectInfo
                {
                    Name = Str(o, "name"),
                    Part = Str(o, "part") ?? string.Empty,
                    Kind = Str(o, "kind"),
                    Flags = (int)Num(o, "flags"),
                    Side = (int)Num(o, "side", 2d),
                    Flip = Num(o, "flip") > 0.5,
                    ProbeStart = (int)Num(o, "probeStart"),
                    ProbeCount = (int)Num(o, "probeCount"),
                    Family = (int)Num(o, "family", -1d),
                    HullRadius = (float)Num(o, "hullR"),
                };
                var range = List(o, "atlas");
                if (range != null && range.Count == 4)
                    info.AtlasRange = new[] { (int)At(range, 0), (int)At(range, 1), (int)At(range, 2), (int)At(range, 3) };
                var door = Obj(o, "door");
                if (door != null)
                {
                    var y = List(door, "y");
                    var a = List(door, "a");
                    info.HasDoor = true;
                    info.DoorHeights = new Vector2(At(y, 0), At(y, 1));
                    info.DoorAngles = new Vector2(At(a, 0), At(a, 1));
                }

                var hue = List(o, "hue");
                if (hue != null)
                    info.Hue = new Color(At(hue, 0), At(hue, 1), At(hue, 2));
                var gain = List(o, "gain");
                if (gain != null)
                    info.Gain = new Vector2(At(gain, 0, 1f), At(gain, 1, 1f));
                info.LineSpacing = (float)Num(o, "lines", info.LineSpacing);
                var span = List(o, "span");
                if (span != null)
                    info.Span = new Vector2(At(span, 0), At(span, 1, 1f));
                var meshRange = List(o, "mesh");
                if (meshRange != null && meshRange.Count == 4)
                    info.MeshRange = new[] { (int)At(meshRange, 0), (int)At(meshRange, 1), (int)At(meshRange, 2), (int)At(meshRange, 3) };

                var cut = Obj(o, "cut");
                if (cut != null)
                {
                    var plane = Str(cut, "type") == "plane";
                    var axis = List(cut, plane ? "n" : "axis");
                    var reference = List(cut, "ref");
                    info.CutType = plane ? 1 : 2;
                    info.CutA = new Vector3(At(axis, 0), At(axis, 1), At(axis, 2));
                    info.CutRef = new Vector3(At(reference, 0), At(reference, 1), At(reference, 2));
                    info.CutP0 = (float)Num(cut, "dOut");
                    info.CutP1 = (float)Num(cut, plane ? "dIn" : "aMax");
                    info.CutGlowWidth = (float)Num(cut, "glow", 0.02);
                }

                Objects[i] = info;
            }

            var families = List(h, "families") ?? new List<object>();
            Families = new Family[families.Count];
            for (var i = 0; i < families.Count; i++)
            {
                var f = (Dictionary<string, object>)families[i];
                var members = List(f, "members") ?? new List<object>();
                Families[i] = new Family
                {
                    Name = Str(f, "name"),
                    Vertices = (int)Num(f, "vertices"),
                    Triangles = (int)Num(f, "triangles"),
                    Members = members.ConvertAll(m => (int)(double)m).ToArray(),
                };
            }

            var parts = List(h, "parts") ?? new List<object>();
            Parts = new Part[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                var p = (Dictionary<string, object>)parts[i];
                var anchor = List(p, "anchorLocal");
                Parts[i] = new Part
                {
                    Name = Str(p, "name"),
                    Title = Str(p, "title"),
                    Description = Str(p, "desc"),
                    Parent = Str(p, "parent"),
                    AnchorObject = (int)Num(p, "anchorObject", -1d),
                    AnchorLocal = new Vector3(At(anchor, 0), At(anchor, 1), At(anchor, 2)),
                };
            }

            var stage = Obj(h, "stage");
            Ground = (float)Num(stage, "ground");
            var centre = List(stage, "centre");
            Centre = new Vector2(At(centre, 0), At(centre, 1));
            StageHeight = (float)Num(stage, "height", 1d);
            RoomHeight = (float)Num(stage, "roomHeight");
            var footprint = List(stage, "footprint");
            if (footprint != null)
                Footprint = new Vector2(At(footprint, 0), At(footprint, 1));
            var front = List(stage, "front");
            if (front != null)
                Front = new Vector3(At(front, 0), At(front, 1), At(front, 2, 1f));
            var voidColor = List(h, "void");
            if (voidColor != null)
                Void = new Color32((byte)At(voidColor, 0), (byte)At(voidColor, 1), (byte)At(voidColor, 2), 255);

            var timeline = Obj(h, "timeline");
            var lines = List(timeline, "lines") ?? new List<object>();
            Lines = lines.ConvertAll(x =>
            {
                var l = (Dictionary<string, object>)x;
                return new Line { Start = Num(l, "start"), End = Num(l, "end"), Caption = Str(l, "caption") };
            }).ToArray();
            var callouts = List(timeline, "callouts") ?? new List<object>();
            Callouts = callouts.ConvertAll(x =>
            {
                var c = (Dictionary<string, object>)x;
                return new Callout { Id = Str(c, "id"), Title = Str(c, "title"), Body = Str(c, "body"), Stat = Str(c, "stat"), Part = Str(c, "part"), In = Num(c, "in"), Out = Num(c, "out") };
            }).ToArray();
            var chapters = List(timeline, "chapters") ?? new List<object>();
            Chapters = chapters.ConvertAll(x =>
            {
                var c = (Dictionary<string, object>)x;
                return new Chapter { Name = Str(c, "name"), Start = Num(c, "start") };
            }).ToArray();
            var clock = List(timeline, "clock");
            if (clock != null)
            {
                Clock = new double[clock.Count * 3];
                for (var i = 0; i < clock.Count; i++)
                {
                    var row = (List<object>)clock[i];
                    for (var k = 0; k < 3; k++)
                        Clock[i * 3 + k] = k < row.Count && row[k] is double v ? v : 0d;
                }
            }

            var fx = Obj(h, "fxFrames");
            var sigma = List(fx, "sigmaLogRange");
            if (sigma != null)
                SigmaLogRange = new Vector2(At(sigma, 0), At(sigma, 1));
            var rules = Obj(h, "rules");
            var dissolve = List(rules, "dissolveGlow");
            if (dissolve != null)
                DissolveGlow = Color.Lerp(new Color(At(dissolve, 0), At(dissolve, 1), At(dissolve, 2)), Color.white, 0.3f);
            if (Version < 3)
                return;

            var cutGlow = List(rules, "cutGlow");
            if (cutGlow != null)
                CutGlow = new Color(At(cutGlow, 0), At(cutGlow, 1), At(cutGlow, 2));
            var sphere = Obj(rules, "dissolve");
            if (sphere != null && Str(sphere, "shape") == "sphere")
            {
                DissolveSphere = true;
                DissolveCentreY = (float)Num(sphere, "centreY");
                DissolveNoiseScale = (float)Num(sphere, "noiseScale", DissolveNoiseScale);
                DissolveNoiseAmplitude = (float)Num(sphere, "noiseAmplitude", DissolveNoiseAmplitude);
                DissolveGlowWidth = (float)Num(sphere, "glowWidth", DissolveGlowWidth);
            }

            var times = List(h, "colorKeyTimes");
            if (times != null && times.Count == ColorKeys)
                ColorKeyTimes = times.ConvertAll(x => (float)(double)x).ToArray();
            var follow = Obj(h, "follow");
            FollowRadius = (float)Num(follow, "radius");
            FollowDeadZone = (float)Num(follow, "deadZone");
        }

        void ReadHolo(Dictionary<string, object> holo)
        {
            if (holo == null)
                return;
            float N(string key, float fallback) => (float)Num(holo, key, fallback);
            Holo[0] = new Vector4(N("base", Holo[0].x), N("rim", Holo[0].y), N("rimPow", Holo[0].z), N("line", Holo[0].w));
            Holo[1] = new Vector4(N("lineW", Holo[1].x), N("sweep", Holo[1].y), N("sweepW", Holo[1].z), N("sweepSpeed", Holo[1].w));
            Holo[2] = new Vector4(N("sweepLo", Holo[2].x), N("sweepSpan", Holo[2].y), N("scan", Holo[2].z), N("flicker", Holo[2].w));
            Holo[3] = new Vector4(N("hi", Holo[3].x), N("edge", Holo[3].y), N("edgeW", Holo[3].z), N("revealNoise", Holo[3].w));
            HoloGain = N("roomGain", 1f);
        }

        // ---------------------------------------------------------------- sections

        void ReadSections(byte[] bytes, int baseOffset, Dictionary<string, object> sections)
        {
            (int offset, int length) Find(string name, bool required = true)
            {
                if (sections.TryGetValue(name, out var v) && v is List<object> pair && pair.Count == 2)
                    return (baseOffset + (int)(double)pair[0], (int)(double)pair[1]);
                if (required)
                    throw new FormatException($"The container has no '{name}' section.");
                return (-1, 0);
            }

            if (IsHologram)
            {
                var (mp, mpl) = Find("mesh.pos");
                MeshPositions = Floats(bytes, mp, mpl / 4);
                var (mn, mnl) = Find("mesh.nor");
                MeshNormals = new short[mnl / 2];
                Buffer.BlockCopy(bytes, mn, MeshNormals, 0, mnl);
                var (mo, mol) = Find("mesh.obj");
                MeshObjects = UShorts(bytes, mo, mol / 2);
                var (mi, mil) = Find("mesh.idx");
                MeshIndices = UInts(bytes, mi, mil / 4);
                if (MeshNormals.Length != MeshPositions.Length || MeshObjects.Length * 3 != MeshPositions.Length)
                    throw new FormatException("The hologram mesh's positions, normals and object indices don't match.");
                var (hp, hpl) = Find("poses");
                DecodePoses(bytes, hp, hpl);
                ReadEffects(Find("fx", required: false));
                return;
            }

            var (posAt, posLen) = Find("atlas.pos");
            AtlasPositions = Floats(bytes, posAt, posLen / 4);
            var (uvAt, uvLen) = Find("atlas.uv");
            var uvs = UShorts(bytes, uvAt, uvLen / 2);
            AtlasUVs = new Vector2[uvs.Length / 2];
            for (var i = 0; i < AtlasUVs.Length; i++)
                AtlasUVs[i] = new Vector2(uvs[i * 2] / 65535f, uvs[i * 2 + 1] / 65535f);
            var (objAt, objLen) = Find("atlas.obj");
            AtlasObjects = UShorts(bytes, objAt, objLen / 2);
            var (idxAt, idxLen) = Find("atlas.idx");
            AtlasIndices = UInts(bytes, idxAt, idxLen / 4);
            var (frontAt, frontLen) = Find("tex.front");
            TexelFront = UInts(bytes, frontAt, frontLen / 4);
            var (backAt, backLen) = Find("tex.back");
            TexelBack = UInts(bytes, backAt, backLen / 4);
            if (TexelFront.Length != AtlasWidth * AtlasHeight || TexelBack.Length != TexelFront.Length)
                throw new FormatException("The container's texel maps don't match its atlas size.");

            for (var i = 0; i < Families.Length; i++)
            {
                var family = Families[i];
                var (fp, fpl) = Find($"fam.{i}.pos");
                family.Positions = Floats(bytes, fp, fpl / 4);
                var (fi, fil) = Find($"fam.{i}.idx");
                family.Indices = UInts(bytes, fi, fil / 4);
                var (pr, prl) = Find($"fam.{i}.probes");
                var v = family.Vertices;
                if (prl < v * 18)
                    throw new FormatException($"Family '{family.Name}' has too little probe data.");
                family.ProbesFront = UShorts(bytes, pr, v * 3);
                family.ProbesBack = UShorts(bytes, pr + v * 6, v * 3);
                family.WeightsFront = new byte[v * 3];
                family.WeightsBack = new byte[v * 3];
                Buffer.BlockCopy(bytes, pr + v * 12, family.WeightsFront, 0, v * 3);
                Buffer.BlockCopy(bytes, pr + v * 15, family.WeightsBack, 0, v * 3);
            }

            var (colAt, colLen) = Find("colors");
            if (Version >= 3)
                IndexColors(bytes, colAt, colLen);
            else
                Colors = DecodeColors(bytes, colAt, colLen, Probes, ColorKeys);
            var (poseAt, poseLen) = Find("poses");
            DecodePoses(bytes, poseAt, poseLen);
            ReadEffects(Find("fx", required: false));

            void ReadEffects((int offset, int length) fx)
            {
                if (fx.offset < 0 || fx.length <= 0)
                    return;
                _fx = bytes;
                _fxOffsets = UInts(bytes, fx.offset, Frames + 1);
                _fxBase = fx.offset + (Frames + 1) * 4;
            }
        }

        static float[] Floats(byte[] b, int offset, int count)
        {
            var a = new float[count];
            Buffer.BlockCopy(b, offset, a, 0, count * 4);
            return a;
        }

        static ushort[] UShorts(byte[] b, int offset, int count)
        {
            var a = new ushort[count];
            Buffer.BlockCopy(b, offset, a, 0, count * 2);
            return a;
        }

        static uint[] UInts(byte[] b, int offset, int count)
        {
            var a = new uint[count];
            Buffer.BlockCopy(b, offset, a, 0, count * 4);
            return a;
        }

        static int Varint(byte[] b, ref int pos)
        {
            int value = 0, shift = 0;
            byte x;
            do
            {
                x = b[pos++];
                value |= (x & 0x7f) << shift;
                shift += 7;
            } while ((x & 0x80) != 0);
            return value;
        }

        /// <summary>Keyframe 0 in full, then only the probes that changed (varint gaps + RGB).</summary>
        public static byte[] DecodeColors(byte[] b, int offset, int length, int probes, int keys)
        {
            var stride = probes * 3;
            var colors = new byte[(long)keys * stride];
            Buffer.BlockCopy(b, offset, colors, 0, stride);
            var pos = offset + stride;
            for (var k = 1; k < keys; k++)
            {
                Buffer.BlockCopy(colors, (k - 1) * stride, colors, k * stride, stride);
                var n = Varint(b, ref pos);
                var index = -1;
                for (var j = 0; j < n; j++)
                {
                    index += Varint(b, ref pos) + 1;
                    var o = k * stride + index * 3;
                    colors[o] = b[pos];
                    colors[o + 1] = b[pos + 1];
                    colors[o + 2] = b[pos + 2];
                    pos += 3;
                }
            }

            if (pos > offset + length)
                throw new FormatException("The colour section is shorter than its keyframes.");
            return colors;
        }

        // ---------------------------------------------------------------- colour keyframes on demand (version 3)

        const int SnapshotEvery = 64;
        byte[] _colorBytes;
        int[] _keyOffsets;
        byte[][] _snapshots;
        byte[] _cursor;
        int _cursorKey = -1;

        /// <summary>Version 3 keeps the colour stream as it is: one pass finds every keyframe's changes, and a full copy is
        /// kept every 64 keyframes, so any keyframe is a copy plus at most 63 sets of changes.</summary>
        void IndexColors(byte[] b, int offset, int length)
        {
            var stride = Probes * 3;
            _colorBytes = b;
            _keyOffsets = new int[ColorKeys];
            _snapshots = new byte[(ColorKeys + SnapshotEvery - 1) / SnapshotEvery][];
            var running = new byte[stride];
            Buffer.BlockCopy(b, offset, running, 0, stride);
            _snapshots[0] = (byte[])running.Clone();
            var pos = offset + stride;
            for (var k = 1; k < ColorKeys; k++)
            {
                _keyOffsets[k] = pos;
                pos = ApplyChanges(b, pos, running);
                if (k % SnapshotEvery == 0)
                    _snapshots[k / SnapshotEvery] = (byte[])running.Clone();
            }

            if (pos > offset + length)
                throw new FormatException("The colour section is shorter than its keyframes.");
            _cursor = running;
            _cursorKey = ColorKeys - 1;
        }

        static int ApplyChanges(byte[] b, int pos, byte[] rgb)
        {
            var n = Varint(b, ref pos);
            var index = -1;
            for (var j = 0; j < n; j++)
            {
                index += Varint(b, ref pos) + 1;
                var o = index * 3;
                rgb[o] = b[pos];
                rgb[o + 1] = b[pos + 1];
                rgb[o + 2] = b[pos + 2];
                pos += 3;
            }

            return pos;
        }

        /// <summary>Colour keyframe <paramref name="key"/> as RGB bytes, 3 per probe (valid until the next call).</summary>
        public byte[] ColorKey(int key)
        {
            if (Colors != null)
            {
                var copy = _cursor ?? (_cursor = new byte[Probes * 3]);
                Buffer.BlockCopy(Colors, key * Probes * 3, copy, 0, Probes * 3);
                return copy;
            }

            if (key == _cursorKey)
                return _cursor;
            if (key < _cursorKey || key - _cursorKey >= SnapshotEvery)
            {
                var s = key / SnapshotEvery;
                Buffer.BlockCopy(_snapshots[s], 0, _cursor, 0, _cursor.Length);
                _cursorKey = s * SnapshotEvery;
            }

            for (var k = _cursorKey + 1; k <= key; k++)
                ApplyChanges(_colorBytes, _keyOffsets[k], _cursor);
            _cursorKey = key;
            return _cursor;
        }

        /// <summary>The colour keyframes around scene time <paramref name="t"/> and how far between them it is.</summary>
        public void KeyAt(double t, out int k0, out int k1, out float mix)
        {
            if (ColorKeyTimes == null)
            {
                var kx = Mathf.Clamp((float)(t * ColorRate), 0f, ColorKeys - 1);
                k0 = (int)kx;
                k1 = Mathf.Min(ColorKeys - 1, k0 + 1);
                mix = kx - k0;
                return;
            }

            var times = ColorKeyTimes;
            if (t <= times[0]) { k0 = k1 = 0; mix = 0f; return; }
            if (t >= times[times.Length - 1]) { k0 = k1 = times.Length - 1; mix = 0f; return; }
            int lo = 0, hi = times.Length - 1;
            while (hi - lo > 1)
            {
                var m = (lo + hi) >> 1;
                if (times[m] <= t) lo = m; else hi = m;
            }

            k0 = lo;
            k1 = hi;
            mix = (float)((t - times[lo]) / Math.Max(1e-6, times[hi] - times[lo]));
        }

        void DecodePoses(byte[] b, int offset, int length)
        {
            var objects = Objects.Length;
            Matrices = new float[(long)Frames * objects * 12];
            Visible = new byte[Frames * objects];
            Highlights = new byte[Frames * objects];
            if (Version >= 3)
                CutLevels = new byte[Frames * objects];
            FrameFloats = new float[Frames * FloatsPerFrame];
            var visBytes = (objects + 7) / 8;
            var pos = offset;
            for (var f = 0; f < Frames; f++)
            {
                if (f > 0)
                {
                    Buffer.BlockCopy(Matrices, (f - 1) * objects * 12 * 4, Matrices, f * objects * 12 * 4, objects * 12 * 4);
                    Buffer.BlockCopy(Highlights, (f - 1) * objects, Highlights, f * objects, objects);
                }

                var moved = Varint(b, ref pos);
                var o = -1;
                for (var j = 0; j < moved; j++)
                {
                    o += Varint(b, ref pos) + 1;
                    Buffer.BlockCopy(b, pos, Matrices, (f * objects + o) * 12 * 4, 48);
                    pos += 48;
                }

                for (var q = 0; q < objects; q++)
                    Visible[f * objects + q] = (byte)((b[pos + (q >> 3)] >> (q & 7)) & 1);
                pos += visBytes;
                var changed = Varint(b, ref pos);
                o = -1;
                for (var j = 0; j < changed; j++)
                {
                    o += Varint(b, ref pos) + 1;
                    Highlights[f * objects + o] = b[pos++];
                }

                Buffer.BlockCopy(b, pos, FrameFloats, f * FloatsPerFrame * 4, FloatsPerFrame * 4);
                pos += FloatsPerFrame * 4;
                if (CutLevels == null)
                    continue;
                if (f > 0)
                    Buffer.BlockCopy(CutLevels, (f - 1) * objects, CutLevels, f * objects, objects);
                var cuts = Varint(b, ref pos);
                o = -1;
                for (var j = 0; j < cuts; j++)
                {
                    o += Varint(b, ref pos) + 1;
                    CutLevels[f * objects + o] = b[pos++];
                }
            }

            if (pos > offset + length)
                throw new FormatException("The pose section is shorter than its frames.");
        }

        // ---------------------------------------------------------------- effects

        /// <summary>
        /// Frame <paramref name="frame"/>'s effect Gaussians: their count, the box their positions are quantised in, and
        /// the raw records (11 bytes each: x, y, z as u16 in the box, log-sigma, r, g, b, alpha), or 0 when there are none.
        /// </summary>
        public int EffectFrame(int frame, out Vector3 min, out Vector3 max, out int recordsOffset, out byte[] data)
        {
            min = max = default;
            recordsOffset = 0;
            data = _fx;
            if (_fx == null || frame < 0 || frame >= Frames)
                return 0;
            var at = _fxBase + (int)_fxOffsets[frame];
            var count = BitConverter.ToInt32(_fx, at);
            if (count <= 0)
                return 0;
            min = new Vector3(BitConverter.ToSingle(_fx, at + 4), BitConverter.ToSingle(_fx, at + 8), BitConverter.ToSingle(_fx, at + 12));
            max = new Vector3(BitConverter.ToSingle(_fx, at + 16), BitConverter.ToSingle(_fx, at + 20), BitConverter.ToSingle(_fx, at + 24));
            recordsOffset = at + 28;
            return count;
        }

        /// <summary>The most effect Gaussians in any frame.</summary>
        public int MaxEffectGaussians()
        {
            var most = 0;
            if (_fx == null)
                return 0;
            for (var f = 0; f < Frames; f++)
                most = Math.Max(most, BitConverter.ToInt32(_fx, _fxBase + (int)_fxOffsets[f]));
            return most;
        }

        // ---------------------------------------------------------------- the content's own clock

        /// <summary>The content's own clock (such as a countdown to an event) at scene time <paramref name="t"/>, or null.</summary>
        public double? ClockAt(double t)
        {
            if (Clock == null || Clock.Length < 6 || t < Clock[0])
                return null;
            var step = Clock[3] - Clock[0];
            var count = Clock.Length / 3;
            var i = Math.Min(count - 2, Math.Max(0, (int)((t - Clock[0]) / step)));
            var f = Math.Min(1d, Math.Max(0d, (t - Clock[i * 3]) / step));
            return Clock[i * 3 + 1] + (Clock[(i + 1) * 3 + 1] - Clock[i * 3 + 1]) * f;
        }
    }
}
