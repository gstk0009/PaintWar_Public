using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public partial class WeaponRuntimeController
{
    private enum LocalSkillState
    {
        Idle,
        Aiming,
        BotPreparing,
        Casting
    }

    [Header("Local Skill Aiming")]
    [SerializeField] private SkillAimAreaVisual skillAimAreaVisualPrefab;
    [SerializeField, Min(0.1f)] private float skillAimCursorSpeed = 10f;
    [SerializeField, Range(0f, 0.2f)] private float skillAimViewportPadding = 0.055f;
    [SerializeField, Min(0.1f)] private float skillCastMovementLockSeconds = 1f;
    [SerializeField, Range(10f, 60f)] private float skillAimUpdatesPerSecond = 30f;

    [Header("Bot Skill Casting")]
    [SerializeField, Min(0f)] private float botSkillPrepareSeconds = 1f;

    private readonly List<Vector2Int> skillAimOffsets = new List<Vector2Int>(25);
    private readonly List<Vector2Int> visibleSkillAimOffsets = new List<Vector2Int>(25);
    private readonly List<Vector2Int> validSkillAimOrigins = new List<Vector2Int>(256);
    private SkillAimAreaVisual skillAimAreaVisual;
    private LocalSkillState localSkillState;
    private WeaponData aimedWeapon;
    private Camera skillAimCamera;
    private Vector3 skillAimCursorWorld;
    private Vector3 skillAimForward = Vector3.forward;
    private Vector2Int skillAimDirection = Vector2Int.up;
    private Vector2Int skillAimOrigin;
    private float nextSkillAimUpdateTime;
    private float skillCastUnlockTime;
    private CancellationTokenSource botSkillPrepareCancellation;

    public bool IsSkillAiming { get { return localSkillState == LocalSkillState.Aiming; } }
    public bool IsSkillPreparing { get { return localSkillState == LocalSkillState.BotPreparing; } }
    public bool IsSkillCasting
    {
        get
        {
            // 기존 봇 컨트롤러는 IsSkillCasting일 때 이동과 판단을 멈춥니다.
            // 준비 상태도 포함해야 준비 중 재이동/중복 시전을 막을 수 있습니다.
            return localSkillState == LocalSkillState.BotPreparing ||
                   localSkillState == LocalSkillState.Casting;
        }
    }
    public bool CanActivateSkillButton
    {
        get
        {
            if (IsSkillCasting)
            {
                return false;
            }

            if (IsSkillAiming)
            {
                return CanReceiveLocalInput;
            }

            return CanReceiveLocalInput && EquippedWeapon != null && IsSkillReady;
        }
    }

    public void HandleSkillButtonPressed()
    {
        if (localSkillState == LocalSkillState.Idle)
        {
            BeginLocalSkillAim();
        }
        else if (localSkillState == LocalSkillState.Aiming)
        {
            ConfirmLocalSkillAim();
        }
    }

    public bool TryCastEquippedWeaponForBotAt(Vector2Int targetIndex)
    {
        WeaponData weapon = EquippedWeapon;
        if (localSkillState != LocalSkillState.Idle ||
            weapon == null ||
            !IsSkillReady ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(targetIndex) ||
            TilePaintManager.Instance.IsTileDestroyed(targetIndex) ||
            playerTransform == null ||
            !IsSkillGameplayPhaseActive())
        {
            return false;
        }

        Vector3 origin =
            TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex);

        Vector3 forward = origin - playerTransform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = playerTransform.forward;
        }

        // 여기서는 아직 스킬을 실제로 발동하지 않습니다.
        // 준비 상태에 진입한 뒤 UniTask가 지정 시간 후 발동합니다.
        aimedWeapon = weapon;
        localSkillState = LocalSkillState.BotPreparing;
        playerMovement?.SetSkillMovementLocked(true);
        playerMovement?.BeginSkillAimAnimation();
        player?.RequestSkillAimEffectActive(true);

        CancelBotSkillPrepareTask();
        CancellationTokenSource source = new CancellationTokenSource();
        botSkillPrepareCancellation = source;

        PrepareAndCastBotSkillAsync(
            weapon,
            targetIndex,
            origin,
            forward,
            source).Forget();

        // true는 '즉시 발동 완료'가 아니라 '준비 시작 성공'을 뜻합니다.
        // 기존 봇 컨트롤러는 계획을 비우고 IsSkillCasting을 통해 대기합니다.
        return true;
    }

    private async UniTask PrepareAndCastBotSkillAsync(
        WeaponData preparedWeapon,
        Vector2Int targetIndex,
        Vector3 origin,
        Vector3 forward,
        CancellationTokenSource source)
    {
        try
        {
            if (botSkillPrepareSeconds > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(botSkillPrepareSeconds),
                    cancellationToken: source.Token);
            }

            source.Token.ThrowIfCancellationRequested();

            // 준비 중 라운드가 끝나거나 무기가 교체되거나 타일이 파괴되면 취소합니다.
            if (localSkillState != LocalSkillState.BotPreparing ||
                aimedWeapon != preparedWeapon ||
                EquippedWeapon != preparedWeapon ||
                !IsSkillGameplayPhaseActive() ||
                !TilePaintManager.HasInstance ||
                !TilePaintManager.Instance.IsInsideGrid(targetIndex) ||
                TilePaintManager.Instance.IsTileDestroyed(targetIndex))
            {
                CancelLocalSkillState();
                return;
            }

            // 준비 시간이 끝난 바로 이 시점에 실제 스킬을 발동합니다.
            if (!TryExecuteEquippedWeaponAt(origin, forward, false))
            {
                CancelLocalSkillState();
                return;
            }

            player?.RequestSkillAimEffectActive(false);

            localSkillState = LocalSkillState.Casting;
            skillCastUnlockTime = Time.time + skillCastMovementLockSeconds;

            // SkillPrepare -> SkillHold 이후 SkillCast로 이어집니다.
            // NetworkAnimator가 서버에서 이 Trigger를 observer들에게 전달합니다.
            playerMovement?.ConfirmSkillCastAnimation();
        }
        catch (OperationCanceledException)
        {
            // CancelLocalSkillState, Equip, despawn 등에서 정상적으로 취소된 경우입니다.
        }
        finally
        {
            if (ReferenceEquals(botSkillPrepareCancellation, source))
            {
                botSkillPrepareCancellation = null;
            }

            source.Dispose();
        }
    }

    private void CancelBotSkillPrepareTask()
    {
        CancellationTokenSource source = botSkillPrepareCancellation;
        botSkillPrepareCancellation = null;

        if (source == null)
        {
            return;
        }

        if (!source.IsCancellationRequested)
        {
            source.Cancel();
        }
    }

    private void TickLocalSkillState()
    {
        if (localSkillState == LocalSkillState.Casting)
        {
            if (Time.time >= skillCastUnlockTime)
            {
                FinishLocalSkillCast();
            }
            return;
        }

        if (localSkillState != LocalSkillState.Aiming)
        {
            return;
        }

        if (!CanReceiveLocalInput || aimedWeapon == null || aimedWeapon != EquippedWeapon ||
            !TilePaintManager.HasInstance || !IsSkillReady || !IsSkillGameplayPhaseActive())
        {
            CancelLocalSkillState();
            return;
        }

        Vector3 input = playerMovement != null
            ? playerMovement.GetSkillAimWorldInput()
            : Vector3.zero;
        if (input.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        skillAimCursorWorld += input * skillAimCursorSpeed * Time.unscaledDeltaTime;
        ClampSkillAimCursorToMap(TilePaintManager.Instance);
        if (Time.unscaledTime < nextSkillAimUpdateTime)
        {
            return;
        }

        nextSkillAimUpdateTime = Time.unscaledTime + 1f / skillAimUpdatesPerSecond;

        Vector3 direction = skillAimCursorWorld - playerTransform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            Vector2Int cardinalDirection = PatternDirection.Forward(direction);
            skillAimForward = direction.normalized;
            if (cardinalDirection != skillAimDirection)
            {
                skillAimDirection = cardinalDirection;
                RebuildSkillAimCandidates();
            }
        }

        SelectNearestValidSkillAimOrigin();
    }

    private void BeginLocalSkillAim()
    {
        aimedWeapon = EquippedWeapon;
        if (!CanReceiveLocalInput || aimedWeapon == null || playerTransform == null ||
            !IsSkillReady || !TilePaintManager.HasInstance ||
            !IsSkillGameplayPhaseActive() || !EnsureSkillAimVisual())
        {
            aimedWeapon = null;
            return;
        }

        Transform cameraTransform = playerMovement != null
            ? playerMovement.GetMovementCameraTransform()
            : null;
        skillAimCamera = cameraTransform != null
            ? cameraTransform.GetComponent<Camera>()
            : Camera.main;
        if (skillAimCamera == null)
        {
            Debug.LogWarning("[WeaponRuntimeController] Skill aiming requires the local gameplay camera.", this);
            aimedWeapon = null;
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        skillAimForward = playerTransform.forward.sqrMagnitude > 0.001f
            ? playerTransform.forward.normalized
            : Vector3.forward;
        skillAimDirection = PatternDirection.Forward(skillAimForward);
        skillAimCursorWorld = playerTransform.position + skillAimForward * tileManager.TileSize * 2f;
        ClampSkillAimCursorToMap(tileManager);
        localSkillState = LocalSkillState.Aiming;
        playerMovement?.SetSkillMovementLocked(true);
        playerMovement?.BeginSkillAimAnimation();

        RebuildSkillAimCandidates();
        if (validSkillAimOrigins.Count == 0)
        {
            Debug.LogWarning("[WeaponRuntimeController] No skill target fits inside the map and camera view.", this);
            CancelLocalSkillState();
            return;
        }

        SelectNearestValidSkillAimOrigin();
        player?.RequestSkillAimEffectActive(true);
    }

    private void ConfirmLocalSkillAim()
    {
        if (localSkillState != LocalSkillState.Aiming || validSkillAimOrigins.Count == 0)
        {
            return;
        }

        player?.RequestSkillAimEffectActive(false);

        Vector3 origin = TilePaintManager.Instance.GetTileCenterWorldPosition(skillAimOrigin);
        if (!TryExecuteEquippedWeaponAt(origin, skillAimForward))
        {
            CancelLocalSkillState();
            return;
        }

        localSkillState = LocalSkillState.Casting;
        skillCastUnlockTime = Time.time + skillCastMovementLockSeconds;
        skillAimAreaVisual.SetCasting(true);
        playerMovement?.ConfirmSkillCastAnimation();
    }

    private void FinishLocalSkillCast()
    {
        CancelBotSkillPrepareTask();
        player?.RequestSkillAimEffectActive(false);

        if (skillAimAreaVisual != null)
        {
            skillAimAreaVisual.Hide();
        }

        localSkillState = LocalSkillState.Idle;
        aimedWeapon = null;
        skillCastUnlockTime = 0f;
        playerMovement?.SetSkillMovementLocked(false);
    }

    private void CancelLocalSkillState()
    {
        CancelBotSkillPrepareTask();
        player?.RequestSkillAimEffectActive(false);

        if (skillAimAreaVisual != null)
        {
            skillAimAreaVisual.Hide();
        }

        localSkillState = LocalSkillState.Idle;
        aimedWeapon = null;
        skillCastUnlockTime = 0f;
        validSkillAimOrigins.Clear();
        skillAimOffsets.Clear();
        visibleSkillAimOffsets.Clear();
        playerMovement?.CancelSkillAnimation();
        playerMovement?.SetSkillMovementLocked(false);
    }

    private bool EnsureSkillAimVisual()
    {
        if (skillAimAreaVisual != null)
        {
            return skillAimAreaVisual.IsConfigured;
        }

        if (skillAimAreaVisualPrefab == null)
        {
            Debug.LogError("[WeaponRuntimeController] Skill Aim Area Visual Prefab is not assigned.", this);
            return false;
        }

        skillAimAreaVisual = Instantiate(skillAimAreaVisualPrefab);
        skillAimAreaVisual.name = "Local Skill Aim Area";
        skillAimAreaVisual.Hide();
        return skillAimAreaVisual.IsConfigured;
    }

    private void RebuildSkillAimCandidates()
    {
        skillAimOffsets.Clear();
        validSkillAimOrigins.Clear();
        if (aimedWeapon == null || !TilePaintManager.HasInstance || skillAimCamera == null)
        {
            return;
        }

        foreach (Vector2Int offset in WeaponPatternResolver.GetOffsets(
                     aimedWeapon,
                     skillAimForward,
                     GetPlayerLevel()))
        {
            skillAimOffsets.Add(offset);
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        for (int z = 0; z < tileManager.GridSize; z++)
        {
            for (int x = 0; x < tileManager.GridSize; x++)
            {
                Vector2Int origin = new Vector2Int(x, z);
                if (tileManager.IsTileDestroyed(origin) || !DoesSkillAreaFit(origin, tileManager))
                {
                    continue;
                }

                validSkillAimOrigins.Add(origin);
            }
        }
    }

    private bool DoesSkillAreaFit(Vector2Int origin, TilePaintManager tileManager)
    {
        for (int i = 0; i < skillAimOffsets.Count; i++)
        {
            Vector2Int index = origin + skillAimOffsets[i];
            if (!tileManager.IsInsideGrid(index))
            {
                return false;
            }

            Vector3 viewport = skillAimCamera.WorldToViewportPoint(
                tileManager.GetTileCenterWorldPosition(index));
            if (viewport.z <= 0f ||
                viewport.x < skillAimViewportPadding ||
                viewport.x > 1f - skillAimViewportPadding ||
                viewport.y < skillAimViewportPadding ||
                viewport.y > 1f - skillAimViewportPadding)
            {
                return false;
            }

        }

        return skillAimOffsets.Count > 0;
    }

    private void SelectNearestValidSkillAimOrigin()
    {
        if (validSkillAimOrigins.Count == 0)
        {
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        float closestDistance = float.MaxValue;
        Vector2Int closest = validSkillAimOrigins[0];
        for (int i = 0; i < validSkillAimOrigins.Count; i++)
        {
            Vector3 world = tileManager.GetTileCenterWorldPosition(validSkillAimOrigins[i]);
            float distance = (world - skillAimCursorWorld).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = validSkillAimOrigins[i];
            }
        }

        skillAimOrigin = closest;
        visibleSkillAimOffsets.Clear();
        for (int i = 0; i < skillAimOffsets.Count; i++)
        {
            visibleSkillAimOffsets.Add(skillAimOffsets[i]);
        }

        skillAimAreaVisual.Show(
            visibleSkillAimOffsets,
            skillAimOrigin,
            tileManager,
            CurrentOwner,
            VisualSettings,
            false);
    }

    private void ClampSkillAimCursorToMap(TilePaintManager tileManager)
    {
        Vector3 first = tileManager.GetTileCenterWorldPosition(Vector2Int.zero);
        Vector3 last = tileManager.GetTileCenterWorldPosition(
            new Vector2Int(tileManager.GridSize - 1, tileManager.GridSize - 1));
        skillAimCursorWorld.x = Mathf.Clamp(skillAimCursorWorld.x, first.x, last.x);
        skillAimCursorWorld.z = Mathf.Clamp(skillAimCursorWorld.z, first.z, last.z);
    }

    private static bool IsSkillGameplayPhaseActive()
    {
        if (!GameRoundManager.HasInstance)
        {
            return true;
        }

        GamePhase phase = GameRoundManager.Instance.CurrentPhase;
        return phase == GamePhase.Farming || phase == GamePhase.Battle;
    }
}
