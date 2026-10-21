using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

public struct TileClaimRequestBroadcast : IBroadcast
{
    // 일반 점령 규칙으로 타일을 점령하기 위한 client -> server 요청입니다.
    public int X;
    public int Y;
    public int Owner;
    public byte NeutralOnly;
}

public struct TileOwnerDelta
{
    public int X;
    public int Y;
    public int Owner;
}

public struct TileOwnerBatchBroadcast : IBroadcast
{
    // server -> clients로 보내는 묶음 owner 변경입니다. 한 프레임의 여러 점령을 합쳐 보냅니다.
    public TileOwnerDelta[] Changes;
}

public struct ForceTileOwnerRequestBroadcast : IBroadcast
{
    public int X;
    public int Y;
    public int Owner;
}

public struct TileGridSnapshotRequestBroadcast : IBroadcast
{
    public byte Requested;
}

public struct TileGridSnapshotBroadcast : IBroadcast
{
    // late join/재접속 클라이언트를 위한 전체 grid snapshot입니다.
    public int GridSize;
    public PaintOwner[] Owners;
    public bool[] Destroyed;
}

// 네트워크 매치에서 타일 소유권을 담당하는 서버 authority입니다.
// 클라이언트가 변경을 요청하면 서버가 owner/라운드 규칙을 검증한 뒤 delta 또는 전체 snapshot을 broadcast합니다.
public sealed class NetworkTilePaintAuthority : MonoBehaviour
{
    public static NetworkTilePaintAuthority Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    [SerializeField] private TilePaintManager tileManager;
    [SerializeField] private TileClaimService claimService;
    // 클라이언트는 grid가 준비되고 서버가 응답할 때까지 initial snapshot 요청을 재시도합니다.
    [SerializeField] private float initialSnapshotRetrySeconds = 0.5f;
    [SerializeField, Min(0.02f)] private float maintenanceInterval = 0.1f;
    [SerializeField, Range(0.02f, 0.2f)] private float tileBatchInterval = 0.05f;

    private NetworkManager networkManager;
    private bool broadcastsRegistered;
    private bool initialSnapshotReceived;
    private float nextSnapshotRequestTime;
    private float nextMaintenanceTime;
    private float nextTileBatchTime;
    // 서버 측 프레임 단위 batch입니다. 같은 index는 broadcast 전에 마지막 owner가 최종값이 됩니다.
    private readonly Dictionary<Vector2Int, PaintOwner> pendingTileChanges =
        new Dictionary<Vector2Int, PaintOwner>();

    public bool HasInitialSnapshot
    {
        get
        {
            CacheNetworkManager();
            return networkManager == null ||
                   networkManager.IsServerStarted ||
                   !networkManager.IsClientStarted ||
                   initialSnapshotReceived;
        }
    }

    public bool ShouldRouteClientClaims
    {
        get
        {
            CacheNetworkManager();
            return networkManager != null && networkManager.IsClientStarted;
        }
    }

