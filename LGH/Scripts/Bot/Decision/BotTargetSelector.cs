using System.Collections.Generic;
using UnityEngine;

// 봇이 다음에 이동할 타일을 선택합니다.
// Farming에서는 배정 구역 안을 찾고, Battle에서는 전체 맵 후보 중 점수가 높은 곳을 고릅니다.
public sealed class BotTargetSelector
{
    public struct Settings
    {
        public int FarmingSearchRadius;
        public int BattleRandomSamples;
        public int TopCandidatePickCount;
        public int TeleportStealSteps;
    }

    private readonly PaintOwner owner;
    private readonly Transform transform;
    private readonly PlayerStoredItemController itemController;
    private readonly Settings settings;
    private readonly BotTileScorer tileScorer;
    // 판단할 때마다 allocation이 생기지 않도록 재사용하는 후보 buffer입니다.
    private readonly List<ScoredTile> candidates = new List<ScoredTile>(256);
    private int reachableGridSize;
    private int reachableStamp;
    private int[] reachable;
    private int[] reachabilityQueue;
    private Vector2Int cachedReachabilityStart;
    private float reachabilityValidUntil;
    private const float ReachabilityCacheSeconds = 0.15f;

    public BotTargetSelector(PaintOwner owner, Transform transform, Settings settings,
        PlayerStoredItemController itemController = null)
    {
        this.owner = owner;
        this.transform = transform;
        this.settings = settings;
        this.itemController = itemController;
        tileScorer = new BotTileScorer(owner);
    }

    public Vector2Int SelectTarget(GamePhase phase)
    {
        // 여러 봇이 완전히 같은 경로를 따라가지 않도록 상위 후보 중 무작위로 고릅니다.
        if (!TilePaintManager.HasInstance)
        {
            return Vector2Int.zero;
        }

        BuildReachabilityMap();

        List<ScoredTile> available = phase == GamePhase.Battle
            ? BuildBattleCandidates()
            : BuildFarmingCandidates();

        if (available.Count == 0)
        {
            return TilePaintManager.Instance.GetGridIndex(transform.position);
        }

        int pickCount = Mathf.Max(1, settings.TopCandidatePickCount);
        int topCount = Mathf.Min(pickCount, available.Count);
        SelectTopCandidatesInPlace(available, topCount);
        return available[Random.Range(0, topCount)].Index;
    }

    private static void SelectTopCandidatesInPlace(List<ScoredTile> source, int count)
    {
        for (int selected = 0; selected < count; selected++)
        {
            int best = selected;
            for (int candidate = selected + 1; candidate < source.Count; candidate++)
            {
                if (source[candidate].Score > source[best].Score)
                {
                    best = candidate;
                }
            }

            if (best == selected)
            {
                continue;
            }

            ScoredTile swap = source[selected];
            source[selected] = source[best];
            source[best] = swap;
        }
    }

    private List<ScoredTile> BuildFarmingCandidates()
    {
        // Farming 목표는 봇에게 배정된 구역 안에 있어야 합니다.
        TilePaintManager tileManager = TilePaintManager.Instance;
        GameParticipant participant = GameRoundManager.Instance.FindParticipant(owner);
        FarmingZone allowedZone = participant != null ? participant.Zone : null;
        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        candidates.Clear();

        if (allowedZone == null)
        {
            return candidates;
        }

        int radius = Mathf.Max(1, settings.FarmingSearchRadius);
        for (int x = currentIndex.x - radius; x <= currentIndex.x + radius; x++)
        {
            for (int z = currentIndex.y - radius; z <= currentIndex.y + radius; z++)
            {
                Vector2Int index = new Vector2Int(x, z);
                if (!tileManager.IsTileWalkable(index) ||
                    !IsReachable(index) ||
                    !allowedZone.ContainsSafely(index))
                {
                    continue;
                }

                candidates.Add(new ScoredTile(index,
                    tileScorer.EvaluateTile(index, true, allowedZone) +
                    ScoreWallBreachPosition(index) + ScoreTeleportStealSetup(index)));
            }
        }

        if (candidates.Count == 0)
        {
            AddSparseZoneCandidates(tileManager, allowedZone, candidates);
        }

        return candidates;
    }

    private void AddSparseZoneCandidates(TilePaintManager tileManager, FarmingZone allowedZone, List<ScoredTile> candidates)
    {
        int step = Mathf.Max(1, tileManager.GridSize / 18);
        for (int x = 0; x < tileManager.GridSize; x += step)
        {
            for (int z = 0; z < tileManager.GridSize; z += step)
            {
                Vector2Int index = new Vector2Int(x, z);
                if (tileManager.IsTileWalkable(index) &&
                    IsReachable(index) &&
                    allowedZone.ContainsSafely(index))
                {
                    candidates.Add(new ScoredTile(index,
                        tileScorer.EvaluateTile(index, true, allowedZone) +
                        ScoreWallBreachPosition(index) + ScoreTeleportStealSetup(index)));
                }
            }
        }
    }

