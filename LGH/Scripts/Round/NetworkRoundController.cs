using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// FishNet 留ㅼ튂 ?쒖옉 ?붿껌, ?뚮젅?댁뼱 owner 諛곗젙, 遊?蹂댁땐, ?쇱슫???곹깭 broadcast瑜?議곗쑉?⑸땲??
/// ?ㅼ젣 ?쇱슫??洹쒖튃? GameRoundManager媛 泥섎━?섍퀬, ??而댄룷?뚰듃???쒕쾭 沅뚰븳怨??ㅽ듃?뚰겕 ?낆텧?λ쭔 ?대떦?⑸땲??
/// </summary>
/// <summary>
/// 매치 시작 요청, PaintOwner 배정, 봇 보충을 네트워크 관점에서 조율합니다.
/// 라운드 상태 broadcast와 late join snapshot도 이 컴포넌트가 담당합니다.
/// 실제 라운드 규칙은 GameRoundManager가 담당하고, 이 컴포넌트는 서버 권한과 FishNet 통신을 담당합니다.
/// </summary>
public class NetworkRoundController : MonoBehaviour
{
    public static NetworkRoundController Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    private const string RoundFlowPrefix = "[RoundFlow] ";
    private static readonly PaintOwner[] OwnerOrder =
    {
        PaintOwner.Player1,
        PaintOwner.Player2,
        PaintOwner.Player3,
        PaintOwner.Player4
    };

