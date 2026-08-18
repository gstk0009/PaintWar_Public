using UnityEngine;

/// <summary>
/// 봇이 장애물과 폭탄을 피해 이동할 수 있도록 4방향 최단 경로를 계산합니다.
/// 50x50 고정 크기 배열을 재사용해서 경로 갱신 시 GC를 만들지 않습니다.
/// </summary>
public sealed class BotGridPathfinder
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
    };

    private int gridSize;
    private readonly PaintOwner owner;
    private int[] visitStamp;
    private int[] parent;
    private int[] queue;
    private int[] path;
    private int stamp;
    private int pathLength;
    private int waypoint;
    private int pathStart;
    private Vector2Int pathTarget = new Vector2Int(int.MinValue, int.MinValue);

    public bool PathUnavailable { get; private set; }
    public Vector2Int PathTarget { get { return pathTarget; } }

    public BotGridPathfinder(PaintOwner owner)
    {
        this.owner = owner;
    }

    public void Invalidate()
    {
        pathTarget = new Vector2Int(int.MinValue, int.MinValue);
        pathLength = 0;
        waypoint = 0;
        PathUnavailable = false;
    }

    public bool TryGetNext(Vector2Int current, Vector2Int target, out Vector2Int next)
    {
        next = current;
        if (!TilePaintManager.HasInstance) return false;
        EnsureCapacity(TilePaintManager.Instance.GridSize);
        if (pathTarget == target && PathUnavailable) return false;

        bool needsPath = pathTarget != target || pathLength == 0;
        if (!needsPath && waypoint < pathLength)
        {
            Vector2Int expected = FromFlat(path[waypoint]);
            Vector2Int previous = waypoint > 0 ? FromFlat(path[waypoint - 1]) : FromFlat(pathStart);
            if (current != expected && current != previous) needsPath = true;
            if (waypoint < pathLength && !IsWalkable(FromFlat(path[waypoint]))) needsPath = true;
        }

        if (needsPath && !Build(current, target)) return false;
        if (waypoint >= pathLength) return false;
        next = FromFlat(path[waypoint]);
        return true;
    }

    public void AdvanceWaypoint()
    {
        if (waypoint < pathLength) waypoint++;
    }

    private bool Build(Vector2Int start, Vector2Int target)
    {
        pathTarget = target;
        pathLength = 0;
        waypoint = 0;
        PathUnavailable = false;
        if (!InBounds(start) || !InBounds(target) || !IsWalkable(target))
        {
            PathUnavailable = true;
            return false;
        }
        if (start == target) return false;

        stamp++;
        if (stamp == int.MaxValue)
        {
            System.Array.Clear(visitStamp, 0, visitStamp.Length);
            stamp = 1;
        }
        int startFlat = Flat(start);
        pathStart = startFlat;
        int targetFlat = Flat(target);
        int head = 0, tail = 0;
        queue[tail++] = startFlat;
        visitStamp[startFlat] = stamp;
        parent[startFlat] = -1;

        while (head < tail && visitStamp[targetFlat] != stamp)
        {
            int currentFlat = queue[head++];
            Vector2Int current = FromFlat(currentFlat);
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int candidate = current + Directions[i];
                if (!InBounds(candidate)) continue;
                int candidateFlat = Flat(candidate);
                if (visitStamp[candidateFlat] == stamp || !IsWalkable(candidate)) continue;
                visitStamp[candidateFlat] = stamp;
                parent[candidateFlat] = currentFlat;
                queue[tail++] = candidateFlat;
            }
        }

        if (visitStamp[targetFlat] != stamp)
        {
            PathUnavailable = true;
            return false;
        }

        int cursor = targetFlat;
        while (cursor != startFlat && cursor >= 0 && pathLength < path.Length)
        {
            path[pathLength++] = cursor;
            cursor = parent[cursor];
        }
        for (int left = 0, right = pathLength - 1; left < right; left++, right--)
        {
            int temp = path[left]; path[left] = path[right]; path[right] = temp;
        }
        return pathLength > 0;
    }

    private void EnsureCapacity(int size)
    {
        if (gridSize == size && queue != null) return;
        gridSize = size;
        int count = size * size;
        visitStamp = new int[count];
        parent = new int[count];
        queue = new int[count];
        path = new int[count];
        Invalidate();
    }

    private bool InBounds(Vector2Int p)
    {
        return p.x >= 0 && p.y >= 0 && p.x < gridSize && p.y < gridSize;
    }

    private int Flat(Vector2Int p) { return p.x * gridSize + p.y; }
    private Vector2Int FromFlat(int value) { return new Vector2Int(value / gridSize, value % gridSize); }

    private bool IsWalkable(Vector2Int p)
    {
        return TilePaintManager.HasInstance && TilePaintManager.Instance.IsTileWalkable(p) &&
               !BombMine.IsBlockingTile(p) && !BombMine.IsEnemyBlastDangerTile(p, owner);
    }
}
