using System.Collections.Generic;
using UnityEngine;

public sealed class CrossPattern : IWeaponPattern
{
    private readonly bool includesCenter;

    public CrossPattern(bool includesCenter)
    {
        this.includesCenter = includesCenter;
    }

    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        Vector2Int f = PatternDirection.Forward(forward);
        Vector2Int r = PatternDirection.Right(f);

        if (includesCenter)
        {
            yield return Vector2Int.zero;
        }

        yield return f;
        yield return -f;
        yield return r;
        yield return -r;
    }
}
