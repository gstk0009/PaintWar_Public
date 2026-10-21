using FishNet;
using FishNet.Transporting;
using System.Collections;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

// master matchmaking server에서 배정된 room server로 클라이언트를 이동시키는 클라이언트 측 전환 담당자입니다.
// 짧은 앱 재시작 후 같은 room으로 재접속할 수 있도록 room assignment를 PlayerPrefs에 보관합니다.
public sealed class RoomAssignmentClient : MonoBehaviour
{
    private const int MasterReconnectAttempts = 15;
    private const float MasterReconnectDelaySeconds = 1f;
    private const string LobbySceneName = "LobbyScene";

    // UI와 loading 코드는 이 flag들을 보고 scene/client 전환이 중복 시작되지 않게 합니다.
    public static bool IsRedirecting { get; private set; }
    public static bool IsConnectingToRoom { get; private set; }
    public static bool IsWaitingForNextMatch { get; private set; }
    private static RoomAssignmentClient instance;

    private bool registered;
    private bool pendingRoomConnection;
    private bool pendingRoomConnectionFromSavedAssignment;
    private bool connectedToRoom;
    private RoomConnectionTarget pendingTarget;
    private bool matchmakingSearchActive;
    private int masterReconnectCount;
    private float masterReconnectAt = -1f;

    private void Start()
    {
        Register();
    }

