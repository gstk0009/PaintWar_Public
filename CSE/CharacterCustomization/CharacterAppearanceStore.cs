using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

public static class CharacterAppearanceStore
{
    private const string LegacyAppearanceKey = "PaintWar.CharacterAppearance";
    private const string AppearanceKeyPrefix = "PaintWar.CharacterAppearance.";
    private const string FallbackAppearanceKey = "PaintWar.CharacterAppearance.Local";
    private const string FullBodyCostume001Id = "Costume_13_001";
    private const string HatCostume002Id = "Costume_13_002";
    private const string FullBodyOutfitId = "Outfit_010";
    private const string FullBodyMascotId = "Mascot_002";
    private const double RequiredClothingChance = 0.9d;
    private const double HairChance = 0.7d;
    private const double HatChance = 0.35d;
    private const double AccessoryChance = 0.25d;
    private const double GlassesChance = 0.25d;
    private const double GlovesChance = 0.35d;

    private static readonly CharacterCosmeticSlot[] Costume001OccupiedSlots =
    {
        CharacterCosmeticSlot.Pants,
        CharacterCosmeticSlot.Outerwear,
        CharacterCosmeticSlot.Shoes
    };

    private static readonly CharacterCosmeticSlot[] OutfitOccupiedSlots =
    {
        CharacterCosmeticSlot.Outerwear
    };

    private static readonly CharacterCosmeticSlot[] MascotOccupiedSlots =
    {
        CharacterCosmeticSlot.Hat,
        CharacterCosmeticSlot.Hair,
        CharacterCosmeticSlot.Outerwear
    };

    public static event Action<string> Saved;
    public static event Action<string> EquippedChanged;

    private static string equippedAppearanceCode;
    private static string equippedUserId;

    public static string CurrentCode =>
        PlayerPrefs.GetString(GetCurrentAppearanceKey(), string.Empty);

    public static string EquippedCode
    {
        get
        {
            string currentUserId = GetActiveUserId();
            bool equippedForCurrentUser =
                string.Equals(equippedUserId, currentUserId, StringComparison.Ordinal);
            return !equippedForCurrentUser || string.IsNullOrWhiteSpace(equippedAppearanceCode)
                ? NormalizeCode(CurrentCode)
                : equippedAppearanceCode;
        }
    }

    public static void Save(string appearanceCode)
    {
        string normalizedCode = NormalizeCode(appearanceCode);
        equippedUserId = GetActiveUserId();
        equippedAppearanceCode = normalizedCode;
        PlayerPrefs.SetString(GetAppearanceKey(equippedUserId), normalizedCode);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveAppearanceCode(normalizedCode);
        Saved?.Invoke(normalizedCode);
        EquippedChanged?.Invoke(normalizedCode);
    }

    public static void ApplyCloudCode(string appearanceCode)
    {
        string normalizedCode = NormalizeCode(appearanceCode);
        equippedUserId = GetActiveUserId();
        equippedAppearanceCode = normalizedCode;
        PlayerPrefs.SetString(GetAppearanceKey(equippedUserId), normalizedCode);
        PlayerPrefs.Save();
        Saved?.Invoke(normalizedCode);
        EquippedChanged?.Invoke(normalizedCode);
    }

    public static void SetEquipped(string appearanceCode)
    {
        string normalizedCode = NormalizeCode(appearanceCode);
        string currentUserId = GetActiveUserId();
        if (normalizedCode == equippedAppearanceCode &&
            string.Equals(equippedUserId, currentUserId, StringComparison.Ordinal))
        {
            return;
        }

        equippedUserId = currentUserId;
        equippedAppearanceCode = normalizedCode;
        EquippedChanged?.Invoke(normalizedCode);
    }

    public static void ClearActiveUserCache()
    {
        equippedUserId = string.Empty;
        equippedAppearanceCode = string.Empty;
        EquippedChanged?.Invoke(string.Empty);
    }

    public static void ClearLocalSessionCache()
    {
        PlayerPrefs.DeleteKey(FallbackAppearanceKey);
        PlayerPrefs.DeleteKey(LegacyAppearanceKey);
        PlayerPrefs.Save();
        ClearActiveUserCache();
    }

