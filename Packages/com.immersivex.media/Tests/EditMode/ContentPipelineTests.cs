using System;
using System.IO;
using System.Linq;
using System.Text;
using ImmersiveX.Media.Editor;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    /// <summary>Getting content into the app without code: folder sources, 4D packing, copying with companion files, the build check.</summary>
    public class ContentPipelineTests
    {
        string _folder;

        [SetUp]
        public void MakeFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ImmersiveXContentTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        [Test]
        public void FolderIndex_ListsEveryFolderWithoutMetaFiles()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "Show", "frames"));
            File.WriteAllText(Path.Combine(_folder, "Show", "frames", "f2.ply"), "x");
            File.WriteAllText(Path.Combine(_folder, "Show", "frames", "f1.ply"), "x");
            File.WriteAllText(Path.Combine(_folder, "Show", "frames", "f1.ply.meta"), "x");
            File.WriteAllText(Path.Combine(_folder, "Show", "sound \"live\".mp3"), "x");

            var index = MediaFolders.ParseIndex(MediaFolders.BuildIndex(_folder));

            CollectionAssert.AreEqual(new[] { "f1.ply", "f2.ply" }, index["Show/frames"]);
            CollectionAssert.AreEqual(new[] { "sound \"live\".mp3" }, index["Show"], "names are escaped in the JSON");
        }

        [Test]
        public void Packer_TurnsSplatFramesIntoAStreamWithOneFit()
        {
            var capture = Path.Combine(_folder, "capture");
            Directory.CreateDirectory(capture);
            for (var frame = 0; frame < 3; frame++)
                File.WriteAllBytes(Path.Combine(capture, $"Frame_{frame:D4}.ply"), GaussianPly(200 + frame * 10, armRaised: frame == 1));
            File.WriteAllText(Path.Combine(capture, "Frame_0000.txt"), "not a frame");

            var frames = SplatSequencePacker.Frames(capture);
            Assert.AreEqual(3, frames.Length);
            var output = Path.Combine(_folder, "stream");
            var result = SplatSequencePacker.Pack(frames, output, 24f, UpAxis.Auto, 0, "Test \"figure\"");

            var manifest = HologramManifest.Parse(File.ReadAllText(result.Manifest), new Uri(result.Manifest).AbsoluteUri);
            Assert.AreEqual(3, manifest.FrameCount);
            Assert.AreEqual(24f, manifest.Fps);
            Assert.IsTrue(manifest.Fit.HasValue, "one fit for the whole clip");
            Assert.AreEqual(1.5f, manifest.Fit.Value.Height, 0.1f, "the usual height, not the raised arm's");
            var tier = manifest.FindTier("base");
            for (var i = 0; i < 3; i++)
            {
                var file = new Uri(manifest.FrameUrl(tier, i)).LocalPath;
                var bytes = File.ReadAllBytes(file);
                Assert.AreEqual(tier.Bytes[i], bytes.Length);
                var data = new uint[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
                var header = HologramFrame.ReadHeader(data);
                Assert.AreEqual(200 + i * 10, header.Count);
                Assert.IsTrue(HologramFrame.IsValid(header, manifest.TextureWidth, bytes.Length));
            }
        }

        [Test]
        public void Packer_IgnoresFoldersOfMeshes()
        {
            File.WriteAllText(Path.Combine(_folder, "a.ply"), "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n");
            File.Copy(Path.Combine(_folder, "a.ply"), Path.Combine(_folder, "b.ply"));
            Assert.IsEmpty(SplatSequencePacker.Frames(_folder));
        }

        [Test]
        public void Import_FindsTheFilesAModelNeeds()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "textures"));
            File.WriteAllText(Path.Combine(_folder, "chair.obj"), "mtllib chair.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl wood\nf 1 2 3\n");
            File.WriteAllText(Path.Combine(_folder, "chair.mtl"), "newmtl wood\nmap_Kd textures/wood.png\nmap_Bump textures/missing.png\n");
            File.WriteAllText(Path.Combine(_folder, "textures", "wood.png"), "x");
            CollectionAssert.AreEquivalent(new[] { "chair.mtl", "textures/wood.png" }, MediaImport.Sidecars(Path.Combine(_folder, "chair.obj")),
                "files that aren't there are left out");

            File.WriteAllText(Path.Combine(_folder, "duck.gltf"),
                "{\"buffers\":[{\"uri\":\"duck%20data.bin\"}],\"images\":[{\"uri\":\"textures/wood.png\"},{\"uri\":\"data:image/png;base64,AAAA\"}]}");
            File.WriteAllText(Path.Combine(_folder, "duck data.bin"), "x");
            CollectionAssert.AreEquivalent(new[] { "duck data.bin", "textures/wood.png" }, MediaImport.Sidecars(Path.Combine(_folder, "duck.gltf")));

            Assert.AreEqual("My_Zebra_4D", MediaImport.SafeName("My Zebra (4D)"));
        }

        [Test]
        public void BuildCheck_CatchesContentThatWontBeOnTheDevice()
        {
            Assert.IsNotNull(MediaBuildCheck.Check("ImmersiveXContent/Nothing Here/stream.json", "base", out _));
            Assert.IsNotNull(MediaBuildCheck.Check("/Users/someone/Downloads/scene.ply", "base", out _));
            Assert.IsNotNull(MediaBuildCheck.Check(string.Empty, "base", out _));
            Assert.IsNull(MediaBuildCheck.Check("https://example.com/show/stream.json", "base", out var none));
            Assert.IsNull(none);
            Assert.IsNull(MediaBuildCheck.Check("http://example.com/film.mp4", "base", out var warning));
            StringAssert.Contains("https", warning);
        }

        /// <summary>A standing figure of Gaussian splats, 1.5 tall in the usual y-down frame, one arm raised on request.</summary>
        static byte[] GaussianPly(int count, bool armRaised)
        {
            var names = new[] { "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2", "opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3" };
            var header = "ply\nformat binary_little_endian 1.0\nelement vertex " + count + "\n" +
                         string.Concat(names.Select(n => $"property float {n}\n")) + "end_header\n";
            var data = new byte[count * names.Length * 4];
            var random = new System.Random(count);
            for (var i = 0; i < count; i++)
            {
                var arm = armRaised && i % 10 == 0;
                var values = new[]
                {
                    (float)random.NextDouble() * 0.4f - 0.2f,
                    arm ? -1.9f : -(float)random.NextDouble() * 1.5f, // y down: the head is at -1.5, a raised hand above it
                    (float)random.NextDouble() * 0.2f - 0.1f,
                    0.5f, 0.2f, -0.3f, 2f, -4f, -4f, -4f, 1f, 0f, 0f, 0f,
                };
                Buffer.BlockCopy(values, 0, data, i * names.Length * 4, names.Length * 4);
            }

            return Encoding.ASCII.GetBytes(header).Concat(data).ToArray();
        }
    }
}
