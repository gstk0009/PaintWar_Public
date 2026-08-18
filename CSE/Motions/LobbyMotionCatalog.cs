using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "LobbyMotionCatalog",
    menuName = "PaintWar/Lobby Motion Catalog")]
public sealed class LobbyMotionCatalog : ScriptableObject
{
    private const string ResourcePath = "LobbyMotionCatalog";

    [SerializeField] private MotionEntry[] entries;

    private static LobbyMotionCatalog cachedCatalog;

    public MotionEntry[] Entries => entries;
    public int Count => entries != null ? entries.Length : 0;

    public static LobbyMotionCatalog LoadDefault()
    {
        if (cachedCatalog == null)
        {
            cachedCatalog = Resources.Load<LobbyMotionCatalog>(ResourcePath);
        }

        return cachedCatalog;
    }

    public bool Contains(string motionId)
    {
        return Find(motionId, null) != null;
    }

    public bool Contains(string motionId, MotionSlot slot)
    {
        return Find(motionId, slot) != null;
    }

    public bool TryGetEntryById(string motionId, out MotionEntry entry)
    {
        entry = Find(motionId, null);
        return entry != null;
    }

    public bool TryGetEntryById(
        string motionId,
        MotionSlot slot,
        out MotionEntry entry)
    {
        entry = Find(motionId, slot);
        return entry != null;
    }

    public bool TryGetClip(string motionId, out AnimationClip clip)
    {
        return TryGetClip(motionId, MotionSlot.Idle, out clip);
    }

    public bool TryGetClip(
        string motionId,
        MotionSlot slot,
        out AnimationClip clip)
    {
        clip = null;
        MotionEntry entry = Find(motionId, slot);
        if (entry == null)
        {
            return false;
        }

        clip = entry.Clip;
        return clip != null;
    }

    public float GetPlaybackSpeed(string motionId)
    {
        return GetPlaybackSpeed(motionId, MotionSlot.Idle);
    }

    public float GetPlaybackSpeed(string motionId, MotionSlot slot)
    {
        MotionEntry entry = Find(motionId, slot);
        return entry != null ? entry.PlaybackSpeed : 1f;
    }

    public MotionEntry GetDefaultEntry()
    {
        return GetDefaultEntry(MotionSlot.Idle);
    }

    public MotionEntry GetDefaultEntry(MotionSlot slot)
    {
        if (entries == null)
        {
            return null;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            MotionEntry entry = entries[i];
            if (entry != null &&
                entry.IsValid &&
                entry.Slot == slot &&
                entry.UnlockByDefault)
            {
                return entry;
            }
        }

        for (int i = 0; i < entries.Length; i++)
        {
            MotionEntry entry = entries[i];
            if (entry != null && entry.IsValid && entry.Slot == slot)
            {
                return entry;
            }
        }

        return null;
    }

    private MotionEntry Find(string motionId, MotionSlot? slot)
    {
        if (string.IsNullOrWhiteSpace(motionId) || entries == null)
        {
            return null;
        }

        string normalizedId = motionId.Trim();
        for (int i = 0; i < entries.Length; i++)
        {
            MotionEntry entry = entries[i];
            if (entry != null &&
                entry.IsValid &&
                (!slot.HasValue || entry.Slot == slot.Value) &&
                string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    public enum MotionSlot
    {
        Idle = 0,
        Victory = 1
    }

    [Serializable]
    public sealed class MotionEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private MotionSlot slot = MotionSlot.Idle;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private bool unlockByDefault = true;
        [SerializeField] private int pointCost;
        [SerializeField] private float playbackSpeed = 1f;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public MotionSlot Slot => slot;
        public AnimationClip Clip => clip;
        public bool UnlockByDefault => unlockByDefault;
        public int PointCost => Mathf.Max(0, pointCost);
        public float PlaybackSpeed => Mathf.Max(0.01f, playbackSpeed);
        public bool IsValid => !string.IsNullOrWhiteSpace(id) && clip != null;
    }
}