    public static string SetSlot(
        string appearanceCode,
        CharacterCosmeticSlot slot,
        string itemId)
    {
        Dictionary<CharacterCosmeticSlot, string> selections = Decode(appearanceCode);
        if (slot == CharacterCosmeticSlot.Body)
        {
            selections.Remove(slot);
            return Encode(selections);
        }

        if (string.IsNullOrWhiteSpace(itemId))
        {
            selections.Remove(slot);
        }
        else
        {
            string normalizedItemId = itemId.Trim();
            selections[slot] = normalizedItemId;
            ApplySelectionConflicts(selections, slot, normalizedItemId);
        }

        return Encode(selections);
    }

    public static string GetSlot(string appearanceCode, CharacterCosmeticSlot slot)
    {
        Dictionary<CharacterCosmeticSlot, string> selections = Decode(appearanceCode);
        return selections.TryGetValue(slot, out string itemId) ? itemId : string.Empty;
    }

    public static Dictionary<CharacterCosmeticSlot, string> Decode(string appearanceCode)
    {
        Dictionary<CharacterCosmeticSlot, string> selections =
            new Dictionary<CharacterCosmeticSlot, string>();

        if (string.IsNullOrWhiteSpace(appearanceCode))
        {
            return selections;
        }

        string[] entries = appearanceCode.Split('|');
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i];
            int separatorIndex = entry.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            string slotName = entry.Substring(0, separatorIndex);
            string itemId = entry.Substring(separatorIndex + 1).Trim();
            if (Enum.TryParse(slotName, out CharacterCosmeticSlot slot) &&
                !string.IsNullOrWhiteSpace(itemId))
            {
                selections[slot] = itemId;
            }
        }

        MoveLegacyCostume002ToHat(selections);
        RemoveUnusedBodySelection(selections);
        return selections;
    }

    public static string NormalizeCode(string appearanceCode)
    {
        Dictionary<CharacterCosmeticSlot, string> selections = Decode(appearanceCode);
        ApplyAllSelectionConflicts(selections);
        return Encode(selections);
    }

    public static bool HasSelections(string appearanceCode)
    {
        return Decode(appearanceCode).Count > 0;
    }

    public static bool EnsureNewProfileDefaultAppearance()
    {
        if (HasSelections(CurrentCode))
        {
            return false;
        }

        CharacterCosmeticCatalog catalog = CharacterCosmeticCatalog.LoadDefault();
        if (catalog == null)
        {
            Debug.LogWarning(
                "[CharacterAppearance] Default appearance could not be created because " +
                "CharacterCosmeticCatalog was not found.");
            return false;
        }

        Dictionary<CharacterCosmeticSlot, string> selections =
            new Dictionary<CharacterCosmeticSlot, string>();
        bool hasFace =
            TrySetFirstSelection(catalog, selections, CharacterCosmeticSlot.Faces);
        bool hasOuterwear =
            TrySetFirstSelection(catalog, selections, CharacterCosmeticSlot.Outerwear);
        bool hasPants =
            TrySetFirstSelection(catalog, selections, CharacterCosmeticSlot.Pants);
        if (!hasFace || !hasOuterwear || !hasPants)
        {
            Debug.LogWarning(
                "[CharacterAppearance] Default appearance could not be created because " +
                "the required cosmetic slots have no items.");
            return false;
        }

        string defaultCode = Encode(selections);
        ApplyCloudCode(defaultCode);
        return true;
    }

    public static string CreateRandomAiAppearanceCode(int seed)
    {
        CharacterCosmeticCatalog catalog = CharacterCosmeticCatalog.LoadDefault();
        if (catalog == null)
        {
            return string.Empty;
        }

        System.Random random = new System.Random(seed);
        Dictionary<CharacterCosmeticSlot, string> selections =
            new Dictionary<CharacterCosmeticSlot, string>();

        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Faces,
            random,
            1d);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Hair,
            random,
            HairChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Outerwear,
            random,
            RequiredClothingChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Pants,
            random,
            RequiredClothingChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Shoes,
            random,
            RequiredClothingChance);

        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Hat,
            random,
            HatChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Accessories,
            random,
            AccessoryChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Glasses,
            random,
            GlassesChance);
        TrySetRandomSelection(
            catalog,
            selections,
            CharacterCosmeticSlot.Gloves,
            random,
            GlovesChance);

        return Encode(selections);
    }

    public static bool IsCosmeticNameOrId(string value)
    {
        return TryCreateSingleSelectionCode(value, out _);
    }

    public static bool TryCreateSingleSelectionCode(string value, out string appearanceCode)
    {
        appearanceCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalizedValue = value.Trim();
        CharacterCosmeticCatalog catalog = CharacterCosmeticCatalog.LoadDefault();
        if (catalog == null || catalog.Slots == null)
        {
            return false;
        }

        CharacterCosmeticCatalog.CosmeticSlotGroup[] slots = catalog.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            CharacterCosmeticCatalog.CosmeticSlotGroup group = slots[i];
            if (group == null || group.Items == null)
            {
                continue;
            }

            CharacterCosmeticCatalog.CosmeticItem[] items = group.Items;
            for (int j = 0; j < items.Length; j++)
            {
                CharacterCosmeticCatalog.CosmeticItem item = items[j];
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.ItemId, normalizedValue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DisplayName, normalizedValue, StringComparison.OrdinalIgnoreCase))
                {
                    appearanceCode = SetSlot(string.Empty, group.Slot, item.ItemId);
                    return true;
                }
            }
        }

        return false;
    }

    public static string Encode(Dictionary<CharacterCosmeticSlot, string> selections)
    {
        if (selections == null || selections.Count == 0)
        {
            return string.Empty;
        }

        List<string> entries = new List<string>();
        foreach (KeyValuePair<CharacterCosmeticSlot, string> pair in selections)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                entries.Add(pair.Key + ":" + pair.Value.Trim());
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return string.Join("|", entries);
    }

    private static void ApplySelectionConflicts(
        Dictionary<CharacterCosmeticSlot, string> selections,
        CharacterCosmeticSlot selectedSlot,
        string selectedItemId)
    {
        if (selections == null || string.IsNullOrWhiteSpace(selectedItemId))
        {
            return;
        }

        if (selectedSlot == CharacterCosmeticSlot.FullBody)
        {
            RemoveOccupiedSlots(selections, GetOccupiedSlotsForFullBody(selectedItemId));
            return;
        }

        if (!selections.TryGetValue(CharacterCosmeticSlot.FullBody, out string fullBodyItemId))
        {
            return;
        }

        if (FullBodyOccupiesSlot(fullBodyItemId, selectedSlot))
        {
            selections.Remove(CharacterCosmeticSlot.FullBody);
        }
    }

    private static void MoveLegacyCostume002ToHat(
        Dictionary<CharacterCosmeticSlot, string> selections)
    {
        if (selections == null ||
            !selections.TryGetValue(CharacterCosmeticSlot.FullBody, out string fullBodyItemId) ||
            !string.Equals(fullBodyItemId, HatCostume002Id, StringComparison.Ordinal))
        {
            return;
        }

        selections.Remove(CharacterCosmeticSlot.FullBody);
        selections[CharacterCosmeticSlot.Hat] = HatCostume002Id;
    }

    private static void RemoveUnusedBodySelection(
        Dictionary<CharacterCosmeticSlot, string> selections)
    {
        selections?.Remove(CharacterCosmeticSlot.Body);
    }

    private static void ApplyAllSelectionConflicts(
        Dictionary<CharacterCosmeticSlot, string> selections)
    {
        if (selections == null ||
            !selections.TryGetValue(CharacterCosmeticSlot.FullBody, out string fullBodyItemId))
        {
            return;
        }

        RemoveOccupiedSlots(selections, GetOccupiedSlotsForFullBody(fullBodyItemId));
    }

    private static void RemoveOccupiedSlots(
        Dictionary<CharacterCosmeticSlot, string> selections,
        CharacterCosmeticSlot[] occupiedSlots)
    {
        if (selections == null || occupiedSlots == null)
        {
            return;
        }

        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            selections.Remove(occupiedSlots[i]);
        }
    }

    private static bool FullBodyOccupiesSlot(
        string fullBodyItemId,
        CharacterCosmeticSlot slot)
    {
        CharacterCosmeticSlot[] occupiedSlots = GetOccupiedSlotsForFullBody(fullBodyItemId);
        if (occupiedSlots == null)
        {
            return false;
        }

        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (occupiedSlots[i] == slot)
            {
                return true;
            }
        }

        return false;
    }

    private static CharacterCosmeticSlot[] GetOccupiedSlotsForFullBody(string fullBodyItemId)
    {
        if (string.Equals(fullBodyItemId, FullBodyCostume001Id, StringComparison.Ordinal))
        {
            return Costume001OccupiedSlots;
        }

        if (string.Equals(fullBodyItemId, FullBodyOutfitId, StringComparison.Ordinal))
        {
            return OutfitOccupiedSlots;
        }

        if (string.Equals(fullBodyItemId, FullBodyMascotId, StringComparison.Ordinal))
        {
            return MascotOccupiedSlots;
        }

        return null;
    }

    private static void TrySetRandomSelection(
        CharacterCosmeticCatalog catalog,
        Dictionary<CharacterCosmeticSlot, string> selections,
        CharacterCosmeticSlot slot,
        System.Random random,
        double chance)
    {
        if (catalog == null ||
            selections == null ||
            random == null ||
            chance <= 0d ||
            random.NextDouble() > chance)
        {
            return;
        }

        CharacterCosmeticCatalog.CosmeticSlotGroup group = catalog.GetSlot(slot);
        if (group == null || group.Items == null || group.Items.Length == 0)
        {
            return;
        }

        List<string> itemIds = new List<string>();
        CharacterCosmeticCatalog.CosmeticItem[] items = group.Items;
        for (int i = 0; i < items.Length; i++)
        {
            CharacterCosmeticCatalog.CosmeticItem item = items[i];
            if (item != null && !string.IsNullOrWhiteSpace(item.ItemId))
            {
                itemIds.Add(item.ItemId.Trim());
            }
        }

        if (itemIds.Count == 0)
        {
            return;
        }

        selections[slot] = itemIds[random.Next(itemIds.Count)];
    }

    private static bool TrySetFirstSelection(
        CharacterCosmeticCatalog catalog,
        Dictionary<CharacterCosmeticSlot, string> selections,
        CharacterCosmeticSlot slot)
    {
        if (catalog == null || selections == null)
        {
            return false;
        }

        CharacterCosmeticCatalog.CosmeticSlotGroup group = catalog.GetSlot(slot);
        if (group == null || group.Items == null)
        {
            return false;
        }

        CharacterCosmeticCatalog.CosmeticItem[] items = group.Items;
        for (int i = 0; i < items.Length; i++)
        {
            CharacterCosmeticCatalog.CosmeticItem item = items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
            {
                continue;
            }

            selections[slot] = item.ItemId.Trim();
            return true;
        }

        return false;
    }

    public static void SendCurrentProfileToServer()
    {
        if (InstanceFinder.ClientManager == null ||
            !InstanceFinder.ClientManager.Started)
        {
            return;
        }

        InstanceFinder.ClientManager.Broadcast(new PlayerProfileBroadcast
        {
            UserId = PlayerProfile.GetSavedUserId(),
            Nickname = PlayerProfile.GetOrCreateNickname(),
            AppearanceCode = NormalizeCode(CurrentCode),
            MotionCode = CharacterMotionStore.GetCurrentProfileMotionCode(),
            SelectedSkillId = PlayerSkillLoadoutStore.CurrentSkillId
        });

        LobbyPlayer.SubmitLocalProfileToOwnedLobbyPlayer();
    }

    private static string GetCurrentAppearanceKey()
    {
        return GetAppearanceKey(GetActiveUserId());
    }

    private static string GetAppearanceKey(string userId)
    {
        userId = SanitizeUserId(userId);
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackAppearanceKey
            : AppearanceKeyPrefix + userId;
    }

    private static string GetActiveUserId()
    {
        return SanitizeUserId(PlayerProfile.GetSavedUserId());
    }

    private static string SanitizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }
}
