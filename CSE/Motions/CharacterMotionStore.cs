using System;
using UnityEngine;

public static class CharacterMotionStore
{
    private const string EquippedIdleMotionKeyPrefix = "PaintWar.Motion.Idle.";
    private const string FallbackEquippedIdleMotionKey = "PaintWar.Motion.Idle.Local";
    private const string EquippedVictoryMotionKeyPrefix = "PaintWar.Motion.Victory.";
    private const string FallbackEquippedVictoryMotionKey = "PaintWar.Motion.Victory.Local";
    private const char ProfileMotionSeparator = '|';

    public static event Action<string> EquippedChanged;
    public static event Action<string> VictoryEquippedChanged;

    public static string CurrentIdleMotionId
    {
        get
        {
            return NormalizeIdleMotionId(
                PlayerPrefs.GetString(GetEquippedIdleMotionKey(), string.Empty));
        }
    }

    public static string CurrentVictoryMotionId
    {
        get
        {
            return NormalizeVictoryMotionId(
                PlayerPrefs.GetString(GetEquippedVictoryMotionKey(), string.Empty));
        }
    }

    public static bool SaveIdleMotion(string motionId, LobbyMotionCatalog catalog = null)
    {
        string normalizedId = NormalizeIdleMotionId(motionId, catalog);
        string key = GetEquippedIdleMotionKey();
        if (string.Equals(
                PlayerPrefs.GetString(key, string.Empty),
                normalizedId,
                StringComparison.Ordinal))
        {
            EquippedChanged?.Invoke(normalizedId);
            SendCurrentProfileToServer();
            return true;
        }

        PlayerPrefs.SetString(key, normalizedId);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveMotionCode(GetCurrentProfileMotionCode());
        EquippedChanged?.Invoke(normalizedId);
        SendCurrentProfileToServer();
        return true;
    }

    public static bool SaveVictoryMotion(
        string motionId,
        LobbyMotionCatalog catalog = null)
    {
        string normalizedId = NormalizeVictoryMotionId(motionId, catalog);
        string key = GetEquippedVictoryMotionKey();
        if (string.Equals(
                PlayerPrefs.GetString(key, string.Empty),
                normalizedId,
                StringComparison.Ordinal))
        {
            VictoryEquippedChanged?.Invoke(normalizedId);
            SendCurrentProfileToServer();
            return true;
        }

        PlayerPrefs.SetString(key, normalizedId);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveMotionCode(GetCurrentProfileMotionCode());
        VictoryEquippedChanged?.Invoke(normalizedId);
        SendCurrentProfileToServer();
        return true;
    }

    public static void SetEquipped(string motionId)
    {
        SetEquippedProfile(motionId);
    }

    public static void SetEquippedProfile(string motionCode)
    {
        ParseProfileMotionCode(
            motionCode,
            out string idleMotionId,
            out string victoryMotionId);

        SetEquippedSlot(
            idleMotionId,
            LobbyMotionCatalog.MotionSlot.Idle,
            GetEquippedIdleMotionKey(),
            EquippedChanged);
        SetEquippedSlot(
            victoryMotionId,
            LobbyMotionCatalog.MotionSlot.Victory,
            GetEquippedVictoryMotionKey(),
            VictoryEquippedChanged);
    }

    private static void SetEquippedSlot(
        string motionId,
        LobbyMotionCatalog.MotionSlot slot,
        string key,
        Action<string> changed)
    {
        string normalizedId = NormalizeMotionId(motionId, slot);
        if (string.Equals(
                PlayerPrefs.GetString(key, string.Empty),
                normalizedId,
                StringComparison.Ordinal))
        {
            return;
        }

        PlayerPrefs.SetString(key, normalizedId);
        PlayerPrefs.Save();
        changed?.Invoke(normalizedId);
    }

    public static void ApplyCloudCode(string motionId)
    {
        SetEquippedProfile(motionId);
    }

    public static string GetCurrentProfileMotionCode()
    {
        return ComposeProfileMotionCode(CurrentIdleMotionId, CurrentVictoryMotionId);
    }

    public static string NormalizeProfileMotionCode(
        string motionCode,
        LobbyMotionCatalog catalog = null)
    {
        ParseProfileMotionCode(
            motionCode,
            out string idleMotionId,
            out string victoryMotionId);

        return ComposeProfileMotionCode(
            NormalizeIdleMotionId(idleMotionId, catalog),
            NormalizeVictoryMotionId(victoryMotionId, catalog));
    }

    public static string NormalizeIdleMotionId(
        string motionId,
        LobbyMotionCatalog catalog = null)
    {
        if (TryParseProfileMotionCode(motionId, out string idleMotionId, out _))
        {
            motionId = idleMotionId;
        }

        return NormalizeMotionId(
            motionId,
            LobbyMotionCatalog.MotionSlot.Idle,
            catalog);
    }

