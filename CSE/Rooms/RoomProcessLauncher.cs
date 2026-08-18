using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 별도 room-server process를 실행, 추적, 정리하는 master-server 컴포넌트입니다.
/// 하나의 room process는 하나의 match를 담당하고, master는 재접속용 user-room 배정을 보관합니다.
/// </summary>
public sealed class RoomProcessLauncher : MonoBehaviour
{
    [SerializeField] private int maxActiveRooms = 2;
    // room process는 고정 port 범위에 bind해서 방화벽/security-list 규칙을 단순하게 유지합니다.
    [SerializeField] private ushort firstRoomPort = ServerLaunchMode.FirstRoomPort;
    [SerializeField] private ushort lastRoomPort = ServerLaunchMode.LastRoomPort;
    [SerializeField] private string publicAddress = "158.180.89.56";
    [SerializeField] private float reconnectGraceSeconds = 180f;
    [SerializeField] private float roomMatchTimeoutSeconds = 5f;
    [SerializeField] private float cleanupIntervalSeconds = 1f;
    [SerializeField] private float roomStartupTimeoutSeconds = 45f;
    [SerializeField] private float roomHeartbeatTimeoutSeconds = 10f;
    [SerializeField] private float roomGameOverCleanupSeconds = 25f;
    [SerializeField] private float roomFinalResultCleanupSeconds = 60f;
    [SerializeField] private int masterMemoryReserveMb = 384;
    [SerializeField] private int estimatedRoomMemoryMb = 256;
    [SerializeField] private int minAvailableMemoryToLaunchMb = 300;
    [SerializeField, Min(5f)] private float memoryDiagnosticsIntervalSeconds = 30f;

    // 런타임 lookup table은 master process 메모리에 보관됩니다.
    private readonly Dictionary<string, LaunchedRoom> roomsById =
        new Dictionary<string, LaunchedRoom>();
    private readonly Dictionary<ushort, string> roomIdByPort =
        new Dictionary<ushort, string>();
    // 재접속 클라이언트를 같은 room으로 보낼 수 있도록 user id 기준으로 저장합니다.
    private readonly Dictionary<string, RoomConnectionTarget> assignmentByUserId =
        new Dictionary<string, RoomConnectionTarget>();
    private int nextRoomNumber = 1;
    private float nextCleanupAt;
    private float nextMemoryDiagnosticsAt;
    private bool isShuttingDown;

    public float ReconnectGraceSeconds { get { return Mathf.Max(30f, reconnectGraceSeconds); } }

    private void Awake()
    {
        if (Application.isBatchMode && ServerLaunchMode.IsMasterServer)
        {
            CleanupOrphanedRoomProcesses();
            CleanupStaleRoomFiles();
        }
    }

    private void Update()
    {
        if (!Application.isBatchMode ||
            !ServerLaunchMode.IsMasterServer ||
            Time.realtimeSinceStartup < nextCleanupAt)
        {
            return;
        }

        nextCleanupAt = Time.realtimeSinceStartup + Mathf.Max(0.25f, cleanupIntervalSeconds);
        CleanupExitedRooms();
        if (Time.realtimeSinceStartup >= nextMemoryDiagnosticsAt)
        {
            nextMemoryDiagnosticsAt = Time.realtimeSinceStartup +
                Mathf.Max(5f, memoryDiagnosticsIntervalSeconds);
            LogMemoryDiagnostics();
        }
    }

    public bool TryGetReconnectTarget(string userId, out RoomConnectionTarget target)
    {
        CleanupExitedRooms();
        userId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(userId) ||
            !assignmentByUserId.TryGetValue(userId, out target))
        {
            target = default;
            return false;
        }

        if (!roomsById.ContainsKey(target.RoomId))
        {
            assignmentByUserId.Remove(userId);
            target = default;
            return false;
        }

        // 결과 화면을 보여 주기 위해 room process가 잠시 살아 있어도 새 매칭에서 재사용하지 않습니다.
        if (RoomRuntimeStatusStore.TryRead(target.RoomId, out RoomRuntimeStatus status) &&
            IsTerminalRoomState(status.State))
        {
            assignmentByUserId.Remove(userId);
            target = default;
            return false;
        }

