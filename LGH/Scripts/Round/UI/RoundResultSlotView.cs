using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

// 4인 결과 보드의 한 칸입니다. 배경색, 이름, 캐릭터 낙하만 담당합니다.
[DisallowMultipleComponent]
public sealed class RoundResultSlotView : MonoBehaviour
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [SerializeField] private PaintOwner owner;
    [SerializeField] private Renderer backplateRenderer;
    [SerializeField] private Transform platform;
    [SerializeField] private RoundResultDisplayAvatar avatar;
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private Color survivorColor = new Color32(0xD7, 0x35, 0xE8, 0xFF);
    [SerializeField] private Color eliminatedColor = new Color32(0xE5, 0x24, 0x35, 0xFF);
    [SerializeField] private Color emptyColor = new Color32(0x29, 0x2E, 0x48, 0xFF);
    [SerializeField] private float fallDistance = 7f;
    [SerializeField, Min(0f)] private float eliminationReactionSeconds = 0.38f;
    [SerializeField, Min(0f)] private float fallAnticipationSeconds = 0.26f;
    [SerializeField, Min(0.1f)] private float fallDuration = 1.05f;

    private Vector3 avatarStartPosition;
    private Quaternion avatarStartRotation;
    private Vector3 platformStartPosition;
    private MaterialPropertyBlock materialBlock;

    public PaintOwner Owner { get { return owner; } }

    private void Awake()
    {
        materialBlock = new MaterialPropertyBlock();
        CacheAuthoredPose();
    }

    public void Prepare(GameParticipant participant, bool wasAlreadyEliminated)
    {
        CacheAuthoredPose();

        if (avatar != null)
        {
            avatar.Release();
            avatar.transform.localPosition = avatarStartPosition;
            avatar.transform.localRotation = avatarStartRotation;
            bool showLivePlayer =
                participant != null &&
                !wasAlreadyEliminated &&
                avatar.Bind(participant);
            avatar.gameObject.SetActive(showLivePlayer);
        }

        if (platform != null)
        {
            platform.localPosition = platformStartPosition;
            platform.gameObject.SetActive(participant != null && !wasAlreadyEliminated);
        }

        if (nameText != null)
        {
            nameText.text = participant != null ? participant.DisplayName : "EMPTY";
            nameText.color = wasAlreadyEliminated ? new Color(1f, 0.72f, 0.72f) : Color.white;
        }

        SetPlateColor(
            participant == null
                ? emptyColor
                : wasAlreadyEliminated
                    ? eliminatedColor
                    : survivorColor);
    }

    public void Release()
    {
        if (avatar != null)
        {
            avatar.Release();
        }
    }

    public void PlaySurvivorWinnerPose()
    {
        if (avatar != null && avatar.gameObject.activeSelf && avatar.HasLivePlayer)
        {
            avatar.PlayWinnerPose();
        }
    }

    public async UniTask PlayEliminationAsync(CancellationToken cancellationToken)
    {
        if (avatar == null || !avatar.gameObject.activeSelf)
        {
            SetPlateColor(eliminatedColor);
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            SetPlateColor(i % 2 == 0 ? eliminatedColor : Color.white);
            await UniTask.Delay(
                140,
                ignoreTimeScale: true,
                cancellationToken: cancellationToken);
        }

        SetPlateColor(eliminatedColor);
        if (nameText != null)
        {
            nameText.color = new Color(1f, 0.72f, 0.72f);
        }

        Vector3 avatarFrom = avatar.transform.localPosition;
        Quaternion rotationFrom = avatar.transform.localRotation;
        Vector3 platformFrom = platform != null ? platform.localPosition : Vector3.zero;

        if (eliminationReactionSeconds > 0f)
        {
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(eliminationReactionSeconds),
                ignoreTimeScale: true,
                cancellationToken: cancellationToken);
        }

        float anticipationElapsed = 0f;
        while (anticipationElapsed < fallAnticipationSeconds)
        {
            anticipationElapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(
                anticipationElapsed / Mathf.Max(0.01f, fallAnticipationSeconds));
            float pulse = Mathf.Sin(normalized * Mathf.PI);
            avatar.transform.localPosition = avatarFrom + Vector3.up * (0.12f * pulse);
            avatar.transform.localRotation =
                rotationFrom * Quaternion.Euler(0f, 0f, -8f * pulse);

            if (platform != null)
            {
                platform.localPosition = platformFrom + Vector3.down * (0.08f * pulse);
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        avatar.transform.localPosition = avatarFrom;
        avatar.transform.localRotation = rotationFrom;
        if (platform != null)
        {
            platform.localPosition = platformFrom;
        }

        // 낙하 애니메이션은 실제 위치 하강과 같은 프레임에 시작합니다.
        avatar.PlayEliminatedPose();
        float elapsed = 0f;

        while (elapsed < fallDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fallDuration));
            float fallProgress = Mathf.Pow(normalized, 1.65f);
            float turnProgress = Mathf.SmoothStep(0f, 1f, normalized);
            float hopProgress = Mathf.Clamp01(normalized / 0.22f);
            float hop = Mathf.Sin(hopProgress * Mathf.PI) * 0.14f;
            avatar.transform.localPosition =
                avatarFrom + Vector3.up * hop + Vector3.down * (fallDistance * fallProgress);
            avatar.transform.localRotation =
                rotationFrom * Quaternion.Euler(
                    24f * turnProgress,
                    0f,
                    105f * turnProgress);

            if (platform != null)
            {
                float platformProgress = Mathf.InverseLerp(0.12f, 1f, normalized);
                platform.localPosition =
                    platformFrom + Vector3.down *
                    (1.4f * Mathf.SmoothStep(0f, 1f, platformProgress));
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        avatar.gameObject.SetActive(false);
        if (platform != null)
        {
            platform.gameObject.SetActive(false);
        }
    }

    private void CacheAuthoredPose()
    {
        if (avatar != null)
        {
            avatarStartPosition = avatar.transform.localPosition;
            avatarStartRotation = avatar.transform.localRotation;
        }

        if (platform != null)
        {
            platformStartPosition = platform.localPosition;
        }
    }

    private void SetPlateColor(Color color)
    {
        if (backplateRenderer == null)
        {
            return;
        }

        if (materialBlock == null)
        {
            materialBlock = new MaterialPropertyBlock();
        }

        backplateRenderer.GetPropertyBlock(materialBlock);
        materialBlock.SetColor(BaseColorProperty, color);
        materialBlock.SetColor(ColorProperty, color);
        backplateRenderer.SetPropertyBlock(materialBlock);
    }
}
