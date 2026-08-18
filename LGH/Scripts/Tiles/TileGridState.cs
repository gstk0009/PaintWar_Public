using System;
using UnityEngine;

/// <summary>
/// 타일 소유자 데이터와 owner별 점령 수만 관리하는 순수 상태 클래스입니다.
/// Renderer, Material, NetworkBehaviour를 모르도록 유지합니다.
/// </summary>
public sealed class TileGridState
{
    private PaintOwner[,] owners;
    private bool[,] destroyed;
    private int[] ownerCounts = new int[5];

    public int GridSize { get; private set; }
    public int TotalTileCount { get { return GridSize * GridSize; } }
    public int ActiveTileCount
    {
        get
        {
            if (ownerCounts == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < ownerCounts.Length; i++)
            {
                count += ownerCounts[i];
            }

            return count;
        }
    }
    public bool IsReady { get { return owners != null && GridSize > 0; } }

    public void Prepare(int gridSize)
    {
        GridSize = Mathf.Max(0, gridSize);
        owners = new PaintOwner[GridSize, GridSize];
        destroyed = new bool[GridSize, GridSize];
        ownerCounts = new int[5];
        if (ownerCounts.Length > (int)PaintOwner.Neutral)
        {
            ownerCounts[(int)PaintOwner.Neutral] = TotalTileCount;
        }
    }

    public bool IsValidIndex(Vector2Int index)
    {
        return owners != null
            && index.x >= 0
            && index.x < GridSize
            && index.y >= 0
            && index.y < GridSize;
    }

    public bool IsDestroyed(Vector2Int index)
    {
        return IsValidIndex(index) && destroyed != null && destroyed[index.x, index.y];
    }

    public bool TryGetOwner(Vector2Int index, out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        if (!IsValidIndex(index))
        {
            return false;
        }

        owner = owners[index.x, index.y];
        return true;
    }

    public bool SetOwner(Vector2Int index, PaintOwner newOwner)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        PaintOwner previousOwner = owners[index.x, index.y];
        if (previousOwner == newOwner || IsDestroyed(index))
        {
            return false;
        }

        UpdateOwnerCount(previousOwner, -1);
        UpdateOwnerCount(newOwner, 1);
        owners[index.x, index.y] = newOwner;
        return true;
    }

    public bool SetDestroyed(Vector2Int index, bool isDestroyed)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        if (destroyed[index.x, index.y] == isDestroyed)
        {
            return false;
        }

        destroyed[index.x, index.y] = isDestroyed;
        PaintOwner currentOwner = owners[index.x, index.y];
        if (isDestroyed)
        {
            UpdateOwnerCount(currentOwner, -1);
            owners[index.x, index.y] = PaintOwner.Neutral;
        }
        else
        {
            UpdateOwnerCount(PaintOwner.Neutral, 1);
        }

        return true;
    }

    public int CountOwnedTiles(PaintOwner owner)
    {
        if (owner == PaintOwner.Neutral || ownerCounts == null)
        {
            return 0;
        }

        int ownerIndex = (int)owner;
        return ownerIndex >= 0 && ownerIndex < ownerCounts.Length ? ownerCounts[ownerIndex] : 0;
    }

    public int[] GetOwnerCounts()
    {
        int[] counts = new int[5];
        if (ownerCounts != null)
        {
            Array.Copy(ownerCounts, counts, Mathf.Min(ownerCounts.Length, counts.Length));
        }

        return counts;
    }

    public PaintOwner[] CreateSnapshot()
    {
        if (!IsReady)
        {
            return Array.Empty<PaintOwner>();
        }

        PaintOwner[] snapshot = new PaintOwner[TotalTileCount];
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                snapshot[x * GridSize + y] = owners[x, y];
            }
        }

        return snapshot;
    }

    public void FillSnapshot(PaintOwner[] ownerDestination, bool[] destroyedDestination)
    {
        if (!IsReady || ownerDestination == null || ownerDestination.Length < TotalTileCount) return;
        for (int x = 0; x < GridSize; x++) for (int y = 0; y < GridSize; y++)
        {
            int flat = x * GridSize + y;
            ownerDestination[flat] = owners[x, y];
            if (destroyedDestination != null && destroyedDestination.Length > flat)
                destroyedDestination[flat] = destroyed[x, y];
        }
    }

    public bool[] CreateDestroyedSnapshot()
    {
        if (!IsReady)
        {
            return Array.Empty<bool>();
        }

        bool[] snapshot = new bool[TotalTileCount];
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                snapshot[x * GridSize + y] = destroyed != null && destroyed[x, y];
            }
        }

        return snapshot;
    }

    public void ApplySnapshot(PaintOwner[] snapshot, bool[] destroyedSnapshot)
    {
        if (!IsReady || snapshot == null)
        {
            return;
        }

        Array.Clear(ownerCounts, 0, ownerCounts.Length);
        for (int i = 0; i < TotalTileCount; i++)
        {
            int x = i / GridSize;
            int y = i % GridSize;
            bool isDestroyed = destroyedSnapshot != null &&
                i < destroyedSnapshot.Length &&
                destroyedSnapshot[i];
            PaintOwner owner = i < snapshot.Length ? snapshot[i] : PaintOwner.Neutral;
            if (isDestroyed)
            {
                owner = PaintOwner.Neutral;
            }

            if (destroyed != null)
            {
                destroyed[x, y] = isDestroyed;
            }

            owners[x, y] = owner;
            if (!isDestroyed)
            {
                UpdateOwnerCount(owner, 1);
            }
        }
    }

    public void ResetAll(PaintOwner owner = PaintOwner.Neutral)
    {
        if (!IsReady)
        {
            return;
        }

        Array.Clear(ownerCounts, 0, ownerCounts.Length);
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (destroyed != null)
                {
                    destroyed[x, y] = false;
                }

                owners[x, y] = owner;
            }
        }

        UpdateOwnerCount(owner, TotalTileCount);
    }

    private void UpdateOwnerCount(PaintOwner owner, int delta)
    {
        if (ownerCounts == null)
        {
            return;
        }

        int ownerIndex = (int)owner;
        if (ownerIndex >= 0 && ownerIndex < ownerCounts.Length)
        {
            ownerCounts[ownerIndex] = Mathf.Max(0, ownerCounts[ownerIndex] + delta);
        }
    }
}
