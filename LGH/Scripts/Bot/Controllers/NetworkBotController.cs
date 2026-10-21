using FishNet;
using FishNet.Component.Transforming;
using UnityEngine;

[RequireComponent(typeof(PlayerPaintIdentity))]
// 네트워크 매치에서 서버만 실행하는 봇 driver입니다.
// 타일 목표를 고르고 이동하며, 서버 타일 authority로 페인트하고 가치가 있을 때 무기를 사용합니다.
public partial class NetworkBotController : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private PaintOwner owner = PaintOwner.Player2;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float arriveDistance = 0.25f;
    // 봇이 매 프레임 map scoring을 하지 않도록 retarget 주기를 제한합니다.
    [SerializeField] private float retargetInterval = 0.75f;
    [SerializeField] private float stuckRetargetSeconds = 1.25f;
    [SerializeField, Min(0.02f)] private float decisionInterval = 0.1f;

    [Header("Painting")]
    [SerializeField, Min(0.02f)] private float paintInterval = 0.12f;

    [Header("Target Search")]
    [SerializeField] private int farmingSearchRadius = 7;
    [SerializeField] private int battleRandomSamples = 64;
    [SerializeField] private int topCandidatePickCount = 3;

    [Header("Weapons")]
    [SerializeField] private bool useWeapons = true;
    [SerializeField] private int weaponSearchRadius = 7;
    [SerializeField] private int weaponBattleRandomSamples = 64;
    [SerializeField] private int weaponMinimumScoreToPlace = 6;
    [SerializeField, Min(1)] private int weaponAimRangeTiles = 8;

    [Header("Abilities")]
    [SerializeField] private bool useTacticalItems = true;
    [SerializeField] private bool useTeleport = true;
    [SerializeField, Min(1)] private int teleportBaseTileSteps = 2;
    [SerializeField, Min(0.01f)] private float teleportFallbackDistance = 1f;
    [SerializeField, Min(0.1f)] private float teleportCooldownSeconds = 3f;
    [SerializeField] private bool useBombMines = true;
    [SerializeField, Min(0.1f)] private float bombMineCooldownSeconds = 5f;
    [SerializeField, Min(0.5f)] private float bombMineEnemyTriggerTiles = 3.5f;
    [SerializeField, Min(1f)] private float bombMineStuckTriggerMultiplier = 1.35f;
    [SerializeField, Min(1)] private int bombMineMinimumTacticalScore = 18;
    [SerializeField, Range(0f, 1f)] private float bombMineComboChance = 0.72f;
    [SerializeField, Range(0f, 1f)] private float bombMineThreeComboChance = 0.22f;
    [SerializeField, Range(0.5f, 1.25f)] private float bombMineComboPlacementInterval = 0.75f;

    private PlayerPaintIdentity identity;
    private PlayerLevelStats levelStats;
    private WeaponRuntimeController weaponController;
    private PlayerStoredItemController itemController;
    private CharacterController characterController;
    private Animator animator;
    private NetworkTransform networkTransform;
    private PlayerMovement playerMovement;
    private BotMovementMotor movementMotor;
    private BotTargetSelector targetSelector;
    private BotWeaponPlanner weaponPlanner;
    private BotTacticalAbilityHelper tacticalAbilityHelper;
    private BotTeleportAbility teleportAbility;
    private BotBrain botBrain;
    private BotActionPlan activeActionPlan;
    // 유효한 경우 봇은 계획된 무기 배치 타일로 이동 중입니다.
    private BotWeaponPlan activeWeaponPlan;
    private Vector2Int targetIndex;
    private bool teleportFollowThroughActive;
    private float nextRetargetTime;
    private float nextDecisionTime;
    private float nextPaintTime;
    private Vector2Int lastPaintIndex = new Vector2Int(int.MinValue, int.MinValue);
    private Coroutine controlInversionRoutine;
    private Coroutine speedBoostRoutine;

    public void Initialize(PaintOwner newOwner)
    {
        owner = newOwner;
        CacheComponents();
        RebuildHelpers();

        if (identity != null)
        {
            identity.SetOwner(owner);
        }

        EquipRandomWeapon();
    }

    private void Awake()
    {
        CacheComponents();
        RebuildHelpers();
    }

    private void Start()
    {
        Initialize(owner);
        if (TilePaintManager.HasInstance) TilePaintManager.Instance.TerritoryChanged += OnTerritoryChanged;
        if (TileClaimService.HasInstance) TileClaimService.Instance.EnemyTerritoryAttacked += OnEnemyTerritoryAttacked;
        float startupStagger = Mathf.Max(0, (int)owner - 1) * 0.05f;
        nextDecisionTime = Time.time + startupStagger;
        nextRetargetTime = Time.time + startupStagger;
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            targetIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        }
        if (startupStagger <= 0f)
        {
            ForceRetarget(GamePhase.Farming);
        }
    }

    private void OnDestroy()
    {
        if (TilePaintManager.HasInstance) TilePaintManager.Instance.TerritoryChanged -= OnTerritoryChanged;
        if (TileClaimService.HasInstance) TileClaimService.Instance.EnemyTerritoryAttacked -= OnEnemyTerritoryAttacked;
        if (botBrain != null) botBrain.Dispose();
        if (tacticalAbilityHelper != null) tacticalAbilityHelper.ResetForRound();
        if (levelStats != null)
        {
            levelStats.LevelMoveSpeedChanged -= OnLevelMoveSpeedChanged;
        }
    }

    private void Update()
    {
        EnsureRuntimeHelpers();
        if (movementMotor == null)
        {
            return;
        }

        // 봇 판단은 서버에서만 합니다. 클라이언트는 네트워크 시스템을 통해 transform/paint 갱신을 받습니다.
        if (!InstanceFinder.IsServerStarted)
        {
            return;
        }

        if (!GameRoundManager.HasInstance || !TilePaintManager.HasInstance)
        {
            movementMotor.SetRunning(false);
            return;
        }

        GameParticipant participant = GameRoundManager.Instance.FindParticipant(owner);
        if (participant != null && participant.IsEliminated)
        {
            movementMotor.SetRunning(false);
            return;
        }

        GamePhase phase = GameRoundManager.Instance.CurrentPhase;
        if (phase != GamePhase.Farming && phase != GamePhase.Battle)
        {
            movementMotor.SetRunning(false);
            return;
        }

        TickActivePhase(phase);
    }

    public void ServerResetForRoundPlacement()
    {
        if (!InstanceFinder.IsServerStarted)
        {
            return;
        }

        if (movementMotor == null || targetSelector == null)
        {
            CacheComponents();
            RebuildHelpers();
        }

        movementMotor.SetRunning(false);
        movementMotor.ResetStuckTimer();
        activeWeaponPlan = BotWeaponPlan.None;
        lastPaintIndex = new Vector2Int(int.MinValue, int.MinValue);
        nextDecisionTime = 0f;
        nextPaintTime = 0f;
        nextRetargetTime = 0f;
        if (botBrain != null) botBrain.ResetForRound();
        if (tacticalAbilityHelper != null) tacticalAbilityHelper.ResetForRound();
        ForceRetarget(GamePhase.Farming);
    }

    public void ResetAfterExternalTileDisplacement()
    {
        if (movementMotor == null)
        {
            CacheComponents();
            RebuildHelpers();
        }

        movementMotor.SetRunning(false);
        movementMotor.ResetStuckTimer();
        activeWeaponPlan = BotWeaponPlan.None;
        teleportFollowThroughActive = false;
        nextDecisionTime = 0f;
        nextRetargetTime = 0f;

        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            targetIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        }
    }

    private void TickActivePhase(GamePhase phase)
    {
        if (weaponController != null && weaponController.IsSkillCasting)
        {
            movementMotor.SetRunning(false);
            return;
        }

        // 이동은 매 프레임 실행하지만 비용이 큰 목표/무기 판단은 decisionInterval마다 실행합니다.
        movementMotor.UpdateStuckTimer();
        bool bombControlsMovement = TryUseBombMineOutsideDecisionLoop(phase);
        if (Time.time >= nextDecisionTime)
        {
            nextDecisionTime = Time.time + decisionInterval;
            if (bombControlsMovement)
            {
                activeWeaponPlan = BotWeaponPlan.None;
            }
            else if (movementMotor.CannotReachTarget)
            {
                activeWeaponPlan = BotWeaponPlan.None;
                ForceRetarget(phase, false);
            }
            else if (movementMotor.StuckTimer >= stuckRetargetSeconds)
            {
                if (!TryUseBotAbilities(phase, true))
                {
                    ForceRetarget(phase);
                }
            }
            else
            {
                RetargetIfNeeded(phase);
            }

            if (!bombControlsMovement)
            {
                TryUseBotAbilities(phase, false);
                TryPlacePlannedWeapon(phase);
            }
        }

        movementMotor.MoveToward(targetIndex, arriveDistance);
        PaintCurrentTile();
    }

    private bool TryUseBombMineOutsideDecisionLoop(GamePhase phase)
    {
        if (!useBombMines ||
            tacticalAbilityHelper == null ||
            movementMotor == null)
        {
            return false;
        }

        bool isStuck = movementMotor.StuckTimer >= stuckRetargetSeconds;
        bool placed = tacticalAbilityHelper.TryUseBombMine(phase, isStuck);
        if (tacticalAbilityHelper.ControlsBombMineMovement)
        {
            activeWeaponPlan = BotWeaponPlan.None;
            Vector2Int comboTarget = tacticalAbilityHelper.BombMineMovementTarget;
            if (targetIndex != comboTarget) movementMotor.ResetStuckTimer();
            targetIndex = comboTarget;
            teleportFollowThroughActive = false;
            return true;
        }

        if (placed) ForceRetarget(phase);
        return false;
    }

    private void RetargetIfNeeded(GamePhase phase)
    {
        if (IsReadyToPlaceActiveWeaponPlan())
        {
            return;
        }

        if (teleportFollowThroughActive)
        {
            if (!movementMotor.HasArrived(targetIndex, arriveDistance))
            {
                return;
            }

            teleportFollowThroughActive = false;
            ForceRetarget(phase);
            return;
        }

        if (Time.time >= nextRetargetTime &&
            movementMotor.HasArrived(targetIndex, arriveDistance))
        {
            ForceRetarget(phase);
        }
    }

    private void ForceRetarget(GamePhase phase, bool allowWeaponPlan = true)
    {
        teleportFollowThroughActive = false;
        // 먼저 무기 배치 계획을 우선하고, 없으면 순수 타일 점수 기반 목표 선택으로 fallback합니다.
        movementMotor.ResetStuckTimer();
        nextRetargetTime = Time.time + retargetInterval + Random.Range(0f, 0.25f);
        activeActionPlan = botBrain != null ? botBrain.BuildPlan(phase) : default;
        targetIndex = activeActionPlan.HasTarget ? activeActionPlan.TargetIndex : targetSelector.SelectTarget(phase);
        activeWeaponPlan = allowWeaponPlan && activeActionPlan.AllowWeapon ? SelectWeaponPlan(phase) : BotWeaponPlan.None;
        if (activeWeaponPlan.IsValid)
            targetIndex = activeWeaponPlan.TargetIndex;
        else
            activeWeaponPlan = BotWeaponPlan.None;
    }

    private void TryPlacePlannedWeapon(GamePhase phase)
    {
        if (teleportFollowThroughActive || !useWeapons || weaponController == null)
        {
            return;
        }

        if (!activeWeaponPlan.IsValid)
        {
            return;
        }

        // WeaponRuntimeController가 차지, 미리보기, 지연, 서버 권한 타일 점령을 처리합니다.
        if (weaponController.TryCastEquippedWeaponForBotAt(activeWeaponPlan.TargetIndex))
        {
            activeWeaponPlan = BotWeaponPlan.None;
            targetIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
            movementMotor.SetRunning(false);
            return;
        }

        activeWeaponPlan = BotWeaponPlan.None;
        ForceRetarget(phase);
    }

    private BotWeaponPlan SelectWeaponPlan(GamePhase phase)
    {
        if (!useWeapons || weaponPlanner == null)
        {
            return BotWeaponPlan.None;
        }

        EquipRandomWeaponIfNeeded();
        return weaponPlanner.SelectPlan(phase);
    }

    private bool IsReadyToPlaceActiveWeaponPlan()
    {
        if (!activeWeaponPlan.IsValid || weaponPlanner == null || !TilePaintManager.HasInstance)
        {
            return false;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        return weaponPlanner.ShouldPlaceAt(currentIndex, activeWeaponPlan, arriveDistance);
    }

    private void PaintCurrentTile()
    {
        if (!TilePaintManager.HasInstance)
        {
            return;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(transform.position);
        if (currentIndex == lastPaintIndex && Time.time < nextPaintTime)
        {
            return;
        }

        lastPaintIndex = currentIndex;
        nextPaintTime = Time.time + paintInterval;
        // 네트워크 봇은 서버에서 실행되므로 authority 경로로 직접 점령할 수 있습니다.
        if (NetworkTilePaintAuthority.HasInstance)
        {
            NetworkTilePaintAuthority.Instance.ServerClaimByWorldPos(transform.position, owner);
            return;
        }

        if (TileClaimService.HasInstance)
        {
            TileClaimService.Instance.TryClaimByWorldPosLocal(transform.position, owner);
        }
    }

    private void CacheComponents()
    {
        identity = GetComponent<PlayerPaintIdentity>();
        levelStats = GetComponent<PlayerLevelStats>();
        weaponController = GetComponent<WeaponRuntimeController>();
        itemController = GetComponent<PlayerStoredItemController>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        networkTransform = GetComponent<NetworkTransform>();
        playerMovement = GetComponent<PlayerMovement>();

        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        if (levelStats == null)
        {
            levelStats = gameObject.AddComponent<PlayerLevelStats>();
        }

        if (GetComponent<PlayerStatusEffectController>() == null)
        {
            gameObject.AddComponent<PlayerStatusEffectController>();
        }

        if (itemController == null)
        {
            itemController = gameObject.AddComponent<PlayerStoredItemController>();
        }
    }

    private void RebuildHelpers()
    {
        PlayerStatusEffectController statusEffects = GetComponent<PlayerStatusEffectController>();
        if (levelStats != null)
        {
            levelStats.LevelMoveSpeedChanged -= OnLevelMoveSpeedChanged;
            levelStats.LevelMoveSpeedChanged += OnLevelMoveSpeedChanged;
        }

        movementMotor = new BotMovementMotor(transform, characterController, animator, levelStats, statusEffects, owner, moveSpeed);
        targetSelector = new BotTargetSelector(owner, transform, new BotTargetSelector.Settings
        {
            FarmingSearchRadius = farmingSearchRadius,
            BattleRandomSamples = battleRandomSamples,
            TopCandidatePickCount = topCandidatePickCount,
            TeleportStealSteps = teleportBaseTileSteps +
                (levelStats != null && levelStats.Level >= 3 ? 1 : 0) +
                (levelStats != null && levelStats.Level >= 5 ? 1 : 0)
        }, itemController);
        weaponPlanner = new BotWeaponPlanner(owner, transform, weaponController, new BotWeaponPlanner.Settings
        {
            FarmingSearchRadius = weaponSearchRadius,
            BattleRandomSamples = weaponBattleRandomSamples,
            TopCandidatePickCount = topCandidatePickCount,
            MinimumScoreToPlace = weaponMinimumScoreToPlace,
            AimRangeTiles = weaponAimRangeTiles
        });
        teleportAbility = new BotTeleportAbility(
            transform,
            characterController,
            levelStats,
            networkTransform,
            playerMovement,
            statusEffects,
            owner,
            teleportBaseTileSteps,
            teleportFallbackDistance,
            teleportCooldownSeconds);
        tacticalAbilityHelper = new BotTacticalAbilityHelper(
            transform,
            owner,
            itemController,
            teleportAbility,
            CreateTacticalAbilitySettings());
        botBrain = new BotBrain(owner, transform, BotTerritoryAIConfig.LoadDefault());
    }

    private void OnTerritoryChanged(TerritoryChange change)
    {
        if (botBrain != null) botBrain.NotifyTerritoryChanged(change);
        if (change.Owner != owner && change.TileCount < 10) return;
        activeWeaponPlan = BotWeaponPlan.None;
        nextRetargetTime = 0f;
        nextDecisionTime = 0f;
        if (GameRoundManager.HasInstance && movementMotor != null)
        {
            GamePhase phase = GameRoundManager.Instance.CurrentPhase;
            if (phase == GamePhase.Farming || phase == GamePhase.Battle) ForceRetarget(phase);
        }
    }

    private void OnEnemyTerritoryAttacked(PaintOwner victim, PaintOwner attacker)
    {
        if (victim == owner && botBrain != null) botBrain.NotifyAttackedBy(attacker);
    }

    private void EnsureRuntimeHelpers()
    {
        if (movementMotor != null &&
            targetSelector != null &&
            tacticalAbilityHelper != null)
        {
            return;
        }

        CacheComponents();
        RebuildHelpers();
    }

    private BotTacticalAbilityHelper.Settings CreateTacticalAbilitySettings()
    {
        return new BotTacticalAbilityHelper.Settings
        {
            BombMineCooldownSeconds = bombMineCooldownSeconds,
            BombMineEnemyTriggerTiles = bombMineEnemyTriggerTiles,
            BombMineStuckTriggerMultiplier = bombMineStuckTriggerMultiplier,
            BombMineMinimumTacticalScore = bombMineMinimumTacticalScore,
            BombMineComboChance = bombMineComboChance,
            BombMineThreeComboChance = bombMineThreeComboChance,
            BombMineComboPlacementInterval = bombMineComboPlacementInterval
        };
    }

    private void OnLevelMoveSpeedChanged(int level, float levelMoveSpeed)
    {
        if (movementMotor != null)
        {
            movementMotor.SetLevelMoveSpeed(levelMoveSpeed);
        }
    }

    private void EquipRandomWeaponIfNeeded()
    {
        if (weaponController == null || weaponController.Weapons == null || weaponController.Weapons.Count == 0)
        {
            return;
        }

        if (weaponController.EquippedWeapon == null)
        {
            weaponController.Equip(Random.Range(0, weaponController.Weapons.Count));
        }
    }

    private void EquipRandomWeapon()
    {
        if (weaponController == null)
        {
            return;
        }

        weaponController.EquipRandomWeapon();
    }

    public void ApplyControlInversion(float duration)
    {
        if (controlInversionRoutine != null)
        {
            StopCoroutine(controlInversionRoutine);
        }

        controlInversionRoutine = StartCoroutine(ControlInversionRoutine(duration));
    }

    public void ApplySpeedBoost(float multiplier, float duration)
    {
        if (speedBoostRoutine != null)
        {
            StopCoroutine(speedBoostRoutine);
        }

        speedBoostRoutine = StartCoroutine(SpeedBoostRoutine(multiplier, duration));
    }

    public void ApplySpeedBoostToSpeed(float targetSpeed, float duration)
    {
        if (speedBoostRoutine != null)
        {
            StopCoroutine(speedBoostRoutine);
        }

        speedBoostRoutine = StartCoroutine(SpeedBoostToSpeedRoutine(targetSpeed, duration));
    }

    private System.Collections.IEnumerator SpeedBoostRoutine(float multiplier, float duration)
    {
        movementMotor.SetTemporarySpeedMultiplier(Mathf.Max(0f, multiplier));
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
        movementMotor.SetTemporarySpeedMultiplier(1f);
        speedBoostRoutine = null;
    }

    private System.Collections.IEnumerator SpeedBoostToSpeedRoutine(float targetSpeed, float duration)
    {
        movementMotor.SetTemporaryTargetSpeed(targetSpeed);
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
        movementMotor.SetTemporarySpeedMultiplier(1f);
        speedBoostRoutine = null;
    }

    private System.Collections.IEnumerator ControlInversionRoutine(float duration)
    {
        ShowControlInversionIndicator(duration);
        movementMotor.SetDirectionInverted(true);
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
        movementMotor.SetDirectionInverted(false);
        HideControlInversionIndicator();
        controlInversionRoutine = null;
    }

    private void ShowControlInversionIndicator(float duration)
    {
        ControlInversionIndicator indicator = GetComponent<ControlInversionIndicator>();
        if (indicator == null)
        {
            indicator = gameObject.AddComponent<ControlInversionIndicator>();
        }

        indicator.Show(duration);
    }

    private void HideControlInversionIndicator()
    {
        ControlInversionIndicator indicator = GetComponent<ControlInversionIndicator>();
        if (indicator != null)
        {
            indicator.Hide();
        }
    }
}
