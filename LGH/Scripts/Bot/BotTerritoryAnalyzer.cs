using UnityEngine;

/// <summary>
/// 공통 영토 상태에서 봇용 읽기 전용 스냅샷을 만듭니다. 연결 판정은 TilePaintManager의 결과를 재사용합니다.
/// </summary>
public sealed class BotTerritoryAnalyzer
{
    private readonly PaintOwner owner;

    public BotTerritoryAnalyzer(PaintOwner owner)
    {
        this.owner = owner;
    }

    public BotTerritorySnapshot Capture(TilePaintManager map, Vector2Int current)
    {
        BotTerritorySnapshot result = new BotTerritorySnapshot
        {
            OwnConnected = map.CountConnectedTerritory(owner),
            OwnIsolated = map.CountIsolatedTerritory(owner),
            LeadingEnemy = PaintOwner.Neutral,
            UnderImmediateDanger = BombMine.IsEnemyBlastDangerTile(current, owner)
        };
        for (int i = 1; i <= 4; i++)
        {
            PaintOwner candidate = (PaintOwner)i;
            if (candidate == owner) continue;
            int count = map.CountConnectedTerritory(candidate);
            if (count <= result.BestEnemyConnected) continue;
            result.BestEnemyConnected = count;
            result.LeadingEnemy = candidate;
        }
        return result;
    }
}
