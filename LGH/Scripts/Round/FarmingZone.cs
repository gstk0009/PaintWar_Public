using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타일 격자 좌표 기준의 폴리곤 파밍 구역입니다.
/// TilePaintManager는 타일 중심점이 현재 플레이어 구역에 포함되는지 이 클래스를 통해 확인합니다.
/// </summary>
public class FarmingZone
{
    private const float EdgeTolerance = 0.001f;
    private const float SafeSpawnEdgePadding = 1.25f;

    private readonly List<Vector2> points;

    /// <summary>
    /// 입력받은 꼭짓점 목록을 파밍 구역 폴리곤으로 저장합니다.
    /// </summary>
    public FarmingZone(IEnumerable<Vector2> polygonPoints)
    {
        points = new List<Vector2>(polygonPoints);
    }

    public IReadOnlyList<Vector2> Points { get { return points; } }

    /// <summary>
    /// 타일 인덱스의 중심점이 이 폴리곤 안에 있으면 true를 반환합니다.
    /// 타일 중심 기준으로 판단해서 2페이즈 삼각형 대각선 경계의 애매한 소유권 문제를 줄입니다.
    /// </summary>
    public bool Contains(Vector2Int index)
    {
        return Contains(GetTileCenterPoint(index));
    }

    /// <summary>
    /// 타일이 구역 안쪽에 있고 경계선과 너무 가깝지 않을 때만 true를 반환합니다.
    /// 봇은 이 함수를 사용해서 삼각형 모서리에서 떨리는 타겟 선택을 피합니다.
    /// </summary>
    public bool ContainsSafely(Vector2Int index, float edgePadding = SafeSpawnEdgePadding)
    {
        Vector2 point = GetTileCenterPoint(index);
        return Contains(point) && GetDistanceToEdge(point) >= edgePadding;
    }

    /// <summary>
    /// 임의의 격자 좌표 점이 폴리곤 내부 또는 경계 위에 있는지 ray casting으로 판정합니다.
    /// </summary>
    public bool Contains(Vector2 point)
    {
        if (points.Count < 3)
        {
            return false;
        }

        if (IsNearEdge(point, EdgeTolerance))
        {
            return true;
        }

        bool inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[j];

            // 표준 ray casting 방식입니다.
            // 분모의 부호를 강제로 양수로 바꾸면 아래 방향 대각선 경계가 깨져 2페이즈 삼각형 판정이 새어 나갈 수 있습니다.
            bool crossesY = (a.y > point.y) != (b.y > point.y);
            if (!crossesY)
            {
                continue;
            }

            float xOnEdge = a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y);
            if (point.x < xOnEdge)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// 폴리곤 중심 근처의 스폰 타일을 찾습니다.
    /// 중심점이 경계에 너무 가까우면 안쪽으로 조금 들어간 안전한 타일을 대신 고릅니다.
    /// </summary>
    public Vector2Int GetSpawnIndex(int gridSize)
    {
        Vector2 center = GetCenter();
        Vector2Int preferred = ClampIndex(new Vector2Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y)), gridSize);
        if (ContainsSafely(preferred, SafeSpawnEdgePadding))
        {
            return preferred;
        }

        int maxRadius = Mathf.Max(2, gridSize / 2);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Vector2Int best = preferred;
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int x = preferred.x - radius; x <= preferred.x + radius; x++)
            {
                for (int y = preferred.y - radius; y <= preferred.y + radius; y++)
                {
                    if (x != preferred.x - radius && x != preferred.x + radius && y != preferred.y - radius && y != preferred.y + radius)
                    {
                        continue;
                    }

                    Vector2Int candidate = ClampIndex(new Vector2Int(x, y), gridSize);
                    if (!ContainsSafely(candidate, SafeSpawnEdgePadding))
                    {
                        continue;
                    }

                    float distance = (GetTileCenterPoint(candidate) - center).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        found = true;
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            if (found)
            {
                return best;
            }
        }

        return preferred;
    }

    /// <summary>
    /// 폴리곤 꼭짓점들의 평균 위치를 반환합니다.
    /// </summary>
    public Vector2 GetCenter()
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
        {
            sum += points[i];
        }

        return points.Count > 0 ? sum / points.Count : Vector2.zero;
    }

    /// <summary>
    /// 주어진 점에서 폴리곤 경계선까지의 가장 짧은 거리를 계산합니다.
    /// </summary>
    public float GetDistanceToEdge(Vector2 point)
    {
        if (points.Count == 0)
        {
            return 0f;
        }

        float bestSqrDistance = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            Vector2 closest = ClosestPointOnSegment(a, b, point);
            bestSqrDistance = Mathf.Min(bestSqrDistance, (closest - point).sqrMagnitude);
        }

        return Mathf.Sqrt(bestSqrDistance);
    }

    /// <summary>
    /// 타일 격자 좌표를 TilePaintManager 기준 월드 좌표로 변환합니다.
    /// </summary>
    public static Vector3 GridPointToWorld(Vector2 point, TilePaintManager tileManager)
    {
        return tileManager.GridOrigin + new Vector3(
            (point.x - 0.5f) * tileManager.TileSize,
            0f,
            (point.y - 0.5f) * tileManager.TileSize);
    }

    /// <summary>
    /// 타일 인덱스의 중심점을 격자 좌표로 반환합니다.
    /// </summary>
    private static Vector2 GetTileCenterPoint(Vector2Int index)
    {
        return new Vector2(index.x + 0.5f, index.y + 0.5f);
    }

    /// <summary>
    /// 타일 인덱스가 그리드 밖으로 나가지 않도록 0부터 gridSize - 1 사이로 제한합니다.
    /// </summary>
    private static Vector2Int ClampIndex(Vector2Int index, int gridSize)
    {
        return new Vector2Int(
            Mathf.Clamp(index.x, 0, gridSize - 1),
            Mathf.Clamp(index.y, 0, gridSize - 1));
    }

    /// <summary>
    /// 점이 폴리곤 경계선에서 epsilon 이내에 있는지 확인합니다.
    /// </summary>
    private bool IsNearEdge(Vector2 point, float epsilon)
    {
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            if ((ClosestPointOnSegment(a, b, point) - point).sqrMagnitude <= epsilon * epsilon)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 선분 AB 위에서 point와 가장 가까운 점을 반환합니다.
    /// </summary>
    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(point - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude);
        return a + Mathf.Clamp01(t) * ab;
    }
}
