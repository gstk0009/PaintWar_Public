using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// local owner에게만 필요한 카메라, joystick, teleport 버튼 연결을 담당합니다.
// NetworkBehaviour가 아니므로 네트워크 RPC 선언과 로컬 UI 책임을 분리할 수 있습니다.
public sealed class PlayerLocalOwnerControls
{
    private readonly Transform target;
    private readonly Button configuredTeleportButton;
    private readonly Action teleportAction;
    private readonly Action<Joystick> joystickAssigned;

    private CancellationTokenSource initializeCts;
    private CameraFollow cachedCameraFollow;
    private Button boundTeleportButton;
    private TMP_Text boundTeleportCooldownText;
    private Image boundTeleportCooldownFill;
    private GameObject boundTeleportLockedOverlay;

    public Joystick Joystick { get; private set; }
    public bool IsInitialized { get; private set; }
    public Transform CameraTransform =>
        cachedCameraFollow != null ? cachedCameraFollow.transform : null;

    public PlayerLocalOwnerControls(
        Transform target,
        Button configuredTeleportButton,
        Action teleportAction,
        Action<Joystick> joystickAssigned)
    {
        this.target = target;
        this.configuredTeleportButton = configuredTeleportButton;
        this.teleportAction = teleportAction;
        this.joystickAssigned = joystickAssigned;
    }

    // local owner가 준비되면 카메라를 붙이고 조작 UI 연결 대기를 시작합니다.
    public void Initialize()
    {
        AttachCamera();
        IsInitialized = false;
        SetJoystick(null);

        CancelInitialize();
        initializeCts = new CancellationTokenSource();
        InitializeUIDelayedAsync(initializeCts.Token).Forget();
    }

    // ownership을 잃거나 client가 멈출 때 local UI 연결을 정리합니다.
    public void Clear()
    {
        CancelInitialize();
        UnbindTeleportButton();
        IsInitialized = false;
        SetJoystick(null);
    }

    public void AttachCamera()
    {
        CameraFollow follow = GetOrFindCameraFollow();
        if (follow != null)
        {
            follow.SetTarget(target, true);
        }
    }

    public void ClearCameraTarget()
    {
        CameraFollow follow = GetOrFindCameraFollow();
        if (follow != null)
        {
            follow.ClearTarget(target);
        }
    }

    public void PlaySpeedBurst(float intensity)
    {
        CameraFollow follow = GetOrFindCameraFollow();
        if (follow != null)
        {
            follow.PlaySpeedBurst(intensity);
        }
    }

