using System.Collections.Generic;
using UnityEngine;

// 봇이 무기를 배치할 만한 타일 후보에 점수를 매기고 이동할 가치가 있는 계획을 반환합니다.
// 적/플레이어를 많이 포함하는 배치를 선호하고, 자기 자신을 맞추는 배치는 피하며 phase별 zone 제한을 지킵니다.
public sealed class BotWeaponPlanner
{
    // 플레이어를 포함하는 배치는 가치가 큽니다. 특히 Battle에서는 무기 hit가 타일 주도권을 빠르게 바꿀 수 있습니다.
    private const int EnemyPlayerCoverageBattleScore = 48;
    private const int EnemyPlayerCoverageFarmingScore = 18;
    private const int SelfCoveragePenalty = 22;

    public struct Settings
    {
        public int FarmingSearchRadius;
        public int BattleRandomSamples;
        public int TopCandidatePickCount;
        public int MinimumScoreToPlace;
        public int AimRangeTiles;
    }

    private readonly PaintOwner owner;
    private readonly Transform transform;
    private readonly WeaponRuntimeController weaponController;
    private readonly Settings settings;
    // 봇이 배치 후보를 다시 계산할 때 반복 allocation을 피하기 위한 재사용 buffer입니다.
    private readonly List<BotWeaponPlan> candidates = new List<BotWeaponPlan>(256);
    private readonly List<BotWeaponPlan> viable = new List<BotWeaponPlan>(8);
    private readonly HashSet<Vector2Int> affectedIndices = new HashSet<Vector2Int>();

    public BotWeaponPlanner(PaintOwner owner, Transform transform, WeaponRuntimeController weaponController, Settings settings)
    {
        this.owner = owner;
        this.transform = transform;
        this.weaponController = weaponController;
        this.settings = settings;
    }

    public bool CanPlan()
    {
        // 장착 무기가 준비되어 있고 타일 데이터가 있을 때만 계획 계산이 의미 있습니다.
        return TilePaintManager.HasInstance
            && weaponController != null
            && weaponController.EquippedWeapon != null
            && weaponController.IsSkillReady;
    }

    public BotWeaponPlan SelectPlan(GamePhase phase)
    {
        // 봇들이 똑같이 움직이지 않도록 1등 계획만 고르지 않고 상위 유효 계획 중 선택합니다.
        if (!CanPlan())
        {
            return BotWeaponPlan.None;
        }

        List<BotWeaponPlan> plans = phase == GamePhase.Battle
            ? BuildBattlePlans()
            : BuildFarmingPlans();

        if (plans.Count == 0)
        {
            return BotWeaponPlan.None;
        }

        int minimumScore = Mathf.Max(1, settings.MinimumScoreToPlace);
        plans.Sort((left, right) => right.Score.CompareTo(left.Score));
        viable.Clear();
        int pickCount = Mathf.Max(1, settings.TopCandidatePickCount);
        for (int i = 0; i < plans.Count && viable.Count < pickCount; i++)
        {
            if (plans[i].Score >= minimumScore) viable.Add(plans[i]);
        }

        if (viable.Count == 0)
        {
            return BotWeaponPlan.None;
        }

        return viable[Random.Range(0, viable.Count)];
    }

    public bool ShouldPlaceAt(Vector2Int currentIndex, BotWeaponPlan plan, float arriveDistance)
    {
        // 봇은 계획된 타일 위에 있거나 world 위치가 충분히 가까우면 무기를 배치할 수 있습니다.
        if (!plan.IsValid || !CanPlan())
        {
            return false;
        }

        if (currentIndex == plan.TargetIndex)
        {
            return true;
        }

        if (TilePaintManager.Instance.IsTileDestroyed(plan.TargetIndex))
        {
            return false;
        }

        Vector3 target = TilePaintManager.Instance.GetTileCenterWorldPosition(plan.TargetIndex);
        Vector2 current2D = new Vector2(transform.position.x, transform.position.z);
        Vector2 target2D = new Vector2(target.x, target.z);
        return Vector2.Distance(current2D, target2D) <= arriveDistance;
    }

