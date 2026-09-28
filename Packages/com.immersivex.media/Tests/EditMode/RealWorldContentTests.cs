using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    /// <summary>
    /// Decodes real files people publish, gathered from Khronos, PlayCanvas, Niantic, antimatter15, GaussianSplats3D,
    /// Open3D, Stanford and others into <c>research/web-corpus</c> (local and git-ignored; its MANIFEST.md lists the
    /// sources and licences). Kept outside StreamingAssets so it never ships in a build. Skipped when a file isn't there.
    /// </summary>
    public class RealWorldContentTests
    {
        static string Web => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "research", "web-corpus"));

        static byte[] File(string path)
        {
            var file = Path.Combine(Web, path);
            if (!System.IO.File.Exists(file))
                Assert.Ignore($"{path} isn't in research/web-corpus.");
            return System.IO.File.ReadAllBytes(file);
        }

        [TestCase("splats/Unicorn_Stuffy.ply")]
        [TestCase("splats/Halo_Believe.ply")]
        [TestCase("splats/DC_border.ply")]
        [TestCase("splats/skull.compressed.ply")]
        [TestCase("splats/guitar.compressed.ply")]
        [TestCase("splats/gs_Skull.splat")]
        [TestCase("splats/hornedlizard.spz")]
        [TestCase("splats/biker.spz")]
        [TestCase("splats/bonsai_trimmed.ksplat")]
        [TestCase("splats/bonsai_high.ksplat")]
        [TestCase("points/fragment.ply")]
        [TestCase("splats4d/longdress/longdress_vox10_1051.ply")]
        public void Splats_Decode(string path)
        {
            var cloud = SplatDecoders.Decode(File(path), MediaSource.Extension(path));

            Assert.Greater(cloud.Count, 1000);
            for (var i = 0; i < cloud.Count; i += Mathf.Max(1, cloud.Count / 500))
            {
                var p = new Vector3(cloud.Positions[i * 3], cloud.Positions[i * 3 + 1], cloud.Positions[i * 3 + 2]);
                Assert.IsTrue(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), $"splat {i} has a finite position");
                var q = new Vector4(cloud.Rotations[i * 4], cloud.Rotations[i * 4 + 1], cloud.Rotations[i * 4 + 2], cloud.Rotations[i * 4 + 3]);
                Assert.AreEqual(1f, q.magnitude, 1e-2f, $"splat {i} has a unit rotation");
                Assert.Greater(cloud.Scales[i * 3], 0f);
            }
        }

        [TestCase("splats/biker.spz")]
        [TestCase("splats/hornedlizard.spz")]
        [TestCase("splats/bonsai_high.ksplat")]
        public void Splats_PackOnAWorkerThreadLikeThePlayer(string path)
        {
            var bytes = File(path);
            var extension = MediaSource.Extension(path);
            var packed = SplatPacker.PackAsync(() => SplatDecoders.Decode(bytes, extension), UpAxis.Auto, 2_000_000).Result;
            Assert.Greater(packed.Header.Count, 1000);
            Assert.GreaterOrEqual(packed.Positions.Length, packed.Header.Count * 3, "positions for every packed splat, for sorting");
        }

        [TestCase("meshes/bun_zipper.ply")]
        [TestCase("meshes/dolphins_colored.ply")]
        [TestCase("meshes/pr2_head_pan.stl")]
        [TestCase("meshes/CornellBox/CornellBox-Original.obj")]
        [TestCase("meshes/MonkeyModel/monkey.obj")]
        public void Meshes_Decode(string path)
        {
            var frame = MeshDecoders.Decode(File(path), MediaSource.Extension(path), UpAxis.PositiveY);

            Assert.Greater(frame.Triangles.Length, 3);
            Assert.Greater(frame.Bounds.size.magnitude, 0f);
            if (path.Contains("CornellBox"))
                Assert.Greater(frame.Parts.Length, 1, "the Cornell box's walls have their own materials");
        }
    }
}
