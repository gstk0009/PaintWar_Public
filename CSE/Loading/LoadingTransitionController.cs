using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

public class LoadingTransitionController : MonoBehaviour
{
    public static LoadingTransitionController Instance { get; private set; }
    public static bool IsGameplayReleased { get; private set; } = true;

    private static int[] serverRosterClientIds = System.Array.Empty<int>();
    private static bool[] serverRosterAiSlots = System.Array.Empty<bool>();
    private static string[] serverRosterDisplayNames = System.Array.Empty<string>();
    private static string[] serverRosterAppearanceCodes = System.Array.Empty<string>();
    private static string[] serverRosterMotionCodes = System.Array.Empty<string>();
    private static string[] serverRosterSkillIds = System.Array.Empty<string>();
    private static string[] serverRosterUserIds = System.Array.Empty<string>();

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingView;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image progressFillImage;

    [Header("Ready Synchronization")]
    [SerializeField] private float readyTimeoutSeconds = 30f;
    [SerializeField] private float localInitializationTimeoutSeconds = 20f;

    [Header("Lobby Reset")]
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private bool rejectNewEntryDuringGameplay = true;

    private readonly HashSet<int> expectedClientIds = new HashSet<int>();
    private readonly HashSet<int> readyClientIds = new HashSet<int>();

    private NetworkManager networkManager;
    private Coroutine clientReadyCoroutine;
    private string targetSceneName;
    private float serverReadyDeadline;
    private bool broadcastsRegistered;
    private bool transitionActive;
    private bool serverWaitingForReady;
    private Coroutine serverGameStartCoroutine;
    private bool serverLobbyResetQueued;
    private bool externalOverlayActive;
    private float externalOverlayShownAt;
    private Coroutine externalHideCoroutine;
    private bool sceneLoadEndHandled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServerRoster()
    {
        ClearServerRoster();
    }

    public static void ClearServerRoster()
    {
        serverRosterClientIds = System.Array.Empty<int>();
        serverRosterAiSlots = System.Array.Empty<bool>();
        serverRosterDisplayNames = System.Array.Empty<string>();
        serverRosterAppearanceCodes = System.Array.Empty<string>();
        serverRosterMotionCodes = System.Array.Empty<string>();
        serverRosterSkillIds = System.Array.Empty<string>();
        serverRosterUserIds = System.Array.Empty<string>();
        IsGameplayReleased = true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        HideLoadingView();
        SetProgress(0f);

        if (loadingView == null)
        {
            Debug.LogError("[Loading] Loading View is not assigned.");
        }
    }

    private void Update()
    {
        BindNetworkManager();

        if (serverWaitingForReady)
        {
            PruneDisconnectedClients();
            TryReleaseClients(false);

            if (serverWaitingForReady &&
                !float.IsPositiveInfinity(serverReadyDeadline) &&
                Time.unscaledTime >= serverReadyDeadline)
            {
                Debug.LogWarning("[Loading] Ready timeout reached. Releasing connected clients.");
                ReleaseClients(true);
            }
        }

        // FishNet's load-end callback can be delayed or missed after a mobile app pause.
        // Recover from the actual Unity scene state instead of waiting forever.
        if (transitionActive && !sceneLoadEndHandled && IsTargetSceneLoaded())
        {
            HandleTargetSceneLoaded();
        }

        if (externalOverlayActive && !transitionActive)
        {
            SetProgress(Mathf.Clamp01(
                0.15f + (Time.unscaledTime - externalOverlayShownAt) / 4f * 0.7f));
        }
    }

    private void OnDestroy()
    {
        UnbindNetworkManager();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void BeginServerTransition(string sceneName)
    {
        BindNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            Debug.LogError("[Loading] A running server is required to begin a transition.");
            return;
        }

        targetSceneName = sceneName;
        transitionActive = true;
        serverWaitingForReady = true;
        serverReadyDeadline = Time.unscaledTime + Mathf.Max(1f, readyTimeoutSeconds);
        sceneLoadEndHandled = false;
        expectedClientIds.Clear();
        readyClientIds.Clear();

        foreach (NetworkConnection connection in networkManager.ServerManager.Clients.Values)
        {
            if (connection != null && connection.IsActive)
            {
                expectedClientIds.Add(connection.ClientId);
            }
        }

        NetworkConnection localConnection = networkManager.ClientManager.Connection;
        if (networkManager.IsClientStarted &&
            localConnection != null &&
            localConnection.IsActive)
        {
            expectedClientIds.Add(localConnection.ClientId);
        }

        ApplyBegin(sceneName);
        networkManager.ServerManager.Broadcast(new GameSceneLoadingBeginBroadcast
        {
            SceneName = sceneName
        });
    }

