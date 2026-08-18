using UnityEngine;

public sealed class BotTacticalAbilityHelper
{
    private const float EnemyNearRadius = 5f;

    public struct Settings
    {
        public float BombMineCooldownSeconds;
        public float BombMineEnemyTriggerTiles;
        public float BombMineStuckTriggerMultiplier;
        public int BombMineMinimumTacticalScore;
        public float BombMineComboChance;
        public float BombMineThreeComboChance;
        public float BombMineComboPlacementInterval;
    }

    private readonly Transform transform;
    private readonly PaintOwner owner;
    private readonly PlayerStoredItemController itemController;
    private readonly BotTeleportAbility teleportAbility;
    private readonly Settings settings;
    private float nextBombMineUseAt;
    private bool placedOpeningPhaseMine;
    private GamePhase openingMinePhase = GamePhase.MatchWaiting;
    private readonly Vector2Int[] comboPositions = new Vector2Int[3];
    private int comboPositionCount;
    private int nextComboPosition;
    private Vector2Int comboEscapeTarget;
    private float nextComboPlacementAt;
    private float comboAbortAt;
    private bool comboEscaping;
    private int[] comboVisitStamp;
    private int[] comboQueue;
    private int comboStamp;

    public bool ControlsBombMineMovement
    {
        get { return comboPositionCount > 0; }
    }

    public Vector2Int BombMineMovementTarget
    {
        get
        {
            return comboEscaping || nextComboPosition >= comboPositionCount
                ? comboEscapeTarget
                : comboPositions[nextComboPosition];
        }
    }

    public BotTacticalAbilityHelper(
        Transform transform,
        PaintOwner owner,
        PlayerStoredItemController itemController,
        BotTeleportAbility teleportAbility,
        Settings settings)
    {
        this.transform = transform;
        this.owner = owner;
        this.itemController = itemController;
        this.teleportAbility = teleportAbility;
        this.settings = NormalizeSettings(settings);
    }

    public bool TryUseTacticalItem(GamePhase phase)
    {
        if (transform == null || itemController == null || !itemController.HasItem)
        {
            return false;
        }

        Vector3 enemyPosition;
        bool hasEnemy = TryFindNearestEnemy(out enemyPosition);
        Vector3 direction = hasEnemy
            ? enemyPosition - transform.position
            : transform.forward;
        direction.y = 0f;

        switch (itemController.HeldItemType)
        {
            case DropItemEffectType.ReverseControlsBomb:
                if (hasEnemy)
                {
                    float radius = Mathf.Max(1f, itemController.HeldRadius);
                    if ((enemyPosition - transform.position).sqrMagnitude <= radius * radius)
                    {
                        itemController.UseHeldItem(direction);
                        return true;
                    }
                }
                break;
            case DropItemEffectType.DecoyStamp:
                if (phase == GamePhase.Battle || IsStandingOnEnemyTile())
                {
                    itemController.UseHeldItem(direction);
                    return true;
                }
                break;
        }

        return false;
    }

