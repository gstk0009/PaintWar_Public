using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Editor-authored SkillTestScene에서 실제 WeaponData/VFX Profile을 미리 봅니다.
// UI와 타일은 씬에 저장되며 런타임에는 데이터와 버튼 상태만 갱신합니다.
[DisallowMultipleComponent]
public sealed class SkillVfxTestController : MonoBehaviour
{
    [Header("Preview Runtime")]
    [SerializeField] private TilePaintManager tileManager;
    [SerializeField] private Renderer[] tileRenderers;
    [SerializeField, Min(1)] private int gridSize = 11;
    [SerializeField, Min(0.1f)] private float tileSize = 1f;
    [SerializeField] private Vector3 gridOrigin = new Vector3(-5f, 0f, -5f);
    [SerializeField] private WeaponRuntimeController weaponController;
    [SerializeField] private PlayerLevelStats levelStats;

    [Header("Authored Controls")]
    [SerializeField] private Button[] weaponButtons;
    [SerializeField] private Button[] levelButtons;
    [SerializeField] private int[] levelValues = { 1, 3, 5 };
    [SerializeField] private Button[] ownerButtons;
    [SerializeField] private Button previewButton;
    [SerializeField] private Button selectProfileButton;
    [SerializeField] private Button resetTilesButton;
    [SerializeField] private TMP_Text statusText;

    private UnityAction[] weaponActions;
    private UnityAction[] levelActions;
    private UnityAction[] ownerActions;
    private int selectedWeaponIndex;
    private int selectedLevel = 1;
    private PaintOwner selectedOwner = PaintOwner.Player1;

    private void Awake()
    {
        PrepareGrid();
        BindControls();
        SelectWeapon(0);
        SelectLevel(1);
        SelectOwner(PaintOwner.Player1);
    }

    private void OnDestroy()
    {
        UnbindControls();
    }

    public void SelectWeapon(int index)
    {
        if (weaponController == null ||
            index < 0 ||
            index >= weaponController.Weapons.Count)
        {
            return;
        }

        selectedWeaponIndex = index;
        weaponController.Equip(index);
        RefreshButtonStates();
        RefreshStatus("READY");
    }

    public void SelectLevel(int level)
    {
        selectedLevel = level >= 5 ? 5 : level >= 3 ? 3 : 1;
        if (levelStats != null)
        {
            int requiredExp =
                levelStats.StatsConfig.GetRequiredExpForLevel(selectedLevel);
            levelStats.ApplyNetworkSnapshot(requiredExp, 0, 0f);
        }

        RefreshButtonStates();
        RefreshStatus("READY");
    }

    public void SelectOwner(PaintOwner owner)
    {
        selectedOwner = owner < PaintOwner.Player1 || owner > PaintOwner.Player4
            ? PaintOwner.Player1
            : owner;
        if (weaponController != null)
        {
            weaponController.SetOwner(selectedOwner);
        }

        RefreshButtonStates();
        RefreshStatus("READY");
    }

    public void PreviewSelectedSkill()
    {
        if (weaponController == null)
        {
            RefreshStatus("NO CONTROLLER");
            return;
        }

        // 네트워크 권한과 쿨다운 없이 실제 배치 VFX 생성 경로만 반복 재생합니다.
        weaponController.Equip(selectedWeaponIndex);
        bool started = weaponController.PreviewEquippedWeaponVisual(selectedLevel);
        RefreshStatus(started ? "PREVIEWING" : "PREVIEW FAILED");
    }

    public void ResetTiles()
    {
        if (tileManager != null)
        {
            tileManager.ResetAllTiles();
        }

        RefreshStatus("GRID RESET");
    }

    public void SelectCurrentVfxProfileAsset()
    {
#if UNITY_EDITOR
        WeaponData weapon = GetSelectedWeapon();
        Object asset = weapon != null && weapon.VfxProfile != null
            ? weapon.VfxProfile
            : weapon;
        if (asset != null)
        {
            UnityEditor.Selection.activeObject = asset;
            UnityEditor.EditorGUIUtility.PingObject(asset);
            RefreshStatus("VFX SO SELECTED");
        }
#else
        RefreshStatus("EDITOR ONLY");
#endif
    }

    private void PrepareGrid()
    {
        if (tileManager == null)
        {
            return;
        }

        tileManager.PrepareGrid(gridSize, tileSize, gridOrigin);
        int expectedCount = gridSize * gridSize;
        int count = tileRenderers != null
            ? Mathf.Min(expectedCount, tileRenderers.Length)
            : 0;
        for (int i = 0; i < count; i++)
        {
            tileManager.RegisterTileRenderer(
                i / gridSize,
                i % gridSize,
                tileRenderers[i]);
        }
    }

