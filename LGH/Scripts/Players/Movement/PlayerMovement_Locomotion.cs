using UnityEngine;

// PlayerMovement의 일반 이동, grid-step 이동, 타일 점령 tick을 담당합니다.
public partial class PlayerMovement
{
    private void HandleMove()
    {
        currentSpeed = GetCurrentMoveSpeed();
        Vector3 move = GetEightDirectionWorldInput();

        if (currentSpeed <= 0.001f)
        {
            CancelSoftGridSnap();
            SetRunning(false);
            return;
        }

        if (move != Vector3.zero)
        {
            CancelSoftGridSnap();
            Vector3 movement = move * currentSpeed * Time.deltaTime;
            Vector3 assistedMovement = movement +
                GetSoftGridLaneAssistMovement(move);
            if (assistedMovement != movement &&
                CanMoveToWorldPosition(transform.position + assistedMovement))
            {
                movement = assistedMovement;
            }

            if (!CanMoveToWorldPosition(transform.position + movement))
            {
                RotateTowardDirection(move);
                SetRunning(true);
                return;
            }

            characterController.Move(movement);
            if (GameRoundManager.HasInstance)
            {
                GameRoundManager.Instance.ClampTransformToOwnerZone(GetCurrentOwner(), transform);
            }

            Quaternion targetRotation = Quaternion.LookRotation(move);
            float rotationBlend =
                1f - Mathf.Exp(-movementRotationSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationBlend);
            SetRunning(true);
        }
        else
        {
            HandleSoftGridSnap();
            SetRunning(false);
        }
    }

