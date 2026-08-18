using FishNet;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// room-server process의 수명 감시자입니다.
/// master가 읽을 heartbeat를 쓰고 startup/empty-room/game-over timeout이 지나면 room을 종료합니다.
/// </summary>
public sealed class RoomServerLifecycle : MonoBehaviour
{
    private const string HostObjectName = "Room Server Lifecycle";

    public static RoomServerLifecycle Instance { get; private set; }

    [SerializeField] private float emptyRoomQuitDelaySeconds = 180f;
    [SerializeField] private float gameOverQuitDelaySeconds = 15f;
    [SerializeField] private float finalResultQuitFallbackSeconds = 45f;
    [SerializeField] private float startupQuitDelaySeconds = 45f;
    [SerializeField] private float heartbeatIntervalSeconds = 1f;

    // 음수 값은 해당 조건을 현재 타이밍 중이 아니라는 뜻입니다.
    private float emptySince = -1f;
    private float finalResultSince = -1f;
    private float gameOverSince = -1f;
    private float processStartedAt;
    private float nextHeartbeatAt;

    // 최종 결과 연출이 시작된 순간부터 true입니다.
    // GamePhase가 아직 RoundResult여도 master에는 FinalResult를 계속 보고합니다.
    private bool finalResultStarted;

    /// <summary>
    /// 최종 결과 UI가 시작되는 순간 server의 GameRoundManager에서 호출합니다.
    /// room process는 계속 유지하지만 master가 이 방을 재접속 대상으로 사용하지 않게 합니다.
    /// </summary>
    public void NotifyFinalResultStarted()
    {
        if (!ServerLaunchMode.IsRoomServer)
        {
            return;
        }

        finalResultStarted = true;

        if (finalResultSince < 0f)
        {
            finalResultSince = Time.unscaledTime;
        }

        int clients =
            InstanceFinder.ServerManager != null &&
            InstanceFinder.ServerManager.Started
                ? InstanceFinder.ServerManager.Clients.Count
                : 0;

        WriteHeartbeat("FinalResult", clients);

        Debug.Log(
            "[Rooms] FinalResult started for room " +
            ServerLaunchMode.RoomId +
            ". Reconnect assignment is now blocked.");
    }

    public void NotifyGameOver()
    {
        if (!ServerLaunchMode.IsRoomServer)
        {
            return;
        }

        finalResultStarted = true;

        if (finalResultSince < 0f)
        {
            finalResultSince = Time.unscaledTime;
        }

        if (gameOverSince < 0f)
        {
            gameOverSince = Time.unscaledTime;
        }

        int clients =
            InstanceFinder.ServerManager != null &&
            InstanceFinder.ServerManager.Started
                ? InstanceFinder.ServerManager.Clients.Count
                : 0;

        WriteHeartbeat("GameOver", clients);

        Debug.Log(
            "[Rooms] GameOver cleanup scheduled for room " +
            ServerLaunchMode.RoomId +
            ".");
    }

    public static RoomServerLifecycle EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject host = new GameObject(HostObjectName);
        return host.AddComponent<RoomServerLifecycle>();
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
        processStartedAt = Time.realtimeSinceStartup;

