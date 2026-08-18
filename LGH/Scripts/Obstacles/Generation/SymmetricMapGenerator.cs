using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SymmetricMapGenerator
{
    private const int FallbackSeed = 0x2468ACE;
    private readonly MapGenerationValidator validator = new MapGenerationValidator();

    public MapGenerationLayout Generate(int seed, MapGenerationConfig sourceConfig)
    {
        MapGenerationConfig config = sourceConfig ?? MapGenerationConfig.CreateDefault();
        config.Sanitize(config.gridSize);
        MapGenerationReport lastReport = null;
        List<string> attemptFailures = new List<string>();

        for (int attempt = 1; attempt <= config.maxGenerationAttempts; attempt++)
        {
            int attemptSeed = unchecked(seed + attempt * 104729);
            MapGenerationLayout candidate = BuildCandidate(attemptSeed, config);
            MapGenerationReport report = validator.Validate(candidate, config);
            report.RequestedSeed = seed;
            report.UsedSeed = attemptSeed;
            report.Attempts = attempt;
            candidate.Report = report;
            if (report.IsValid)
            {
                report.AttemptFailureReasons.AddRange(attemptFailures);
                return candidate;
            }
            attemptFailures.Add("attempt " + attempt + " seed=" + attemptSeed + ": " +
                                string.Join(" | ", report.FailureReasons.ToArray()));
            lastReport = report;
        }

        MapGenerationLayout fallback = BuildCandidate(FallbackSeed, config);
        MapGenerationReport fallbackReport = validator.Validate(fallback, config);
        fallbackReport.RequestedSeed = seed;
        fallbackReport.UsedSeed = FallbackSeed;
        fallbackReport.Attempts = config.maxGenerationAttempts;
        fallbackReport.UsedFallback = true;
        fallbackReport.AttemptFailureReasons.AddRange(attemptFailures);
        fallback.Report = fallbackReport;
        if (!fallbackReport.IsValid && lastReport != null)
        {
            fallbackReport.FailureReasons.Insert(0, "Fallback invalid after last failure: " + string.Join(" | ", lastReport.FailureReasons));
        }
        return fallback;
    }

    private static MapGenerationLayout BuildCandidate(int seed, MapGenerationConfig config)
    {
        int size = config.gridSize;
        MapGenerationLayout layout = new MapGenerationLayout(size);
        System.Random random = new System.Random(seed);
        AddSpawns(layout);

        bool[] protectedEmpty = new bool[size * size];
        bool[] noPurple = new bool[size * size];
        SpawnSafetyZoneBuilder.Reserve(layout, protectedEmpty, noPurple);
        RoutePlanner.BuildTwoRoutes(layout, noPurple);

        List<Vector2Int> representatives = BuildRepresentatives(size);
        Shuffle(representatives, random);

        HashSet<int> grayReps = new HashSet<int>();
        CentralCrossBuilder.PlaceDividerBreakables(layout, config.encounterBarrierWidth, representatives, grayReps);
        CentralCrossBuilder.PlaceGateBreakables(layout, config.encounterBarrierWidth, representatives, grayReps);

        HashSet<int> purpleReps = new HashSet<int>();
        PlacePurpleSkeleton(layout, config, representatives, protectedEmpty, noPurple, purpleReps, random);

        SpawnSafetyZoneBuilder.PlaceBufferBreakables(layout, protectedEmpty, purpleReps, grayReps);
        SpawnSafetyZoneBuilder.LockBufferDensity(layout, protectedEmpty);
        RoutePlanner.PlaceAndLockCorridorBreakables(layout, config, representatives, protectedEmpty,
            purpleReps, grayReps, random);
        ObstacleClusterGenerator.PlaceBreakables(layout, config, representatives, protectedEmpty, purpleReps, grayReps, random);
        ItemQuotaDistributor.Distribute(layout, config, grayReps, random);
        return layout;
    }

    private static void AddSpawns(MapGenerationLayout layout)
    {
        RoundZoneLayout zones = RoundZoneLayoutBuilder.Build(4, layout.GridSize);
        for (int i = 0; i < zones.Zones.Count; i++) layout.Spawns.Add(zones.Zones[i].GetSpawnIndex(layout.GridSize));
    }

    private static List<Vector2Int> BuildRepresentatives(int size)
    {
        int half = size / 2;
        List<Vector2Int> result = new List<Vector2Int>(half * half);
        for (int x = 0; x < half; x++)
            for (int y = 0; y < half; y++)
                result.Add(new Vector2Int(x, y));
        return result;
    }

    private static void PlacePurpleSkeleton(MapGenerationLayout layout, MapGenerationConfig config,
        List<Vector2Int> reps, bool[] protectedEmpty, bool[] noPurple, HashSet<int> selected, System.Random random)
    {
        int unitsNeeded = config.unbreakableCount / 4;
        int crossUnits = Mathf.Clamp(config.crossUnbreakableCount / 4, 0, unitsNeeded);
        SelectUnits(layout, reps, selected, 1, delegate(Vector2Int p)
        {
            return CentralCrossBuilder.IsIntersection(layout.GridSize, config.encounterBarrierWidth, p) &&
                   OrbitAllowed(layout, p, protectedEmpty, noPurple, GeneratedCellType.Unbreakable);
        });
        SelectUnits(layout, reps, selected, crossUnits, delegate(Vector2Int p)
        {
            return CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p) &&
                   !CentralCrossBuilder.IsIntersection(layout.GridSize, config.encounterBarrierWidth, p) &&
                   !CentralCrossBuilder.IsGate(layout.GridSize, config.encounterBarrierWidth, p) &&
                   OrbitAllowed(layout, p, protectedEmpty, noPurple, GeneratedCellType.Unbreakable);
        });
        SelectUnits(layout, reps, selected, unitsNeeded, delegate(Vector2Int p)
        {
            return !CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p) &&
                   OrbitAllowed(layout, p, protectedEmpty, noPurple, GeneratedCellType.Unbreakable);
        });
    }

    internal static bool IsEncounterBarrierBand(MapGenerationConfig config, Vector2Int p)
    {
        return CentralCrossBuilder.IsInside(config.gridSize, config.encounterBarrierWidth, p);
    }

    internal static void SelectUnits(MapGenerationLayout layout, List<Vector2Int> reps, HashSet<int> selected,
        int targetUnits, Predicate<Vector2Int> predicate)
    {
        for (int i = 0; i < reps.Count && selected.Count < targetUnits; i++)
        {
            Vector2Int rep = reps[i];
            int key = layout.Flat(rep);
            if (selected.Contains(key) || !predicate(rep)) continue;
            ApplyOrbit(layout, rep, GeneratedCellType.Unbreakable);
            if (CreatesPurpleBlock(layout, rep))
            {
                ApplyOrbit(layout, rep, GeneratedCellType.Empty);
                continue;
            }
            selected.Add(key);
        }
    }

    internal static bool OrbitAllowed(MapGenerationLayout layout, Vector2Int rep, bool[] protectedEmpty,
        bool[] prohibited, GeneratedCellType requested)
    {
        Vector2Int p = rep;
        for (int i = 0; i < 4; i++)
        {
            int flat = layout.Flat(p);
            if (protectedEmpty[flat] || prohibited[flat] || layout.Cells[flat] != GeneratedCellType.Empty) return false;
            p = Rotate(p, layout.GridSize);
        }
        return true;
    }

    internal static void ApplyOrbit(MapGenerationLayout layout, Vector2Int rep, GeneratedCellType type)
    {
        Vector2Int p = rep;
        for (int i = 0; i < 4; i++)
        {
            layout.Cells[layout.Flat(p)] = type;
            p = Rotate(p, layout.GridSize);
        }
    }

    private static bool CreatesPurpleBlock(MapGenerationLayout layout, Vector2Int rep)
    {
        Vector2Int p = rep;
        for (int rotation = 0; rotation < 4; rotation++)
        {
            for (int x = Mathf.Max(0, p.x - 2); x <= Mathf.Min(layout.GridSize - 3, p.x); x++)
                for (int y = Mathf.Max(0, p.y - 2); y <= Mathf.Min(layout.GridSize - 3, p.y); y++)
                {
                    bool full = true;
                    for (int dx = 0; dx < 3 && full; dx++)
                        for (int dy = 0; dy < 3; dy++)
                            if (layout.Get(new Vector2Int(x + dx, y + dy)) != GeneratedCellType.Unbreakable) { full = false; break; }
                    if (full) return true;
                }
            p = Rotate(p, layout.GridSize);
        }
        return false;
    }

    public static Vector2Int Rotate(Vector2Int p, int size) { return new Vector2Int(size - 1 - p.y, p.x); }

    internal static Vector2Int Representative(Vector2Int p, int size)
    {
        Vector2Int best = p;
        for (int i = 0; i < 3; i++)
        {
            p = Rotate(p, size);
            if (p.x < best.x || (p.x == best.x && p.y < best.y)) best = p;
        }
        return best;
    }

    internal static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T temp = list[i]; list[i] = list[j]; list[j] = temp;
        }
    }
}

