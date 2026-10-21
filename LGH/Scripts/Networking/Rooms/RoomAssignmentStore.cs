using UnityEngine;

public struct RoomConnectionTarget
{
    // Room id는 status/heartbeat 파일에 쓰이고, address와 port는 transport endpoint입니다.
    public string RoomId;
    public string Address;
    public ushort Port;
    public float ReconnectGraceSeconds;
}

// 마지막으로 배정된 room을 저장하는 클라이언트 측 PlayerPrefs 저장소입니다.
// 짧은 모바일 앱 재시작 후 matchmaking 전에 room 재접속을 시도할 수 있게 합니다.
public static class RoomAssignmentStore
{
    private const string RoomIdKey = "PaintWar.RoomAssignment.RoomId";
    private const string AddressKey = "PaintWar.RoomAssignment.Address";
    private const string PortKey = "PaintWar.RoomAssignment.Port";
    private const string SavedAtKey = "PaintWar.RoomAssignment.SavedAt";
    private const string UserIdKey = "PaintWar.RoomAssignment.UserId";
    private const string GraceKey = "PaintWar.RoomAssignment.GraceSeconds";

    public static void Save(RoomConnectionTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.RoomId) ||
            string.IsNullOrWhiteSpace(target.Address) ||
            target.Port == 0)
        {
            return;
        }

        PlayerPrefs.SetString(RoomIdKey, target.RoomId.Trim());
        PlayerPrefs.SetString(AddressKey, target.Address.Trim());
        PlayerPrefs.SetInt(PortKey, target.Port);
        PlayerPrefs.SetFloat(SavedAtKey, Time.realtimeSinceStartup);
        PlayerPrefs.SetString(UserIdKey, PlayerProfile.GetSavedUserId());
        PlayerPrefs.SetFloat(GraceKey, Mathf.Max(0f, target.ReconnectGraceSeconds));
        PlayerPrefs.Save();
        Debug.Log(
            "[Rooms] Saved room assignment. room=" + target.RoomId +
            " port=" + target.Port +
            " uidSet=" + !string.IsNullOrWhiteSpace(PlayerProfile.GetSavedUserId()) + ".");
    }

    public static bool TryLoad(out RoomConnectionTarget target)
    {
        target = default;

        // 저장된 room assignment는 현재 인증/guest user에 종속됩니다.
        string userId = PlayerProfile.GetSavedUserId();
        string savedUserId = PlayerPrefs.GetString(UserIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(savedUserId) &&
            !string.IsNullOrWhiteSpace(userId) &&
            savedUserId != userId)
        {
            Debug.LogWarning("[Rooms] Saved room assignment belongs to another user. Clearing it.");
            Clear();
            return false;
        }

        string roomId = PlayerPrefs.GetString(RoomIdKey, string.Empty);
        string address = PlayerPrefs.GetString(AddressKey, string.Empty);
        int port = PlayerPrefs.GetInt(PortKey, 0);
        float savedAt = PlayerPrefs.GetFloat(SavedAtKey, -1f);
        float grace = PlayerPrefs.GetFloat(GraceKey, 180f);

        if (string.IsNullOrWhiteSpace(roomId) ||
            string.IsNullOrWhiteSpace(address) ||
            port <= 0 ||
            savedAt < 0f)
        {
            Debug.Log(
                "[Rooms] No saved room assignment. " +
                "roomSet=" + !string.IsNullOrWhiteSpace(roomId) +
                " addressSet=" + !string.IsNullOrWhiteSpace(address) +
                " port=" + port +
                " savedAt=" + savedAt.ToString("0.##") + ".");
            return false;
        }

        // assignment는 짧은 app session 동안만 유효하므로 여기서는 local realtime으로 충분합니다.
        float age = Time.realtimeSinceStartup - savedAt;
        if (age > Mathf.Max(30f, grace))
        {
            Debug.Log(
                "[Rooms] Saved room assignment expired. age=" +
                age.ToString("0.##") + " grace=" + grace.ToString("0.##") + ".");
            Clear();
            return false;
        }

        target = new RoomConnectionTarget
        {
            RoomId = roomId,
            Address = address,
            Port = (ushort)port,
            ReconnectGraceSeconds = grace
        };
        return true;
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(RoomIdKey);
        PlayerPrefs.DeleteKey(AddressKey);
        PlayerPrefs.DeleteKey(PortKey);
        PlayerPrefs.DeleteKey(SavedAtKey);
        PlayerPrefs.DeleteKey(UserIdKey);
        PlayerPrefs.DeleteKey(GraceKey);
        PlayerPrefs.Save();
        Debug.Log("[Rooms] Cleared saved room assignment.");
    }
}
