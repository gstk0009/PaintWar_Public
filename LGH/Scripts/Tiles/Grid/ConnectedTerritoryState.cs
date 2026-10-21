using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class TerritoryRuleSettings
{
    [Min(1)] public int territoryTargetCount = 500;
    [Min(0.1f)] public float isolationGraceSeconds = 3f;
    [Range(1f, 2f)] public float connectedTileSpeedMultiplier = 1.08f;
    [Range(0.1f, 1f)] public float enemyTileSpeedMultiplier = 0.88f;
}

public enum TerritoryChangeKind { Isolated, Restored, Collapsed }

public enum TerritoryTileState { Neutral, OwnedUnconnected, ConnectedValid, IsolatedPending }

public struct TerritoryChange
{
    public PaintOwner Owner;
    public TerritoryChangeKind Kind;
    public int TileCount;
    public float GraceSeconds;
}

// Allocation-stable, four-neighbour territory connectivity state.
public sealed class ConnectedTerritoryState
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
    };

    private int size;
    private PaintOwner[] protectedOwners;
    private bool[] connected;
    private bool[] visited;
    private float[] isolatedSince;
    private PaintOwner[] isolatedOwner;
    private PaintOwner[] previousOwners;
    private int[] queue;
    private int[] connectedCounts = new int[5];
    private int[] isolatedCounts = new int[5];
    private int[] restoredCounts = new int[5];
    private readonly List<int> collapseBuffer = new List<int>(256);
    public bool HasIsolatedTiles { get; private set; }

    public event Action<TerritoryChange> Changed;

    public void Prepare(int gridSize)
    {
        size = Mathf.Max(0, gridSize);
        int count = size * size;
        protectedOwners = new PaintOwner[count];
        connected = new bool[count];
        visited = new bool[count];
        isolatedSince = new float[count];
        isolatedOwner = new PaintOwner[count];
        previousOwners = new PaintOwner[count];
        queue = new int[count];
        connectedCounts = new int[5];
        for (int i = 0; i < isolatedSince.Length; i++) isolatedSince[i] = -1f;
    }

    public void ClearHomes()
    {
        if (protectedOwners == null) return;
        Array.Clear(protectedOwners, 0, protectedOwners.Length);
        Array.Clear(connected, 0, connected.Length);
        Array.Clear(connectedCounts, 0, connectedCounts.Length);
        Array.Clear(isolatedOwner, 0, isolatedOwner.Length);
        Array.Clear(previousOwners, 0, previousOwners.Length);
        HasIsolatedTiles = false;
        for (int i = 0; i < isolatedSince.Length; i++) isolatedSince[i] = -1f;
    }

    public void SetHome(PaintOwner owner, Vector2Int center)
    {
        if (owner == PaintOwner.Neutral || protectedOwners == null) return;
        for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
        {
            int x = center.x + dx, y = center.y + dy;
            if (x >= 0 && y >= 0 && x < size && y < size)
                protectedOwners[x * size + y] = owner;
        }
    }

    public PaintOwner GetProtectedOwner(Vector2Int index)
    {
        return IsValid(index) ? protectedOwners[index.x * size + index.y] : PaintOwner.Neutral;
    }

    public bool IsConnected(Vector2Int index)
    {
        return IsValid(index) && connected[index.x * size + index.y];
    }

    public bool IsIsolated(Vector2Int index)
    {
        return IsValid(index) && isolatedSince[index.x * size + index.y] >= 0f;
    }

    public bool IsIsolatedFlat(int flatIndex)
    {
        return isolatedSince != null && flatIndex >= 0 && flatIndex < isolatedSince.Length &&
               isolatedSince[flatIndex] >= 0f;
    }

    public TerritoryTileState GetTileState(Vector2Int index, PaintOwner owner)
    {
        if (!IsValid(index) || owner == PaintOwner.Neutral) return TerritoryTileState.Neutral;
        int flat = index.x * size + index.y;
        if (isolatedSince[flat] >= 0f && isolatedOwner[flat] == owner)
            return TerritoryTileState.IsolatedPending;
        return connected[flat] ? TerritoryTileState.ConnectedValid : TerritoryTileState.OwnedUnconnected;
    }

    public int GetIsolatedCount(PaintOwner owner)
    {
        int value = (int)owner;
        return value > 0 && value < isolatedCounts.Length ? isolatedCounts[value] : 0;
    }

    public int GetConnectedCount(PaintOwner owner)
    {
        int i = (int)owner;
        return i > 0 && i < connectedCounts.Length ? connectedCounts[i] : 0;
    }

    public void Recalculate(PaintOwner[] owners, bool[] destroyed, float now)
    {
        if (owners == null || owners.Length != size * size) return;
        bool[] wasConnected = connected;
        connected = visited;
        visited = wasConnected;
        Array.Clear(connected, 0, connected.Length);
        Array.Clear(connectedCounts, 0, connectedCounts.Length);
        HasIsolatedTiles = false;

        for (int ownerValue = 1; ownerValue <= 4; ownerValue++)
        {
            PaintOwner owner = (PaintOwner)ownerValue;
            int head = 0, tail = 0;
            for (int flat = 0; flat < protectedOwners.Length; flat++)
            {
                if (protectedOwners[flat] != owner || owners[flat] != owner || connected[flat]) continue;
                connected[flat] = true;
                queue[tail++] = flat;
            }
            while (head < tail)
            {
                int flat = queue[head++];
                connectedCounts[ownerValue]++;
                int x = flat / size, y = flat % size;
                for (int d = 0; d < Directions.Length; d++)
                {
                    int nx = x + Directions[d].x, ny = y + Directions[d].y;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    int next = nx * size + ny;
                    if (connected[next] || (destroyed != null && destroyed[next]) || owners[next] != owner) continue;
                    connected[next] = true;
                    queue[tail++] = next;
                }
            }
        }

        Array.Clear(isolatedCounts, 0, isolatedCounts.Length);
        Array.Clear(restoredCounts, 0, restoredCounts.Length);
        for (int i = 0; i < owners.Length; i++)
        {
            PaintOwner owner = owners[i];
            bool protectedTile = protectedOwners[i] != PaintOwner.Neutral;
            bool sameOwnerAsPreviousBatch = owner != PaintOwner.Neutral && previousOwners[i] == owner;
            bool newlyIsolated = sameOwnerAsPreviousBatch && visited[i] && !connected[i];
            bool remainsPending = isolatedSince[i] >= 0f && isolatedOwner[i] == owner;

            if (owner != PaintOwner.Neutral && connected[i])
            {
                if (remainsPending)
                {
                    restoredCounts[(int)owner]++;
                }
                isolatedSince[i] = -1f;
                isolatedOwner[i] = PaintOwner.Neutral;
            }
            else if (owner != PaintOwner.Neutral && !protectedTile && (remainsPending || newlyIsolated))
            {
                HasIsolatedTiles = true;
                if (!remainsPending)
                {
                    isolatedSince[i] = now;
                    isolatedOwner[i] = owner;
                    isolatedCounts[(int)owner]++;
                }
            }
            else
            {
                // Newly painted disconnected tiles stay owned but never enter the old owner's grace timer.
                isolatedSince[i] = -1f;
                isolatedOwner[i] = PaintOwner.Neutral;
            }
            previousOwners[i] = owner;
        }
        PublishCounts(isolatedCounts, TerritoryChangeKind.Isolated);
        PublishCounts(restoredCounts, TerritoryChangeKind.Restored);
    }

    public List<int> CollectExpired(PaintOwner[] owners, float now, float graceSeconds)
    {
        collapseBuffer.Clear();
        for (int i = 0; i < isolatedSince.Length; i++)
            if (isolatedSince[i] >= 0f && now - isolatedSince[i] >= graceSeconds &&
                owners[i] == isolatedOwner[i] && isolatedOwner[i] != PaintOwner.Neutral && !connected[i])
                collapseBuffer.Add(i);
        return collapseBuffer;
    }

    public void PublishCollapsed(PaintOwner owner, int count, float graceSeconds)
    {
        if (count > 0) Changed?.Invoke(new TerritoryChange
        { Owner = owner, Kind = TerritoryChangeKind.Collapsed, TileCount = count, GraceSeconds = graceSeconds });
    }

    private void PublishCounts(int[] counts, TerritoryChangeKind kind)
    {
        for (int i = 1; i <= 4; i++) if (counts[i] > 0) Changed?.Invoke(new TerritoryChange
        { Owner = (PaintOwner)i, Kind = kind, TileCount = counts[i] });
    }

    private bool IsValid(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < size && p.y < size;
}
