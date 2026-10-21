using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Local-player level and experience display.
public sealed class PlayerStatusHud : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Image experienceFill;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text experienceText;

    [Header("Runtime Binding")]
    [SerializeField, Min(0.1f)] private float bindRetryInterval = 0.5f;

    private WeaponRuntimeController weaponController;
    private PlayerLevelStats levelStats;
    private float nextBindTime;
    private int displayedLevel = -1;
    private int displayedExperience = -1;
    private int displayedNextExperience = -1;

    private void Update()
    {
        if (weaponController == null || levelStats == null)
        {
            TryBindLocalPlayer();
        }

        RefreshLevelAndExperience();
    }

    private void TryBindLocalPlayer()
    {
        if (Time.unscaledTime < nextBindTime) return;
        nextBindTime = Time.unscaledTime + bindRetryInterval;

        weaponController = WeaponRuntimeController.FindLocalPlayerController();
        if (weaponController != null)
        {
            levelStats = weaponController.GetComponent<PlayerLevelStats>();
            displayedLevel = -1;
        }
    }

    private void RefreshLevelAndExperience()
    {
        int level = levelStats != null ? levelStats.Level : 1;
        int current = levelStats != null ? levelStats.CurrentExp : 0;
        int levelStart = levelStats != null
            ? levelStats.StatsConfig.GetRequiredExpForLevel(level)
            : 0;
        int next = levelStats != null ? levelStats.NextLevelRequiredExp : 0;
        bool maxLevel = levelStats != null && levelStats.IsMaxLevel;

        if (levelText != null && displayedLevel != level)
        {
            levelText.text = "LV. " + level;
        }

        if (experienceText != null &&
            (displayedExperience != current || displayedNextExperience != next))
        {
            experienceText.text = maxLevel ? "MAX" : current + " / " + next + " EXP";
        }

        if (experienceFill != null &&
            (displayedLevel != level ||
             displayedExperience != current ||
             displayedNextExperience != next))
        {
            experienceFill.fillAmount = maxLevel
                ? 1f
                : Mathf.InverseLerp(levelStart, Mathf.Max(levelStart + 1, next), current);
        }

        displayedLevel = level;
        displayedExperience = current;
        displayedNextExperience = next;
    }
}