        if (ServerLaunchMode.IsRoomServer)
        {
            WriteHeartbeat("starting", 0);
        }
    }

    private void Update()
    {
        if (!ServerLaunchMode.IsRoomServer)
        {
            return;
        }

        // startup watchdog은 FishNet listen 전에 child process가 영원히 멈춰 있는 상황을 막습니다.
        if (InstanceFinder.ServerManager == null ||
            !InstanceFinder.ServerManager.Started)
        {
            WriteHeartbeatIfDue("starting", 0);

            if (Time.realtimeSinceStartup - processStartedAt >=
                Mathf.Max(5f, startupQuitDelaySeconds))
            {
                QuitRoomServer(
                    "[Rooms] Room server startup timeout reached.");
            }

            return;
        }

        int clientCount =
            InstanceFinder.ServerManager.Clients.Count;

        WriteHeartbeatIfDue(
            GetRuntimeState(),
            clientCount);

        TrackEmptyRoom();
        TrackGameOver();
        TrackFinalResultFallback();
    }

    private void TrackEmptyRoom()
    {
        // 모든 인간 플레이어가 끊기면 reconnect grace window 동안만 process를 유지합니다.
        bool hasClients =
            InstanceFinder.ServerManager.Clients.Count > 0;

        if (hasClients)
        {
            emptySince = -1f;
            return;
        }

        if (emptySince < 0f)
        {
            emptySince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - emptySince >=
            Mathf.Max(1f, emptyRoomQuitDelaySeconds))
        {
            QuitRoomServer(
                "[Rooms] Room server empty timeout reached.");
        }
    }

    private void TrackGameOver()
    {
        // 최종 결과 UI와 네트워크 메시지가 정리될 시간을 준 뒤 process를 해제합니다.
        if (!GameRoundManager.HasInstance ||
            GameRoundManager.Instance.CurrentPhase != GamePhase.GameOver)
        {
            return;
        }

        if (gameOverSince < 0f)
        {
            NotifyGameOver();
            return;
        }

        if (Time.unscaledTime - gameOverSince >=
            Mathf.Max(1f, gameOverQuitDelaySeconds))
        {
            QuitRoomServer(
                "[Rooms] Room server game over timeout reached.");
        }
    }

    private void TrackFinalResultFallback()
    {
        // GameOver 전환 코드가 예외나 취소로 끝나더라도 room process가 영구히 남지 않게 합니다.
        if (!finalResultStarted ||
            finalResultSince < 0f ||
            gameOverSince >= 0f)
        {
            return;
        }

        float fallbackSeconds = Mathf.Max(
            gameOverQuitDelaySeconds + 5f,
            finalResultQuitFallbackSeconds);

        if (Time.unscaledTime - finalResultSince >= fallbackSeconds)
        {
            QuitRoomServer(
                "[Rooms] Room server final-result fallback timeout reached.");
        }
    }

    private static void QuitRoomServer(string reason)
    {
        Debug.Log(reason + " Quitting room server.");

        RoomRuntimeStatusStore.Delete(
            ServerLaunchMode.RoomId);

        if (InstanceFinder.ServerManager != null &&
            InstanceFinder.ServerManager.Started)
        {
            InstanceFinder.ServerManager.StopConnection(true);
        }

        Application.Quit(0);
    }

    private void OnApplicationQuit()
    {
        if (ServerLaunchMode.IsRoomServer)
        {
            RoomRuntimeStatusStore.Delete(
                ServerLaunchMode.RoomId);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void WriteHeartbeatIfDue(
        string state,
        int clientCount)
    {
        if (Time.realtimeSinceStartup < nextHeartbeatAt)
        {
            return;
        }

        WriteHeartbeat(state, clientCount);
    }

    private void WriteHeartbeat(
        string state,
        int clientCount)
    {
        // master의 RoomProcessLauncher가 이 작은 temp file을 읽어 멈춘 room process를 감지합니다.
        nextHeartbeatAt =
            Time.realtimeSinceStartup +
            Mathf.Max(0.25f, heartbeatIntervalSeconds);

        RoomRuntimeStatusStore.Write(
            ServerLaunchMode.RoomId,
            clientCount,
            state);
    }

    private string GetRuntimeState()
    {
        if (GameRoundManager.HasInstance &&
            GameRoundManager.Instance.CurrentPhase == GamePhase.GameOver)
        {
            return "GameOver";
        }

        // 최종 결과 시작 후 GameOver 전까지 CurrentPhase가 RoundResult여도
        // FinalResult heartbeat를 유지하여 master가 재접속을 허용하지 않게 합니다.
        if (finalResultStarted)
        {
            return "FinalResult";
        }

        if (!GameRoundManager.HasInstance)
        {
            return "lobby";
        }

        return GameRoundManager.Instance.CurrentPhase.ToString();
    }
}