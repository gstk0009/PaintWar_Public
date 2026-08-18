using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "LobbyEmojiCatalog",
    menuName = "PaintWar/Lobby Emoji Catalog")]
public sealed class LobbyEmojiCatalog : ScriptableObject
{
    private const string ResourcePath = "LobbyEmojiCatalog";

    [SerializeField] private EmojiEntry[] entries;

    private static LobbyEmojiCatalog cachedCatalog;

    public EmojiEntry[] Entries => entries;
    public int Count => entries != null ? entries.Length : 0;

    public static LobbyEmojiCatalog LoadDefault()
    {
        if (cachedCatalog == null)
        {
            cachedCatalog = Resources.Load<LobbyEmojiCatalog>(ResourcePath);
        }

        return cachedCatalog;
    }

    public bool TryGetEntry(int index, out EmojiEntry entry)
    {
        entry = null;
        if (entries == null || index < 0 || index >= entries.Length)
        {
            return false;
        }

        entry = entries[index];
        return entry != null && entry.IsValid;
    }

    public bool TryGetSprite(string emojiId, out Sprite sprite)
    {
        sprite = null;
        EmojiEntry entry = Find(emojiId);
        if (entry == null)
        {
            return false;
        }

        sprite = entry.Sprite;
        return sprite != null;
    }

    public bool Contains(string emojiId)
    {
        return Find(emojiId) != null;
    }

    public bool TryGetEntryById(string emojiId, out EmojiEntry entry)
    {
        entry = Find(emojiId);
        return entry != null;
    }

    private EmojiEntry Find(string emojiId)
    {
        if (string.IsNullOrWhiteSpace(emojiId) || entries == null)
        {
            return null;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            EmojiEntry entry = entries[i];
            if (entry != null &&
                entry.IsValid &&
                string.Equals(entry.Id, emojiId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    [Serializable]
    public sealed class EmojiEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private bool unlockByDefault;
        [SerializeField] private int pointCost;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public Sprite Sprite => sprite;
        public bool UnlockByDefault => unlockByDefault;
        public int PointCost => Mathf.Max(0, pointCost);
        public bool IsValid => !string.IsNullOrWhiteSpace(id) && sprite != null;
    }
}