        return true;
    }

    public static bool IsTerminalRoomState(string state)
    {
        return string.Equals(state, "FinalResult", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "GameOver", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "Stopping", StringComparison.OrdinalIgnoreCase);
    }

    public bool TryLaunchRoom(out RoomConnectionTarget target)
    {
        CleanupExitedRooms();
        CleanupEmptyRoomsForCapacity();
        target = default;

        // 수용량은 Inspector 설정과 process에서 보이는 메모리 예산 둘 다로 제한됩니다.
        int effectiveMaxRooms = GetEffectiveMaxActiveRooms();
        if (roomsById.Count >= effectiveMaxRooms)
        {
            UnityEngine.Debug.LogWarning(
                "[Rooms] Room capacity reached. active=" + roomsById.Count +
                " effectiveMax=" + effectiveMaxRooms + ".");
            return false;
        }

        long requiredAvailableMemoryMb = Mathf.Max(64, minAvailableMemoryToLaunchMb);
        if (TryGetAvailableMemoryMb(out long availableMemoryMb) &&
            availableMemoryMb < requiredAvailableMemoryMb)
        {
            UnityEngine.Debug.LogWarning(
                "[Rooms] Insufficient free memory to launch a room. availableMb=" +
                availableMemoryMb + " requiredBeforeLaunchMb=" +
                requiredAvailableMemoryMb + ".");
            return false;
        }

        if (!TryFindFreePort(out ushort port))
        {
            UnityEngine.Debug.LogWarning("[Rooms] No free room port is available.");
            return false;
        }

        string executablePath = GetCurrentExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            UnityEngine.Debug.LogError("[Rooms] Cannot find current server executable.");
            return false;
        }

        string roomId = "room-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") +
                        "-" + nextRoomNumber.ToString("000");
        nextRoomNumber++;

        string address = !string.IsNullOrWhiteSpace(ServerLaunchMode.PublicAddress)
            ? ServerLaunchMode.PublicAddress
            : publicAddress;

        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            Arguments =
                "-batchmode -nographics " +
                "-paintwarServerRole room " +
                "-paintwarRoomId " + Quote(roomId) + " " +
                "-paintwarRoomPort " + port + " " +
                "-paintwarPublicAddress " + Quote(address) + " " +
                "-paintwarRoomMatchTimeout " + Mathf.Max(1f, roomMatchTimeoutSeconds).ToString("0.##"),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // child process를 시작하기 전에 오래된 파일을 지웁니다. room은 Process.Start 직후
        // 첫 heartbeat를 바로 기록할 수 있습니다.
        RoomRuntimeStatusStore.Delete(roomId);

        Process process = Process.Start(info);
        if (process == null)
        {
            UnityEngine.Debug.LogError("[Rooms] Room process did not start.");
            return false;
        }

        target = new RoomConnectionTarget
        {
            RoomId = roomId,
            Address = address,
            Port = port,
            ReconnectGraceSeconds = ReconnectGraceSeconds
        };

        roomsById[roomId] = new LaunchedRoom
        {
            RoomId = roomId,
            Port = port,
            Process = process,
            LaunchedUtc = DateTime.UtcNow
        };
        roomIdByPort[port] = roomId;

        UnityEngine.Debug.Log(
            "[Rooms] Launched room " + roomId +
            " pid=" + process.Id +
            " port=" + port + ".");
        return true;
    }

    public void RememberAssignment(string userId, RoomConnectionTarget target)
    {
        userId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(target.RoomId))
        {
            return;
        }

        // 최종 결과가 시작된 방은 늦게 도착한 assignment 요청으로도 다시 저장하지 않습니다.
        if (RoomRuntimeStatusStore.TryRead(
                target.RoomId,
                out RoomRuntimeStatus status) &&
            IsTerminalRoomState(status.State))
        {
            assignmentByUserId.Remove(userId);
            return;
        }

        assignmentByUserId[userId] = target;
    }

    private bool TryFindFreePort(out ushort port)
    {
        ushort start = Math.Min(firstRoomPort, lastRoomPort);
        ushort end = Math.Max(firstRoomPort, lastRoomPort);
        for (ushort candidate = start; candidate <= end; candidate++)
        {
            if (!roomIdByPort.ContainsKey(candidate))
            {
                port = candidate;
                return true;
            }
        }

        port = 0;
        return false;
    }

    private void CleanupExitedRooms()
    {
        List<string> endedRooms = null;
        List<string> unhealthyRooms = null;
        List<string> finishedRooms = null;
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, LaunchedRoom> pair in roomsById)
        {
            Process process = pair.Value.Process;
            if (process == null || process.HasExited)
            {
                if (endedRooms == null)
                {
                    endedRooms = new List<string>();
                }

                endedRooms.Add(pair.Key);
                continue;
            }

            // A healthy room writes heartbeat files; missing or stale heartbeats are treated as hung processes.
            bool readHeartbeat = RoomRuntimeStatusStore.TryRead(
                pair.Key,
                out RoomRuntimeStatus runtimeStatus);
            if (readHeartbeat)
            {
                pair.Value.HasHeartbeat = true;
                pair.Value.LastHeartbeatUtc = runtimeStatus.HeartbeatUtc;

                // FinalResult부터는 process를 유지하더라도 기존 user assignment를 즉시 제거합니다.
                if (IsTerminalRoomState(runtimeStatus.State))
                {
                    ClearAssignmentsForRoom(pair.Key);
                }

                if (string.Equals(
                        runtimeStatus.State,
                        "FinalResult",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!pair.Value.FinalResultObservedUtc.HasValue)
                    {
                        pair.Value.FinalResultObservedUtc = now;
                    }

                    if ((now - pair.Value.FinalResultObservedUtc.Value).TotalSeconds >=
                        Mathf.Max(10f, roomFinalResultCleanupSeconds))
                    {
                        if (finishedRooms == null)
                        {
                            finishedRooms = new List<string>();
                        }

                        finishedRooms.Add(pair.Key);
                    }
                }

                if (string.Equals(runtimeStatus.State, "GameOver", StringComparison.OrdinalIgnoreCase))
                {
                    if (!pair.Value.GameOverObservedUtc.HasValue)
                        pair.Value.GameOverObservedUtc = now;
                    if ((now - pair.Value.GameOverObservedUtc.Value).TotalSeconds >=
                        Mathf.Max(5f, roomGameOverCleanupSeconds))
                    {
                        if (finishedRooms == null) finishedRooms = new List<string>();
                        finishedRooms.Add(pair.Key);
                    }
                }
            }

            bool startupTimedOut = !pair.Value.HasHeartbeat &&
                (now - pair.Value.LaunchedUtc).TotalSeconds >=
                Mathf.Max(5f, roomStartupTimeoutSeconds);
            bool heartbeatTimedOut = pair.Value.HasHeartbeat &&
                (now - pair.Value.LastHeartbeatUtc).TotalSeconds >=
                Mathf.Max(2f, roomHeartbeatTimeoutSeconds);

            if (startupTimedOut || heartbeatTimedOut)
            {
                if (unhealthyRooms == null)
                {
                    unhealthyRooms = new List<string>();
                }

                unhealthyRooms.Add(pair.Key);
            }
        }

        if (endedRooms != null)
        {
            for (int i = 0; i < endedRooms.Count; i++)
            {
                RemoveRoom(endedRooms[i]);
            }
        }

        if (unhealthyRooms != null)
        {
            for (int i = 0; i < unhealthyRooms.Count; i++)
            {
                TerminateRoom(unhealthyRooms[i], "heartbeat timeout");
            }
        }
        if (finishedRooms != null)
        {
            for (int i = 0; i < finishedRooms.Count; i++)
                TerminateRoom(finishedRooms[i], "terminal-state cleanup fallback");
        }
    }

    private void CleanupEmptyRoomsForCapacity()
    {
        if (roomsById.Count == 0)
        {
            return;
        }

        int effectiveMaxRooms = GetEffectiveMaxActiveRooms();
        if (roomsById.Count < effectiveMaxRooms)
        {
            return;
        }

        List<string> emptyRooms = null;
        foreach (KeyValuePair<string, LaunchedRoom> pair in roomsById)
        {
            Process process = pair.Value.Process;
            if (process == null || process.HasExited)
            {
                continue;
            }

            if (!RoomRuntimeStatusStore.TryRead(
                    pair.Key,
                    out RoomRuntimeStatus runtimeStatus))
            {
                continue;
            }

            if (runtimeStatus.ClientCount > 0 ||
                string.Equals(runtimeStatus.State, "starting", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (emptyRooms == null)
            {
                emptyRooms = new List<string>();
            }

            emptyRooms.Add(pair.Key);
        }

        if (emptyRooms == null)
        {
            return;
        }

        for (int i = 0; i < emptyRooms.Count; i++)
        {
            TerminateRoom(emptyRooms[i], "empty room capacity reclaim");
        }
    }

    private void TerminateRoom(string roomId, string reason)
    {
        if (!roomsById.TryGetValue(roomId, out LaunchedRoom room))
        {
            return;
        }

        try
        {
            if (room.Process != null && !room.Process.HasExited)
            {
                room.Process.Kill();
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[Rooms] Failed to terminate " + roomId + ": " + exception.Message);
        }

        UnityEngine.Debug.LogWarning(
            "[Rooms] Terminated unhealthy room " + roomId + " (" + reason + ").");
        RemoveRoom(roomId);
    }

    private void RemoveRoom(string roomId)
    {
        if (!roomsById.TryGetValue(roomId, out LaunchedRoom room))
        {
            return;
        }

        roomsById.Remove(roomId);
        roomIdByPort.Remove(room.Port);
        RoomRuntimeStatusStore.Delete(roomId);

        try
        {
            room.Process?.Dispose();
        }
        catch (Exception)
        {
        }

        ClearAssignmentsForRoom(roomId);

        UnityEngine.Debug.Log("[Rooms] Room " + roomId + " released.");
    }

    private void ClearAssignmentsForRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId) ||
            assignmentByUserId.Count == 0)
        {
            return;
        }

        List<string> usersToClear = null;
        foreach (KeyValuePair<string, RoomConnectionTarget> pair in assignmentByUserId)
        {
            if (!string.Equals(
                    pair.Value.RoomId,
                    roomId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (usersToClear == null)
            {
                usersToClear = new List<string>();
            }

            usersToClear.Add(pair.Key);
        }

        if (usersToClear == null)
        {
            return;
        }

        for (int i = 0; i < usersToClear.Count; i++)
        {
            assignmentByUserId.Remove(usersToClear[i]);
        }
    }

    private int GetEffectiveMaxActiveRooms()
    {
        // master와 OS용 메모리 여유를 남긴 뒤 실행 가능한 room process 수를 계산합니다.
        int configuredMaximum = Mathf.Max(1, maxActiveRooms);
        int totalMemoryMb = SystemInfo.systemMemorySize;
        if (totalMemoryMb <= 0)
        {
            return configuredMaximum;
        }

        int roomBudgetMb = Mathf.Max(0, totalMemoryMb - Mathf.Max(0, masterMemoryReserveMb));
        int memoryMaximum = Mathf.Max(
            1,
            roomBudgetMb / Mathf.Max(64, estimatedRoomMemoryMb));
        return Mathf.Min(configuredMaximum, memoryMaximum);
    }

    private static bool TryGetAvailableMemoryMb(out long availableMemoryMb)
    {
        availableMemoryMb = 0;
        const string memInfoPath = "/proc/meminfo";
        if (!File.Exists(memInfoPath))
        {
            return false;
        }

        try
        {
            foreach (string line in File.ReadLines(memInfoPath))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    continue;
                }

                string value = line.Substring("MemAvailable:".Length).Trim();
                string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && long.TryParse(parts[0], out long availableKb))
                {
                    availableMemoryMb = availableKb / 1024L;
                    return true;
                }

                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private void LogMemoryDiagnostics()
    {
        StringBuilder message = new StringBuilder(160);
        message.Append("[Rooms] Memory activeRooms=").Append(roomsById.Count);
        if (TryGetAvailableMemoryMb(out long availableMemoryMb))
        {
            message.Append(" availableMb=").Append(availableMemoryMb);
        }

        foreach (KeyValuePair<string, LaunchedRoom> pair in roomsById)
        {
            Process process = pair.Value.Process;
            if (process == null || process.HasExited)
            {
                continue;
            }

            try
            {
                process.Refresh();
                message.Append(' ').Append(pair.Key).Append("RssMb=")
                    .Append(process.WorkingSet64 / (1024L * 1024L));
            }
            catch (Exception)
            {
                message.Append(' ').Append(pair.Key).Append("RssMb=unavailable");
            }
        }

        UnityEngine.Debug.Log(message.ToString());
    }

    private void OnApplicationQuit()
    {
        StopAllRooms();
    }

    private void OnDestroy()
    {
        if (Application.isBatchMode && ServerLaunchMode.IsMasterServer)
        {
            StopAllRooms();
        }
    }

    private void StopAllRooms()
    {
        if (isShuttingDown)
        {
            return;
        }

        isShuttingDown = true;
        List<string> roomIds = new List<string>(roomsById.Keys);
        for (int i = 0; i < roomIds.Count; i++)
        {
            TerminateRoom(roomIds[i], "master server shutdown");
        }
    }

    private static void CleanupOrphanedRoomProcesses()
    {
        // dedicated server 재시작 뒤 이전 room child가 남을 수 있어 PaintWar room-role process만 종료합니다.
        const string procPath = "/proc";
        if (!Directory.Exists(procPath))
        {
            return;
        }

        int currentProcessId = Process.GetCurrentProcess().Id;
        int terminatedCount = 0;
        foreach (string processDirectory in Directory.GetDirectories(procPath))
        {
            string processName = Path.GetFileName(processDirectory);
            if (!int.TryParse(processName, out int processId) ||
                processId == currentProcessId)
            {
                continue;
            }

            try
            {
                string commandLinePath = Path.Combine(processDirectory, "cmdline");
                if (!File.Exists(commandLinePath))
                {
                    continue;
                }

                string commandLine = File.ReadAllText(commandLinePath).Replace('\0', ' ');
                if (!commandLine.Contains(
                    "-paintwarServerRole room",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                using (Process orphan = Process.GetProcessById(processId))
                {
                    if (!orphan.HasExited)
                    {
                        orphan.Kill();
                        terminatedCount++;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        if (terminatedCount > 0)
        {
            UnityEngine.Debug.LogWarning(
                "[Rooms] Terminated " + terminatedCount +
                " orphaned room process(es) during master startup.");
        }
    }

    private static void CleanupStaleRoomFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PaintWarRooms");
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            string[] runtimeFiles = Directory.GetFiles(directory, "*.runtime");
            string[] statusFiles = Directory.GetFiles(directory, "*.status");
            for (int i = 0; i < runtimeFiles.Length; i++)
            {
                File.Delete(runtimeFiles[i]);
            }

            for (int i = 0; i < statusFiles.Length; i++)
            {
                File.Delete(statusFiles[i]);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[Rooms] Failed to clear stale room files: " + exception.Message);
        }
    }

    private static string GetCurrentExecutablePath()
    {
        using (Process process = Process.GetCurrentProcess())
        {
            return process.MainModule != null ? process.MainModule.FileName : string.Empty;
        }
    }

    private static string NormalizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private sealed class LaunchedRoom
    {
        // master cleanup loop가 추적하는 process metadata와 heartbeat 상태입니다.
        public string RoomId;
        public ushort Port;
        public Process Process;
        public DateTime LaunchedUtc;
        public bool HasHeartbeat;
        public DateTime LastHeartbeatUtc;
        public DateTime? FinalResultObservedUtc;
        public DateTime? GameOverObservedUtc;
    }
}
