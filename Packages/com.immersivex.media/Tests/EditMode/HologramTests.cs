using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    public class HologramTests
    {
        const string ManifestUrl = "https://cdn.example.com/demos/show/stream.json";

        const string ManifestJson = @"{
            ""version"": 3, ""fps"": 24.0, ""frames"": 3, ""texture_width"": 1024, ""gaussian_bytes"": 17,
            ""scale_range"": [-9.0, 1.0], ""base_url"": null,
            ""audio"": { ""file"": ""audio.m4a"", ""codec"": ""aac"" },
            ""camera"": { ""target"": [0.0, 0.07, 1.67] },
            ""tiers"": [
                { ""name"": ""full"", ""path"": ""tiers/full"", ""rate_mb_s"": 186.2, ""frames"": [[500, 34848], [600, 34848], [700, 34848]] },
                { ""name"": ""base"", ""path"": ""tiers/base"", ""rate_mb_s"": 23.6, ""frames"": [[50, 17440], [60, 17440], [70, 17440]] }
            ],
            ""note"": ""quotes \"" and unicode \u00e9""
        }";

        [Test]
        public void Json_ReadsNestedArraysStringsAndNumbers()
        {
            var root = (Dictionary<string, object>)Json.Parse(ManifestJson);

            Assert.AreEqual(24d, root["fps"]);
            Assert.IsNull(root["base_url"]);
            Assert.AreEqual("quotes \" and unicode é", root["note"]);
            var tiers = (List<object>)root["tiers"];
            var frames = (List<object>)((Dictionary<string, object>)tiers[1])["frames"];
            Assert.AreEqual(60d, ((List<object>)frames[1])[0]);
            Assert.Throws<FormatException>(() => Json.Parse("{\"a\": [1, 2}"));
        }

        [Test]
        public void Manifest_ResolvesUrlsAndSortsTiersSmallestFirst()
        {
            var manifest = HologramManifest.Parse(ManifestJson, ManifestUrl);

            Assert.AreEqual(24f, manifest.Fps);
            Assert.AreEqual(3, manifest.FrameCount);
            Assert.AreEqual(0.125, manifest.Duration, 1e-9);
            Assert.AreEqual(new Vector2(-9f, 1f), manifest.ScaleRange);
            Assert.AreEqual("https://cdn.example.com/demos/show/audio.m4a", manifest.AudioUrl);

            var tier = manifest.FindTier("base");
            Assert.AreEqual("base", tier.Name);
            Assert.AreEqual(70, tier.MaxCount);
            Assert.AreEqual("https://cdn.example.com/demos/show/tiers/base/000002.bin", manifest.FrameUrl(tier, 2));
            Assert.AreEqual("full", manifest.Tiers[manifest.Tiers.Count - 1].Name);
            Assert.AreEqual(manifest.Tiers[0], manifest.FindTier("missing"), "an unknown tier falls back to the smallest");
            Assert.IsNull(manifest.Fit, "fitted per frame unless the stream says otherwise");
        }

        [Test]
        public void Manifest_BundledClipKeepsOneFitAndPlaysFromInsideTheApp()
        {
            var json = ManifestJson.Replace(@"""base_url"": null,", @"""base_url"": null, ""fit"": { ""centre_x"": -0.02, ""centre_z"": -0.06, ""top"": -1.35, ""bottom"": 0.003 },")
                .Replace(@"""audio"": { ""file"": ""audio.m4a"", ""codec"": ""aac"" },", @"""audio"": null,");
            var manifest = HologramManifest.Parse(json, "jar:file:///data/app/genxr.immersivex.app/base.apk!/assets/ImmersiveXContent/Zebra/stream.json");

            Assert.IsTrue(manifest.Fit.HasValue);
            Assert.AreEqual(1.353f, manifest.Fit.Value.Height, 1e-4f);
            Assert.AreEqual(-0.02f, manifest.Fit.Value.CentreX, 1e-6f);
            Assert.IsNull(manifest.AudioUrl, "no soundtrack: the frame clock plays it");
            Assert.AreEqual("jar:file:///data/app/genxr.immersivex.app/base.apk!/assets/ImmersiveXContent/Zebra/tiers/base/000001.bin",
                manifest.FrameUrl(manifest.FindTier("base"), 1));
        }

        [Test]
        public void Frame_HeaderAndPositionsDecode()
        {
            var data = MakeFrame(new[] { new Vector3(-1f, -2f, 3f), new Vector3(1f, 0f, 5f) }, new Vector3(-1f, -2f, 3f), new Vector3(1f, 0f, 5f));
            var header = HologramFrame.ReadHeader(data);

            Assert.AreEqual(2, header.Count);
            Assert.AreEqual(1, header.Rows);
            Assert.AreEqual(new Vector3(-1f, -2f, 3f), header.Min);
            Assert.IsTrue(HologramFrame.IsValid(header, 1024, HologramFrame.ExpectedBytes(1, 1024)));
            Assert.IsFalse(HologramFrame.IsValid(header, 1024, HologramFrame.ExpectedBytes(2, 1024)), "size must match the manifest");
            Assert.AreEqual(8 + 1024 * 4, HologramFrame.AlphaStart(header, 1024));

            var xyz = new float[6];
            HologramFrame.DecodePositions(data, header, xyz);
            Assert.AreEqual(-1f, xyz[0], 1e-4f);
            Assert.AreEqual(-2f, xyz[1], 1e-4f);
            Assert.AreEqual(5f, xyz[5], 1e-4f);
        }

        [Test]
        public void Fit_FindsTheFeetAndHeightAndIgnoresStrays()
        {
            // A 1.6 m column of points (y down: head at -1.0, feet at 0.6) plus one stray far below.
            var points = new List<Vector3>();
            for (var i = 0; i <= 1000; i++)
                points.Add(new Vector3(0.2f, -1f + 1.6f * i / 1000f, 2f));
            points.Add(new Vector3(0.2f, 3f, 2f));
            var xyz = Flatten(points);

            var fit = HologramFrame.Fit(xyz, points.Count, -1f, 3f, new int[256]);

            Assert.AreEqual(1.6f, fit.Height, 0.05f);
            Assert.AreEqual(0.6f, fit.Bottom, 0.05f);
            Assert.AreEqual(0.2f, fit.CentreX, 1e-3f);
            Assert.AreEqual(2f, fit.CentreZ, 1e-3f);
        }

        [Test]
        public void Fit_CutsAreDetected()
        {
            var shot = new HologramFit { Top = -1f, Bottom = 0.6f, CentreX = 0f, CentreZ = 2f };
            var sameShot = new HologramFit { Top = -0.9f, Bottom = 0.6f, CentreX = 0.05f, CentreZ = 2f };
            var closeUp = new HologramFit { Top = -0.2f, Bottom = 0.4f, CentreX = 0f, CentreZ = 0.8f };

            Assert.IsFalse(HologramFit.IsCut(shot, sameShot));
            Assert.IsTrue(HologramFit.IsCut(shot, closeUp));
        }

        [Test]
        public void Sort_DrawsFarthestFirst()
        {
            var xyz = Flatten(new List<Vector3> { new Vector3(0, 0, 2), new Vector3(0, 0, 5), new Vector3(0, 0, 1), new Vector3(0, 0, 3) });
            var order = new uint[4];

            HologramFrame.SortBackToFront(xyz, 4, Vector3.zero, new int[4], new int[65536], order);

            CollectionAssert.AreEqual(new uint[] { 1, 3, 0, 2 }, order);
        }

        [Test]
        public void Window_WrapsWhenLooping()
        {
            Assert.AreEqual(2, HologramLoader.WindowFrame(3660, 10, 3668, loop: true));
            Assert.AreEqual(-1, HologramLoader.WindowFrame(3660, 10, 3668, loop: false));
            Assert.AreEqual(3665, HologramLoader.WindowFrame(3660, 5, 3668, loop: false));
        }

        [Test]
        public void Controls_FormatTime()
        {
            Assert.AreEqual("0:00", MediaControls.FormatTime(0));
            Assert.AreEqual("1:01", MediaControls.FormatTime(61.5));
            Assert.AreEqual("2:32", MediaControls.FormatTime(152.99));
        }

        /// <summary>A frame file with one row of 1024 texels holding <paramref name="points"/> quantised to the box.</summary>
        static uint[] MakeFrame(Vector3[] points, Vector3 min, Vector3 max)
        {
            var data = new uint[HologramFrame.ExpectedBytes(1, 1024) / 4];
            data[0] = (uint)points.Length;
            data[1] = Bits(min.x); data[2] = Bits(min.y); data[3] = Bits(min.z);
            data[4] = Bits(max.x); data[5] = Bits(max.y); data[6] = Bits(max.z);
            data[7] = 1;
            for (var i = 0; i < points.Length; i++)
            {
                var q = new Vector3(Mathf.InverseLerp(min.x, max.x, points[i].x), Mathf.InverseLerp(min.y, max.y, points[i].y), Mathf.InverseLerp(min.z, max.z, points[i].z)) * 65535f;
                data[8 + i * 4] = (uint)Mathf.RoundToInt(q.x) | ((uint)Mathf.RoundToInt(q.y) << 16);
                data[9 + i * 4] = (uint)Mathf.RoundToInt(q.z);
            }

            return data;
        }

        static uint Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

        static float[] Flatten(List<Vector3> points)
        {
            var xyz = new float[points.Count * 3];
            for (var i = 0; i < points.Count; i++)
            {
                xyz[i * 3] = points[i].x;
                xyz[i * 3 + 1] = points[i].y;
                xyz[i * 3 + 2] = points[i].z;
            }

            return xyz;
        }
    }
}
