using System.Collections.Generic;

/// <summary>
/// 라운드 종료 후 탈락자와 최종 승자를 고르는 규칙을 담당합니다.
/// 점령 타일 수, 레벨, 경험치, 마지막 점령 시간을 같은 순서로 비교해서 판정합니다.
/// </summary>
/// <summary>
/// 라운드 결과에서 탈락자와 최종 승자를 고르는 규칙을 모아둔 클래스입니다.
/// 점령 수, 레벨, 경험치, 마지막 점령 시간을 tie-breaker로 사용합니다.
/// </summary>
public static class RoundEliminationResolver
{
    /// <summary>
    /// 현재 라운드의 탈락자를 찾습니다.
    /// 점령 타일 수가 가장 적은 참가자를 먼저 고르고, 동률이면 레벨/경험치/마지막 점령 시간이 낮은 쪽이 탈락합니다.
    /// </summary>
    public static GameParticipant FindRoundLoser(IEnumerable<GameParticipant> participants)
    {
        GameParticipant loser = null;
        foreach (GameParticipant participant in participants)
        {
            if (participant == null || participant.IsEliminated)
            {
                continue;
            }

            if (loser == null || CompareForFinalRanking(participant, loser) > 0)
            {
                loser = participant;
            }
        }

        return loser;
    }

    /// <summary>
    /// 파이널 라운드 종료 후 최종 승자를 찾습니다.
    /// 탈락 판정과 반대로 점령 타일 수, 레벨, 경험치, 마지막 점령 시간이 높은 순서로 우선합니다.
    /// </summary>
    public static GameParticipant FindWinner(IEnumerable<GameParticipant> participants)
    {
        GameParticipant winner = null;
        foreach (GameParticipant participant in participants)
        {
            if (participant == null || participant.IsEliminated)
            {
                continue;
            }

            if (winner == null || CompareForFinalRanking(participant, winner) < 0)
            {
                winner = participant;
            }
        }

        return winner;
    }

    public static GameParticipant FindWinnerAfterElimination(
        IEnumerable<GameParticipant> participants,
        GameParticipant eliminated)
    {
        GameParticipant winner = null;
        foreach (GameParticipant participant in participants)
        {
            if (participant == null || participant.IsEliminated || participant == eliminated)
            {
                continue;
            }

            if (winner == null || CompareForFinalRanking(participant, winner) < 0)
            {
                winner = participant;
            }
        }

        return winner;
    }

    /// <summary>
    /// 전체 참가자를 최종 순위(1등부터)로 정렬합니다. 호출자가 제공한 목록을 재사용해
    /// 매치 종료 시점 외에는 할당이나 정렬 비용이 발생하지 않습니다.
    /// </summary>
    public static void BuildFinalRanking(
        IReadOnlyList<GameParticipant> participants,
        List<GameParticipant> destination)
    {
        destination.Clear();
        if (participants == null)
        {
            return;
        }

        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && !participant.IsEliminated)
            {
                destination.Add(participant);
            }
        }

        destination.Sort(CompareForFinalRanking);
    }

    private static int CompareForFinalRanking(GameParticipant left, GameParticipant right)
    {
        int result = right.CurrentTileCount.CompareTo(left.CurrentTileCount);
        if (result != 0) return result;

        result = right.Level.CompareTo(left.Level);
        if (result != 0) return result;

        result = right.CurrentExp.CompareTo(left.CurrentExp);
        if (result != 0) return result;

        result = right.LastClaimTimestamp.CompareTo(left.LastClaimTimestamp);
        if (result != 0) return result;

        // 모든 점수가 같아도 서버와 클라이언트가 항상 같은 순위를 얻도록 owner로 마무리합니다.
        return ((int)left.Owner).CompareTo((int)right.Owner);
    }

    /// <summary>
    /// 참가자를 탈락 처리하고 게임 오브젝트를 비활성화합니다.
    /// eliminationOrder는 나중에 결과 UI나 리플레이가 필요할 때 활용할 수 있도록 순서를 기록합니다.
    /// </summary>
    public static void Eliminate(GameParticipant participant, List<GameParticipant> eliminationOrder)
    {
        // 탈락 상태는 참가자 데이터와 실제 오브젝트 활성 상태 양쪽에 반영해야 합니다.
        if (participant == null)
        {
            return;
        }

        participant.IsEliminated = true;
        if (eliminationOrder != null)
        {
            eliminationOrder.Add(participant);
        }

        if (participant.Transform != null)
        {
            Player player = participant.Transform.GetComponent<Player>();
            if (player != null)
            {
                // FishNet 오브젝트는 RPC로 클라이언트 활성 상태까지 함께 꺼줍니다.
                player.ServerSetEliminated(true);
            }
            else
            {
                participant.Transform.gameObject.SetActive(false);
            }
        }
    }
}
