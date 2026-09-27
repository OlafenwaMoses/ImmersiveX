using System;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class PlacementIndexTests
    {
        static readonly DateTime Noon = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Set_ClearsTheAnchorAndHandsBackTheOldOne()
        {
            var index = new PlacementIndex();
            var pose = new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 30f, 0f));
            Assert.AreEqual(string.Empty, index.Set("media", pose, 1.6f, Noon));
            Assert.IsTrue(index.SetAnchor("media", "anchor-1"));

            var old = index.Set("media", new Pose(Vector3.zero, Quaternion.identity), 2f, Noon);

            Assert.AreEqual("anchor-1", old, "the old anchor is handed back so it can be erased");
            Assert.AreEqual(string.Empty, index.Find("media").anchor, "no anchor until the new one is saved");
            Assert.AreEqual(2f, index.Find("media").size);
            Assert.IsFalse(index.SetAnchor("missing", "anchor-2"));
        }

        [Test]
        public void Json_RoundTripsPosesAnchorsAndSizes()
        {
            var index = new PlacementIndex();
            index.Set("floating", new Pose(new Vector3(0.5f, 1.2f, -1f), Quaternion.Euler(10f, 45f, 5f)), 0f, Noon);
            index.SetAnchor("floating", "8f2c1f0e-0000-4000-8000-000000000001");

            var copy = PlacementIndex.FromJson(index.ToJson()).Find("floating");

            Assert.AreEqual(new Vector3(0.5f, 1.2f, -1f), copy.position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(10f, 45f, 5f), copy.rotation), 0.01f);
            Assert.AreEqual("8f2c1f0e-0000-4000-8000-000000000001", copy.anchor);
            Assert.AreEqual("2026-09-28T12:00:00.0000000Z", copy.savedAtUtc);
            StringAssert.Contains($"\"version\": {PlacementIndex.CurrentVersion}", index.ToJson());
        }

        [Test]
        public void Json_Version1PlacementsStillLoad()
        {
            const string version1 = "{\"placements\":[{\"key\":\"Demo/Media/a.ply\",\"x\":1.5,\"z\":-2.0,\"yaw\":90.0,\"size\":1.6}]}";

            var entry = PlacementIndex.FromJson(version1).Find("Demo/Media/a.ply");

            Assert.AreEqual(new Vector3(1.5f, 0f, -2f), entry.position);
            Assert.AreEqual(90f, entry.rotation.eulerAngles.y, 0.01f);
            Assert.AreEqual(1.6f, entry.size);
            Assert.AreEqual(string.Empty, entry.anchor);
        }

        [Test]
        public void ShortIds_AreTheFirstEightCharacters()
        {
            Assert.AreEqual("66cc47b8", ContentAnchor.Short("66cc47b8-1234-4000-8000-000000000000"));
            Assert.AreEqual("-", ContentAnchor.Short(string.Empty));
        }
    }
}
