using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbySkillSelectionUI : MonoBehaviour
{
    [SerializeField] private WeaponData[] skills;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button viewTabButton;
    [SerializeField] private Button rangeTabButton;
    [SerializeField] private Button replayPreviewButton;
    [SerializeField] private GameObject modalView;
    [SerializeField] private RectTransform panel;
    [SerializeField] private GameObject viewContent;
    [SerializeField] private GameObject rangeContent;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private Button[] skillButtons;
    [SerializeField] private GameObject[] selectedIndicators;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI[] levelPatternTexts;
    [SerializeField] private TextMeshProUGUI[] levelPatternFooterTexts;
    [SerializeField] private TextMeshProUGUI statusText;

    private readonly StringBuilder patternBuilder = new StringBuilder(256);
    private int previewIndex;
    private int selectedTab;
    private GameObject previewEffectInstance;

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (applyButton != null) applyButton.onClick.AddListener(ApplySelection);
        if (viewTabButton != null) viewTabButton.onClick.AddListener(ShowViewTab);
        if (rangeTabButton != null) rangeTabButton.onClick.AddListener(ShowRangeTab);
        if (replayPreviewButton != null) replayPreviewButton.onClick.AddListener(ReplayPreview);

        int buttonCount = skillButtons != null ? skillButtons.Length : 0;
        for (int i = 0; i < buttonCount; i++)
        {
            int capturedIndex = i;
            if (skillButtons[i] != null)
            {
                skillButtons[i].onClick.AddListener(() => SelectSkill(capturedIndex));
            }
        }

        ClosePanel();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnEnable()
    {
        LobbyButtonController.MatchmakingViewStateChanged += ApplyMatchmakingVisibility;
    }

    private void OnDisable()
    {
        LobbyButtonController.MatchmakingViewStateChanged -= ApplyMatchmakingVisibility;
    }

    private void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.RemoveListener(ClosePanel);
        if (applyButton != null) applyButton.onClick.RemoveListener(ApplySelection);
        if (viewTabButton != null) viewTabButton.onClick.RemoveListener(ShowViewTab);
        if (rangeTabButton != null) rangeTabButton.onClick.RemoveListener(ShowRangeTab);
        if (replayPreviewButton != null) replayPreviewButton.onClick.RemoveListener(ReplayPreview);
        if (skillButtons != null)
        {
            for (int i = 0; i < skillButtons.Length; i++)
            {
                if (skillButtons[i] != null) skillButtons[i].onClick.RemoveAllListeners();
            }
        }

        ClearPreviewEffect();
    }

    public void BindSceneReferences(
        WeaponData[] sceneSkills,
        Button sceneOpenButton,
        Button sceneCloseButton,
        Button sceneApplyButton,
        Button sceneViewTabButton,
        Button sceneRangeTabButton,
        Button sceneReplayPreviewButton,
        GameObject sceneModalView,
        RectTransform scenePanel,
        GameObject sceneViewContent,
        GameObject sceneRangeContent,
        Camera scenePreviewCamera,
        Transform scenePreviewAnchor,
        Button[] sceneSkillButtons,
        GameObject[] sceneSelectedIndicators,
        TextMeshProUGUI sceneTitleText,
        TextMeshProUGUI sceneDescriptionText,
        TextMeshProUGUI[] sceneLevelPatternTexts,
        TextMeshProUGUI[] sceneLevelPatternFooterTexts,
        TextMeshProUGUI sceneStatusText)
    {
        skills = sceneSkills;
        openButton = sceneOpenButton;
        closeButton = sceneCloseButton;
        applyButton = sceneApplyButton;
        viewTabButton = sceneViewTabButton;
        rangeTabButton = sceneRangeTabButton;
        replayPreviewButton = sceneReplayPreviewButton;
        modalView = sceneModalView;
        panel = scenePanel;
        viewContent = sceneViewContent;
        rangeContent = sceneRangeContent;
        previewCamera = scenePreviewCamera;
        previewAnchor = scenePreviewAnchor;
        skillButtons = sceneSkillButtons;
        selectedIndicators = sceneSelectedIndicators;
        titleText = sceneTitleText;
        descriptionText = sceneDescriptionText;
        levelPatternTexts = sceneLevelPatternTexts;
        levelPatternFooterTexts = sceneLevelPatternFooterTexts;
        statusText = sceneStatusText;
    }

    public void OpenPanel()
    {
        previewIndex = FindSkillIndex(PlayerSkillLoadoutStore.CurrentSkillId);
        if (modalView != null) modalView.SetActive(true);
        selectedTab = 0;
        RefreshTabVisibility();
        RefreshDetails();
        ReplayPreview();
    }

    public void ClosePanel()
    {
        if (modalView != null) modalView.SetActive(false);
        if (previewCamera != null) previewCamera.enabled = false;
        ClearPreviewEffect();
    }

    private void SelectSkill(int index)
    {
        if (skills == null || index < 0 || index >= skills.Length || skills[index] == null)
        {
            return;
        }

        previewIndex = index;
        RefreshDetails();
        if (selectedTab == 0)
        {
            ReplayPreview();
        }
    }

    private void ApplySelection()
    {
        WeaponData skill = GetPreviewSkill();
        if (skill == null)
        {
            return;
        }

        PlayerSkillLoadoutStore.Save(skill.WeaponId);
        RefreshEquippedStatus();
        RefreshIndicators();
    }

    private void RefreshDetails()
    {
        WeaponData skill = GetPreviewSkill();
        if (skill == null)
        {
            return;
        }

        if (titleText != null) titleText.text = skill.DisplayName;
        if (descriptionText != null) descriptionText.text = skill.Description;
        int[] levels = { 1, 3, 5 };
        int patternTextCount = levelPatternTexts != null ? levelPatternTexts.Length : 0;
        for (int i = 0; i < patternTextCount && i < levels.Length; i++)
        {
            if (levelPatternTexts[i] != null)
            {
                levelPatternTexts[i].text = BuildPatternGrid(skill, levels[i]);
            }
            if (levelPatternFooterTexts != null &&
                i < levelPatternFooterTexts.Length &&
                levelPatternFooterTexts[i] != null)
            {
                levelPatternFooterTexts[i].text = BuildPatternFooter(skill, levels[i]);
            }
        }

        RefreshEquippedStatus();
        RefreshIndicators();
    }

    public void ShowViewTab()
    {
        selectedTab = 0;
        RefreshTabVisibility();
        ReplayPreview();
    }

    public void ShowRangeTab()
    {
        selectedTab = 1;
        RefreshTabVisibility();
    }

    public void ReplayPreview()
    {
        ClearPreviewEffect();
        WeaponData skill = GetPreviewSkill();
        if (skill == null || skill.VfxProfile == null || previewAnchor == null)
        {
            return;
        }

        MagicWeaponVfxProfile profile = skill.VfxProfile;
        GameObject effectPrefab = profile.ImpactPrefab != null
            ? profile.ImpactPrefab
            : profile.CastMarkerPrefab != null
                ? profile.CastMarkerPrefab
                : profile.HitPrefab;
        if (effectPrefab == null)
        {
            return;
        }

        previewEffectInstance = Instantiate(effectPrefab, previewAnchor);
        previewEffectInstance.name = "SkillPreviewEffect";
        previewEffectInstance.transform.localPosition = Vector3.zero;
        previewEffectInstance.transform.localRotation = Quaternion.identity;
        previewEffectInstance.transform.localScale *= profile.ImpactScale;

        ParticleSystem[] particles = previewEffectInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
            main.simulationSpeed = 0.65f;
            particles[i].Play(true);
        }

        if (previewCamera != null && selectedTab == 0)
        {
            previewCamera.enabled = true;
        }
    }

    private void RefreshTabVisibility()
    {
        if (viewContent != null) viewContent.SetActive(selectedTab == 0);
        if (rangeContent != null) rangeContent.SetActive(selectedTab == 1);
        if (previewCamera != null) previewCamera.enabled = selectedTab == 0;

        if (viewTabButton != null) viewTabButton.interactable = selectedTab != 0;
        if (rangeTabButton != null) rangeTabButton.interactable = selectedTab != 1;

        if (selectedTab != 0)
        {
            ClearPreviewEffect();
        }
    }

    private void ClearPreviewEffect()
    {
        if (previewEffectInstance != null)
        {
            Destroy(previewEffectInstance);
            previewEffectInstance = null;
        }
    }

    private string BuildPatternGrid(WeaponData skill, int level)
    {
        string[] rows = skill.GetPatternRowsForLevel(level);
        patternBuilder.Clear();

        if (rows != null)
        {
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                string row = rows[rowIndex] ?? string.Empty;
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    char cell = row[columnIndex];
                    if (cell == '2')
                    {
                        patternBuilder.Append('P');
                    }
                    else if (cell == '1')
                    {
                        patternBuilder.Append('■');
                    }
                    else
                    {
                        patternBuilder.Append('□');
                    }

                    if (columnIndex < row.Length - 1)
                    {
                        patternBuilder.Append(' ');
                    }
                }

                if (rowIndex < rows.Length - 1) patternBuilder.Append('\n');
            }
        }

        return patternBuilder.ToString();
    }

    private string BuildPatternFooter(WeaponData skill, int level)
    {
        string[] rows = skill.GetPatternRowsForLevel(level);
        int tileCount = 0;
        int width = 0;
        if (rows != null)
        {
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                string row = rows[rowIndex] ?? string.Empty;
                width = Mathf.Max(width, row.Length);
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    if (row[columnIndex] == '1' || row[columnIndex] == '2') tileCount++;
                }
            }
        }

        return width + "x" + (rows != null ? rows.Length : 0) + "  /  " + tileCount + " TILES";
    }

    private void RefreshEquippedStatus()
    {
        if (statusText == null) return;

        string currentSkillId = PlayerSkillLoadoutStore.CurrentSkillId;
        string displayName = currentSkillId;
        if (skills != null)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && string.Equals(skills[i].WeaponId, currentSkillId, StringComparison.Ordinal))
                {
                    displayName = skills[i].DisplayName;
                    break;
                }
            }
        }

        statusText.text = "EQUIPPED SKILL\n" + displayName;
    }

    private void RefreshIndicators()
    {
        if (selectedIndicators == null)
        {
            return;
        }

        for (int i = 0; i < selectedIndicators.Length; i++)
        {
            if (selectedIndicators[i] != null)
            {
                selectedIndicators[i].SetActive(i == previewIndex);
            }
        }
    }

    private WeaponData GetPreviewSkill()
    {
        return skills != null && previewIndex >= 0 && previewIndex < skills.Length
            ? skills[previewIndex]
            : null;
    }

    private int FindSkillIndex(string skillId)
    {
        if (skills != null)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && string.Equals(
                        skills[i].WeaponId,
                        skillId,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return 0;
    }

    private void ApplyMatchmakingVisibility(bool matchmakingActive)
    {
        if (openButton != null) openButton.gameObject.SetActive(!matchmakingActive);
        if (matchmakingActive) ClosePanel();
    }
}
