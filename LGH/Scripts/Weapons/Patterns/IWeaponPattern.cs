using System.Collections.Generic;
using UnityEngine;

public interface IWeaponPattern
{
    IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward);
}
