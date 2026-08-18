using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 라운드 시작/종료 때 화면을 덮고 안내 문구를 보여주는 전환 UI입니다.
/// </summary>
public class RoundTransitionView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image dimImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI detailText;

    private void Awake()
    {
        if (canvasGroup == null || dimImage == null || titleText == null || detailText == null)
        {
            Debug.LogError(
                "[RoundTransitionView] Scene references are missing. Re-run the editor HUD setup.",
                this);
            enabled = false;
            return;
        }

        SetAlpha(0f);
    }

    private void OnEnable()
    {
        BringToFront();
    }

    public async UniTask PlayRoundEndAsync(
        int roundIndex,
        GameParticipant eliminated,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        ShowOverlay();

        string eliminatedLabel = GetOwnerLabel(eliminated);
        string eliminatedColor = ColorUtility.ToHtmlStringRGB(GetOwnerColor(eliminated != null ? eliminated.Owner : PaintOwner.Neutral));

        titleText.text = "PHASE " + roundIndex + " END!!!";
        detailText.text = eliminated != null
            ? "ELIMINATED <color=#" + eliminatedColor + ">" + eliminatedLabel + "</color>"
            : "NO ELIMINATION";

        await FadeToAsync(1f, fadeSeconds, cancellationToken);
        await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: cancellationToken);
    }

    public async UniTask PlayFinalResultAsync(
        GameParticipant winner,
        GameParticipant loser,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        ShowOverlay();

        string winnerLabel = GetOwnerLabel(winner);
        string winnerColor = ColorUtility.ToHtmlStringRGB(GetOwnerColor(winner != null ? winner.Owner : PaintOwner.Neutral));
        string loserLabel = GetOwnerLabel(loser);
        string loserColor = ColorUtility.ToHtmlStringRGB(GetOwnerColor(loser != null ? loser.Owner : PaintOwner.Neutral));

        titleText.text = "FINAL PHASE END!!!";
        detailText.text = "WINNER <color=#" + winnerColor + ">" + winnerLabel + "</color>\n" +
            "LOSER <color=#" + loserColor + ">" + loserLabel + "</color>";

        await FadeToAsync(1f, fadeSeconds, cancellationToken);
        await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: cancellationToken);
    }

    public async UniTask PlayMatchResultsAsync(
        GameParticipant winner,
        IReadOnlyList<GameParticipant> rankedEliminated,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        ShowOverlay();
        titleText.text = "FINAL RESULTS";
        await FadeToAsync(1f, fadeSeconds, cancellationToken);

        if (rankedEliminated != null)
        {
            for (int i = 0; i < rankedEliminated.Count; i++)
            {
                GameParticipant eliminated = rankedEliminated[i];
                int rank = rankedEliminated.Count - i + 1;
                detailText.text = rank + GetOrdinalSuffix(rank) + "  " + GetOwnerLabel(eliminated);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(Mathf.Max(0.5f, holdSeconds)),
                    ignoreTimeScale: true,
                    cancellationToken: cancellationToken);
            }
        }

        GameParticipant loser = rankedEliminated != null && rankedEliminated.Count > 0
            ? rankedEliminated[rankedEliminated.Count - 1]
            : null;
        await PlayFinalResultAsync(winner, loser, 0f, holdSeconds, cancellationToken);
    }

    public async UniTask PlayRoundStartAsync(
        int roundIndex,
        float holdSeconds,
        float fadeSeconds,
        float blackHoldSeconds,
        Func<CancellationToken, UniTask> onGameplayCovered,
        CancellationToken cancellationToken)
    {
        ShowOverlay();
        SetAlpha(1f);

        titleText.text = roundIndex >= 3 ? "FINAL PHASE START" : "PHASE " + roundIndex + " START";
        detailText.text = "FARMING";

        await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: cancellationToken);
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

        await FadeToAsync(0f, fadeSeconds, cancellationToken);
        gameObject.SetActive(false);
    }

    private void ShowOverlay()
    {
        gameObject.SetActive(true);
        BringToFront();
    }

    private void BringToFront()
    {
        transform.SetAsLastSibling();
    }

    public void HideImmediate()
    {
        SetAlpha(0f);
        gameObject.SetActive(false);
    }

    private async UniTask FadeToAsync(float targetAlpha, float duration, CancellationToken cancellationToken)
    {
        float startAlpha = canvasGroup.alpha;
        if (duration <= 0f)
        {
            SetAlpha(targetAlpha);
            return;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        SetAlpha(targetAlpha);
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
            canvasGroup.blocksRaycasts = alpha > 0.95f;
            canvasGroup.interactable = false;
        }
    }

    private string GetOwnerLabel(GameParticipant participant)
    {
        if (participant == null)
        {
            return "";
        }

        return "P" + (int)participant.Owner;
    }

    private Color GetOwnerColor(PaintOwner owner)
    {
        switch (owner)
        {
            case PaintOwner.Player1:
                return new Color32(0xE2, 0x4B, 0x4A, 0xFF);
            case PaintOwner.Player2:
                return new Color32(0x37, 0x8A, 0xDD, 0xFF);
            case PaintOwner.Player3:
                return new Color32(0xEF, 0x9F, 0x27, 0xFF);
            case PaintOwner.Player4:
                return new Color32(0x1D, 0x9E, 0x75, 0xFF);
            default:
                return Color.white;
        }
    }

    private static string GetOrdinalSuffix(int rank)
    {
        if (rank == 1) return "ST";
        if (rank == 2) return "ND";
        if (rank == 3) return "RD";
        return "TH";
    }
}
