using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 생존 참가자에게 파밍 구역을 배정합니다.
/// 구역 도형 생성은 RoundZoneLayoutBuilder가 담당하고,
/// 이 클래스는 생성된 구역을 실제 참가자 데이터에 연결하는 역할만 합니다.
/// </summary>
public static class RoundZoneAssigner
{
    /// <summary>
    /// 생존 참가자를 owner 순서대로 정렬한 뒤 생성된 파밍 구역을 하나씩 연결하고 경계 선분 목록을 반환합니다.
    /// </summary>
    public static List<BoundarySegment> AssignZones(IReadOnlyList<GameParticipant> participants, TilePaintManager tileManager)
    {
        if (participants == null || tileManager == null)
        {
            return new List<BoundarySegment>();
        }

        List<GameParticipant> alive = participants
            .Where(participant => participant != null && !participant.IsEliminated)
            .OrderBy(participant => participant.Owner)
            .ToList();

        RoundZoneLayout layout = RoundZoneLayoutBuilder.Build(alive.Count, tileManager.GridSize);
        for (int i = 0; i < alive.Count; i++)
        {
            alive[i].Zone = layout.Zones[UnityEngine.Mathf.Min(i, layout.Zones.Count - 1)];
        }

        return layout.BoundarySegments;
    }
}
