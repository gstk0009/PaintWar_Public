using FishNet.Broadcast;
using UnityEngine;

public static class PlayerProfile
{
    private const string GuestUidKey = "PaintWar.GuestUid";
    private const string GuestNicknameKeyPrefix = "PaintWar.GuestNickname.";
    private const string NicknameConfirmedKeyPrefix = "PaintWar.NicknameConfirmed.";
    private const string FallbackNicknameKey = "PaintWar.GuestNickname.Local";
    private const string FallbackNicknameConfirmedKey = "PaintWar.NicknameConfirmed.Local";

    public static string UserId { get; private set; } = string.Empty;
    public static string Nickname { get; private set; } = string.Empty;
    public static bool IsNicknameConfirmed { get; private set; }

    public static void SetFirebaseUser(string userId, bool reuseSavedGuestProfile = false)
    {
        string previousUserId = SanitizeUserId(PlayerPrefs.GetString(GuestUidKey, string.Empty));
        UserId = SanitizeUserId(userId);
        CharacterAppearanceStore.ClearActiveUserCache();
        CharacterMotionStore.ClearActiveUserCache();
        string key = GetNicknameKey(UserId);
        string confirmedKey = GetNicknameConfirmedKey(UserId);

        string savedNickname = SanitizeNickname(PlayerPrefs.GetString(key, string.Empty));
        bool isConfirmed = PlayerPrefs.GetInt(confirmedKey, 0) == 1;
        if (reuseSavedGuestProfile && string.IsNullOrWhiteSpace(savedNickname))
        {
            savedNickname = GetSavedGuestNickname(previousUserId);
            isConfirmed = GetSavedGuestNicknameConfirmed(previousUserId);
        }

        string nickname = savedNickname;
        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = GenerateNickname();
        }

        Nickname = nickname;
        IsNicknameConfirmed = isConfirmed;
        PlayerPrefs.SetString(GuestUidKey, UserId);
        PlayerPrefs.SetString(key, Nickname);
        PlayerPrefs.SetInt(confirmedKey, IsNicknameConfirmed ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static string GetOrCreateNickname()
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
        {
            return Nickname;
        }

        UserId = SanitizeUserId(PlayerPrefs.GetString(GuestUidKey, string.Empty));
        string nickname = string.IsNullOrWhiteSpace(UserId)
            ? PlayerPrefs.GetString(FallbackNicknameKey, string.Empty)
            : PlayerPrefs.GetString(GetNicknameKey(UserId), string.Empty);

        nickname = SanitizeNickname(nickname);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = GenerateNickname();
        }

