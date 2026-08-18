using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum CharacterCosmeticSlot
{
    Body = 0,
    Hat = 1,
    Accessories = 2,
    Pants = 3,
    Faces = 4,
    Hair = 5,
    Outerwear = 6,
    Shoes = 7,
    FullBody = 8,
    Glasses = 9,
    Gloves = 10
}

[CreateAssetMenu(
    fileName = "CharacterCosmeticCatalog",
    menuName = "PaintWar/Character Cosmetic Catalog")]
public sealed class CharacterCosmeticCatalog : ScriptableObject
{
    private const string ResourcePath = "CharacterCosmeticCatalog";
    private const string ThumbnailResourceRoot = "CharacterCosmeticThumbnails/";

    [SerializeField] private CosmeticSlotGroup[] slots;

    private static CharacterCosmeticCatalog cachedCatalog;

    public CosmeticSlotGroup[] Slots => slots;

    public static CharacterCosmeticCatalog LoadDefault()
    {
        if (cachedCatalog == null)
        {
            cachedCatalog = Resources.Load<CharacterCosmeticCatalog>(ResourcePath);
        }

        return cachedCatalog;
    }

    public CosmeticSlotGroup GetSlot(CharacterCosmeticSlot slot)
    {
        if (slots == null)
        {
            return null;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].Slot == slot)
            {
                return slots[i];
            }
        }

        return null;
    }

    public CosmeticItem FindItem(CharacterCosmeticSlot slot, string itemId)
    {
        CosmeticSlotGroup group = GetSlot(slot);
        return group != null ? group.FindItem(itemId) : null;
    }

    [Serializable]
    public sealed class CosmeticSlotGroup
    {
        [SerializeField] private CharacterCosmeticSlot slot;
        [SerializeField] private string displayName;
        [SerializeField] private bool allowNone = true;
        [SerializeField] private CosmeticItem[] items;

        public CharacterCosmeticSlot Slot => slot;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? slot.ToString() : displayName;
        public bool AllowNone => allowNone;
        public CosmeticItem[] Items => items;

        public CosmeticItem FindItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Length; i++)
            {
                CosmeticItem item = items[i];
                if (item != null && item.ItemId == itemId)
                {
                    return item;
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class CosmeticItem
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private string resourcePath;
        [SerializeField] private string editorAssetPath;

        public string ItemId => itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;
        public GameObject Prefab => prefab;

        public Sprite LoadThumbnailSprite()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return Resources.Load<Sprite>(ThumbnailResourceRoot + itemId.Trim());
        }

        public GameObject LoadPrefab()
        {
            if (IsUsablePrefab(prefab))
            {
                return prefab;
            }

            if (!string.IsNullOrWhiteSpace(resourcePath))
            {
                GameObject resourcePrefab = Resources.Load<GameObject>(resourcePath.Trim());
                if (resourcePrefab != null)
                {
                    return resourcePrefab;
                }
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(editorAssetPath))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(editorAssetPath.Trim());
            }
#endif

            return prefab;
        }

        private static bool IsUsablePrefab(GameObject target)
        {
            return target != null && !string.IsNullOrWhiteSpace(target.name);
        }
    }
}
