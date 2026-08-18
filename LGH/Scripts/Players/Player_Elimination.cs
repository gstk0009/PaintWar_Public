using UnityEngine;

/// <summary>
/// 플레이어 탈락 상태 적용과 late join 보정 처리를 담당합니다.
/// </summary>
public partial class Player
{
    public void ApplyEliminatedSnapshot(bool eliminated)
    {
        ApplyEliminatedLocal(eliminated);
    }

    /// <summary>
    /// RoundManager 상태와 맞는 타이밍에 플레이어 active 상태를 갱신합니다.
    /// </summary>
    private void ApplyEliminatedLocal(bool eliminated)
    {
        if (eliminated && !CanApplyEliminatedNow())
        {
            pendingEliminatedApply = true;
            if (!loggedPendingEliminatedApply)
            {
                loggedPendingEliminatedApply = true;
                Debug.LogWarning(
                    "[Player] Queued eliminated=true until round state catches up. " +
                    "phase=" + (GameRoundManager.HasInstance ? GameRoundManager.Instance.CurrentPhase.ToString() : "None") +
                    " object=" + name + ".");
            }
            return;
        }

        pendingEliminatedApply = false;
        loggedPendingEliminatedApply = false;

        bool shouldBeActive = !eliminated;
        if (gameObject.activeSelf != shouldBeActive)
        {
            gameObject.SetActive(shouldBeActive);
        }
    }

    /// <summary>
    /// 현재 라운드 phase에서 탈락 상태를 적용해도 되는지 확인합니다.
    /// </summary>
    private bool CanApplyEliminatedNow()
    {
        if (!GameRoundManager.HasInstance)
        {
            return true;
        }

        GamePhase phase = GameRoundManager.Instance.CurrentPhase;
        if (phase == GamePhase.RoundResult || phase == GamePhase.GameOver)
        {
            return true;
        }

        GameParticipant participant =
            GameRoundManager.Instance.FindParticipant(myPaintOwner.Value);
        return participant != null && participant.IsEliminated;
    }
}
