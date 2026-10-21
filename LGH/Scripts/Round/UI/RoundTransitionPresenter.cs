using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

// 라운드 전환 표시를 씬에 저장된 3D 결과 무대 또는 기존 텍스트 HUD로 전달합니다.
public sealed class RoundTransitionPresenter
{
    private RoundTransitionView transitionView;
    private RoundResultStageView resultStageView;

    public bool IsFinalResultActive
    {
        get
        {
            RoundResultStageView stageView = EnsureResultStageView();
            return stageView != null && stageView.IsFinalResultActive;
        }
    }

    public async UniTask PlayRoundEndAsync(
        int roundIndex,
        GameParticipant eliminated,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.PlayRoundResultBgm();
        }

        RoundResultStageView stageView = EnsureResultStageView();
        if (stageView != null)
        {
            await stageView.PlayRoundEndAsync(
                roundIndex,
                GameRoundManager.HasInstance ? GameRoundManager.Instance.Participants : null,
                eliminated,
                fadeSeconds,
                holdSeconds,
                cancellationToken);
            return;
        }

        RoundTransitionView view = EnsureTransitionView();
        if (view != null)
        {
            await view.PlayRoundEndAsync(
                roundIndex,
                eliminated,
                fadeSeconds,
                holdSeconds,
                cancellationToken);
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
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.RestoreGameBgm();
        }

        RoundResultStageView stageView = EnsureResultStageView();
        if (stageView != null &&
            (stageView.IsPresentationActive || roundIndex > 1))
        {
            await stageView.PlayRoundStartAsync(
                roundIndex,
                holdSeconds,
                fadeSeconds,
                blackHoldSeconds,
                onGameplayCovered,
                cancellationToken);
            return;
        }

        RoundTransitionView view = EnsureTransitionView();
        if (view != null)
        {
            await view.PlayRoundStartAsync(
                roundIndex,
                holdSeconds,
                fadeSeconds,
                blackHoldSeconds,
                onGameplayCovered,
                cancellationToken);
        }
    }

    public async UniTask PlayFinalResultAsync(
        GameParticipant winner,
        GameParticipant loser,
        float fadeSeconds,
        float holdSeconds,
        CancellationToken cancellationToken)
    {
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.PlayWinnerBgm();
        }

        RoundResultStageView stageView = EnsureResultStageView();
        if (stageView != null)
        {
            await stageView.PlayFinalResultAsync(
                winner,
                fadeSeconds,
                holdSeconds,
                cancellationToken);
            return;
        }

        RoundTransitionView view = EnsureTransitionView();
        if (view != null)
        {
            await view.PlayFinalResultAsync(
                winner,
                loser,
                fadeSeconds,
                holdSeconds,
                cancellationToken);
        }
    }

    public async UniTask PlayMatchResultsAsync(
        GameParticipant winner,
        IReadOnlyList<GameParticipant> rankedEliminated,
        float fadeSeconds,
        float winnerHoldSeconds,
        CancellationToken cancellationToken)
    {
        RoundResultStageView stageView = EnsureResultStageView();
        if (stageView != null)
        {
            if (CSE.SoundManager.Instance != null)
            {
                CSE.SoundManager.Instance.PlayRoundResultBgm();
            }

            await stageView.PlayRankingRevealAsync(
                GameRoundManager.HasInstance ? GameRoundManager.Instance.Participants : null,
                rankedEliminated,
                fadeSeconds,
                cancellationToken);

            if (CSE.SoundManager.Instance != null)
            {
                CSE.SoundManager.Instance.PlayWinnerBgm();
            }

            await stageView.PlayFinalResultAsync(
                winner,
                fadeSeconds,
                winnerHoldSeconds,
                cancellationToken);
            return;
        }

        RoundTransitionView view = EnsureTransitionView();
        if (view != null)
        {
            await view.PlayMatchResultsAsync(
                winner,
                rankedEliminated,
                fadeSeconds,
                winnerHoldSeconds,
                cancellationToken);
        }
    }

    public void HideImmediate()
    {
        RoundResultStageView stageView = EnsureResultStageView();
        if (stageView != null)
        {
            stageView.HideImmediate();
        }

        RoundTransitionView view = transitionView;
        if (view == null)
        {
            view = Object.FindFirstObjectByType<RoundTransitionView>(FindObjectsInactive.Include);
            transitionView = view;
        }

        if (view != null)
        {
            view.HideImmediate();
        }
    }

    private RoundResultStageView EnsureResultStageView()
    {
        if (resultStageView == null)
        {
            resultStageView =
                Object.FindFirstObjectByType<RoundResultStageView>(FindObjectsInactive.Include);
        }

        return resultStageView;
    }

    private RoundTransitionView EnsureTransitionView()
    {
        if (transitionView != null)
        {
            return transitionView;
        }

        transitionView =
            Object.FindFirstObjectByType<RoundTransitionView>(FindObjectsInactive.Include);
        if (transitionView != null)
        {
            return transitionView;
        }

        Debug.LogError(
            "[RoundTransitionPresenter] Round transition presentation is missing from GameScene. " +
            "Run PaintWar/UI/Create Or Rebuild 3D Round Result Stage.");
        return null;
    }
}
