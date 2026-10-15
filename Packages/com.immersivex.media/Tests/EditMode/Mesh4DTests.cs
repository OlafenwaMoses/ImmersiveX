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
            poses.Add(0); // frame 1: nothing moved
            poses.Add(0); // hidden
            poses.AddRange(new byte[] { 1, 0, 255 }); // highlight 255
            floats[0] = 0.5f;
            poses.AddRange(Of(floats));
            s.Add("poses", poses.ToArray());

            var header = "{\"title\":\"Test\",\"duration\":0.0333,\"fps\":30,\"frames\":2,\"colorRate\":2,\"colorKeys\":3,\"probes\":2," +
                "\"triangles\":1,\"audio\":\"sound.m4a\",\"atlas\":{\"width\":2,\"height\":2}," +
                "\"objects\":[{\"name\":\"Shell\",\"part\":\"body\",\"kind\":\"hull\",\"flags\":1,\"side\":2,\"flip\":0,\"probeStart\":0,\"probeCount\":2," +
                "\"atlas\":[0,3,0,3],\"door\":{\"y\":[1,2],\"a\":[-1.57,0.32]}}]," +
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
            Assert.Throws<FormatException>(() => Mesh4DContainer.Read(SmallContainer(3)));
            Assert.Throws<FormatException>(() => Mesh4DContainer.Read(new byte[4]));
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