    public bool IsServerInitialized
    {
        get
        {
            CacheNetworkManager();
            return networkManager != null && networkManager.IsServerStarted;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CacheComponents();
    }

    private void Update()
    {
        if (pendingTileChanges.Count > 0 && Time.unscaledTime >= nextTileBatchTime)
        {
            nextTileBatchTime = Time.unscaledTime + Mathf.Max(0.02f, tileBatchInterval);
            FlushPendingTileChanges();
        }

        if (Time.unscaledTime < nextMaintenanceTime)
        {
            return;
        }

        nextMaintenanceTime = Time.unscaledTime + maintenanceInterval;
        CacheNetworkManager();
        RegisterBroadcasts();
        RequestSnapshotWhenReady();

        if (broadcastsRegistered && networkManager != null &&
            !networkManager.IsServerStarted && initialSnapshotReceived)
        {
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        UnregisterBroadcasts();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool RequestClaimByWorldPos(Vector3 worldPosition, PaintOwner attacker)
    {
        CacheComponents();
        if (tileManager == null)
        {
            return false;
        }

        return RequestClaimByGridIndexInternal(
            tileManager.GetGridIndex(worldPosition),
            attacker,
            true);
    }

    public bool RequestClaimByGridIndex(Vector2Int index, PaintOwner attacker)
    {
        return RequestClaimByGridIndexInternal(index, attacker, false);
    }

    private bool RequestClaimByGridIndexInternal(
        Vector2Int index,
        PaintOwner attacker,
        bool neutralOnly)
    {
        CacheComponents();
        CacheNetworkManager();
        if (!CanRequest(index, attacker))
        {
            return false;
        }

        if (tileManager.TryGetTileOwner(index, out PaintOwner currentOwner) &&
            (currentOwner == attacker ||
             (neutralOnly && currentOwner != PaintOwner.Neutral)))
        {
            return false;
        }

        // host/server는 즉시 적용하고, remote client는 broadcast 요청을 보냅니다.
        if (IsServerInitialized)
        {
            return ApplyServerClaimAndBroadcast(index, attacker, neutralOnly);
        }

        if (networkManager == null || !networkManager.IsClientStarted)
        {
            return false;
        }

        networkManager.ClientManager.Broadcast(new TileClaimRequestBroadcast
        {
            X = index.x,
            Y = index.y,
            Owner = (int)attacker,
            NeutralOnly = neutralOnly ? (byte)1 : (byte)0
        });
        return true;
    }

    public void ServerResetAllTiles()
    {
        // 라운드 시작 reset 뒤에는 모든 클라이언트가 neutral 상태로 돌아가도록 전체 snapshot을 보내야 합니다.
        CacheComponents();
        if (!IsServerInitialized || tileManager == null)
        {
            return;
        }

        tileManager.ResetAllTiles();
        pendingTileChanges.Clear();
        BroadcastGridSnapshot();
    }

    public bool ServerClaimByWorldPos(Vector3 worldPosition, PaintOwner attacker)
    {
        CacheComponents();
        if (!IsServerInitialized || tileManager == null)
        {
            return false;
        }

        return ApplyServerClaimAndBroadcast(
            tileManager.GetGridIndex(worldPosition),
            attacker,
            true);
    }

    public bool ServerClaimByGridIndex(Vector2Int index, PaintOwner attacker)
    {
        if (!IsServerInitialized)
        {
            return false;
        }

        return ApplyServerClaimAndBroadcast(index, attacker, false);
    }

    public bool RequestForceSetOwner(Vector2Int index, PaintOwner owner)
    {
        // Kept for existing effects. Remote clients must never submit final tile owners.
        return ServerForceSetOwner(index, owner);
    }

    public bool ServerForceSetOwner(Vector2Int index, PaintOwner owner)
    {
        CacheComponents();
        CacheNetworkManager();
        if (!IsServerInitialized || !CanForceSetRequest(index) ||
            owner < PaintOwner.Neutral || owner > PaintOwner.Player4)
        {
            return false;
        }

        if (tileManager.TryGetTileOwner(index, out PaintOwner currentOwner) &&
            currentOwner == owner)
        {
            return false;
        }

        return ApplyServerForceSetAndBroadcast(index, owner);
    }

    public bool ServerForceSetOwnerIgnoringProtection(Vector2Int index, PaintOwner owner)
    {
        CacheComponents();
        CacheNetworkManager();
        if (tileManager == null ||
            !tileManager.IsInsideGrid(index) ||
            tileManager.IsTileDestroyed(index) ||
            (networkManager != null &&
             networkManager.IsClientStarted &&
             !networkManager.IsServerStarted))
        {
            return false;
        }

        if (owner != PaintOwner.Neutral &&
            GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, index))
        {
            return false;
        }

        bool changed = tileManager.ForceSetTileOwnerIgnoringProtectionLocal(index, owner);
        if (!changed)
        {
            return false;
        }

        BroadcastTileOwner(index, owner);
        return true;
    }

    private void OnTileClaimRequest(
        NetworkConnection connection,
        TileClaimRequestBroadcast message,
        Channel channel)
    {
        // 요청한 owner가 보낸 connection의 등록된 PaintOwner와 맞는지 검증합니다.
        PaintOwner owner = (PaintOwner)message.Owner;
        if (!IsServerInitialized || connection == null ||
            !connection.IsActive || !connection.IsAuthenticated ||
            owner < PaintOwner.Player1 || owner > PaintOwner.Player4 ||
            !NetworkRoundController.HasInstance ||
            !NetworkRoundController.Instance.IsOwnerAllowed(connection, owner))
        {
            return;
        }

        ApplyServerClaimAndBroadcast(
            new Vector2Int(message.X, message.Y),
            owner,
            message.NeutralOnly != 0);
    }

    private void OnForceTileOwnerRequest(
        NetworkConnection connection,
        ForceTileOwnerRequestBroadcast message,
        Channel channel)
    {
        // Keep the legacy wire message registered so older/malicious clients are
        // safely ignored. Validated server gameplay effects use ServerForceSetOwner.
    }

    private void OnTileSnapshotRequest(
        NetworkConnection connection,
        TileGridSnapshotRequestBroadcast message,
        Channel channel)
    {
        if (!IsServerInitialized)
        {
            return;
        }

        SendGridSnapshotTo(connection);
    }

    private void OnTileOwnerBatch(TileOwnerBatchBroadcast message, Channel channel)
    {
        CacheComponents();
        if (tileManager == null || message.Changes == null)
        {
            return;
        }

        for (int i = 0; i < message.Changes.Length; i++)
        {
            TileOwnerDelta change = message.Changes[i];
            tileManager.ForceSetTileOwnerLocal(
                new Vector2Int(change.X, change.Y),
                (PaintOwner)change.Owner);
        }
    }

    private void OnTileGridSnapshot(TileGridSnapshotBroadcast message, Channel channel)
    {
        CacheComponents();
        if (tileManager == null || message.Owners == null)
        {
            return;
        }

        tileManager.ApplyTileSnapshotLocal(message.Owners, message.Destroyed);
        initialSnapshotReceived = true;
        enabled = false;
    }

    private bool ApplyServerClaimAndBroadcast(
        Vector2Int index,
        PaintOwner attacker,
        bool neutralOnly = false)
    {
        // 일반 점령 경로는 가능하면 TileClaimService를 사용해 EXP와 중립화 규칙이 실행되게 합니다.
        CacheComponents();
        if (tileManager == null || !tileManager.IsInsideGrid(index))
        {
            return false;
        }

        if (tileManager.IsTileDestroyed(index))
        {
            return false;
        }

        bool changed;
        if (neutralOnly)
        {
            changed = claimService != null
                ? claimService.TryClaimNeutralByGridIndexLocal(index, attacker)
                : tileManager.TryClaimNeutralByGridIndexLocal(index, attacker);
        }
        else
        {
            changed = claimService != null
                ? claimService.TryClaimByGridIndexLocal(index, attacker)
                : tileManager.TryClaimByGridIndexLocal(index, attacker);
        }

        if (!changed || !tileManager.TryGetTileOwner(index, out PaintOwner owner))
        {
            return false;
        }

        BroadcastTileOwner(index, owner);
        return true;
    }

    private bool ApplyServerForceSetAndBroadcast(Vector2Int index, PaintOwner owner)
    {
        // Force set은 최종 owner를 이미 아는 도구/효과가 사용하지만, 그래도 라운드 zone 규칙은 지킵니다.
        CacheComponents();
        if (tileManager == null ||
            !tileManager.IsInsideGrid(index) ||
            tileManager.IsTileDestroyed(index))
        {
            return false;
        }

        if (owner != PaintOwner.Neutral &&
            GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, index))
        {
            return false;
        }

        bool changed = tileManager.ForceSetTileOwnerLocal(index, owner);
        if (!changed)
        {
            return false;
        }

        BroadcastTileOwner(index, owner);
        return true;
    }

    private void BroadcastTileOwner(Vector2Int index, PaintOwner owner)
    {
        // 여러 타일 변경이 하나의 broadcast를 공유하도록 네트워크 전송을 Update까지 미룹니다.
        pendingTileChanges[index] = owner;
        enabled = true;
    }

    private void FlushPendingTileChanges()
    {
        if (pendingTileChanges.Count == 0)
        {
            return;
        }

        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            pendingTileChanges.Clear();
            return;
        }

        TileOwnerDelta[] changes = new TileOwnerDelta[pendingTileChanges.Count];
        int writeIndex = 0;
        foreach (KeyValuePair<Vector2Int, PaintOwner> pair in pendingTileChanges)
        {
            changes[writeIndex++] = new TileOwnerDelta
            {
                X = pair.Key.x,
                Y = pair.Key.y,
                Owner = (int)pair.Value
            };
        }

        pendingTileChanges.Clear();
        networkManager.ServerManager.Broadcast(new TileOwnerBatchBroadcast
        {
            Changes = changes
        });
    }