    public static void CaptureServerRoster(
        NetworkConnection[] playerSlots,
        NetworkObject[] aiPlayerSlots,
        string[] displayNames = null,
        string[] appearanceCodes = null,
        string[] motionCodes = null,
        string[] skillIds = null,
        string[] userIds = null)
    {
        int slotCount = playerSlots != null ? playerSlots.Length : 0;
        serverRosterClientIds = new int[slotCount];
        serverRosterAiSlots = new bool[slotCount];
        serverRosterDisplayNames = new string[slotCount];
        serverRosterAppearanceCodes = new string[slotCount];
        serverRosterMotionCodes = new string[slotCount];
        serverRosterSkillIds = new string[slotCount];
        serverRosterUserIds = new string[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            NetworkConnection connection = playerSlots[i];
            serverRosterClientIds[i] = connection != null
                ? connection.ClientId
                : -1;
            serverRosterAiSlots[i] =
                connection == null &&
                aiPlayerSlots != null &&
                i < aiPlayerSlots.Length &&
                aiPlayerSlots[i] != null;
            serverRosterDisplayNames[i] =
                displayNames != null &&
                i < displayNames.Length &&
                !string.IsNullOrWhiteSpace(displayNames[i])
                    ? displayNames[i]
                    : CreateDefaultRosterDisplayName(i);
            serverRosterAppearanceCodes[i] =
                appearanceCodes != null &&
                i < appearanceCodes.Length &&
                !string.IsNullOrWhiteSpace(appearanceCodes[i])
                    ? CharacterAppearanceStore.NormalizeCode(appearanceCodes[i])
                    : string.Empty;
            serverRosterMotionCodes[i] =
                motionCodes != null &&
                i < motionCodes.Length &&
                !string.IsNullOrWhiteSpace(motionCodes[i])
                    ? CharacterMotionStore.NormalizeProfileMotionCode(motionCodes[i])
                    : string.Empty;
            serverRosterSkillIds[i] =
                skillIds != null &&
                i < skillIds.Length &&
                !string.IsNullOrWhiteSpace(skillIds[i])
                    ? PlayerSkillLoadoutStore.NormalizeSkillId(skillIds[i])
                    : string.Empty;
            serverRosterUserIds[i] =
                userIds != null &&
                i < userIds.Length &&
                !string.IsNullOrWhiteSpace(userIds[i])
                    ? userIds[i].Trim()
                    : string.Empty;
        }
    }