public static class SpawnSafetyZoneBuilder
{
    public static void Reserve(MapGenerationLayout layout, bool[] protectedEmpty, bool[] noPurple)
    {
        for (int i = 0; i < layout.Spawns.Count; i++)
        {
            Vector2Int spawn = layout.Spawns[i];
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                {
                    Vector2Int p = spawn + new Vector2Int(dx, dy);
                    if (!layout.InBounds(p)) continue;
                    noPurple[layout.Flat(p)] = true;
                    if (Mathf.Abs(dx) <= 2 && Mathf.Abs(dy) <= 2) protectedEmpty[layout.Flat(p)] = true;
                }
        }
    }

    public static void PlaceBufferBreakables(MapGenerationLayout layout, bool[] protectedEmpty,
        HashSet<int> purpleReps, HashSet<int> grayReps)
    {
        Vector2Int spawn = layout.Spawns[2];
        Vector2Int[] offsets =
        {
            new Vector2Int(-1, 3), new Vector2Int(0, 3),
            new Vector2Int(3, -1), new Vector2Int(3, 0)
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int p = spawn + offsets[i];
            Vector2Int rep = SymmetricMapGenerator.Representative(p, layout.GridSize);
            int key = layout.Flat(rep);
            if (purpleReps.Contains(key) || grayReps.Contains(key)) continue;
            SymmetricMapGenerator.ApplyOrbit(layout, rep, GeneratedCellType.Breakable);
            grayReps.Add(key);
        }
    }

