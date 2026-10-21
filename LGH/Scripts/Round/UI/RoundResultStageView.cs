using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// GameScene에 저장된 3D 라운드 결과 무대를 재생합니다.
// 런타임에는 고정된 씬 오브젝트의 표시 값과 활성 상태만 변경합니다.
[DisallowMultipleComponent]
public sealed class RoundResultStageView : MonoBehaviour
{
    [Header("Authored Scene Roots")]
    [SerializeField] private GameObject stageVisualRoot;
    [SerializeField] private GameObject roundBoardRoot;
    [SerializeField] private GameObject championRoot;
    [SerializeField] private Canvas resultCanvas;
    [SerializeField] private CanvasGroup resultCanvasGroup;
    [SerializeField] private GraphicRaycaster resultRaycaster;
    [SerializeField] private Image blackoutImage;

    [Header("Cameras and Existing HUD")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Camera resultCamera;
    [SerializeField] private Canvas[] gameplayCanvases;
    [SerializeField] private Transform roundCameraPose;
    [SerializeField] private Transform championCameraStartPose;
    [SerializeField] private Transform championCameraEndPose;

    [Header("Board")]
    [SerializeField] private RoundResultSlotView[] slots;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Button finalExitButton;
    [SerializeField] private CanvasGroup finalExitButtonGroup;
    [SerializeField] private TextMeshProUGUI finalExitButtonLabel;

    [Header("Champion")]
    [SerializeField] private RoundResultDisplayAvatar championAvatar;
    [SerializeField] private Transform championPodium;
    [SerializeField] private ParticleSystem championConfetti;

    [Header("Timing")]
    [SerializeField] private float eliminationCueDelay = 0.45f;
    [SerializeField, Min(0f)] private float cameraCutBlackHoldSeconds = 0.1f;
    [SerializeField] private float championCameraMoveSeconds = 1.1f;
    [SerializeField, Min(1f)] private float finalExitAutoReturnSeconds = 30f;

    private bool presentationActive;
    private bool finalResultActive;
    private bool gameplayCameraWasEnabled;
    private bool[] gameplayCanvasWasEnabled;
    private Vector3 championAvatarStartPosition;
    private Quaternion championAvatarStartRotation;
    private int finalExitCountdownVersion;

    public bool IsPresentationActive { get { return presentationActive; } }
    public bool IsFinalResultActive { get { return finalResultActive; } }

    private void Awake()
    {
        if (!HasRequiredReferences())
        {
            Debug.LogError(
                "[RoundResultStageView] Scene references are missing. " +
                "Run PaintWar/UI/Create Or Rebuild 3D Round Result Stage.",
                this);
            enabled = false;
            return;
        }

        championAvatarStartPosition = championAvatar.transform.localPosition;
        championAvatarStartRotation = championAvatar.transform.localRotation;
        finalExitButton.onClick.RemoveListener(OnFinalExitClicked);
        finalExitButton.onClick.AddListener(OnFinalExitClicked);
        finalExitButton.transform.SetAsLastSibling();
        HideImmediate();
    }

    public async UniTask PlayRoundEndAsync(
        int roundIndex,
        IReadOnlyList<GameParticipant> participants,
        GameParticipant eliminated,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        titleText.text = "ROUND " + roundIndex + " RESULTS";
        detailText.text = "WHO WILL SURVIVE?";
        finalResultActive = false;
        SetFinalExitVisible(false);

        await EnterPresentationAsync(
            false,
            roundCameraPose,
            () => PrepareBoard(participants, eliminated),
            fadeSeconds,
            cancellationToken);
        await UniTask.Delay(
            TimeSpan.FromSeconds(eliminationCueDelay),
            ignoreTimeScale: true,
            cancellationToken: cancellationToken);

        RoundResultSlotView eliminatedSlot =
            eliminated != null ? FindSlot(eliminated.Owner) : null;
        if (eliminatedSlot != null)
        {
            await eliminatedSlot.PlayEliminationAsync(cancellationToken);
        }

        PlayVisibleSurvivorWinnerPoses();

        int survivorCount = CountSurvivorsAfterResult(participants, eliminated);
        detailText.text = survivorCount + (survivorCount == 1 ? " SURVIVOR" : " SURVIVORS");
        await UniTask.Delay(
            TimeSpan.FromSeconds(Mathf.Max(0.1f, holdSeconds)),
            ignoreTimeScale: true,
            cancellationToken: cancellationToken);
    }

    public async UniTask PlayRankingRevealAsync(
        IReadOnlyList<GameParticipant> participants,
        IReadOnlyList<GameParticipant> rankedEliminated,
        float fadeSeconds,
        CancellationToken cancellationToken)
    {
        titleText.text = "FINAL RESULTS";
        detailText.text = "RANKING REVEAL";
        finalResultActive = false;
        SetFinalExitVisible(false);

        await EnterPresentationAsync(
            false,
            roundCameraPose,
            () => PrepareBoard(participants, null),
            fadeSeconds,
            cancellationToken);

        if (rankedEliminated == null)
        {
            return;
        }

        for (int i = 0; i < rankedEliminated.Count; i++)
        {
            GameParticipant eliminated = rankedEliminated[i];
            int displayedRank = rankedEliminated.Count - i + 1;
            detailText.text = GetOrdinal(displayedRank) + " PLACE  " +
                (eliminated != null ? eliminated.DisplayName : "PLAYER");

            await UniTask.Delay(
                TimeSpan.FromSeconds(eliminationCueDelay),
                ignoreTimeScale: true,
                cancellationToken: cancellationToken);

            RoundResultSlotView eliminatedSlot =
                eliminated != null ? FindSlot(eliminated.Owner) : null;
            if (eliminatedSlot != null)
            {
                await eliminatedSlot.PlayEliminationAsync(cancellationToken);
            }


            PlayVisibleSurvivorWinnerPoses();

        }
    }

    public async UniTask PlayRoundStartAsync(
        int roundIndex,
        float holdSeconds,
        float fadeSeconds,
        float blackHoldSeconds,
        Func<CancellationToken, UniTask> onGameplayCovered,
        CancellationToken cancellationToken)
    {
        if (!presentationActive)
        {
            ShowPresentation(false);
            ApplyCameraPose(roundCameraPose);
            await FadeBlackAsync(
                1f,
                0f,
                Mathf.Min(0.2f, Mathf.Max(0f, fadeSeconds)),
                cancellationToken);
        }

        titleText.text = roundIndex >= 3 ? "FINAL ROUND START" : "ROUND " + roundIndex + " START";
        detailText.text = "GET READY!";
        await UniTask.Delay(
            TimeSpan.FromSeconds(Mathf.Max(0.35f, holdSeconds)),
            ignoreTimeScale: true,
            cancellationToken: cancellationToken);
        await ExitPresentationAsync(
            fadeSeconds,
            blackHoldSeconds,
            onGameplayCovered,
            cancellationToken);
    }

    public async UniTask PlayFinalResultAsync(
        GameParticipant winner,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        championAvatar.transform.localPosition = championAvatarStartPosition;
        championAvatar.transform.localRotation = championAvatarStartRotation;
        finalResultActive = true;
        SetFinalExitVisible(false);

        titleText.text = "CHAMPION";
        detailText.text = winner != null ? winner.DisplayName : "WINNER";

        if (championConfetti != null)
        {
            championConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        await EnterPresentationAsync(
            true,
            championCameraStartPose,
            () =>
            {
                // 순위 보드에서 사용한 실제 플레이어를 먼저 원래 상태로 돌린 뒤 우승 무대에 다시 바인딩합니다.
                ReleasePresentedPlayers();
                championAvatar.Bind(winner);
                championAvatar.PlayWinnerPose();
            },
            fadeSeconds,
            cancellationToken);
        resultCanvasGroup.interactable = true;
        resultCanvasGroup.blocksRaycasts = true;
        SetFinalExitVisible(true);
        StartFinalExitCountdown();
        if (championConfetti != null)
        {
            championConfetti.Play(true);
        }

        await AnimateChampionCameraAsync(cancellationToken);
        await UniTask.Delay(
            TimeSpan.FromSeconds(Mathf.Max(0.5f, holdSeconds)),
            ignoreTimeScale: true,
            cancellationToken: cancellationToken);
    }

    public void HideImmediate()
    {
        ReleasePresentedPlayers();
        presentationActive = false;
        finalResultActive = false;
        finalExitCountdownVersion++;
        SetFinalExitVisible(false);

        if (stageVisualRoot != null)
        {
            stageVisualRoot.SetActive(false);
        }

        if (resultCamera != null)
        {
            resultCamera.enabled = false;
        }

        if (resultCanvas != null)
        {
            // 비활성 Canvas로 시작하면 일부 Unity 버전에서 Graphic depth가 -1로
            // 남아 처음 표시한 버튼이 Raycast 대상에 등록되지 않습니다.
            resultCanvas.enabled = true;
        }

        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = 0f;
            resultCanvasGroup.interactable = false;
            resultCanvasGroup.blocksRaycasts = false;
        }

        if (gameplayCamera != null)
        {
            gameplayCamera.enabled = true;
        }

        RestoreGameplayCanvasStates();
    }

    private void ShowPresentation(bool championMode)
    {
        if (!presentationActive)
        {
            gameplayCameraWasEnabled = gameplayCamera.enabled;
            CaptureGameplayCanvasStates();
        }

        presentationActive = true;
        stageVisualRoot.SetActive(true);
        roundBoardRoot.SetActive(!championMode);
        championRoot.SetActive(championMode);
        resultCanvas.enabled = true;
        resultCanvasGroup.alpha = 1f;
        resultCanvasGroup.interactable = false;
        resultCanvasGroup.blocksRaycasts = true;
        SetBlackoutAlpha(1f);
        SetGameplayCanvasesEnabled(false);
        gameplayCamera.enabled = false;
        resultCamera.enabled = true;
    }

    private async UniTask EnterPresentationAsync(
        bool championMode,
        Transform cameraPose,
        Action onStageActivated,
        float fadeSeconds,
        CancellationToken cancellationToken)
    {
        bool wasPresentationActive = presentationActive;
        if (!wasPresentationActive)
        {
            gameplayCameraWasEnabled = gameplayCamera.enabled;
            CaptureGameplayCanvasStates();

            stageVisualRoot.SetActive(false);
            resultCamera.enabled = false;
        }

        presentationActive = true;
        resultCanvas.enabled = true;
        resultCanvasGroup.alpha = 1f;
        resultCanvasGroup.interactable = false;
        resultCanvasGroup.blocksRaycasts = true;
        SetBlackoutAlpha(0f);

        // 이미 결과 보드를 표시 중이면 기존 결과 카메라를 검은 화면이 완전히 덮을 때까지 유지합니다.
        // 먼저 카메라를 끄면 Unity의 "No cameras rendering" 프레임이 노출됩니다.
        await FadeBlackAsync(0f, 1f, fadeSeconds, cancellationToken);

        stageVisualRoot.SetActive(true);
        roundBoardRoot.SetActive(!championMode);
        championRoot.SetActive(championMode);
        SetGameplayCanvasesEnabled(false);
        gameplayCamera.enabled = false;
        ApplyCameraPose(cameraPose);
        resultCamera.enabled = true;
        onStageActivated?.Invoke();

        if (cameraCutBlackHoldSeconds > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(cameraCutBlackHoldSeconds),
                ignoreTimeScale: true,
                cancellationToken: cancellationToken);
        }

        await FadeBlackAsync(1f, 0f, fadeSeconds, cancellationToken);
    }