    public bool TryUseBombMine(GamePhase phase, bool isStuck)
    {
        if (transform == null ||
            itemController == null ||
            !TilePaintManager.HasInstance ||
            (phase != GamePhase.Farming && phase != GamePhase.Battle))
        {
            ResetForRound();
            placedOpeningPhaseMine = false;
            openingMinePhase = phase;
            return false;
        }

        if (openingMinePhase != phase)
        {
            openingMinePhase = phase;
            placedOpeningPhaseMine = false;
        }

        if (teleportAbility != null && teleportAbility.CooldownRemaining <= 0f &&
            BombMine.TryFindStealableMineOnTeleportLine(
                owner, transform.position, teleportAbility.TileSteps, out Vector3 _))
        {
            return false;
        }

        float now = Time.time;
        if (comboPositionCount > 0)
        {
            return TickBombMineCombo(now);
        }

        if (!itemController.CanUseBombMineAbility)
        {
            return false;
        }

        if (now < nextBombMineUseAt)
        {
            return false;
        }

        if (!BombMine.CanPlace(transform.position, owner))
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        if (!HasEscapeTile(currentIndex))
        {
            return false;
        }

        bool hasEnemy = TryFindNearestEnemy(out Vector3 enemyPosition);
        float triggerDistance = Mathf.Max(1f, tileManager.TileSize * settings.BombMineEnemyTriggerTiles);
        float enemySqrDistance = hasEnemy
            ? (enemyPosition - transform.position).sqrMagnitude
            : float.MaxValue;
        bool openingPhaseTrap = !placedOpeningPhaseMine;
        int tacticalScore = ScoreBombMinePlacement(
            phase,
            currentIndex,
            hasEnemy,
            enemyPosition,
            enemySqrDistance,
            triggerDistance,
            isStuck,
            openingPhaseTrap);
        if (tacticalScore < settings.BombMineMinimumTacticalScore)
        {
            return false;
        }

        bool hasComboPlan = TryBuildBombMineCombo(currentIndex, tacticalScore);
        Vector3 direction = hasEnemy
            ? enemyPosition - transform.position
            : transform.forward;
        direction.y = 0f;
        if (!itemController.TryUseBombMineAbility(direction))
        {
            ClearBombMineCombo();
            return false;
        }

        if (openingPhaseTrap)
        {
            placedOpeningPhaseMine = true;
        }

        if (hasComboPlan)
        {
            nextComboPosition = 1;
            nextComboPlacementAt = now + settings.BombMineComboPlacementInterval;
            comboAbortAt = now + 3.5f;
        }
        else
        {
            nextBombMineUseAt = now + settings.BombMineCooldownSeconds + Random.Range(0f, 1.5f);
        }
        return true;
    }

    public void ResetForRound()
    {
        ClearBombMineCombo();
        nextBombMineUseAt = 0f;
        placedOpeningPhaseMine = false;
        openingMinePhase = GamePhase.MatchWaiting;
    }

    private bool TickBombMineCombo(float now)
    {
        if (!TilePaintManager.HasInstance || transform == null || itemController == null)
        {
            ClearBombMineCombo();
            return false;
        }

        Vector2Int current = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (comboEscaping)
        {
            if (current == comboEscapeTarget || now >= comboAbortAt + 3f)
            {
                FinishBombMineCombo(now);
            }

            return false;
        }

        if (now >= comboAbortAt || !itemController.CanUseBombMineAbility)
        {
            BeginComboEscape(now);
            return false;
        }

        Vector2Int placement = comboPositions[nextComboPosition];
        if (current != placement || now < nextComboPlacementAt)
        {
            return false;
        }

        if (!BombMine.CanPlace(
                TilePaintManager.Instance.GetTileCenterWorldPosition(placement),
                owner,
                itemController.DefaultBombMineMaxCount) ||
            !HasEscapeTile(placement))
        {
            BeginComboEscape(now);
            return false;
        }

        Vector3 direction = transform.forward;
        if (nextComboPosition + 1 < comboPositionCount)
        {
            Vector2Int delta = comboPositions[nextComboPosition + 1] - placement;
            direction = new Vector3(delta.x, 0f, delta.y);
        }

        if (!itemController.TryUseBombMineAbility(direction))
        {
            BeginComboEscape(now);
            return false;
        }

        nextComboPosition++;
        nextComboPlacementAt = now + settings.BombMineComboPlacementInterval;
        if (nextComboPosition >= comboPositionCount)
        {
            BeginComboEscape(now);
        }

        return true;
    }

    private bool TryBuildBombMineCombo(Vector2Int current, int tacticalScore)
    {
        ClearBombMineCombo();
        int available = Mathf.Min(itemController.BombMineCharges, itemController.DefaultBombMineMaxCount);
        float scoreBonus = Mathf.Clamp01((tacticalScore - settings.BombMineMinimumTacticalScore) / 30f) * 0.2f;
        if (available < 2 || Random.value > settings.BombMineComboChance + scoreBonus)
        {
            return false;
        }

        int desiredCount = available >= 3 && Random.value < settings.BombMineThreeComboChance
            ? 3
            : 2;
        int directionOffset = Random.Range(0, 4);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2Int direction = GetCardinalDirection((attempt + directionOffset) % 4);
            comboPositions[0] = current;
            comboPositions[1] = current + direction;
            comboPositions[2] = comboPositions[1] + direction;
            comboPositionCount = desiredCount;
            if (!AreComboPlacementsValid() || !TryFindComboEscape(comboPositions[desiredCount - 1], out comboEscapeTarget))
            {
                comboPositionCount = 0;
                continue;
            }

            nextComboPosition = 0;
            comboEscaping = false;
            return true;
        }

