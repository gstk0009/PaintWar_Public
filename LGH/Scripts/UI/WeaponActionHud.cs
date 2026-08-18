using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 무기 상태를 표시합니다. 시각 계층, sprite, layout은 모두 씬에서 작성합니다.
/// 런타임 코드는 gameplay를 바인딩하고 플레이 중 실제로 바뀌는 값만 갱신합니다.
/// </summary>
public sealed class WeaponActionHud : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private WeaponRuntimeController controller;
    [SerializeField] private Button skillButton;
    [SerializeField] private GameObject skillLockedOverlay;
    [SerializeField] private TMP_Text skillLockTimerText;
    [SerializeField] private Image cooldownRing;
    [SerializeField] private TMP_Text bombMineCountText;
    [SerializeField] private TMP_Text weaponText;

    [Header("Runtime Binding")]
    [SerializeField, Min(0.1f)] private float controllerSearchInterval = 0.5f;

    private float nextControllerSearchTime;
    private int displayedMineCharges = int.MinValue;
    private WeaponData displayedWeapon;
    private bool? displayedInteractable;
    private bool? displayedLocked;
    private bool? displayedTimerVisible;
    private int displayedTimerTenths = int.MinValue;
    private int displayedSkillProgressPercent = int.MinValue;
    private bool showingEmptyState;
    private bool? displayedAiming;
    private bool? displayedCasting;

    public static WeaponActionHud FindExisting()
    {
        return FindFirstObjectByType<WeaponActionHud>();
    }

    private void Awake()
    {
        BindButton();
    }

    private void OnEnable()
    {
        TryBindLocalController();
    }

    private void OnDestroy()
    {
        if (skillButton != null)
        {
            skillButton.onClick.RemoveListener(UseSkill);
        }
    }

    public void Initialize(WeaponRuntimeController newController)
    {
        controller = newController;
        InvalidateDisplayCache();
    }

    private void Update()
    {
        if (controller == null)
        {
            TryBindLocalController();
        }

        if (controller == null)
        {
            SetEmptyState();
            return;
        }

        WeaponData weapon = controller.EquippedWeapon;
        float lockRemaining = controller.SkillLockRemaining;
        bool locked = lockRemaining > 0.05f;
        float cooldownRemaining = controller.GetCooldownRemaining();
        bool coolingDown = !locked && cooldownRemaining > 0.05f;
        bool showTimer = locked || coolingDown;
        int timerTenths = showTimer
            ? Mathf.CeilToInt((locked ? lockRemaining : cooldownRemaining) * 10f)
            : 0;
        bool aiming = controller.IsSkillAiming;
        bool casting = controller.IsSkillCasting;
        bool interactable = controller.CanActivateSkillButton;

        bool hasMineStatus = PlayerStoredItemController.TryGetLocalBombMineStatus(
            out int mineCharges,
            out _,
            out _,
            out _);
        if (!hasMineStatus)
        {
            mineCharges = 0;
        }

        float skillCooldownProgress = controller.GetCooldownProgress();
        int skillProgressPercent = Mathf.RoundToInt(skillCooldownProgress * 100f);

        if (bombMineCountText != null && displayedMineCharges != mineCharges)
        {
            bombMineCountText.text = mineCharges.ToString();
        }

        if (skillLockTimerText != null)
        {
            if (displayedTimerVisible != showTimer)
            {
                skillLockTimerText.gameObject.SetActive(showTimer);
            }

            if (showTimer &&
                (displayedTimerTenths != timerTenths ||
                 displayedLocked != locked))
            {
                skillLockTimerText.text = (timerTenths / 10f).ToString("0.0") + "s";
            }
        }

        if (weaponText != null &&
            (displayedWeapon != weapon ||
             displayedAiming != aiming ||
             displayedCasting != casting))
        {
            weaponText.text = casting
                ? "CASTING"
                : aiming
                    ? "CONFIRM"
                    : weapon != null ? weapon.DisplayName : "Skill";
        }

        if (cooldownRing != null)
        {
            if (displayedSkillProgressPercent != skillProgressPercent)
            {
                cooldownRing.fillAmount = skillCooldownProgress;
            }
        }

        if (skillButton != null && displayedInteractable != interactable)
        {
            skillButton.interactable = interactable;
        }

        if (skillLockedOverlay != null && displayedLocked != locked)
        {
            skillLockedOverlay.SetActive(locked);
        }

        displayedMineCharges = mineCharges;
        displayedWeapon = weapon;
        displayedInteractable = interactable;
        displayedLocked = locked;
        displayedTimerVisible = showTimer;
        displayedTimerTenths = timerTenths;
        displayedSkillProgressPercent = skillProgressPercent;
        displayedAiming = aiming;
        displayedCasting = casting;
        showingEmptyState = false;
    }

    private void UseSkill()
    {
        if (controller != null)
        {
            controller.HandleSkillButtonPressed();
        }
    }

    private void BindButton()
    {
        if (skillButton == null)
        {
            Debug.LogError("[WeaponActionHud] Skill Button is not assigned in the scene.", this);
            return;
        }

        skillButton.onClick.RemoveListener(UseSkill);
        skillButton.onClick.AddListener(UseSkill);
    }

    private void TryBindLocalController()
    {
        if (Time.unscaledTime < nextControllerSearchTime)
        {
            return;
        }

        nextControllerSearchTime = Time.unscaledTime + controllerSearchInterval;
        WeaponRuntimeController localController = WeaponRuntimeController.FindLocalPlayerController();
        if (localController != null)
        {
            Initialize(localController);
        }
    }

    private void SetEmptyState()
    {
        if (showingEmptyState)
        {
            return;
        }

        if (bombMineCountText != null) bombMineCountText.text = "0";
        if (weaponText != null) weaponText.text = "Skill";
        if (cooldownRing != null) cooldownRing.fillAmount = 0f;
        if (skillButton != null) skillButton.interactable = false;
        if (skillLockedOverlay != null) skillLockedOverlay.SetActive(false);
        if (skillLockTimerText != null) skillLockTimerText.gameObject.SetActive(false);

        displayedMineCharges = 0;
        displayedWeapon = null;
        displayedInteractable = false;
        displayedLocked = false;
        displayedTimerVisible = false;
        displayedTimerTenths = int.MinValue;
        displayedSkillProgressPercent = int.MinValue;
        displayedAiming = null;
        displayedCasting = null;
        showingEmptyState = true;
    }

    private void InvalidateDisplayCache()
    {
        displayedMineCharges = int.MinValue;
        displayedWeapon = null;
        displayedInteractable = null;
        displayedLocked = null;
        displayedTimerVisible = null;
        displayedTimerTenths = int.MinValue;
        displayedSkillProgressPercent = int.MinValue;
        displayedAiming = null;
        displayedCasting = null;
        showingEmptyState = false;
    }
}
