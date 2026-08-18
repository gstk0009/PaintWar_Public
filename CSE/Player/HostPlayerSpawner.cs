using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 씬에서 인간 플레이어 오브젝트를 생성하는 서버 측 spawner입니다.
/// 로비 roster slot을 PaintOwner로 매핑하고 room server용 짧은 재접속 snapshot을 보관합니다.
/// </summary>
public class HostPlayerSpawner : MonoBehaviour
{
    private const float RoomAdmissionProfileTimeoutSeconds = 5f;

    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float fallbackSpawnSpacing = 2f;
    private NetworkManager _networkManager;
    // 이미 게임 씬 플레이어 오브젝트를 받은 client id 목록입니다.
    private readonly HashSet<int> _spawnedClientIds = new HashSet<int>();
    // spawn되었거나 재접속한 클라이언트가 현재 차지한 roster slot index입니다.
    private readonly HashSet<int> _claimedRosterSlots = new HashSet<int>();
    private readonly Dictionary<int, int> _slotByClientId = new Dictionary<int, int>();
    private readonly Dictionary<int, string> _userIdByClientId = new Dictionary<int, string>();
    private readonly Dictionary<int, GameObject> _playerObjectByClientId = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, string> _selectedSkillByClientId =
        new Dictionary<int, string>();
    // room server는 빠른 재접속을 위해 user id별 마지막 위치/스탯 snapshot을 보관합니다.
    private readonly Dictionary<string, SavedPlayerSnapshot> _lastSnapshotByUserId =
        new Dictionary<string, SavedPlayerSnapshot>();
    // 재접속 client로 ownership이 넘어간 이전 client id입니다. FishNet 이벤트 순서로 인한 중복 spawn을 막습니다.
    private readonly HashSet<int> _supersededClientIds = new HashSet<int>();
    private readonly Dictionary<int, float> _roomAdmissionDeadlines =
        new Dictionary<int, float>();
    private float _nextSpawnCheckTime;
    private float _nextPoseCaptureTime;
    private bool _registeredSceneEvents;
    private bool _registeredProfileBroadcast;

    public NetworkObject PlayerPrefab { get { return playerPrefab; } }

    private void Start()
    {
        CacheNetworkManager();
    }

    private void Update()
    {
        CacheNetworkManager();

        if (_networkManager == null ||
            !_networkManager.IsServerStarted ||
            Time.unscaledTime < _nextSpawnCheckTime)
        {
            return;
        }

        _nextSpawnCheckTime = Time.unscaledTime + 0.25f;
        EnforceRoomRosterAdmission();
        ServerSpawnPlayersForExistingConnections();
    }

    private void OnDestroy()
    {
        if (_networkManager != null && _registeredSceneEvents)
        {
            _networkManager.SceneManager.OnClientPresenceChangeEnd -=
                OnClientPresenceChangeEnd;
            _networkManager.ServerManager.OnRemoteConnectionState -=
                OnRemoteConnectionState;
            _registeredSceneEvents = false;
        }

        if (_networkManager != null &&
            _registeredProfileBroadcast &&
            _networkManager.ServerManager != null)
        {
            _networkManager.ServerManager.UnregisterBroadcast<PlayerProfileBroadcast>(
                OnPlayerProfileBroadcast);
            _registeredProfileBroadcast = false;
        }
    }

    private void CacheNetworkManager()
    {
        if (_networkManager == null)
        {
            _networkManager = InstanceFinder.NetworkManager;
        }

        if (_networkManager == null || _registeredSceneEvents)
        {
            return;
        }

        _networkManager.SceneManager.OnClientPresenceChangeEnd +=
            OnClientPresenceChangeEnd;
        _networkManager.ServerManager.OnRemoteConnectionState +=
            OnRemoteConnectionState;
        _registeredSceneEvents = true;

        if (!_registeredProfileBroadcast &&
            _networkManager.ServerManager != null)
        {
            _networkManager.ServerManager.RegisterBroadcast<PlayerProfileBroadcast>(
                OnPlayerProfileBroadcast,
                false);
            _registeredProfileBroadcast = true;
        }

        if (_networkManager.IsServerStarted)
        {
            ServerSpawnPlayersForExistingConnections();
        }
    }