    private Vector3 GetSoftGridLaneAssistMovement(Vector3 moveDirection)
    {
        if (!useSoftGridAssist ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady ||
            softGridLaneAssistStrength <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 direction = moveDirection.normalized;
        float absX = Mathf.Abs(direction.x);
        float absZ = Mathf.Abs(direction.z);
        // Diagonal input stays fully free. Only a clearly dominant cardinal axis
        // receives perpendicular lane assistance.
        if (Mathf.Min(absX, absZ) * 1.5f >= Mathf.Max(absX, absZ))
        {
            return Vector3.zero;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int tileIndex = tileManager.GetGridIndex(transform.position);
        Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(tileIndex);
        float maxCorrection = currentSpeed * softGridLaneAssistStrength * Time.deltaTime;
        Vector3 correction = Vector3.zero;
        if (absX > absZ)
        {
            correction.z = Mathf.Clamp(
                tileCenter.z - transform.position.z,
                -maxCorrection,
                maxCorrection);
        }
        else
        {
            correction.x = Mathf.Clamp(
                tileCenter.x - transform.position.x,
                -maxCorrection,
                maxCorrection);
        }

        return correction;
    }

    private void HandleSoftGridSnap()
    {
        if (!useSoftGridAssist ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            CancelSoftGridSnap();
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        if (!isSoftGridSnapping)
        {
            Vector2Int tileIndex = tileManager.GetGridIndex(transform.position);
            if (!tileManager.IsInsideGrid(tileIndex) ||
                tileManager.IsTileDestroyed(tileIndex))
            {
                return;
            }

            Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(tileIndex);
            tileCenter.y = transform.position.y;
            Vector3 offset = tileCenter - transform.position;
            offset.y = 0f;
            float snapRadius = tileManager.TileSize * softGridSnapRadiusRatio;
            if (offset.sqrMagnitude > snapRadius * snapRadius ||
                !CanMoveToWorldPosition(tileCenter))
            {
                return;
            }

            softGridSnapTarget = tileCenter;
            isSoftGridSnapping = true;
        }

        Vector3 toTarget = softGridSnapTarget - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance <= softGridSnapArrivalTolerance)
        {
            CancelSoftGridSnap();
            return;
        }

        float snapSpeed = tileManager.TileSize * softGridSnapRadiusRatio /
            Mathf.Max(0.05f, softGridSnapDuration);
        Vector3 movement = toTarget.normalized *
            Mathf.Min(distance, snapSpeed * Time.deltaTime);
        if (!CanMoveToWorldPosition(transform.position + movement))
        {
            CancelSoftGridSnap();
            return;
        }

        characterController.Move(movement);
    }

    private void CancelSoftGridSnap()
    {
        isSoftGridSnapping = false;
        softGridSnapTarget = transform.position;
    }

    private Vector3 GetEightDirectionWorldInput()
    {
        Joystick activeJoystick = GetActiveJoystick();
        Vector2 input = activeJoystick != null
            ? new Vector2(activeJoystick.Horizontal, activeJoystick.Vertical)
            : Vector2.zero;

        if (isControlInverted)
        {
            input *= -1f;
        }

        Vector2 deadZoneInput = ApplyMovementRadialDeadZone(input);
        Vector2 filteredInput = FilterMovementToEightDirections(deadZoneInput);
        return GetCameraRelativeMovementDirection(filteredInput);
    }

    public Vector3 GetSkillAimWorldInput()
    {
        Joystick activeJoystick = GetActiveJoystick();
        Vector2 input = activeJoystick != null
            ? new Vector2(activeJoystick.Horizontal, activeJoystick.Vertical)
            : Vector2.zero;

        if (isControlInverted)
        {
            input *= -1f;
        }

        return GetCameraRelativeMovementDirection(
            ApplyMovementRadialDeadZone(input));
    }

    public Transform GetMovementCameraTransform()
    {
        if (movementCameraTransform == null && localOwnerControls != null)
        {
            movementCameraTransform = localOwnerControls.CameraTransform;
        }

        return movementCameraTransform;
    }

    private Vector2 ApplyMovementRadialDeadZone(Vector2 input)
    {
        float magnitude = Mathf.Clamp01(input.magnitude);
        if (magnitude <= movementRadialDeadZone)
        {
            return Vector2.zero;
        }

        float rescaledMagnitude = Mathf.InverseLerp(
            movementRadialDeadZone,
            1f,
            magnitude);
        return input.normalized * rescaledMagnitude;
    }

    private Vector2 FilterMovementToEightDirections(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            hasRetainedMovementSector = false;
            return Vector2.zero;
        }

        const int sectorCount = 8;
        const float sectorSize = 360f / sectorCount;
        float angle = Mathf.Repeat(
            Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg,
            360f);
        int nearestSector = Mathf.RoundToInt(angle / sectorSize) % sectorCount;

        if (!hasRetainedMovementSector)
        {
            retainedMovementSector = nearestSector;
            hasRetainedMovementSector = true;
        }
        else
        {
            float retainedAngle = retainedMovementSector * sectorSize;
            float distanceFromRetained = Mathf.Abs(
                Mathf.DeltaAngle(retainedAngle, angle));
            if (distanceFromRetained >
                sectorSize * 0.5f + movementDirectionHysteresisDegrees)
            {
                retainedMovementSector = nearestSector;
            }
        }

        float snappedRadians = retainedMovementSector * sectorSize * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(
            Mathf.Cos(snappedRadians),
            Mathf.Sin(snappedRadians));
        return direction * input.magnitude;
    }

    private Vector3 GetCameraRelativeMovementDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        GetMovementCameraTransform();

        if (movementCameraTransform == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 screenRight = movementCameraTransform.right;
        screenRight.y = 0f;
        screenRight.Normalize();

        Vector3 screenUp = Vector3.ProjectOnPlane(
            movementCameraTransform.up,
            Vector3.up);
        screenUp.Normalize();

        Vector3 worldDirection = screenRight * input.x + screenUp * input.y;
        return worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized * input.magnitude
            : Vector3.zero;
    }

    private void HandleGridStepMove()
    {
        currentSpeed = GetCurrentMoveSpeed();
        if (currentSpeed <= 0.001f)
        {
            if (isStepMoving)
            {
                CancelCurrentStep(false);
            }

            SetRunning(false);
            return;
        }

        if (isStepMoving)
        {
            MoveTowardStepTarget();
            return;
        }

        Vector2Int direction = GetQuantizedInputDirection();
        if (direction == Vector2Int.zero)
        {
            SetRunning(false);
            return;
        }

        if (!TryGetNextStepTarget(direction, out Vector3 targetPosition))
        {
            RotateTowardDirection(new Vector3(direction.x, 0f, direction.y));
            SetRunning(true);
            return;
        }

        BeginGridStep(targetPosition, direction);
        MoveTowardStepTarget();
    }

    private void BeginGridStep(Vector3 targetPosition, Vector2Int direction)
    {
        stepTargetPosition = targetPosition;
        isStepMoving = true;
        RotateTowardDirection(new Vector3(direction.x, 0f, direction.y));
        SetRunning(true);
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.PlayMovementStep();
        }
    }

