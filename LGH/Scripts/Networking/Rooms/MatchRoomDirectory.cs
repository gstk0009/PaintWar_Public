using System;
using System.Collections.Generic;
using UnityEngine;

public enum MatchRoomStatus
{
    Waiting,
    Loading,
    InGame,
    Ending,
    Ended
}

public sealed class MatchRoomMember
{
    // UserId는 재접속 후에도 유지되는 값이고, ClientId는 현재 FishNet 연결 id입니다.
    public string UserId;
    public int ClientId;
    public int SlotIndex;
    public bool IsAi;
    public bool IsConnected;
    public float DisconnectedAt;
}

public sealed class MatchRoomSession
{
    private readonly List<MatchRoomMember> members = new List<MatchRoomMember>();

    public MatchRoomSession(string roomId, string sceneName, int maxPlayers)
    {
        RoomId = roomId;
        SceneName = sceneName;
        MaxPlayers = Mathf.Max(1, maxPlayers);
        Status = MatchRoomStatus.Waiting;
        CreatedAt = Time.unscaledTime;
        LastEmptyAt = -1f;
    }

    public string RoomId { get; private set; }
    public string SceneName { get; private set; }
    public int MaxPlayers { get; private set; }
    public int SceneHandle { get; private set; } = -1;
    public MatchRoomStatus Status { get; private set; }
    public float CreatedAt { get; private set; }
    public float StartedAt { get; private set; } = -1f;
    public float LastEmptyAt { get; private set; }
    public IReadOnlyList<MatchRoomMember> Members { get { return members; } }
    public bool IsFull { get { return members.Count >= MaxPlayers; } }

    public void SetSceneHandle(int sceneHandle)
    {
        SceneHandle = sceneHandle;
    }

    public void SetStatus(MatchRoomStatus status)
    {
        // StartedAt은 room이 처음 gameplay에 들어간 순간 한 번만 기록됩니다.
        Status = status;
        if (status == MatchRoomStatus.InGame && StartedAt < 0f)
        {
            StartedAt = Time.unscaledTime;
        }
    }

    public bool TryAddMember(string userId, int clientId, int slotIndex, bool isAi, out MatchRoomMember member)
    {
        member = null;
        if (IsFull)
        {
            return false;
        }

        member = new MatchRoomMember
        {
            UserId = NormalizeUserId(userId),
            ClientId = clientId,
            SlotIndex = slotIndex,
            IsAi = isAi,
            IsConnected = !isAi,
            DisconnectedAt = -1f
        };
        members.Add(member);
        LastEmptyAt = -1f;
        return true;
    }

    public bool TryFindMemberByUserId(string userId, out MatchRoomMember member)
    {
        userId = NormalizeUserId(userId);
        for (int i = 0; i < members.Count; i++)
        {
            MatchRoomMember candidate = members[i];
            if (candidate != null && candidate.UserId == userId)
            {
                member = candidate;
                return true;
            }
        }

        member = null;
        return false;
    }

    public bool MarkDisconnected(int clientId)
    {
        for (int i = 0; i < members.Count; i++)
        {
            MatchRoomMember member = members[i];
            if (member == null || member.ClientId != clientId)
            {
                continue;
            }

            member.IsConnected = false;
            member.DisconnectedAt = Time.unscaledTime;
            UpdateEmptyTimestamp();
            return true;
        }

        return false;
    }

    public void MarkReconnected(MatchRoomMember member, int clientId)
    {
        if (member == null)
        {
            return;
        }

        member.ClientId = clientId;
        member.IsConnected = true;
        member.DisconnectedAt = -1f;
        LastEmptyAt = -1f;
    }

