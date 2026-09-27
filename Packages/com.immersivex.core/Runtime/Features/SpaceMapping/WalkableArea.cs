using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Where the user can walk: the floor inside the walls, minus a safety margin from the walls.
    /// Furniture is not subtracted (it's still used for physics). Stored as a small grid in floor-plane space,
    /// capped at 400 × 400 cells, so it's cheap to build once and cheap to query.
    /// </summary>
    public sealed class WalkableArea
    {
        public const float DefaultCellSize = 0.05f;
        const int MaxCellsPerSide = 400;

        readonly bool[] _cells;

        public Vector2 Origin { get; }
        public float CellSize { get; }
        public int Columns { get; }
        public int Rows { get; }
        public float Margin { get; }

        /// <summary>Walkable area in square metres.</summary>
        public float Area { get; }

        WalkableArea(Vector2 origin, float cellSize, int columns, int rows, float margin, bool[] cells, int walkableCells)
        {
            Origin = origin;
            CellSize = cellSize;
            Columns = columns;
            Rows = rows;
            Margin = margin;
            _cells = cells;
            Area = walkableCells * cellSize * cellSize;
        }

        /// <summary>Build from the floor polygon (floor-plane space) and the wall margin in metres.</summary>
        public static WalkableArea Build(Vector2[] floorPolygon, float margin, float cellSize = DefaultCellSize)
        {
            var bounds = Polygon.Bounds(floorPolygon);
            cellSize = Mathf.Max(cellSize, Mathf.Max(bounds.width, bounds.height) / MaxCellsPerSide);
            var columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / cellSize));
            var rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / cellSize));
            var cells = new bool[columns * rows];
            var walkable = 0;

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var centre = new Vector2(bounds.xMin + (column + 0.5f) * cellSize, bounds.yMin + (row + 0.5f) * cellSize);
                    if (Polygon.Contains(floorPolygon, centre) && Polygon.DistanceToEdges(floorPolygon, centre) >= margin)
                    {
                        cells[row * columns + column] = true;
                        walkable++;
                    }
                }
            }

            return new WalkableArea(bounds.min, cellSize, columns, rows, margin, cells, walkable);
        }

        /// <summary>True when a point in floor-plane space (x, z) is walkable.</summary>
        public bool Contains(Vector2 floorPoint)
        {
            var column = Mathf.FloorToInt((floorPoint.x - Origin.x) / CellSize);
            var row = Mathf.FloorToInt((floorPoint.y - Origin.y) / CellSize);
            return IsWalkable(column, row);
        }

        /// <summary>
        /// The middle of the walkable area in floor-plane space: the centroid of its cells, or the walkable cell nearest
        /// to it when the centroid falls outside (an L-shaped room, say). Null when nothing is walkable.
        /// </summary>
        public Vector2? Centre()
        {
            double sumColumns = 0, sumRows = 0;
            var cells = 0;
            for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
            {
                if (!IsWalkable(column, row))
                    continue;
                sumColumns += column + 0.5;
                sumRows += row + 0.5;
                cells++;
            }

            if (cells == 0)
                return null;

            var centroid = new Vector2((float)(sumColumns / cells), (float)(sumRows / cells));
            if (IsWalkable(Mathf.FloorToInt(centroid.x), Mathf.FloorToInt(centroid.y)))
                return Origin + centroid * CellSize;

            var best = Vector2.zero;
            var bestDistance = float.MaxValue;
            for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
            {
                if (!IsWalkable(column, row))
                    continue;
                var cell = new Vector2(column + 0.5f, row + 0.5f);
                var distance = (cell - centroid).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }

            return Origin + best * CellSize;
        }

        bool IsWalkable(int column, int row) =>
            column >= 0 && row >= 0 && column < Columns && row < Rows && _cells[row * Columns + column];

        /// <summary>
        /// The outline of the walkable area as straight segments in floor-plane space, with runs of cell edges merged.
        /// Used to draw the area on the floor.
        /// </summary>
        public List<(Vector2 from, Vector2 to)> Outline()
        {
            var segments = new List<(Vector2, Vector2)>();

            // Horizontal edges: between row r-1 and row r, where exactly one side is walkable.
            for (var row = 0; row <= Rows; row++)
            {
                var start = -1;
                for (var column = 0; column <= Columns; column++)
                {
                    var edge = column < Columns && IsWalkable(column, row) != IsWalkable(column, row - 1);
                    if (edge && start < 0)
                        start = column;
                    else if (!edge && start >= 0)
                    {
                        segments.Add((Corner(start, row), Corner(column, row)));
                        start = -1;
                    }
                }
            }

            // Vertical edges: between column c-1 and column c.
            for (var column = 0; column <= Columns; column++)
            {
                var start = -1;
                for (var row = 0; row <= Rows; row++)
                {
                    var edge = row < Rows && IsWalkable(column, row) != IsWalkable(column - 1, row);
                    if (edge && start < 0)
                        start = row;
                    else if (!edge && start >= 0)
                    {
                        segments.Add((Corner(column, start), Corner(column, row)));
                        start = -1;
                    }
                }
            }

            return segments;
        }

        Vector2 Corner(int column, int row) => Origin + new Vector2(column * CellSize, row * CellSize);
    }
}