    private List<ScoredTile> BuildBattleCandidates()
    {
        // Battle mode는 예측 가능한 경로를 피하기 위해 성긴 grid sample과 무작위 sample을 섞습니다.
        TilePaintManager tileManager = TilePaintManager.Instance;
        candidates.Clear();
        int step = Mathf.Max(1, tileManager.GridSize / 24);

        for (int x = 0; x < tileManager.GridSize; x += step)
        {
            for (int z = 0; z < tileManager.GridSize; z += step)
            {
                Vector2Int index = new Vector2Int(x, z);
                if (!tileManager.IsTileWalkable(index))
                {
                    continue;
                }

                if (!IsReachable(index)) continue;

                candidates.Add(new ScoredTile(index,
                    tileScorer.EvaluateTile(index, false, null) +
                    ScoreWallBreachPosition(index) + ScoreTeleportStealSetup(index)));
            }
        }

        int randomSamples = Mathf.Max(0, settings.BattleRandomSamples);
        for (int i = 0; i < randomSamples; i++)
        {
            Vector2Int randomIndex = new Vector2Int(
                Random.Range(0, tileManager.GridSize),
                Random.Range(0, tileManager.GridSize));
            if (!tileManager.IsTileWalkable(randomIndex))
            {
                continue;
            }

            if (!IsReachable(randomIndex)) continue;

            candidates.Add(new ScoredTile(randomIndex,
                tileScorer.EvaluateTile(randomIndex, false, null) +
                ScoreWallBreachPosition(randomIndex) + ScoreTeleportStealSetup(randomIndex)));
        }

        return candidates;
    }

    private int ScoreWallBreachPosition(Vector2Int center)
    {
        if (itemController == null || !itemController.CanUseBombMineAbility) return 0;
        int radius = Mathf.Max(1, itemController.DefaultBombMineCrossRadius);
        int breakableDirections = 0;
        breakableDirections += HasBreakableInRay(center, Vector2Int.left, radius) ? 1 : 0;
        breakableDirections += HasBreakableInRay(center, Vector2Int.right, radius) ? 1 : 0;
        breakableDirections += HasBreakableInRay(center, Vector2Int.down, radius) ? 1 : 0;
        breakableDirections += HasBreakableInRay(center, Vector2Int.up, radius) ? 1 : 0;
        return Mathf.Min(2, breakableDirections) * 18;
    }

    private int ScoreTeleportStealSetup(Vector2Int index)
    {
        return settings.TeleportStealSteps > 0 &&
               BombMine.HasStealableMineOnTeleportLine(owner, index, settings.TeleportStealSteps)
            ? 30
            : 0;
    }

    private static bool HasBreakableInRay(Vector2Int center, Vector2Int direction, int radius)
    {
        for (int distance = 1; distance <= radius; distance++)
        {
            Vector2Int index = center + direction * distance;
            if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                WeaponPatternTraversal.IsUnbreakable(index)) return false;
            if (WeaponPatternTraversal.IsBreakable(index)) return true;
            if (BombMine.IsBlockingTile(index)) return false;
        }
        return false;
    }

    private void BuildReachabilityMap()
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        int size = tileManager.GridSize;
        Vector2Int start = tileManager.GetGridIndex(transform.position);
        if (reachable != null && reachableGridSize == size &&
            start == cachedReachabilityStart && Time.time < reachabilityValidUntil)
        {
            return;
        }

        int count = size * size;
        if (reachable == null || reachableGridSize != size)
        {
            reachableGridSize = size;
            reachable = new int[count];
            reachabilityQueue = new int[count];
            reachableStamp = 0;
        }
        reachableStamp++;
        if (reachableStamp == int.MaxValue)
        {
            System.Array.Clear(reachable, 0, reachable.Length);
            reachableStamp = 1;
        }

        if (!tileManager.IsInsideGrid(start)) return;
        cachedReachabilityStart = start;
        reachabilityValidUntil = Time.time + ReachabilityCacheSeconds;
        int head = 0, tail = 0;
        int startFlat = start.x * size + start.y;
        reachabilityQueue[tail++] = startFlat;
        reachable[startFlat] = reachableStamp;
        while (head < tail)
        {
            int flat = reachabilityQueue[head++];
            Vector2Int current = new Vector2Int(flat / size, flat % size);
            AddReachable(current + Vector2Int.left, tileManager, size, ref tail);
            AddReachable(current + Vector2Int.right, tileManager, size, ref tail);
            AddReachable(current + Vector2Int.down, tileManager, size, ref tail);
            AddReachable(current + Vector2Int.up, tileManager, size, ref tail);
        }
    }

    private void AddReachable(Vector2Int index, TilePaintManager tileManager, int size, ref int tail)
    {
        if (!tileManager.IsInsideGrid(index) || !tileManager.IsTileWalkable(index) ||
            BombMine.IsBlockingTile(index) || BombMine.IsEnemyBlastDangerTile(index, owner)) return;
        int flat = index.x * size + index.y;
        if (reachable[flat] == reachableStamp) return;
        reachable[flat] = reachableStamp;
        reachabilityQueue[tail++] = flat;
    }

    private bool IsReachable(Vector2Int index)
    {
        return index.x >= 0 && index.y >= 0 && index.x < reachableGridSize && index.y < reachableGridSize &&
               reachable[index.x * reachableGridSize + index.y] == reachableStamp;
    }

    private struct ScoredTile
    {
        public readonly Vector2Int Index;
        public readonly int Score;

        public ScoredTile(Vector2Int index, int score)
        {
            Index = index;
            Score = score;
        }
    }
}