    public bool HasConnectedHuman()
    {
        for (int i = 0; i < members.Count; i++)
        {
            MatchRoomMember member = members[i];
            if (member != null && !member.IsAi && member.IsConnected)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateEmptyTimestamp()
    {
        LastEmptyAt = HasConnectedHuman() ? -1f : Time.unscaledTime;
    }

    private static string NormalizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }
}

public sealed class MatchRoomDirectory : MonoBehaviour
{
    public static MatchRoomDirectory Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    [Header("Capacity")]
    [SerializeField] private int maxActiveRooms = 8;
    [SerializeField] private int maxPlayersPerRoom = 4;
    [SerializeField] private int maxQueuedPlayers = 64;

    [Header("Cleanup")]
    [SerializeField] private float reconnectGraceSeconds = 180f;
    [SerializeField] private float emptyRoomUnloadDelaySeconds = 30f;
    [SerializeField] private float endedRoomUnloadDelaySeconds = 10f;

    private readonly Dictionary<string, MatchRoomSession> roomsById =
        new Dictionary<string, MatchRoomSession>();
    // user id로 재접속 대상 room id를 찾기 위한 index입니다.
    private readonly Dictionary<string, string> roomIdByUserId =
        new Dictionary<string, string>();
    private int nextRoomNumber = 1;

    public int MaxActiveRooms { get { return Mathf.Max(1, maxActiveRooms); } }
    public int MaxPlayersPerRoom { get { return Mathf.Max(1, maxPlayersPerRoom); } }
    public int MaxQueuedPlayers { get { return Mathf.Max(0, maxQueuedPlayers); } }
    public float ReconnectGraceSeconds { get { return Mathf.Max(0f, reconnectGraceSeconds); } }
    public float EmptyRoomUnloadDelaySeconds { get { return Mathf.Max(0f, emptyRoomUnloadDelaySeconds); } }
    public float EndedRoomUnloadDelaySeconds { get { return Mathf.Max(0f, endedRoomUnloadDelaySeconds); } }
    public int ActiveRoomCount { get { return roomsById.Count; } }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool CanCreateRoom()
    {
        return roomsById.Count < MaxActiveRooms;
    }

    public bool TryCreateRoom(string sceneName, out MatchRoomSession room)
    {
        room = null;
        if (!CanCreateRoom() || string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        string roomId = "room-" + nextRoomNumber.ToString("0000");
        nextRoomNumber++;
        room = new MatchRoomSession(roomId, sceneName, MaxPlayersPerRoom);
        roomsById.Add(roomId, room);
        return true;
    }

    public bool TryFindReconnectRoom(string userId, out MatchRoomSession room, out MatchRoomMember member)
    {
        // A disconnected member can reclaim the same room only during the reconnect grace window.
        room = null;
        member = null;
        userId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(userId) ||
            !roomIdByUserId.TryGetValue(userId, out string roomId) ||
            !roomsById.TryGetValue(roomId, out room))
        {
            return false;
        }

        if (!room.TryFindMemberByUserId(userId, out member))
        {
            room = null;
            return false;
        }

        if (member.IsConnected)
        {
            return true;
        }

        return member.DisconnectedAt < 0f ||
               Time.unscaledTime - member.DisconnectedAt <= ReconnectGraceSeconds;
    }

    public void RegisterMember(MatchRoomSession room, MatchRoomMember member)
    {
        if (room == null || member == null || string.IsNullOrWhiteSpace(member.UserId))
        {
            return;
        }

        roomIdByUserId[member.UserId] = room.RoomId;
    }

    public void MarkDisconnected(int clientId)
    {
        foreach (MatchRoomSession room in roomsById.Values)
        {
            if (room.MarkDisconnected(clientId))
            {
                return;
            }
        }
    }

    public void RemoveRoom(string roomId)
    {
        roomId = NormalizeUserId(roomId);
        if (!roomsById.TryGetValue(roomId, out MatchRoomSession room))
        {
            return;
        }

        for (int i = 0; i < room.Members.Count; i++)
        {
            MatchRoomMember member = room.Members[i];
            if (member != null && !string.IsNullOrWhiteSpace(member.UserId))
            {
                roomIdByUserId.Remove(member.UserId);
            }
        }

        roomsById.Remove(roomId);
    }

    private static string NormalizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }
}
