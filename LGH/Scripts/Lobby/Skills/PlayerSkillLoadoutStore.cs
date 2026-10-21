using System;
using UnityEngine;

public static class PlayerSkillLoadoutStore
{
    public const string DefaultSkillId = "meteor_cross";

    private const string SkillKeyPrefix = "PaintWar.Skill.Selected.";
    private const string FallbackSkillKey = "PaintWar.Skill.Selected.Local";

    private static readonly string[] SupportedSkillIds =
    {
        "meteor_cross",
        "fire_surge",
        "arcane_lance",
        "frost_wall",
        "lightning_chain",
        "crystal_bloom",
        "ground_rupture",
        "red_energy_burst"
    };

    public static event Action<string> SelectedChanged;

    public static string CurrentSkillId => NormalizeSkillId(
        PlayerPrefs.GetString(GetCurrentSkillKey(), DefaultSkillId));

    public static void Save(string skillId)
    {
        string normalizedId = NormalizeSkillId(skillId);
        PlayerPrefs.SetString(GetCurrentSkillKey(), normalizedId);
        PlayerPrefs.Save();
        FirebasePlayerProfileStore.SaveSelectedSkillId(normalizedId);
        SelectedChanged?.Invoke(normalizedId);
        LobbyButtonController.SendCurrentPlayerProfile();
    }

    public static void ApplyCloudSkillId(string skillId)
    {
        string normalizedId = NormalizeSkillId(skillId);
        PlayerPrefs.SetString(GetCurrentSkillKey(), normalizedId);
        PlayerPrefs.Save();
        SelectedChanged?.Invoke(normalizedId);
    }

    public static string NormalizeSkillId(string skillId)
    {
        if (!string.IsNullOrWhiteSpace(skillId))
        {
            string candidate = skillId.Trim();
            for (int i = 0; i < SupportedSkillIds.Length; i++)
            {
                if (string.Equals(candidate, SupportedSkillIds[i], StringComparison.Ordinal))
                {
                    return SupportedSkillIds[i];
                }
            }
        }

        return DefaultSkillId;
    }

    public static bool IsSupported(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        string candidate = skillId.Trim();
        for (int i = 0; i < SupportedSkillIds.Length; i++)
        {
            if (string.Equals(candidate, SupportedSkillIds[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCurrentSkillKey()
    {
        string userId = PlayerProfile.GetSavedUserId();
        return string.IsNullOrWhiteSpace(userId)
            ? FallbackSkillKey
            : SkillKeyPrefix + userId.Trim();
    }
}
