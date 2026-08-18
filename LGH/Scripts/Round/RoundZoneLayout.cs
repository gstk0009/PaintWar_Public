using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 파밍 구역 배치 결과를 담는 순수 데이터 클래스입니다.
/// 이 구조를 따로 두면 서버 라운드 로직과 머지할 때 편합니다.
/// 네트워크 코드는 시각화나 봇 생성 로직을 건드리지 않고 같은 구역 목록을 재사용할 수 있습니다.
/// </summary>
public sealed class RoundZoneLayout
{
    /// <summary>
    /// 생존 참가자에게 순서대로 배정할 파밍 구역 목록입니다.
    /// </summary>
    public readonly List<FarmingZone> Zones = new List<FarmingZone>();

    /// <summary>
    /// 구역 사이에 시각/물리 경계를 만들 선분 목록입니다.
    /// </summary>
    public readonly List<BoundarySegment> BoundarySegments = new List<BoundarySegment>();
}

/// <summary>
/// 생존 플레이어 수에 맞는 파밍 구역 도형을 생성합니다.
/// MonoBehaviour에 의존하지 않아서 테스트하기 쉽고, 나중에 서버 로직으로 옮기기도 편합니다.
/// </summary>
public static class RoundZoneLayoutBuilder
{
    /// <summary>
    /// 생존자 수에 맞춰 전체 맵을 1/2/3/4인용 파밍 구역으로 나눕니다.
    /// </summary>
    public static RoundZoneLayout Build(int aliveCount, int gridSize)
    {
        RoundZoneLayout layout = new RoundZoneLayout();
        float half = gridSize * 0.5f;

        if (aliveCount <= 1)
        {
            layout.Zones.Add(new FarmingZone(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(gridSize, 0f),
                new Vector2(gridSize, gridSize),
                new Vector2(0f, gridSize)
            }));
            return layout;
        }

        if (aliveCount == 4)
        {
            AddFourPlayerLayout(layout, gridSize, half);
            return layout;
        }

        if (aliveCount == 3)
        {
            AddThreePlayerLayout(layout, gridSize, half);
            return layout;
        }

        AddTwoPlayerLayout(layout, gridSize, half);
        return layout;
    }

    /// <summary>
    /// 4인 생존 상태에서 맵을 좌상/우상/좌하/우하 네 사각 구역으로 나눕니다.
    /// </summary>
    private static void AddFourPlayerLayout(RoundZoneLayout layout, int gridSize, float half)
    {
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(0f, half), new Vector2(half, half), new Vector2(half, gridSize), new Vector2(0f, gridSize) }));
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(half, half), new Vector2(gridSize, half), new Vector2(gridSize, gridSize), new Vector2(half, gridSize) }));
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(0f, 0f), new Vector2(half, 0f), new Vector2(half, half), new Vector2(0f, half) }));
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(half, 0f), new Vector2(gridSize, 0f), new Vector2(gridSize, half), new Vector2(half, half) }));

        layout.BoundarySegments.Add(new BoundarySegment(new Vector2(half, 0f), new Vector2(half, gridSize)));
        layout.BoundarySegments.Add(new BoundarySegment(new Vector2(0f, half), new Vector2(gridSize, half)));
    }

    /// <summary>
    /// 3인 생존 상태에서 위쪽 두 사각형과 아래쪽 삼각형에 가까운 구역으로 맵을 나눕니다.
    /// </summary>
    private static void AddThreePlayerLayout(RoundZoneLayout layout, int gridSize, float half)
    {
        Vector2 center = new Vector2(half, half);
        Vector2 bottomLeft = new Vector2(0f, 0f);
        Vector2 bottomRight = new Vector2(gridSize, 0f);
        Vector2 topLeft = new Vector2(0f, gridSize);
        Vector2 topRight = new Vector2(gridSize, gridSize);
        Vector2 topMiddle = new Vector2(half, gridSize);

        // P1/P2는 위쪽 좌우에서 서로 마주 보고, P3는 아래쪽 삼각형을 차지합니다.
        // 세 구역은 경계선만 공유하므로 한 타일이 두 구역에 동시에 속하면 안 됩니다.
        layout.Zones.Add(new FarmingZone(new[] { bottomLeft, center, topMiddle, topLeft }));
        layout.Zones.Add(new FarmingZone(new[] { center, bottomRight, topRight, topMiddle }));
        layout.Zones.Add(new FarmingZone(new[] { bottomLeft, bottomRight, center }));

        layout.BoundarySegments.Add(new BoundarySegment(topMiddle, center));
        layout.BoundarySegments.Add(new BoundarySegment(bottomLeft, center));
        layout.BoundarySegments.Add(new BoundarySegment(bottomRight, center));
    }

    /// <summary>
    /// 2인 생존 상태에서 맵을 좌우 두 사각 구역으로 나눕니다.
    /// </summary>
    private static void AddTwoPlayerLayout(RoundZoneLayout layout, int gridSize, float half)
    {
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(0f, 0f), new Vector2(half, 0f), new Vector2(half, gridSize), new Vector2(0f, gridSize) }));
        layout.Zones.Add(new FarmingZone(new[] { new Vector2(half, 0f), new Vector2(gridSize, 0f), new Vector2(gridSize, gridSize), new Vector2(half, gridSize) }));

        layout.BoundarySegments.Add(new BoundarySegment(new Vector2(half, 0f), new Vector2(half, gridSize)));
    }
}
