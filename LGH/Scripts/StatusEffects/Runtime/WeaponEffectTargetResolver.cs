using System.Collections.Generic;
using UnityEngine;

public static class WeaponEffectTargetResolver
{
    private static readonly HashSet<PaintOwner> AddedOwners = new HashSet<PaintOwner>();

    public static List<PlayerPaintIdentity> FindTargets(
        PaintOwner sourceOwner,
        Vector3 origin,
        IEnumerable<Vector2Int> offsets)
    {
        List<PlayerPaintIdentity> targets = new List<PlayerPaintIdentity>();
        if (!TilePaintManager.HasInstance)
        {
            return targets;
        }

        Vector2Int originIndex = TilePaintManager.Instance.GetGridIndex(origin);
        HashSet<Vector2Int> affectedIndices = new HashSet<Vector2Int>();
        foreach (Vector2Int offset in offsets)
        {
            Vector2Int index = originIndex + offset;
            if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                TilePaintManager.Instance.IsTileDestroyed(index))
            {
                continue;
            }

            if (GameRoundManager.HasInstance &&
                !GameRoundManager.Instance.CanOwnerClaimTile(sourceOwner, index))
            {
                continue;
            }

            affectedIndices.Add(index);
        }

        AddedOwners.Clear();

        IReadOnlyList<PlayerPaintIdentity> players = PlayerPaintIdentity.ActivePlayers;
        for (int i = 0; i < players.Count; i++)
        {
            AddIfAffected(targets, players[i], sourceOwner, affectedIndices);
        }

        if (GameRoundManager.HasInstance)
        {
            IReadOnlyList<GameParticipant> participants = GameRoundManager.Instance.Participants;
            for (int i = 0; i < participants.Count; i++)
            {
                GameParticipant participant = participants[i];
                if (participant == null || participant.IsEliminated)
                {
                    continue;
                }

                PlayerPaintIdentity identity = participant.Identity;
                if (identity == null && participant.Transform != null)
                {
                    participant.Transform.TryGetComponent(out identity);
                }

                AddIfAffected(targets, identity, sourceOwner, affectedIndices);
            }
        }

        AddedOwners.Clear();
        return targets;
    }

    private static void AddIfAffected(
        List<PlayerPaintIdentity> targets,
        PlayerPaintIdentity candidate,
        PaintOwner sourceOwner,
        HashSet<Vector2Int> affectedIndices)
    {
        if (candidate == null || candidate.Owner == PaintOwner.Neutral || candidate.Owner == sourceOwner)
        {
            return;
        }

        Vector2Int candidateIndex = TilePaintManager.Instance.GetGridIndex(candidate.transform.position);
        if (TilePaintManager.Instance.IsTileDestroyed(candidateIndex))
        {
            return;
        }

        if (!affectedIndices.Contains(candidateIndex))
        {
            return;
        }

        if (!AddedOwners.Add(candidate.Owner))
        {
            return;
        }

        targets.Add(candidate);
    }
}