    private void BroadcastGridSnapshot()
    {
        CacheComponents();
        CacheNetworkManager();
        if (networkManager == null ||
            !networkManager.IsServerStarted ||
            tileManager == null ||
            !tileManager.IsGridReady)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(new TileGridSnapshotBroadcast
        {
            GridSize = tileManager.GridSize,
            Owners = tileManager.GetFlatGridSnapshot(),
            Destroyed = tileManager.GetFlatDestroyedSnapshot()
        });
    }

    public void SendGridSnapshotTo(NetworkConnection connection)
    {
        CacheComponents();
        CacheNetworkManager();
        if (connection == null ||
            networkManager == null ||
            !networkManager.IsServerStarted ||
            tileManager == null ||
            !tileManager.IsGridReady)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(connection, new TileGridSnapshotBroadcast
        {
            GridSize = tileManager.GridSize,
            Owners = tileManager.GetFlatGridSnapshot(),
            Destroyed = tileManager.GetFlatDestroyedSnapshot()
        });
    }

    private void RequestSnapshotWhenReady()
    {
        // remote client는 local grid가 구성된 뒤 서버에 owner data를 요청합니다.
        if (initialSnapshotReceived)
        {
            return;
        }

        CacheComponents();
        CacheNetworkManager();
        if (networkManager == null ||
            !networkManager.IsClientStarted ||
            networkManager.IsServerStarted ||
            tileManager == null ||
            !tileManager.IsGridReady ||
            Time.unscaledTime < nextSnapshotRequestTime)
        {
            return;
        }

        nextSnapshotRequestTime = Time.unscaledTime +
            Mathf.Max(0.1f, initialSnapshotRetrySeconds);
        networkManager.ClientManager.Broadcast(new TileGridSnapshotRequestBroadcast
        {
            Requested = 1
        });
    }

