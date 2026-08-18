using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬에 배치된 로컬 입력 UI 참조를 보관합니다.
/// 네트워크로 spawn되는 player prefab은 씬 오브젝트를 직접 serialize할 수 없어서 이 view를 먼저 조회합니다.
/// </summary>
public sealed class PlayerLocalInputView : MonoBehaviour
{
    public static PlayerLocalInputView Instance { get; private set; }

    // GameScene Inspector에서 직접 연결해두는 로컬 조작 UI입니다.
    [SerializeField] private Joystick joystick;
    [SerializeField] private Button teleportButton;
    [SerializeField] private TMP_Text teleportCooldownText;
    [SerializeField] private Image teleportCooldownFill;
    [SerializeField] private GameObject teleportLockedOverlay;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static bool TryGetJoystick(out Joystick result)
    {
        // 씬 참조가 있으면 이름 검색보다 이 경로를 우선 사용합니다.
        result = Instance != null ? Instance.joystick : null;
        return result != null;
    }

    public static bool TryGetTeleportButton(out Button result)
    {
        // PlayerMovement prefab에는 scene Button을 직접 물릴 수 없어 view를 통해 가져옵니다.
        result = Instance != null ? Instance.teleportButton : null;
        return result != null;
    }

    public static bool TryGetTeleportCooldownText(out TMP_Text result)
    {
        // 버튼의 기존 TMP 텍스트를 쿨타임 표시용으로 재사용합니다.
        result = Instance != null ? Instance.teleportCooldownText : null;
        return result != null;
    }

    public static bool TryGetTeleportCooldownFill(out Image result)
    {
        // 버튼의 Image를 원형 fill 게이지로 사용할 수 있게 제공합니다.
        result = Instance != null ? Instance.teleportCooldownFill : null;
        return result != null;
    }

    public static bool TryGetTeleportLockedOverlay(out GameObject result)
    {
        result = Instance != null ? Instance.teleportLockedOverlay : null;
        return result != null;
    }
}
