using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "LGH/Players/Player Level Stats Config", fileName = "PlayerLevelStatsConfig")]
public class PlayerLevelStatsConfig : ScriptableObject
{
    private const string DefaultResourcePath = "PlayerLevelStatsConfig";

    [Serializable]
    public struct LevelStat
    {
        [SerializeField] private int requiredExp;
        [SerializeField] private float cooldownMultiplier;
        [FormerlySerializedAs("moveSpeedBonus")]
        [SerializeField] private float moveSpeed;

        public int RequiredExp { get { return Mathf.Max(0, requiredExp); } }
        public float CooldownMultiplier { get { return Mathf.Max(0.01f, cooldownMultiplier); } }
        public float MoveSpeed { get { return Mathf.Max(0f, moveSpeed); } }

        public LevelStat(int requiredExp, float cooldownMultiplier, float moveSpeed)
        {
            this.requiredExp = Mathf.Max(0, requiredExp);
            this.cooldownMultiplier = Mathf.Max(0.01f, cooldownMultiplier);
            this.moveSpeed = Mathf.Max(0f, moveSpeed);
        }
    }

    [SerializeField]
    private LevelStat[] levels =
    {
        new LevelStat(0, 1f, 7f),
        new LevelStat(500, 0.90f, 7.25f),
        new LevelStat(1200, 0.80f, 7.5f),
        new LevelStat(2200, 0.70f, 7.75f),
        new LevelStat(3600, 0.60f, 8f)
    };

    private static PlayerLevelStatsConfig cachedDefault;

    public int MaxLevel { get { return Mathf.Max(1, levels != null ? levels.Length : 0); } }

    public int GetRequiredExpForLevel(int level)
    {
        return GetStat(level).RequiredExp;
    }

    public int GetNextRequiredExp(int currentLevel)
    {
        int clampedLevel = Mathf.Clamp(currentLevel, 1, MaxLevel);
        return clampedLevel >= MaxLevel
            ? GetRequiredExpForLevel(MaxLevel)
            : GetRequiredExpForLevel(clampedLevel + 1);
    }

    public float GetCooldownMultiplier(int level)
    {
        return GetStat(level).CooldownMultiplier;
    }

    public float GetMoveSpeed(int level)
    {
        return GetStat(level).MoveSpeed;
    }

    public int GetLevelForExp(int currentExp)
    {
        int resolvedLevel = 1;
        int safeExp = Mathf.Max(0, currentExp);
        for (int i = 1; i <= MaxLevel; i++)
        {
            if (safeExp >= GetRequiredExpForLevel(i))
            {
                resolvedLevel = i;
            }
        }

        return Mathf.Clamp(resolvedLevel, 1, MaxLevel);
    }

    // playerLevelStatsConfig가 Inspector에 등록이 안된 상태일 때 Resources에서 찾아서 붙여주는 에러 방지용
    public static PlayerLevelStatsConfig GetOrLoadDefault()
    {
        if (cachedDefault != null)
        {
            return cachedDefault;
        }

        cachedDefault = Resources.Load<PlayerLevelStatsConfig>(DefaultResourcePath);
        if (cachedDefault == null)
        {
            cachedDefault = CreateInstance<PlayerLevelStatsConfig>();
        }

        return cachedDefault;
    }

    private LevelStat GetStat(int level)
    {
        if (levels == null || levels.Length == 0)
        {
            return new LevelStat(0, 1f, 0f);
        }

        int index = Mathf.Clamp(level, 1, MaxLevel) - 1;
        return levels[index];
    }
}
