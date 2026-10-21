using UnityEngine;

// 봇의 실제 이동, 회전, 애니메이션, 막힘 감지를 담당합니다.
// BotPlayerController에서 이 기능을 분리한 이유는 AI 판단 코드와 물리 이동 코드를 섞지 않기 위해서입니다.
public sealed class BotMovementMotor
{
    private readonly Transform transform;
    private readonly CharacterController characterController;
    private readonly Animator animator;
    private readonly PlayerLevelStats levelStats;
    private readonly PlayerStatusEffectController statusEffects;
    private readonly float baseMoveSpeed;
    private readonly PaintOwner owner;
    private float levelMoveSpeed;

    private Vector3 lastPosition;
    private float stuckTimer;
    private bool isDirectionInverted;
    private float temporarySpeedMultiplier = 1f;
    private readonly BotGridPathfinder pathfinder;

    public float StuckTimer { get { return stuckTimer; } }
    public bool CannotReachTarget { get { return pathfinder.PathUnavailable; } }

    public void SetDirectionInverted(bool value)
    {
        isDirectionInverted = value;
    }

    public void SetTemporarySpeedMultiplier(float multiplier)
    {
        temporarySpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    public void SetTemporaryTargetSpeed(float targetSpeed)
    {
        float multiplier = statusEffects != null ? statusEffects.MovementSpeedMultiplier : 1f;
        float baseSpeed = Mathf.Max(0.001f, levelMoveSpeed * multiplier);
        temporarySpeedMultiplier = Mathf.Max(0f, targetSpeed) / baseSpeed;
    }

    // 봇 이동에 필요한 Transform, CharacterController, Animator, 성장 스탯을 묶어 보관합니다.
    public BotMovementMotor(
        Transform transform,
        CharacterController characterController,
        Animator animator,
        PlayerLevelStats levelStats,
        PlayerStatusEffectController statusEffects,
        PaintOwner owner,
        float baseMoveSpeed)
    {
        this.transform = transform;
        this.characterController = characterController;
        this.animator = animator;
        this.levelStats = levelStats;
        this.statusEffects = statusEffects;
        pathfinder = new BotGridPathfinder(owner);
        this.owner = owner;
        this.baseMoveSpeed = baseMoveSpeed;
        levelMoveSpeed = ResolveMoveSpeed();
        lastPosition = transform.position;
    }

    public void SetLevelMoveSpeed(float moveSpeed)
    {
        levelMoveSpeed = Mathf.Max(0f, moveSpeed);
    }

    // 이전 프레임 위치와 현재 위치를 비교해서 막힘 시간을 누적합니다.
    // 거의 움직이지 못하는 시간이 길어지면 BotPlayerController가 Stuck 상태로 전환합니다.
    public void UpdateStuckTimer()
    {
        float movedSqr = (transform.position - lastPosition).sqrMagnitude;
        stuckTimer = movedSqr < 0.0004f ? stuckTimer + Time.deltaTime : 0f;
        lastPosition = transform.position;
    }

    // 새 목표를 잡았거나 막힘 상태를 처리했을 때 막힘 타이머를 초기화합니다.
    public void ResetStuckTimer()
    {
        stuckTimer = 0f;
        lastPosition = transform.position;
        pathfinder.Invalidate();
    }

    // 현재 위치가 목표 타일 중심에 충분히 가까운지 확인합니다.
    // 이 값이 true면 다음 목표를 다시 뽑습니다.
    public bool HasArrived(Vector2Int targetIndex, float arriveDistance)
    {
        if (!IsTileWalkable(targetIndex))
        {
            return true;
        }

        if (pathfinder.PathTarget == targetIndex && pathfinder.PathUnavailable)
        {
            return true;
        }

        Vector3 target = TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex);
        Vector2 current2D = new Vector2(transform.position.x, transform.position.z);
        Vector2 target2D = new Vector2(target.x, target.z);
        return Vector2.Distance(current2D, target2D) <= arriveDistance;
    }

    // 목표 타일 중심을 향해 이동합니다.
    // 이동속도는 기본 이동속도에 PlayerLevelStats의 레벨 보너스를 더해서 플레이어와 같은 성장 규칙을 사용합니다.
    public void MoveToward(Vector2Int targetIndex, float arriveDistance)
    {
        if (!TilePaintManager.HasInstance ||
            characterController == null ||
            (statusEffects != null && statusEffects.IsStunned))
        {
            SetRunning(false);
            return;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (TilePaintManager.Instance.IsTileDestroyed(currentIndex))
        {
            SetRunning(false);
            return;
        }

        if (!IsTileWalkable(targetIndex))
        {
            SetRunning(false);
            return;
        }

        if (!pathfinder.TryGetNext(currentIndex, targetIndex, out Vector2Int nextIndex))
        {
            SetRunning(false);
            return;
        }

        Vector3 target = TilePaintManager.Instance.GetTileCenterWorldPosition(nextIndex);
        target.y = transform.position.y;

        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= arriveDistance * arriveDistance)
        {
            pathfinder.AdvanceWaypoint();
            SetRunning(false);
            return;
        }

        if (isDirectionInverted)
        {
            direction *= -1f;
        }

        float currentSpeed = GetCurrentMoveSpeed();
        if (currentSpeed <= 0.001f)
        {
            SetRunning(false);
            return;
        }

        Vector3 movement = direction.normalized * currentSpeed * Time.deltaTime;
        if (!CanTravelToWorldPosition(transform.position, transform.position + movement))
        {
            SetRunning(false);
            return;
        }

        characterController.Move(movement);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);
        SetRunning(true);
    }

    // 이동 여부를 Animator의 isRun 파라미터에 반영합니다.
    public void SetRunning(bool isRunning)
    {
        if (animator != null)
        {
            animator.SetBool("isRun", isRunning);
        }
    }

    // 기본 이동속도와 레벨 보너스를 합산해 이번 프레임에 사용할 이동속도를 계산합니다.
    private float GetCurrentMoveSpeed()
    {
        float multiplier = statusEffects != null ? statusEffects.MovementSpeedMultiplier : 1f;
        float tileMultiplier = TilePaintManager.HasInstance
            ? TilePaintManager.Instance.GetTileSpeedMultiplier(TilePaintManager.Instance.GetGridIndex(transform.position), owner)
            : 1f;
        return levelMoveSpeed * tileMultiplier * multiplier * temporarySpeedMultiplier;
    }

    private float ResolveMoveSpeed()
    {
        return levelStats != null ? levelStats.MoveSpeed : baseMoveSpeed;
    }

    private static bool IsTileWalkable(Vector2Int index)
    {
        return TilePaintManager.HasInstance &&
               TilePaintManager.Instance.IsTileWalkable(index) &&
               !BombMine.IsBlockingTile(index);
    }

    private static bool CanTravelToWorldPosition(Vector3 from, Vector3 to)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int startIndex = tileManager.GetGridIndex(from);
        float distance = Vector3.Distance(from, to);
        float sampleDistance = Mathf.Max(0.05f, tileManager.TileSize * 0.45f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / sampleDistance));

        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = Vector3.Lerp(from, to, i / (float)steps);
            Vector2Int sampleIndex = tileManager.GetGridIndex(sample);
            if (sampleIndex == startIndex && tileManager.IsTileDestroyed(startIndex))
            {
                continue;
            }

            if (sampleIndex == startIndex && BombMine.IsBlockingTile(startIndex))
            {
                continue;
            }

            if (!IsTileWalkable(sampleIndex))
            {
                return false;
            }
        }

        return true;
    }
}
