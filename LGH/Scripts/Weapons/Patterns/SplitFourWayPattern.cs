using System.Collections.Generic;
using UnityEngine;

public sealed class SplitFourWayPattern : IWeaponPattern
{
    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        Vector2Int f = PatternDirection.Forward(forward);
        Vector2Int r = PatternDirection.Right(f);

        foreach (Vector2Int direction in new[] { f, -f, r, -r })
        {
            for (int i = 1; i <= weapon.Range; i++)
            {
                yield return direction * i;
            }
        }
    }
}
