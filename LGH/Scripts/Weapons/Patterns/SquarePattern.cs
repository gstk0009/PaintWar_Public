using System.Collections.Generic;
using UnityEngine;

public sealed class SquarePattern : IWeaponPattern
{
    private readonly int fixedRadius;

    public SquarePattern(int fixedRadius)
    {
        this.fixedRadius = fixedRadius;
    }

    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        int radius = fixedRadius > 0 ? fixedRadius : weapon.Range;
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                yield return new Vector2Int(x, y);
            }
        }
    }
}
