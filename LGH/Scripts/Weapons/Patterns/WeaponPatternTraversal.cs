using UnityEngine;

// 플레이어 표시, 서버 스킬 판정, 봇 점수가 공유하는 범위 규칙입니다.
// 스킬은 영역 공격이므로 중간 장애물에 막히지 않고 각 타일을 독립 판정합니다.
// 보라 장애물 칸 자체만 무효이며 회색 장애물은 적중하여 파괴됩니다.
public static class WeaponPatternTraversal
{
    public static bool CanReachOffset(Vector2Int origin, Vector2Int offset)
    {
        if (!TilePaintManager.HasInstance)
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int target = origin + offset;
        if (!tileManager.IsInsideGrid(target) || tileManager.IsTileDestroyed(target))
        {
            return false;
        }

        return !IsUnbreakable(target);
    }

    public static bool IsBreakable(Vector2Int index)
    {
        return ObstacleFieldManager.HasInstance &&
               ObstacleFieldManager.Instance.IsTileMovementBlocked(index) &&
               !ObstacleFieldManager.Instance.IsTileUnbreakable(index);
    }

    public static bool IsUnbreakable(Vector2Int index)
    {
        return ObstacleFieldManager.HasInstance &&
               ObstacleFieldManager.Instance.IsTileUnbreakable(index);
    }
}
