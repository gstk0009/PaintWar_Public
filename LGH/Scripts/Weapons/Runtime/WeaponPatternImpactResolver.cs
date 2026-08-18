using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어, 봇, 원격 클라이언트 스킬이 동일한 서버 권한 타일 판정을 사용하게 합니다.
/// 도달 가능한 타일 수집, 도색, 지뢰 연쇄, 회색 장애물 파괴 순서를 한곳에서 보장합니다.
/// </summary>
public static class WeaponPatternImpactResolver
{
    public static int CollectReachableTiles(WeaponData weapon, Vector3 origin, Vector3 forward,
        int playerLevel, List<Vector2Int> output)
    {
        output.Clear();
        if (weapon == null || !TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
            return 0;

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int originIndex = tileManager.GetGridIndex(origin);
        foreach (Vector2Int offset in WeaponPatternResolver.GetOffsets(
                     weapon, forward, Mathf.Max(1, playerLevel)))
        {
            Vector2Int target = originIndex + offset;
            if (WeaponPatternTraversal.CanReachOffset(originIndex, offset)) output.Add(target);
        }
        return output.Count;
    }

    public static int ApplyAuthoritative(WeaponData weapon, Vector3 origin, Vector3 forward,
        PaintOwner sourceOwner, int playerLevel, List<Vector2Int> reusableBuffer)
    {
        if (sourceOwner == PaintOwner.Neutral ||
            CollectReachableTiles(weapon, origin, forward, playerLevel, reusableBuffer) == 0)
            return 0;

        // 장애물이 남아 있는 상태에서는 점령 서비스가 해당 바닥을 거부할 수 있으므로
        // 회색 장애물을 먼저 제거한 뒤 모든 적중 칸에 소유권 규칙을 적용합니다.
        int destroyedObstacles = ObstacleFieldManager.HasInstance
            ? ObstacleFieldManager.Instance.ServerDestroyAtGridIndices(reusableBuffer)
            : 0;
        int detonatedMines = BombMine.DetonateAtIndices(reusableBuffer, true);

        for (int i = 0; i < reusableBuffer.Count; i++)
        {
            Vector2Int target = reusableBuffer[i];
            if (GameRoundManager.HasInstance &&
                !GameRoundManager.Instance.CanOwnerClaimTile(sourceOwner, target)) continue;

            if (NetworkTilePaintAuthority.HasInstance && NetworkTilePaintAuthority.Instance.IsServerInitialized)
                NetworkTilePaintAuthority.Instance.ServerClaimByGridIndex(target, sourceOwner);
            else if (TileClaimService.HasInstance)
                TileClaimService.Instance.TryClaimByGridIndex(target, sourceOwner);
            else
                TilePaintManager.Instance.TryClaimByGridIndex(target, sourceOwner);
        }

        if (detonatedMines > 0 || destroyedObstacles > 0)
        {
            Debug.Log("[WeaponImpact] owner=" + sourceOwner +
                      " weapon=" + weapon.WeaponId +
                      " reachableTiles=" + reusableBuffer.Count +
                      " mines=" + detonatedMines +
                      " destroyedGray=" + destroyedObstacles + ".");
        }
        return reusableBuffer.Count;
    }

    public static int DetonateMinesInPattern(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        int playerLevel,
        List<Vector2Int> reusableBuffer)
    {
        if (CollectReachableTiles(weapon, origin, forward, playerLevel, reusableBuffer) == 0)
        {
            return 0;
        }

        return BombMine.DetonateAtIndices(reusableBuffer, true);
    }
}
