using System.Collections.Generic;
using UnityEngine;

public sealed class GridPattern : IWeaponPattern
{
    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward)
    {
        return GetOffsets(weapon, forward, 1);
    }

    public IEnumerable<Vector2Int> GetOffsets(WeaponData weapon, Vector3 forward, int playerLevel)
    {
        Vector2Int f = PatternDirection.Forward(forward);
        Vector2Int r = PatternDirection.Right(f);
        string[] rows = weapon.GetPatternRowsForLevel(playerLevel);
        Vector2Int pivot = weapon.GetPatternPivotForLevel(playerLevel);

        // 패턴 배열의 pivot을 원점으로 삼아 플레이어 전방/우측 기준 타일 offset으로 변환합니다.
        for (int row = 0; row < rows.Length; row++)
        {
            string cells = rows[row];
            for (int column = 0; column < cells.Length; column++)
            {
                if (!IsPaintCell(cells[column]))
                {
                    continue;
                }

                int localX = column - pivot.x;
                int localY = pivot.y - row;
                yield return r * localX + f * localY;
            }
        }
    }

    private static bool IsPaintCell(char cell)
    {
        return cell == 'X' || cell == 'x' || cell == '1' || cell == '2' || cell == '#';
    }
}
