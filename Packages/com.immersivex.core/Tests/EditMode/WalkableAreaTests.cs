using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class WalkableAreaTests
    {
        static readonly Vector2[] Square4m = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2) };

        // An L-shaped room: 4 × 4 m with a 2 × 2 m corner missing (12 m²).
        static readonly Vector2[] LShape =
        {
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 2), new Vector2(2, 2), new Vector2(2, 4), new Vector2(0, 4),
        };

        [Test]
        public void SquareRoom_LosesTheWallMarginOnEverySide()
        {
            var area = WalkableArea.Build(Square4m, margin: 0.3f);

            Assert.AreEqual(3.4f * 3.4f, area.Area, 0.35f); // 11.56 m², within one ring of 5 cm cells
            Assert.IsTrue(area.Contains(Vector2.zero));
            Assert.IsTrue(area.Contains(new Vector2(1.6f, 1.6f)));
            Assert.IsFalse(area.Contains(new Vector2(1.8f, 0f)), "0.2 m from a wall is inside the margin");
            Assert.IsFalse(area.Contains(new Vector2(5f, 0f)), "outside the room");
        }

        [Test]
        public void Centre_OfASquareRoomIsItsMiddle()
        {
            var centre = WalkableArea.Build(Square4m, margin: 0.3f).Centre();

            Assert.IsTrue(centre.HasValue);
            Assert.AreEqual(0f, centre.Value.x, 0.05f);
            Assert.AreEqual(0f, centre.Value.y, 0.05f);
        }

        [Test]
        public void Centre_OfAnLShapedRoomIsSomewhereYouCanStand()
        {
            var area = WalkableArea.Build(LShape, margin: 0.3f);
            var centre = area.Centre();

            Assert.IsTrue(centre.HasValue);
            Assert.IsTrue(area.Contains(centre.Value), $"{centre.Value} should be walkable");
        }

        [Test]
        public void Centre_IsNullWhenNothingIsWalkable()
        {
            var tiny = new[] { new Vector2(0, 0), new Vector2(0.4f, 0), new Vector2(0.4f, 0.4f), new Vector2(0, 0.4f) };
            Assert.IsNull(WalkableArea.Build(tiny, margin: 0.3f).Centre());
        }

        [Test]
        public void LShapedRoom_ExcludesTheMissingCorner()
        {
            var area = WalkableArea.Build(LShape, margin: 0.3f);

            Assert.IsTrue(area.Contains(new Vector2(1f, 1f)));
            Assert.IsTrue(area.Contains(new Vector2(3f, 1f)));
            Assert.IsTrue(area.Contains(new Vector2(1f, 3f)));
            Assert.IsFalse(area.Contains(new Vector2(3f, 3f)), "the missing corner");
            Assert.IsFalse(area.Contains(new Vector2(2.1f, 2.1f)), "too close to the inner corner");
            // Exact: two 3.4 × 1.4 m arms overlapping in 1.4 × 1.4 m (7.56 m²), plus the rounded region near the
            // inner corner that is still 0.3 m from every wall (0.09 − π·0.09/4 ≈ 0.02 m²) = 7.58 m².
            Assert.AreEqual(7.58f, area.Area, 0.2f);
        }

        [Test]
        public void ZeroMargin_CoversTheWholeFloor() =>
            Assert.AreEqual(16f, WalkableArea.Build(Square4m, margin: 0f).Area, 0.01f);

        [Test]
        public void HugeFloors_StayWithinTheGridCap()
        {
            var warehouse = new[] { new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40), new Vector2(0, 40) };

            var area = WalkableArea.Build(warehouse, margin: 0.3f);

            Assert.LessOrEqual(area.Columns, 400);
            Assert.LessOrEqual(area.Rows, 400);
            Assert.AreEqual(59.4f * 39.4f, area.Area, 60f);
        }

        [Test]
        public void Outline_OfASquareIsFourSides()
        {
            var outline = WalkableArea.Build(Square4m, margin: 0.3f).Outline();

            Assert.AreEqual(4, outline.Count);
            var perimeter = 0f;
            foreach (var (from, to) in outline)
                perimeter += Vector2.Distance(from, to);
            Assert.AreEqual(4f * 3.4f, perimeter, 0.5f);
        }
    }
}