    private bool CanRequest(Vector2Int index, PaintOwner attacker)
    {
        return tileManager != null &&
               tileManager.IsGridReady &&
               tileManager.IsInsideGrid(index) &&
               !tileManager.IsTileDestroyed(index) &&
               attacker != PaintOwner.Neutral;
    }

    private bool CanForceSetRequest(Vector2Int index)
    {
        return tileManager != null &&
               tileManager.IsGridReady &&
               tileManager.IsInsideGrid(index) &&
               !tileManager.IsTileDestroyed(index);
    }

    private void CacheComponents()
    {
        if (tileManager == null)
        {
            tileManager = GetComponent<TilePaintManager>();
        }

        if (claimService == null)
        {
            claimService = GetComponent<TileClaimService>();
        }

        if (tileManager == null && TilePaintManager.HasInstance)
        {
            tileManager = TilePaintManager.Instance;
        }

        if (claimService == null && TileClaimService.HasInstance)
        {
            claimService = TileClaimService.Instance;
        }
    }

    private void CacheNetworkManager()
    {
        if (networkManager == null)
        {
            networkManager = FindFirstObjectByType<NetworkManager>();
        }
    }

    private void RegisterBroadcasts()
    {
        if (broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<TileClaimRequestBroadcast>(
            OnTileClaimRequest,
            true);
        networkManager.ServerManager.RegisterBroadcast<ForceTileOwnerRequestBroadcast>(
            OnForceTileOwnerRequest,
            false);
        networkManager.ServerManager.RegisterBroadcast<TileGridSnapshotRequestBroadcast>(
            OnTileSnapshotRequest,
            false);
        networkManager.ClientManager.RegisterBroadcast<TileOwnerBatchBroadcast>(OnTileOwnerBatch);
        networkManager.ClientManager.RegisterBroadcast<TileGridSnapshotBroadcast>(OnTileGridSnapshot);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<TileClaimRequestBroadcast>(OnTileClaimRequest);
        networkManager.ServerManager.UnregisterBroadcast<ForceTileOwnerRequestBroadcast>(OnForceTileOwnerRequest);
        networkManager.ServerManager.UnregisterBroadcast<TileGridSnapshotRequestBroadcast>(OnTileSnapshotRequest);
        networkManager.ClientManager.UnregisterBroadcast<TileOwnerBatchBroadcast>(OnTileOwnerBatch);
        networkManager.ClientManager.UnregisterBroadcast<TileGridSnapshotBroadcast>(OnTileGridSnapshot);
        broadcastsRegistered = false;
    }
}