    private void BindControls()
    {
        if (weaponButtons != null)
        {
            weaponActions = new UnityAction[weaponButtons.Length];
            for (int i = 0; i < weaponButtons.Length; i++)
            {
                int index = i;
                weaponActions[i] = () => SelectWeapon(index);
                if (weaponButtons[i] != null)
                {
                    weaponButtons[i].onClick.AddListener(weaponActions[i]);
                }
            }
        }

        if (levelButtons != null)
        {
            levelActions = new UnityAction[levelButtons.Length];
            for (int i = 0; i < levelButtons.Length; i++)
            {
                int level = levelValues != null && i < levelValues.Length
                    ? levelValues[i]
                    : 1;
                levelActions[i] = () => SelectLevel(level);
                if (levelButtons[i] != null)
                {
                    levelButtons[i].onClick.AddListener(levelActions[i]);
                }
            }
        }

        if (ownerButtons != null)
        {
            ownerActions = new UnityAction[ownerButtons.Length];
            for (int i = 0; i < ownerButtons.Length; i++)
            {
                PaintOwner owner = (PaintOwner)(i + 1);
                ownerActions[i] = () => SelectOwner(owner);
                if (ownerButtons[i] != null)
                {
                    ownerButtons[i].onClick.AddListener(ownerActions[i]);
                }
            }
        }

        if (previewButton != null)
        {
            previewButton.onClick.AddListener(PreviewSelectedSkill);
        }

        if (selectProfileButton != null)
        {
            selectProfileButton.onClick.AddListener(SelectCurrentVfxProfileAsset);
        }

        if (resetTilesButton != null)
        {
            resetTilesButton.onClick.AddListener(ResetTiles);
        }
    }

    private void UnbindControls()
    {
        RemoveActions(weaponButtons, weaponActions);
        RemoveActions(levelButtons, levelActions);
        RemoveActions(ownerButtons, ownerActions);

        if (previewButton != null)
        {
            previewButton.onClick.RemoveListener(PreviewSelectedSkill);
        }

        if (selectProfileButton != null)
        {
            selectProfileButton.onClick.RemoveListener(SelectCurrentVfxProfileAsset);
        }

        if (resetTilesButton != null)
        {
            resetTilesButton.onClick.RemoveListener(ResetTiles);
        }
    }

    private void RefreshButtonStates()
    {
        SetSelectedButton(weaponButtons, selectedWeaponIndex);

        int selectedLevelIndex = -1;
        if (levelValues != null)
        {
            for (int i = 0; i < levelValues.Length; i++)
            {
                if (levelValues[i] == selectedLevel)
                {
                    selectedLevelIndex = i;
                    break;
                }
            }
        }

        SetSelectedButton(levelButtons, selectedLevelIndex);
        SetSelectedButton(ownerButtons, (int)selectedOwner - 1);
    }

    private void RefreshStatus(string state)
    {
        if (statusText == null)
        {
            return;
        }

        WeaponData weapon = GetSelectedWeapon();
        MagicWeaponVfxProfile profile = weapon != null ? weapon.VfxProfile : null;
        string weaponName = weapon != null ? weapon.DisplayName : "NONE";
        string profileName = profile != null ? profile.name : "NO VFX PROFILE";
        string scaleSummary = profile != null
            ? "Impact " + profile.ImpactScale.ToString("0.##") +
              "  Lv3 x" + profile.ImpactLevel3ScaleMultiplier.ToString("0.##") +
              "  Lv5 x" + profile.ImpactLevel5ScaleMultiplier.ToString("0.##") +
              "  Y " + profile.ImpactSurfaceOffset.ToString("0.##") + "\n" +
              "Owner Tint " + profile.OwnerTintAmount.ToString("0.##") +
              "  Boost " + profile.OwnerTintVisibilityBoost.ToString("0.##") +
              "  Max " + profile.EffectTintMaxBrightness.ToString("0.##")
            : string.Empty;

        statusText.text =
            state + "\n" +
            weaponName + "  |  LV." + selectedLevel + "  |  " + selectedOwner + "\n" +
            profileName + "\n" +
            scaleSummary;
    }

    private WeaponData GetSelectedWeapon()
    {
        return weaponController != null &&
               selectedWeaponIndex >= 0 &&
               selectedWeaponIndex < weaponController.Weapons.Count
            ? weaponController.Weapons[selectedWeaponIndex]
            : null;
    }

    private static void SetSelectedButton(Button[] buttons, int selectedIndex)
    {
        if (buttons == null)
        {
            return;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
            {
                buttons[i].interactable = i != selectedIndex;
            }
        }
    }

    private static void RemoveActions(Button[] buttons, UnityAction[] actions)
    {
        if (buttons == null || actions == null)
        {
            return;
        }

        int count = Mathf.Min(buttons.Length, actions.Length);
        for (int i = 0; i < count; i++)
        {
            if (buttons[i] != null && actions[i] != null)
            {
                buttons[i].onClick.RemoveListener(actions[i]);
            }
        }
    }
}
