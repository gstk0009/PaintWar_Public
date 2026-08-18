using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;

public static class FirebasePlayerProfileStore
{
    private const string UsersCollectionName = "users";
    private const string NicknameField = "nickname";
    private const string NicknameNormalizedField = "nicknameNormalized";
    private const string NicknameConfirmedField = "nicknameConfirmed";
    private const string AppearanceCodeField = "appearanceCode";
    private const string MotionCodeField = "motionCode";
    private const string SelectedSkillIdField = "selectedSkillId";
    private const string UnlockedEmojiIdsField = "unlockedEmojiIds";
    private const string UpdatedAtField = "updatedAt";
    private const string EmojiSlotFieldPrefix = "emojiSlot";

    public static bool LastLoadFailedTransiently { get; private set; }
    public static bool LastNicknameAvailabilityCheckFailed { get; private set; }

    public static async Task<bool> LoadCurrentUserAsync()
    {
        LastLoadFailedTransiently = false;
        DocumentReference document = GetCurrentUserDocument();
        if (document == null)
        {
            return false;
        }

        DocumentSnapshot snapshot;
        try
        {
            snapshot = await document.GetSnapshotAsync();
        }
        catch (Exception exception)
        {
            if (!IsRecoverableFirestoreFailure(exception))
            {
                throw;
            }

            LastLoadFailedTransiently = true;
            Debug.LogWarning(
                "[FirestoreProfile] Cloud profile is temporarily unavailable; using local cache. " +
                GetInnermostMessage(exception));
            return false;
        }

        if (!snapshot.Exists)
        {
            return false;
        }

        Dictionary<string, object> data = snapshot.ToDictionary();
        ApplyNickname(data);
        ApplyAppearance(data);
        ApplyMotion(data);
        ApplySelectedSkill(data);
        ApplyEmojiLoadout(data);
        return true;
    }

    public static void ApplyCurrentLocalDataToRuntime()
    {
        LobbyCurrentCharacterPreview.RefreshImmediatelyForFrontScreen();
        LobbyButtonController.SendCurrentPlayerProfile();
    }

