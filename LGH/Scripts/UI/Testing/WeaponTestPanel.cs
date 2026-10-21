using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WeaponTestPanel : MonoBehaviour
{
    [SerializeField] private WeaponRuntimeController controller;
    [SerializeField] private TMP_Text cooldownText;
    [SerializeField] private PlayerLevelStats levelStats;
    [SerializeField] private Button[] weaponButtons;
    [SerializeField] private Button[] ownerButtons;
    [SerializeField] private WeaponActionHud actionHud;

    private float nextControllerSearchTime;

    private void OnEnable()
    {
        TryBindLocalController();
    }

    public void InitializePanel(WeaponRuntimeController activeController)
    {
        controller = activeController;

        if (controller != null)
        {
            levelStats = controller.LevelStats;
        }

        if (levelStats == null)
        {
            levelStats = Object.FindFirstObjectByType<PlayerLevelStats>();
        }

        BindWeaponButtons();
        BindOwnerButtons();
        BindActionHud();
        RefreshStaticLabels();

    }

    private void Update()
    {
        if (controller == null)
        {
            TryBindLocalController();
        }

        if (controller == null || cooldownText == null)
        {
            return;
        }

        string weaponName = controller.EquippedWeapon != null ? controller.EquippedWeapon.DisplayName : "None";
        bool ready = controller.IsSkillReady;
        float remaining = controller.GetCooldownRemaining();
        string cooldownLine = ready
            ? weaponName + " Ready"
            : weaponName + " Cooldown " + remaining.ToString("0.0") + "s";
        cooldownText.text = cooldownLine + GetLevelLine();
    }

    private string GetLevelLine()
    {
        if (levelStats == null)
        {
            return string.Empty;
        }

        string expText = levelStats.IsMaxLevel
            ? levelStats.CurrentExp + " MAX"
            : levelStats.CurrentExp + " / " + levelStats.NextLevelRequiredExp;
        return "\nLv " + levelStats.Level + "  EXP " + expText;
    }

    public void EquipWeapon(int index)
    {
        if (controller != null)
        {
            controller.Equip(index);
        }
    }

    public void SelectOwner(int ownerIndex)
    {
        if (controller != null)
        {
            controller.SetOwner((PaintOwner)Mathf.Clamp(ownerIndex, 1, 4));
        }
    }

    private void BindWeaponButtons()
    {
        if (weaponButtons == null) return;

        for (int i = 0; i < weaponButtons.Length; i++)
        {
            int index = i;
            if (weaponButtons[i] != null)
            {
                weaponButtons[i].onClick.RemoveAllListeners();
                weaponButtons[i].onClick.AddListener(() => EquipWeapon(index));
            }
        }
    }

    private void BindOwnerButtons()
    {
        if (ownerButtons == null) return;

        for (int i = 0; i < ownerButtons.Length; i++)
        {
            int ownerIndex = i + 1;
            if (ownerButtons[i] != null)
            {
                ownerButtons[i].onClick.RemoveAllListeners();
                ownerButtons[i].onClick.AddListener(() => SelectOwner(ownerIndex));
            }
        }
    }

    private void TryBindLocalController()
    {
        if (Time.unscaledTime < nextControllerSearchTime)
        {
            return;
        }

        nextControllerSearchTime = Time.unscaledTime + 0.25f;
        WeaponRuntimeController localController = WeaponRuntimeController.FindLocalPlayerController();
        if (localController != null)
        {
            InitializePanel(localController);
        }
    }

    private void BindActionHud()
    {
        if (actionHud == null)
        {
            actionHud = WeaponActionHud.FindExisting();
        }

        if (actionHud != null)
        {
            actionHud.Initialize(controller);
        }
    }

    private void RefreshStaticLabels()
    {
        if (controller != null && weaponButtons != null)
        {
            for (int i = 0; i < weaponButtons.Length; i++)
            {
                if (weaponButtons[i] == null || i >= controller.Weapons.Count)
                {
                    continue;
                }

                TMP_Text label = weaponButtons[i].GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = controller.Weapons[i].DisplayName;
                }
            }
        }

        if (ownerButtons == null) return;

        for (int i = 0; i < ownerButtons.Length; i++)
        {
            if (ownerButtons[i] == null) continue;

            PaintOwner owner = (PaintOwner)(i + 1);
            Image image = ownerButtons[i].GetComponent<Image>();
            if (image != null)
            {
                image.color = GetOwnerColor(owner);
            }

            TMP_Text label = ownerButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = "P" + (i + 1);
                label.color = owner == PaintOwner.Player3 ? Color.black : Color.white;
            }
        }
    }

    private static Color GetOwnerColor(PaintOwner owner)
    {
        switch (owner)
        {
            case PaintOwner.Player1: return new Color32(0xE2, 0x4B, 0x4A, 0xFF);
            case PaintOwner.Player2: return new Color32(0x37, 0x8A, 0xDD, 0xFF);
            case PaintOwner.Player3: return new Color32(0xEF, 0x9F, 0x27, 0xFF);
            case PaintOwner.Player4: return new Color32(0x1D, 0x9E, 0x75, 0xFF);
            default: return Color.white;
        }
    }
}
