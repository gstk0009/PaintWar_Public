using System.Collections.Generic;
using UnityEngine;

public sealed class ForwardConePattern : IWeaponPattern
{
    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        Vector2Int f = PatternDirection.Forward(forward);
        Vector2Int r = PatternDirection.Right(f);

        for (int distance = 1; distance <= weapon.Range; distance++)
        {
            yield return f * distance;
            yield return f * distance + r;
            yield return f * distance - r;
        }
    }
}