    private List<BotWeaponPlan> BuildFarmingPlans()
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        GameParticipant participant = GameRoundManager.HasInstance
            ? GameRoundManager.Instance.FindParticipant(owner)
            : null;
        FarmingZone zone = participant != null ? participant.Zone : null;
        candidates.Clear();
        List<BotWeaponPlan> plans = candidates;

        if (zone == null)
        {
            return plans;
        }

        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        int radius = Mathf.Max(1, settings.FarmingSearchRadius);
        for (int x = currentIndex.x - radius; x <= currentIndex.x + radius; x++)
        {
            for (int z = currentIndex.y - radius; z <= currentIndex.y + radius; z++)
            {
                Vector2Int index = new Vector2Int(x, z);
                if (tileManager.IsInsideGrid(index) &&
                    !tileManager.IsTileDestroyed(index) &&
                    zone.ContainsSafely(index))
                {
                    AddPlanIfUseful(plans, index, true, zone);
                }
            }
        }

        AddParticipantPressurePlans(plans, true, zone);
        return plans;
    }

    private List<BotWeaponPlan> BuildBattlePlans()
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        candidates.Clear();
        List<BotWeaponPlan> plans = candidates;
        int step = Mathf.Max(1, tileManager.GridSize / 24);

        for (int x = 0; x < tileManager.GridSize; x += step)
        {
            for (int z = 0; z < tileManager.GridSize; z += step)
            {
                AddPlanIfUseful(plans, new Vector2Int(x, z), false, null);
            }
        }

        int randomSamples = Mathf.Max(0, settings.BattleRandomSamples);
        for (int i = 0; i < randomSamples; i++)
        {
            AddPlanIfUseful(
                plans,
                new Vector2Int(Random.Range(0, tileManager.GridSize), Random.Range(0, tileManager.GridSize)),
                false,
                null);
        }

        AddParticipantPressurePlans(plans, false, null);
        return plans;
    }

    private void AddParticipantPressurePlans(List<BotWeaponPlan> plans, bool farming, FarmingZone zone)
    {
        // 봇이 타일뿐 아니라 전술적으로 무기를 쓰도록 적 플레이어 주변 후보를 추가합니다.
        if (!GameRoundManager.HasInstance || !TilePaintManager.HasInstance)
        {
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        WeaponData weapon = weaponController.EquippedWeapon;
        int radius = Mathf.Clamp((weapon != null ? weapon.Range : 1) + 2, 2, 6);

        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null || participant.IsEliminated || participant.Owner == owner || participant.Transform == null)
            {
                continue;
            }

            Vector2Int center = tileManager.GetGridIndex(participant.Transform.position);
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int z = center.y - radius; z <= center.y + radius; z++)
                {
                    Vector2Int index = new Vector2Int(x, z);
                    if (!tileManager.IsInsideGrid(index) ||
                        tileManager.IsTileDestroyed(index))
                    {
                        continue;
                    }

                    if (farming && (zone == null || !zone.ContainsSafely(index)))
                    {
                        continue;
                    }

                    AddPlanIfUseful(plans, index, farming, zone);
                }
            }
        }
    }

    private void AddPlanIfUseful(List<BotWeaponPlan> plans, Vector2Int placementIndex, bool farming, FarmingZone zone)
    {
        // placementIndex는 공격 대상이 아니라 봇이 실제로 이동해 설 위치입니다.
        // 장애물 칸을 계획으로 허용하면 봇이 그 벽을 향해 계속 이동하게 됩니다.
        if (!TilePaintManager.Instance.IsTileWalkable(placementIndex) ||
            BombMine.IsBlockingTile(placementIndex) ||
            BombMine.IsEnemyBlastDangerTile(placementIndex, owner))
        {
            return;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        int aimRange = Mathf.Max(1, settings.AimRangeTiles);
        if ((placementIndex - currentIndex).sqrMagnitude > aimRange * aimRange)
        {
            return;
        }

        int score = ScorePlacement(placementIndex, farming, zone);
        if (score > 0)
        {
            plans.Add(new BotWeaponPlan(placementIndex, score));
        }
    }

    private int ScorePlacement(Vector2Int placementIndex, bool farming, FarmingZone zone)
    {
        // 실제 무기 사용과 같은 pattern resolver로 영향 타일 각각의 점수를 계산합니다.
        TilePaintManager tileManager = TilePaintManager.Instance;
        WeaponData weapon = weaponController.EquippedWeapon;
        if (weapon == null)
        {
            return 0;
        }

        int score = 0;
        int affectedTiles = 0;
        int usefulTiles = 0;
        affectedIndices.Clear();
        int level = weaponController.LevelStats != null ? weaponController.LevelStats.Level : 1;
        Vector3 forward = EstimateForwardTo(placementIndex);

        foreach (Vector2Int offset in WeaponPatternResolver.GetOffsets(weapon, forward, level))
        {
            Vector2Int target = placementIndex + offset;
            if (!tileManager.IsInsideGrid(target))
            {
                score -= 6;
                continue;
            }

            if (tileManager.IsTileDestroyed(target))
            {
                score -= 8;
                continue;
            }

            if (farming && (zone == null || !zone.ContainsSafely(target)))
            {
                score -= 8;
                continue;
            }

            if (!WeaponPatternTraversal.CanReachOffset(placementIndex, offset))
            {
                if (WeaponPatternTraversal.IsUnbreakable(target))
                {
                    score -= 3;
                }
                continue;
            }

            if (GameRoundManager.HasInstance && !GameRoundManager.Instance.CanOwnerClaimTile(owner, target))
            {
                score -= 8;
                continue;
            }

            affectedIndices.Add(target);
            if (!tileManager.TryGetTileOwner(target, out PaintOwner tileOwner))
            {
                continue;
            }

            affectedTiles++;
            int tileScore = ScoreTargetTile(tileOwner, farming);
            if (WeaponPatternTraversal.IsBreakable(target))
            {
                tileScore += farming ? 7 : 11;
            }
            score += tileScore;
            if (tileScore > 0)
            {
                usefulTiles++;
            }
        }

        int participantScore = ScoreParticipantCoverage(affectedIndices, farming);
        if (affectedTiles == 0 || (usefulTiles == 0 && participantScore <= 0))
        {
            return 0;
        }

        score += participantScore;
        score += Mathf.Min(affectedTiles, 16) * (farming ? 1 : 2);
        score -= Mathf.RoundToInt(Vector2Int.Distance(tileManager.GetGridIndex(transform.position), placementIndex) * (farming ? 0.35f : 0.22f));
        score += Random.Range(0, 3);
        return score;
    }

    private int ScoreTargetTile(PaintOwner tileOwner, bool farming)
    {
        if (tileOwner == PaintOwner.Neutral)
        {
            return farming ? 12 : 8;
        }

        if (tileOwner == owner)
        {
            return farming ? -4 : -7;
        }

        return farming ? 20 : 32;
    }

    private int ScoreParticipantCoverage(HashSet<Vector2Int> affectedIndices, bool farming)
    {
        // 자기 자신을 맞추면 감점하고 적 포함은 가점합니다. 탈락 참가자는 owner 체크에서 제외됩니다.
        if (!GameRoundManager.HasInstance || !TilePaintManager.HasInstance || affectedIndices.Count == 0)
        {
            return 0;
        }

        int score = 0;
        Vector2Int selfIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (affectedIndices.Contains(selfIndex))
        {
            score -= SelfCoveragePenalty;
        }

        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null || participant.IsEliminated || participant.Transform == null)
            {
                continue;
            }

            Vector2Int participantIndex = TilePaintManager.Instance.GetGridIndex(participant.Transform.position);
            if (!affectedIndices.Contains(participantIndex))
            {
                continue;
            }

            if (participant.Owner == owner)
            {
                score -= SelfCoveragePenalty;
                continue;
            }

            score += farming ? EnemyPlayerCoverageFarmingScore : EnemyPlayerCoverageBattleScore;
        }

        return score;
    }

    private Vector3 EstimateForwardTo(Vector2Int placementIndex)
    {
        if (!TilePaintManager.HasInstance)
        {
            return transform.forward;
        }

        Vector3 target = TilePaintManager.Instance.GetTileCenterWorldPosition(placementIndex);
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
    }
}
