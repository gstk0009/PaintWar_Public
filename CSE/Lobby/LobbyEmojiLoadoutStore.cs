using System;
using System.Text;
using UnityEngine;

public static class LobbyEmojiLoadoutStore
{
    public const int SlotCount = 3;

    private const string SlotKeyPrefix = "PaintWar.Emoji.Slot.";
    private const string UnlockedKey = "PaintWar.Emoji.Unlocked";
    private const char Separator = '|';

    public static event Action Changed;

    public static string GetEquippedEmojiId(int slotIndex, LobbyEmojiCatalog catalog)
    {
        if (!IsValidSlot(slotIndex))
        {
            return string.Empty;
        }

        EnsureDefaultSlots(catalog);
        string emojiId = PlayerPrefs.GetString(GetSlotKey(slotIndex), string.Empty);
        return IsEmojiUsable(emojiId, catalog) ? emojiId : string.Empty;
    }

    public static bool TryGetEquippedEntry(
        int slotIndex,
        LobbyEmojiCatalog catalog,
        out LobbyEmojiCatalog.EmojiEntry entry)
    {
        entry = null;
        string emojiId = GetEquippedEmojiId(slotIndex, catalog);
        return catalog != null &&
               !string.IsNullOrWhiteSpace(emojiId) &&
               catalog.TryGetEntryById(emojiId, out entry);
    }

    public static bool EquipSlot(
        int slotIndex,
        string emojiId,
        LobbyEmojiCatalog catalog)
    {
        if (!IsValidSlot(slotIndex) ||
            string.IsNullOrWhiteSpace(emojiId) ||
            !IsEmojiUsable(emojiId, catalog))
        {
            return false;
        }

        string normalizedId = emojiId.Trim();
        int equippedSlot = FindEquippedSlot(normalizedId, catalog);
        if (equippedSlot >= 0 && equippedSlot != slotIndex)
        {
            return false;
        }

        string key = GetSlotKey(slotIndex);
        if (string.Equals(
                PlayerPrefs.GetString(key, string.Empty),
                normalizedId,
                StringComparison.Ordinal))
        {
            return true;
        }

        PlayerPrefs.SetString(key, normalizedId);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveEmojiLoadout(catalog);
        Changed?.Invoke();
        return true;
    }

    public static bool IsUnlocked(
        LobbyEmojiCatalog.EmojiEntry entry,
        LobbyEmojiCatalog catalog)
    {
        if (entry == null || !entry.IsValid)
        {
            return false;
        }

        if (entry.UnlockByDefault || IsUnlockedInPrefs(entry.Id))
        {
            return true;
        }

        return false;
    }

    public static void UnlockForLocalProfile(string emojiId, LobbyEmojiCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(emojiId) ||
            catalog == null ||
            !catalog.TryGetEntryById(emojiId.Trim(), out LobbyEmojiCatalog.EmojiEntry entry) ||
            entry == null ||
            !entry.IsValid)
        {
            return;
        }

