using UnityEngine;

/// <summary>
/// 파밍 경계 렌더러와 콜라이더 생성에 사용하는 격자 좌표 선분입니다.
/// 두 점은 월드 좌표가 아니라 타일 격자 좌표를 사용합니다.
/// </summary>
public readonly struct BoundarySegment
{
    public readonly Vector2 Start;
    public readonly Vector2 End;

    /// <summary>
    /// 격자 좌표 기준 시작점과 끝점을 가진 경계 선분을 만듭니다.
    /// </summary>
    public BoundarySegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }
}