    public static void LockBufferDensity(MapGenerationLayout layout, bool[] protectedEmpty)
    {
        for (int i = 0; i < layout.Spawns.Count; i++)
        {
            Vector2Int spawn = layout.Spawns[i];
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                {
                    Vector2Int p = spawn + new Vector2Int(dx, dy);
                    if (layout.InBounds(p) && layout.Get(p) == GeneratedCellType.Empty)
                        protectedEmpty[layout.Flat(p)] = true;
                }
        }
    }
}

public static class CentralCrossBuilder
{
    public static bool IsInside(int gridSize, int width, Vector2Int p)
    {
        int min = (gridSize - width) / 2;
        int max = min + width - 1;
        return (p.x >= min && p.x <= max) || (p.y >= min && p.y <= max);
    }

    public static bool IsIntersection(int gridSize, int width, Vector2Int p)
    {
        int min = (gridSize - width) / 2;
        int max = min + width - 1;
        return p.x >= min && p.x <= max && p.y >= min && p.y <= max;
    }

    public static bool IsGate(int gridSize, int width, Vector2Int p)
    {
        if (!IsInside(gridSize, width, p) || IsIntersection(gridSize, width, p)) return false;
        float center = (gridSize - 1) * 0.5f;
        float axialDistance = p.x >= (gridSize - width) / 2 && p.x < (gridSize + width) / 2
            ? Mathf.Abs(p.y - center)
            : Mathf.Abs(p.x - center);
        // Two independently breakable gates per arm: one 2 tiles thick and one 3 tiles thick.
        return (axialDistance >= 6.5f && axialDistance <= 7.5f) ||
               (axialDistance >= 12.5f && axialDistance <= 14.5f);
    }

    public static void PlaceGateBreakables(MapGenerationLayout layout, int width, List<Vector2Int> reps,
        HashSet<int> selected)
    {
        for (int i = 0; i < reps.Count; i++)
        {
            Vector2Int rep = reps[i];
            if (!IsGate(layout.GridSize, width, rep)) continue;
            int key = layout.Flat(rep);
            if (!selected.Add(key)) continue;
            SymmetricMapGenerator.ApplyOrbit(layout, rep, GeneratedCellType.Breakable);
        }
    }

