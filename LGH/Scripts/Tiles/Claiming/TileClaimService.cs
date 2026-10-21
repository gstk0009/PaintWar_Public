using System;
using System.Collections.Generic;
using UnityEngine;

// TilePaintManager 위에서 동작하는 gameplay 규칙 계층입니다.
// 적 타일 2단계 점령, EXP 보상, 점령 요청의 네트워크 라우팅을 처리합니다.
public class TileClaimService : MonoBehaviour
{
    private struct NeutralizedTileInfo
    {
        // 적 타일을 누가 중립화했는지와 언제 최종 점령할 수 있는지 기록합니다.
        public PaintOwner PreviousOwner;
        public PaintOwner Attacker;
        public float CanCompleteAt;
    }

    public static TileClaimService Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }
    public event Action<PaintOwner, PaintOwner> EnemyTerritoryAttacked;

    [SerializeField] private TilePaintManager tileManager;
    [SerializeField] private bool awardExperience = true;
    [SerializeField] private float enemyTileCompleteDelay = 0.35f;
    [SerializeField] private int neutralTileClaimExp = 1;
    [SerializeField] private int enemyTileNeutralizeExp = 1;
    [SerializeField] private int enemyTileCompleteExp = 3;
    [SerializeField] private int itemPickupExp = 5;

    // 적 소유 타일은 owner -> Neutral -> attacker 순서로 바뀌며, 같은 공격자가 마무리하기 전에 짧은 지연이 있습니다.
    private readonly Dictionary<Vector2Int, NeutralizedTileInfo> neutralizedTiles = new Dictionary<Vector2Int, NeutralizedTileInfo>();

    private void Awake()
    {
        Instance = this;
        if (tileManager == null)
        {
            tileManager = GetComponent<TilePaintManager>();
        }
    }

    public bool TryClaimByWorldPos(Vector3 worldPosition, PaintOwner attacker)
    {
        // 외부 진입점입니다. 네트워크 모드에서는 서버 authority로 전달합니다.
        TilePaintManager manager = GetManager();
        if (manager == null)
        {
            return false;
        }

        if (manager.ShouldRouteClaimsThroughNetworkAuthority)
        {
            return NetworkTilePaintAuthority.Instance.RequestClaimByWorldPos(worldPosition, attacker);
        }

        return TryClaimNeutralByGridIndexLocal(manager.GetGridIndex(worldPosition), attacker);
    }

    public bool TryClaimByWorldPosLocal(Vector3 worldPosition, PaintOwner attacker)
    {
        TilePaintManager manager = GetManager();
        if (manager == null)
        {
            return false;
        }

        return TryClaimNeutralByGridIndexLocal(manager.GetGridIndex(worldPosition), attacker);
    }

    public bool TryClaimNeutralByGridIndexLocal(Vector2Int index, PaintOwner attacker)
    {
        TilePaintManager manager = GetManager();
        if (manager == null || attacker == PaintOwner.Neutral ||
            !manager.TryGetTileOwner(index, out PaintOwner beforeOwner) ||
            beforeOwner != PaintOwner.Neutral)
        {
            return false;
        }

        bool changed = manager.TryClaimNeutralByGridIndexLocal(index, attacker);
        if (changed && awardExperience)
        {
            PlayerLevelStats.AddTileClaimReward(attacker, neutralTileClaimExp);
        }

        return changed;
    }

    public bool TryClaimByGridIndex(Vector2Int index, PaintOwner attacker)
    {
        TilePaintManager manager = GetManager();
        if (manager == null || attacker == PaintOwner.Neutral)
        {
            return false;
        }

        if (manager.ShouldRouteClaimsThroughNetworkAuthority)
        {
            return NetworkTilePaintAuthority.Instance.RequestClaimByGridIndex(index, attacker);
        }

        return TryClaimByGridIndexLocal(index, attacker);
    }

    public bool TryClaimByGridIndexLocal(Vector2Int index, PaintOwner attacker)
    {
        // 실제 타일 점령 규칙과 보상을 적용하는 서버/오프라인 경로입니다.
        TilePaintManager manager = GetManager();
        if (manager == null || attacker == PaintOwner.Neutral)
        {
            return false;
        }

        if (!manager.TryGetTileOwner(index, out PaintOwner beforeOwner))
        {
            return false;
        }

        if (IsWaitingForEnemyTileComplete(index, attacker, beforeOwner))
        {
            return false;
        }

        bool changed = manager.TryClaimByGridIndexLocal(index, attacker);
        if (!changed)
        {
            return false;
        }

        if (!manager.TryGetTileOwner(index, out PaintOwner afterOwner))
        {
            return true;
        }

        if (beforeOwner != PaintOwner.Neutral &&
            beforeOwner != attacker &&
            afterOwner != beforeOwner)
        {
            EnemyTerritoryAttacked?.Invoke(beforeOwner, attacker);
        }

        if (awardExperience)
        {
            AwardExperience(index, beforeOwner, afterOwner, attacker);
        }

        TrackNeutralizedTile(index, beforeOwner, afterOwner, attacker);
        return true;
    }

    public void AddItemPickupExp(PaintOwner owner)
    {
        PlayerLevelStats.AddExp(owner, itemPickupExp);
    }

    public void ResetRoundState()
    {
        neutralizedTiles.Clear();
    }

    private bool IsWaitingForEnemyTileComplete(Vector2Int index, PaintOwner attacker, PaintOwner beforeOwner)
    {
        if (beforeOwner != PaintOwner.Neutral)
        {
            return false;
        }

        if (!neutralizedTiles.TryGetValue(index, out NeutralizedTileInfo info))
        {
            return false;
        }

        return info.Attacker == attacker && Time.time < info.CanCompleteAt;
    }

    private void TrackNeutralizedTile(Vector2Int index, PaintOwner beforeOwner, PaintOwner afterOwner, PaintOwner attacker)
    {
        if (afterOwner == PaintOwner.Neutral && beforeOwner != PaintOwner.Neutral && beforeOwner != attacker)
        {
            neutralizedTiles[index] = new NeutralizedTileInfo
            {
                PreviousOwner = beforeOwner,
                Attacker = attacker,
                CanCompleteAt = Time.time + enemyTileCompleteDelay
            };
            return;
        }

        if (afterOwner == attacker)
        {
            neutralizedTiles.Remove(index);
        }
    }

    private void AwardExperience(Vector2Int index, PaintOwner beforeOwner, PaintOwner afterOwner, PaintOwner attacker)
    {
        // 적 타일 중립화와 최종 점령은 서로 다른 보상을 지급합니다.
        if (afterOwner == PaintOwner.Neutral && beforeOwner != PaintOwner.Neutral && beforeOwner != attacker)
        {
            PlayerLevelStats.AddExp(attacker, enemyTileNeutralizeExp);
            return;
        }

        if (afterOwner != attacker)
        {
            return;
        }

        bool completedEnemyClaim = neutralizedTiles.TryGetValue(index, out NeutralizedTileInfo info)
            && info.PreviousOwner != PaintOwner.Neutral
            && info.PreviousOwner != attacker
            && Time.time >= info.CanCompleteAt;

        PlayerLevelStats.AddTileClaimReward(
            attacker,
            completedEnemyClaim ? enemyTileCompleteExp : neutralTileClaimExp);
    }

    private TilePaintManager GetManager()
    {
        if (tileManager == null && TilePaintManager.HasInstance)
        {
            tileManager = TilePaintManager.Instance;
        }

        return tileManager;
    }
}
