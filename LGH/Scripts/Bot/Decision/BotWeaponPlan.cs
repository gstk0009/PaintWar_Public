using UnityEngine;

public readonly struct BotWeaponPlan
{
    public readonly bool IsValid;
    public readonly Vector2Int TargetIndex;
    public readonly int Score;

    public BotWeaponPlan(Vector2Int targetIndex, int score)
    {
        IsValid = true;
        TargetIndex = targetIndex;
        Score = score;
    }

    public static BotWeaponPlan None { get { return default; } }
}
