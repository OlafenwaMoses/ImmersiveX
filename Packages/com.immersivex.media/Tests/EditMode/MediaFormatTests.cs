using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    /// <summary>
    /// Every splat, point and mesh format decodes the committed test figure (StreamingAssets/ImmersiveXSamples, made by
    /// tools/samples/make_media_samples.py) the same way round: head above feet, green arm on the viewer's left, nose
    /// towards the viewer.
    /// </summary>
    public class MediaFormatTests
    {
        static string Samples => Path.Combine(Application.streamingAssetsPath, "ImmersiveXSamples");

        static byte[] Sample(string path)
        {
            var file = Path.Combine(Samples, path);
            if (!File.Exists(file))
                Assert.Ignore($"{file} is missing; run tools/samples/make_media_samples.py.");
            return File.ReadAllBytes(file);
        }

        [TestCase("splats/figure.ply")]
        [TestCase("splats/figure.compressed.ply")]
        [TestCase("splats/figure.splat")]
        [TestCase("splats/figure.spz")]
        [TestCase("splats/figure-v2.spz")]
        [TestCase("splats/figure-v3.spz")]
        [TestCase("splats/figure.ksplat")]
        [TestCase("splats/figure-level0.ksplat")]
        [TestCase("points/figure-points.ply")]
        public void SplatFormats_DecodeTheFigureTheRightWayRound(string path)
        {
            var cloud = SplatDecoders.Decode(Sample(path), MediaSource.Extension(path));
            cloud.ToYDown(UpAxis.Auto);

            Assert.AreEqual(6900, cloud.Count);
            var head = Centroid(cloud, (r, g, b) => r > 0.75f && g < 0.3f && b < 0.3f);
            var feet = Centroid(cloud, (r, g, b) => b > 0.75f && r < 0.35f);
            var green = Centroid(cloud, (r, g, b) => g > 0.7f && r < 0.3f && b < 0.4f);
            var yellow = Centroid(cloud, (r, g, b) => r > 0.8f && g > 0.7f && b < 0.3f);

            // y down: the head has the smaller y. x right: the green arm is on the left (negative x).
            Assert.Less(head.y, feet.y - 1f, "head above feet");
            Assert.Less(green.x, -0.3f, "green arm on the viewer's left");
            Assert.Greater(yellow.x, 0.3f, "yellow arm on the right");

            // Rotations decode to unit quaternions and scales stay positive.
            for (var i = 0; i < cloud.Count; i += 97)
            {
                var q = new Vector4(cloud.Rotations[i * 4], cloud.Rotations[i * 4 + 1], cloud.Rotations[i * 4 + 2], cloud.Rotations[i * 4 + 3]);
                Assert.AreEqual(1f, q.magnitude, 1e-3f);
                Assert.Greater(cloud.Scales[i * 3], 0f);
            }
        }

        [TestCase("splats/figure.ply")]
        [TestCase("splats/figure.spz")]
        public void SplatFormats_NoseFacesTheViewer(string path)
        {
            var cloud = SplatDecoders.Decode(Sample(path), MediaSource.Extension(path));
            cloud.ToYDown(UpAxis.Auto);
            var nose = Centroid(cloud, (r, g, b) => r > 0.9f && g > 0.35f && g < 0.65f && b < 0.2f);
            var head = Centroid(cloud, (r, g, b) => r > 0.75f && g < 0.3f && b < 0.3f);
            Assert.Less(nose.z, head.z - 0.05f, "in the y-down frame the viewer is on the -z side");
        }

        [TestCase("meshes/figure.obj")]
        [TestCase("meshes/figure-mesh.ply")]
        public void MeshFormats_DecodeTheFigureInUnityCoordinates(string path)
        {
            var frame = MeshDecoders.Decode(Sample(path), MediaSource.Extension(path), UpAxis.PositiveY);

            Assert.AreEqual(56, frame.Positions.Length);
            Assert.AreEqual(84 * 3, frame.Triangles.Length);
            Assert.IsNotNull(frame.Colors);
            Vector3 Average(System.Func<Color32, bool> pick) =>
                frame.Positions.Where((_, i) => pick(frame.Colors[i])).Aggregate(Vector3.zero, (a, p) => a + p) /
                frame.Positions.Where((_, i) => pick(frame.Colors[i])).Count();
            var head = Average(c => c.r > 200 && c.g < 60);
            var green = Average(c => c.g > 180 && c.r < 60);
            var nose = Average(c => c.r > 240 && c.g > 100 && c.g < 160);
            Assert.Greater(head.y, 1.3f, "y up");
            Assert.Less(green.x, -0.2f, "green arm on the viewer's left");
            Assert.Less(nose.z, -0.1f, "the front faces -Z, towards a viewer in front of the media");
        }

        [Test]
        public void Obj_TexturedTotemNamesItsTextureThroughTheMtl()
        {
            var frame = MeshDecoders.Decode(Sample("meshes/totem.obj"), ".obj", UpAxis.PositiveY);
            Assert.AreEqual("totem.mtl", frame.MaterialLibrary);
            Assert.AreEqual("skin", frame.Material);
            Assert.AreEqual("totem.png", MeshDecoders.MtlTexture("newmtl skin\nKd 1 1 1\nmap_Kd totem.png\n", "skin"));
            Assert.IsNotNull(frame.Uvs);
        }

        [Test]
        public void Ply_ContentIsDetectedFromTheHeader()
        {
            Assert.AreEqual(PlyContent.GaussianSplats, PlyFile.Parse(Sample("splats/figure.ply")).Content);
            Assert.AreEqual(PlyContent.CompressedSplats, PlyFile.Parse(Sample("splats/figure.compressed.ply")).Content);
            Assert.AreEqual(PlyContent.PointCloud, PlyFile.Parse(Sample("points/figure-points.ply")).Content);
            Assert.AreEqual(PlyContent.Mesh, PlyFile.Parse(Sample("meshes/figure-mesh.ply")).Content);
        }

        [Test]
        public void Json_StreamsAndSequencesAreTellApart()
        {
            var stream = new MediaDetection { Url = "file:///s/stream.json", Bytes = Sample("hologram/stream.json") };
            MediaDetector.ClassifyJson(stream);
            Assert.AreEqual(MediaKind.HologramStream, stream.Kind);

            var sequence = new MediaDetection { Url = "file:///s/sequence.json", Bytes = Sample("mesh-sequence/sequence.json") };
            MediaDetector.ClassifyJson(sequence);
            Assert.AreEqual(MediaKind.Sequence, sequence.Kind);
        }

        [Test]
        public void Sequence_FramesAndPatternsResolveNextToTheJson()
        {
            var listed = MediaSequence.FromJson("{\"type\":\"sequence\",\"fps\":24,\"frames\":[\"a.ply\",\"b.ply\"],\"up\":\"-y\"}", "https://x.test/show/sequence.json");
            CollectionAssert.AreEqual(new[] { "https://x.test/show/a.ply", "https://x.test/show/b.ply" }, listed.Frames);
            Assert.AreEqual(UpAxis.NegativeY, listed.Up);
            Assert.AreEqual(2 / 24.0, listed.Duration, 1e-9);

            var pattern = MediaSequence.FromJson("{\"type\":\"sequence\",\"pattern\":\"frames/f_{0:D3}.obj\",\"start\":7,\"count\":3}", "file:///data/seq.json");
            CollectionAssert.AreEqual(new[] { "file:///data/frames/f_007.obj", "file:///data/frames/f_008.obj", "file:///data/frames/f_009.obj" }, pattern.Frames);
        }

        [Test]
        public void Source_ResolvesRelativePathsForEveryScheme()
        {
            Assert.AreEqual("https://cdn.test/a/b/c.png", MediaSource.Relative("https://cdn.test/a/b/model.gltf", "c.png"));
            Assert.AreEqual("https://cdn.test/a/c.png", MediaSource.Relative("https://cdn.test/a/b/model.gltf", "../c.png"));
            Assert.AreEqual("jar:file:///app.apk!/assets/s/f%201.ply", MediaSource.Relative("jar:file:///app.apk!/assets/s/seq.json", "f 1.ply"));
            Assert.AreEqual(".spz", MediaSource.Extension("https://cdn.test/x/figure.SPZ?v=2#top"));
            Assert.AreEqual("figure.spz", MediaSource.FileName("https://cdn.test/x/figure.spz?v=2"));
        }

        [TestCase("holiday.mp4", 1920, 1080, MediaFormat.Auto, 0f, VideoLayout.Mono)]
        [TestCase("tour_360.mp4", 1920, 1080, MediaFormat.Auto, 360f, VideoLayout.Mono)]
        [TestCase("plain.mp4", 4096, 2048, MediaFormat.Auto, 360f, VideoLayout.Mono)]
        [TestCase("concert_360_TB.mp4", 2048, 2048, MediaFormat.Auto, 360f, VideoLayout.TopBottom)]
        [TestCase("dive_180_sbs.mp4", 4096, 2048, MediaFormat.Auto, 180f, VideoLayout.SideBySide)]
        [TestCase("square.mp4", 1080, 1080, MediaFormat.Auto, 0f, VideoLayout.Mono)]
        [TestCase("holiday.mp4", 1920, 1080, MediaFormat.Video360, 360f, VideoLayout.Mono)]
        [TestCase("MaryOculus.mp4", 2048, 2048, MediaFormat.Video360, 360f, VideoLayout.TopBottom)] // square 360°: top-bottom
        [TestCase("clip.mp4", 3840, 1920, MediaFormat.Video180, 180f, VideoLayout.SideBySide)]    // 2:1 180°: VR180
        [TestCase("park.jpg", 6000, 3000, MediaFormat.Auto, 360f, VideoLayout.Mono)]              // a 360° photo
        [TestCase("portrait.jpg", 3000, 4000, MediaFormat.Auto, 0f, VideoLayout.Mono)]
        [TestCase("IMG_1805.jpg", 4000, 3000, MediaFormat.Auto, 0f, VideoLayout.Mono)]                // a camera number isn't 180°
        [TestCase("beach_outdoor.jpg", 6000, 3000, MediaFormat.Auto, 360f, VideoLayout.Mono)]       // "outdoor" isn't over-under
        [TestCase("walrus-360.top-bottom.jpg", 4096, 4096, MediaFormat.Auto, 360f, VideoLayout.TopBottom)]
        [TestCase("0001-0100_vr180_injected.mp4", 2048, 1024, MediaFormat.Auto, 180f, VideoLayout.SideBySide)]
        [TestCase("Pano360 Москва, улица.jpg", 4608, 2304, MediaFormat.Auto, 360f, VideoLayout.Mono)]
        public void Pictures_KindComesFromTheFormatNameAndShape(string name, int width, int height, MediaFormat requested, float coverage, VideoLayout layout)
        {
            var (c, l) = Projection.Classify(requested, StereoLayout.Auto, name, width, height);
            Assert.AreEqual(coverage, c);
            Assert.AreEqual(layout, l);
        }

        [Test]
        public void Pictures_TheStereoSettingWins()
        {
            Assert.AreEqual((360f, VideoLayout.Mono), Projection.Classify(MediaFormat.Auto, StereoLayout.Mono, "concert_360_TB.mp4", 2048, 2048));
            Assert.AreEqual((180f, VideoLayout.SideBySide), Projection.Classify(MediaFormat.Auto, StereoLayout.SideBySide, "untitled.mp4", 3840, 1920),
                "a 2:1 side-by-side frame is two square 180° eyes");
            Assert.AreEqual((0f, VideoLayout.Mono), Projection.Classify(MediaFormat.Video, StereoLayout.TopBottom, "movie_tb.mp4", 1920, 2160),
                "flat stereo isn't split");
        }

        [Test]
        public void Obj_SeveralMaterialsBecomeParts()
        {
            const string obj = "mtllib room.mtl\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\n" +
                               "usemtl red wall\nf 1 2 3\nusemtl floor\nf 1 3 4\nusemtl red wall\nf 2 3 4\n";
            var frame = MeshDecoders.Obj(obj, UpAxis.PositiveY);

            Assert.AreEqual(2, frame.Parts.Length);
            Assert.AreEqual("red wall", frame.Parts[0].Material, "names with spaces are kept");
            Assert.AreEqual(6, frame.Parts[0].Triangles.Length, "both 'red wall' faces, although the groups were split");
            Assert.AreEqual("floor", frame.Parts[1].Material);
            Assert.AreEqual(9, frame.Triangles.Length, "Triangles still has every face");
            Assert.AreEqual("room.mtl", frame.MaterialLibrary);
            Assert.IsNull(MeshDecoders.Obj("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", UpAxis.PositiveY).Parts, "one material: no parts");
        }

        [Test]
        public void Mtl_ReadsColoursTexturesAndOptions()
        {
            const string mtl = "newmtl red wall\nKd 1 0 0\nnewmtl wood\nKd 0.8 0.8 0.8\nmap_Kd -s 1 1 1 -clamp on textures/wood grain.png\nbump wood_n.png\nd 0.5\n";
            var materials = MeshDecoders.Mtl(mtl);

            Assert.AreEqual("red wall", materials[0].Key);
            Assert.AreEqual(Color.red, materials[0].Value.Diffuse);
            Assert.IsNull(materials[0].Value.Texture);
            Assert.AreEqual("textures/wood grain.png", materials[1].Value.Texture, "options skipped, spaces kept");
            Assert.AreEqual(0.5f, materials[1].Value.Opacity);
            CollectionAssert.AreEqual(new[] { "textures/wood grain.png", "wood_n.png" }, MeshDecoders.MtlTextures(mtl));
        }

        [Test]
        public void Stl_BinaryAndAsciiDecode()
        {
            var binary = new byte[84 + 50];
            System.BitConverter.GetBytes(1u).CopyTo(binary, 80);
            var corners = new[] { 0f, 0f, 0f, 10f, 0f, 0f, 0f, 0f, 20f }; // z-up: the third corner is 20 above the first
            for (var k = 0; k < corners.Length; k++)
                System.BitConverter.GetBytes(corners[k]).CopyTo(binary, 84 + 12 + k * 4);
            var fromBinary = MeshDecoders.Stl(binary, UpAxis.PositiveZ);
            Assert.AreEqual(3, fromBinary.Positions.Length);
            Assert.AreEqual(20f, fromBinary.Bounds.size.y, 1e-4f, "z-up becomes Unity's y-up");

            const string ascii = "solid part\n facet normal 0 0 1\n  outer loop\n   vertex 0 0 0\n   vertex 10 0 0\n   vertex 0 0 20\n  endloop\n endfacet\nendsolid part\n";
            var fromAscii = MeshDecoders.Stl(System.Text.Encoding.ASCII.GetBytes(ascii), UpAxis.PositiveZ);
            Assert.AreEqual(3, fromAscii.Triangles.Length);
            Assert.AreEqual(fromBinary.Bounds, fromAscii.Bounds);
        }

        [Test]
        public void Splats_FarBackgroundShellIsLeftOut()
        {
            var random = new System.Random(3);
            var cloud = new SplatCloud(10_600);
            for (var i = 0; i < cloud.Count; i++)
            {
                var direction = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized;
                var radius = i < 10_000 ? (float)random.NextDouble() : 200f; // an object, then a sky shell 200 units out
                cloud.Positions[i * 3] = direction.x * radius;
                cloud.Positions[i * 3 + 1] = direction.y * radius;
                cloud.Positions[i * 3 + 2] = direction.z * radius;
                cloud.Rotations[i * 4] = 1f;
            }

            var trimmed = cloud.WithoutFarBackground(out var dropped);
            Assert.AreEqual(600, dropped);
            Assert.AreEqual(10_000, trimmed.Count);
            Assert.AreSame(trimmed, trimmed.WithoutFarBackground(out var none), "a compact capture keeps everything");
            Assert.AreEqual(0, none);
        }

        [Test]
        public void Sequence_FolderFramesPlayInNaturalOrderWithTheirSound()
        {
            var sequence = MediaSequence.FromFiles("jar:file:///app.apk!/assets/show", new[] { "f10.ply", "f2.ply", "f 1.ply", "sound.mp3", "notes.txt" });
            CollectionAssert.AreEqual(new[] { "jar:file:///app.apk!/assets/show/f%201.ply", "jar:file:///app.apk!/assets/show/f2.ply", "jar:file:///app.apk!/assets/show/f10.ply" }, sequence.Frames);
            Assert.AreEqual("jar:file:///app.apk!/assets/show/sound.mp3", sequence.AudioUrl);
            Assert.Throws<System.FormatException>(() => MediaSequence.FromFiles("file:///x", new[] { "a.ply", "b.obj" }), "one kind of frame per folder");
        }

        [Test]
        public void HalfFloats_Decode()
        {
            Assert.AreEqual(1f, SplatDecoders.HalfToFloat(0x3c00));
            Assert.AreEqual(-2f, SplatDecoders.HalfToFloat(0xc000));
            Assert.AreEqual(0.5f, SplatDecoders.HalfToFloat(0x3800));
            Assert.AreEqual(5.96046448e-8f, SplatDecoders.HalfToFloat(0x0001), 1e-12f);
        }

        [Test]
        public void Layout_SpotsSurroundTheFirstWithoutBlockingIt()
        {
            var towardUser = Vector3.back;
            Assert.AreEqual(Vector3.zero, MediaLayout.Offset(0, towardUser));
            for (var slot = 1; slot < 8; slot++)
            {
                var offset = MediaLayout.Offset(slot, towardUser);
                Assert.AreEqual(1.3f, offset.magnitude, 1e-3f);
                Assert.Greater(Vector3.Angle(offset, towardUser), 40f, "never straight between the first media and the user");
            }
        }

        static Vector3 Centroid(SplatCloud cloud, System.Func<float, float, float, bool> pick)
        {
            var sum = Vector3.zero;
            var n = 0;
            for (var i = 0; i < cloud.Count; i++)
            {
                if (!pick(cloud.Colors[i * 4], cloud.Colors[i * 4 + 1], cloud.Colors[i * 4 + 2]))
                    continue;
                sum += new Vector3(cloud.Positions[i * 3], cloud.Positions[i * 3 + 1], cloud.Positions[i * 3 + 2]);
                n++;
            }

            Assert.Greater(n, 20, "the colour picks some splats");
            return sum / n;
        }
    }
}
