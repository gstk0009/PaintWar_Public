using UnityEngine;

public enum BotPersonality
{
    Aggressive,
    Defensive,
    Rival,
    Opportunist
}

/// <summary>
/// Keeps the four server-side bot target reservations allocation-free. Reservations expire so
/// disconnects and missed cleanup cannot permanently block a target.
/// </summary>
public static class BotOpponentCoordinator
{
    private const int OwnerSlots = 5;
    private static readonly PaintOwner[] Targets = new PaintOwner[OwnerSlots];
    private static readonly float[] ExpiresAt = new float[OwnerSlots];
    private static int roundIndex = int.MinValue;

    public static void BeginRound(int currentRoundIndex)
    {
        if (roundIndex == currentRoundIndex)
        {
            return;
        }

        roundIndex = currentRoundIndex;
        Clear();
    }

    public static bool TryReserve(
        PaintOwner botOwner,
        PaintOwner targetOwner,
        int maximumFocus,
        float now,
        float duration)
    {
        int botIndex = (int)botOwner;
        if (!IsOwnerIndex(botIndex) || targetOwner == PaintOwner.Neutral || botOwner == targetOwner)
        {
            return false;
        }

        CleanupExpired(now);
        if (Targets[botIndex] == targetOwner)
        {
            ExpiresAt[botIndex] = now + Mathf.Max(0.5f, duration);
            return true;
        }

        if (CountReservations(targetOwner, now, botOwner) >= Mathf.Max(1, maximumFocus))
        {
            return false;
        }

        Targets[botIndex] = targetOwner;
        ExpiresAt[botIndex] = now + Mathf.Max(0.5f, duration);
        return true;
    }

    public static int CountReservations(PaintOwner targetOwner, float now, PaintOwner exceptOwner)
    {
        CleanupExpired(now);
        int count = 0;
        for (int i = 1; i < OwnerSlots; i++)
        {
            if ((PaintOwner)i != exceptOwner && Targets[i] == targetOwner)
            {
                count++;
            }
        }

        return count;
    }

    public static void Release(PaintOwner botOwner)
    {
        int index = (int)botOwner;
        if (!IsOwnerIndex(index))
        {
            return;
        }

        Targets[index] = PaintOwner.Neutral;
        ExpiresAt[index] = 0f;
    }

    public static void Clear()
    {
        for (int i = 0; i < OwnerSlots; i++)
        {
            Targets[i] = PaintOwner.Neutral;
            ExpiresAt[i] = 0f;
        }
    }

    private static void CleanupExpired(float now)
    {
        for (int i = 1; i < OwnerSlots; i++)
        {
            if (Targets[i] != PaintOwner.Neutral && now >= ExpiresAt[i])
            {
                Targets[i] = PaintOwner.Neutral;
                ExpiresAt[i] = 0f;
            }
        }
    }

    private static bool IsOwnerIndex(int index)
    {
        return index > 0 && index < OwnerSlots;
    }
}