    public static string NormalizeVictoryMotionId(
        string motionId,
        LobbyMotionCatalog catalog = null)
    {
        if (TryParseProfileMotionCode(motionId, out _, out string victoryMotionId))
        {
            motionId = victoryMotionId;
        }

        return NormalizeMotionId(
            motionId,
            LobbyMotionCatalog.MotionSlot.Victory,
            catalog);
    }

    private static string NormalizeMotionId(
        string motionId,
        LobbyMotionCatalog.MotionSlot slot,
        LobbyMotionCatalog catalog = null)
    {
        if (string.IsNullOrWhiteSpace(motionId))
        {
            return string.Empty;
        }

        string normalizedId = motionId.Trim();
        if (catalog == null)
        {
            catalog = LobbyMotionCatalog.LoadDefault();
        }
        return catalog != null && catalog.Contains(normalizedId, slot)
            ? normalizedId
            : string.Empty;
    }

    public static bool IsMotionUsable(
        LobbyMotionCatalog.MotionEntry entry,
        LobbyMotionCatalog catalog)
    {
        return entry != null &&
               entry.IsValid &&
               (entry.UnlockByDefault || IsUnlockedInCurrentVersion(entry.Id, catalog));
    }

    public static void ClearActiveUserCache()
    {
        EquippedChanged?.Invoke(string.Empty);
        VictoryEquippedChanged?.Invoke(string.Empty);
    }

    public static void ClearLocalSessionCache()
    {
        string userId = PlayerProfile.GetSavedUserId();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            PlayerPrefs.DeleteKey(EquippedIdleMotionKeyPrefix + userId.Trim());
            PlayerPrefs.DeleteKey(EquippedVictoryMotionKeyPrefix + userId.Trim());
        }

        PlayerPrefs.DeleteKey(FallbackEquippedIdleMotionKey);
        PlayerPrefs.DeleteKey(FallbackEquippedVictoryMotionKey);
        PlayerPrefs.Save();
        ClearActiveUserCache();
    }

    public static void SendCurrentProfileToServer()
    {
        LobbyButtonController.SendCurrentPlayerProfile();
        LobbyPlayer.SubmitLocalProfileToOwnedLobbyPlayer();
    }

    private static bool IsUnlockedInCurrentVersion(
        string motionId,
        LobbyMotionCatalog catalog)
    {
        return !string.IsNullOrWhiteSpace(motionId) &&
               catalog != null &&
               catalog.Contains(motionId);
    }

    private static string ComposeProfileMotionCode(
        string idleMotionId,
        string victoryMotionId)
    {
        idleMotionId = string.IsNullOrWhiteSpace(idleMotionId)
            ? string.Empty
            : idleMotionId.Trim();
        victoryMotionId = string.IsNullOrWhiteSpace(victoryMotionId)
            ? string.Empty
            : victoryMotionId.Trim();

        if (string.IsNullOrWhiteSpace(victoryMotionId))
        {
            return idleMotionId;
        }

        return idleMotionId + ProfileMotionSeparator + victoryMotionId;
    }

    private static void ParseProfileMotionCode(
        string motionCode,
        out string idleMotionId,
        out string victoryMotionId)
    {
        if (!TryParseProfileMotionCode(
                motionCode,
                out idleMotionId,
                out victoryMotionId))
        {
            idleMotionId = motionCode;
            victoryMotionId = string.Empty;
        }
    }

    private static bool TryParseProfileMotionCode(
        string motionCode,
        out string idleMotionId,
        out string victoryMotionId)
    {
        idleMotionId = string.Empty;
        victoryMotionId = string.Empty;
        if (string.IsNullOrWhiteSpace(motionCode))
        {
            return false;
        }

        int separatorIndex = motionCode.IndexOf(ProfileMotionSeparator);
        if (separatorIndex < 0)
        {
            return false;
        }

        idleMotionId = motionCode.Substring(0, separatorIndex).Trim();
        victoryMotionId = separatorIndex < motionCode.Length - 1
            ? motionCode.Substring(separatorIndex + 1).Trim()
            : string.Empty;
        return true;
    }

    private static string GetEquippedIdleMotionKey()
    {
        string userId = PlayerProfile.GetSavedUserId();
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackEquippedIdleMotionKey
            : EquippedIdleMotionKeyPrefix + userId.Trim();
    }

    private static string GetEquippedVictoryMotionKey()
    {
        string userId = PlayerProfile.GetSavedUserId();
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackEquippedVictoryMotionKey
            : EquippedVictoryMotionKeyPrefix + userId.Trim();
    }
}
