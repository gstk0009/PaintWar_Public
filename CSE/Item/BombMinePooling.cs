using FishNet;
using UnityEngine;

public sealed partial class BombMine
{
    private static bool IsOccupied(Vector2Int index)
    {
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null && mine.armed && mine.gridIndex == index)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConvertMineAt(Vector2Int index, PaintOwner newOwner)
    {
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null &&
                mine.armed &&
                mine.gridIndex == index)
            {
                return mine.ConvertOwner(newOwner);
            }
        }

        return false;
    }

    private static int CountActiveMines(PaintOwner mineOwner)
    {
        int count = 0;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null && mine.armed && mine.chargeOwner == mineOwner)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryResolvePlacement(
        Vector3 worldPosition,
        PaintOwner mineOwner,
        int maxActiveMines,
        out Vector2Int index)
    {
        index = Vector2Int.zero;
        if (mineOwner == PaintOwner.Neutral ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        index = tileManager.GetGridIndex(worldPosition);
        int activeLimit = maxActiveMines > 0
            ? maxActiveMines
            : GetMaxActiveMinesForOwner(mineOwner);

        if (!tileManager.IsInsideGrid(index) ||
            tileManager.IsTileDestroyed(index) ||
            IsOccupied(index) ||
            CountActiveMines(mineOwner) >= activeLimit)
        {
            return false;
        }

        return !GameRoundManager.HasInstance ||
               GameRoundManager.Instance.CanOwnerClaimTile(mineOwner, index);
    }

    private static int GetMaxActiveMinesForOwner(PaintOwner mineOwner)
    {
        int level = GetOwnerLevel(mineOwner);
        return activeSettings != null
            ? activeSettings.GetMaxActiveMinesForLevel(level)
            : BombMineSettings.GetDefaultMaxActiveMinesForLevel(level);
    }

    private static int GetOwnerLevel(PaintOwner mineOwner)
    {
        return PlayerLevelStats.TryGetStats(mineOwner, out PlayerLevelStats stats)
            ? Mathf.Max(1, stats.Level)
            : 1;
    }

    private static bool IsGameplayPhaseActive()
    {
        bool serverStarted = InstanceFinder.NetworkManager != null &&
                             InstanceFinder.NetworkManager.IsServerStarted;
        if (!serverStarted && !LoadingTransitionController.IsGameplayReleased)
        {
            return false;
        }

        if (!GameRoundManager.HasInstance)
        {
            return true;
        }

        GamePhase phase = GameRoundManager.Instance.CurrentPhase;
        return phase == GamePhase.Farming || phase == GamePhase.Battle;
    }

    private static BombMine Rent()
    {
        while (Pool.Count > 0)
        {
            BombMine pooled = Pool.Pop();
            if (pooled != null)
            {
                return pooled;
            }
        }

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Collider primitiveCollider = obj.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            primitiveCollider.isTrigger = true;
            primitiveCollider.enabled = false;
            Destroy(primitiveCollider);
        }

        obj.AddComponent<BoxCollider>();

        return obj.AddComponent<BombMine>();
    }

    private void ReturnToPool()
    {
        armed = false;
        ActiveBombs.Remove(this);
        scheduledChainContext = null;
        scheduledComboDepth = 0;
        scheduledExplodeAt = 0f;
        scheduledChainSourcePosition = Vector3.zero;
        scheduledChainSourceOwner = PaintOwner.Neutral;
        originalOwner = PaintOwner.Neutral;
        chargeOwner = PaintOwner.Neutral;
        mineLevel = 1;
        SetScheduledChainLinkVisible(false);
        SetComboGlowVisible(false);
        warningVisible = false;
        warningComboDepth = 0;
        SetWarningVisible(false);
        SetEmphasisEffectVisible(false);
        blockingColliderArmed = false;
        if (blockingCollider != null)
        {
            blockingCollider.enabled = false;
        }

        gameObject.SetActive(false);
        if (Pool.Count < MaxPoolSize)
        {
            Pool.Push(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
