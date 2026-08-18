using System.Collections.Generic;
using UnityEngine;

public sealed class ForwardLinePattern : IWeaponPattern
{
    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        Vector2Int direction = PatternDirection.Forward(forward);
        for (int i = 1; i <= weapon.Range; i++)
        {
            yield return direction * i;
        }
    }
}
