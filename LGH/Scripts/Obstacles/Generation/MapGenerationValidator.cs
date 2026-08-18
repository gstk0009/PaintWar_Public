using System.Collections.Generic;
using UnityEngine;

public sealed class MapGenerationValidator
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
    };

    public MapGenerationReport Validate(MapGenerationLayout layout, MapGenerationConfig config)
    {
        MapGenerationReport report = new MapGenerationReport();
        CountCells(layout, report);
        Require(report, report.BreakableCount == config.breakableCount,
            "Breakable count " + report.BreakableCount + " != " + config.breakableCount);
        Require(report, report.UnbreakableCount == config.unbreakableCount,
            "Unbreakable count " + report.UnbreakableCount + " != " + config.unbreakableCount);
        Require(report, report.ItemCount == config.containedItemCount,
            "Item count " + report.ItemCount + " != " + config.containedItemCount);
        Require(report, report.WalkableCount == layout.Cells.Length - config.breakableCount - config.unbreakableCount,
            "Walkable count mismatch: " + report.WalkableCount);

        ValidateSymmetry(layout, report);
        ValidateSafetyZones(layout, report);
        ValidateInitialSeparation(layout, report);
        ValidateRoutes(layout, report);
        ValidateRegionalDensities(layout, config, report);
        ValidatePurpleTopology(layout, report);
        ValidateNoLongSingleTileChokepoint(layout, report);
        ValidateConnectivityAndFairness(layout, report);
        ValidateItems(layout, config, report);
        report.IsValid = report.FailureReasons.Count == 0;
        return report;
    }

    private static void CountCells(MapGenerationLayout layout, MapGenerationReport report)
    {
        for (int i = 0; i < layout.Cells.Length; i++)
        {
            if (layout.Cells[i] == GeneratedCellType.Breakable) report.BreakableCount++;
            else if (layout.Cells[i] == GeneratedCellType.Unbreakable) report.UnbreakableCount++;
            else report.WalkableCount++;
            if (layout.ContainsItem[i]) report.ItemCount++;
            if (layout.ContainsItem[i] && layout.Cells[i] != GeneratedCellType.Breakable)
                report.FailureReasons.Add("Item exists outside a breakable tile at flat=" + i);
        }
    }

    private static void ValidateSymmetry(MapGenerationLayout layout, MapGenerationReport report)
    {
        for (int x = 0; x < layout.GridSize; x++)
            for (int y = 0; y < layout.GridSize; y++)
            {
                Vector2Int p = new Vector2Int(x, y);
                Vector2Int r = SymmetricMapGenerator.Rotate(p, layout.GridSize);
                int a = layout.Flat(p); int b = layout.Flat(r);
                if (layout.Cells[a] != layout.Cells[b] || layout.ContainsItem[a] != layout.ContainsItem[b])
                {
                    report.FailureReasons.Add("90-degree rotational hash mismatch at " + p);
                    return;
                }
            }
    }

    private static void ValidateSafetyZones(MapGenerationLayout layout, MapGenerationReport report)
    {
        for (int i = 0; i < layout.Spawns.Count; i++)
        {
            Vector2Int spawn = layout.Spawns[i];
            int bufferGray = 0;
            bool north = false, south = false, east = false, west = false;
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                {
                    Vector2Int p = spawn + new Vector2Int(dx, dy);
                    if (!layout.InBounds(p)) continue;
                    GeneratedCellType type = layout.Get(p);
                    if (Mathf.Abs(dx) <= 2 && Mathf.Abs(dy) <= 2 && type != GeneratedCellType.Empty)
                        report.FailureReasons.Add("Spawn 5x5 is obstructed for player " + (i + 1));
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) <= 3)
                    {
                        if (type == GeneratedCellType.Unbreakable)
                            report.FailureReasons.Add("Unbreakable obstacle inside spawn 7x7 for player " + (i + 1));
                        if (type == GeneratedCellType.Breakable) bufferGray++;
                    }
                }
            for (int offset = -1; offset <= 1; offset++)
            {
                north |= IsNonPurple(layout, spawn + new Vector2Int(offset, 3));
                south |= IsNonPurple(layout, spawn + new Vector2Int(offset, -3));
                east |= IsNonPurple(layout, spawn + new Vector2Int(3, offset));
                west |= IsNonPurple(layout, spawn + new Vector2Int(-3, offset));
            }
            int exits = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);
            Require(report, exits >= 2, "Player " + (i + 1) + " has fewer than two spawn exits");
            Require(report, bufferGray <= 4,
                "Player " + (i + 1) + " spawn 7x7 gray density exceeds 10%: " + bufferGray + "/49");
        }

    }

    private static void ValidateInitialSeparation(MapGenerationLayout layout, MapGenerationReport report)
    {
        bool[] reachable = FloodByCellType(layout, layout.Spawns[0], GeneratedCellType.Empty);
        for (int i = 1; i < layout.Spawns.Count; i++)
            Require(report, !reachable[layout.Flat(layout.Spawns[i])],
                "Initial empty-only path connects player 1 safe zone to player " + (i + 1));
    }

    private static void ValidateRoutes(MapGenerationLayout layout, MapGenerationReport report)
    {
        int primaryCount = 0, secondaryCount = 0;
        bool overlapOutsideCenter = false;
        for (int i = 0; i < layout.Cells.Length; i++)
        {
            if (layout.PrimaryRoute[i]) primaryCount++;
            if (layout.SecondaryRoute[i]) secondaryCount++;
            if ((layout.PrimaryRoute[i] || layout.SecondaryRoute[i]) && layout.Cells[i] == GeneratedCellType.Unbreakable)
                report.FailureReasons.Add("Reserved central route is blocked by purple at flat=" + i);
            if (layout.PrimaryRoute[i] && layout.SecondaryRoute[i])
            {
                Vector2Int p = new Vector2Int(i / layout.GridSize, i % layout.GridSize);
                if (!CentralArenaBuilder.IsInside(layout.GridSize, 16, p)) overlapOutsideCenter = true;
            }
        }
        Require(report, primaryCount > 0 && secondaryCount > 0, "Two central routes were not generated");
        Require(report, !overlapOutsideCenter, "The two central routes merge before the central arena");
    }

    private static void ValidatePurpleTopology(MapGenerationLayout layout, MapGenerationReport report)
    {
        for (int x = 0; x <= layout.GridSize - 3; x++)
            for (int y = 0; y <= layout.GridSize - 3; y++)
            {
                bool full = true;
                for (int dx = 0; dx < 3 && full; dx++)
                    for (int dy = 0; dy < 3; dy++)
                        if (layout.Get(new Vector2Int(x + dx, y + dy)) != GeneratedCellType.Unbreakable) { full = false; break; }
                if (full)
                {
                    report.FailureReasons.Add("Forbidden full 3x3 purple block at " + new Vector2Int(x, y));
                    return;
                }
            }
    }

    private static void ValidateRegionalDensities(MapGenerationLayout layout, MapGenerationConfig config,
        MapGenerationReport report)
    {
        int crossTotal = 0, crossObstacles = 0, outerTotal = 0, outerObstacles = 0;
        int intersectionTotal = 0, intersectionObstacles = 0;
        int corridorTotal = 0, corridorGray = 0;
        for (int i = 0; i < layout.Cells.Length; i++)
        {
            Vector2Int p = new Vector2Int(i / layout.GridSize, i % layout.GridSize);
            bool cross = CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p);
            if (cross)
            {
                crossTotal++;
                if (layout.Cells[i] != GeneratedCellType.Empty) crossObstacles++;
                if (CentralCrossBuilder.IsIntersection(layout.GridSize, config.encounterBarrierWidth, p))
                {
                    intersectionTotal++;
                    if (layout.Cells[i] != GeneratedCellType.Empty) intersectionObstacles++;
                }
            }
            else
            {
                outerTotal++;
                if (layout.Cells[i] != GeneratedCellType.Empty) outerObstacles++;
            }
            if (!cross && (layout.PrimaryRoute[i] || layout.SecondaryRoute[i]))
            {
                corridorTotal++;
                if (layout.Cells[i] == GeneratedCellType.Breakable) corridorGray++;
            }
        }
        float crossRate = crossObstacles / (float)Mathf.Max(1, crossTotal);
        float outerRate = outerObstacles / (float)Mathf.Max(1, outerTotal);
        float intersectionRate = intersectionObstacles / (float)Mathf.Max(1, intersectionTotal);
        float corridorGrayRate = corridorGray / (float)Mathf.Max(1, corridorTotal);
        Require(report, crossObstacles >= 360 && crossObstacles <= 390,
            "Central cross obstacle count outside 360-390: " + crossObstacles);
        Require(report, crossRate >= 0.62f && crossRate <= 0.68f,
            "Central cross density outside 62-68%: " + (crossRate * 100f).ToString("0.##") + "%");
        Require(report, outerRate >= 0.18f && outerRate <= 0.22f,
            "Outer quadrant density outside 18-22%: " + (outerRate * 100f).ToString("0.##") + "%");
        Require(report, intersectionRate >= 0.45f && intersectionRate <= 0.60f,
            "Central intersection density outside 45-60%: " + (intersectionRate * 100f).ToString("0.##") + "%");
        Require(report, corridorGrayRate >= 0.18f && corridorGrayRate <= 0.22f,
            "Corridor gray density outside 18-22%: " + (corridorGrayRate * 100f).ToString("0.##") + "%");
        ValidateCrossGates(layout, config, report);
    }

    private static void ValidateCrossGates(MapGenerationLayout layout, MapGenerationConfig config,
        MapGenerationReport report)
    {
        int gateCells = 0;
        for (int x = 0; x < layout.GridSize; x++)
        for (int y = 0; y < layout.GridSize; y++)
        {
            Vector2Int p = new Vector2Int(x, y);
            if (!CentralCrossBuilder.IsGate(layout.GridSize, config.encounterBarrierWidth, p)) continue;
            gateCells++;
            Require(report, layout.Get(p) == GeneratedCellType.Breakable,
                "Cross gate is not breakable at " + p);
        }
        Require(report, gateCells >= 96, "Cross has fewer than two 2+ tile gates per arm: " + gateCells);
    }

    private static void ValidateNoLongSingleTileChokepoint(MapGenerationLayout layout, MapGenerationReport report)
    {
        int horizontalRun = 0;
        for (int y = 1; y < layout.GridSize - 1; y++)
        {
            horizontalRun = 0;
            for (int x = 0; x < layout.GridSize; x++)
            {
                Vector2Int p = new Vector2Int(x, y);
                bool narrow = layout.Get(p) != GeneratedCellType.Unbreakable &&
                              layout.Get(new Vector2Int(x, y - 1)) == GeneratedCellType.Unbreakable &&
                              layout.Get(new Vector2Int(x, y + 1)) == GeneratedCellType.Unbreakable;
                horizontalRun = narrow ? horizontalRun + 1 : 0;
                if (horizontalRun > 3)
                {
                    report.FailureReasons.Add("Horizontal one-tile chokepoint exceeds 3 tiles near " + p);
                    return;
                }
            }
        }
        for (int x = 1; x < layout.GridSize - 1; x++)
        {
            int verticalRun = 0;
            for (int y = 0; y < layout.GridSize; y++)
            {
                Vector2Int p = new Vector2Int(x, y);
                bool narrow = layout.Get(p) != GeneratedCellType.Unbreakable &&
                              layout.Get(new Vector2Int(x - 1, y)) == GeneratedCellType.Unbreakable &&
                              layout.Get(new Vector2Int(x + 1, y)) == GeneratedCellType.Unbreakable;
                verticalRun = narrow ? verticalRun + 1 : 0;
                if (verticalRun > 3)
                {
                    report.FailureReasons.Add("Vertical one-tile chokepoint exceeds 3 tiles near " + p);
                    return;
                }
            }
        }
    }

    private static void ValidateConnectivityAndFairness(MapGenerationLayout layout, MapGenerationReport report)
    {
        bool[] visited = FloodNonPurple(layout, layout.Spawns[0]);
        for (int i = 0; i < layout.Cells.Length; i++)
            if (layout.Cells[i] != GeneratedCellType.Unbreakable && !visited[i])
            {
                report.FailureReasons.Add("Purple skeleton isolates a traversable area at flat=" + i);
                break;
            }

        int[] centerDistances = new int[layout.Spawns.Count];
        report.MinCenterDistance = int.MaxValue;
        for (int i = 0; i < layout.Spawns.Count; i++)
        {
            centerDistances[i] = DistanceToCenter(layout, layout.Spawns[i]);
            report.MinCenterDistance = Mathf.Min(report.MinCenterDistance, centerDistances[i]);
            report.MaxCenterDistance = Mathf.Max(report.MaxCenterDistance, centerDistances[i]);
            Require(report, centerDistances[i] >= 0, "Player " + (i + 1) + " cannot reach center after gray removal");
        }
        if (report.MinCenterDistance == int.MaxValue) report.MinCenterDistance = -1;
        if (report.MinCenterDistance > 0)
            report.CenterDistanceVariancePercent = (report.MaxCenterDistance - report.MinCenterDistance) * 100f / report.MinCenterDistance;
        Require(report, report.CenterDistanceVariancePercent <= 10f,
            "Center shortest-path variance exceeds 10%: " + report.CenterDistanceVariancePercent.ToString("0.##"));
    }

    private static void ValidateItems(MapGenerationLayout layout, MapGenerationConfig config, MapGenerationReport report)
    {
        int[] outerQuota = new int[4];
        int common = 0;
        for (int x = 0; x < layout.GridSize; x++)
            for (int y = 0; y < layout.GridSize; y++)
            {
                Vector2Int p = new Vector2Int(x, y);
                int flat = layout.Flat(p);
                if (!layout.ContainsItem[flat]) continue;
                if (CentralArenaBuilder.IsInside(layout.GridSize, config.centralArenaSize, p) ||
                    layout.PrimaryRoute[flat] || layout.SecondaryRoute[flat]) common++;
                else outerQuota[Quadrant(p, layout.GridSize)]++;
            }
        for (int i = 0; i < 4; i++) Require(report, outerQuota[i] == 9,
            "Player " + (i + 1) + " outer item quota " + outerQuota[i] + " != 9");
        Require(report, common == 8, "Center/common corridor item quota " + common + " != 8");

        report.MinNearestItemDistance = int.MaxValue;
        for (int i = 0; i < layout.Spawns.Count; i++)
        {
            int nearest = NearestItemDistance(layout, layout.Spawns[i]);
            report.MinNearestItemDistance = Mathf.Min(report.MinNearestItemDistance, nearest);
            report.MaxNearestItemDistance = Mathf.Max(report.MaxNearestItemDistance, nearest);
        }
        Require(report, report.MaxNearestItemDistance - report.MinNearestItemDistance <= 2,
            "Nearest item distance difference exceeds 2 tiles");
    }

    private static int Quadrant(Vector2Int p, int size)
    {
        int half = size / 2;
        if (p.y >= half) return p.x < half ? 0 : 1;
        return p.x < half ? 2 : 3;
    }

    private static bool[] FloodNonPurple(MapGenerationLayout layout, Vector2Int start)
    {
        bool[] visited = new bool[layout.Cells.Length];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        visited[layout.Flat(start)] = true; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int n = p + Directions[i];
                if (!layout.InBounds(n)) continue;
                int flat = layout.Flat(n);
                if (visited[flat] || layout.Cells[flat] == GeneratedCellType.Unbreakable) continue;
                visited[flat] = true; queue.Enqueue(n);
            }
        }
        return visited;
    }

    private static bool[] FloodByCellType(MapGenerationLayout layout, Vector2Int start,
        GeneratedCellType allowed)
    {
        bool[] visited = new bool[layout.Cells.Length];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        if (layout.Get(start) != allowed) return visited;
        visited[layout.Flat(start)] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int n = p + Directions[i];
                if (!layout.InBounds(n)) continue;
                int flat = layout.Flat(n);
                if (visited[flat] || layout.Cells[flat] != allowed) continue;
                visited[flat] = true;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    private static int DistanceToCenter(MapGenerationLayout layout, Vector2Int start)
    {
        int[] distances = NewDistances(layout.Cells.Length);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        distances[layout.Flat(start)] = 0; queue.Enqueue(start);
        int coreMin = layout.GridSize / 2 - 3;
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue(); int distance = distances[layout.Flat(p)];
            if (p.x >= coreMin && p.x < coreMin + 6 && p.y >= coreMin && p.y < coreMin + 6) return distance;
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int n = p + Directions[i];
                if (!layout.InBounds(n)) continue;
                int flat = layout.Flat(n);
                if (distances[flat] >= 0 || layout.Cells[flat] == GeneratedCellType.Unbreakable) continue;
                distances[flat] = distance + 1; queue.Enqueue(n);
            }
        }
        return -1;
    }

    private static int NearestItemDistance(MapGenerationLayout layout, Vector2Int start)
    {
        int[] distances = NewDistances(layout.Cells.Length);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        distances[layout.Flat(start)] = 0; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue(); int flat = layout.Flat(p);
            if (layout.ContainsItem[flat]) return distances[flat];
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int n = p + Directions[i];
                if (!layout.InBounds(n)) continue;
                int nf = layout.Flat(n);
                if (distances[nf] >= 0 || layout.Cells[nf] == GeneratedCellType.Unbreakable) continue;
                distances[nf] = distances[flat] + 1; queue.Enqueue(n);
            }
        }
        return int.MaxValue / 2;
    }

    private static int[] NewDistances(int count)
    {
        int[] values = new int[count];
        for (int i = 0; i < values.Length; i++) values[i] = -1;
        return values;
    }

    private static bool IsNonPurple(MapGenerationLayout layout, Vector2Int p)
    {
        return layout.InBounds(p) && layout.Get(p) != GeneratedCellType.Unbreakable;
    }

    private static void Require(MapGenerationReport report, bool condition, string failure)
    {
        if (!condition && !report.FailureReasons.Contains(failure)) report.FailureReasons.Add(failure);
    }
}