        ClearBombMineCombo();
        return false;
    }

    private bool AreComboPlacementsValid()
    {
        for (int i = 1; i < comboPositionCount; i++)
        {
            Vector2Int index = comboPositions[i];
            if (!CanUseEscapeTile(index) || BombMine.IsEnemyBlastDangerTile(index, owner))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryFindComboEscape(Vector2Int start, out Vector2Int escape)
    {
        escape = start;
        TilePaintManager map = TilePaintManager.Instance;
        int radius = Mathf.Max(2, itemController.DefaultBombMineCrossRadius);
        int bestScore = int.MinValue;
        int searchRadius = radius + 3;
        BuildComboReachability(start);
        for (int x = Mathf.Max(0, start.x - searchRadius); x <= Mathf.Min(map.GridSize - 1, start.x + searchRadius); x++)
        for (int y = Mathf.Max(0, start.y - searchRadius); y <= Mathf.Min(map.GridSize - 1, start.y + searchRadius); y++)
        {
            Vector2Int candidate = new Vector2Int(x, y);
            if (!CanUseEscapeTile(candidate) ||
                BombMine.IsEnemyBlastDangerTile(candidate, owner) ||
                IsInsidePlannedBlast(candidate, radius) ||
                !IsComboReachable(candidate))
            {
                continue;
            }

            int distance = Mathf.Abs(x - start.x) + Mathf.Abs(y - start.y);
            int score = -distance * 3 + CountEscapeTiles(candidate) * 5;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            escape = candidate;
        }

        return bestScore != int.MinValue;
    }

    private bool IsInsidePlannedBlast(Vector2Int candidate, int radius)
    {
        for (int i = 0; i < comboPositionCount; i++)
        {
            Vector2Int mine = comboPositions[i];
            if ((candidate.x == mine.x && Mathf.Abs(candidate.y - mine.y) <= radius) ||
                (candidate.y == mine.y && Mathf.Abs(candidate.x - mine.x) <= radius))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildComboReachability(Vector2Int start)
    {
        int size = TilePaintManager.Instance.GridSize;
        int count = size * size;
        if (comboVisitStamp == null || comboVisitStamp.Length != count)
        {
            comboVisitStamp = new int[count];
            comboQueue = new int[count];
            comboStamp = 0;
        }

        comboStamp++;
        if (comboStamp == int.MaxValue)
        {
            System.Array.Clear(comboVisitStamp, 0, comboVisitStamp.Length);
            comboStamp = 1;
        }

        int head = 0;
        int tail = 0;
        int startFlat = start.x * size + start.y;
        comboQueue[tail++] = startFlat;
        comboVisitStamp[startFlat] = comboStamp;
        while (head < tail)
        {
            int flat = comboQueue[head++];
            Vector2Int current = new Vector2Int(flat / size, flat % size);
            for (int i = 0; i < 4; i++)
            {
                Vector2Int next = current + GetCardinalDirection(i);
                if (!TilePaintManager.Instance.IsInsideGrid(next)) continue;
                int nextFlat = next.x * size + next.y;
                if (comboVisitStamp[nextFlat] == comboStamp || !CanUseEscapeTile(next) || IsPlannedMine(next)) continue;
                comboVisitStamp[nextFlat] = comboStamp;
                comboQueue[tail++] = nextFlat;
            }
        }
    }

    private bool IsComboReachable(Vector2Int target)
    {
        int size = TilePaintManager.Instance.GridSize;
        int flat = target.x * size + target.y;
        return comboVisitStamp != null && flat >= 0 && flat < comboVisitStamp.Length &&
               comboVisitStamp[flat] == comboStamp;
    }

    private bool IsPlannedMine(Vector2Int index)
    {
        for (int i = 0; i < comboPositionCount; i++)
        {
            if (comboPositions[i] == index) return true;
        }

        return false;
    }

    private void BeginComboEscape(float now)
    {
        comboEscaping = true;
        comboAbortAt = now;
    }

    private void FinishBombMineCombo(float now)
    {
        ClearBombMineCombo();
        nextBombMineUseAt = now + settings.BombMineCooldownSeconds + Random.Range(0f, 1.5f);
    }

    private void ClearBombMineCombo()
    {
        comboPositionCount = 0;
        nextComboPosition = 0;
        comboEscapeTarget = default;
        nextComboPlacementAt = 0f;
        comboAbortAt = 0f;
        comboEscaping = false;
    }

    private static Vector2Int GetCardinalDirection(int index)
    {
        switch (index & 3)
        {
            case 0: return Vector2Int.right;
            case 1: return Vector2Int.up;
            case 2: return Vector2Int.left;
            default: return Vector2Int.down;
        }
    }

    public bool TryUseTeleport(GamePhase phase, bool isStuck, Vector2Int targetIndex)
    {
        if (teleportAbility == null || !TilePaintManager.HasInstance)
        {
            return false;
        }

        // 텔레포트 경로 위 상대 폭탄은 소유권을 빼앗을 수 있으므로 가장 먼저 노립니다.
        // 같은 행/열/대각선이면서 실제 텔레포트 거리 안에 있는 폭탄만 후보로 사용합니다.
        if (BombMine.TryFindStealableMineOnTeleportLine(
                owner, transform.position, teleportAbility.TileSteps, out Vector3 minePosition) &&
            teleportAbility.TryTeleportToward(minePosition))
        {
            return true;
        }

        if (isStuck)
        {
            Vector3 target = TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex);
            return teleportAbility.TryTeleportToward(target);
        }

        if (phase == GamePhase.Battle && TryFindNearestEnemy(out Vector3 enemyPosition))
        {
            if ((enemyPosition - transform.position).sqrMagnitude > EnemyNearRadius * EnemyNearRadius)
            {
                return teleportAbility.TryTeleportToward(enemyPosition);
            }
        }

        return false;
    }

    private bool TryFindNearestEnemy(out Vector3 position)
    {
        position = Vector3.zero;
        if (!GameRoundManager.HasInstance || transform == null)
        {
            return false;
        }

        float bestSqrDistance = float.MaxValue;
        bool found = false;
        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null ||
                participant.IsEliminated ||
                participant.Owner == owner ||
                participant.Transform == null)
            {
                continue;
            }

            float sqrDistance = (participant.Transform.position - transform.position).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                position = participant.Transform.position;
                found = true;
            }
        }

        return found;
    }

    private bool IsStandingOnEnemyTile()
    {
        if (!TilePaintManager.HasInstance || transform == null)
        {
            return false;
        }

        Vector2Int index = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (!TilePaintManager.Instance.IsTileWalkable(index))
        {
            return false;
        }

        return TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner tileOwner) &&
               tileOwner != PaintOwner.Neutral &&
               tileOwner != owner;
    }

    private bool IsStandingOnContestedTile()
    {
        if (!TilePaintManager.HasInstance || transform == null)
        {
            return false;
        }

        Vector2Int index = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return false;
        }

        return TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner tileOwner) &&
               tileOwner != owner;
    }

    private int ScoreBombMinePlacement(
        GamePhase phase,
        Vector2Int currentIndex,
        bool hasEnemy,
        Vector3 enemyPosition,
        float enemySqrDistance,
        float triggerDistance,
        bool isStuck,
        bool openingPhaseTrap)
    {
        int score = 0;
        score += ScoreBombMinePaintCoverage(phase, currentIndex);
        score += ScoreBombMineEnemyPressure(
            hasEnemy,
            enemyPosition,
            enemySqrDistance,
            triggerDistance,
            isStuck);
        score += ScoreBombMineRouteValue(currentIndex);

        if (openingPhaseTrap)
        {
            score += phase == GamePhase.Battle ? 4 : 2;
        }

        if (phase == GamePhase.Battle && IsStandingOnContestedTile())
        {
            score += 6;
        }

        return score;
    }

    private int ScoreBombMinePaintCoverage(GamePhase phase, Vector2Int centerIndex)
    {
        int score = 0;
        int radius = itemController != null
            ? Mathf.Max(1, itemController.DefaultBombMineCrossRadius)
            : 1;

        score += ScoreBombMineAffectedTile(phase, centerIndex);
        score += ScoreBombMineRay(phase, centerIndex, Vector2Int.right, radius);
        score += ScoreBombMineRay(phase, centerIndex, Vector2Int.left, radius);
        score += ScoreBombMineRay(phase, centerIndex, Vector2Int.up, radius);
        score += ScoreBombMineRay(phase, centerIndex, Vector2Int.down, radius);

        return score;
    }

    private int ScoreBombMineRay(
        GamePhase phase,
        Vector2Int centerIndex,
        Vector2Int direction,
        int radius)
    {
        int score = 0;
        for (int distance = 1; distance <= radius; distance++)
        {
            Vector2Int index = centerIndex + direction * distance;
            if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                TilePaintManager.Instance.IsTileDestroyed(index))
            {
                break;
            }

            if (WeaponPatternTraversal.IsUnbreakable(index))
            {
                score -= 4;
                break;
            }

            score += ScoreBombMineAffectedTile(phase, index);
            if (WeaponPatternTraversal.IsBreakable(index))
            {
                score += phase == GamePhase.Battle ? 7 : 5;
                break;
            }

            if (BombMine.IsBlockingTile(index))
            {
                score += 10;
                break;
            }
        }

        return score;
    }

    private int ScoreBombMineAffectedTile(GamePhase phase, Vector2Int index)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return -4;
        }

        if (GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, index))
        {
            return phase == GamePhase.Farming ? -3 : -1;
        }

        if (!TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner tileOwner))
        {
            return 0;
        }

        if (tileOwner == owner)
        {
            return phase == GamePhase.Battle ? -2 : -1;
        }

        if (tileOwner == PaintOwner.Neutral)
        {
            return phase == GamePhase.Farming ? 2 : 1;
        }

        return phase == GamePhase.Battle ? 6 : 4;
    }

    private int ScoreBombMineEnemyPressure(
        bool hasEnemy,
        Vector3 enemyPosition,
        float enemySqrDistance,
        float triggerDistance,
        bool isStuck)
    {
        if (!hasEnemy || !TilePaintManager.HasInstance)
        {
            return 0;
        }

        float tileSize = Mathf.Max(0.1f, TilePaintManager.Instance.TileSize);
        float distanceTiles = Mathf.Sqrt(enemySqrDistance) / tileSize;
        float triggerTiles = Mathf.Max(1f, triggerDistance / tileSize);
        int score = 0;

        if (distanceTiles <= 1.5f)
        {
            score += 18;
        }
        else if (distanceTiles <= triggerTiles)
        {
            score += Mathf.RoundToInt(Mathf.Lerp(14f, 7f, distanceTiles / triggerTiles));
        }
        else if (isStuck && distanceTiles <= triggerTiles * settings.BombMineStuckTriggerMultiplier)
        {
            score += 8;
        }

        Vector2Int enemyIndex = TilePaintManager.Instance.GetGridIndex(enemyPosition);
        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (distanceTiles <= triggerTiles &&
            (Mathf.Abs(enemyIndex.x - currentIndex.x) <= 1 ||
            Mathf.Abs(enemyIndex.y - currentIndex.y) <= 1))
        {
            score += 3;
        }

        return score;
    }

    private int ScoreBombMineRouteValue(Vector2Int currentIndex)
    {
        int escapeCount = CountEscapeTiles(currentIndex);
        if (escapeCount <= 0)
        {
            return -100;
        }

        int score = 0;
        if (escapeCount == 1)
        {
            score += 5;
        }
        else if (escapeCount == 2)
        {
            score += 3;
        }

        score += CountAdjacentContestedTiles(currentIndex) * 2;
        // 회색 벽을 실제로 제거할 수 있는 위치라면 전투 점수보다 진로 개척을 우선합니다.
        // 보라 벽은 아래 ray 검사에서 제외되므로 파괴 불가능한 벽 앞에 폭탄을 낭비하지 않습니다.
        score += CountBreakableWallDirections(currentIndex) * 14;
        return score;
    }

    private int CountBreakableWallDirections(Vector2Int center)
    {
        int radius = itemController != null ? Mathf.Max(1, itemController.DefaultBombMineCrossRadius) : 1;
        int count = 0;
        if (HasBreakableWallInRay(center, Vector2Int.left, radius)) count++;
        if (HasBreakableWallInRay(center, Vector2Int.right, radius)) count++;
        if (HasBreakableWallInRay(center, Vector2Int.down, radius)) count++;
        if (HasBreakableWallInRay(center, Vector2Int.up, radius)) count++;
        return count;
    }

    private static bool HasBreakableWallInRay(Vector2Int center, Vector2Int direction, int radius)
    {
        for (int distance = 1; distance <= radius; distance++)
        {
            Vector2Int index = center + direction * distance;
            if (!TilePaintManager.Instance.IsInsideGrid(index) || WeaponPatternTraversal.IsUnbreakable(index)) return false;
            if (WeaponPatternTraversal.IsBreakable(index)) return true;
            if (BombMine.IsBlockingTile(index)) return false;
        }
        return false;
    }

    private int CountEscapeTiles(Vector2Int currentIndex)
    {
        int count = 0;
        if (CanUseEscapeTile(currentIndex + Vector2Int.up)) count++;
        if (CanUseEscapeTile(currentIndex + Vector2Int.down)) count++;
        if (CanUseEscapeTile(currentIndex + Vector2Int.left)) count++;
        if (CanUseEscapeTile(currentIndex + Vector2Int.right)) count++;
        return count;
    }

    private int CountAdjacentContestedTiles(Vector2Int currentIndex)
    {
        int count = 0;
        if (IsContestedTile(currentIndex + Vector2Int.up)) count++;
        if (IsContestedTile(currentIndex + Vector2Int.down)) count++;
        if (IsContestedTile(currentIndex + Vector2Int.left)) count++;
        if (IsContestedTile(currentIndex + Vector2Int.right)) count++;
        return count;
    }

    private bool IsContestedTile(Vector2Int index)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return false;
        }

        return TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner tileOwner) &&
               tileOwner != owner;
    }

    private bool HasEscapeTile(Vector2Int currentIndex)
    {
        return CanUseEscapeTile(currentIndex + Vector2Int.up) ||
               CanUseEscapeTile(currentIndex + Vector2Int.down) ||
               CanUseEscapeTile(currentIndex + Vector2Int.left) ||
               CanUseEscapeTile(currentIndex + Vector2Int.right);
    }

    private bool CanUseEscapeTile(Vector2Int index)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            !TilePaintManager.Instance.IsTileWalkable(index) ||
            BombMine.IsBlockingTile(index))
        {
            return false;
        }

        return !GameRoundManager.HasInstance ||
               GameRoundManager.Instance.CanOwnerClaimTile(owner, index);
    }

    private static Settings NormalizeSettings(Settings source)
    {
        source.BombMineCooldownSeconds = Mathf.Max(0.1f, source.BombMineCooldownSeconds);
        source.BombMineEnemyTriggerTiles = Mathf.Max(0.5f, source.BombMineEnemyTriggerTiles);
        source.BombMineStuckTriggerMultiplier = Mathf.Max(1f, source.BombMineStuckTriggerMultiplier);
        source.BombMineMinimumTacticalScore = Mathf.Max(1, source.BombMineMinimumTacticalScore);
        source.BombMineComboChance = Mathf.Clamp01(source.BombMineComboChance);
        source.BombMineThreeComboChance = Mathf.Clamp01(source.BombMineThreeComboChance);
        source.BombMineComboPlacementInterval = Mathf.Clamp(source.BombMineComboPlacementInterval, 0.5f, 1.25f);
        return source;
    }
}
