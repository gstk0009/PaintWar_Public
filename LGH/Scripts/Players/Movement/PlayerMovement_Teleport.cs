using Cysharp.Threading.Tasks;
using UnityEngine;

// 텔레포트 입력, 거리 계산, 쿨타임, 위치 보정, VFX 재생을 담당합니다.
// FishNet RPC 선언은 hash 순서를 확인하기 쉽도록 PlayerMovement.cs에 모아둡니다.
public partial class PlayerMovement
{
    // 버튼 UI에서 표시할 현재 텔레포트 남은 쿨타임입니다.
    public float GetTeleportCooldownRemaining()
    {
        return Mathf.Max(0f, teleportCooldownEndsAt - Time.time);
    }

    public float GetTeleportLockRemaining()
    {
        return isTeleportLocked
            ? Mathf.Max(0f, teleportLockEndsAt - Time.time)
            : 0f;
    }

    // 로컬 owner의 텔레포트 버튼에서 들어오는 진입점입니다.
    // owner가 아니거나, 이미 텔레포트 중이거나, 쿨타임 중이면 위치 이동 없이 UI만 갱신합니다.
    public void PlayerTeleport()
    {
        if (!IsOwner ||
            isTeleporting ||
            isSkillMovementLocked ||
            isTeleportLocked ||
            GetTeleportCooldownRemaining() > 0f ||
            !CanUseAbilities() ||
            !LoadingTransitionController.IsGameplayReleased)
        {
            RefreshTeleportCooldownDisplay(true);
            return;
        }

        teleportCooldownEndsAt = Time.time + Mathf.Max(0.1f, teleportCooldownSeconds);
        RefreshTeleportCooldownDisplay(true);
        TeleportAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // 코루틴 대신 UniTask로 한 프레임을 넘긴 뒤 위치를 보정합니다.
    // 오브젝트가 despawn/destroy되는 타이밍에는 cancellation으로 조용히 빠져나옵니다.
    private async UniTaskVoid TeleportAsync(System.Threading.CancellationToken cancellationToken)
    {
        isTeleporting = true;
        isStepMoving = false;
        CancelSoftGridSnap();
        SetRunning(false);

        try
        {
            if (localOwnerControls != null)
            {
                localOwnerControls.PlaySpeedBurst(1f);
            }

            Vector3 startPosition = transform.position;
            Vector3 teleportDirection = GetTeleportDirection();
            PlayTeleportEffect(
                ref teleportStartEffectInstance,
                ref teleportStartParticles,
                ref teleportStartRenderers,
                ref teleportStartEffectPlayVersion,
                true,
                startPosition);

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            Teleport(teleportDirection);
            Vector3 endPosition = transform.position;
            PlayTeleportEffect(
                ref teleportEndEffectInstance,
                ref teleportEndParticles,
                ref teleportEndRenderers,
                ref teleportEndEffectPlayVersion,
                false,
                endPosition);
            if (CSE.SoundManager.Instance != null)
            {
                CSE.SoundManager.Instance.PlayTeleportAt(endPosition);
            }
            ConvertTeleportPathBombMines(startPosition, endPosition);
            ServerShowTeleportEffects(startPosition, endPosition);

            CheckAndPaintTile();
        }
        catch (System.OperationCanceledException)
        {
        }
        finally
        {
            isTeleporting = false;
            RefreshTeleportCooldownDisplay(true);
        }
    }

    private Vector3 GetTeleportDirection()
    {
        Vector3 inputDirection = GetEightDirectionWorldInput();
        if (inputDirection.sqrMagnitude > 0.0001f)
        {
            return inputDirection.normalized;
        }

        // 조이스틱 입력이 없을 때는 현재 바라보는 방향으로 텔레포트합니다.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude <= 0.0001f
            ? Vector3.forward
            : forward.normalized;
    }

    private void Teleport(Vector3 direction)
    {
        int teleportSteps = GetCurrentTeleportTileSteps();
        float distance = GetTeleportWorldDistance(teleportSteps);
        if (distance <= 0.0001f)
        {
            return;
        }

        bool useGridTeleport =
            TilePaintManager.HasInstance &&
            TilePaintManager.Instance.IsGridReady;
        Vector3 targetPosition;
        if (useGridTeleport)
        {
            // grid 모드에서는 타일 중심과 owner zone 규칙을 지키는 위치만 허용합니다.
            if (TryGetGridTeleportTarget(direction, teleportSteps, out targetPosition))
            {
                ApplyTeleportPosition(targetPosition, direction);
            }
            else
            {
                FinishTeleport();
            }

            return;
        }

        Vector3 movement = direction * distance;
        if (!CanMoveToWorldPosition(transform.position + movement))
        {
            FinishTeleport();
            return;
        }

        Vector3 positionBeforeMove = transform.position;
        characterController.Move(movement);
        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.ClampTransformToOwnerZone(GetCurrentOwner(), transform);
        }

        Vector3 moved = transform.position - positionBeforeMove;
        moved.y = 0f;
        if (moved.sqrMagnitude > 0.0001f)
        {
            RotateTowardDirection(moved);
        }

        FinishTeleport();
    }