    private void OnPlayerProfileBroadcast(
        NetworkConnection connection,
        PlayerProfileBroadcast message,
        Channel channel)
    {
        // room 재접속 매칭은 FishNet의 임시 client id가 아니라 안정적인 user id를 기준으로 합니다.
        RoomConnectionProfileCache.Set(connection, message.UserId);
        _selectedSkillByClientId[connection.ClientId] =
            PlayerSkillLoadoutStore.NormalizeSkillId(message.SelectedSkillId);
        _supersededClientIds.Remove(connection.ClientId);

        if (!TryGetAuthorizedRoomSlot(connection, out int _))
        {
            RejectUnauthorizedRoomConnection(
                connection,
                "profile user is not part of the locked match roster");
            return;
        }

        _roomAdmissionDeadlines.Remove(connection.ClientId);
        if (_playerObjectByClientId.TryGetValue(connection.ClientId, out GameObject playerObject) &&
            playerObject != null &&
            playerObject.TryGetComponent(out WeaponRuntimeController existingWeaponController))
        {
            existingWeaponController.EquipById(
                _selectedSkillByClientId[connection.ClientId]);
        }
        SpawnPlayerForConnection(connection);
    }

    private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
    {
        if (!args.Added ||
            args.Scene != gameObject.scene ||
            !_networkManager.IsServerStarted)
        {
            return;
        }

        SpawnPlayerForConnection(args.Connection);
    }

    private void OnRemoteConnectionState(
        NetworkConnection connection,
        FishNet.Transporting.RemoteConnectionStateArgs args)
    {
        if (connection == null ||
            args.ConnectionState != FishNet.Transporting.RemoteConnectionState.Stopped)
        {
            return;
        }

        CaptureLastPose(connection.ClientId);
        _spawnedClientIds.Remove(connection.ClientId);
        _supersededClientIds.Remove(connection.ClientId);
        _roomAdmissionDeadlines.Remove(connection.ClientId);
        RoomConnectionProfileCache.Remove(connection);
        _playerObjectByClientId.Remove(connection.ClientId);
        _userIdByClientId.Remove(connection.ClientId);
        _selectedSkillByClientId.Remove(connection.ClientId);
        if (_slotByClientId.TryGetValue(connection.ClientId, out int slotIndex))
        {
            _claimedRosterSlots.Remove(slotIndex);
            _slotByClientId.Remove(connection.ClientId);
            Debug.Log(
                "[GameSpawn] Released match slot " + (slotIndex + 1) +
                " for disconnected client " + connection.ClientId + ".");
        }
    }

    public void ServerSpawnPlayersForExistingConnections()
    {
        CacheNetworkManager();
        if (_networkManager == null || !_networkManager.IsServerStarted)
        {
            return;
        }

        // host mode에서는 remote client dictionary에 없는 local client connection이 있을 수 있습니다.
        List<NetworkConnection> connections =
            new List<NetworkConnection>(_networkManager.ServerManager.Clients.Values);
        NetworkConnection localConnection = _networkManager.ClientManager.Connection;
        if (_networkManager.IsClientStarted &&
            localConnection != null &&
            localConnection.IsActive &&
            !ContainsConnection(connections, localConnection.ClientId))
        {
            connections.Add(localConnection);
        }

        connections.Sort((left, right) => left.ClientId.CompareTo(right.ClientId));

        foreach (NetworkConnection conn in connections)
        {
            SpawnPlayerForConnection(conn);
        }

        CaptureActivePlayerPoses();
    }

