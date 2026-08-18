using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchmakingManager : NetworkBehaviour
{
    public static MatchmakingManager Instance;
    private const int RoomAssignmentBroadcastRepeats = 4;
    private const float RoomAssignmentBroadcastInterval = 0.25f;

    [SerializeField] private GameObject lobbyPlayerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float lobbySpawnHeight = 0.3f;

    [Header("Game Start")]
    [SerializeField] private int requiredPlayerCount = 4;
    [SerializeField] private float gameStartDelay = 5f;
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Matchmaking Timer")]
    [SerializeField] private float matchTimeout = 60f;
    [SerializeField] private float minimumMatchTimeout = 30f;

    [Header("Multi Room")]
    [SerializeField] private bool useProcessRoomsOnMasterServer = true;
    [SerializeField] private float roomAssignmentDisconnectDelay = 1.5f;

    public readonly SyncVar<int> lobbyLeaderClientId = new SyncVar<int>(-1);
    public readonly SyncVar<int> lobbyHumanCount = new SyncVar<int>(0);
    public readonly SyncVar<int> lobbyParticipantCount = new SyncVar<int>(0);
    public readonly SyncVar<int> lobbyRequiredCount = new SyncVar<int>(4);
    public readonly SyncVar<int> matchWaitRemainingSeconds = new SyncVar<int>(-1);
    public readonly SyncVar<int> gameStartRemainingSeconds = new SyncVar<int>(-1);

    private readonly List<NetworkConnection> joinOrder = new List<NetworkConnection>();
    private NetworkConnection[] playerSlots;
    private NetworkObject[] lobbyPlayerSlots;
    private NetworkObject[] aiPlayerSlots;
    private string[] aiDisplayNames;
    private string[] aiAppearanceCodes;
    private string[] aiMotionCodes;
    private readonly Dictionary<int, string> playerNicknamesByClientId =
        new Dictionary<int, string>();
    private readonly Dictionary<int, string> playerAppearancesByClientId =
        new Dictionary<int, string>();
    private readonly Dictionary<int, string> playerMotionsByClientId =
        new Dictionary<int, string>();
    private readonly Dictionary<int, string> playerSkillsByClientId =
        new Dictionary<int, string>();
    private readonly Dictionary<int, string> playerUserIdsByClientId =
        new Dictionary<int, string>();

    private Coroutine matchCountdownCoroutine;
    private Coroutine gameStartCoroutine;
    private bool matchmakingTimedOut;
    private bool isGameStarting;
    private RoomProcessLauncher roomProcessLauncher;
    private readonly HashSet<int> loadedStartSceneHandlerClientIds = new HashSet<int>();
    private readonly HashSet<int> redirectingClientIds = new HashSet<int>();
    private readonly List<NetworkConnection> pendingLobbyConnections =
        new List<NetworkConnection>();
    private bool transitioningToGameScene;
    private bool isLobbyAdmissionLocked;

    private void Awake()
    {
        Instance = this;

        if (GetComponent<LobbyWaitTimerView>() == null)
        {
            gameObject.AddComponent<LobbyWaitTimerView>();
        }

        RoomAssignmentClient.EnsureInstance();

        roomProcessLauncher = GetComponent<RoomProcessLauncher>();
        if (roomProcessLauncher == null)
        {
            roomProcessLauncher = gameObject.AddComponent<RoomProcessLauncher>();
        }

        if (ServerLaunchMode.IsRoomServer &&
            RoomServerLifecycle.Instance == null)
        {
            RoomServerLifecycle.EnsureInstance();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        transitioningToGameScene = false;
        isLobbyAdmissionLocked = false;
        requiredPlayerCount = Mathf.Max(1, requiredPlayerCount);
        if (ServerLaunchMode.IsRoomServer)
        {
            minimumMatchTimeout = 1.5f;
            matchTimeout = 1.5f;
            gameStartDelay = 0f;
            Debug.Log(
                "[Rooms] Room server matchmaking timeout set to " +
                matchTimeout.ToString("0.##") +
                "s, start delay=" + gameStartDelay.ToString("0.##") + "s.");
        }

        matchTimeout = Mathf.Max(minimumMatchTimeout, matchTimeout);
        playerSlots = new NetworkConnection[requiredPlayerCount];
        lobbyPlayerSlots = new NetworkObject[requiredPlayerCount];
        aiPlayerSlots = new NetworkObject[requiredPlayerCount];
        aiDisplayNames = new string[requiredPlayerCount];
        aiAppearanceCodes = new string[requiredPlayerCount];
        aiMotionCodes = new string[requiredPlayerCount];
        lobbyRequiredCount.Value = requiredPlayerCount;
        ResetLobbyTimerState();

        ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        ServerManager.OnAuthenticationResult += ServerManager_OnAuthenticationResult;
        ServerManager.RegisterBroadcast<PlayerProfileBroadcast>(
            OnPlayerProfileBroadcast,
            false);

        JoinExistingConnections();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (!transitioningToGameScene)
        {
            StopMatchmakingTimer();
            StopGameStartCountdown();
            ResetLobbyTimerState();
        }

        UnregisterServerCallbacks();
        playerNicknamesByClientId.Clear();
        playerAppearancesByClientId.Clear();
        playerMotionsByClientId.Clear();
        playerSkillsByClientId.Clear();
        playerUserIdsByClientId.Clear();
        aiAppearanceCodes = null;
        aiMotionCodes = null;
        aiDisplayNames = null;
        loadedStartSceneHandlerClientIds.Clear();
        redirectingClientIds.Clear();
        pendingLobbyConnections.Clear();
    }

    public bool IsLobbyLeader(NetworkConnection connection)
    {
        return connection != null &&
               connection.ClientId == lobbyLeaderClientId.Value;
    }

    private void ServerManager_OnRemoteConnectionState(
        NetworkConnection connection,
        RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            JoinLobbyRoomWhenReady(connection);
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            UnregisterLoadedStartScenesHandler(connection);
            bool wasPending = pendingLobbyConnections.Remove(connection);
            if (connection != null)
            {
                redirectingClientIds.Remove(connection.ClientId);
            }

            LeaveLobbyRoom(connection);
            if (wasPending && connection != null)
            {
                playerNicknamesByClientId.Remove(connection.ClientId);
                playerAppearancesByClientId.Remove(connection.ClientId);
                playerMotionsByClientId.Remove(connection.ClientId);
                playerSkillsByClientId.Remove(connection.ClientId);
                playerUserIdsByClientId.Remove(connection.ClientId);
                RoomConnectionProfileCache.Remove(connection);
            }
        }
    }

    private void JoinExistingConnections()
    {
        if (ServerManager == null || ServerManager.Clients == null)
        {
            return;
        }

        foreach (NetworkConnection connection in ServerManager.Clients.Values)
        {
            JoinLobbyRoomWhenReady(connection);
        }
    }

    private void ServerManager_OnAuthenticationResult(
        NetworkConnection connection,
        bool authenticated)
    {
        if (!authenticated)
        {
            return;
        }

        JoinLobbyRoomWhenReady(connection);
    }

    private void JoinLobbyRoomWhenReady(NetworkConnection connection)
    {
        if (connection == null || FindPlayerSlot(connection) >= 0)
        {
            return;
        }

        if (redirectingClientIds.Contains(connection.ClientId))
        {
            return;
        }

        if (!connection.IsAuthenticated)
        {
            return;
        }

        if (isLobbyAdmissionLocked)
        {
            HoldOrRejectLateConnection(connection);
            return;
        }

        if (!connection.LoadedStartScenes(true))
        {
            RegisterLoadedStartScenesHandler(connection);
            return;
        }

        UnregisterLoadedStartScenesHandler(connection);
        JoinLobbyRoom(connection);
    }

    private void RegisterLoadedStartScenesHandler(NetworkConnection connection)
    {
        if (connection == null ||
            loadedStartSceneHandlerClientIds.Contains(connection.ClientId))
        {
            return;
        }

        connection.OnLoadedStartScenes += Connection_OnLoadedStartScenes;
        loadedStartSceneHandlerClientIds.Add(connection.ClientId);
    }

    private void UnregisterLoadedStartScenesHandler(NetworkConnection connection)
    {
        if (connection == null ||
            !loadedStartSceneHandlerClientIds.Remove(connection.ClientId))
        {
            return;
        }

        connection.OnLoadedStartScenes -= Connection_OnLoadedStartScenes;
    }

    private void Connection_OnLoadedStartScenes(
        NetworkConnection connection,
        bool asServer)
    {
        if (!asServer)
        {
            return;
        }

        JoinLobbyRoomWhenReady(connection);
    }

    private void JoinLobbyRoom(NetworkConnection connection)
    {
        if (isLobbyAdmissionLocked ||
            isGameStarting ||
            FindPlayerSlot(connection) >= 0)
        {
            if (isLobbyAdmissionLocked && FindPlayerSlot(connection) < 0)
            {
                HoldOrRejectLateConnection(connection);
            }

            return;
        }

        if (!HasEnoughSpawnPoints())
        {
            Debug.LogError($"[Server] At least {requiredPlayerCount} lobby spawn points are required.");
            return;
        }

        int slotIndex = FindFirstEmptySlot();
        if (slotIndex < 0)
        {
            Debug.LogWarning($"[Server] Lobby is full. Client {connection.ClientId} cannot join.");
            return;
        }

        playerSlots[slotIndex] = connection;
        joinOrder.Add(connection);

        Transform targetPoint = spawnPoints[slotIndex];
        Vector3 spawnPosition = GetLobbySpawnPosition(targetPoint);
        GameObject playerObject = Instantiate(
            lobbyPlayerPrefab,
            spawnPosition,
            targetPoint.rotation);
        Spawn(playerObject, connection);

        lobbyPlayerSlots[slotIndex] = playerObject.GetComponent<NetworkObject>();

        LobbyPlayer lobbyPlayer = playerObject.GetComponent<LobbyPlayer>();
        if (lobbyPlayer != null)
        {
            lobbyPlayer.roomIndex.Value = slotIndex;
            lobbyPlayer.playerName.Value = GetDisplayName(connection, slotIndex);
            lobbyPlayer.appearanceCode.Value = GetAppearanceCode(connection);
            lobbyPlayer.motionCode.Value = GetMotionCode(connection);
        }

        UpdateLobbyLeader();
        UpdateLobbyCounts();
        Debug.Log($"[Server] Client {connection.ClientId} joined lobby slot {slotIndex + 1}.");

        if (RealPlayerCount == 1)
        {
            StartMatchmakingTimer();
        }

        if (TotalParticipantCount >= requiredPlayerCount)
        {
            StopMatchmakingTimer();
            StartGameStartCountdown();
        }
    }

    private void LeaveLobbyRoom(NetworkConnection connection)
    {
        int slotIndex = FindPlayerSlot(connection);
        if (slotIndex < 0)
        {
            return;
        }

        playerSlots[slotIndex] = null;
        if (connection != null)
        {
            playerNicknamesByClientId.Remove(connection.ClientId);
            playerAppearancesByClientId.Remove(connection.ClientId);
            playerUserIdsByClientId.Remove(connection.ClientId);
            RoomConnectionProfileCache.Remove(connection);
        }
        joinOrder.Remove(connection);

        NetworkObject lobbyPlayerObject = lobbyPlayerSlots[slotIndex];
        lobbyPlayerSlots[slotIndex] = null;
        if (lobbyPlayerObject != null && lobbyPlayerObject.IsSpawned)
        {
            ServerManager.Despawn(lobbyPlayerObject);
        }

        UpdateLobbyLeader();
        UpdateLobbyCounts();
        Debug.Log($"[Server] Client {connection.ClientId} left lobby slot {slotIndex + 1}.");

        if (matchmakingTimedOut)
        {
            FillEmptySlotsWithAi();
        }

        if (RealPlayerCount == 0)
        {
            StopMatchmakingTimer();
            StopGameStartCountdown();
            matchmakingTimedOut = false;
            RemoveAiPlayers();
            ResetLobbyTimerState();
            AdmitPendingLobbyConnections();
            return;
        }

        if (TotalParticipantCount < requiredPlayerCount)
        {
            StopGameStartCountdown();

            if (!matchmakingTimedOut)
            {
                StartMatchmakingTimer();
            }

            AdmitPendingLobbyConnections();
        }
        else
        {
            StartGameStartCountdown();
        }
    }

    private void UpdateLobbyLeader()
    {
        joinOrder.RemoveAll(connection => connection == null || FindPlayerSlot(connection) < 0);

        NetworkConnection leader = joinOrder.Count > 0 ? joinOrder[0] : null;
        lobbyLeaderClientId.Value = leader != null ? leader.ClientId : -1;

        for (int i = 0; i < lobbyPlayerSlots.Length; i++)
        {
            NetworkObject playerObject = lobbyPlayerSlots[i];
            if (playerObject == null)
            {
                continue;
            }

            LobbyPlayer lobbyPlayer = playerObject.GetComponent<LobbyPlayer>();
            if (lobbyPlayer != null)
            {
                lobbyPlayer.isLobbyLeader.Value =
                    playerSlots[i] != null && playerSlots[i] == leader;
            }
        }
    }

    private int FindPlayerSlot(NetworkConnection connection)
    {
        if (connection == null || playerSlots == null)
        {
            return -1;
        }

        for (int i = 0; i < playerSlots.Length; i++)
        {
            if (playerSlots[i] == connection)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindFirstEmptySlot()
    {
        for (int i = 0; i < requiredPlayerCount; i++)
        {
            if (playerSlots[i] == null && aiPlayerSlots[i] == null)
            {
                return i;
            }
        }

        return -1;
    }

    private void StartMatchmakingTimer()
    {
        if (matchmakingTimedOut || isGameStarting || matchCountdownCoroutine != null)
        {
            return;
        }

        matchCountdownCoroutine = StartCoroutine(MatchCountdownRoutine());
        Debug.Log(
            "[Server] Waiting for players. " +
            RealPlayerCount + "/" + requiredPlayerCount +
            " human player(s), AI fill in " + matchTimeout + " seconds.");
    }

    private void StopMatchmakingTimer()
    {
        if (matchCountdownCoroutine == null)
        {
            return;
        }

        StopCoroutine(matchCountdownCoroutine);
        matchCountdownCoroutine = null;
        matchWaitRemainingSeconds.Value = -1;
        Debug.Log("[Server] Matchmaking timer stopped.");
    }

    private void StartGameStartCountdown()
    {
        bool assigningProcessRoom = ShouldAssignProcessRoom();
        if (gameStartCoroutine != null ||
            isGameStarting ||
            (!assigningProcessRoom && TotalParticipantCount < requiredPlayerCount) ||
            (assigningProcessRoom && RealPlayerCount <= 0))
        {
            return;
        }

        isLobbyAdmissionLocked = true;
        gameStartCoroutine = StartCoroutine(GameStartCountdownRoutine());
    }

    private void StopGameStartCountdown()
    {
        if (gameStartCoroutine == null || isGameStarting)
        {
            return;
        }

        StopCoroutine(gameStartCoroutine);
        gameStartCoroutine = null;
        gameStartRemainingSeconds.Value = -1;
        isLobbyAdmissionLocked = false;
        Debug.Log("[Server] Game start countdown cancelled because the lobby is not full.");
    }

    private IEnumerator GameStartCountdownRoutine()
    {
        bool assigningProcessRoom = ShouldAssignProcessRoom();
        string countdownMessage = assigningProcessRoom
            ? "[Rooms] Match ready. Assigning room in "
            : "[Server] Lobby full. Starting game in ";
        Debug.Log(countdownMessage + gameStartDelay.ToString("0.##") + " seconds.");

        float endTime = Time.unscaledTime + gameStartDelay;
        while (Time.unscaledTime < endTime)
        {
            gameStartRemainingSeconds.Value =
                Mathf.CeilToInt(Mathf.Max(0f, endTime - Time.unscaledTime));
            yield return null;
        }

        gameStartCoroutine = null;
        gameStartRemainingSeconds.Value = 0;
        if ((!assigningProcessRoom && TotalParticipantCount < requiredPlayerCount) ||
            (assigningProcessRoom && RealPlayerCount <= 0))
        {
            gameStartRemainingSeconds.Value = -1;
            isLobbyAdmissionLocked = false;
            RemoveAiPlayers();
            AdmitPendingLobbyConnections();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("[Server] Game scene name is not configured.");
            gameStartRemainingSeconds.Value = -1;
            isLobbyAdmissionLocked = false;
            RemoveAiPlayers();
            AdmitPendingLobbyConnections();
            yield break;
        }

        isGameStarting = true;
        matchWaitRemainingSeconds.Value = -1;

        if (ShouldAssignProcessRoom())
        {
            yield return AssignProcessRoomRoutine();
            yield break;
        }

        LoadingTransitionController.CaptureServerRoster(
            playerSlots,
            aiPlayerSlots,
            CreateRosterDisplayNames(),
            CreateRosterAppearanceCodes(),
            CreateRosterMotionCodes(),
            CreateRosterSkillIds(),
            CreateRosterUserIds());

        PrepareForGameSceneTransition();

        if (LoadingTransitionController.Instance != null)
        {
            LoadingTransitionController.Instance.BeginServerTransition(gameSceneName);
        }
        else
        {
            Debug.LogError(
                "[Server] LoadingTransitionController is missing from the lobby scene.");
        }

        SceneLoadData sceneLoadData = new SceneLoadData(gameSceneName)
        {
            ReplaceScenes = ReplaceOption.All
        };

        SceneManager.LoadGlobalScenes(sceneLoadData);
    }

    private void PrepareForGameSceneTransition()
    {
        transitioningToGameScene = true;
        matchmakingTimedOut = false;
        matchCountdownCoroutine = null;
        gameStartCoroutine = null;
        UnregisterServerCallbacks();
        DespawnLobbyPlayersForTransition();
        RemoveAiPlayersForTransition();
        joinOrder.Clear();
        loadedStartSceneHandlerClientIds.Clear();
        redirectingClientIds.Clear();
    }

    private void UnregisterServerCallbacks()
    {
        if (ServerManager == null)
        {
            return;
        }

        ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        ServerManager.OnAuthenticationResult -= ServerManager_OnAuthenticationResult;
        ServerManager.UnregisterBroadcast<PlayerProfileBroadcast>(
            OnPlayerProfileBroadcast);
    }

    private void DespawnLobbyPlayersForTransition()
    {
        if (lobbyPlayerSlots == null)
        {
            return;
        }

        for (int i = 0; i < lobbyPlayerSlots.Length; i++)
        {
            NetworkObject lobbyPlayerObject = lobbyPlayerSlots[i];
            lobbyPlayerSlots[i] = null;
            if (lobbyPlayerObject != null && lobbyPlayerObject.IsSpawned)
            {
                ServerManager.Despawn(lobbyPlayerObject);
            }
        }
    }

    private void RemoveAiPlayersForTransition()
    {
        if (aiPlayerSlots == null)
        {
            return;
        }

        for (int i = 0; i < aiPlayerSlots.Length; i++)
        {
            NetworkObject aiPlayer = aiPlayerSlots[i];
            aiPlayerSlots[i] = null;
            if (aiPlayer != null && aiPlayer.IsSpawned)
            {
                ServerManager.Despawn(aiPlayer);
            }
        }
    }

    private bool ShouldAssignProcessRoom()
    {
        return useProcessRoomsOnMasterServer &&
               Application.isBatchMode &&
               ServerLaunchMode.IsMasterServer;
    }

    private IEnumerator AssignProcessRoomRoutine()
    {
        if (roomProcessLauncher == null)
        {
            roomProcessLauncher = GetComponent<RoomProcessLauncher>();
        }

        if (roomProcessLauncher == null ||
            !roomProcessLauncher.TryLaunchRoom(out RoomConnectionTarget target))
        {
            Debug.LogError("[Rooms] Cannot allocate a room server. Returning lobby to waiting state.");
            isGameStarting = false;
            gameStartRemainingSeconds.Value = -1;
            matchmakingTimedOut = false;
            isLobbyAdmissionLocked = false;
            RemoveAiPlayers();
            AdmitPendingLobbyConnections();
            if (RealPlayerCount > 0)
            {
                StartMatchmakingTimer();
            }

            yield break;
        }

        RoomAssignmentBroadcast message = new RoomAssignmentBroadcast
        {
            RoomId = target.RoomId,
            Address = target.Address,
            Port = target.Port,
            ReconnectGraceSeconds = target.ReconnectGraceSeconds
        };

        for (int repeat = 0; repeat < RoomAssignmentBroadcastRepeats; repeat++)
        {
            for (int i = 0; i < playerSlots.Length; i++)
            {
                NetworkConnection connection = playerSlots[i];
                if (connection == null || !connection.IsActive)
                {
                    continue;
                }

                if (repeat == 0 &&
                    playerUserIdsByClientId.TryGetValue(connection.ClientId, out string userId))
                {
                    roomProcessLauncher.RememberAssignment(userId, target);
                }

                ServerManager.Broadcast(connection, message);
            }

            if (repeat < RoomAssignmentBroadcastRepeats - 1)
            {
                yield return new WaitForSecondsRealtime(RoomAssignmentBroadcastInterval);
            }
        }

        Debug.Log(
            "[Rooms] Assigned " + RealPlayerCount +
            " player(s) to " + target.RoomId +
            " at " + target.Address + ":" + target.Port + ".");

        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, roomAssignmentDisconnectDelay));
        ResetMasterLobbyAfterRoomAssignment();
    }

    private void ResetMasterLobbyAfterRoomAssignment()
    {
        for (int i = 0; i < playerSlots.Length; i++)
        {
            NetworkConnection connection = playerSlots[i];
            if (connection == null)
            {
                continue;
            }

            playerNicknamesByClientId.Remove(connection.ClientId);
            playerAppearancesByClientId.Remove(connection.ClientId);
            playerMotionsByClientId.Remove(connection.ClientId);
            playerUserIdsByClientId.Remove(connection.ClientId);
            if (connection.IsActive)
            {
                connection.Disconnect(false);
            }
        }

        for (int i = 0; i < lobbyPlayerSlots.Length; i++)
        {
            NetworkObject lobbyPlayerObject = lobbyPlayerSlots[i];
            lobbyPlayerSlots[i] = null;
            if (lobbyPlayerObject != null && lobbyPlayerObject.IsSpawned)
            {
                ServerManager.Despawn(lobbyPlayerObject);
            }
        }

        RemoveAiPlayers();
        joinOrder.Clear();
        loadedStartSceneHandlerClientIds.Clear();
        redirectingClientIds.Clear();
        playerSlots = new NetworkConnection[requiredPlayerCount];
        lobbyPlayerSlots = new NetworkObject[requiredPlayerCount];
        aiPlayerSlots = new NetworkObject[requiredPlayerCount];
        aiDisplayNames = new string[requiredPlayerCount];
        aiAppearanceCodes = new string[requiredPlayerCount];
        aiMotionCodes = new string[requiredPlayerCount];
        matchmakingTimedOut = false;
        isGameStarting = false;
        isLobbyAdmissionLocked = false;
        ResetLobbyTimerState();
        AdmitPendingLobbyConnections();
    }

    private void HoldOrRejectLateConnection(NetworkConnection connection)
    {
        if (connection == null || !connection.IsActive)
        {
            return;
        }

        if (ShouldAssignProcessRoom())
        {
            if (!pendingLobbyConnections.Contains(connection))
            {
                pendingLobbyConnections.Add(connection);
                ServerManager.Broadcast(connection, new MatchmakingQueueStateBroadcast
                {
                    WaitingForNextMatch = true
                });
                Debug.Log(
                    "[Rooms] Client " + connection.ClientId +
                    " is waiting for the next match roster.");
            }

            return;
        }

        Debug.LogWarning(
            "[Server] Client " + connection.ClientId +
            " rejected because the match roster is already locked.");
        connection.Disconnect(false);
    }

    private void AdmitPendingLobbyConnections()
    {
        if (isLobbyAdmissionLocked || pendingLobbyConnections.Count == 0)
        {
            return;
        }

        NetworkConnection[] pending = pendingLobbyConnections.ToArray();
        pendingLobbyConnections.Clear();
        for (int i = 0; i < pending.Length; i++)
        {
            NetworkConnection connection = pending[i];
            if (connection == null || !connection.IsActive)
            {
                continue;
            }

            ServerManager.Broadcast(connection, new MatchmakingQueueStateBroadcast
            {
                WaitingForNextMatch = false
            });
            JoinLobbyRoomWhenReady(connection);
        }
    }

    private IEnumerator MatchCountdownRoutine()
    {
        Debug.Log($"[Server] Matchmaking timer started for {matchTimeout} seconds.");
        float endTime = Time.unscaledTime + matchTimeout;
        while (Time.unscaledTime < endTime)
        {
            matchWaitRemainingSeconds.Value =
                Mathf.CeilToInt(Mathf.Max(0f, endTime - Time.unscaledTime));
            yield return null;
        }

        matchCountdownCoroutine = null;
        matchWaitRemainingSeconds.Value = 0;
        matchmakingTimedOut = true;

        // Close admission before creating AI players so no connection can enter
        // between roster completion and the game-start countdown.
        isLobbyAdmissionLocked = true;
        FillEmptySlotsWithAi();
        if (TotalParticipantCount < requiredPlayerCount)
        {
            isLobbyAdmissionLocked = false;
            RemoveAiPlayers();
            AdmitPendingLobbyConnections();
            yield break;
        }

        StartGameStartCountdown();
    }

    private void FillEmptySlotsWithAi()
    {
        if (!HasEnoughSpawnPoints())
        {
            Debug.LogError($"[Server] At least {requiredPlayerCount} lobby spawn points are required.");
            return;
        }

        for (int i = 0; i < requiredPlayerCount; i++)
        {
            if (playerSlots[i] != null || aiPlayerSlots[i] != null)
            {
                continue;
            }

            Transform targetPoint = spawnPoints[i];
            Vector3 spawnPosition = GetLobbySpawnPosition(targetPoint);
            GameObject aiObject = Instantiate(
                lobbyPlayerPrefab,
                spawnPosition,
                targetPoint.rotation);
            Spawn(aiObject, null);

            aiPlayerSlots[i] = aiObject.GetComponent<NetworkObject>();

            LobbyPlayer aiLobbyPlayer = aiObject.GetComponent<LobbyPlayer>();
            if (aiLobbyPlayer != null)
            {
                aiLobbyPlayer.roomIndex.Value = i;
                aiLobbyPlayer.playerName.Value = GetOrCreateAiDisplayName(i);
                aiLobbyPlayer.appearanceCode.Value = GetOrCreateAiAppearanceCode(i);
                aiLobbyPlayer.motionCode.Value = GetOrCreateAiMotionCode(i);
                aiLobbyPlayer.isLobbyLeader.Value = false;
            }

            Debug.Log("[Server] AI filled lobby slot " + (i + 1) + ".");
        }

        UpdateLobbyCounts();
    }

    private void RemoveAiPlayers()
    {
        if (aiPlayerSlots == null)
        {
            return;
        }

        for (int i = 0; i < aiPlayerSlots.Length; i++)
        {
            NetworkObject aiPlayer = aiPlayerSlots[i];
            aiPlayerSlots[i] = null;
            if (aiAppearanceCodes != null && i < aiAppearanceCodes.Length)
            {
                aiAppearanceCodes[i] = string.Empty;
            }
            if (aiDisplayNames != null && i < aiDisplayNames.Length)
            {
                aiDisplayNames[i] = string.Empty;
            }
            if (aiMotionCodes != null && i < aiMotionCodes.Length)
            {
                aiMotionCodes[i] = string.Empty;
            }

            if (aiPlayer != null && aiPlayer.IsSpawned)
            {
                ServerManager.Despawn(aiPlayer);
            }
        }

        UpdateLobbyCounts();
    }

    private void UpdateLobbyCounts()
    {
        lobbyRequiredCount.Value = requiredPlayerCount;
        lobbyHumanCount.Value = RealPlayerCount;
        lobbyParticipantCount.Value = TotalParticipantCount;
    }

    private void OnPlayerProfileBroadcast(
        NetworkConnection connection,
        PlayerProfileBroadcast message,
        Channel channel)
    {
        ServerApplyPlayerProfile(
            connection,
            message.Nickname,
            message.AppearanceCode,
            message.MotionCode,
            message.SelectedSkillId,
            message.UserId);
    }

    public void ServerApplyPlayerProfile(
        NetworkConnection connection,
        string rawNickname,
        string rawAppearanceCode,
        string rawMotionCode = null,
        string rawSelectedSkillId = null,
        string rawUserId = null)
    {
        if (connection == null)
        {
            return;
        }

        string appearanceCode =
            CharacterAppearanceStore.NormalizeCode(rawAppearanceCode);
        string motionCode =
            CharacterMotionStore.NormalizeProfileMotionCode(rawMotionCode);
        string selectedSkillId =
            PlayerSkillLoadoutStore.NormalizeSkillId(rawSelectedSkillId);
        string nicknameSource = ResolveNicknameSource(rawNickname, rawAppearanceCode);
        string nickname = ResolveProfileNickname(
            connection,
            PlayerProfile.SanitizeNickname(nicknameSource));

        if (string.IsNullOrWhiteSpace(appearanceCode) &&
            CharacterAppearanceStore.HasSelections(rawNickname))
        {
            appearanceCode = CharacterAppearanceStore.NormalizeCode(rawNickname);
        }

        if (string.IsNullOrWhiteSpace(appearanceCode) &&
            CharacterAppearanceStore.TryCreateSingleSelectionCode(
                rawAppearanceCode,
                out string inferredAppearanceCodeFromAppearance))
        {
            appearanceCode = inferredAppearanceCodeFromAppearance;
        }

        if (string.IsNullOrWhiteSpace(appearanceCode) &&
            CharacterAppearanceStore.TryCreateSingleSelectionCode(
                rawNickname,
                out string inferredAppearanceCode))
        {
            appearanceCode = inferredAppearanceCode;
        }

        if (!string.IsNullOrWhiteSpace(rawUserId))
        {
            string userId = rawUserId.Trim();
            playerUserIdsByClientId[connection.ClientId] = userId;
            RoomConnectionProfileCache.Set(connection, userId);
            if (TryRedirectReconnect(connection, userId))
            {
                return;
            }
        }

        playerNicknamesByClientId[connection.ClientId] = nickname;
        playerAppearancesByClientId[connection.ClientId] = appearanceCode;
        playerMotionsByClientId[connection.ClientId] = motionCode;
        playerSkillsByClientId[connection.ClientId] = selectedSkillId;

        int slotIndex = FindPlayerSlot(connection);
        if (slotIndex >= 0 &&
            lobbyPlayerSlots != null &&
            slotIndex < lobbyPlayerSlots.Length &&
            lobbyPlayerSlots[slotIndex] != null)
        {
            LobbyPlayer lobbyPlayer =
                lobbyPlayerSlots[slotIndex].GetComponent<LobbyPlayer>();
            if (lobbyPlayer != null)
            {
                lobbyPlayer.playerName.Value = nickname;
                lobbyPlayer.appearanceCode.Value = appearanceCode;
                lobbyPlayer.motionCode.Value = motionCode;
            }
        }

        Debug.Log(
            "[Profile] Client " + connection.ClientId +
            " nickname set to " + nickname +
            ", appearance=" + appearanceCode +
            ", motion=" + motionCode +
            ", skill=" + selectedSkillId + ".");
    }

    private string ResolveProfileNickname(NetworkConnection connection, string nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname) &&
            !CharacterAppearanceStore.HasSelections(nickname) &&
            !CharacterAppearanceStore.IsCosmeticNameOrId(nickname))
        {
            return nickname;
        }

        if (connection != null &&
            playerNicknamesByClientId.TryGetValue(connection.ClientId, out string previousNickname) &&
            !string.IsNullOrWhiteSpace(previousNickname) &&
            !CharacterAppearanceStore.HasSelections(previousNickname) &&
            !CharacterAppearanceStore.IsCosmeticNameOrId(previousNickname))
        {
            return previousNickname;
        }

        return "Player " + (connection != null ? connection.ClientId + 1 : 1);
    }

    private static string ResolveNicknameSource(string rawNickname, string rawAppearanceCode)
    {
        bool nicknameLooksLikeAppearance =
            CharacterAppearanceStore.HasSelections(rawNickname) ||
            CharacterAppearanceStore.IsCosmeticNameOrId(rawNickname);
        bool appearanceLooksLikeNickname =
            !string.IsNullOrWhiteSpace(rawAppearanceCode) &&
            !CharacterAppearanceStore.HasSelections(rawAppearanceCode) &&
            !CharacterAppearanceStore.IsCosmeticNameOrId(rawAppearanceCode);

        return nicknameLooksLikeAppearance && appearanceLooksLikeNickname
            ? rawAppearanceCode
            : rawNickname;
    }

    private bool TryRedirectReconnect(NetworkConnection connection, string userId)
    {
        if (connection == null || !ShouldAssignProcessRoom())
        {
            return false;
        }

        if (roomProcessLauncher == null)
        {
            roomProcessLauncher = GetComponent<RoomProcessLauncher>();
        }

        if (roomProcessLauncher == null ||
            !roomProcessLauncher.TryGetReconnectTarget(userId, out RoomConnectionTarget target))
        {
            return false;
        }

        redirectingClientIds.Add(connection.ClientId);
        RoomAssignmentBroadcast message = new RoomAssignmentBroadcast
        {
            RoomId = target.RoomId,
            Address = target.Address,
            Port = target.Port,
            ReconnectGraceSeconds = target.ReconnectGraceSeconds
        };

        LeaveLobbyRoom(connection);
        StartCoroutine(DisconnectRedirectedClientRoutine(connection, message));
        Debug.Log(
            "[Rooms] Redirected reconnecting client " + connection.ClientId +
            " to " + target.RoomId + ".");
        return true;
    }

    private IEnumerator DisconnectRedirectedClientRoutine(
        NetworkConnection connection,
        RoomAssignmentBroadcast message)
    {
        for (int repeat = 0; repeat < RoomAssignmentBroadcastRepeats; repeat++)
        {
            if (connection == null || !connection.IsActive)
            {
                break;
            }

            ServerManager.Broadcast(connection, message);
            if (repeat < RoomAssignmentBroadcastRepeats - 1)
            {
                yield return new WaitForSecondsRealtime(RoomAssignmentBroadcastInterval);
            }
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, roomAssignmentDisconnectDelay));
        if (connection != null)
        {
            redirectingClientIds.Remove(connection.ClientId);
            connection.Disconnect(false);
        }
    }

    private string GetDisplayName(NetworkConnection connection, int slotIndex)
    {
        if (connection != null &&
            playerNicknamesByClientId.TryGetValue(connection.ClientId, out string nickname) &&
            !string.IsNullOrWhiteSpace(nickname) &&
            !CharacterAppearanceStore.HasSelections(nickname) &&
            !CharacterAppearanceStore.IsCosmeticNameOrId(nickname))
        {
            return nickname;
        }

        return "Player " + (slotIndex + 1);
    }

    private string GetAppearanceCode(NetworkConnection connection)
    {
        if (connection != null &&
            playerAppearancesByClientId.TryGetValue(
                connection.ClientId,
                out string appearanceCode))
        {
            return CharacterAppearanceStore.NormalizeCode(appearanceCode);
        }

        return string.Empty;
    }

    private string GetMotionCode(NetworkConnection connection)
    {
        if (connection != null &&
            playerMotionsByClientId.TryGetValue(
                connection.ClientId,
                out string motionCode))
        {
            return CharacterMotionStore.NormalizeProfileMotionCode(motionCode);
        }

        return string.Empty;
    }

    private string GetSelectedSkillId(NetworkConnection connection)
    {
        if (connection != null &&
            playerSkillsByClientId.TryGetValue(connection.ClientId, out string skillId))
        {
            return PlayerSkillLoadoutStore.NormalizeSkillId(skillId);
        }

        return PlayerSkillLoadoutStore.DefaultSkillId;
    }

    private string GetOrCreateAiAppearanceCode(int slotIndex)
    {
        EnsureAiAppearanceStorage();
        if (slotIndex < 0 || slotIndex >= aiAppearanceCodes.Length)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(aiAppearanceCodes[slotIndex]))
        {
            aiAppearanceCodes[slotIndex] =
                CharacterAppearanceStore.CreateRandomAiAppearanceCode(
                    CreateAiAppearanceSeed(slotIndex));
        }

        return CharacterAppearanceStore.NormalizeCode(aiAppearanceCodes[slotIndex]);
    }

    private string GetOrCreateAiDisplayName(int slotIndex)
    {
        EnsureAiDisplayNameStorage();
        if (slotIndex < 0 || slotIndex >= aiDisplayNames.Length)
        {
            return "Bot";
        }

        if (!string.IsNullOrWhiteSpace(aiDisplayNames[slotIndex]))
        {
            return aiDisplayNames[slotIndex];
        }

        int startIndex = AiNicknamePool.GetRandomStartIndex(
            CreateAiAppearanceSeed(slotIndex));
        for (int offset = 0; offset < AiNicknamePool.Count; offset++)
        {
            string candidate = AiNicknamePool.GetName(startIndex + offset);
            bool alreadyUsed = false;
            for (int i = 0; i < aiDisplayNames.Length; i++)
            {
                if (string.Equals(
                        aiDisplayNames[i],
                        candidate,
                        StringComparison.OrdinalIgnoreCase))
                {
                    alreadyUsed = true;
                    break;
                }
            }

            if (!alreadyUsed)
            {
                aiDisplayNames[slotIndex] = candidate;
                return candidate;
            }
        }

        aiDisplayNames[slotIndex] = "Bot" + (slotIndex + 1);
        return aiDisplayNames[slotIndex];
    }

    private void EnsureAiDisplayNameStorage()
    {
        if (aiDisplayNames == null ||
            aiDisplayNames.Length != requiredPlayerCount)
        {
            aiDisplayNames = new string[requiredPlayerCount];
        }
    }

    private void EnsureAiAppearanceStorage()
    {
        if (aiAppearanceCodes == null ||
            aiAppearanceCodes.Length != requiredPlayerCount)
        {
            aiAppearanceCodes = new string[requiredPlayerCount];
        }
    }

    private string GetOrCreateAiMotionCode(int slotIndex)
    {
        EnsureAiMotionStorage();
        if (slotIndex < 0 || slotIndex >= aiMotionCodes.Length)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(aiMotionCodes[slotIndex]))
        {
            aiMotionCodes[slotIndex] = CreateRandomAiMotionCode(
                CreateAiAppearanceSeed(slotIndex));
        }

        return CharacterMotionStore.NormalizeProfileMotionCode(aiMotionCodes[slotIndex]);
    }

    private void EnsureAiMotionStorage()
    {
        if (aiMotionCodes == null ||
            aiMotionCodes.Length != requiredPlayerCount)
        {
            aiMotionCodes = new string[requiredPlayerCount];
        }
    }

    private static string CreateRandomAiMotionCode(int seed)
    {
        LobbyMotionCatalog catalog = LobbyMotionCatalog.LoadDefault();
        string idleMotionId = GetRandomMotionId(
            catalog,
            LobbyMotionCatalog.MotionSlot.Idle,
            seed);
        int victorySeed;
        unchecked
        {
            victorySeed = seed * 397 ^ 0x5f3759df;
        }
        string victoryMotionId = GetRandomMotionId(
            catalog,
            LobbyMotionCatalog.MotionSlot.Victory,
            victorySeed);

        if (string.IsNullOrWhiteSpace(victoryMotionId))
        {
            return CharacterMotionStore.NormalizeProfileMotionCode(idleMotionId);
        }

        return CharacterMotionStore.NormalizeProfileMotionCode(
            idleMotionId + "|" + victoryMotionId);
    }

    private static string GetRandomMotionId(
        LobbyMotionCatalog catalog,
        LobbyMotionCatalog.MotionSlot slot,
        int seed)
    {
        if (catalog == null || catalog.Entries == null)
        {
            return string.Empty;
        }

        int validCount = 0;
        LobbyMotionCatalog.MotionEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyMotionCatalog.MotionEntry entry = entries[i];
            if (entry != null && entry.IsValid && entry.Slot == slot)
            {
                validCount++;
            }
        }

        if (validCount <= 0)
        {
            return string.Empty;
        }

        System.Random random = new System.Random(seed);
        int targetIndex = random.Next(validCount);
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyMotionCatalog.MotionEntry entry = entries[i];
            if (entry == null || !entry.IsValid || entry.Slot != slot)
            {
                continue;
            }

            if (targetIndex-- == 0)
            {
                return entry.Id;
            }
        }

        return string.Empty;
    }

    private int CreateAiAppearanceSeed(int slotIndex)
    {
        unchecked
        {
            int seed = (int)DateTime.UtcNow.Ticks;
            seed = (seed * 397) ^ GetInstanceID();
            seed = (seed * 397) ^ (slotIndex + 1);
            seed = (seed * 397) ^ (int)(Time.realtimeSinceStartup * 1000f);
            return seed;
        }
    }

    private string[] CreateRosterDisplayNames()
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        string[] displayNames = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            if (playerSlots[i] != null)
            {
                displayNames[i] = GetDisplayName(playerSlots[i], i);
            }
            else if (aiPlayerSlots != null &&
                     i < aiPlayerSlots.Length &&
                     aiPlayerSlots[i] != null)
            {
                displayNames[i] = GetOrCreateAiDisplayName(i);
            }
            else
            {
                displayNames[i] = "Player " + (i + 1);
            }
        }

        return displayNames;
    }

    private string[] CreateRosterAppearanceCodes()
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        string[] appearanceCodes = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            if (playerSlots[i] != null)
            {
                appearanceCodes[i] = GetAppearanceCode(playerSlots[i]);
            }
            else if (aiPlayerSlots != null &&
                     i < aiPlayerSlots.Length &&
                     aiPlayerSlots[i] != null)
            {
                appearanceCodes[i] = GetOrCreateAiAppearanceCode(i);
            }
            else
            {
                appearanceCodes[i] = string.Empty;
            }
        }

        return appearanceCodes;
    }

    private string[] CreateRosterMotionCodes()
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        string[] motionCodes = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            if (playerSlots[i] != null)
            {
                motionCodes[i] = GetMotionCode(playerSlots[i]);
            }
            else if (aiPlayerSlots != null &&
                     i < aiPlayerSlots.Length &&
                     aiPlayerSlots[i] != null)
            {
                motionCodes[i] = GetOrCreateAiMotionCode(i);
            }
            else
            {
                motionCodes[i] = string.Empty;
            }
        }

        return motionCodes;
    }

    private string[] CreateRosterUserIds()
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        string[] userIds = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            NetworkConnection connection = playerSlots[i];
            if (connection != null &&
                playerUserIdsByClientId.TryGetValue(connection.ClientId, out string userId))
            {
                userIds[i] = userId;
            }
            else
            {
                userIds[i] = string.Empty;
            }
        }

        return userIds;
    }

    private string[] CreateRosterSkillIds()
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        string[] skillIds = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            skillIds[i] = playerSlots[i] != null
                ? GetSelectedSkillId(playerSlots[i])
                : string.Empty;
        }

        return skillIds;
    }

    private void ResetLobbyTimerState()
    {
        matchWaitRemainingSeconds.Value = -1;
        gameStartRemainingSeconds.Value = -1;
        UpdateLobbyCounts();
    }

    private bool HasEnoughSpawnPoints()
    {
        return spawnPoints != null && spawnPoints.Length >= requiredPlayerCount;
    }

    private Vector3 GetLobbySpawnPosition(Transform spawnPoint)
    {
        if (spawnPoint == null)
        {
            return new Vector3(0f, lobbySpawnHeight, 0f);
        }

        Vector3 position = spawnPoint.position;
        position.y = lobbySpawnHeight;
        return position;
    }

    private int RealPlayerCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < playerSlots.Length; i++)
            {
                if (playerSlots[i] != null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private int TotalParticipantCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < requiredPlayerCount; i++)
            {
                if (playerSlots[i] != null || aiPlayerSlots[i] != null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

public class LobbyWaitTimerView : MonoBehaviour
{
    private const string CanvasObjectName = "LobbyTimerCanvas";
    private const string TimerObjectName = "LobbyWaitTimerText";

    [System.NonSerialized] private GameObject timerRoot;
    [SerializeField] private Canvas timerCanvas;
    [SerializeField] private TextMeshProUGUI timerText;
    private bool missingSceneTimerLogged;
    private float nextBindAttemptTime;
    private int displayedTimerSignature = int.MinValue;

    private void Start()
    {
        BindSceneTimer();
        SetTimerVisible(false);
    }

    private void Update()
    {
        if ((timerRoot == null || timerCanvas == null || timerText == null) &&
            Time.unscaledTime >= nextBindAttemptTime)
        {
            nextBindAttemptTime = Time.unscaledTime + 1f;
            BindSceneTimer();
        }
        UpdateText();
    }

    private void BindSceneTimer()
    {
        if (timerRoot == null)
        {
            timerRoot = FindSceneObject(CanvasObjectName);
        }

        if (timerCanvas == null)
        {
            GameObject canvasObject = timerRoot != null
                ? timerRoot
                : FindSceneObject(CanvasObjectName);
            if (canvasObject != null)
            {
                timerRoot = canvasObject;
                timerCanvas = canvasObject.GetComponent<Canvas>();
            }
        }

        if (timerText == null)
        {
            GameObject textObject = FindSceneObject(TimerObjectName);
            if (textObject != null)
            {
                timerText = textObject.GetComponent<TextMeshProUGUI>();
            }
        }

        if ((timerRoot == null || timerCanvas == null || timerText == null) &&
            !missingSceneTimerLogged)
        {
            missingSceneTimerLogged = true;
            Debug.LogWarning(
                "[Lobby] Lobby timer UI is missing. Add LobbyTimerCanvas with " +
                "LobbyWaitTimerText to LobbyScene.");
        }
    }

    private void UpdateText()
    {
        if (timerText == null)
        {
            return;
        }

        MatchmakingManager manager = MatchmakingManager.Instance;
        if (manager == null || !IsLobbyClientActive())
        {
            SetTimerVisible(false);
            displayedTimerSignature = int.MinValue;
            return;
        }

        int humans = manager.lobbyHumanCount.Value;
        int participants = manager.lobbyParticipantCount.Value;
        int required = Mathf.Max(1, manager.lobbyRequiredCount.Value);
        int startRemaining = manager.gameStartRemainingSeconds.Value;
        int waitRemaining = manager.matchWaitRemainingSeconds.Value;

        int signature = humans;
        unchecked
        {
            signature = signature * 397 ^ participants;
            signature = signature * 397 ^ required;
            signature = signature * 397 ^ startRemaining;
            signature = signature * 397 ^ waitRemaining;
        }

        if (signature == displayedTimerSignature)
        {
            return;
        }

        displayedTimerSignature = signature;

        SetTimerVisible(true);

        if (startRemaining >= 0)
        {
            timerText.text = $"Starting game in {startRemaining}s  ({participants}/{required})";
            return;
        }

        if (waitRemaining >= 0)
        {
            timerText.text = $"Lobby {humans}/{required} players  |  AI fill in {waitRemaining}s";
            return;
        }

        timerText.text = $"Lobby {humans}/{required} players";
    }

    private void SetTimerVisible(bool visible)
    {
        if (timerRoot != null && timerRoot.activeSelf != visible)
        {
            timerRoot.SetActive(visible);
        }
    }

    private static GameObject FindSceneObject(string objectName)
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate == null ||
                candidate.name != objectName ||
                !candidate.scene.IsValid())
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static bool IsLobbyClientActive()
    {
        return InstanceFinder.ClientManager != null &&
               InstanceFinder.ClientManager.Started;
    }

    public static void HideSceneTimerImmediate()
    {
        GameObject timerRoot = FindSceneObject(CanvasObjectName);
        if (timerRoot != null)
        {
            timerRoot.SetActive(false);
        }
    }
}