        AddUnlockedId(entry.Id);
        FirebasePlayerProfileStore.SaveEmojiLoadout(catalog);
        Changed?.Invoke();
    }

    public static string[] GetEquippedEmojiIds(LobbyEmojiCatalog catalog)
    {
        EnsureDefaultSlots(catalog);
        string[] emojiIds = new string[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            emojiIds[i] = GetEquippedEmojiId(i, catalog);
        }

        return emojiIds;
    }

    public static string GetSerializedUnlockedEmojiIds()
    {
        return PlayerPrefs.GetString(UnlockedKey, string.Empty);
    }

    public static void ApplyCloudLoadout(
        string[] slotEmojiIds,
        string serializedUnlockedEmojiIds,
        LobbyEmojiCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(serializedUnlockedEmojiIds))
        {
            PlayerPrefs.SetString(UnlockedKey, NormalizeSerializedUnlockedIds(
                serializedUnlockedEmojiIds,
                catalog));
        }

        if (slotEmojiIds != null)
        {
            for (int i = 0; i < SlotCount && i < slotEmojiIds.Length; i++)
            {
                string emojiId = slotEmojiIds[i];
                string key = GetSlotKey(i);
                if (IsEmojiUsable(emojiId, catalog))
                {
                    PlayerPrefs.SetString(key, emojiId.Trim());
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }
        }

        EnsureDefaultSlots(catalog);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static int FindEquippedSlot(string emojiId, LobbyEmojiCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(emojiId))
        {
            return -1;
        }

        string normalizedId = emojiId.Trim();
        for (int i = 0; i < SlotCount; i++)
        {
            string equippedId = GetEquippedEmojiId(i, catalog);
            if (string.Equals(equippedId, normalizedId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void EnsureDefaultSlots(LobbyEmojiCatalog catalog)
    {
        if (catalog == null)
        {
            return;
        }

        bool changed = false;
        string[] assignedEmojiIds = new string[SlotCount];
        for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            string key = GetSlotKey(slotIndex);
            string currentId = PlayerPrefs.GetString(key, string.Empty).Trim();
            if (IsEmojiUsable(currentId, catalog) &&
                !ContainsAssignedEmojiId(assignedEmojiIds, currentId))
            {
                assignedEmojiIds[slotIndex] = currentId;
                continue;
            }

            if (TryGetDefaultUnlockedEntry(
                    catalog,
                    assignedEmojiIds,
                    out LobbyEmojiCatalog.EmojiEntry entry))
            {
                PlayerPrefs.SetString(key, entry.Id);
                assignedEmojiIds[slotIndex] = entry.Id;
                changed = true;
            }
        }

        if (changed)
        {
            PlayerPrefs.Save();
            FirebasePlayerProfileStore.SaveEmojiLoadout(catalog);
        }
    }

    private static bool TryGetDefaultUnlockedEntry(
        LobbyEmojiCatalog catalog,
        string[] assignedEmojiIds,
        out LobbyEmojiCatalog.EmojiEntry entry)
    {
        entry = null;
        if (catalog == null || catalog.Entries == null)
        {
            return false;
        }

        LobbyEmojiCatalog.EmojiEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry candidate = entries[i];
            if (candidate == null ||
                !candidate.IsValid ||
                !candidate.UnlockByDefault ||
                ContainsAssignedEmojiId(assignedEmojiIds, candidate.Id))
            {
                continue;
            }

            entry = candidate;
            return true;
        }

        return false;
    }

    private static bool ContainsAssignedEmojiId(string[] assignedEmojiIds, string emojiId)
    {
        if (assignedEmojiIds == null || string.IsNullOrWhiteSpace(emojiId))
        {
            return false;
        }

        string normalizedId = emojiId.Trim();
        for (int i = 0; i < assignedEmojiIds.Length; i++)
        {
            if (string.Equals(assignedEmojiIds[i], normalizedId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmojiUsable(string emojiId, LobbyEmojiCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(emojiId) ||
            catalog == null ||
            !catalog.TryGetEntryById(emojiId.Trim(), out LobbyEmojiCatalog.EmojiEntry entry))
        {
            return false;
        }

        return IsUnlocked(entry, catalog);
    }

    private static bool IsUnlockedInPrefs(string emojiId)
    {
        if (string.IsNullOrWhiteSpace(emojiId))
        {
            return false;
        }

        string normalizedId = emojiId.Trim();
        string serialized = PlayerPrefs.GetString(UnlockedKey, string.Empty);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return false;
        }

        string[] ids = serialized.Split(Separator);
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.Equals(ids[i], normalizedId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddUnlockedId(string emojiId)
    {
        string normalizedId = emojiId.Trim();
        if (IsUnlockedInPrefs(normalizedId))
        {
            return;
        }

        string serialized = PlayerPrefs.GetString(UnlockedKey, string.Empty);
        StringBuilder builder = new StringBuilder(serialized);
        if (builder.Length > 0)
        {
            builder.Append(Separator);
        }

        builder.Append(normalizedId);
        PlayerPrefs.SetString(UnlockedKey, builder.ToString());
        PlayerPrefs.Save();
    }

    private static string NormalizeSerializedUnlockedIds(
        string serializedUnlockedEmojiIds,
        LobbyEmojiCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(serializedUnlockedEmojiIds))
        {
            return string.Empty;
        }

        string[] ids = serializedUnlockedEmojiIds.Split(Separator);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < ids.Length; i++)
        {
            string emojiId = ids[i];
            if (string.IsNullOrWhiteSpace(emojiId) ||
                catalog == null ||
                !catalog.TryGetEntryById(
                    emojiId.Trim(),
                    out LobbyEmojiCatalog.EmojiEntry entry) ||
                entry == null ||
                !entry.IsValid ||
                IsSerializedBuilderContaining(builder, entry.Id))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(Separator);
            }

            builder.Append(entry.Id);
        }

        return builder.ToString();
    }

    private static bool IsSerializedBuilderContaining(
        StringBuilder builder,
        string emojiId)
    {
        if (builder == null || string.IsNullOrWhiteSpace(emojiId))
        {
            return false;
        }

        string[] ids = builder.ToString().Split(Separator);
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.Equals(ids[i], emojiId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < SlotCount;
    }

    private static string GetSlotKey(int slotIndex)
    {
        return SlotKeyPrefix + slotIndex;
    }
}