    public static void PlaceDividerBreakables(MapGenerationLayout layout, int width, List<Vector2Int> reps,
        HashSet<int> selected)
    {
        int min = (layout.GridSize - width) / 2;
        int dividerMax = min + width / 2;
        int dividerMin = dividerMax - 1;
        for (int i = 0; i < reps.Count; i++)
        {
            Vector2Int rep = reps[i];
            if (rep.x < dividerMin || rep.x > dividerMax) continue;
            int key = layout.Flat(rep);
            if (!selected.Add(key)) continue;
            SymmetricMapGenerator.ApplyOrbit(layout, rep, GeneratedCellType.Breakable);
        }
    }
}

public static class CentralArenaBuilder
{
    public static bool IsInside(int gridSize, int arenaSize, Vector2Int p)
    {
        int min = (gridSize - arenaSize) / 2;
        return p.x >= min && p.y >= min && p.x < min + arenaSize && p.y < min + arenaSize;
    }

    public static void ReserveSafeCore(MapGenerationLayout layout, bool[] protectedEmpty, bool[] noPurple)
    {
        int min = layout.GridSize / 2 - 3;
        for (int x = min; x < min + 6; x++)
            for (int y = min; y < min + 6; y++)
            {
                int flat = layout.Flat(new Vector2Int(x, y));
                protectedEmpty[flat] = true;
                noPurple[flat] = true;
            }
    }
}

public static class RoutePlanner
{
    public static void BuildTwoRoutes(MapGenerationLayout layout, bool[] noPurple)
    {
        Vector2Int spawn = layout.Spawns[2];
        List<Vector2Int> primary = new List<Vector2Int>();
        List<Vector2Int> secondary = new List<Vector2Int>();
        AddRect(primary, spawn.x - 1, spawn.x, spawn.y + 3, 18);
        AddRect(primary, spawn.x - 1, 17, 17, 18);
        AddRect(secondary, spawn.x + 3, 18, spawn.y - 1, spawn.y);
        AddRect(secondary, 17, 18, spawn.y - 1, 17);
        MarkRotated(layout, primary, layout.PrimaryRoute, noPurple);
        MarkRotated(layout, secondary, layout.SecondaryRoute, noPurple);
    }

    private static void AddRect(List<Vector2Int> target, int minX, int maxX, int minY, int maxY)
    {
        for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++) target.Add(new Vector2Int(x, y));
    }

    private static void MarkRotated(MapGenerationLayout layout, List<Vector2Int> source, bool[] route, bool[] noPurple)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Vector2Int p = source[i];
            for (int r = 0; r < 4; r++)
            {
                int flat = layout.Flat(p); route[flat] = true; noPurple[flat] = true;
                p = SymmetricMapGenerator.Rotate(p, layout.GridSize);
            }
        }
    }

    public static void PlaceAndLockCorridorBreakables(MapGenerationLayout layout, MapGenerationConfig config,
        List<Vector2Int> reps, bool[] protectedEmpty, HashSet<int> purple, HashSet<int> gray, System.Random random)
    {
        int corridorCells = 0;
        for (int i = 0; i < layout.Cells.Length; i++)
        {
            Vector2Int p = new Vector2Int(i / layout.GridSize, i % layout.GridSize);
            if ((layout.PrimaryRoute[i] || layout.SecondaryRoute[i]) &&
                !CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p)) corridorCells++;
        }
        int targetUnits = Mathf.RoundToInt(corridorCells * config.corridorBreakableTarget / 4f);
        int currentUnits = 0;
        foreach (int key in gray)
        {
            Vector2Int rep = new Vector2Int(key / layout.GridSize, key % layout.GridSize);
            if ((layout.PrimaryRoute[key] || layout.SecondaryRoute[key]) &&
                !CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, rep)) currentUnits++;
        }
        List<Vector2Int> candidates = new List<Vector2Int>(reps);
        SymmetricMapGenerator.Shuffle(candidates, random);
        for (int i = 0; i < candidates.Count && currentUnits < targetUnits; i++)
        {
            Vector2Int rep = candidates[i]; int key = layout.Flat(rep);
            if ((!layout.PrimaryRoute[key] && !layout.SecondaryRoute[key]) ||
                CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, rep) ||
                gray.Contains(key) || purple.Contains(key)) continue;
            bool allowed = true; Vector2Int p = rep;
            for (int r = 0; r < 4; r++)
            {
                int flat = layout.Flat(p);
                if (protectedEmpty[flat] || layout.Cells[flat] != GeneratedCellType.Empty) { allowed = false; break; }
                p = SymmetricMapGenerator.Rotate(p, layout.GridSize);
            }
            if (!allowed) continue;
            SymmetricMapGenerator.ApplyOrbit(layout, rep, GeneratedCellType.Breakable);
            gray.Add(key); currentUnits++;
        }
        for (int i = 0; i < layout.Cells.Length; i++)
        {
            Vector2Int p = new Vector2Int(i / layout.GridSize, i % layout.GridSize);
            if ((layout.PrimaryRoute[i] || layout.SecondaryRoute[i]) &&
                !CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p) &&
                layout.Cells[i] == GeneratedCellType.Empty) protectedEmpty[i] = true;
        }
    }
}