    private void SpawnPlayerForConnection(NetworkConnection conn)
    {
        if (playerPrefab == null ||
            conn == null ||
            !conn.IsActive ||
            _supersededClientIds.Contains(conn.ClientId))
        {
            return;
        }

        if (_spawnedClientIds.Contains(conn.ClientId) || HasGamePlayer(conn))
        {
            return;
        }

        // 재접속은 먼저 기존 오브젝트 ownership을 넘겨 스탯, 쿨다운, 위치를 유지하려고 시도합니다.
        if (TryTakeOverExistingPlayerForReconnect(conn))
        {
            return;
        }

        if (!TryGetSpawnSlot(conn, out int slotIndex))
        {
            return;
        }

        PaintOwner owner = GetOwnerForSlot(slotIndex);
        string appearanceCode = LoadingTransitionController.GetServerRosterAppearanceCode(slotIndex);
        string motionCode = LoadingTransitionController.GetServerRosterMotionCode(slotIndex);
        string userId = LoadingTransitionController.GetServerRosterUserId(slotIndex);
        string selectedSkillId = LoadingTransitionController.GetServerRosterSkillId(slotIndex);
        if (_selectedSkillByClientId.TryGetValue(conn.ClientId, out string submittedSkillId))
        {
            selectedSkillId = submittedSkillId;
        }
        selectedSkillId = PlayerSkillLoadoutStore.NormalizeSkillId(selectedSkillId);
        Vector3 pos = GetSpawnPosition(slotIndex);
        Quaternion rotation = Quaternion.identity;
        SavedPlayerSnapshot savedSnapshot = default;
        bool hasSavedSnapshot = TryGetReconnectSnapshot(userId, out savedSnapshot);
        if (hasSavedSnapshot)
        {
            pos = savedSnapshot.Position;
            rotation = savedSnapshot.Rotation;
            Debug.Log(
                "[GameSpawn] Restoring reconnect snapshot for user " +
                userId + " in room " + ServerLaunchMode.RoomId +
                ". exp=" + savedSnapshot.CurrentExp +
                " claimed=" + savedSnapshot.ClaimedTiles + ".");
        }

        NetworkObject nob = Instantiate(playerPrefab, pos, rotation);
        NetworkRoundController.SetDefaultBotPrefab(playerPrefab);

        NetworkBotController botController = nob.GetComponent<NetworkBotController>();
        if (botController != null)
        {
            Destroy(botController);
        }

        PlayerMovement playerMovement = nob.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.enabled = true;
        }

        Player player = nob.GetComponent<Player>();
        if (player != null)
        {
            player.ServerSetPaintOwner(owner);
            player.ServerSetAppearanceCode(appearanceCode);
            player.ServerSetMotionCode(motionCode);
        }

        WeaponRuntimeController weaponController = nob.GetComponent<WeaponRuntimeController>();
        if (weaponController != null)
        {
            if (!weaponController.EquipById(selectedSkillId))
            {
                Debug.LogWarning(
                    "[GameSpawn] Skill '" + selectedSkillId +
                    "' was not found on the player prefab; using its authored default.");
            }
        }

        PlayerLevelStats levelStats = nob.GetComponent<PlayerLevelStats>();
        if (hasSavedSnapshot && levelStats != null)
        {
            levelStats.ApplyNetworkSnapshot(
                savedSnapshot.CurrentExp,
                savedSnapshot.ClaimedTiles,
                savedSnapshot.LastClaimTimestamp);
        }

        _networkManager.ServerManager.Spawn(nob, conn, gameObject.scene);
        if (player != null)
        {
            player.ServerSetPaintOwner(owner);
            player.ServerSetAppearanceCode(appearanceCode);
            player.ServerSetMotionCode(motionCode);
            if (hasSavedSnapshot && levelStats != null)
            {
                player.ServerBroadcastLevelStats(
                    levelStats.CurrentExp,
                    levelStats.ClaimedTiles,
                    levelStats.LastClaimTimestamp);
            }
        }