    private void MoveTowardStepTarget()
    {
        float remainingDistance = Mathf.Max(0f, GetCurrentMoveSpeed()) * Time.deltaTime;
        int transitions = 0;

        // High speed boosts may cross more than one tile in a frame, so cap chained work.
        while (isStepMoving && remainingDistance > 0f &&
               transitions < maxStepTransitionsPerFrame)
        {
            Vector3 toTarget = stepTargetPosition - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= stepArrivalTolerance)
            {
                CompleteCurrentStep();
                transitions++;
                continue;
            }

            float travelDistance = Mathf.Min(remainingDistance, distance);
            Vector3 movement = toTarget * (travelDistance / distance);
            Vector3 positionBeforeMove = transform.position;
            CollisionFlags collisionFlags = characterController.Move(movement);
            Vector3 actualMovement = transform.position - positionBeforeMove;
            actualMovement.y = 0f;

            float forwardProgress = Vector3.Dot(actualMovement, movement.normalized);
            if ((collisionFlags & CollisionFlags.Sides) != 0 &&
                forwardProgress < travelDistance * 0.1f)
            {
                CancelCurrentStep(true);
                break;
            }

            remainingDistance -= travelDistance;
            RotateTowardDirection(toTarget);

            if (travelDistance >= distance - stepArrivalTolerance)
            {
                CompleteCurrentStep();
                transitions++;
            }
        }
    }

    private void CompleteCurrentStep()
    {
        isStepMoving = false;
        Vector2Int nextDirection = GetQuantizedInputDirection();
        if (nextDirection != Vector2Int.zero &&
            TryGetNextStepTarget(nextDirection, out Vector3 nextTargetPosition))
        {
            BeginGridStep(nextTargetPosition, nextDirection);
            return;
        }

        SetRunning(nextDirection != Vector2Int.zero);
    }

    private void CancelCurrentStep()
    {
        CancelCurrentStep(true);
    }

    private void CancelCurrentStep(bool keepInputRunning)
    {
        isStepMoving = false;
        stepTargetPosition = transform.position;
        SetRunning(keepInputRunning && GetQuantizedInputDirection() != Vector2Int.zero);
    }

    private bool TryGetNextStepTarget(Vector2Int direction, out Vector3 targetPosition)
    {
        targetPosition = transform.position;
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        Vector2Int targetIndex = currentIndex + direction;
        if (!IsTileWalkable(targetIndex))
        {
            return false;
        }

        PaintOwner owner = GetCurrentOwner();
        if (GameRoundManager.HasInstance && !GameRoundManager.Instance.CanOwnerClaimTile(owner, targetIndex))
        {
            return false;
        }

        Vector3 tileCenter = TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex);
        targetPosition = new Vector3(tileCenter.x, transform.position.y, tileCenter.z);
        return true;
    }

    private bool CanMoveToWorldPosition(Vector3 worldPosition)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return true;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        Vector2Int targetIndex = TilePaintManager.Instance.GetGridIndex(worldPosition);
        if (!CanTravelToWorldPosition(transform.position, worldPosition, currentIndex))
        {
            return false;
        }

        PaintOwner owner = GetCurrentOwner();
        return !GameRoundManager.HasInstance ||
               GameRoundManager.Instance.CanOwnerClaimTile(owner, targetIndex);
    }

    private static bool CanTravelToWorldPosition(
        Vector3 from,
        Vector3 to,
        Vector2Int startIndex)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        float distance = Vector3.Distance(from, to);
        float sampleDistance = Mathf.Max(0.05f, tileManager.TileSize * 0.45f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / sampleDistance));
        Vector2Int previousIndex = startIndex;

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

            Vector2Int indexDelta = sampleIndex - previousIndex;
            if (indexDelta.x != 0 && indexDelta.y != 0)
            {
                Vector2Int horizontalIndex = previousIndex +
                    new Vector2Int(indexDelta.x, 0);
                Vector2Int verticalIndex = previousIndex +
                    new Vector2Int(0, indexDelta.y);
                if (!IsTileWalkable(horizontalIndex) ||
                    !IsTileWalkable(verticalIndex))
                {
                    return false;
                }
            }

            previousIndex = sampleIndex;
        }

        return true;
    }

    private static bool IsTileWalkable(Vector2Int index)
    {
        return TilePaintManager.HasInstance &&
               TilePaintManager.Instance.IsInsideGrid(index) &&
               !TilePaintManager.Instance.IsTileDestroyed(index) &&
               !BombMine.IsBlockingTile(index);
    }

    private Vector2Int GetQuantizedInputDirection()
    {
        Joystick activeJoystick = GetActiveJoystick();
        Vector2 input = activeJoystick != null
            ? new Vector2(activeJoystick.Horizontal, activeJoystick.Vertical)
            : Vector2.zero;

        if (isControlInverted)
        {
            input *= -1f;
        }

        if (input.sqrMagnitude < stepInputDeadZone * stepInputDeadZone)
        {
            return Vector2Int.zero;
        }

        return new Vector2Int(
            Mathf.Abs(input.x) > 0.0001f ? (input.x > 0f ? 1 : -1) : 0,
            Mathf.Abs(input.y) > 0.0001f ? (input.y > 0f ? 1 : -1) : 0);
    }

    private void RotateTowardDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    private void SetRunning(bool running)
    {
        if (animator != null)
        {
            animator.SetBool(IsRunHash, running);
        }
    }

    private void CheckAndPaintTile()
    {
        if (tileClaimDriver != null)
        {
            tileClaimDriver.Tick(paintRetryInterval);
        }
    }
}
