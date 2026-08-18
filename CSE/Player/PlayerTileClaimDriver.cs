using System;
using UnityEngine;

/// <summary>
/// PlayerMovement에서 타일 점령 체크만 분리한 helper입니다.
/// MonoBehaviour가 아니므로 프리팹 수정 없이 PlayerMovement가 직접 생성해서 사용합니다.
/// </summary>
public sealed class PlayerTileClaimDriver
{
    // 현재 플레이어 위치입니다. 매 프레임 transform 검색을 하지 않도록 생성 시점에 받습니다.
    private readonly Transform playerTransform;
    // owner는 네트워크 SyncVar 변경으로 바뀔 수 있어 필요할 때 PlayerMovement에서 읽습니다.
    private readonly Func<PaintOwner> ownerProvider;

    // 마지막으로 확인한 타일입니다. 같은 타일에 머무를 때 불필요한 점령 요청을 줄입니다.
    private Vector2Int lastTileIndex = new Vector2Int(-1, -1);
    // authority 대기/적 타일 위 반복 요청을 제한하는 다음 재시도 시간입니다.
    private float nextPaintRetryTime;

    public PlayerTileClaimDriver(Transform playerTransform, Func<PaintOwner> ownerProvider)
    {
        this.playerTransform = playerTransform;
        this.ownerProvider = ownerProvider;
    }

    /// <summary>
    /// 라운드 배치나 owner 초기화 뒤 이전 타일 캐시를 비웁니다.
    /// </summary>
    public void Reset()
    {
        lastTileIndex = new Vector2Int(-1, -1);
        nextPaintRetryTime = 0f;
    }

    /// <summary>
    /// 현재 위치의 타일 owner를 확인하고 필요할 때만 점령 요청을 보냅니다.
    /// </summary>
    public void Tick(float paintRetryInterval)
    {
        if (!TilePaintManager.HasInstance || playerTransform == null || ownerProvider == null)
        {
            return;
        }

        PaintOwner owner = ownerProvider();
        if (owner == PaintOwner.Neutral)
        {
            return;
        }

        TilePaintManager tilePaintManager = TilePaintManager.Instance;
        Vector2Int currentIndex = tilePaintManager.GetGridIndex(playerTransform.position);
        if (!tilePaintManager.IsInsideGrid(currentIndex) ||
            tilePaintManager.IsTileDestroyed(currentIndex))
        {
            return;
        }

        if (currentIndex != lastTileIndex)
        {
            lastTileIndex = currentIndex;
            nextPaintRetryTime = 0f;
        }

        if (tilePaintManager.TryGetTileOwner(currentIndex, out PaintOwner currentOwner) &&
            currentOwner != PaintOwner.Neutral)
        {
            return;
        }

        if (Time.unscaledTime < nextPaintRetryTime)
        {
            return;
        }

        nextPaintRetryTime = Time.unscaledTime + Mathf.Max(0.05f, paintRetryInterval);
        tilePaintManager.TryClaimByWorldPos(playerTransform.position, owner);
    }
}
