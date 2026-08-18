using System.Collections.Generic;

/// <summary>
/// GameRoundManager의 현재 상태를 FishNet broadcast 데이터로 변환합니다.
/// </summary>
public static class NetworkRoundStateFactory
{
    public static RoundStateBroadcast Create(GameRoundManager manager)
    {
        int participantMask = 0;
        int botMask = 0;
        int eliminatedMask = 0;
        IReadOnlyList<GameParticipant> participants = manager.Participants;

        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            int bit = 1 << ((int)participant.Owner - 1);
            participantMask |= bit;
            if (participant.IsBot)
            {
                botMask |= bit;
            }

            if (participant.IsEliminated)
            {
                eliminatedMask |= bit;
            }
        }

        return new RoundStateBroadcast
        {
            Phase = (int)manager.CurrentPhase,
            RoundIndex = manager.CurrentRoundIndex,
            PhaseRemainingSeconds = manager.PhaseRemainingSeconds,
            MatchWaitRemainingSeconds = manager.MatchWaitRemainingSeconds,
            WinnerOwner = manager.Winner != null ? (int)manager.Winner.Owner : 0,
            RoundResultEliminatedOwner = manager.RoundResultEliminated != null ? (int)manager.RoundResultEliminated.Owner : 0,
            SecondPlaceOwner = (int)manager.GetFinalRankOwner(1),
            ThirdPlaceOwner = (int)manager.GetFinalRankOwner(2),
            FourthPlaceOwner = (int)manager.GetFinalRankOwner(3),
            ParticipantMask = participantMask,
            BotMask = botMask,
            EliminatedMask = eliminatedMask,
            ItemSpawnSeed = manager.ItemSpawnSeed,
            FinalCountdownActive = manager.IsFinalCountdownActive ? (byte)1 : (byte)0,
            Player1TileCount = GetTileCount(participants, PaintOwner.Player1),
            Player2TileCount = GetTileCount(participants, PaintOwner.Player2),
            Player3TileCount = GetTileCount(participants, PaintOwner.Player3),
            Player4TileCount = GetTileCount(participants, PaintOwner.Player4)
        };
    }

    public static RoundRosterBroadcast CreateRoster(GameRoundManager manager)
    {
        int participantMask = 0;
        int botMask = 0;
        IReadOnlyList<GameParticipant> participants = manager.Participants;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.Owner == PaintOwner.Neutral) continue;
            int bit = 1 << ((int)participant.Owner - 1);
            participantMask |= bit;
            if (participant.IsBot) botMask |= bit;
        }

        return new RoundRosterBroadcast
        {
            ParticipantMask = participantMask,
            BotMask = botMask,
            Player1DisplayName = GetDisplayName(participants, PaintOwner.Player1),
            Player2DisplayName = GetDisplayName(participants, PaintOwner.Player2),
            Player3DisplayName = GetDisplayName(participants, PaintOwner.Player3),
            Player4DisplayName = GetDisplayName(participants, PaintOwner.Player4)
        };
    }

    private static string GetDisplayName(
        IReadOnlyList<GameParticipant> participants,
        PaintOwner owner)
    {
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null &&
                participant.Owner == owner &&
                !string.IsNullOrWhiteSpace(participant.DisplayName))
            {
                return participant.DisplayName;
            }
        }

        return "P" + (int)owner;
    }

    private static int GetTileCount(
        IReadOnlyList<GameParticipant> participants,
        PaintOwner owner)
    {
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && participant.Owner == owner)
            {
                return participant.CurrentTileCount;
            }
        }

        return 0;
    }
}

