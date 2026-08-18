using System.Collections.Generic;
using UnityEngine;

public static class WeaponPatternResolver
{
    private static readonly GridPattern GridPattern = new GridPattern();
    private static readonly Dictionary<WeaponPatternType, IWeaponPattern> Patterns = new Dictionary<WeaponPatternType, IWeaponPattern>
    {
        { WeaponPatternType.Bomb3x3, new SquarePattern(1) },
        { WeaponPatternType.ForwardLine, new ForwardLinePattern() },
        { WeaponPatternType.ForwardCone3x3, new ForwardConePattern() },
        { WeaponPatternType.LaserLine, new ForwardLinePattern() },
        { WeaponPatternType.DashLine, new ForwardLinePattern() },
        { WeaponPatternType.Cross, new CrossPattern(true) },
        { WeaponPatternType.AdjacentFour, new CrossPattern(false) },
        { WeaponPatternType.SplitFourWay, new SplitFourWayPattern() },
        { WeaponPatternType.Square5x5, new SquarePattern(2) },
        { WeaponPatternType.CustomOffsets, new CustomOffsetsPattern() }
    };

    public static IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        return GetOffsets(weapon, forward, 1);
    }

    public static IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward, int playerLevel)
    {
        if (weapon == null)
        {
            return EmptyPattern.Offsets;
        }

        if (weapon.HasPatternRows)
        {
            return GridPattern.GetOffsets(weapon, forward, playerLevel);
        }

        if (!Patterns.TryGetValue(weapon.PatternType, out IWeaponPattern pattern))
        {
            return EmptyPattern.Offsets;
        }

        return pattern.GetOffsets(weapon, forward);
    }
}