    [Header("Input")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key startKey = Key.P;
#else
    [SerializeField] private KeyCode startKey = KeyCode.P;
#endif

    [Header("Match")]
    [SerializeField] private float matchmakingWaitSeconds = 30f;
    // 주기적인 전체 상태 broadcast로 늦게 들어오거나 패킷이 지연된 클라이언트가 서버 상태를 따라오게 합니다.
    [SerializeField] private float roundStateBroadcastInterval = 1f;

    [Header("Bots")]
    [SerializeField] private NetworkObject botPrefab;

    [Header("Server Recovery")]
    [SerializeField] private string lobbySceneName = "LobbyScene";
    // room server가 아닌 서버는 gameplay 중 모든 client가 나가면 lobby로 돌아갑니다.
    [SerializeField] private float emptyServerReturnDelay = 5f;

    private NetworkManager networkManager;
    private NetworkRoundPlayerRegistry playerRegistry;
    private NetworkRoundBotSpawner botSpawner;
    // lobby 복귀, 오브젝트 파괴, 대기 스킵 시 countdown token을 취소합니다.
    private CancellationTokenSource countdownCts;
    private float nextStateBroadcastTime;
    private float nextPlayerScanTime;
    private float nextClientRoundStateLogTime;
    private bool broadcastsRegistered;
    private bool countdownSkipRequested;
    private bool loadingStartHandled;
    private float emptyServerSince = -1f;
    private bool returningToLobby;
    // roster broadcast는 이 signature가 바뀔 때만 전송합니다.
    private int lastRosterSignature = int.MinValue;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        playerRegistry = new NetworkRoundPlayerRegistry(OwnerOrder);
        botSpawner = new NetworkRoundBotSpawner();

        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.SetNetworkControlled(true);
        }
    }

    private void Start()
    {
        CacheNetworkManager();
        RegisterBroadcasts();
        TryAssignDefaultBotPrefabFromScene();
    }

    private void OnDestroy()
    {
        CancelCountdown();
        UnregisterBroadcasts();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        CacheNetworkManager();
        RegisterBroadcasts();

        if (WasStartKeyPressed())
        {
            RequestMatchStart();
        }

        if (IsServerActive() &&
            !AreExpectedHumansRegistered() &&
            Time.time >= nextPlayerScanTime)
        {
            nextPlayerScanTime = Time.time + 0.5f;
            ForceServerSpawnScenePlayers();
            ScanExistingNetworkPlayers();
        }

        ReturnEmptyServerToLobby();

        if (IsServerActive() && Time.time >= nextStateBroadcastTime)
        {
            nextStateBroadcastTime = Time.time +
                Mathf.Max(0.25f, roundStateBroadcastInterval);
            BroadcastRoundState();
        }
    }

    public static void RegisterSpawnedPlayer(NetworkConnection conn, GameObject playerObject)
    {
        RegisterSpawnedPlayer(conn, playerObject, PaintOwner.Neutral);
    }

    public static void RegisterSpawnedPlayer(
        NetworkConnection conn,
        GameObject playerObject,
        PaintOwner preferredOwner)
    {
        if (!HasInstance)
        {
            RoundSystemBootstrap.EnsureForActiveScene();
        }

        if (HasInstance)
        {
            Instance.RegisterPlayer(conn, playerObject, preferredOwner);
        }
    }

    public static void SetDefaultBotPrefab(NetworkObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        if (!HasInstance)
        {
            RoundSystemBootstrap.EnsureForActiveScene();
        }

        if (!HasInstance)
        {
            return;
        }

        Instance.botPrefab = prefab;
        Instance.botSpawner.SetDefaultPrefab(prefab);
    }

    public bool TryGetOwner(NetworkConnection conn, out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        return playerRegistry != null && playerRegistry.TryGetOwner(conn, out owner);
    }

    public bool IsOwnerAllowed(NetworkConnection conn, PaintOwner owner)
    {
        return playerRegistry != null && playerRegistry.IsOwnerAllowed(conn, owner);
    }

    public void ServerStartGameAfterLoading()
    {
        CacheNetworkManager();
        if (!IsServerActive())
        {
            Debug.LogWarning(RoundFlowPrefix + "ServerStartGameAfterLoading ignored because the server is not active.");
            return;
        }

        if (loadingStartHandled)
        {
            Debug.LogWarning(RoundFlowPrefix + "ServerStartGameAfterLoading ignored because the loading start routine is already running.");
            return;
        }

        // 씬/도메인 타이밍에 따라 loading callback이 여러 번 올 수 있어 routine 중복 실행을 막습니다.
        loadingStartHandled = true;
        LogFlow(
            "ServerStartGameAfterLoading requested. activeScene=" +
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
            " activeConnections=" + CountActiveConnections() +
            " rosterSlots=" + LoadingTransitionController.ServerRosterSlotCount +
            " rosterHumans=" + LoadingTransitionController.ServerRosterHumanCount + ".");
        ServerStartGameAfterLoadingAsync().Forget();
    }

    private async UniTaskVoid ServerStartGameAfterLoadingAsync()
    {
        EnsureRoundManager();
        LogFlow("Server loading start routine entered. " + BuildServerReadinessSummary() + ".");

        if (!GameRoundManager.HasInstance)
        {
            Debug.LogError(RoundFlowPrefix + "GameRoundManager is not ready after loading.");
            loadingStartHandled = false;
            return;
        }

        // HostPlayerSpawner가 예상 인간 플레이어의 서버 권한 오브젝트를 모두 만들 때까지 기다립니다.
        float registrationDeadline = Time.unscaledTime + 10f;
        float nextRegistrationLogTime = 0f;
        while (Time.unscaledTime < registrationDeadline)
        {
            ForceServerSpawnScenePlayers();
            ScanExistingNetworkPlayers();

            int expectedHumans = GetExpectedHumanCount();
            if (playerRegistry.RegisteredCount >= expectedHumans)
            {
                break;
            }

            if (Time.unscaledTime >= nextRegistrationLogTime)
            {
                nextRegistrationLogTime = Time.unscaledTime + 1f;
                LogFlow(
                    "Waiting for human registration. registered=" +
                    playerRegistry.RegisteredCount + "/" + expectedHumans +
                    " " + BuildServerReadinessSummary() + ".");
            }

            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        int expectedHumanCount = GetExpectedHumanCount();
        if (playerRegistry.RegisteredCount < expectedHumanCount)
        {
            Debug.LogError(
                $"{RoundFlowPrefix}Human player registration incomplete. " +
                $"{playerRegistry.RegisteredCount}/{expectedHumanCount} registered. " +
                "Bots will not replace connected players. " +
                BuildServerReadinessSummary() + ".");
            loadingStartHandled = false;
            return;
        }

        LogFlow(
            "Human registration complete. registered=" +
            playerRegistry.RegisteredCount + "/" + expectedHumanCount +
            " " + BuildServerReadinessSummary() + ".");

        const int maxStartAttempts = 3;
        for (int attempt = 1; attempt <= maxStartAttempts; attempt++)
        {
            if (await StartRoundsFromSkippedCountdownAsync(attempt))
            {
                BroadcastRoundState();
                LogFlow(
                    "Round start completed after loading. registeredHumans=" +
                    playerRegistry.RegisteredCount +
                    " phase=" + GameRoundManager.Instance.CurrentPhase +
                    " round=" + GameRoundManager.Instance.CurrentRoundIndex + ".");
                return;
            }

            if (attempt < maxStartAttempts)
            {
                Debug.LogWarning(
                    RoundFlowPrefix +
                    "Round start attempt " + attempt + " failed. Retrying in 1 second.");
                await UniTask.Delay(TimeSpan.FromSeconds(1d));
            }
        }

        Debug.LogError(
            RoundFlowPrefix +
            "Round start failed after loading. The scene is loaded, but gameplay was not started. " +
            BuildServerReadinessSummary() + ".");
        loadingStartHandled = false;
    }

    private int CountActiveConnections()
    {
        if (networkManager == null)
        {
            return 0;
        }

        int count = 0;
        foreach (NetworkConnection connection in networkManager.ServerManager.Clients.Values)
        {
            if (connection != null && connection.IsActive)
            {
                count++;
            }
        }

        NetworkConnection localConnection = networkManager.ClientManager.Connection;
        if (networkManager.IsClientStarted &&
            localConnection != null &&
            localConnection.IsActive &&
            !networkManager.ServerManager.Clients.ContainsKey(localConnection.ClientId))
        {
            count++;
        }

        return count;
    }

    private int GetExpectedHumanCount()
    {
        if (LoadingTransitionController.ServerRosterSlotCount <= 0)
        {
            return CountActiveConnections();
        }

        int count = 0;
        for (int i = 0; i < LoadingTransitionController.ServerRosterSlotCount; i++)
        {
            if (!LoadingTransitionController.TryGetServerRosterClientId(i, out int clientId))
            {
                continue;
            }

            if (IsClientStillConnected(clientId))
            {
                count++;
            }
        }

        return count;
    }

    private bool AreExpectedHumansRegistered()
    {
        return playerRegistry != null &&
               playerRegistry.RegisteredCount >= GetExpectedHumanCount();
    }

    private void RequestMatchStart()
    {
        // 매치가 이미 진행 중이면 GameRoundManager가 MatchWaiting이 아니므로 TryStartCountdown이 바로 종료됩니다.
        // 吏꾪뻾 以묒뿉??GameRoundManager phase媛 MatchWaiting???꾨땲誘濡?TryStartCountdown?먯꽌 臾댁떆?⑸땲??
        if (IsServerActive() && !IsClientActive())
        {
            TryStartCountdown();
            return;
        }

        if (IsClientActive() && !IsServerActive())
        {
            networkManager.ClientManager.Broadcast(new RoundStartRequestBroadcast { Requested = 1 });
            return;
        }

        if (IsServerActive())
        {
            TryStartCountdown();
        }
    }

    private void RegisterPlayer(
        NetworkConnection conn,
        GameObject playerObject,
        PaintOwner preferredOwner = PaintOwner.Neutral)
    {
        if (conn == null || playerObject == null || !IsServerActive())
        {
            return;
        }

        // 씬 로드나 재접속 뒤 재연결될 수 있으므로 기존 owner 배정을 유지합니다.
        if (playerRegistry.TryGetRegisteredOwner(conn, out PaintOwner existingOwner))
        {
            ApplyOwner(playerObject, existingOwner);

            if (GameRoundManager.HasInstance &&
                GameRoundManager.Instance.FindParticipant(existingOwner) == null)
            {
                GameRoundManager.Instance.RegisterNetworkHumanPlayer(
                    existingOwner,
                    GetDisplayNameForOwner(existingOwner),
                    playerObject);
                LogFlow(
                    "Re-linked registered client " + conn.ClientId +
                    " as " + existingOwner +
                    ". participants=" + GameRoundManager.Instance.Participants.Count + ".");
            }

            return;
        }

        Player player = playerObject.GetComponent<Player>();
        PaintOwner currentOwner = player != null
            ? player.myPaintOwner.Value
            : PaintOwner.Neutral;
        PaintOwner owner = preferredOwner != PaintOwner.Neutral
            ? preferredOwner
            : currentOwner;

        if (preferredOwner != PaintOwner.Neutral)
        {
            playerRegistry.UnregisterOwner(owner);
        }
        else if (!playerRegistry.IsOwnerAvailable(owner))
        {
            owner = playerRegistry.GetFirstFreeOwner();
        }

        if (owner == PaintOwner.Neutral)
        {
            owner = playerRegistry.GetFirstFreeOwner();
        }

        if (owner == PaintOwner.Neutral)
        {
            Debug.LogWarning("[RoundNet] 鍮?PaintOwner ?щ’???놁뼱 ?뚮젅?댁뼱瑜?李멸??먮줈 ?깅줉?섏? 紐삵뻽?듬땲??");
            return;
        }

        ApplyOwner(playerObject, owner);
        playerRegistry.Register(conn, playerObject, owner);

        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.RegisterNetworkHumanPlayer(
                owner,
                GetDisplayNameForOwner(owner),
                playerObject);
        }

        BroadcastRoundState();
        LogFlow(
            "Client " + conn.ClientId + " registered as " + owner +
            ". participants=" +
            (GameRoundManager.HasInstance ? GameRoundManager.Instance.Participants.Count : 0) + ".");
    }

    private void ScanExistingNetworkPlayers()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            if (player == null || !player.Owner.IsValid)
            {
                continue;
            }

            RegisterPlayer(
                player.Owner,
                player.gameObject,
                player.myPaintOwner.Value);
        }
    }

    private void ForceServerSpawnScenePlayers()
    {
        HostPlayerSpawner[] spawners = FindObjectsByType<HostPlayerSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < spawners.Length; i++)
        {
            HostPlayerSpawner spawner = spawners[i];
            if (spawner != null)
            {
                spawner.ServerSpawnPlayersForExistingConnections();
            }
        }
    }

    private bool IsClientStillConnected(int clientId)
    {
        if (networkManager == null)
        {
            return false;
        }

        if (networkManager.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection connection) &&
            connection != null &&
            connection.IsActive)
        {
            return true;
        }

        NetworkConnection localConnection = networkManager.ClientManager.Connection;
        return networkManager.IsClientStarted &&
               localConnection != null &&
               localConnection.IsActive &&
               localConnection.ClientId == clientId;
    }

    private void ReturnEmptyServerToLobby()
    {
        // room server는 짧게 사는 child process라 lobby로 돌아가지 않고 RoomServerLifecycle을 통해 종료됩니다.
        if (!IsServerActive() || returningToLobby)
        {
            return;
        }

        if (ServerLaunchMode.IsRoomServer)
        {
            return;
        }

        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeSceneName == lobbySceneName)
        {
            emptyServerSince = -1f;
            return;
        }

        if (CountActiveConnections() > 0)
        {
            emptyServerSince = -1f;
            return;
        }

        if (emptyServerSince < 0f)
        {
            emptyServerSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - emptyServerSince < emptyServerReturnDelay)
        {
            return;
        }

        ReturnServerToLobby("[RoundNet] Empty gameplay server returned to lobby.");
    }

    private void ReturnServerToLobby(string logMessage)
    {
        if (!IsServerActive() || returningToLobby)
        {
            return;
        }

        returningToLobby = true;
        loadingStartHandled = false;
        LoadingTransitionController.ClearServerRoster();
        CancelCountdown();

        if (ServerLaunchMode.IsRoomServer)
        {
            Debug.Log(logMessage + " Room server will quit instead of returning to lobby.");
            networkManager.ServerManager.StopConnection(true);
            Application.Quit(0);
            return;
        }

        SceneLoadData sceneLoadData = new SceneLoadData(lobbySceneName)
        {
            ReplaceScenes = ReplaceOption.All
        };

        networkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
        Debug.Log(logMessage);
    }

    private void TryAssignDefaultBotPrefabFromScene()
    {
        botSpawner.SetDefaultPrefab(botPrefab);
        botSpawner.TryAssignDefaultPrefabFromScene();
        botPrefab = botSpawner.BotPrefab;
    }

    private void TryStartCountdown()
    {
        if (!IsServerActive())
        {
            return;
        }

        EnsureRoundManager();
        if (!GameRoundManager.HasInstance || GameRoundManager.Instance.CurrentPhase != GamePhase.MatchWaiting)
        {
            return;
        }

        if (countdownCts != null)
        {
            // countdown 중 시작 키를 다시 누르면 남은 대기를 건너뛰고 즉시 시작합니다.
            // 移댁슫?몃떎??以?P瑜??ㅼ떆 ?꾨Ⅴ硫??⑥? ?쒓컙??嫄대꼫?곌퀬 利됱떆 ?쒖옉?⑸땲??
            countdownSkipRequested = true;
            return;
        }

        countdownCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        ServerCountdownAsync(countdownCts).Forget();
    }

    private bool WasStartKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyboard[startKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(startKey);
#endif
    }

    private async UniTask ServerCountdownAsync(CancellationTokenSource source)
    {
        try
        {
            CancellationToken token = source.Token;
            EnsureRoundManager();
            GameRoundManager.Instance.SetNetworkControlled(true);
            GameRoundManager.Instance.EnterNetworkMatchWaiting(matchmakingWaitSeconds);
            countdownSkipRequested = false;
            BroadcastRoundState();

            float endTime = Time.time + matchmakingWaitSeconds;
            while (Time.time < endTime && !countdownSkipRequested)
            {
                BroadcastRoundState();
                await UniTask.Delay(
                    TimeSpan.FromSeconds(Mathf.Max(0.25f, roundStateBroadcastInterval)),
                    cancellationToken: token);
            }

            countdownSkipRequested = false;
            if (!await StartRoundsFromSkippedCountdownAsync(0))
            {
                Debug.LogError(RoundFlowPrefix + "Round start failed after matchmaking countdown.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (countdownCts == source)
            {
                countdownCts = null;
                source.Dispose();
            }
        }
    }

    private async UniTask<bool> StartRoundsFromSkippedCountdownAsync(int attemptNumber)
    {
        EnsureRoundManager();
        if (!GameRoundManager.HasInstance)
        {
            Debug.LogError(RoundFlowPrefix + "Cannot start rounds because GameRoundManager is missing.");
            return false;
        }

        LogFlow(
            "Round start attempt " + attemptNumber +
            " validating prerequisites. " + BuildServerReadinessSummary() + ".");

        // 라운드 setup은 생성된 타일이 TilePaintManager에 등록되어 있어야 진행됩니다.
        float gridReadyDeadline = Time.unscaledTime + 15f;
        float nextGridLogTime = 0f;
        while ((!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady) &&
               Time.unscaledTime < gridReadyDeadline)
        {
            RoundSystemBootstrap.EnsureForActiveScene();
            if (Time.unscaledTime >= nextGridLogTime)
            {
                nextGridLogTime = Time.unscaledTime + 1f;
                LogFlow("Waiting for tile grid before round start. " + GetTileGridDebugStatus() + ".");
            }

            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            Debug.LogError(
                RoundFlowPrefix +
                "Cannot start rounds because TilePaintManager grid is not ready. " +
                GetTileGridDebugStatus() + ".");
            return false;
        }

        countdownSkipRequested = false;
        FillEmptySlotsWithBots();
        int participantCount = GameRoundManager.Instance.Participants.Count;
        if (participantCount <= 0)
        {
            Debug.LogError(
                RoundFlowPrefix +
                "Cannot start rounds because GameRoundManager has no participants after bot fill. " +
                BuildServerReadinessSummary() + ".");
            return false;
        }

        LogFlow(
            "Calling GameRoundManager.StartNetworkRounds. participants=" +
            participantCount + " " + GetTileGridDebugStatus() + ".");
        GameRoundManager.Instance.StartNetworkRounds();

        float phaseDeadline = Time.unscaledTime + 3f;
        while (GameRoundManager.Instance.CurrentPhase == GamePhase.MatchWaiting &&
               Time.unscaledTime < phaseDeadline)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        BroadcastRoundState();
        if (GameRoundManager.Instance.CurrentPhase == GamePhase.MatchWaiting)
        {
            Debug.LogError(
                RoundFlowPrefix +
                "StartNetworkRounds returned but GameRoundManager is still MatchWaiting. " +
                "Check the preceding BeginRound/GameRoundManager logs for the blocked setup step. " +
                BuildServerReadinessSummary() + ".");
            return false;
        }

        LogFlow(
            "Round phase active. phase=" + GameRoundManager.Instance.CurrentPhase +
            " round=" + GameRoundManager.Instance.CurrentRoundIndex +
            " remaining=" + GameRoundManager.Instance.PhaseRemainingSeconds.ToString("0.##") + ".");
        return true;
    }

    private void FillEmptySlotsWithBots()
    {
        // 캡처된 lobby roster가 AI slot을 결정합니다. roster가 없는 로컬 테스트는 빈 slot을 모두 채웁니다.
        for (int i = 0; i < OwnerOrder.Length; i++)
        {
            if (LoadingTransitionController.ServerRosterSlotCount > 0 &&
                !LoadingTransitionController.IsServerRosterAiSlot(i))
            {
                continue;
            }

            PaintOwner owner = OwnerOrder[i];
            if (GameRoundManager.HasInstance && GameRoundManager.Instance.FindParticipant(owner) != null)
            {
                continue;
            }

            botSpawner.SpawnBot(owner, networkManager, ApplyOwner);
        }
    }

    private void ApplyOwner(GameObject playerObject, PaintOwner owner)
    {
        Player player = playerObject.GetComponent<Player>();
        if (player != null)
        {
            player.ServerSetPaintOwner(owner);
        }

        PlayerPaintIdentity identity = playerObject.GetComponent<PlayerPaintIdentity>();
        if (identity != null)
        {
            identity.SetOwner(owner);
        }

        PlayerLevelStats stats = playerObject.GetComponent<PlayerLevelStats>();
        if (stats != null)
        {
            stats.RefreshOwnerRegistration();
        }
    }

    private static string GetDisplayNameForOwner(PaintOwner owner)
    {
        int slotIndex = (int)owner - 1;
        if (slotIndex >= 0 &&
            LoadingTransitionController.ServerRosterSlotCount > 0)
        {
            return LoadingTransitionController.GetServerRosterDisplayName(slotIndex);
        }

        return "P" + (int)owner;
    }

    public void BroadcastRoundStateNow()
    {
        BroadcastRoundState();
    }

    private void BroadcastRoundState()
    {
        if (!IsServerActive() || !GameRoundManager.HasInstance)
        {
            return;
        }

        RoundStateBroadcast state = NetworkRoundStateFactory.Create(GameRoundManager.Instance);
        networkManager.ServerManager.Broadcast(state);
        BroadcastRosterIfChanged();
    }

    private void BroadcastRosterIfChanged()
    {
        RoundRosterBroadcast roster = NetworkRoundStateFactory.CreateRoster(GameRoundManager.Instance);
        int signature = roster.ParticipantMask * 397 ^ roster.BotMask;
        unchecked
        {
            signature = signature * 397 ^ (roster.Player1DisplayName?.GetHashCode() ?? 0);
            signature = signature * 397 ^ (roster.Player2DisplayName?.GetHashCode() ?? 0);
            signature = signature * 397 ^ (roster.Player3DisplayName?.GetHashCode() ?? 0);
            signature = signature * 397 ^ (roster.Player4DisplayName?.GetHashCode() ?? 0);
        }

        if (signature == lastRosterSignature) return;
        lastRosterSignature = signature;
        networkManager.ServerManager.Broadcast(roster);
    }

    private void OnRoundStartRequest(NetworkConnection conn, RoundStartRequestBroadcast message, FishNet.Transporting.Channel channel)
    {
        TryStartCountdown();
    }

    private void OnRoundState(RoundStateBroadcast message, FishNet.Transporting.Channel channel)
    {
        if (IsServerActive())
        {
            return;
        }

        EnsureRoundManager();
        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.ApplyNetworkState(message);
            if (Time.unscaledTime >= nextClientRoundStateLogTime)
            {
                nextClientRoundStateLogTime = Time.unscaledTime + 3f;
                Debug.Log(
                    "[RoundNet] Client applied round state. phase=" +
                    ((GamePhase)message.Phase) +
                    " round=" + message.RoundIndex +
                    " remaining=" + message.PhaseRemainingSeconds.ToString("0.##") +
                    " participantsMask=" + message.ParticipantMask +
                    " eliminatedMask=" + message.EliminatedMask + ".");
            }
        }
    }

    private void OnRoundRoster(RoundRosterBroadcast message, FishNet.Transporting.Channel channel)
    {
        if (IsServerActive()) return;
        EnsureRoundManager();
        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.ApplyNetworkRoster(message);
        }
    }

    private void EnsureRoundManager()
    {
        RoundSystemBootstrap.EnsureForActiveScene();
        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.SetNetworkControlled(true);
        }
    }

    private string BuildServerReadinessSummary()
    {
        int participantCount = GameRoundManager.HasInstance
            ? GameRoundManager.Instance.Participants.Count
            : 0;
        return "activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
               " activeConnections=" + CountActiveConnections() +
               " expectedHumans=" + GetExpectedHumanCount() +
               " registeredHumans=" + (playerRegistry != null ? playerRegistry.RegisteredCount : 0) +
               " participants=" + participantCount +
               " rosterSlots=" + LoadingTransitionController.ServerRosterSlotCount +
               " rosterHumans=" + LoadingTransitionController.ServerRosterHumanCount +
               " " + GetTileGridDebugStatus();
    }

    private static string GetTileGridDebugStatus()
    {
        if (!TilePaintManager.HasInstance)
        {
            return "TilePaintManager=missing";
        }

        return TilePaintManager.Instance.DebugGridStatus;
    }

    private static void LogFlow(string message)
    {
        Debug.Log(RoundFlowPrefix + message);
    }

    private void CacheNetworkManager()
    {
        if (networkManager == null)
        {
            networkManager = InstanceFinder.NetworkManager;
        }
    }

    private void RegisterBroadcasts()
    {
        if (broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<RoundStartRequestBroadcast>(OnRoundStartRequest, false);
        networkManager.ServerManager.RegisterBroadcast<GameSceneReadyBroadcast>(
            OnGameSceneReady,
            false);
        networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        networkManager.ClientManager.RegisterBroadcast<RoundStateBroadcast>(OnRoundState);
        networkManager.ClientManager.RegisterBroadcast<RoundRosterBroadcast>(OnRoundRoster);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<RoundStartRequestBroadcast>(OnRoundStartRequest);
        networkManager.ServerManager.UnregisterBroadcast<GameSceneReadyBroadcast>(OnGameSceneReady);
        networkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        networkManager.ClientManager.UnregisterBroadcast<RoundStateBroadcast>(OnRoundState);
        networkManager.ClientManager.UnregisterBroadcast<RoundRosterBroadcast>(OnRoundRoster);
        broadcastsRegistered = false;
    }

    private void OnGameSceneReady(
        NetworkConnection connection,
        GameSceneReadyBroadcast message,
        FishNet.Transporting.Channel channel)
    {
        if (!ServerLaunchMode.IsRoomServer ||
            connection == null ||
            message.SceneName != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
        {
            return;
        }

        SendCurrentMatchStateAfterClientReady(connection).Forget();
    }

    private async UniTaskVoid SendCurrentMatchStateAfterClientReady(NetworkConnection connection)
    {
        // late join/재접속 클라이언트는 씬 준비 뒤 라운드 상태, roster, 타일 grid, level snapshot을 받아야 합니다.
        float deadline = Time.unscaledTime + 3f;
        while (Time.unscaledTime < deadline &&
               !IsCurrentMatchSnapshotReady(connection))
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        SendCurrentMatchStateTo(connection);
    }

    private bool IsCurrentMatchSnapshotReady(NetworkConnection connection)
    {
        if (connection == null || !connection.IsActive || !IsServerActive())
        {
            return false;
        }

        return GameRoundManager.HasInstance &&
               NetworkTilePaintAuthority.HasInstance &&
               TilePaintManager.HasInstance &&
               TilePaintManager.Instance.IsGridReady;
    }

    private void ServerManager_OnRemoteConnectionState(
        NetworkConnection connection,
        RemoteConnectionStateArgs args)
    {
        // disconnect된 client는 owner mapping을 해제하고, 재접속 흐름에서 spawner가 다시 배정할 수 있습니다.
        if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            playerRegistry.Unregister(connection);
            return;
        }

        if (args.ConnectionState != RemoteConnectionState.Started ||
            connection == null)
        {
            return;
        }

        if (ServerLaunchMode.IsRoomServer)
        {
            SendCurrentMatchStateTo(connection);
            return;
        }

        if (!ShouldResetToLobbyForEnterGame())
        {
            return;
        }

        Debug.LogWarning(
            $"[RoundNet] Client {connection.ClientId} entered while a game scene " +
            "was active. Disconnecting the new entry and keeping the active match alive.");
        connection.Disconnect(true);
    }

    private void SendCurrentMatchStateTo(NetworkConnection connection)
    {
        if (connection == null || !connection.IsActive || !IsServerActive())
        {
            return;
        }

        if (GameRoundManager.HasInstance)
        {
            networkManager.ServerManager.Broadcast(
                connection,
                NetworkRoundStateFactory.CreateRoster(GameRoundManager.Instance));
            networkManager.ServerManager.Broadcast(
                connection,
                NetworkRoundStateFactory.Create(GameRoundManager.Instance));
        }

        if (NetworkTilePaintAuthority.HasInstance)
        {
            NetworkTilePaintAuthority.Instance.SendGridSnapshotTo(connection);
        }

        PlayerLevelStats.BroadcastAllNetworkSnapshots();
        Debug.Log("[RoundNet] Sent current match snapshot to client " + connection.ClientId + ".");
    }

    private bool ShouldResetToLobbyForEnterGame()
    {
        if (!IsServerActive())
        {
            return false;
        }

        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return activeSceneName != lobbySceneName;
    }

    private void CancelCountdown()
    {
        if (countdownCts == null)
        {
            return;
        }

        CancellationTokenSource source = countdownCts;
        countdownCts = null;
        countdownSkipRequested = false;
        source.Cancel();
        source.Dispose();
    }

    private bool IsServerActive()
    {
        return networkManager != null && networkManager.IsServerStarted;
    }

    private bool IsClientActive()
    {
        return networkManager != null && networkManager.IsClientStarted;
    }
}
