using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX.Tests
{
    public class SpaceMatchingTests
    {
        string _folder;

        [SetUp]
        public void CreateFolder() => _folder = Path.Combine(Path.GetTempPath(), "ImmersiveXTests-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        static RoomSnapshot Room(float width, float length, float ceiling, params float[] walls)
        {
            var planes = new List<PlaneSample>
            {
                new PlaneSample
                {
                    Id = "floor", Alignment = PlaneAlignment.HorizontalUp, Classifications = PlaneClassifications.Floor,
                    Pose = Pose.identity, Size = new Vector2(width, length),
                    Boundary = new[]
                    {
                        new Vector2(-width / 2, -length / 2), new Vector2(width / 2, -length / 2),
                        new Vector2(width / 2, length / 2), new Vector2(-width / 2, length / 2),
                    },
                },
                new PlaneSample
                {
                    Id = "ceiling", Alignment = PlaneAlignment.HorizontalDown, Classifications = PlaneClassifications.Ceiling,
                    Pose = new Pose(new Vector3(0, ceiling, 0), Quaternion.identity), Size = new Vector2(width, length), Boundary = new Vector2[0],
                },
            };
            for (var i = 0; i < walls.Length; i++)
            {
                planes.Add(new PlaneSample
                {
                    Id = "wall" + i, Alignment = PlaneAlignment.Vertical, Classifications = PlaneClassifications.WallFace,
                    Pose = Pose.identity, Size = new Vector2(walls[i], ceiling), Boundary = new Vector2[0],
                });
            }

            return RoomSnapshot.From(planes, furnitureCount: 2);
        }

        [Test]
        public void Snapshot_FindsFloorWallsAndCeiling()
        {
            var room = Room(4f, 5f, 2.6f, 4f, 5f, 4f, 5f);

            Assert.IsTrue(room.HasFloor);
            Assert.AreEqual("floor", room.FloorId);
            Assert.AreEqual(4, room.WallLengths.Count);
            Assert.AreEqual(5f, room.WallLengths[0], 1e-4f, "longest wall first");
            Assert.AreEqual(2.6f, room.CeilingHeight, 1e-4f);
            Assert.AreEqual(2, room.FurnitureCount);
        }

        [Test]
        public void Snapshot_WithoutLabels_UsesTheLowestLargeUpwardPlaneAsFloor()
        {
            var planes = new List<PlaneSample>
            {
                new PlaneSample { Id = "table", Alignment = PlaneAlignment.HorizontalUp, Pose = new Pose(new Vector3(0, 0.75f, 0), Quaternion.identity),
                    Size = new Vector2(1.5f, 1f), Boundary = new[] { Vector2.zero, Vector2.right, Vector2.one } },
                new PlaneSample { Id = "ground", Alignment = PlaneAlignment.HorizontalUp, Pose = Pose.identity,
                    Size = new Vector2(3f, 3f), Boundary = new[] { Vector2.zero, new Vector2(3, 0), new Vector2(3, 3) } },
            };

            Assert.AreEqual("ground", RoomSnapshot.From(planes, 0).FloorId);
        }

        [Test]
        public void SameRoomScannedAgain_IsRecognisedWithHighConfidence()
        {
            var saved = MappedSpace.Create("Room 1", "metaquest");
            saved.Update(Room(4f, 5f, 2.6f, 4f, 5f, 4f, 5f), "old-floor-id", 0.3f, 12f);

            // A re-scan: new floor ID, slightly different measurements.
            var rescan = SpaceSignature.From(Room(4.05f, 4.95f, 2.62f, 4.05f, 4.95f, 4.05f, 4.95f));
            var match = SpaceMatcher.Match(new[] { saved }, rescan, "new-floor-id");

            Assert.AreEqual(MatchConfidence.High, match.Confidence);
            Assert.AreSame(saved, match.Space);
            Assert.IsFalse(match.ByNativeKey);
        }

        [Test]
        public void PlatformRoomId_WinsOutright()
        {
            var saved = MappedSpace.Create("Room 1", "metaquest");
            saved.Update(Room(4f, 5f, 2.6f, 4f, 5f), "floor-123", 0.3f, 12f);

            var different = SpaceSignature.From(Room(9f, 9f, 3f, 9f, 9f));
            var match = SpaceMatcher.Match(new[] { saved }, different, "floor-123");

            Assert.AreEqual(MatchConfidence.High, match.Confidence);
            Assert.IsTrue(match.ByNativeKey);
        }

        [Test]
        public void DifferentRoom_IsNotMatched()
        {
            var bedroom = MappedSpace.Create("Room 1", "metaquest");
            bedroom.Update(Room(3f, 3.5f, 2.4f, 3f, 3.5f, 3f, 3.5f), "a", 0.3f, 7f);

            var hall = SpaceSignature.From(Room(8f, 12f, 3.2f, 8f, 12f, 8f, 12f, 4f, 4f));
            var match = SpaceMatcher.Match(new[] { bedroom }, hall, "b");

            Assert.Less(match.Score, SpaceMatcher.MediumConfidence);
            Assert.AreNotEqual(MatchConfidence.High, match.Confidence);
            Assert.AreNotEqual(MatchConfidence.Medium, match.Confidence);
        }

        [Test]
        public void Library_SavesLoadsAndDeletesRooms()
        {
            var library = new SpaceLibrary(_folder);
            Assert.AreEqual("Room 1", library.NextName());

            var room = MappedSpace.Create(library.NextName(), "metaquest");
            room.Update(Room(4f, 5f, 2.6f, 4f, 5f), "floor-1", 0.3f, 12.3f);
            library.Save(room);

            var loaded = library.LoadAll();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(room.Id, loaded[0].Id);
            Assert.AreEqual("floor-1", loaded[0].NativeKey);
            Assert.AreEqual(4, loaded[0].FloorPolygon.Length);
            Assert.AreEqual(12.3f, loaded[0].WalkableArea, 1e-4f);
            Assert.AreEqual("Room 2", library.NextName());

            Assert.IsTrue(library.Delete(room.Id));
            Assert.AreEqual(0, library.LoadAll().Count);
        }

        [Test]
        public void Library_SkipsCorruptFiles()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ not json");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Skipped unreadable saved room"));

            Assert.AreEqual(0, new SpaceLibrary(_folder).LoadAll().Count);
        }
    }
}