public static class ObstacleClusterGenerator
{
    public static void PlaceBreakables(MapGenerationLayout layout, MapGenerationConfig config,
        List<Vector2Int> reps, bool[] protectedEmpty, HashSet<int> purpleReps,
        HashSet<int> selected, System.Random random)
    {
        int targetUnits = config.breakableCount / 4;
        int crossTargetUnits = Mathf.Clamp(config.crossBreakableCount / 4, 0, targetUnits);
        int intersectionCurrent = CountSelected(layout, selected, config, 0);
        SelectBreakables(layout, reps, selected, purpleReps, protectedEmpty,
            Mathf.Max(0, 4 - intersectionCurrent), 0, config, random);
        intersectionCurrent = CountSelected(layout, selected, config, 0);
        int crossCurrent = CountSelected(layout, selected, config, 1);
        SelectBreakables(layout, reps, selected, purpleReps, protectedEmpty,
            Mathf.Max(0, crossTargetUnits - intersectionCurrent - crossCurrent), 1, config, random);
        SelectBreakables(layout, reps, selected, purpleReps, protectedEmpty,
            targetUnits - selected.Count, 2, config, random);
    }

    private static int CountSelected(MapGenerationLayout layout, HashSet<int> selected,
        MapGenerationConfig config, int region)
    {
        int count = 0;
        foreach (int flat in selected)
        {
            Vector2Int p = new Vector2Int(flat / layout.GridSize, flat % layout.GridSize);
            if (MatchesRegion(layout, config, p, region)) count++;
        }
        return count;
    }

    private static void SelectBreakables(MapGenerationLayout layout, List<Vector2Int> reps,
        HashSet<int> selected, HashSet<int> purple, bool[] protectedEmpty, int amount,
        int region, MapGenerationConfig config, System.Random random)
    {
        int target = selected.Count + amount;
        List<Vector2Int> anchors = new List<Vector2Int>(reps);
        SymmetricMapGenerator.Shuffle(anchors, random);
        int cursor = 0;
        while (selected.Count < target && cursor < anchors.Count)
        {
            Vector2Int anchor = anchors[cursor++];
            if (!IsAllowed(layout, anchor, selected, purple, protectedEmpty, region, config)) continue;
            int remaining = target - selected.Count;
            int desired = remaining <= 6 ? remaining : random.Next(2, 7);
            if (remaining - desired == 1) desired++;
            List<Vector2Int> cluster = GrowCluster(layout, anchor, desired, selected, purple,
                protectedEmpty, region, config, random);
            for (int i = 0; i < cluster.Count && selected.Count < target; i++)
            {
                Vector2Int rep = cluster[i];
                int key = layout.Flat(rep);
                if (!selected.Add(key)) continue;
                SymmetricMapGenerator.ApplyOrbit(layout, rep, GeneratedCellType.Breakable);
            }
        }
    }

