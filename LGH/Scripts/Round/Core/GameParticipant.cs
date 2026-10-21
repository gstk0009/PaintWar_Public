using UnityEngine;

// 한 라운드 매치에 참여하는 플레이어 또는 봇의 런타임 상태를 담습니다.
public class GameParticipant
{
    public PaintOwner Owner;
    public string DisplayName;
    public bool IsBot;
    public bool IsEliminated;
    public FarmingZone Zone;
    public Transform Transform;
    public PlayerPaintIdentity Identity;
    public PlayerLevelStats Stats;

    private int networkTileCount = -1;

    // 현재 TilePaintManager 기준으로 이 참가자가 점령한 타일 수를 반환합니다.
    public int CurrentTileCount
    {
        get
        {
            if (networkTileCount >= 0)
            {
                return networkTileCount;
            }

            return TilePaintManager.HasInstance
                ? TilePaintManager.Instance.CountConnectedTerritory(Owner)
                : 0;
        }
    }

    public void SetNetworkTileCount(int tileCount)
    {
        networkTileCount = Mathf.Max(0, tileCount);
    }

    // 참가자의 현재 레벨을 반환하고, 스탯 컴포넌트가 없으면 기본값 1을 사용합니다.
    public int Level
    {
        get { return Stats != null ? Stats.Level : 1; }
    }

    // 참가자의 현재 경험치를 반환하고, 스탯 컴포넌트가 없으면 0을 사용합니다.
    public int CurrentExp
    {
        get { return Stats != null ? Stats.CurrentExp : 0; }
    }

    // 마지막으로 타일을 점령한 시간을 반환해 동률 판정에 사용합니다.
    public float LastClaimTimestamp
    {
        get { return Stats != null ? Stats.LastClaimTimestamp : 0f; }
    }
}
