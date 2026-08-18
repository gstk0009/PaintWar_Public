using FishNet;
using FishNet.Transporting;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public class LobbyButtonController : MonoBehaviour
{
    [Header("Development Settings")]
    [SerializeField] private bool isLocalTesting = true;
    [SerializeField] private bool clonesJoinAsClientOnly = true;
    [SerializeField] private bool autoStartServerInBatchMode = true;
    [SerializeField] private float profileResendIntervalSeconds = 2f;
    [SerializeField] private float lobbyProgressWatchdogSeconds = 6f;
    [SerializeField] private float roomAssignmentWatchdogSeconds = 90f;
    [SerializeField] private float completedCountdownGraceSeconds = 8f;

    [Header("Lobby UI")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject[] frontLobbyOnlyObjects;

    private static bool sharedManualLeaveRequested;

    public static event Action<bool> MatchmakingViewStateChanged;
    public static bool IsMatchmakingViewActive { get; private set; }

    public static void PrepareForFrontLobbyReturn()
    {
        RoomAssignmentClient.CancelMatchmakingSearch();
        sharedManualLeaveRequested = true;
        SetMatchmakingViewActive(false);
        LobbyCurrentCharacterPreview.RefreshImmediatelyForFrontScreen();
    }

    private bool connectionEventsRegistered;
    private bool enterGameRequested;
    private bool manualLeaveRequested;
    private bool clientConnectionChanging;
    private float enterGameRequestedAt = -1f;
    private float nextProfileResendAt = -1f;
    private float countdownCompletedAt = -1f;

    private void Start()
    {
        SetMatchmakingViewActive(false);
        RegisterConnectionEvents();
        AutoStartDedicatedServerIfNeeded();
        LoadAndApplyCloudProfileForLobby();
        RefreshButtonStates();
    }

    private void OnDestroy()
    {
        UnregisterConnectionEvents();
    }

    private void Update()
    {
        MonitorEnterGameProgress();
        RefreshButtonStates();
    }

    public void OnClick_StartMatchmaking()
    {
        LobbyCurrentCharacterPreview.HideImmediatelyForMatchmaking();
        SetMatchmakingViewActive(true);
        SetFrontLobbyOnlyObjectsActive(false);
        enterGameRequested = true;
        manualLeaveRequested = false;
        sharedManualLeaveRequested = false;
        RoomAssignmentClient.BeginMatchmakingSearch();
        enterGameRequestedAt = Time.unscaledTime;
        nextProfileResendAt = Time.unscaledTime + profileResendIntervalSeconds;
        countdownCompletedAt = -1f;

        if (clientConnectionChanging ||
            RoomAssignmentClient.IsRedirecting ||
            RoomAssignmentClient.IsConnectingToRoom)
        {
            Debug.Log("[Network] Start matchmaking ignored while connection is changing.");
            return;
        }

        bool shouldStartServer = ShouldStartLocalServer();
        Debug.Log(
            "[Network] Start matchmaking clicked. " +
            "startServer=" + shouldStartServer +
            " clientStarted=" + InstanceFinder.ClientManager.Started +
            " serverStarted=" + InstanceFinder.ServerManager.Started + ".");

        if (shouldStartServer && !InstanceFinder.ServerManager.Started)
        {
            bool started = InstanceFinder.ServerManager.StartConnection();
            Debug.Log("[Network] Server start requested. result=" + started + ".");
        }

        if (!InstanceFinder.ClientManager.Started)
        {
            EnsureClientTargetsMasterServer();
            clientConnectionChanging = true;
            bool started = InstanceFinder.ClientManager.StartConnection();
            Debug.Log(
                "[Network] Client start requested. result=" + started +
                " address=" + GetClientAddress() + ".");
            if (!started)
            {
                clientConnectionChanging = false;
            }
        }
    }

    public void OnClick_StartHost()
    {
        if (!InstanceFinder.ServerManager.Started)
        {
            bool started = InstanceFinder.ServerManager.StartConnection();
            Debug.Log("[Network] Host server start requested. result=" + started + ".");
        }

        if (!InstanceFinder.ClientManager.Started)
        {
            EnsureClientTargetsMasterServer();
            bool started = InstanceFinder.ClientManager.StartConnection();
            Debug.Log("[Network] Host client start requested. result=" + started + ".");
        }
    }

    public void OnClick_JoinAsClient()
    {
        LobbyCurrentCharacterPreview.HideImmediatelyForMatchmaking();
        SetMatchmakingViewActive(true);
        SetFrontLobbyOnlyObjectsActive(false);
        enterGameRequested = true;
        manualLeaveRequested = false;
        sharedManualLeaveRequested = false;
        RoomAssignmentClient.BeginMatchmakingSearch();
        enterGameRequestedAt = Time.unscaledTime;
        nextProfileResendAt = Time.unscaledTime + profileResendIntervalSeconds;
        countdownCompletedAt = -1f;

        if (InstanceFinder.ServerManager.Started)
        {
            Debug.LogWarning(
                "[Network] JoinAsClient skipped because this process is already running a server.");
            return;
        }

        if (!InstanceFinder.ClientManager.Started)
        {
            EnsureClientTargetsMasterServer();
            bool started = InstanceFinder.ClientManager.StartConnection();
            Debug.Log(
                "[Network] Client-only start requested. result=" + started +
                " address=" + GetClientAddress() + ".");
        }
    }

    public void OnClick_LeaveLobby()
    {
        SetMatchmakingViewActive(false);
        manualLeaveRequested = true;
        sharedManualLeaveRequested = true;
        enterGameRequested = false;
        RoomAssignmentClient.CancelMatchmakingSearch();
        enterGameRequestedAt = -1f;
        nextProfileResendAt = -1f;
        countdownCompletedAt = -1f;
        RoomAssignmentStore.Clear();
        EnsureClientTargetsMasterServer();

        bool clientStarted = InstanceFinder.ClientManager != null &&
                             InstanceFinder.ClientManager.Started;
        bool serverStarted = InstanceFinder.ServerManager != null &&
                             InstanceFinder.ServerManager.Started;

        if (clientStarted)
        {
            InstanceFinder.ClientManager.StopConnection();
        }

        bool stoppedLocalServer = false;
        if (serverStarted && ShouldStopLocalServerOnExit())
        {
            InstanceFinder.ServerManager.StopConnection(true);
            stoppedLocalServer = true;
            Debug.Log("[Network] Local lobby server stopped for exit.");
        }

        if (clientStarted && serverStarted && !stoppedLocalServer)
        {
            Debug.Log("[Network] Local client left the lobby. Server remains running.");
        }
        else if (clientStarted && stoppedLocalServer)
        {
            Debug.Log("[Network] Local lobby connection stopped.");
        }
        else if (clientStarted)
        {
            Debug.Log("[Network] Lobby connection stopped.");
        }
        else
        {
            Debug.Log("[Network] Exit requested while client was not connected.");
        }

        LobbyWaitTimerView.HideSceneTimerImmediate();
        SetButtonStates(false);
        LobbyCurrentCharacterPreview.RefreshImmediatelyForFrontScreen();
    }

private void RefreshButtonStates()
    {
        bool clientStarted = InstanceFinder.ClientManager != null &&
                             InstanceFinder.ClientManager.Started;

        SetMatchmakingViewActive(
            !manualLeaveRequested &&
            !sharedManualLeaveRequested &&
            (enterGameRequested || clientStarted));
        SetButtonStates(clientStarted);
    }

    private static void SetMatchmakingViewActive(bool active)
    {
        if (IsMatchmakingViewActive == active)
        {
            return;
        }

        IsMatchmakingViewActive = active;
        MatchmakingViewStateChanged?.Invoke(active);
    }

    private void SetButtonStates(bool clientStarted)
    {
        if (startButton != null)
        {
            startButton.gameObject.SetActive(!clientStarted);
            startButton.interactable = !clientStarted;
        }

        if (leaveButton != null)
        {
            leaveButton.gameObject.SetActive(clientStarted);
            leaveButton.interactable = clientStarted;
        }

        SetFrontLobbyOnlyObjectsActive(!clientStarted && !IsMatchmakingViewActive);
    }

    private void SetFrontLobbyOnlyObjectsActive(bool active)
    {
        if (frontLobbyOnlyObjects == null)
        {
            return;
        }

        for (int i = 0; i < frontLobbyOnlyObjects.Length; i++)
        {
            GameObject target = frontLobbyOnlyObjects[i];
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }

    private void MonitorEnterGameProgress()
    {
        if (!enterGameRequested ||
            manualLeaveRequested ||
            sharedManualLeaveRequested ||
            InstanceFinder.ClientManager == null ||
            !InstanceFinder.ClientManager.Started ||
            RoomAssignmentClient.IsRedirecting ||
            RoomAssignmentClient.IsConnectingToRoom)
        {
            return;
        }

        if (Time.unscaledTime >= nextProfileResendAt)
        {
            nextProfileResendAt = Time.unscaledTime +
                Mathf.Max(1f, profileResendIntervalSeconds);
            SendPlayerProfile();
        }

        if (enterGameRequestedAt > 0f &&
            Time.unscaledTime - enterGameRequestedAt >=
            Mathf.Max(3f, lobbyProgressWatchdogSeconds) &&
            !HasLobbyProgress())
        {
            RestartMasterConnection(
                "[Network] Lobby progress did not arrive after connecting. Reconnecting to master.");
            return;
        }

        bool countdownCompleted = IsMatchmakingCountdownComplete();
        if (countdownCompleted)
        {
            if (countdownCompletedAt < 0f)
            {
                countdownCompletedAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - countdownCompletedAt >=
                Mathf.Max(1f, completedCountdownGraceSeconds))
            {
                RestartMasterConnection(
                    "[Network] Room assignment did not arrive after countdown completion. Reconnecting to master.");
            }

            return;
        }

        countdownCompletedAt = -1f;
        if (enterGameRequestedAt > 0f &&
            Time.unscaledTime - enterGameRequestedAt >=
            Mathf.Max(10f, roomAssignmentWatchdogSeconds))
        {
            RestartMasterConnection(
                "[Network] Room assignment watchdog timed out. Reconnecting to master.");
        }
    }

    private static bool IsMatchmakingCountdownComplete()
    {
        MatchmakingManager manager = MatchmakingManager.Instance;
        if (manager == null)
        {
            return false;
        }

        int waitRemaining = manager.matchWaitRemainingSeconds.Value;
        int startRemaining = manager.gameStartRemainingSeconds.Value;
        return waitRemaining == 0 && startRemaining == 0;
    }

    private static bool HasLobbyProgress()
    {
        MatchmakingManager manager = MatchmakingManager.Instance;
        if (manager == null)
        {
            return false;
        }

        return manager.lobbyHumanCount.Value > 0 ||
               manager.lobbyParticipantCount.Value > 0 ||
               manager.matchWaitRemainingSeconds.Value >= 0 ||
               manager.gameStartRemainingSeconds.Value >= 0;
    }

    private void RestartMasterConnection(string reason)
    {
        Debug.LogWarning(reason);
        countdownCompletedAt = -1f;
        enterGameRequestedAt = Time.unscaledTime;
        nextProfileResendAt = Time.unscaledTime + profileResendIntervalSeconds;
        EnsureClientTargetsMasterServer();

        if (InstanceFinder.ClientManager != null &&
            InstanceFinder.ClientManager.Started)
        {
            InstanceFinder.ClientManager.StopConnection();
        }
    }

    private bool ShouldStartLocalServer()
    {
#if !UNITY_EDITOR
        return false;
#else
        if (!isLocalTesting)
        {
            return false;
        }

        if (IsConfiguredForRemoteServer())
        {
            return false;
        }

        return !clonesJoinAsClientOnly || !IsEditorClone();
#endif
    }

    private bool ShouldStopLocalServerOnExit()
    {
        if (Application.isBatchMode)
        {
            return false;
        }

        return ShouldStartLocalServer();
    }

    private static bool IsConfiguredForRemoteServer()
    {
        return IsRemoteAddress(GetClientAddress());
    }

    private static string GetClientAddress()
    {
        Transport transport = InstanceFinder.NetworkManager != null
            ? InstanceFinder.NetworkManager.TransportManager.Transport
            : null;
        return transport != null ? transport.GetClientAddress() : string.Empty;
    }

    private static bool IsRemoteAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        address = address.Trim().ToLowerInvariant();
        return address != "localhost" &&
               address != "127.0.0.1" &&
               address != "::1" &&
               address != "0.0.0.0";
    }

    private static void EnsureClientTargetsMasterServer()
    {
        if (InstanceFinder.ServerManager != null &&
            InstanceFinder.ServerManager.Started)
        {
            return;
        }

        string address = GetClientAddress();
#if !UNITY_EDITOR
        if (!IsRemoteAddress(address))
        {
            address = ServerLaunchMode.DefaultPublicAddress;
        }
#endif
        if (!IsRemoteAddress(address))
        {
            return;
        }

        ushort port = ServerLaunchMode.GetConfiguredPort();
        if (port == ServerLaunchMode.MasterPort &&
            string.Equals(GetClientAddress(), address, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ServerLaunchMode.ApplyClientTransport(address, ServerLaunchMode.MasterPort);
        Debug.Log(
            "[Rooms] Reset client transport to master before matchmaking. address=" +
            address + ".");
    }

    private void AutoStartDedicatedServerIfNeeded()
    {
        if (!autoStartServerInBatchMode ||
            !Application.isBatchMode ||
            InstanceFinder.ServerManager == null ||
            InstanceFinder.ServerManager.Started)
        {
            return;
        }

        ServerLaunchMode.ApplyServerTransportOverride();
        bool started = InstanceFinder.ServerManager.StartConnection();
        Debug.Log("[Network] Batch mode server auto-start requested. result=" + started + ".");
    }

    private void RegisterConnectionEvents()
    {
        if (connectionEventsRegistered ||
            InstanceFinder.ClientManager == null ||
            InstanceFinder.ServerManager == null)
        {
            return;
        }

        InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        InstanceFinder.ClientManager.OnAuthenticated += OnClientAuthenticated;
        InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
        connectionEventsRegistered = true;
    }

    private void UnregisterConnectionEvents()
    {
        if (!connectionEventsRegistered)
        {
            return;
        }

        if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            InstanceFinder.ClientManager.OnAuthenticated -= OnClientAuthenticated;
        }

        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        connectionEventsRegistered = false;
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        Debug.Log(
            "[Network] Client connection state: " + args.ConnectionState +
            " address=" + GetClientAddress() + ".");

        if (args.ConnectionState == LocalConnectionState.Starting ||
            args.ConnectionState == LocalConnectionState.Stopping)
        {
            clientConnectionChanging = true;
        }
        else if (args.ConnectionState == LocalConnectionState.Started ||
                 args.ConnectionState == LocalConnectionState.Stopped)
        {
            clientConnectionChanging = false;
        }

        if (args.ConnectionState == LocalConnectionState.Started)
        {
            SetMatchmakingViewActive(true);
            SendPlayerProfile();
        }

        if (args.ConnectionState == LocalConnectionState.Stopped &&
            (!enterGameRequested ||
             manualLeaveRequested ||
             sharedManualLeaveRequested))
        {
            SetMatchmakingViewActive(false);
        }
    }

    private void OnClientAuthenticated()
    {
        SendPlayerProfile();
    }

    public static void SendCurrentPlayerProfile()
    {
        SendPlayerProfile();
    }

    private async void LoadAndApplyCloudProfileForLobby()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        try
        {
            bool loaded = await FirebasePlayerProfileStore.LoadCurrentUserAsync();
            if (!loaded && !FirebasePlayerProfileStore.LastLoadFailedTransiently)
            {
                FirebasePlayerProfileStore.SaveCurrentSnapshot();
                return;
            }

            if (loaded)
            {
                FirebasePlayerProfileStore.ApplyCurrentLocalDataToRuntime();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[FirestoreProfile] Lobby profile load failed; using local cache.");
            Debug.LogException(exception);
        }
    }

    private static void SendPlayerProfile()
    {
        if (InstanceFinder.ClientManager == null ||
            !InstanceFinder.ClientManager.Started)
        {
            return;
        }

        string nickname = PlayerProfile.GetOrCreateNickname();
        string userId = PlayerProfile.GetSavedUserId();
        InstanceFinder.ClientManager.Broadcast(new PlayerProfileBroadcast
        {
            UserId = userId,
            Nickname = nickname,
            AppearanceCode = CharacterAppearanceStore.NormalizeCode(
                CharacterAppearanceStore.CurrentCode),
            MotionCode = CharacterMotionStore.GetCurrentProfileMotionCode(),
            SelectedSkillId = PlayerSkillLoadoutStore.CurrentSkillId
        });
        LobbyPlayer.SubmitLocalProfileToOwnedLobbyPlayer();

        Debug.Log(
            "[Profile] Sent player profile to server: " + nickname +
            " uidSet=" + !string.IsNullOrWhiteSpace(userId) +
            " port=" + ServerLaunchMode.GetConfiguredPort() + ".");
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        Debug.Log("[Network] Server connection state: " + args.ConnectionState + ".");
    }

    private static bool IsEditorClone()
    {
#if UNITY_EDITOR
        Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
        MethodInfo isCloneMethod = clonesManagerType != null
            ? clonesManagerType.GetMethod("IsClone", BindingFlags.Public | BindingFlags.Static)
            : null;

        if (isCloneMethod != null)
        {
            object value = isCloneMethod.Invoke(null, null);
            return value is bool && (bool)value;
        }

        string projectPath = Application.dataPath.Replace('\\', '/');
        return projectPath.Contains("_clone") || projectPath.Contains("_Clone");
#else
        return false;
#endif
    }
}