        _spawnedClientIds.Add(conn.ClientId);
        _claimedRosterSlots.Add(slotIndex);
        _slotByClientId[conn.ClientId] = slotIndex;
        _userIdByClientId[conn.ClientId] = userId;
        _playerObjectByClientId[conn.ClientId] = nob.gameObject;
        NetworkRoundController.RegisterSpawnedPlayer(conn, nob.gameObject, owner);
        if (ServerLaunchMode.IsRoomServer)
        {
            SaveLastSnapshot(userId, nob.gameObject);
        }
        Debug.Log(
            $"[GameSpawn] Spawned client {conn.ClientId} " +
            $"in match slot {slotIndex + 1} as {owner}.");
    }

    private bool HasGamePlayer(NetworkConnection conn)
    {
        foreach (NetworkObject ownedObject in conn.Objects)
        {
            if (ownedObject != null &&
                ownedObject.gameObject.scene == gameObject.scene &&
                ownedObject.GetComponent<Player>() != null)
            {
                _spawnedClientIds.Add(conn.ClientId);
                if (TryGetSpawnSlot(conn, out int slotIndex))
                {
                    _claimedRosterSlots.Add(slotIndex);
                    _slotByClientId[conn.ClientId] = slotIndex;
                    string userId = LoadingTransitionController.GetServerRosterUserId(slotIndex);
                    _userIdByClientId[conn.ClientId] = userId;
                    _playerObjectByClientId[conn.ClientId] = ownedObject.gameObject;
                    SaveLastSnapshot(userId, ownedObject.gameObject);
                }

                NetworkRoundController.RegisterSpawnedPlayer(
                    conn,
                    ownedObject.gameObject,
                    GetOwnerForConnection(conn));
                return true;
            }
        }

        return false;
    }

    private bool TryTakeOverExistingPlayerForReconnect(NetworkConnection conn)
    {
        if (!ServerLaunchMode.IsRoomServer ||
            !LoadingTransitionController.HasServerRosterUserIds ||
            !RoomConnectionProfileCache.TryGetUserId(conn, out string userId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        foreach (KeyValuePair<int, string> pair in _userIdByClientId)
        {
            int previousClientId = pair.Key;
            if (previousClientId == conn.ClientId ||
                pair.Value != userId ||
                !_slotByClientId.TryGetValue(previousClientId, out int slotIndex) ||
                !_playerObjectByClientId.TryGetValue(previousClientId, out GameObject playerObject) ||
                playerObject == null)
            {
                continue;
            }

            NetworkObject networkObject = playerObject.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                continue;
            }

            // ownership 이전 직전에 저장해서 fallback snapshot이 최신 상태를 유지하게 합니다.
            SaveLastSnapshot(userId, playerObject);
            _supersededClientIds.Add(previousClientId);
            networkObject.GiveOwnership(conn);

            _spawnedClientIds.Remove(previousClientId);
            _slotByClientId.Remove(previousClientId);
            _userIdByClientId.Remove(previousClientId);
            _playerObjectByClientId.Remove(previousClientId);
            _selectedSkillByClientId.Remove(previousClientId);

            _spawnedClientIds.Add(conn.ClientId);
            _claimedRosterSlots.Add(slotIndex);
            _slotByClientId[conn.ClientId] = slotIndex;
            _userIdByClientId[conn.ClientId] = userId;
            _playerObjectByClientId[conn.ClientId] = playerObject;

            PaintOwner owner = GetOwnerForSlot(slotIndex);
            NetworkRoundController.RegisterSpawnedPlayer(conn, playerObject, owner);

            Player player = playerObject.GetComponent<Player>();
            if (player != null)
            {
                player.ServerSetPaintOwner(owner);
                player.ServerSetAppearanceCode(
                    LoadingTransitionController.GetServerRosterAppearanceCode(slotIndex));
                player.ServerSetMotionCode(
                    LoadingTransitionController.GetServerRosterMotionCode(slotIndex));
            }

            WeaponRuntimeController weaponController =
                playerObject.GetComponent<WeaponRuntimeController>();
            if (weaponController != null)
            {
                string selectedSkillId = LoadingTransitionController.GetServerRosterSkillId(slotIndex);
                if (_selectedSkillByClientId.TryGetValue(conn.ClientId, out string submittedSkillId))
                {
                    selectedSkillId = submittedSkillId;
                }

                weaponController.EquipById(
                    PlayerSkillLoadoutStore.NormalizeSkillId(selectedSkillId));
            }

            PlayerLevelStats levelStats = playerObject.GetComponent<PlayerLevelStats>();
            if (levelStats != null && player != null)
            {
                player.ServerBroadcastLevelStats(
                    levelStats.CurrentExp,
                    levelStats.ClaimedTiles,
                    levelStats.LastClaimTimestamp);
            }

            Debug.Log(
                "[GameSpawn] Reassigned reconnecting user " + userId +
                " from client " + previousClientId +
                " to client " + conn.ClientId +
                " in match slot " + (slotIndex + 1) + ".");

            NetworkConnection previousConnection =
                FindConnectionByClientId(previousClientId);
            if (previousConnection != null && previousConnection.IsActive)
            {
                previousConnection.Disconnect(false);
            }

            return true;
        }

        return false;
    }

    private bool TryGetSpawnSlot(NetworkConnection conn, out int slotIndex)
    {
        // 로컬 테스트 씬에는 캡처된 lobby roster가 없을 수 있으므로 그때는 접속 순서를 사용합니다.
        if (LoadingTransitionController.ServerRosterSlotCount <= 0)
        {
            slotIndex = _spawnedClientIds.Count;
            return true;
        }

        if (LoadingTransitionController.ServerRosterSlotCount > 0)
        {
            return TryGetAuthorizedRoomSlot(conn, out slotIndex) &&
                   !_claimedRosterSlots.Contains(slotIndex);
        }

        if (LoadingTransitionController.TryGetServerHumanSlot(
                conn.ClientId,
                out slotIndex))
        {
            return !_claimedRosterSlots.Contains(slotIndex);
        }

        for (int i = 0; i < LoadingTransitionController.ServerRosterSlotCount; i++)
        {
            if (_claimedRosterSlots.Contains(i) ||
                !LoadingTransitionController.IsServerRosterHumanSlot(i))
            {
                continue;
            }

            slotIndex = i;
            Debug.LogWarning(
                $"[GameSpawn] Client {conn.ClientId} was not found in the " +
                $"captured lobby roster. Using unclaimed human slot {i + 1}.");
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private bool TryGetAuthorizedRoomSlot(
        NetworkConnection connection,
        out int slotIndex)
    {
        slotIndex = -1;
        if (connection == null)
        {
            return false;
        }

        if (LoadingTransitionController.ServerRosterSlotCount <= 0)
        {
            return true;
        }

        bool hasUserId =
            RoomConnectionProfileCache.TryGetUserId(connection, out string userId);
        if (hasUserId &&
            LoadingTransitionController.TryGetServerHumanSlotByUserId(
                userId,
                out slotIndex))
        {
            return true;
        }

        if (!LoadingTransitionController.TryGetServerHumanSlot(
                connection.ClientId,
                out slotIndex))
        {
            slotIndex = -1;
            return false;
        }

        string expectedUserId =
            LoadingTransitionController.GetServerRosterUserId(slotIndex);
        if (string.IsNullOrWhiteSpace(expectedUserId))
        {
            return true;
        }

        if (hasUserId &&
            string.Equals(expectedUserId, userId, System.StringComparison.Ordinal))
        {
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private void EnforceRoomRosterAdmission()
    {
        if (LoadingTransitionController.ServerRosterSlotCount <= 0 ||
            _networkManager == null ||
            _networkManager.ServerManager == null)
        {
            _roomAdmissionDeadlines.Clear();
            return;
        }

        List<NetworkConnection> connections =
            new List<NetworkConnection>(_networkManager.ServerManager.Clients.Values);
        float now = Time.unscaledTime;
        for (int i = 0; i < connections.Count; i++)
        {
            NetworkConnection connection = connections[i];
            if (connection == null || !connection.IsActive)
            {
                continue;
            }

            if (TryGetAuthorizedRoomSlot(connection, out int _))
            {
                _roomAdmissionDeadlines.Remove(connection.ClientId);
                continue;
            }

            if (RoomConnectionProfileCache.TryGetUserId(
                    connection,
                    out string _))
            {
                RejectUnauthorizedRoomConnection(
                    connection,
                    "profile user is not part of the locked match roster");
                continue;
            }

            if (!_roomAdmissionDeadlines.TryGetValue(
                    connection.ClientId,
                    out float deadline))
            {
                _roomAdmissionDeadlines[connection.ClientId] =
                    now + RoomAdmissionProfileTimeoutSeconds;
                continue;
            }

            if (now >= deadline)
            {
                RejectUnauthorizedRoomConnection(
                    connection,
                    "profile was not received before the admission timeout");
            }
        }
    }

    private void RejectUnauthorizedRoomConnection(
        NetworkConnection connection,
        string reason)
    {
        if (connection == null)
        {
            return;
        }

        _roomAdmissionDeadlines.Remove(connection.ClientId);
        _supersededClientIds.Add(connection.ClientId);
        string roomLabel = string.IsNullOrWhiteSpace(ServerLaunchMode.RoomId)
            ? "single-room match"
            : ServerLaunchMode.RoomId;
        Debug.LogWarning(
            "[RoomAdmission] Disconnecting client " + connection.ClientId +
            " from locked room " + roomLabel +
            ": " + reason + ".");
        if (connection.IsActive)
        {
            connection.Disconnect(false);
        }
    }

    private void CaptureActivePlayerPoses()
    {
        // 모바일 disconnect나 app pause를 대비한 가벼운 주기 snapshot입니다. 이 데이터는 room server에서만 필요합니다.
        if (!ServerLaunchMode.IsRoomServer ||
            Time.unscaledTime < _nextPoseCaptureTime)
        {
            return;
        }

        _nextPoseCaptureTime = Time.unscaledTime + 0.25f;
        foreach (KeyValuePair<int, GameObject> pair in _playerObjectByClientId)
        {
            if (pair.Value == null ||
                !_userIdByClientId.TryGetValue(pair.Key, out string userId))
            {
                continue;
            }

            SaveLastSnapshot(userId, pair.Value);
        }
    }

    private void CaptureLastPose(int clientId)
    {
        if (!ServerLaunchMode.IsRoomServer ||
            !_userIdByClientId.TryGetValue(clientId, out string userId) ||
            !_playerObjectByClientId.TryGetValue(clientId, out GameObject playerObject) ||
            playerObject == null)
        {
            return;
        }

        SaveLastSnapshot(userId, playerObject);
    }

    private void SaveLastSnapshot(string userId, GameObject playerObject)
    {
        userId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (string.IsNullOrWhiteSpace(userId) || playerObject == null)
        {
            return;
        }

        Transform playerTransform = playerObject.transform;
        PlayerLevelStats levelStats = playerObject.GetComponent<PlayerLevelStats>();
        _lastSnapshotByUserId[userId] = new SavedPlayerSnapshot
        {
            Position = playerTransform.position,
            Rotation = playerTransform.rotation,
            CurrentExp = levelStats != null ? levelStats.CurrentExp : 0,
            ClaimedTiles = levelStats != null ? levelStats.ClaimedTiles : 0,
            LastClaimTimestamp = levelStats != null ? levelStats.LastClaimTimestamp : 0f
        };
    }

    private bool TryGetReconnectSnapshot(string userId, out SavedPlayerSnapshot snapshot)
    {
        userId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (ServerLaunchMode.IsRoomServer &&
            !string.IsNullOrWhiteSpace(userId) &&
            _lastSnapshotByUserId.TryGetValue(userId, out snapshot))
        {
            return true;
        }

        snapshot = default;
        return false;
    }

    private static bool ContainsConnection(
        List<NetworkConnection> connections,
        int clientId)
    {
        for (int i = 0; i < connections.Count; i++)
        {
            NetworkConnection connection = connections[i];
            if (connection != null && connection.ClientId == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private NetworkConnection FindConnectionByClientId(int clientId)
    {
        if (_networkManager == null || _networkManager.ServerManager == null)
        {
            return null;
        }

        return _networkManager.ServerManager.Clients.TryGetValue(
            clientId,
            out NetworkConnection connection)
            ? connection
            : null;
    }

    private PaintOwner GetOwnerForConnection(NetworkConnection conn)
    {
        return LoadingTransitionController.TryGetServerHumanSlot(
            conn.ClientId,
            out int slotIndex)
            ? GetOwnerForSlot(slotIndex)
            : PaintOwner.Neutral;
    }

    private static PaintOwner GetOwnerForSlot(int slotIndex)
    {
        int ownerValue = slotIndex + 1;
        return ownerValue >= (int)PaintOwner.Player1 &&
               ownerValue <= (int)PaintOwner.Player4
            ? (PaintOwner)ownerValue
            : PaintOwner.Neutral;
    }

    private Vector3 GetSpawnPosition(int spawnIndex)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            if (spawnIndex < spawnPoints.Length)
            {
                Transform spawn = spawnPoints[spawnIndex];
                return spawn != null ? spawn.position : Vector3.zero;
            }

            Transform fallbackSpawn = spawnPoints[0];
            Vector3 origin = fallbackSpawn != null ? fallbackSpawn.position : Vector3.zero;
            return origin + Vector3.right * (fallbackSpawnSpacing * spawnIndex);
        }

        return Vector3.zero;
    }

    private struct SavedPlayerSnapshot
    {
        // 전체 player object를 직렬화하지 않고도 재접속이 이어지는 느낌을 주기 위한 최소 상태입니다.
        public Vector3 Position;
        public Quaternion Rotation;
        public int CurrentExp;
        public int ClaimedTiles;
        public float LastClaimTimestamp;
    }
}