    // scene 전환 직후 UI가 늦게 준비될 수 있어 joystick이 잡힐 때까지 UniTask로 짧게 재시도합니다.
    private async UniTaskVoid InitializeUIDelayedAsync(CancellationToken cancellationToken)
    {
        Joystick foundJoystick = null;

        try
        {
            while (foundJoystick == null)
            {
                if (!PlayerLocalInputView.TryGetJoystick(out foundJoystick))
                {
                    foundJoystick = Joystick.Instance;
                }

                if (foundJoystick == null)
                {
                    foundJoystick = UnityEngine.Object.FindFirstObjectByType<Joystick>();
                }

                if (foundJoystick == null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.1f), cancellationToken: cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SetJoystick(foundJoystick);
        BindTeleportButton();
        IsInitialized = true;
        CancelInitialize();
    }

    private void BindTeleportButton()
    {
        Button teleportButton = GetTeleportButton();
        if (teleportButton == null)
        {
            return;
        }

        if (boundTeleportButton == teleportButton)
        {
            return;
        }

        UnbindTeleportButton();
        boundTeleportButton = teleportButton;
        boundTeleportCooldownText = GetTeleportCooldownText(teleportButton);
        boundTeleportCooldownFill = GetTeleportCooldownFill(teleportButton);
        boundTeleportLockedOverlay = GetTeleportLockedOverlay();
        ConfigureTeleportCooldownFill();
        boundTeleportButton.onClick.RemoveListener(OnTeleportButtonClicked);
        boundTeleportButton.onClick.AddListener(OnTeleportButtonClicked);
        SetTeleportCooldown(0f, 0f, 1f, true, false);
    }

    private Button GetTeleportButton()
    {
        if (configuredTeleportButton != null)
        {
            return configuredTeleportButton;
        }

        if (PlayerLocalInputView.TryGetTeleportButton(out Button viewTeleportButton))
        {
            return viewTeleportButton;
        }

        GameObject teleportButtonObject = GameObject.Find("TeleportBtn");
        if (teleportButtonObject == null)
        {
            teleportButtonObject = GameObject.Find("RollBtn");
        }

        return teleportButtonObject != null
            ? teleportButtonObject.GetComponent<Button>()
            : null;
    }

    private TMP_Text GetTeleportCooldownText(Button teleportButton)
    {
        if (PlayerLocalInputView.TryGetTeleportCooldownText(out TMP_Text viewCooldownText))
        {
            return viewCooldownText;
        }

        return teleportButton != null
            ? teleportButton.GetComponentInChildren<TMP_Text>(true)
            : null;
    }

    private Image GetTeleportCooldownFill(Button teleportButton)
    {
        if (PlayerLocalInputView.TryGetTeleportCooldownFill(out Image viewCooldownFill))
        {
            return viewCooldownFill;
        }

        return teleportButton != null
            ? teleportButton.targetGraphic as Image
            : null;
    }

    private GameObject GetTeleportLockedOverlay()
    {
        PlayerLocalInputView.TryGetTeleportLockedOverlay(out GameObject overlay);
        return overlay;
    }

    private void ConfigureTeleportCooldownFill()
    {
        if (boundTeleportCooldownFill == null)
        {
            return;
        }

        boundTeleportCooldownFill.type = Image.Type.Filled;
        boundTeleportCooldownFill.fillMethod = Image.FillMethod.Radial360;
        boundTeleportCooldownFill.fillOrigin = (int)Image.Origin360.Top;
        boundTeleportCooldownFill.fillClockwise = true;
        boundTeleportCooldownFill.fillAmount = 1f;
    }

    private void UnbindTeleportButton()
    {
        if (boundTeleportButton == null)
        {
            return;
        }

        boundTeleportButton.onClick.RemoveListener(OnTeleportButtonClicked);
        if (boundTeleportCooldownText != null)
        {
            boundTeleportCooldownText.text = "";
        }

        if (boundTeleportCooldownFill != null)
        {
            boundTeleportCooldownFill.fillAmount = 1f;
        }

        if (boundTeleportLockedOverlay != null)
        {
            boundTeleportLockedOverlay.SetActive(false);
        }

        boundTeleportButton = null;
        boundTeleportCooldownText = null;
        boundTeleportCooldownFill = null;
        boundTeleportLockedOverlay = null;
    }

    public void SetTeleportCooldown(
        float remainingSeconds,
        float cooldownRemainingSeconds,
        float totalSeconds,
        bool ready,
        bool locked)
    {
        if (boundTeleportButton != null)
        {
            boundTeleportButton.interactable = ready;
        }

        if (boundTeleportLockedOverlay != null)
        {
            boundTeleportLockedOverlay.SetActive(locked);
        }

        if (boundTeleportCooldownFill != null)
        {
            float duration = Mathf.Max(0.01f, totalSeconds);
            bool cooldownReady = cooldownRemainingSeconds <= 0.05f;
            boundTeleportCooldownFill.fillAmount = cooldownReady
                ? 1f
                : Mathf.Clamp01(1f - (cooldownRemainingSeconds / duration));
        }

        if (boundTeleportCooldownText == null)
        {
            return;
        }

        int remainingTenths = Mathf.CeilToInt(remainingSeconds * 10f);
        boundTeleportCooldownText.text = ready
            ? ""
            : (remainingTenths / 10f).ToString("0.0") + "s";
    }

    private void OnTeleportButtonClicked()
    {
        teleportAction?.Invoke();
    }

    private CameraFollow GetOrFindCameraFollow()
    {
        if (cachedCameraFollow != null)
        {
            return cachedCameraFollow;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cachedCameraFollow = mainCamera.GetComponent<CameraFollow>();
            if (cachedCameraFollow == null)
            {
                cachedCameraFollow = mainCamera.gameObject.AddComponent<CameraFollow>();
            }
        }

        if (cachedCameraFollow == null)
        {
            cachedCameraFollow = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
        }

        return cachedCameraFollow;
    }

    private void SetJoystick(Joystick newJoystick)
    {
        Joystick = newJoystick;
        joystickAssigned?.Invoke(newJoystick);
    }

    private void CancelInitialize()
    {
        if (initializeCts == null)
        {
            return;
        }

        initializeCts.Cancel();
        initializeCts.Dispose();
        initializeCts = null;
    }
}