    private int GetCurrentTeleportTileSteps()
    {
        int level = levelStats != null ? levelStats.Level : 1;
        int steps = Mathf.Max(1, teleportBaseTileSteps);
        // 레벨 1/3/5에서 2/3/4칸이 되도록 단계적으로 증가시킵니다.
        if (level >= 3)
        {
            steps++;
        }

        if (level >= 5)
        {
            steps++;
        }

        return steps;
    }

    private float GetTeleportWorldDistance(int teleportSteps)
    {
        // grid가 준비되어 있으면 tile size를 기준으로 칸 수를 월드 거리로 바꿉니다.
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            return teleportSteps * Mathf.Max(0.0001f, TilePaintManager.Instance.TileSize);
        }

        return teleportSteps * Mathf.Max(0.0001f, teleportDistance);
    }

    private bool TryGetGridTeleportTarget(Vector3 direction, int teleportSteps, out Vector3 targetPosition)
    {
        targetPosition = transform.position;
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        Vector2Int gridDirection = GetGridDirection(direction);
        if (gridDirection == Vector2Int.zero)
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        PaintOwner owner = GetCurrentOwner();

        for (int step = Mathf.Max(1, teleportSteps); step >= 1; step--)
        {
            // 목표 칸이 막혀 있거나 zone 밖이면 한 칸씩 줄여 가장 먼 유효 위치를 찾습니다.
            Vector2Int targetIndex = currentIndex + gridDirection * step;
            if (!TilePaintManager.Instance.IsTileWalkable(targetIndex) ||
                tileManager.IsHomeProtected(targetIndex) ||
                BombMine.IsBlockingTile(targetIndex))
            {
                continue;
            }

            if (GameRoundManager.HasInstance &&
                !GameRoundManager.Instance.CanOwnerClaimTile(owner, targetIndex))
            {
                continue;
            }

            Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(targetIndex);
            targetPosition = new Vector3(
                tileCenter.x,
                tileManager.GetTileSurfaceY(targetIndex),
                tileCenter.z);
            return true;
        }

        return false;
    }

    private static Vector2Int GetGridDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector2Int.zero;
        }

        direction.Normalize();
        return new Vector2Int(
            Mathf.Abs(direction.x) > 0.25f ? (direction.x > 0f ? 1 : -1) : 0,
            Mathf.Abs(direction.z) > 0.25f ? (direction.z > 0f ? 1 : -1) : 0);
    }

    private void ApplyTeleportPosition(Vector3 position, Vector3 direction)
    {
        // CharacterController가 켜진 상태에서는 transform 위치 보정이 밀릴 수 있어 순간적으로 끕니다.
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
        {
            characterController.enabled = false;
        }

        transform.position = position;

        if (controllerWasEnabled)
        {
            characterController.enabled = true;
        }

        RotateTowardDirection(direction);
        FinishTeleport();
    }

    private void ConvertTeleportPathBombMines(Vector3 startPosition, Vector3 endPosition)
    {
        PaintOwner teleportOwner = GetCurrentOwner();
        if (IsServerInitialized)
        {
            int convertedCount = BombMine.ConvertMinesAlongTeleportPath(
                startPosition,
                endPosition,
                teleportOwner);
            if (convertedCount > 0)
            {
                ObserversConvertTeleportPathBombMines(startPosition, endPosition, teleportOwner);
            }

            return;
        }

        BombMine.ConvertMinesAlongTeleportPath(
            startPosition,
            endPosition,
            teleportOwner);

        if (IsClientInitialized)
        {
            ServerConvertTeleportPathBombMines(startPosition, endPosition);
        }
    }

    private void FinishTeleport()
    {
        // 텔레포트 직후에는 grid-step 이동과 타일 점령 캐시가 이전 위치를 물고 있지 않게 초기화합니다.
        tileClaimDriver.Reset();
        isStepMoving = false;
        stepTargetPosition = transform.position;
        if (networkTransform != null)
        {
            networkTransform.Teleport();
        }
    }

    private void SpawnTeleportEffect(Vector3 position)
    {
        // RPC wrapper 호환용입니다. owner 로컬에서는 start/end 전용 캐시를 직접 사용합니다.
        PlayTeleportEffect(
            ref teleportStartEffectInstance,
            ref teleportStartParticles,
            ref teleportStartRenderers,
            ref teleportStartEffectPlayVersion,
            true,
            position);
    }

    private void PlayTeleportEffect(
        ref GameObject effectInstance,
        ref ParticleSystem[] particleSystems,
        ref ParticleSystemRenderer[] particleRenderers,
        ref int playVersion,
        bool isStartEffect,
        Vector3 position)
    {
        if (teleportEffectPrefab == null)
        {
            return;
        }

        if (effectInstance == null)
        {
            // 최초 1회만 생성하고 이후에는 같은 인스턴스를 위치만 바꿔 재생해 GC와 Instantiate 비용을 줄입니다.
            effectInstance = Instantiate(teleportEffectPrefab);
            effectInstance.name = teleportEffectPrefab.name + " (Pooled)";
            particleSystems = effectInstance.GetComponentsInChildren<ParticleSystem>(true);
            particleRenderers =
                effectInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);
        }

        effectInstance.transform.SetPositionAndRotation(position, Quaternion.identity);
        effectInstance.SetActive(true);

        if (particleSystems == null || particleSystems.Length == 0)
        {
            int versionWithoutParticles = ++playVersion;
            DisableTeleportEffectAfterDelayAsync(
                effectInstance,
                versionWithoutParticles,
                isStartEffect,
                this.GetCancellationTokenOnDestroy()).Forget();
            return;
        }

        // PaintOwner 동기화가 이펙트 캐시 생성보다 늦을 수 있으므로 재생 직전에 매번 갱신합니다.
        // 같은 파티클 인스턴스를 재사용하므로 이 작업은 색상 프로퍼티만 변경하며 할당을 만들지 않습니다.
        ApplyTeleportEffectOwnerColor(particleSystems, particleRenderers);

        int currentVersion = ++playVersion;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particle = particleSystems[i];
            if (particle == null)
            {
                continue;
            }

            particle.Clear(true);
            particle.Play(true);
        }

        DisableTeleportEffectAfterDelayAsync(
            effectInstance,
            currentVersion,
            isStartEffect,
            this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void ApplyTeleportEffectOwnerColor(
        ParticleSystem[] particleSystems,
        ParticleSystemRenderer[] particleRenderers)
    {
        if (particleSystems == null)
        {
            return;
        }

        Color ownerColor = GetTeleportEffectOwnerColor();
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particle = particleSystems[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.startColor = new ParticleSystem.MinMaxGradient(ownerColor);
        }

        // 원본 재질의 HDR 흰색이 모든 색 채널을 포화시켜 결과가 흰색으로
        // 보이지 않도록 인스턴스별 곱 색상을 중립 흰색으로 정규화합니다.
        if (particleRenderers == null)
        {
            return;
        }

        if (teleportEffectMaterialBlock == null)
        {
            teleportEffectMaterialBlock = new MaterialPropertyBlock();
        }

        for (int i = 0; i < particleRenderers.Length; i++)
        {
            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            if (particleRenderer == null)
            {
                continue;
            }

            particleRenderer.GetPropertyBlock(teleportEffectMaterialBlock);
            teleportEffectMaterialBlock.SetColor("_BaseColor", Color.white);
            teleportEffectMaterialBlock.SetColor("_Color", Color.white);
            teleportEffectMaterialBlock.SetColor("_TintColor", Color.white);
            particleRenderer.SetPropertyBlock(teleportEffectMaterialBlock);
            teleportEffectMaterialBlock.Clear();
        }
    }

    private Color GetTeleportEffectOwnerColor()
    {
        if (identity == null)
        {
            identity = GetComponent<PlayerPaintIdentity>();
        }

        PaintOwner owner = GetCurrentOwner();
        if (TilePaintManager.HasInstance)
        {
            Material ownerMaterial = TilePaintManager.Instance.GetOwnerMaterial(owner);
            if (ownerMaterial != null)
            {
                if (ownerMaterial.HasProperty("_BaseColor"))
                {
                    return ownerMaterial.GetColor("_BaseColor");
                }

                if (ownerMaterial.HasProperty("_Color"))
                {
                    return ownerMaterial.GetColor("_Color");
                }
            }
        }

        switch (owner)
        {
            case PaintOwner.Player1:
                return new Color32(0xE2, 0x4B, 0x4A, 0xFF);
            case PaintOwner.Player2:
                return new Color32(0x37, 0x8A, 0xDD, 0xFF);
            case PaintOwner.Player3:
                return new Color32(0xEF, 0x9F, 0x27, 0xFF);
            case PaintOwner.Player4:
                return new Color32(0x1D, 0x9E, 0x75, 0xFF);
            default:
                return Color.white;
        }
    }

    private async UniTaskVoid DisableTeleportEffectAfterDelayAsync(
        GameObject effectInstance,
        int playVersion,
        bool isStartEffect,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(Mathf.Max(0.1f, teleportEffectLifetime)),
                cancellationToken: cancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        int currentVersion = isStartEffect
            ? teleportStartEffectPlayVersion
            : teleportEndEffectPlayVersion;

        if (effectInstance != null && currentVersion == playVersion)
        {
            effectInstance.SetActive(false);
        }
    }

    private void RefreshTeleportCooldownDisplay(bool force = false)
    {
        float cooldownRemaining = GetTeleportCooldownRemaining();
        float lockRemaining = GetTeleportLockRemaining();
        float remaining = Mathf.Max(cooldownRemaining, lockRemaining);
        int tenths = Mathf.CeilToInt(remaining * 10f);
        bool abilityAllowed = CanUseAbilities();
        // 0.1초 단위가 바뀔 때만 텍스트를 갱신해 매 프레임 문자열 생성을 피합니다.
        if (!force &&
            displayedTeleportCooldownTenths == tenths &&
            displayedTeleportAllowed == abilityAllowed)
        {
            return;
        }

        displayedTeleportCooldownTenths = tenths;
        displayedTeleportAllowed = abilityAllowed;
        if (localOwnerControls != null)
        {
            bool isReady = !isTeleportLocked && cooldownRemaining <= 0.05f;
            localOwnerControls.SetTeleportCooldown(
                remaining,
                cooldownRemaining,
                Mathf.Max(0.1f, teleportCooldownSeconds),
                isReady && abilityAllowed,
                isTeleportLocked);
        }
    }

    private bool CanUseAbilities()
    {
        return statusEffects == null || !statusEffects.IsStunned;
    }
}