    public static bool TryGetServerHumanSlot(int clientId, out int slotIndex)
    {
        for (int i = 0; i < serverRosterClientIds.Length; i++)
        {
            if (serverRosterClientIds[i] == clientId)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    public static bool TryGetServerHumanSlotByUserId(string userId, out int slotIndex)
    {
        userId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            slotIndex = -1;
            return false;
        }

        for (int i = 0; i < serverRosterUserIds.Length; i++)
        {
            if (string.Equals(
                    serverRosterUserIds[i],
                    userId,
                    System.StringComparison.Ordinal))
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    public static bool HasServerRosterUserIds
    {
        get
        {
            for (int i = 0; i < serverRosterUserIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(serverRosterUserIds[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static bool TryGetServerRosterClientId(int slotIndex, out int clientId)
    {
        if (slotIndex >= 0 && slotIndex < serverRosterClientIds.Length)
        {
            clientId = serverRosterClientIds[slotIndex];
            return clientId >= 0;
        }

        clientId = -1;
        return false;
    }

    public static string GetServerRosterDisplayName(int slotIndex)
    {
        if (slotIndex >= 0 &&
            slotIndex < serverRosterDisplayNames.Length &&
            !string.IsNullOrWhiteSpace(serverRosterDisplayNames[slotIndex]))
        {
            return serverRosterDisplayNames[slotIndex];
        }

        return CreateDefaultRosterDisplayName(slotIndex);
    }

    public static string GetServerRosterAppearanceCode(int slotIndex)
    {
        if (slotIndex >= 0 &&
            slotIndex < serverRosterAppearanceCodes.Length &&
            !string.IsNullOrWhiteSpace(serverRosterAppearanceCodes[slotIndex]))
        {
            return CharacterAppearanceStore.NormalizeCode(serverRosterAppearanceCodes[slotIndex]);
        }

        return string.Empty;
    }

    public static string GetServerRosterMotionCode(int slotIndex)
    {
        if (slotIndex >= 0 &&
            slotIndex < serverRosterMotionCodes.Length &&
            !string.IsNullOrWhiteSpace(serverRosterMotionCodes[slotIndex]))
        {
            return CharacterMotionStore.NormalizeProfileMotionCode(
                serverRosterMotionCodes[slotIndex]);
        }

        return string.Empty;
    }

    public static string GetServerRosterUserId(int slotIndex)
    {
        if (slotIndex >= 0 &&
            slotIndex < serverRosterUserIds.Length &&
            !string.IsNullOrWhiteSpace(serverRosterUserIds[slotIndex]))
        {
            return serverRosterUserIds[slotIndex];
        }

        return string.Empty;
    }

    public static string GetServerRosterSkillId(int slotIndex)
    {
        if (slotIndex >= 0 &&
            slotIndex < serverRosterSkillIds.Length &&
            !string.IsNullOrWhiteSpace(serverRosterSkillIds[slotIndex]))
        {
            return PlayerSkillLoadoutStore.NormalizeSkillId(serverRosterSkillIds[slotIndex]);
        }

        return string.Empty;
    }

    public static int ServerRosterSlotCount => serverRosterClientIds.Length;

    public static int ServerRosterHumanCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < serverRosterClientIds.Length; i++)
            {
                if (serverRosterClientIds[i] >= 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static bool IsServerRosterAiSlot(int slotIndex)
    {
        return slotIndex >= 0 &&
               slotIndex < serverRosterAiSlots.Length &&
               serverRosterAiSlots[slotIndex];
    }

    public static bool IsServerRosterHumanSlot(int slotIndex)
    {
        return slotIndex >= 0 &&
               slotIndex < serverRosterClientIds.Length &&
               serverRosterClientIds[slotIndex] >= 0;
    }

    private static string CreateDefaultRosterDisplayName(int slotIndex)
    {
        return "P" + (slotIndex + 1);
    }

    private void BindNetworkManager()
    {
        NetworkManager foundManager = InstanceFinder.NetworkManager;
        if (foundManager == null || foundManager == networkManager)
        {
            return;
        }

        UnbindNetworkManager();
        networkManager = foundManager;
        networkManager.SceneManager.OnLoadStart += OnSceneLoadStart;
        networkManager.SceneManager.OnLoadPercentChange += OnSceneLoadPercentChange;
        networkManager.SceneManager.OnLoadEnd += OnSceneLoadEnd;
        networkManager.ServerManager.OnAuthenticationResult += OnServerAuthenticationResult;
        RegisterBroadcasts();
    }

    private void UnbindNetworkManager()
    {
        if (networkManager == null)
        {
            return;
        }

        if (broadcastsRegistered)
        {
            networkManager.ServerManager.UnregisterBroadcast<GameSceneReadyBroadcast>(
                OnClientReady);
            networkManager.ClientManager.UnregisterBroadcast<GameSceneLoadingBeginBroadcast>(
                OnLoadingBegin);
            networkManager.ClientManager.UnregisterBroadcast<GameSceneLoadingReleaseBroadcast>(
                OnLoadingRelease);
            broadcastsRegistered = false;
        }

        networkManager.SceneManager.OnLoadStart -= OnSceneLoadStart;
        networkManager.SceneManager.OnLoadPercentChange -= OnSceneLoadPercentChange;
        networkManager.SceneManager.OnLoadEnd -= OnSceneLoadEnd;
        networkManager.ServerManager.OnAuthenticationResult -= OnServerAuthenticationResult;
        networkManager = null;
    }

    private void OnServerAuthenticationResult(
        NetworkConnection connection,
        bool authenticated)
    {
        if (!rejectNewEntryDuringGameplay ||
            !authenticated ||
            networkManager == null ||
            !networkManager.IsServerStarted ||
            serverLobbyResetQueued ||
            UnitySceneManager.GetActiveScene().name == lobbySceneName)
        {
            return;
        }

        Debug.LogWarning(
            $"[Loading] Client {connection.ClientId} entered while gameplay " +
            "was active. Disconnecting the new entry and keeping the active match alive.");

        if (connection != null && connection.IsActive)
        {
            connection.Disconnect(true);
        }
    }

    public void QueueServerLobbyReset()
    {
        BindNetworkManager();
        if (serverLobbyResetQueued ||
            networkManager == null ||
            !networkManager.IsServerStarted)
        {
            return;
        }

        serverLobbyResetQueued = true;
        serverWaitingForReady = false;
        transitionActive = false;
        IsGameplayReleased = true;
        expectedClientIds.Clear();
        readyClientIds.Clear();
        ClearServerRoster();

        SceneLoadData sceneLoadData = new SceneLoadData(lobbySceneName)
        {
            ReplaceScenes = ReplaceOption.All
        };

        networkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
    }

    public static bool ShowExternalOverlay(string status, float initialProgress = 0.15f)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[Loading] Cannot show loading overlay because LoadingTransitionController is missing.");
            return false;
        }

        Instance.ShowExternalOverlayInternal(status, initialProgress);
        return true;
    }

    public static void SetExternalOverlayStatus(string status)
    {
        if (Instance != null)
        {
            Instance.SetStatus(status);
        }
    }

    public static void HideExternalOverlay()
    {
        if (Instance != null)
        {
            Instance.HideExternalOverlayInternal();
        }
    }

    public static void HideExternalOverlayWhenSceneReady(string sceneName)
    {
        if (Instance != null)
        {
            Instance.StartExternalHideWhenSceneReady(sceneName);
        }
    }

    private void ShowExternalOverlayInternal(string status, float initialProgress)
    {
        if (Application.isBatchMode)
        {
            return;
        }

        externalOverlayActive = true;
        externalOverlayShownAt = Time.unscaledTime;
        if (externalHideCoroutine != null)
        {
            StopCoroutine(externalHideCoroutine);
            externalHideCoroutine = null;
        }

        ShowLoadingView();
        SetStatus(status);
        SetProgress(initialProgress);
    }

    private void HideExternalOverlayInternal()
    {
        externalOverlayActive = false;
        if (externalHideCoroutine != null)
        {
            StopCoroutine(externalHideCoroutine);
            externalHideCoroutine = null;
        }

        if (!transitionActive)
        {
            HideLoadingView();
        }
    }

    private void StartExternalHideWhenSceneReady(string sceneName)
    {
        if (externalHideCoroutine != null)
        {
            StopCoroutine(externalHideCoroutine);
        }

        externalHideCoroutine = StartCoroutine(
            HideExternalOverlayWhenSceneReadyRoutine(sceneName));
    }

    private IEnumerator HideExternalOverlayWhenSceneReadyRoutine(string sceneName)
    {
        float deadline = Time.unscaledTime + 20f;
        while (Time.unscaledTime < deadline)
        {
            if (UnitySceneManager.GetActiveScene().name == sceneName)
            {
                break;
            }

            yield return null;
        }

        externalHideCoroutine = null;
        HideExternalOverlayInternal();
    }

    private void RegisterBroadcasts()
    {
        if (networkManager == null || broadcastsRegistered)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<GameSceneReadyBroadcast>(
            OnClientReady,
            false);
        networkManager.ClientManager.RegisterBroadcast<GameSceneLoadingBeginBroadcast>(
            OnLoadingBegin);
        networkManager.ClientManager.RegisterBroadcast<GameSceneLoadingReleaseBroadcast>(
            OnLoadingRelease);
        broadcastsRegistered = true;
    }

    private void OnLoadingBegin(
        GameSceneLoadingBeginBroadcast message,
        Channel channel)
    {
        ApplyBegin(message.SceneName);
    }

    private void ApplyBegin(string sceneName)
    {
        externalOverlayActive = false;
        targetSceneName = sceneName;
        transitionActive = true;
        IsGameplayReleased = false;
        sceneLoadEndHandled = false;
        ShowLoadingView();
        SetProgress(0f);
        SetStatus("Loading game...");
    }

    private void OnSceneLoadStart(SceneLoadStartEventArgs args)
    {
        if (!transitionActive)
        {
            return;
        }

        ShowLoadingView();
        SetStatus("Loading scene...");
    }

    private void OnSceneLoadPercentChange(SceneLoadPercentEventArgs args)
    {
        if (!transitionActive)
        {
            return;
        }

        SetProgress(Mathf.Clamp01(args.Percent * 0.9f));
    }

    private void OnSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (args.LoadedScenes != null)
        {
            for (int i = 0; i < args.LoadedScenes.Length; i++)
            {
                if (serverLobbyResetQueued &&
                    args.LoadedScenes[i].name == lobbySceneName)
                {
                    serverLobbyResetQueued = false;
                    HideLoadingView();
                    return;
                }
            }
        }

        if (!transitionActive || !ContainsTargetScene(args))
        {
            return;
        }

        ActivateTargetScene(args);
        HandleTargetSceneLoaded();
    }

    private bool IsTargetSceneLoaded()
    {
        if (string.IsNullOrWhiteSpace(targetSceneName)) return false;
        UnityEngine.SceneManagement.Scene scene = UnitySceneManager.GetSceneByName(targetSceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private void HandleTargetSceneLoaded()
    {
        if (sceneLoadEndHandled) return;
        sceneLoadEndHandled = true;

        UnityEngine.SceneManagement.Scene scene = UnitySceneManager.GetSceneByName(targetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            UnitySceneManager.SetActiveScene(scene);
        }

        RoundSystemBootstrap.EnsureForActiveScene();
        SetProgress(0.9f);
        SetStatus("Preparing players...");

        if (networkManager != null && networkManager.IsClientStarted)
        {
            if (clientReadyCoroutine != null) StopCoroutine(clientReadyCoroutine);
            clientReadyCoroutine = StartCoroutine(ReportReadyWhenInitialized());
        }
    }

    private void ActivateTargetScene(SceneLoadEndEventArgs args)
    {
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            if (args.LoadedScenes[i].name == targetSceneName)
            {
                UnitySceneManager.SetActiveScene(args.LoadedScenes[i]);
                RoundSystemBootstrap.EnsureForActiveScene();
                return;
            }
        }

        UnityEngine.SceneManagement.Scene scene =
            UnitySceneManager.GetSceneByName(targetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            UnitySceneManager.SetActiveScene(scene);
            RoundSystemBootstrap.EnsureForActiveScene();
        }
    }

    private bool ContainsTargetScene(SceneLoadEndEventArgs args)
    {
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            if (args.LoadedScenes[i].name == targetSceneName)
            {
                return true;
            }
        }

        for (int i = 0; i < args.SkippedSceneNames.Length; i++)
        {
            if (args.SkippedSceneNames[i] == targetSceneName)
            {
                return true;
            }
        }

        return UnitySceneManager.GetActiveScene().name == targetSceneName;
    }

    private IEnumerator ReportReadyWhenInitialized()
    {
        float nextStatusUpdateTime = 0f;
        float deadline = Time.unscaledTime + Mathf.Max(1f, localInitializationTimeoutSeconds);

        while (!IsLocalGameSceneReady(out string waitingReason))
        {
            RoundSystemBootstrap.EnsureForActiveScene();

            if (Time.unscaledTime >= nextStatusUpdateTime)
            {
                nextStatusUpdateTime = Time.unscaledTime + 0.5f;
                SetStatus($"Preparing: {waitingReason}");
            }

            if (Time.unscaledTime >= deadline)
            {
                Debug.LogWarning(
                    "[Loading] Local initialization timed out while " + waitingReason +
                    ". Reporting scene readiness so every client can leave the loading barrier.");
                break;
            }

            yield return null;
        }

        clientReadyCoroutine = null;
        SetProgress(1f);
        SetStatus("Waiting for other players...");
        Debug.Log($"[Loading] Local client is ready for '{targetSceneName}'.");

        networkManager.ClientManager.Broadcast(new GameSceneReadyBroadcast
        {
            SceneName = targetSceneName
        });
    }

    private bool IsLocalGameSceneReady(out string waitingReason)
    {
        if (UnitySceneManager.GetActiveScene().name != targetSceneName)
        {
            waitingReason = "activating scene";
            return false;
        }

        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            waitingReason = "building tile grid";
            return false;
        }

        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerMovement movement = movements[i];
            if (movement != null &&
                movement.IsOwner &&
                movement.IsLocalInitializationComplete &&
                IsPlayerColorReady(movement))
            {
                waitingReason = string.Empty;
                return true;
            }
        }

        waitingReason = "spawning local player";
        return false;
    }

    private static bool IsPlayerColorReady(PlayerMovement movement)
    {
        Player player = movement.GetComponent<Player>();
        if (player == null)
        {
            return true;
        }

        return player.myPaintOwner.Value != PaintOwner.Neutral &&
               player.TryApplyPaintOwnerMaterial();
    }

    private void OnClientReady(
        NetworkConnection connection,
        GameSceneReadyBroadcast message,
        Channel channel)
    {
        if (!serverWaitingForReady ||
            message.SceneName != targetSceneName ||
            connection == null ||
            !expectedClientIds.Contains(connection.ClientId))
        {
            return;
        }

        readyClientIds.Add(connection.ClientId);
        Debug.Log(
            $"[Loading] Client {connection.ClientId} ready " +
            $"({readyClientIds.Count}/{expectedClientIds.Count}).");
        TryReleaseClients(false);
    }

    private void PruneDisconnectedClients()
    {
        if (networkManager == null)
        {
            return;
        }

        expectedClientIds.RemoveWhere(clientId => !IsClientStillConnected(clientId));
    }

    private bool IsClientStillConnected(int clientId)
    {
        if (networkManager.ServerManager.Clients.ContainsKey(clientId))
        {
            return true;
        }

        NetworkConnection localConnection = networkManager.ClientManager.Connection;
        return networkManager.IsClientStarted &&
               localConnection != null &&
               localConnection.IsActive &&
               localConnection.ClientId == clientId;
    }

    private void TryReleaseClients(bool timedOut)
    {
        if (!serverWaitingForReady ||
            expectedClientIds.Count == 0 ||
            !readyClientIds.IsSupersetOf(expectedClientIds))
        {
            return;
        }

        ReleaseClients(timedOut);
    }

    private void ReleaseClients(bool timedOut)
    {
        serverWaitingForReady = false;
        Debug.Log(
            "[RoundFlow] Loading release issued. timedOut=" + timedOut +
            " readyClients=" + readyClientIds.Count +
            "/" + expectedClientIds.Count +
            " targetScene=" + targetSceneName + ".");

        GameSceneLoadingReleaseBroadcast message = new GameSceneLoadingReleaseBroadcast
        {
            SceneName = targetSceneName,
            TimedOut = timedOut
        };

        networkManager.ServerManager.Broadcast(message);

        if (serverGameStartCoroutine != null)
        {
            StopCoroutine(serverGameStartCoroutine);
        }

        serverGameStartCoroutine = StartCoroutine(StartServerGameWhenReady());

        if (networkManager.IsClientStarted)
        {
            ApplyRelease(message);
        }
    }

    private IEnumerator StartServerGameWhenReady()
    {
        float nextLogTime = 0f;
        while (networkManager != null &&
               networkManager.IsServerStarted &&
               (!NetworkRoundController.HasInstance || !GameRoundManager.HasInstance))
        {
            RoundSystemBootstrap.EnsureForActiveScene();
            if (Time.unscaledTime >= nextLogTime)
            {
                nextLogTime = Time.unscaledTime + 1f;
                Debug.Log(
                    "[RoundFlow] Waiting for round systems after loading. " +
                    "NetworkRoundController=" + NetworkRoundController.HasInstance +
                    " GameRoundManager=" + GameRoundManager.HasInstance + ".");
            }

            yield return null;
        }

        serverGameStartCoroutine = null;
        if (networkManager != null &&
            networkManager.IsServerStarted &&
            NetworkRoundController.HasInstance)
        {
            Debug.Log("[RoundFlow] Invoking NetworkRoundController.ServerStartGameAfterLoading.");
            NetworkRoundController.Instance.ServerStartGameAfterLoading();
        }
    }

    private void OnLoadingRelease(
        GameSceneLoadingReleaseBroadcast message,
        Channel channel)
    {
        ApplyRelease(message);
    }

    private void ApplyRelease(GameSceneLoadingReleaseBroadcast message)
    {
        if (!transitionActive || message.SceneName != targetSceneName)
        {
            return;
        }

        transitionActive = false;
        IsGameplayReleased = true;
        if (clientReadyCoroutine != null)
        {
            StopCoroutine(clientReadyCoroutine);
            clientReadyCoroutine = null;
        }
        HideLoadingView();

        if (message.TimedOut)
        {
            Debug.LogWarning("[Loading] Gameplay released after ready timeout.");
        }
    }

    private void ShowLoadingView()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        if (loadingView != null)
        {
            loadingView.SetActive(true);
        }
    }

    private void HideLoadingView()
    {
        if (loadingView != null)
        {
            loadingView.SetActive(false);
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetProgress(float progress)
    {
        if (progressFillImage != null)
        {
            progressFillImage.fillAmount = Mathf.Clamp01(progress);
        }
    }
}
