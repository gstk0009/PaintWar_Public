using FishNet.Connection;
using FishNet.Object;

/// <summary>
/// PlayerLevelStats의 네트워크 snapshot 전달을 담당합니다.
/// </summary>
public partial class Player
{
    /// <summary>
    /// observer RPC가 받은 서버 snapshot을 실제 PlayerLevelStats에 적용합니다.
    /// </summary>
    private void ApplyLevelStatsSnapshot(int currentExp, int claimedTiles, float lastClaimTimestamp)
    {
        if (levelStats == null)
        {
            levelStats = GetComponent<PlayerLevelStats>();
        }

        if (levelStats != null)
        {
            levelStats.ApplyNetworkSnapshot(currentExp, claimedTiles, lastClaimTimestamp);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ServerRequestLevelBoostForTest(NetworkConnection sender = null)
    {
        PlayerPaintIdentity identity = GetComponent<PlayerPaintIdentity>();
        if (NetworkRoundController.HasInstance &&
            identity != null &&
            !NetworkRoundController.Instance.IsOwnerAllowed(sender, identity.Owner))
        {
            return;
        }

        if (levelStats == null)
        {
            levelStats = GetComponent<PlayerLevelStats>();
        }

        if (levelStats != null)
        {
            levelStats.ApplyLevelBoost();
        }
    }
}
