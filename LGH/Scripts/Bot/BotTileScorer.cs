using UnityEngine;

/// <summary>
/// 봇 후보 타일의 점수를 계산합니다.
/// 어떤 타일을 후보로 볼지는 BotTargetSelector가 결정하고,
/// 이 클래스는 "그 후보가 얼마나 좋은가"만 판단합니다.
/// </summary>
/// <summary>
/// 봇이 후보 타일을 얼마나 선호할지 점수화합니다.
/// 후보 수집은 BotTargetSelector가 하고, 이 클래스는 점수 계산만 담당합니다.
/// </summary>
public sealed class BotTileScorer
{
    private readonly PaintOwner owner;
    private readonly Vector2 battleAnchor;

    /// <summary>
    /// 봇 owner와 배틀 페이즈에서 선호할 기준 위치를 준비합니다.
    /// </summary>
    public BotTileScorer(PaintOwner owner)
    {
        this.owner = owner;
        battleAnchor = GetBattleAnchor(owner);
    }

    /// <summary>
    /// 후보 타일 하나의 점수를 계산합니다.
    /// 파밍에서는 경계에서 너무 가까운 타일과 적 근처 타일을 덜 선호하고,
    /// 배틀에서는 적 타일과 다른 봇과 떨어진 위치를 더 선호합니다.
    /// </summary>
    public int EvaluateTile(Vector2Int index, bool farming, FarmingZone farmingZone)
    {
        // phase에 따라 같은 타일도 다른 의미를 가집니다.
        // Farming은 자기 구역 내부 안정성, Battle은 전장 확장성과 적 타일 공격 가치를 더 봅니다.
        if (!TilePaintManager.HasInstance)
        {
            return 0;
        }

        if (!TilePaintManager.Instance.IsTileWalkable(index))
        {
            return int.MinValue;
        }

        int score = ScoreByTileOwner(index, farming);
        Vector2 point = new Vector2(index.x + 0.5f, index.y + 0.5f);

        if (farming)
        {
            score += ScoreFarmingPosition(index, point, farmingZone);
        }
        else
        {
            score += ScoreBattlePosition(index, point);
        }

        score += Random.Range(0, 4);
        return score;
    }

    /// <summary>
    /// 현재 타일 소유자에 따라 중립/내 타일/상대 타일의 기본 점수를 계산합니다.
    /// </summary>
    private int ScoreByTileOwner(Vector2Int index, bool farming)
    {
        if (!TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner currentOwner))
        {
            return 0;
        }

        if (currentOwner == PaintOwner.Neutral)
        {
            return farming ? 12 : 8;
        }

        if (currentOwner == owner)
        {
            return farming ? -5 : -10;
        }

        return farming ? 15 : 24;
    }

    /// <summary>
    /// 파밍 페이즈에서 경계선 거리와 주변 적 수를 기준으로 위치 보정 점수를 계산합니다.
    /// </summary>
    private int ScoreFarmingPosition(Vector2Int index, Vector2 point, FarmingZone farmingZone)
    {
        int score = 0;
        if (farmingZone != null)
        {
            score += Mathf.RoundToInt(Mathf.Min(10f, farmingZone.GetDistanceToEdge(point) * 1.5f));
        }

        score -= CountNearbyEnemies(index, 3) * 8;
        return score;
    }

    /// <summary>
    /// 배틀 페이즈에서 기준 위치 접근성, 다른 봇과의 거리, 주변 적 수를 점수에 반영합니다.
    /// </summary>
    private int ScoreBattlePosition(Vector2Int index, Vector2 point)
    {
        int score = 0;
        score += Mathf.RoundToInt(Mathf.Max(0f, 16f - Vector2.Distance(point, battleAnchor) * 0.25f));
        score += Mathf.RoundToInt(Mathf.Min(14f, GetNearestOtherBotDistance(index) * 0.5f));
        score -= CountNearbyEnemies(index, 2) * 4;
        return score;
    }

    /// <summary>
    /// 후보 타일 주변 반경 안에 있는 살아 있는 상대 참가자 수를 셉니다.
    /// </summary>
    private int CountNearbyEnemies(Vector2Int index, int radius)
    {
        if (!GameRoundManager.HasInstance || !TilePaintManager.HasInstance)
        {
            return 0;
        }

        int count = 0;
        int sqrRadius = radius * radius;

        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null || participant.IsEliminated || participant.Owner == owner || participant.Transform == null)
            {
                continue;
            }

            Vector2Int enemyIndex = TilePaintManager.Instance.GetGridIndex(participant.Transform.position);
            if ((enemyIndex - index).sqrMagnitude <= sqrRadius)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 후보 타일에서 가장 가까운 다른 봇까지의 거리를 반환해 봇끼리 뭉치지 않도록 돕습니다.
    /// </summary>
    private float GetNearestOtherBotDistance(Vector2Int index)
    {
        if (!GameRoundManager.HasInstance || !TilePaintManager.HasInstance)
        {
            return 12f;
        }

        float nearest = float.MaxValue;
        foreach (GameParticipant participant in GameRoundManager.Instance.Participants)
        {
            if (participant == null || participant.IsEliminated || participant.Owner == owner || participant.Transform == null)
            {
                continue;
            }

            // 로컬 봇/네트워크 봇을 모두 포함하기 위해 컴포넌트 검색 대신 참가자 메타데이터를 사용합니다.
            if (!participant.IsBot)
            {
                continue;
            }

            Vector2Int otherIndex = TilePaintManager.Instance.GetGridIndex(participant.Transform.position);
            nearest = Mathf.Min(nearest, Vector2Int.Distance(index, otherIndex));
        }

        return nearest == float.MaxValue ? 12f : nearest;
    }

    /// <summary>
    /// owner별로 배틀 페이즈에서 자연스럽게 향할 기준 위치를 맵 사분면 근처로 잡습니다.
    /// </summary>
    private Vector2 GetBattleAnchor(PaintOwner targetOwner)
    {
        int size = TilePaintManager.HasInstance ? TilePaintManager.Instance.GridSize : 50;
        float low = size * 0.22f;
        float high = size * 0.78f;

        switch (targetOwner)
        {
            case PaintOwner.Player1:
                return new Vector2(low, high);
            case PaintOwner.Player2:
                return new Vector2(high, high);
            case PaintOwner.Player3:
                return new Vector2(low, low);
            case PaintOwner.Player4:
                return new Vector2(high, low);
            default:
                return new Vector2(size * 0.5f, size * 0.5f);
        }
    }
}
