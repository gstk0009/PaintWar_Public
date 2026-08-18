using System.Collections.Generic;
using UnityEngine;

public enum GeneratedCellType : byte
{
    Empty,
    Breakable,
    Unbreakable
}

public sealed class MapGenerationLayout
{
    public readonly int GridSize;
    public readonly GeneratedCellType[] Cells;
    public readonly bool[] ContainsItem;
    public readonly bool[] PrimaryRoute;
    public readonly bool[] SecondaryRoute;
    public readonly List<Vector2Int> Spawns = new List<Vector2Int>(4);
    public MapGenerationReport Report;

    public MapGenerationLayout(int gridSize)
    {
        GridSize = gridSize;
        Cells = new GeneratedCellType[gridSize * gridSize];
        ContainsItem = new bool[Cells.Length];
        PrimaryRoute = new bool[Cells.Length];
        SecondaryRoute = new bool[Cells.Length];
    }

    public int Flat(Vector2Int p) { return p.x * GridSize + p.y; }
    public GeneratedCellType Get(Vector2Int p) { return Cells[Flat(p)]; }
    public bool InBounds(Vector2Int p) { return p.x >= 0 && p.y >= 0 && p.x < GridSize && p.y < GridSize; }
}

public sealed class MapGenerationReport
{
    public int RequestedSeed;
    public int UsedSeed;
    public int Attempts;
    public bool UsedFallback;
    public bool IsValid;
    public int BreakableCount;
    public int UnbreakableCount;
    public int ItemCount;
    public int WalkableCount;
    public int MinCenterDistance;
    public int MaxCenterDistance;
    public float CenterDistanceVariancePercent;
    public int MinNearestItemDistance;
    public int MaxNearestItemDistance;
    public readonly List<string> FailureReasons = new List<string>();
    public readonly List<string> AttemptFailureReasons = new List<string>();

    public override string ToString()
    {
        return "seed=" + RequestedSeed + " usedSeed=" + UsedSeed +
               " attempts=" + Attempts + " fallback=" + UsedFallback +
               " valid=" + IsValid + " breakable=" + BreakableCount +
               " unbreakable=" + UnbreakableCount + " items=" + ItemCount +
               " walkable=" + WalkableCount + " centerDistance=" +
               MinCenterDistance + "-" + MaxCenterDistance +
               " (" + CenterDistanceVariancePercent.ToString("0.##") + "%).";
    }
}