    private void Update()
    {
        if (!matchmakingSearchActive ||
            masterReconnectAt < 0f ||
            Time.unscaledTime < masterReconnectAt ||
            InstanceFinder.ClientManager == null ||
            InstanceFinder.ClientManager.Started ||
            IsRedirecting ||
            IsConnectingToRoom)
        {
            return;
        }

        masterReconnectAt = -1f;
        if (!StartMasterConnection(ServerLaunchMode.DefaultPublicAddress))
        {
            ScheduleMasterReconnect();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        Unregister();
        if (instance == this)
        {
            instance = null;
            IsWaitingForNextMatch = false;
        }

        if (IsRedirecting)
        {
            IsRedirecting = false;
        }
    }

    public static RoomAssignmentClient EnsureInstance()
    {
        if (instance != null)
        {
            instance.Register();
            return instance;
        }

        GameObject host = new GameObject("RoomAssignmentClient");
        instance = host.AddComponent<RoomAssignmentClient>();
        instance.Register();
        return instance;
    }

    public static void BeginMatchmakingSearch()
    {
        RoomAssignmentClient client = EnsureInstance();
        client.matchmakingSearchActive = true;
        client.masterReconnectCount = 0;
        client.masterReconnectAt = -1f;
    }

    public static void CancelMatchmakingSearch()
    {
        if (instance == null)
        {
            return;
        }

        instance.matchmakingSearchActive = false;
        instance.masterReconnectCount = 0;
        instance.masterReconnectAt = -1f;
        IsWaitingForNextMatch = false;
        LoadingTransitionController.HideExternalOverlay();
    }

    public static bool TryConnectToSavedRoom(MonoBehaviour runner)
    {
        // 복귀 플레이어가 기존 room을 먼저 시도할 수 있도록 일반 matchmaking 전에 호출됩니다.
        if (!RoomAssignmentStore.TryLoad(out RoomConnectionTarget target))
        {
            return false;
        }

        RoomAssignmentClient client = EnsureInstance();
        client.StartCoroutine(client.SwitchToRoomRoutine(target, true));
        return true;
    }

    private void Register()
    {
        if (registered ||
            InstanceFinder.ClientManager == null)
        {
            return;
        }

        InstanceFinder.ClientManager.RegisterBroadcast<RoomAssignmentBroadcast>(
            OnRoomAssignment);
        InstanceFinder.ClientManager.RegisterBroadcast<MatchmakingQueueStateBroadcast>(
            OnMatchmakingQueueState);
        InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        registered = true;
    }

    private void Unregister()
    {
        if (!registered ||
            InstanceFinder.ClientManager == null)
        {
            return;
        }

        InstanceFinder.ClientManager.UnregisterBroadcast<RoomAssignmentBroadcast>(
            OnRoomAssignment);
        InstanceFinder.ClientManager.UnregisterBroadcast<MatchmakingQueueStateBroadcast>(
            OnMatchmakingQueueState);
        InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        registered = false;
    }

    private void OnRoomAssignment(RoomAssignmentBroadcast message, Channel channel)
    {
        IsWaitingForNextMatch = false;
        masterReconnectCount = 0;
        masterReconnectAt = -1f;

        // master server가 이 클라이언트를 room에 배정했으므로 master 연결을 끊기 전에 저장합니다.
        RoomConnectionTarget target = new RoomConnectionTarget
        {
            RoomId = message.RoomId,
            Address = message.Address,
            Port = message.Port,
            ReconnectGraceSeconds = message.ReconnectGraceSeconds
        };

        RoomAssignmentStore.Save(target);
        LoadingTransitionController.ShowExternalOverlay("Preparing match room...");
        StartCoroutine(SwitchToRoomRoutine(target, false));
    }

    private void OnMatchmakingQueueState(
        MatchmakingQueueStateBroadcast message,
        Channel channel)
    {
        IsWaitingForNextMatch = message.WaitingForNextMatch;
        masterReconnectAt = -1f;
        masterReconnectCount = 0;

        if (message.WaitingForNextMatch)
        {
            matchmakingSearchActive = true;
            LoadingTransitionController.ShowExternalOverlay("Finding next match...");
            Debug.Log("[Rooms] Waiting in the next-match queue.");
            return;
        }

        LoadingTransitionController.HideExternalOverlay();
        LobbyButtonController.SendCurrentPlayerProfile();
        Debug.Log("[Rooms] Admitted to the next match roster.");
    }

    private IEnumerator SwitchToRoomRoutine(RoomConnectionTarget target, bool fromSavedAssignment)
    {
        if (string.IsNullOrWhiteSpace(target.Address) || target.Port == 0)
        {
            yield break;
        }

        IsRedirecting = true;
        LoadingTransitionController.ShowExternalOverlay(
            fromSavedAssignment
                ? "Checking previous match..."
                : "Preparing match room...");
        Debug.Log(
            "[Rooms] Switching client to room " + target.RoomId +
            " at " + target.Address + ":" + target.Port +
            (fromSavedAssignment ? " from saved assignment." : "."));

        // 먼저 master 연결을 끊고, 같은 transport의 대상 주소를 room endpoint로 바꿉니다.
        if (InstanceFinder.ClientManager != null &&
            InstanceFinder.ClientManager.Started)
        {
            InstanceFinder.ClientManager.StopConnection();
            float deadline = Time.unscaledTime + 5f;
            while (InstanceFinder.ClientManager.Started &&
                   Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        if (!ServerLaunchMode.ApplyClientTransport(target.Address, target.Port))
        {
            LoadingTransitionController.HideExternalOverlay();
            IsRedirecting = false;
            yield break;
        }

        LoadingTransitionController.SetExternalOverlayStatus("Connecting to match room...");
        yield return null;

        bool started = InstanceFinder.ClientManager != null &&
                       InstanceFinder.ClientManager.StartConnection();
        Debug.Log("[Rooms] Room client connection requested. result=" + started + ".");
        if (started)
        {
            pendingTarget = target;
            pendingRoomConnection = true;
            pendingRoomConnectionFromSavedAssignment = fromSavedAssignment;
            IsConnectingToRoom = true;
        }
        else
        {
            ClearPendingRoomConnection();
            if (fromSavedAssignment)
            {
                RoomAssignmentStore.Clear();
                if (!StartMasterConnection(target.Address))
                {
                    ScheduleMasterReconnect();
                }
            }
            else
            {
                LoadingTransitionController.HideExternalOverlay();
            }
        }

        if (!started)
        {
            IsRedirecting = false;
        }
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        // 연결된 room connection이 끊기면 저장된 assignment를 지우고 master server로 돌아갑니다.
        if (args.ConnectionState == LocalConnectionState.Stopped &&
            connectedToRoom &&
            !pendingRoomConnection)
        {
            connectedToRoom = false;
            if (!RoomAssignmentStore.TryLoad(out RoomConnectionTarget _))
            {
                LoadingTransitionController.HideExternalOverlay();
                return;
            }

            RoomAssignmentStore.Clear();
            LoadingTransitionController.SetExternalOverlayStatus("Returning to matchmaking...");
            if (!StartMasterConnection(ServerLaunchMode.DefaultPublicAddress))
            {
                ScheduleMasterReconnect();
            }
            return;
        }

        if (!pendingRoomConnection)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped &&
                matchmakingSearchActive &&
                !IsRedirecting &&
                !IsConnectingToRoom)
            {
                ScheduleMasterReconnect();
            }
            return;
        }

        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("[Rooms] Room client connected.");
            connectedToRoom = true;
            LoadingTransitionController.SetExternalOverlayStatus("Loading match...");
            LoadingTransitionController.HideExternalOverlayWhenSceneReady("GameScene");
            LobbyButtonController.SendCurrentPlayerProfile();
            ClearPendingRoomConnection();
            return;
        }

        if (args.ConnectionState != LocalConnectionState.Stopped)
        {
            return;
        }

        // 저장된 assignment가 만료된 room을 가리킬 수 있으므로 실패하면 master로 fallback합니다.
        bool fallbackToMaster = pendingRoomConnectionFromSavedAssignment;
        string masterAddress = pendingTarget.Address;
        Debug.LogWarning(
            "[Rooms] Room connection failed. room=" + pendingTarget.RoomId +
            " port=" + pendingTarget.Port +
            " fallbackToMaster=" + fallbackToMaster + ".");

        RoomAssignmentStore.Clear();
        LoadingTransitionController.SetExternalOverlayStatus("Returning to matchmaking...");
        ClearPendingRoomConnection();
        if (fallbackToMaster)
        {
            if (!StartMasterConnection(masterAddress))
            {
                ScheduleMasterReconnect();
            }
        }
        else
        {
            LoadingTransitionController.HideExternalOverlay();
        }
    }