    private static List<Vector2Int> GrowCluster(MapGenerationLayout layout, Vector2Int anchor, int desired,
        HashSet<int> selected, HashSet<int> purple, bool[] protectedEmpty, int region,
        MapGenerationConfig config, System.Random random)
    {
        List<Vector2Int> cluster = new List<Vector2Int>(desired) { anchor };
        HashSet<int> local = new HashSet<int> { layout.Flat(anchor) };
        int cursor = 0;
        while (cluster.Count < desired && cursor < cluster.Count)
        {
            Vector2Int current = cluster[cursor++];
            List<Vector2Int> neighbors = new List<Vector2Int>(4)
            {
                current + Vector2Int.left, current + Vector2Int.right,
                current + Vector2Int.down, current + Vector2Int.up
            };
            SymmetricMapGenerator.Shuffle(neighbors, random);
            for (int i = 0; i < neighbors.Count && cluster.Count < desired; i++)
            {
                Vector2Int next = neighbors[i];
                if (next.x < 0 || next.y < 0 || next.x >= layout.GridSize / 2 || next.y >= layout.GridSize / 2) continue;
                int key = layout.Flat(next);
                if (local.Contains(key) || !IsAllowed(layout, next, selected, purple, protectedEmpty, region, config)) continue;
                local.Add(key); cluster.Add(next);
            }
        }
        return cluster;
    }

    private static bool IsAllowed(MapGenerationLayout layout, Vector2Int rep, HashSet<int> selected,
        HashSet<int> purple, bool[] protectedEmpty, int region, MapGenerationConfig config)
    {
        int key = layout.Flat(rep);
        if (selected.Contains(key) || purple.Contains(key) || !MatchesRegion(layout, config, rep, region)) return false;
        Vector2Int p = rep;
        for (int r = 0; r < 4; r++)
        {
            int flat = layout.Flat(p);
            if (protectedEmpty[flat] || layout.Cells[flat] != GeneratedCellType.Empty) return false;
            p = SymmetricMapGenerator.Rotate(p, layout.GridSize);
        }
        return true;
    }

    private static bool MatchesRegion(MapGenerationLayout layout, MapGenerationConfig config,
        Vector2Int p, int region)
    {
        bool cross = CentralCrossBuilder.IsInside(layout.GridSize, config.encounterBarrierWidth, p);
        if (region == 0) return CentralCrossBuilder.IsIntersection(layout.GridSize, config.encounterBarrierWidth, p);
        if (region == 1) return cross &&
            !CentralCrossBuilder.IsIntersection(layout.GridSize, config.encounterBarrierWidth, p);
        return !cross;
    }
}

public static class ItemQuotaDistributor
{
    public static void Distribute(MapGenerationLayout layout, MapGenerationConfig config,
        HashSet<int> grayReps, System.Random random)
    {
        List<Vector2Int> outer = new List<Vector2Int>();
        List<Vector2Int> common = new List<Vector2Int>();
        foreach (int flat in grayReps)
        {
            Vector2Int rep = new Vector2Int(flat / layout.GridSize, flat % layout.GridSize);
            if (CentralArenaBuilder.IsInside(layout.GridSize, config.centralArenaSize, rep) ||
                layout.PrimaryRoute[flat] || layout.SecondaryRoute[flat]) common.Add(rep);
            else outer.Add(rep);
        }
        SymmetricMapGenerator.Shuffle(outer, random);
        SymmetricMapGenerator.Shuffle(common, random);
        SelectItemOrbits(layout, outer, 9);
        SelectItemOrbits(layout, common, 2);
    }

    private static void SelectItemOrbits(MapGenerationLayout layout, List<Vector2Int> candidates, int count)
    {
        int selected = 0;
        for (int i = 0; i < candidates.Count && selected < count; i++)
        {
            Vector2Int p = candidates[i];
            bool adjacentItem = false;
            Vector2Int q = p;
            for (int r = 0; r < 4 && !adjacentItem; r++)
            {
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int n = q + Directions[d];
                    if (layout.InBounds(n) && layout.ContainsItem[layout.Flat(n)]) { adjacentItem = true; break; }
                }
                q = SymmetricMapGenerator.Rotate(q, layout.GridSize);
            }
            if (adjacentItem) continue;
            q = p;
            for (int r = 0; r < 4; r++) { layout.ContainsItem[layout.Flat(q)] = true; q = SymmetricMapGenerator.Rotate(q, layout.GridSize); }
            selected++;
        }
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
    };
}