    public static void SaveCurrentSnapshot()
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddProfileFields(fields);
        AddAppearanceFields(fields, CharacterAppearanceStore.CurrentCode);
        AddMotionFields(fields, CharacterMotionStore.GetCurrentProfileMotionCode());
        AddSelectedSkillFields(fields, PlayerSkillLoadoutStore.CurrentSkillId);
        AddEmojiFields(fields, LobbyEmojiCatalog.LoadDefault());
        SaveFields(fields);
    }

    public static void SaveCurrentProfile()
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddProfileFields(fields);
        SaveFields(fields);
    }

    public static async Task<bool> IsNicknameAvailableAsync(string nickname)
    {
        LastNicknameAvailabilityCheckFailed = false;
        nickname = PlayerProfile.SanitizeNickname(nickname);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return false;
        }

        string currentUserId = GetCurrentUserId();
        try
        {
            string normalizedNickname = NormalizeNicknameForLookup(nickname);
            if (await HasNicknameConflictAsync(
                    NicknameNormalizedField,
                    normalizedNickname,
                    currentUserId))
            {
                return false;
            }

            return !await HasNicknameConflictAsync(NicknameField, nickname, currentUserId);
        }
        catch (Exception exception)
        {
            if (!IsRecoverableFirestoreFailure(exception))
            {
                throw;
            }

            LastNicknameAvailabilityCheckFailed = true;
            Debug.LogWarning(
                "[FirestoreProfile] Nickname availability check failed. " +
                GetInnermostMessage(exception));
            return false;
        }
    }

    public static void SaveAppearanceCode(string appearanceCode)
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddAppearanceFields(fields, appearanceCode);
        SaveFields(fields);
    }

    public static void SaveEmojiLoadout(LobbyEmojiCatalog catalog)
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddEmojiFields(fields, catalog);
        SaveFields(fields);
    }

    public static void SaveMotionCode(string motionCode)
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddMotionFields(fields, motionCode);
        SaveFields(fields);
    }

    public static void SaveSelectedSkillId(string skillId)
    {
        Dictionary<string, object> fields = new Dictionary<string, object>();
        AddSelectedSkillFields(fields, skillId);
        SaveFields(fields);
    }

    private static void ApplyNickname(Dictionary<string, object> data)
    {
        if (data == null ||
            !TryGetString(data, NicknameField, out string nickname))
        {
            return;
        }

        bool confirmed = TryGetBool(data, NicknameConfirmedField, out bool value) && value;
        PlayerProfile.ApplyCloudProfile(nickname, confirmed);
    }

    private static void ApplyAppearance(Dictionary<string, object> data)
    {
        if (data != null &&
            TryGetString(data, AppearanceCodeField, out string appearanceCode))
        {
            CharacterAppearanceStore.ApplyCloudCode(appearanceCode);
        }
    }

    private static void ApplyMotion(Dictionary<string, object> data)
    {
        if (data != null &&
            TryGetString(data, MotionCodeField, out string motionCode))
        {
            CharacterMotionStore.ApplyCloudCode(motionCode);
        }
    }

    private static void ApplySelectedSkill(Dictionary<string, object> data)
    {
        if (data != null &&
            TryGetString(data, SelectedSkillIdField, out string selectedSkillId))
        {
            PlayerSkillLoadoutStore.ApplyCloudSkillId(selectedSkillId);
        }
    }

    private static void ApplyEmojiLoadout(Dictionary<string, object> data)
    {
        if (data == null)
        {
            return;
        }

        string[] slots = new string[LobbyEmojiLoadoutStore.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            if (TryGetString(data, GetEmojiSlotField(i), out string emojiId))
            {
                slots[i] = emojiId;
            }
        }

        TryGetString(data, UnlockedEmojiIdsField, out string unlockedEmojiIds);
        LobbyEmojiLoadoutStore.ApplyCloudLoadout(
            slots,
            unlockedEmojiIds,
            LobbyEmojiCatalog.LoadDefault());
    }

    private static void AddProfileFields(Dictionary<string, object> fields)
    {
        string nickname = PlayerProfile.GetOrCreateNickname();
        fields[NicknameField] = nickname;
        fields[NicknameNormalizedField] = NormalizeNicknameForLookup(nickname);
        fields[NicknameConfirmedField] = PlayerProfile.HasConfirmedNickname();
    }

    private static void AddAppearanceFields(
        Dictionary<string, object> fields,
        string appearanceCode)
    {
        fields[AppearanceCodeField] =
            CharacterAppearanceStore.NormalizeCode(appearanceCode);
    }

    private static void AddMotionFields(
        Dictionary<string, object> fields,
        string motionCode)
    {
        fields[MotionCodeField] =
            CharacterMotionStore.NormalizeProfileMotionCode(motionCode);
    }

    private static void AddSelectedSkillFields(
        Dictionary<string, object> fields,
        string skillId)
    {
        fields[SelectedSkillIdField] =
            PlayerSkillLoadoutStore.NormalizeSkillId(skillId);
    }

    private static void AddEmojiFields(
        Dictionary<string, object> fields,
        LobbyEmojiCatalog catalog)
    {
        string[] slots = LobbyEmojiLoadoutStore.GetEquippedEmojiIds(catalog);
        for (int i = 0; i < LobbyEmojiLoadoutStore.SlotCount; i++)
        {
            string emojiId = i < slots.Length ? slots[i] : string.Empty;
            fields[GetEmojiSlotField(i)] = string.IsNullOrWhiteSpace(emojiId)
                ? string.Empty
                : emojiId.Trim();
        }

        fields[UnlockedEmojiIdsField] =
            LobbyEmojiLoadoutStore.GetSerializedUnlockedEmojiIds();
    }

    private static void SaveFields(Dictionary<string, object> fields)
    {
        if (fields == null || fields.Count == 0)
        {
            return;
        }

        DocumentReference document = GetCurrentUserDocument();
        if (document == null)
        {
            return;
        }

        fields[UpdatedAtField] = FieldValue.ServerTimestamp;
        document.SetAsync(fields, SetOptions.MergeAll).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                Debug.LogWarning("[FirestoreProfile] Save was canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                Debug.LogWarning(
                    "[FirestoreProfile] Save failed; local cache remains active. " +
                    GetInnermostMessage(task.Exception));
            }
        });
    }

    private static DocumentReference GetCurrentUserDocument()
    {
        string userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return FirebaseFirestore.DefaultInstance
            .Collection(UsersCollectionName)
            .Document(userId.Trim());
    }

    private static async Task<bool> HasNicknameConflictAsync(
        string fieldName,
        string value,
        string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        QuerySnapshot snapshot = await FirebaseFirestore.DefaultInstance
            .Collection(UsersCollectionName)
            .WhereEqualTo(fieldName, value)
            .GetSnapshotAsync();

        foreach (DocumentSnapshot document in snapshot.Documents)
        {
            if (document == null)
            {
                continue;
            }

            if (!string.Equals(document.Id, currentUserId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCurrentUserId()
    {
        FirebaseAuth auth = FirebaseAuth.DefaultInstance;
        FirebaseUser user = auth != null ? auth.CurrentUser : null;
        return user != null ? user.UserId : PlayerProfile.GetSavedUserId();
    }

    private static string NormalizeNicknameForLookup(string nickname)
    {
        nickname = PlayerProfile.SanitizeNickname(nickname);
        return string.IsNullOrWhiteSpace(nickname)
            ? string.Empty
            : nickname.ToLowerInvariant();
    }

    private static bool TryGetString(
        Dictionary<string, object> data,
        string fieldName,
        out string value)
    {
        value = string.Empty;
        if (data == null ||
            string.IsNullOrWhiteSpace(fieldName) ||
            !data.TryGetValue(fieldName, out object rawValue) ||
            rawValue == null)
        {
            return false;
        }

        value = rawValue.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetBool(
        Dictionary<string, object> data,
        string fieldName,
        out bool value)
    {
        value = false;
        if (data == null ||
            string.IsNullOrWhiteSpace(fieldName) ||
            !data.TryGetValue(fieldName, out object rawValue) ||
            rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return bool.TryParse(rawValue.ToString(), out value);
    }

    private static string GetEmojiSlotField(int slotIndex)
    {
        return EmojiSlotFieldPrefix + slotIndex;
    }

    private static bool IsRecoverableFirestoreFailure(Exception exception)
    {
        string message = GetInnermostMessage(exception).ToLowerInvariant();
        return message.Contains("offline") ||
               message.Contains("unavailable") ||
               message.Contains("deadline") ||
               message.Contains("timeout") ||
               message.Contains("network");
    }

    private static string GetInnermostMessage(Exception exception)
    {
        if (exception == null)
        {
            return string.Empty;
        }

        Exception current = exception;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
