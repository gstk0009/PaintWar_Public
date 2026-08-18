using UnityEngine;

/// <summary>
/// 로컬/네트워크 봇이 공유하는 영토 분석기입니다. 연결 여부는 TilePaintManager의 공통 결과만 사용하며
/// 별도의 BFS나 소유권 복제 상태를 만들지 않습니다.
/// </summary>
public sealed class BotBrain
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
    };

    private readonly PaintOwner owner;
    private readonly Transform transform;
    private readonly BotTerritoryAIConfig config;
    private readonly BotTerritoryAnalyzer analyzer;
    private readonly Vector2Int[] enemyConnectionTargets = new Vector2Int[5];
    private readonly int[] enemyConnectionScores = new int[5];
    private readonly BotPersonality personality;
    private PaintOwner committedOpponent;
    private PaintOwner previousOpponent;
    private PaintOwner recentAggressor;
    private float opponentCommitUntil;
    private float rivalryUntil;
    private int observedRoundIndex = int.MinValue;

    public BotBrain(PaintOwner owner, Transform transform, BotTerritoryAIConfig config)
    {
        this.owner = owner;
        this.transform = transform;
        this.config = config != null ? config : BotTerritoryAIConfig.LoadDefault();
        analyzer = new BotTerritoryAnalyzer(owner);
        personality = (BotPersonality)(((int)owner - 1) % 4);
    }

    public BotActionPlan BuildPlan(GamePhase phase)
    {
        TilePaintManager map = TilePaintManager.Instance;
        Vector2Int current = map.GetGridIndex(transform.position);
        BotTerritorySnapshot snapshot = analyzer.Capture(map, current);
        BotGoal goal = BotGoalEvaluator.Evaluate(snapshot, config);
        EnsureRoundState();
        Vector2Int target;
        bool found;
        PaintOwner targetOwner = PaintOwner.Neutral;
        switch (goal)
        {
            case BotGoal.EvadeDanger:
                found = TryFindSafeTile(map, current, out target);
                break;
            case BotGoal.RepairTerritory:
                found = TryFindRepairBridge(map, current, out target);
                break;
            case BotGoal.DenyEnemyVictory:
            case BotGoal.AttackConnection:
            case BotGoal.PressureEnemy:
                found = TrySelectEnemyConnection(
                    map,
                    snapshot,
                    phase,
                    current,
                    goal == BotGoal.DenyEnemyVictory,
                    out targetOwner,
                    out target);
                break;
            default:
                ReleaseOpponent();
                found = TryFindConnectedFrontier(map, current, out target);
                break;
        }
        return new BotActionPlan
        {
            Goal = goal,
            TargetOwner = targetOwner,
            TargetIndex = found ? target : current,
            HasTarget = found,
            AllowWeapon = (phase == GamePhase.Farming || phase == GamePhase.Battle) &&
                          goal != BotGoal.RepairTerritory && goal != BotGoal.EvadeDanger
        };
    }

    public void NotifyTerritoryChanged(TerritoryChange change)
    {
        if (change.Owner != owner ||
            (change.Kind != TerritoryChangeKind.Isolated && change.Kind != TerritoryChangeKind.Collapsed) ||
            !TryFindLikelyAggressor(out PaintOwner aggressor))
        {
            return;
        }

        recentAggressor = aggressor;
        rivalryUntil = Time.time + Mathf.Max(1f, config.rivalryMemorySeconds);
    }

    public void NotifyAttackedBy(PaintOwner attacker)
    {
        if (attacker == PaintOwner.Neutral || attacker == owner)
        {
            return;
        }

        recentAggressor = attacker;
        rivalryUntil = Time.time + Mathf.Max(1f, config.rivalryMemorySeconds);
    }

    public void ResetForRound()
    {
        ReleaseOpponent();
        recentAggressor = PaintOwner.Neutral;
        rivalryUntil = 0f;
        opponentCommitUntil = 0f;
        observedRoundIndex = GameRoundManager.HasInstance
            ? GameRoundManager.Instance.CurrentRoundIndex
            : int.MinValue;
        BotOpponentCoordinator.BeginRound(observedRoundIndex);
    }

    public void Dispose()
    {
        ReleaseOpponent();
    }

    private bool TryFindSafeTile(TilePaintManager map, Vector2Int current, out Vector2Int best)
    {
        int bestDistance = int.MaxValue;
        best = current;
        int radius = Mathf.Max(2, config.threatRadius);
        for (int x = Mathf.Max(0, current.x - radius); x <= Mathf.Min(map.GridSize - 1, current.x + radius); x++)
        for (int y = Mathf.Max(0, current.y - radius); y <= Mathf.Min(map.GridSize - 1, current.y + radius); y++)
        {
            Vector2Int p = new Vector2Int(x, y);
            int distance = Mathf.Abs(x - current.x) + Mathf.Abs(y - current.y);
            if (distance == 0 || distance >= bestDistance || !map.IsTileWalkable(p) ||
                BombMine.IsEnemyBlastDangerTile(p, owner)) continue;
            bestDistance = distance;
            best = p;
        }
        return bestDistance != int.MaxValue;
    }

    private bool TryFindRepairBridge(TilePaintManager map, Vector2Int current, out Vector2Int best)
    {
        return TryFindBest(map, current, delegate(Vector2Int p, PaintOwner tileOwner)
        {
            if (tileOwner == owner || map.IsHomeProtected(p)) return false;
            bool connectedSide = false;
            bool isolatedSide = false;
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int neighbor = p + Directions[i];
                if (!map.IsInsideGrid(neighbor) || !map.TryGetTileOwner(neighbor, out PaintOwner neighborOwner) ||
                    neighborOwner != owner) continue;
                connectedSide |= map.IsTileConnectedTerritory(neighbor, owner);
                isolatedSide |= map.IsTileIsolated(neighbor);
            }
            return connectedSide && isolatedSide;
        }, out best);
    }

    private bool TryFindConnectedFrontier(TilePaintManager map, Vector2Int current, out Vector2Int best)
    {
        int bestScore = int.MinValue;
        best = current;
        for (int x = 0; x < map.GridSize; x++)
        for (int y = 0; y < map.GridSize; y++)
        {
            Vector2Int p = new Vector2Int(x, y);
            if (!map.IsTileWalkable(p) || !map.TryGetTileOwner(p, out PaintOwner tileOwner) ||
                tileOwner == owner || map.IsHomeProtected(p)) continue;
            int connectedNeighbors = 0;
            int openNeighbors = 0;
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int neighbor = p + Directions[i];
                if (map.IsTileConnectedTerritory(neighbor, owner)) connectedNeighbors++;
                else if (map.IsTileWalkable(neighbor)) openNeighbors++;
            }
            if (connectedNeighbors == 0) continue;
            int distance = Mathf.Abs(x - current.x) + Mathf.Abs(y - current.y);
            int score = connectedNeighbors * 22 + openNeighbors * 5 - distance * 2;
            if (tileOwner != PaintOwner.Neutral) score += 45;
            if (score <= bestScore) continue;
            bestScore = score;
            best = p;
        }
        return bestScore != int.MinValue;
    }

    private bool TrySelectEnemyConnection(
        TilePaintManager map,
        BotTerritorySnapshot snapshot,
        GamePhase phase,
        Vector2Int current,
        bool urgent,
        out PaintOwner selectedOwner,
        out Vector2Int selectedTarget)
    {
        BuildEnemyConnectionCandidates(map, current);
        float now = Time.time;
        int maximumFocus = urgent ? 2 : 1;

        if (now < opponentCommitUntil &&
            IsViableOpponent(committedOpponent) &&
            enemyConnectionScores[(int)committedOpponent] != int.MinValue &&
            BotOpponentCoordinator.TryReserve(
                owner,
                committedOpponent,
                maximumFocus,
                now,
                config.opponentReservationSeconds))
        {
            selectedOwner = committedOpponent;
            selectedTarget = enemyConnectionTargets[(int)committedOpponent];
            return true;
        }

        PaintOwner bestOwner = PaintOwner.Neutral;
        float bestUtility = float.MinValue;
        bool finalCountdown = IsFinalCountdown(phase);
        for (int i = 1; i <= 4; i++)
        {
            PaintOwner candidate = (PaintOwner)i;
            if (!IsViableOpponent(candidate) || enemyConnectionScores[i] == int.MinValue)
            {
                continue;
            }

            int reservations = BotOpponentCoordinator.CountReservations(candidate, now, owner);
            if (reservations >= maximumFocus)
            {
                continue;
            }

            int connected = map.CountConnectedTerritory(candidate);
            Vector2Int connectionTarget = enemyConnectionTargets[i];
            int distance = Mathf.Abs(connectionTarget.x - current.x) + Mathf.Abs(connectionTarget.y - current.y);
            float utility = connected * config.opponentTerritoryWeight +
                            enemyConnectionScores[i] * config.opponentConnectionWeight -
                            distance * config.opponentDistanceWeight -
                            reservations * config.reservationPenalty;

            if (candidate == snapshot.LeadingEnemy)
            {
                utility += urgent ? config.urgentLeaderBonus : config.leaderBonus;
            }

            if (candidate == previousOpponent && candidate != committedOpponent)
            {
                utility -= config.repeatedTargetPenalty;
            }

            if (candidate == recentAggressor && now < rivalryUntil)
            {
                utility += personality == BotPersonality.Rival ? 72f : 38f;
            }

            utility += GetPersonalityAdjustment(connected, distance, enemyConnectionScores[i]);
            utility += GetTimeAdjustment(finalCountdown, connected, snapshot.OwnConnected);
            if (utility > bestUtility)
            {
                bestUtility = utility;
                bestOwner = candidate;
            }
        }

        if (bestOwner == PaintOwner.Neutral ||
            !BotOpponentCoordinator.TryReserve(
                owner,
                bestOwner,
                maximumFocus,
                now,
                config.opponentReservationSeconds))
        {
            ReleaseOpponent();
            selectedOwner = PaintOwner.Neutral;
            selectedTarget = current;
            return false;
        }

        if (committedOpponent != bestOwner)
        {
            previousOpponent = committedOpponent;
            committedOpponent = bestOwner;
        }

        opponentCommitUntil = now + Mathf.Max(0.5f, config.opponentCommitSeconds);
        selectedOwner = bestOwner;
        selectedTarget = enemyConnectionTargets[(int)bestOwner];
        return true;
    }

    private void BuildEnemyConnectionCandidates(TilePaintManager map, Vector2Int current)
    {
        for (int i = 0; i < enemyConnectionScores.Length; i++)
        {
            enemyConnectionScores[i] = int.MinValue;
            enemyConnectionTargets[i] = current;
        }

        for (int x = 0; x < map.GridSize; x++)
        for (int y = 0; y < map.GridSize; y++)
        {
            Vector2Int p = new Vector2Int(x, y);
            if (!map.IsTileWalkable(p) ||
                !map.TryGetTileOwner(p, out PaintOwner enemy) ||
                enemy == PaintOwner.Neutral ||
                enemy == owner ||
                map.IsHomeProtected(p) ||
                !map.IsTileConnectedTerritory(p, enemy)) continue;

            bool left = map.IsTileConnectedTerritory(p + Vector2Int.left, enemy);
            bool right = map.IsTileConnectedTerritory(p + Vector2Int.right, enemy);
            bool down = map.IsTileConnectedTerritory(p + Vector2Int.down, enemy);
            bool up = map.IsTileConnectedTerritory(p + Vector2Int.up, enemy);
            int neighbors = (left ? 1 : 0) + (right ? 1 : 0) + (down ? 1 : 0) + (up ? 1 : 0);
            int bridgeBonus = (left && right) || (down && up) ? 100 : 0;
            int edgeBonus = neighbors == 1 ? 32 : neighbors == 2 ? 55 : 0;
            int distance = Mathf.Abs(x - current.x) + Mathf.Abs(y - current.y);
            int score = bridgeBonus + edgeBonus - distance * 2;
            int enemyIndex = (int)enemy;
            if (score <= enemyConnectionScores[enemyIndex]) continue;
            enemyConnectionScores[enemyIndex] = score;
            enemyConnectionTargets[enemyIndex] = p;
        }
    }

    private float GetPersonalityAdjustment(int connected, int distance, int connectionScore)
    {
        float raw;
        switch (personality)
        {
            case BotPersonality.Aggressive:
                raw = connectionScore * 0.55f - distance * 0.25f;
                break;
            case BotPersonality.Defensive:
                raw = connected * 0.18f - distance * 0.55f;
                break;
            case BotPersonality.Rival:
                raw = recentAggressor != PaintOwner.Neutral ? 26f : 0f;
                break;
            default:
                raw = connectionScore * 0.25f + connected * 0.08f;
                break;
        }

        return raw * Mathf.Clamp(config.personalityInfluence, 0f, 0.25f);
    }

    private static float GetTimeAdjustment(bool finalCountdown, int enemyConnected, int ownConnected)
    {
        if (!finalCountdown)
        {
            return 0f;
        }

        int rankSwing = enemyConnected - ownConnected;
        return rankSwing >= 0 ? Mathf.Min(80f, 24f + rankSwing * 0.3f) : Mathf.Max(-20f, rankSwing * 0.08f);
    }

    private bool IsFinalCountdown(GamePhase phase)
    {
        if (phase != GamePhase.Battle || !GameRoundManager.HasInstance)
        {
            return false;
        }

        return GameRoundManager.Instance.IsFinalCountdownActive ||
               GameRoundManager.Instance.PhaseRemainingSeconds <= config.finalCountdownThreshold;
    }

    private bool IsViableOpponent(PaintOwner candidate)
    {
        if (candidate == PaintOwner.Neutral || candidate == owner)
        {
            return false;
        }

        if (!GameRoundManager.HasInstance)
        {
            return true;
        }

        GameParticipant participant = GameRoundManager.Instance.FindParticipant(candidate);
        return participant == null || !participant.IsEliminated;
    }

    private bool TryFindLikelyAggressor(out PaintOwner aggressor)
    {
        aggressor = PaintOwner.Neutral;
        if (!GameRoundManager.HasInstance || transform == null)
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null || participant.Owner == owner || participant.IsEliminated || participant.Transform == null)
            {
                continue;
            }

            float distance = (participant.Transform.position - transform.position).sqrMagnitude;
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            aggressor = participant.Owner;
        }

        return aggressor != PaintOwner.Neutral;
    }

    private void EnsureRoundState()
    {
        int round = GameRoundManager.HasInstance ? GameRoundManager.Instance.CurrentRoundIndex : 0;
        if (round == observedRoundIndex)
        {
            return;
        }

        observedRoundIndex = round;
        committedOpponent = PaintOwner.Neutral;
        previousOpponent = PaintOwner.Neutral;
        recentAggressor = PaintOwner.Neutral;
        opponentCommitUntil = 0f;
        rivalryUntil = 0f;
        BotOpponentCoordinator.BeginRound(round);
    }

    private void ReleaseOpponent()
    {
        BotOpponentCoordinator.Release(owner);
        committedOpponent = PaintOwner.Neutral;
        opponentCommitUntil = 0f;
    }

    private delegate bool CandidatePredicate(Vector2Int index, PaintOwner tileOwner);

    private bool TryFindBest(TilePaintManager map, Vector2Int current, CandidatePredicate predicate,
        out Vector2Int best)
    {
        int bestDistance = int.MaxValue;
        best = current;
        int size = map.GridSize;
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            Vector2Int p = new Vector2Int(x, y);
            if (!map.IsTileWalkable(p) || !map.TryGetTileOwner(p, out PaintOwner tileOwner) ||
                !predicate(p, tileOwner)) continue;
            int distance = Mathf.Abs(x - current.x) + Mathf.Abs(y - current.y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = p;
        }
        return bestDistance != int.MaxValue;
    }
}