    private void ClearPendingRoomConnection()
    {
        pendingRoomConnection = false;
        pendingRoomConnectionFromSavedAssignment = false;
        pendingTarget = default;
        IsConnectingToRoom = false;
        IsRedirecting = false;
    }

    private bool StartMasterConnection(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            address = ServerLaunchMode.DefaultPublicAddress;
        }

        if (!ServerLaunchMode.ApplyClientTransport(address, ServerLaunchMode.MasterPort))
        {
            return false;
        }

        bool started = InstanceFinder.ClientManager != null &&
                       !InstanceFinder.ClientManager.Started &&
                       InstanceFinder.ClientManager.StartConnection();
        Debug.Log("[Rooms] Fallback master connection requested. result=" + started + ".");
        if (!started)
        {
            LoadingTransitionController.HideExternalOverlay();
        }

        return started;
    }

    private void ScheduleMasterReconnect()
    {
        if (!matchmakingSearchActive || masterReconnectAt >= 0f)
        {
            return;
        }

        if (masterReconnectCount >= MasterReconnectAttempts)
        {
            matchmakingSearchActive = false;
            masterReconnectAt = -1f;
            Debug.LogWarning(
                "[Rooms] Could not find another room within the reconnect limit. " +
                "Returning to the lobby entrance.");
            StartCoroutine(ReturnToLobbyEntranceRoutine());
            return;
        }

        masterReconnectCount++;
        masterReconnectAt = Time.unscaledTime + MasterReconnectDelaySeconds;
        LoadingTransitionController.SetExternalOverlayStatus("Finding another match...");
        Debug.Log(
            "[Rooms] Searching for another room. Master reconnect scheduled (" +
            masterReconnectCount + "/" + MasterReconnectAttempts + ").");
    }

    private IEnumerator ReturnToLobbyEntranceRoutine()
    {
        RoomAssignmentStore.Clear();
        ServerLaunchMode.ApplyClientTransport(
            ServerLaunchMode.DefaultPublicAddress,
            ServerLaunchMode.MasterPort);

        if (UnitySceneManager.GetActiveScene().name != LobbySceneName)
        {
            UnitySceneManager.LoadScene(LobbySceneName);
            yield return null;
        }

        LoadingTransitionController.HideExternalOverlay();
        LobbyButtonController.PrepareForFrontLobbyReturn();
    }
}