    private async UniTask ExitPresentationAsync(
        float fadeSeconds,
        float blackHoldSeconds,
        Func<CancellationToken, UniTask> onGameplayCovered,
        CancellationToken cancellationToken)
    {
        await FadeBlackAsync(0f, 1f, fadeSeconds, cancellationToken);

        ReleasePresentedPlayers();
        resultCamera.enabled = false;
        stageVisualRoot.SetActive(false);
        gameplayCamera.enabled = gameplayCameraWasEnabled;
        RestoreGameplayCanvasStates();

        if (onGameplayCovered != null)
        {
            await onGameplayCovered(cancellationToken);
        }

        if (blackHoldSeconds > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(blackHoldSeconds),
                ignoreTimeScale: true,
                cancellationToken: cancellationToken);
        }

        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken);
        await FadeBlackAsync(1f, 0f, fadeSeconds, cancellationToken);
        resultCanvasGroup.alpha = 0f;
        resultCanvasGroup.interactable = false;
        resultCanvasGroup.blocksRaycasts = false;
        presentationActive = false;
    }

    private void OnFinalExitClicked()
    {
        finalExitCountdownVersion++;
        resultCanvasGroup.interactable = false;
        finalExitButton.interactable = false;
        MatchExitService.ReturnToLobby();
    }

    private void SetFinalExitVisible(bool visible)
    {
        if (finalExitButton == null || finalExitButtonGroup == null)
        {
            return;
        }

        // Graphic을 비활성화하면 다시 켠 첫 프레임에 depth=-1로 남아
        // Raycast가 누락될 수 있으므로 오브젝트는 활성 상태로 유지합니다.
        finalExitButton.gameObject.SetActive(true);
        finalExitButton.interactable = visible;
        finalExitButtonGroup.alpha = visible ? 1f : 0f;
        finalExitButtonGroup.interactable = visible;
        finalExitButtonGroup.blocksRaycasts = visible;
    }

    private void StartFinalExitCountdown()
    {
        int version = ++finalExitCountdownVersion;
        RunFinalExitCountdownAsync(version, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RunFinalExitCountdownAsync(
        int version,
        CancellationToken cancellationToken)
    {
        float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, finalExitAutoReturnSeconds);
        int displayedSeconds = -1;

        try
        {
            while (version == finalExitCountdownVersion && finalResultActive)
            {
                int seconds = Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.realtimeSinceStartup));
                if (seconds != displayedSeconds)
                {
                    displayedSeconds = seconds;
                    finalExitButtonLabel.text = "EXIT  " + seconds;
                }

                if (seconds <= 0)
                {
                    OnFinalExitClicked();
                    return;
                }

                await UniTask.Delay(
                    TimeSpan.FromMilliseconds(250),
                    ignoreTimeScale: true,
                    cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReleasePresentedPlayers()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Release();
                }
            }
        }

        if (championAvatar != null)
        {
            championAvatar.Release();
        }
    }

    private void PrepareBoard(
        IReadOnlyList<GameParticipant> participants,
        GameParticipant currentEliminated)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            RoundResultSlotView slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            GameParticipant participant = FindParticipant(participants, slot.Owner);
            bool isCurrentEliminated =
                currentEliminated != null && currentEliminated.Owner == slot.Owner;
            bool wasAlreadyEliminated =
                participant != null && participant.IsEliminated && !isCurrentEliminated;
            slot.Prepare(participant, wasAlreadyEliminated);
        }
    }

    private async UniTask AnimateChampionCameraAsync(CancellationToken cancellationToken)
    {
        Vector3 cameraStartPosition = resultCamera.transform.position;
        Quaternion cameraStartRotation = resultCamera.transform.rotation;
        Vector3 avatarStart = championAvatar.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < championCameraMoveSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized =
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, championCameraMoveSeconds));
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            resultCamera.transform.position =
                Vector3.Lerp(cameraStartPosition, championCameraEndPose.position, eased);
            resultCamera.transform.rotation =
                Quaternion.Slerp(cameraStartRotation, championCameraEndPose.rotation, eased);
            championAvatar.transform.localPosition =
                avatarStart + Vector3.up * (Mathf.Sin(normalized * Mathf.PI) * 0.18f);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        ApplyCameraPose(championCameraEndPose);
        championAvatar.transform.localPosition = avatarStart;
    }

    private async UniTask FadeBlackAsync(
        float from,
        float to,
        float duration,
        CancellationToken cancellationToken)
    {
        SetBlackoutAlpha(from);
        if (duration <= 0f)
        {
            SetBlackoutAlpha(to);
            return;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetBlackoutAlpha(Mathf.Lerp(from, to, elapsed / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        SetBlackoutAlpha(to);
    }

    private void SetBlackoutAlpha(float alpha)
    {
        Color color = blackoutImage.color;
        color.a = Mathf.Clamp01(alpha);
        blackoutImage.color = color;
    }

    private void CaptureGameplayCanvasStates()
    {
        if (gameplayCanvasWasEnabled == null ||
            gameplayCanvasWasEnabled.Length != gameplayCanvases.Length)
        {
            gameplayCanvasWasEnabled = new bool[gameplayCanvases.Length];
        }

        for (int i = 0; i < gameplayCanvases.Length; i++)
        {
            Canvas canvas = gameplayCanvases[i];
            gameplayCanvasWasEnabled[i] = canvas != null && canvas.enabled;
        }
    }

    private void SetGameplayCanvasesEnabled(bool canvasEnabled)
    {
        for (int i = 0; i < gameplayCanvases.Length; i++)
        {
            Canvas canvas = gameplayCanvases[i];
            if (canvas != null)
            {
                canvas.enabled = canvasEnabled;
            }
        }
    }

    private void RestoreGameplayCanvasStates()
    {
        if (gameplayCanvasWasEnabled == null ||
            gameplayCanvasWasEnabled.Length != gameplayCanvases.Length)
        {
            return;
        }

        for (int i = 0; i < gameplayCanvases.Length; i++)
        {
            Canvas canvas = gameplayCanvases[i];
            if (canvas != null)
            {
                canvas.enabled = gameplayCanvasWasEnabled[i];
            }
        }
    }

    private void ApplyCameraPose(Transform pose)
    {
        if (pose == null)
        {
            return;
        }

        resultCamera.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    private RoundResultSlotView FindSlot(PaintOwner owner)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].Owner == owner)
            {
                return slots[i];
            }
        }

        return null;
    }

    private void PlayVisibleSurvivorWinnerPoses()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].PlaySurvivorWinnerPose();
            }
        }
    }

    private static GameParticipant FindParticipant(
        IReadOnlyList<GameParticipant> participants,
        PaintOwner owner)
    {
        if (participants == null)
        {
            return null;
        }

        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && participant.Owner == owner)
            {
                return participant;
            }
        }

        return null;
    }

    private static int CountSurvivorsAfterResult(
        IReadOnlyList<GameParticipant> participants,
        GameParticipant eliminated)
    {
        if (participants == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.IsEliminated)
            {
                continue;
            }

            if (eliminated != null && participant.Owner == eliminated.Owner)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static string GetOrdinal(int rank)
    {
        switch (rank)
        {
            case 1: return "1ST";
            case 2: return "2ND";
            case 3: return "3RD";
            default: return rank + "TH";
        }
    }

    private bool HasRequiredReferences()
    {
        return stageVisualRoot != null &&
            roundBoardRoot != null &&
            championRoot != null &&
            resultCanvas != null &&
            resultCanvasGroup != null &&
            resultRaycaster != null &&
            blackoutImage != null &&
            gameplayCamera != null &&
            resultCamera != null &&
            AreGameplayCanvasesAssigned() &&
            roundCameraPose != null &&
            championCameraStartPose != null &&
            championCameraEndPose != null &&
            slots != null &&
            slots.Length == 4 &&
            titleText != null &&
            detailText != null &&
            finalExitButton != null &&
            finalExitButtonGroup != null &&
            finalExitButtonLabel != null &&
            championAvatar != null;
    }

    private bool AreGameplayCanvasesAssigned()
    {
        if (gameplayCanvases == null || gameplayCanvases.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < gameplayCanvases.Length; i++)
        {
            if (gameplayCanvases[i] == null)
            {
                return false;
            }
        }

        return true;
    }
}
