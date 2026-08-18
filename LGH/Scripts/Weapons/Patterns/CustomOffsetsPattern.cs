using System.Collections.Generic;
using UnityEngine;

public sealed class CustomOffsetsPattern : IWeaponPattern
{
    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        return weapon.CustomOffsets ?? EmptyPattern.Offsets;
    }
}