        Nickname = nickname;
        IsNicknameConfirmed = PlayerPrefs.GetInt(GetNicknameConfirmedKey(UserId), 0) == 1;
        PlayerPrefs.SetString(
            string.IsNullOrWhiteSpace(UserId) ? FallbackNicknameKey : GetNicknameKey(UserId),
            Nickname);
        PlayerPrefs.Save();
        return Nickname;
    }

    public static bool HasConfirmedNickname()
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
        {
            return IsNicknameConfirmed;
        }

        UserId = SanitizeUserId(PlayerPrefs.GetString(GuestUidKey, string.Empty));
        bool isConfirmed = PlayerPrefs.GetInt(GetNicknameConfirmedKey(UserId), 0) == 1;
        if (!isConfirmed)
        {
            return false;
        }

        string nickname = string.IsNullOrWhiteSpace(UserId)
            ? PlayerPrefs.GetString(FallbackNicknameKey, string.Empty)
            : PlayerPrefs.GetString(GetNicknameKey(UserId), string.Empty);

        Nickname = SanitizeNickname(nickname);
        IsNicknameConfirmed = !string.IsNullOrWhiteSpace(Nickname);
        return IsNicknameConfirmed;
    }

    public static bool TryConfirmNickname(string nickname)
    {
        nickname = SanitizeNickname(nickname);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return false;
        }

        Nickname = nickname;
        IsNicknameConfirmed = true;
        PlayerPrefs.SetString(
            string.IsNullOrWhiteSpace(UserId) ? FallbackNicknameKey : GetNicknameKey(UserId),
            Nickname);
        PlayerPrefs.SetInt(GetNicknameConfirmedKey(UserId), 1);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveCurrentProfile();
        return true;
    }

    public static void ApplyCloudProfile(string nickname, bool isConfirmed)
    {
        nickname = SanitizeNickname(nickname);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        Nickname = nickname;
        IsNicknameConfirmed = isConfirmed;
        PlayerPrefs.SetString(
            string.IsNullOrWhiteSpace(UserId) ? FallbackNicknameKey : GetNicknameKey(UserId),
            Nickname);
        PlayerPrefs.SetInt(GetNicknameConfirmedKey(UserId), IsNicknameConfirmed ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static string GetSavedUserId()
    {
        if (!string.IsNullOrWhiteSpace(UserId))
        {
            return UserId;
        }

        UserId = SanitizeUserId(PlayerPrefs.GetString(GuestUidKey, string.Empty));
        return UserId;
    }

    public static void ClearLocalGuestProfile()
    {
        string savedUserId = SanitizeUserId(PlayerPrefs.GetString(GuestUidKey, string.Empty));
        CharacterAppearanceStore.ClearLocalSessionCache();
        CharacterMotionStore.ClearLocalSessionCache();

        if (!string.IsNullOrWhiteSpace(savedUserId))
        {
            PlayerPrefs.DeleteKey(GetNicknameKey(savedUserId));
            PlayerPrefs.DeleteKey(GetNicknameConfirmedKey(savedUserId));
        }

        PlayerPrefs.DeleteKey(GuestUidKey);
        PlayerPrefs.DeleteKey(FallbackNicknameKey);
        PlayerPrefs.DeleteKey(FallbackNicknameConfirmedKey);
        PlayerPrefs.Save();

        UserId = string.Empty;
        Nickname = string.Empty;
        IsNicknameConfirmed = false;
    }

    public static void ClearRuntimeGuestProfile()
    {
        CharacterAppearanceStore.ClearActiveUserCache();
        CharacterMotionStore.ClearActiveUserCache();

        UserId = string.Empty;
        Nickname = string.Empty;
        IsNicknameConfirmed = false;
    }

    public static string SanitizeNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return string.Empty;
        }

        nickname = nickname.Trim();
        if (nickname.Length > 20)
        {
            nickname = nickname.Substring(0, 20);
        }

        return nickname;
    }

    private static string GenerateNickname()
    {
        int number = Random.Range(0, 10000);
        return "guest" + number.ToString("0000");
    }

    private static string GetNicknameKey(string userId)
    {
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackNicknameKey
            : GuestNicknameKeyPrefix + userId;
    }

    private static string GetNicknameConfirmedKey(string userId)
    {
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackNicknameConfirmedKey
            : NicknameConfirmedKeyPrefix + userId;
    }

    private static string GetSavedGuestNickname(string previousUserId)
    {
        string nickname = string.Empty;
        if (!string.IsNullOrWhiteSpace(previousUserId))
        {
            nickname = PlayerPrefs.GetString(GetNicknameKey(previousUserId), string.Empty);
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = PlayerPrefs.GetString(FallbackNicknameKey, string.Empty);
        }

        return SanitizeNickname(nickname);
    }

    private static bool GetSavedGuestNicknameConfirmed(string previousUserId)
    {
        if (!string.IsNullOrWhiteSpace(previousUserId) &&
            PlayerPrefs.GetInt(GetNicknameConfirmedKey(previousUserId), 0) == 1)
        {
            return true;
        }

        return PlayerPrefs.GetInt(FallbackNicknameConfirmedKey, 0) == 1;
    }

    private static string SanitizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }
}

public struct PlayerProfileBroadcast : IBroadcast
{
    public string UserId;
    public string Nickname;
    public string AppearanceCode;
    public string MotionCode;
    public string SelectedSkillId;
}

public struct RoomAssignmentBroadcast : IBroadcast
{
    public string RoomId;
    public string Address;
    public ushort Port;
    public float ReconnectGraceSeconds;
}

public struct MatchmakingQueueStateBroadcast : IBroadcast
{
    public bool WaitingForNextMatch;
}
