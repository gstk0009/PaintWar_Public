using UnityEngine;

/// <summary>
/// 타일 격자의 크기, 타일 크기, 원점 기준 좌표 변환만 담당합니다.
/// </summary>
public sealed class TileGridMapper
{
    public int GridSize { get; private set; }
    public float TileSize { get; private set; }
    public Vector3 GridOrigin { get; private set; }
    public bool IsReady { get { return GridSize > 0 && TileSize > 0f; } }

    public void Configure(int gridSize, float tileSize, Vector3 gridOrigin)
    {
        GridSize = Mathf.Max(0, gridSize);
        TileSize = tileSize;
        GridOrigin = gridOrigin;
    }

    /// <summary>
    /// 월드 좌표를 가장 가까운 타일 인덱스로 변환합니다.
    /// +tileSize/2 보정은 현재 TileGenerator의 타일 배치 방식과 맞추기 위한 값입니다.
    /// </summary>
    public Vector2Int GetGridIndex(Vector3 worldPosition)
    {
        if (TileSize <= 0f)
        {
            return Vector2Int.zero;
        }

        int x = Mathf.FloorToInt((worldPosition.x - GridOrigin.x + (TileSize * 0.5f)) / TileSize);
        int z = Mathf.FloorToInt((worldPosition.z - GridOrigin.z + (TileSize * 0.5f)) / TileSize);
        return new Vector2Int(x, z);
    }

    public Vector3 GetWorldPosition(Vector2Int index)
    {
        return GridOrigin + new Vector3(index.x * TileSize, 0f, index.y * TileSize);
    }

    public Vector3 GetTileCenterWorldPosition(Vector2Int index)
    {
        return GetWorldPosition(index);
    }

    public Vector2Int ClampIndex(Vector2Int index)
    {
        if (GridSize <= 0)
        {
            return Vector2Int.zero;
        }

        return new Vector2Int(
            Mathf.Clamp(index.x, 0, GridSize - 1),
            Mathf.Clamp(index.y, 0, GridSize - 1));
    }
}
