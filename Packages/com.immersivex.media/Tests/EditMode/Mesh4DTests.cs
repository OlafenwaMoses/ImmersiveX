using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ImmersiveX.Media.Editor;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    /// <summary>
    /// The 4D mesh container (<c>.sp4m</c>), read from a small container written here byte by byte.
    /// </summary>
    public class Mesh4DTests
    {
        // ---------------------------------------------------------------- a container written by hand

        sealed class Sections
        {
            readonly MemoryStream _data = new MemoryStream();
            public readonly List<string> Entries = new List<string>();

            public void Add(string name, byte[] bytes)
            {
                while (_data.Length % 4 != 0)
                    _data.WriteByte(0);
                Entries.Add($"\"{name}\":[{_data.Length},{bytes.Length}]");
                _data.Write(bytes, 0, bytes.Length);
            }

            public byte[] Bytes => _data.ToArray();
        }

        static byte[] Of(params float[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return b;
        }

        static byte[] Of(params ushort[] v)
        {
            var b = new byte[v.Length * 2];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return b;
        }

        static byte[] Of(params uint[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return b;
        }

        /// <summary>One triangle on a 2×2 atlas, 2 probes with 3 colour keys, 1 object over 2 frames.</summary>
        static byte[] SmallContainer(int version = 2)
        {
            var s = new Sections();
            s.Add("atlas.pos", Of(0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f));
            s.Add("atlas.uv", Of((ushort)0, (ushort)0, (ushort)65535, (ushort)0, (ushort)0, (ushort)65535));
            s.Add("atlas.obj", Of((ushort)0, (ushort)0, (ushort)0));
            s.Add("atlas.idx", Of(0u, 1u, 2u));
            s.Add("tex.front", Of(0u, 1u, 0xffffffffu, 1u));
            s.Add("tex.back", Of(1u, 1u, 0xffffffffu, 0u));

            // colours: key 0 in full; key 1 changes probe 1; key 2 changes nothing
            var colors = new List<byte> { 10, 20, 30, 40, 50, 60 };
            colors.AddRange(new byte[] { 1, 1, 200, 201, 202 }); // one change, gap 1 (probe 1)
            colors.Add(0);
            s.Add("colors", colors.ToArray());

            // poses: frame 0 moves object 0 (a translation); frame 1 moves nothing and raises its highlight
            var poses = new List<byte>();
            poses.AddRange(new byte[] { 1, 0 });
            poses.AddRange(Of(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 5f, 6f, 7f));
            poses.Add(1); // visible
            poses.AddRange(new byte[] { 1, 0, 0 }); // highlight 0
            var floats = new float[Mesh4DContainer.FloatsPerFrame];
            floats[0] = 0.25f;
            poses.AddRange(Of(floats));
            if (version >= 3)
                poses.AddRange(new byte[] { 1, 0, 64 }); // its own cutaway at level 64
            poses.Add(0); // frame 1: nothing moved
            poses.Add(0); // hidden
            poses.AddRange(new byte[] { 1, 0, 255 }); // highlight 255
            floats[0] = 0.5f;
            poses.AddRange(Of(floats));
            if (version >= 3)
                poses.Add(0); // the cut level carries forward
            s.Add("poses", poses.ToArray());
            var v3 = version >= 3
                ? ",\"colorKeyTimes\":[0,0.5,2],\"follow\":{\"radius\":9,\"deadZone\":0.5}," +
                  "\"rules\":{\"dissolve\":{\"shape\":\"sphere\",\"centreY\":0.8,\"noiseScale\":0.5,\"noiseAmplitude\":2,\"glowWidth\":0.4}}"
                : string.Empty;
            var cut = version >= 3 ? ",\"cut\":{\"type\":\"wedge\",\"axis\":[0,0,1],\"ref\":[0,1,0],\"aMax\":1.05,\"glow\":0.02}" : string.Empty;

            var header = "{\"title\":\"Test\",\"duration\":0.0333,\"fps\":30,\"frames\":2,\"colorRate\":2,\"colorKeys\":3,\"probes\":2," +
                "\"triangles\":1,\"audio\":\"sound.m4a\",\"atlas\":{\"width\":2,\"height\":2}," +
                "\"objects\":[{\"name\":\"Shell\",\"part\":\"body\",\"kind\":\"hull\",\"flags\":1,\"side\":2,\"flip\":0,\"probeStart\":0,\"probeCount\":2," +
                "\"atlas\":[0,3,0,3],\"door\":{\"y\":[1,2],\"a\":[-1.57,0.32]}" + cut + "}]" + v3 + "," +
                "\"parts\":[{\"name\":\"body\",\"title\":\"Body\",\"anchorObject\":0,\"anchorLocal\":[0,1,2]}]," +
                "\"stage\":{\"ground\":-0.5,\"centre\":[1,2],\"height\":2.5,\"front\":[0,0,1]}," +
                "\"timeline\":{\"lines\":[{\"start\":0,\"end\":1,\"caption\":\"Hello\"}],\"chapters\":[{\"name\":\"Start\",\"start\":0}]," +
                "\"clock\":[[10,-5,1],[11,-4,1]]}," +
                "\"sections\":{" + string.Join(",", s.Entries) + "}}";
            var json = Encoding.UTF8.GetBytes(header);
            var file = new MemoryStream();
            file.Write(Encoding.ASCII.GetBytes("SP4M"), 0, 4);
            file.Write(BitConverter.GetBytes(version), 0, 4);
            file.Write(BitConverter.GetBytes(json.Length), 0, 4);
            file.Write(json, 0, json.Length);
            while (file.Length % 4 != 0)
                file.WriteByte(0);
            var data = s.Bytes;
            file.Write(data, 0, data.Length);
            return file.ToArray();
        }

        /// <summary>A version 4 hologram: one triangle, one object over 2 frames, materialising and fading.</summary>
        static byte[] HologramContainer()
        {
            var s = new Sections();
            s.Add("mesh.pos", Of(0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f));
            var normals = new short[] { 0, 0, 32767, 0, 0, 32767, 0, 0, -32767 };
            var nb = new byte[normals.Length * 2];
            Buffer.BlockCopy(normals, 0, nb, 0, nb.Length);
            s.Add("mesh.nor", nb);
            s.Add("mesh.obj", Of((ushort)0, (ushort)0, (ushort)0));
            s.Add("mesh.idx", Of(0u, 1u, 2u));
            var poses = new List<byte>();
            poses.AddRange(new byte[] { 1, 0 }); // frame 0 moves object 0
            poses.AddRange(Of(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 5f, 6f, 7f));
            poses.Add(1); // visible
            poses.AddRange(new byte[] { 1, 0, 128 }); // glow
            var floats = new float[Mesh4DContainer.FloatsPerFrame];
            floats[7] = 0.8f; // the fade
            poses.AddRange(Of(floats));
            poses.AddRange(new byte[] { 1, 0, 200 }); // materialised 200/255
            poses.Add(0); // frame 1: nothing moved
            poses.Add(1);
            poses.Add(0); // the glow carries forward
            floats[7] = 1f;
            poses.AddRange(Of(floats));
            poses.Add(0); // so does the level
            s.Add("poses", poses.ToArray());
            var header = "{\"format\":\"SP4M\",\"version\":4,\"look\":\"hologram\",\"title\":\"Holo\",\"duration\":0.0333,\"fps\":30,\"frames\":2,\"triangles\":1," +
                "\"holo\":{\"base\":0.1,\"rim\":1.2,\"rimPow\":2.5,\"line\":0.5,\"lineW\":0.003,\"sweep\":0.7,\"sweepW\":0.08,\"sweepSpeed\":0.6,\"sweepLo\":-0.6,\"sweepSpan\":6.4,\"scan\":0.14,\"flicker\":0.06,\"hi\":0.85,\"edge\":1.7,\"edgeW\":0.05,\"revealNoise\":0.16}," +
                "\"objects\":[{\"name\":\"Figure/Body\",\"part\":\"\",\"kind\":\"subject\",\"flags\":0,\"side\":2,\"hue\":[1,0.5,0.25],\"gain\":[0.5,0.2],\"lines\":0.05,\"span\":[-0.1,1.8],\"mesh\":[0,3,0,3]}]," +
                "\"families\":[],\"parts\":[],\"stage\":{\"ground\":0,\"centre\":[0,0],\"height\":3,\"roomHeight\":1.8,\"front\":[0,0,1]}," +
                "\"timeline\":{\"lines\":[],\"chapters\":[{\"name\":\"Night city\",\"start\":0}],\"callouts\":[]}," +
                "\"sections\":{" + string.Join(",", s.Entries) + "}}";
            var json = Encoding.UTF8.GetBytes(header);
            var file = new MemoryStream();
            file.Write(Encoding.ASCII.GetBytes("SP4M"), 0, 4);
            file.Write(BitConverter.GetBytes(4), 0, 4);
            file.Write(BitConverter.GetBytes(json.Length), 0, 4);
            file.Write(json, 0, json.Length);
            while (file.Length % 4 != 0)
                file.WriteByte(0);
            var data = s.Bytes;
            file.Write(data, 0, data.Length);
            return file.ToArray();
        }

        static byte[] Gzip(byte[] bytes)
        {
            var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                gzip.Write(bytes, 0, bytes.Length);
            return output.ToArray();
        }

        [Test]
        public void Container_ReadsHeader()
        {
            var c = Mesh4DContainer.Read(SmallContainer());

            Assert.AreEqual("Test", c.Title);
            Assert.AreEqual(2, c.Frames);
            Assert.AreEqual(3, c.ColorKeys);
            Assert.AreEqual(1, c.ObjectCount);
            Assert.AreEqual("sound.m4a", c.AudioFile);
            Assert.AreEqual(1, c.Objects[0].Flags);
            Assert.IsTrue(c.Objects[0].HasDoor);
            Assert.AreEqual(new Vector2(1f, 2f), c.Objects[0].DoorHeights);
            Assert.AreEqual(0, c.Parts[0].AnchorObject);
            Assert.AreEqual(2.5f, c.StageHeight, 1e-4f);
            Assert.AreEqual("Hello", c.Lines[0].Caption);
            Assert.AreEqual(-4.5d, c.ClockAt(10.5).Value, 1e-9);
            Assert.IsNull(c.ClockAt(9.0));
            Assert.IsFalse(c.HasEffects);
        }

        [Test]
        public void Container_DecodesColourKeysAsChanges()
        {
            var c = Mesh4DContainer.Read(SmallContainer());

            // key 0 as written; key 1 changes probe 1; key 2 carries key 1 forward
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40, 50, 60 }, Slice(c.Colors, 0, 6));
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 200, 201, 202 }, Slice(c.Colors, 6, 6));
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 200, 201, 202 }, Slice(c.Colors, 12, 6));
        }

        [Test]
        public void Container_DecodesPosesAsChanges()
        {
            var c = Mesh4DContainer.Read(SmallContainer());

            Assert.AreEqual(5f, c.Matrices[9]);
            Assert.AreEqual(5f, c.Matrices[12 + 9], "a frame that moves nothing keeps the last pose");
            Assert.AreEqual(1, c.Visible[0]);
            Assert.AreEqual(0, c.Visible[1]);
            Assert.AreEqual(0, c.Highlights[0]);
            Assert.AreEqual(255, c.Highlights[1]);
            Assert.AreEqual(0.25f, c.FrameFloats[0]);
            Assert.AreEqual(0.5f, c.FrameFloats[Mesh4DContainer.FloatsPerFrame]);
            Assert.AreEqual(4, c.TexelFront.Length);
            Assert.AreEqual(0xffffffffu, c.TexelFront[2]);
        }

        [Test]
        public void Parts_EachPartFollowsItsOwnObject()
        {
            var kinds = new Dictionary<int, string> { [3] = "numeral", [4] = "face", [5] = "hull", [7] = "bell", [8] = "numeral" };

            Assert.AreEqual(3, Mesh4DParts.FrameOf(new List<int> { 3, 4 }, o => kinds[o]), "no hull: its first object, not object 0");
            Assert.AreEqual(5, Mesh4DParts.FrameOf(new List<int> { 3, 4, 5 }, o => kinds[o]), "its hull");
            Assert.AreEqual(8, Mesh4DParts.FrameOf(new List<int> { 7, 8 }, o => kinds[o]), "not a bell");
            Assert.AreEqual(7, Mesh4DParts.FrameOf(new List<int> { 7 }, o => kinds[o]), "only a bell: that");
        }

        [Test]
        public void Parts_ARecordedScaleIsNotAMove()
        {
            // a number shrunk into its row: the part's transform (no scale) is where the recording has it, so nothing moved
            var home = Matrix4x4.TRS(new Vector3(-2.45f, 0.06f, -0.45f), Quaternion.Euler(0f, 30f, 4f), new Vector3(0.55f, 0.57f, 0.55f));
            var placed = Matrix4x4.TRS(home.GetColumn(3), home.rotation, Vector3.one);

            Assert.IsTrue(Mesh4DParts.IsIdentity(placed * Mesh4DParts.Rigid(home).inverse));
            Assert.IsFalse(Mesh4DParts.IsIdentity(placed * home.inverse), "against the scaled pose it would read as a move");
        }

        [Test]
        public void Container_ReadsGzippedBytes()
        {
            var c = Mesh4DContainer.Read(Gzip(SmallContainer()));

            Assert.AreEqual(2, c.Frames);
            Assert.AreEqual(200, c.Colors[9]);
        }

        [Test]
        public void Container_RefusesOtherFiles()
        {
            var bytes = SmallContainer();
            bytes[0] = (byte)'X';
            Assert.Throws<FormatException>(() => Mesh4DContainer.Read(bytes));
            Assert.Throws<FormatException>(() => Mesh4DContainer.Read(SmallContainer(5)));
            Assert.Throws<FormatException>(() => Mesh4DContainer.Read(new byte[4]));
        }

        [Test]
        public void Container_Version3_ReadsCutawaysKeyTimesAndFollow()
        {
            var c = Mesh4DContainer.Read(SmallContainer(3));

            Assert.AreEqual(3, c.Version);
            Assert.AreEqual(2, c.Objects[0].CutType, "a wedge");
            Assert.AreEqual(new Vector3(0f, 0f, 1f), c.Objects[0].CutA);
            Assert.AreEqual(1.05f, c.Objects[0].CutP1, 1e-5f);
            Assert.AreEqual(64, c.CutLevels[0]);
            Assert.AreEqual(64, c.CutLevels[1], "a frame that changes nothing keeps the level");
            Assert.AreEqual(9f, c.FollowRadius);
            Assert.AreEqual(0.5f, c.FollowDeadZone);
            Assert.IsTrue(c.DissolveSphere);
            Assert.AreEqual(0.8f, c.DissolveCentreY, 1e-5f);
            Assert.IsNull(c.Colors, "version 3 decodes colour keyframes on demand");
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40, 50, 60 }, c.ColorKey(0));
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 200, 201, 202 }, c.ColorKey(2));
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40, 50, 60 }, c.ColorKey(0), "going back starts from a full copy");

            c.KeyAt(1.25, out var k0, out var k1, out var mix);
            Assert.AreEqual(1, k0);
            Assert.AreEqual(2, k1);
            Assert.AreEqual(0.5f, mix, 1e-5f, "keyframes at their listed times, not evenly spaced");
        }

        [Test]
        public void ContainerFile_ReadsGzippedAndPlainFiles_AndRefusesACutFile()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ImmersiveXTests");
            Directory.CreateDirectory(folder);
            var gz = Path.Combine(folder, "hologram.sp4m");
            var plain = Path.Combine(folder, "hologram-plain.sp4m");
            var cut = Path.Combine(folder, "hologram-cut.sp4m");
            try
            {
                var bytes = HologramContainer();
                var zipped = Gzip(bytes);
                File.WriteAllBytes(gz, zipped);
                File.WriteAllBytes(plain, bytes);
                File.WriteAllBytes(cut, zipped.AsSpan(0, zipped.Length / 2).ToArray());

                var fromGz = Mesh4DContainer.ReadFile(gz);
                var fromPlain = Mesh4DContainer.ReadFile(plain);
                var fromBytes = Mesh4DContainer.Read(zipped);
                Assert.IsTrue(fromGz.IsHologram);
                Assert.AreEqual(fromBytes.Frames, fromGz.Frames);
                Assert.AreEqual(fromBytes.Triangles, fromGz.Triangles);
                Assert.AreEqual(fromBytes.Triangles, fromPlain.Triangles);
                CollectionAssert.AreEqual(fromBytes.Matrices, fromGz.Matrices);
                Assert.Catch(() => Mesh4DContainer.ReadFile(cut), "half a gzip file is refused");
            }
            finally
            {
                foreach (var f in new[] { gz, plain, cut })
                    if (File.Exists(f))
                        File.Delete(f);
            }
        }

        [Test]
        public void Container_Version4_ReadsTheHologramLook()
        {
            var c = Mesh4DContainer.Read(Gzip(HologramContainer()));

            Assert.AreEqual(4, c.Version);
            Assert.IsTrue(c.IsHologram);
            Assert.AreEqual(0.1f, c.Holo[0].x, 1e-6f, "the look's constants come from the header");
            Assert.AreEqual(0.16f, c.Holo[3].w, 1e-6f);
            var o = c.Objects[0];
            Assert.AreEqual(new Color(1f, 0.5f, 0.25f), o.Hue);
            Assert.AreEqual(new Vector2(0.5f, 0.2f), o.Gain);
            Assert.AreEqual(0.05f, o.LineSpacing, 1e-6f);
            Assert.AreEqual(new Vector2(-0.1f, 1.8f), o.Span);
            CollectionAssert.AreEqual(new[] { 0, 3, 0, 3 }, o.MeshRange);
            Assert.AreEqual(9, c.MeshPositions.Length);
            Assert.AreEqual(-32767, c.MeshNormals[8]);
            CollectionAssert.AreEqual(new uint[] { 0, 1, 2 }, c.MeshIndices);
            Assert.AreEqual(128, c.Highlights[0], "the glow");
            Assert.AreEqual(128, c.Highlights[1], "carried forward");
            Assert.AreEqual(200, c.CutLevels[0], "how far it has materialised");
            Assert.AreEqual(200, c.CutLevels[1], "carried forward");
            Assert.AreEqual(0.8f, c.FrameFloats[7], 1e-6f, "the fade");
            Assert.AreEqual(5f, c.Matrices[9]);
            Assert.AreEqual(1.8f, c.RoomHeight);
            Assert.AreEqual(0, c.Probes, "no colour probes");
        }

        [TestCase("scene.sp4m", true)]
        [TestCase("Scene.SP4M.gz", true)]
        [TestCase("scene.glb", false)]
        [TestCase("sp4m.json", false)]
        public void Container_Names(string name, bool expected)
        {
            Assert.AreEqual(expected, Mesh4DContainer.IsContainerName(name));
        }

        [Test]
        public void Import_KeepsGzipButDropsTheGzName()
        {
            // Android's packer unzips .gz assets and drops the extension, so the app couldn't find them
            Assert.AreEqual("scene.sp4m", MediaImport.AndroidSafeName("scene.sp4m.gz"));
            Assert.AreEqual("scene.sp4m", MediaImport.AndroidSafeName("scene.sp4m"));
            Assert.AreEqual("sound.m4a", MediaImport.AndroidSafeName("sound.m4a"));
        }

        static byte[] Slice(byte[] a, int start, int count)
        {
            var s = new byte[count];
            Array.Copy(a, start, s, 0, count);
            return s;
        }
    }
}
