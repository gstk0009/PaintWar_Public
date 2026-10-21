using FishNet.Component.Animating;
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 인간 플레이어의 로컬 owner 이동 컨트롤러입니다.
// owner일 때만 조이스틱 입력을 읽고, 타일 authority를 통해 페인트합니다.
// 세부 기능은 PlayerMovement.* partial 파일로 나눠 관리합니다.
public partial class PlayerMovement : NetworkBehaviour
{
    private static readonly int IsRunHash = Animator.StringToHash("isRun");

    // 로컬 gameplay UI 참조입니다. 플레이어 오브젝트가 먼저 spawn될 수 있어 씬 로드 뒤에 찾습니다.
    public Joystick joystick;
    [SerializeField] private float moveSpeed = 7f;
    public float MoveSpeed => moveSpeed;
    public void SetMoveSpeed(float newMoveSpeed) => moveSpeed = Mathf.Max(0f, newMoveSpeed);

    [Header("Local UI")]
    // scene UI를 직접 연결할 수 있는 테스트/비네트워크 배치용 참조입니다.
    // 네트워크 spawn prefab에서는 보통 비워두고 PlayerLocalInputView를 통해 찾습니다.
    [SerializeField] private Button teleportButton;

    [Header("Grid Step Movement")]
    // Grid-step 모드는 이동을 타일 중심에 맞춰 페인트와 farming zone 체크를 안정적으로 만듭니다.
    [SerializeField] private bool useGridStepMovement = false;
    [SerializeField] private float stepInputDeadZone = 0.35f;
    [SerializeField, Min(1)] private int maxStepTransitionsPerFrame = 4;
    [SerializeField, Min(0.0001f)] private float stepArrivalTolerance = 0.002f;
    [SerializeField, Min(0.05f)] private float paintRetryInterval = 0.15f;

    [Header("Eight Direction Movement")]
    [SerializeField, Range(0f, 0.9f)] private float movementRadialDeadZone = 0.16f;
    [SerializeField, Range(0f, 20f)] private float movementDirectionHysteresisDegrees = 7f;
    [SerializeField, Min(0f)] private float movementRotationSharpness = 12f;

    [Header("Soft Grid Assist")]
    [SerializeField] private bool useSoftGridAssist = true;
    [SerializeField, Range(0f, 0.5f)] private float softGridLaneAssistStrength = 0.2f;
    [SerializeField, Range(0.05f, 0.45f)] private float softGridSnapRadiusRatio = 0.28f;
    [SerializeField, Range(0.05f, 0.2f)] private float softGridSnapDuration = 0.1f;
    [SerializeField, Min(0.001f)] private float softGridSnapArrivalTolerance = 0.01f;

    [Header("Teleport")]
    // 레벨 1 기준 텔레포트 칸 수입니다. level 3/5에서 1칸씩 추가됩니다.
    [SerializeField, Min(1)] private int teleportBaseTileSteps = 2;
    // grid가 준비되지 않은 자유 이동 상황에서 1칸으로 볼 fallback 월드 거리입니다.
    [SerializeField, Min(0.01f)] private float teleportDistance = 1f;
    [SerializeField, Min(0.1f)] private float teleportCooldownSeconds = 3f;
    [SerializeField] private GameObject teleportEffectPrefab;
    [SerializeField, Min(0.1f)] private float teleportEffectLifetime = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool enableSelfTeleportLockTestKey = true;
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key selfTeleportLockTestKey = Key.Q;
#else
    [SerializeField] private KeyCode selfTeleportLockTestKey = KeyCode.Q;
#endif
    [SerializeField, Min(0.1f)] private float selfTeleportLockTestDuration = 5f;

    private float currentSpeed;
    private bool isTeleporting;
    private float teleportCooldownEndsAt = -1f;
    private int displayedTeleportCooldownTenths = -1;
    private bool? displayedTeleportAllowed;
    private float temporarySpeedBonus;
    private CancellationTokenSource speedBoostCts;
    private CancellationTokenSource controlInversionCts;
    private CancellationTokenSource teleportLockCts;
    private bool isControlInverted;
    private bool isTeleportLocked;
    private float teleportLockEndsAt = -1f;

    private CharacterController characterController;
    private Animator animator;
    private PlayerLevelStats levelStats;
    private PlayerLevelStats subscribedLevelStats;
    private PlayerPaintIdentity identity;
    private NetworkAnimator networkAnimator;
    private NetworkTransform networkTransform;
    private PlayerStatusEffectController statusEffects;
    private ControlInversionIndicator controlInversionIndicator;
    private TeleportLockIndicator teleportLockIndicator;
    private PlayerLocalOwnerControls localOwnerControls;
    private PlayerTileClaimDriver tileClaimDriver;
    // 시작/도착 VFX를 각각 하나씩 캐시해서 텔레포트마다 Instantiate/Destroy하지 않습니다.
    private GameObject teleportStartEffectInstance;
    private GameObject teleportEndEffectInstance;
    private ParticleSystem[] teleportStartParticles;
    private ParticleSystem[] teleportEndParticles;
    private ParticleSystemRenderer[] teleportStartRenderers;
    private ParticleSystemRenderer[] teleportEndRenderers;
    private MaterialPropertyBlock teleportEffectMaterialBlock;
    private int teleportStartEffectPlayVersion;
    private int teleportEndEffectPlayVersion;

    private bool isStepMoving;
    private Vector3 stepTargetPosition;
    private bool isSoftGridSnapping;
    private Vector3 softGridSnapTarget;
    private Transform movementCameraTransform;
    private int retainedMovementSector;
    private bool hasRetainedMovementSector;
    private bool isSkillMovementLocked;

    public bool IsLocalInitializationComplete =>
        localOwnerControls != null && localOwnerControls.IsInitialized;
    public bool IsTeleportLocked => isTeleportLocked;
    public bool IsSkillMovementLocked => isSkillMovementLocked;

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        currentSpeed = MoveSpeed;
        CacheComponents();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            InitializeLocalOwnerControls();
        }
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);

        if (IsOwner)
        {
            InitializeLocalOwnerControls();
        }
        else if (localOwnerControls != null)
        {
            localOwnerControls.Clear();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (IsOwner && localOwnerControls != null)
        {
            localOwnerControls.ClearCameraTarget();
            localOwnerControls.Clear();
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        RefreshTeleportCooldownDisplay();
        if (!LoadingTransitionController.IsGameplayReleased) return;
        HandleSelfTeleportLockTestKey();
        if (!IsRoundGameplayActive())
        {
            isStepMoving = false;
            stepTargetPosition = transform.position;
            CancelSoftGridSnap();
            if (animator != null)
            {
                animator.SetBool(IsRunHash, false);
            }

            return;
        }

        if (isSkillMovementLocked)
        {
            if (isStepMoving)
            {
                CancelCurrentStep(false);
            }

            CancelSoftGridSnap();

            SetRunning(false);
            CheckAndPaintTile();
            return;
        }

        Joystick activeJoystick = GetActiveJoystick();
        if (IsLocalInitializationComplete && activeJoystick != null && !isTeleporting)
        {
            if (useGridStepMovement)
            {
                HandleGridStepMove();
            }
            else
            {
                HandleMove();
            }
        }

        CheckAndPaintTile();
    }

    private static bool IsRoundGameplayActive()
    {
        if (!GameRoundManager.HasInstance)
        {
            return true;
        }

        GamePhase phase = GameRoundManager.Instance.CurrentPhase;
        return phase == GamePhase.Farming || phase == GamePhase.Battle;
    }

    private void InitializeLocalOwnerControls()
    {
        CacheComponents();
        tileClaimDriver.Reset();
        isStepMoving = false;
        CancelSoftGridSnap();
        localOwnerControls.Initialize();
    }

    // PlayerMovement가 자주 쓰는 컴포넌트를 한 번 캐시합니다.
    private void CacheComponents()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (networkAnimator == null)
        {
            networkAnimator = GetComponent<NetworkAnimator>();
        }

        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        if (identity == null)
        {
            identity = GetComponent<PlayerPaintIdentity>();
        }

        PlayerLevelStats foundLevelStats = GetComponent<PlayerLevelStats>();
        if (levelStats != foundLevelStats)
        {
            UnsubscribeLevelMoveSpeed();
            levelStats = foundLevelStats;
        }

        SubscribeLevelMoveSpeed();
        ApplyLevelMoveSpeed();

        if (statusEffects == null)
        {
            statusEffects = GetComponent<PlayerStatusEffectController>();
            if (statusEffects == null)
            {
                statusEffects = gameObject.AddComponent<PlayerStatusEffectController>();
            }
        }

        if (controlInversionIndicator == null)
        {
            controlInversionIndicator = GetComponent<ControlInversionIndicator>();
        }

        if (teleportLockIndicator == null)
        {
            teleportLockIndicator = GetComponent<TeleportLockIndicator>();
        }

        if (localOwnerControls == null)
        {
            localOwnerControls = new PlayerLocalOwnerControls(
                transform,
                teleportButton,
                PlayerTeleport,
                OnJoystickAssigned);
        }

        if (tileClaimDriver == null)
        {
            tileClaimDriver = new PlayerTileClaimDriver(transform, GetCurrentOwner);
        }
    }

    // FishNet RPC declarations stay together so generated RPC hash order is easier to audit.
    public void ServerPlaceForRound(Vector3 position, Quaternion rotation)
    {
        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        ApplyRoundPlacement(position, rotation);
        ForceRoundPlacementNetworkSync();

        if (Owner.IsValid)
        {
            TargetPlaceForRound(Owner, position, rotation);
        }

        if (IsServerInitialized)
        {
            ObserversPlaceForRound(position, rotation);
        }
    }

    [TargetRpc]
    private void TargetPlaceForRound(NetworkConnection conn, Vector3 position, Quaternion rotation)
    {
        ApplyRoundPlacement(position, rotation);
        ForceRoundPlacementNetworkSync();
        if (localOwnerControls != null)
        {
            localOwnerControls.AttachCamera();
        }
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversPlaceForRound(Vector3 position, Quaternion rotation)
    {
        ApplyRoundPlacement(position, rotation);
    }

    [ServerRpc]
    private void ServerShowTeleportEffects(Vector3 startPosition, Vector3 endPosition)
    {
        ObserversShowTeleportEffects(startPosition, endPosition);
    }

    [ServerRpc]
    private void ServerConvertTeleportPathBombMines(Vector3 startPosition, Vector3 endPosition)
    {
        PaintOwner teleportOwner = GetCurrentOwner();
        int convertedCount = BombMine.ConvertMinesAlongTeleportPath(
            startPosition,
            endPosition,
            teleportOwner);
        if (convertedCount > 0)
        {
            ObserversConvertTeleportPathBombMines(startPosition, endPosition, teleportOwner);
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    private void ObserversConvertTeleportPathBombMines(
        Vector3 startPosition,
        Vector3 endPosition,
        PaintOwner teleportOwner)
    {
        BombMine.ConvertMinesAlongTeleportPath(
            startPosition,
            endPosition,
            teleportOwner);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversShowTeleportEffects(Vector3 startPosition, Vector3 endPosition)
    {
        PlayTeleportEffect(
            ref teleportStartEffectInstance,
            ref teleportStartParticles,
            ref teleportStartRenderers,
            ref teleportStartEffectPlayVersion,
            true,
            startPosition);
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
    }

    public void ShowBotTeleportEffects(Vector3 startPosition, Vector3 endPosition)
    {
        if (IsServerInitialized)
        {
            PaintOwner teleportOwner = GetCurrentOwner();
            int convertedCount = BombMine.ConvertMinesAlongTeleportPath(
                startPosition,
                endPosition,
                teleportOwner);
            if (convertedCount > 0)
            {
                ObserversConvertTeleportPathBombMines(startPosition, endPosition, teleportOwner);
            }

            // Network bots are ownerless, so ExcludeOwner still reaches every observing client.
            ObserversShowTeleportEffects(startPosition, endPosition);
            return;
        }

        BombMine.ConvertMinesAlongTeleportPath(
            startPosition,
            endPosition,
            GetCurrentOwner());

        PlayTeleportEffect(
            ref teleportStartEffectInstance,
            ref teleportStartParticles,
            ref teleportStartRenderers,
            ref teleportStartEffectPlayVersion,
            true,
            startPosition);
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
    }

    [TargetRpc]
    public void TargetApplyControlInversion(NetworkConnection conn, float duration)
    {
        ApplyControlInversion(duration);
    }

    [ObserversRpc]
    public void ObserversShowControlInversionIndicator(float duration)
    {
        ShowControlInversionIndicator(duration);
    }

    [TargetRpc]
    public void TargetApplyTeleportLock(NetworkConnection conn, float duration)
    {
        ApplyTeleportLock(duration);
    }

    [ObserversRpc]
    public void ObserversShowTeleportLockIndicator(float duration)
    {
        ShowTeleportLockIndicator(duration);
    }

    [ServerRpc]
    private void ServerApplySelfTeleportLockTest(float duration)
    {
        ObserversShowTeleportLockIndicator(duration);
        if (Owner.IsValid)
        {
            TargetApplyTeleportLock(Owner, duration);
        }
        else
        {
            ApplyTeleportLock(duration);
        }
    }

    private PaintOwner GetCurrentOwner()
    {
        return identity != null ? identity.Owner : PaintOwner.Player1;
    }

    private Joystick GetActiveJoystick()
    {
        return localOwnerControls != null && localOwnerControls.Joystick != null
            ? localOwnerControls.Joystick
            : joystick;
    }

    public void SetSkillMovementLocked(bool locked)
    {
        isSkillMovementLocked = locked;
        if (!locked)
        {
            return;
        }

        isStepMoving = false;
        stepTargetPosition = transform.position;
        SetRunning(false);
    }

    private void HandleSelfTeleportLockTestKey()
    {
        if (!enableSelfTeleportLockTestKey || !WasSelfTeleportLockTestKeyPressed())
        {
            return;
        }

        float duration = Mathf.Max(0.1f, selfTeleportLockTestDuration);
        if (IsClientInitialized)
        {
            ServerApplySelfTeleportLockTest(duration);
            return;
        }

        ApplyTeleportLock(duration);
    }

    private bool WasSelfTeleportLockTestKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[selfTeleportLockTestKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(selfTeleportLockTestKey);
#endif
    }

    private void SubscribeLevelMoveSpeed()
    {
        if (levelStats == null || subscribedLevelStats == levelStats)
        {
            return;
        }

        UnsubscribeLevelMoveSpeed();
        subscribedLevelStats = levelStats;
        subscribedLevelStats.LevelMoveSpeedChanged += OnLevelMoveSpeedChanged;
    }

    private void UnsubscribeLevelMoveSpeed()
    {
        if (subscribedLevelStats == null)
        {
            return;
        }

        subscribedLevelStats.LevelMoveSpeedChanged -= OnLevelMoveSpeedChanged;
        subscribedLevelStats = null;
    }

    private void OnLevelMoveSpeedChanged(int level, float levelMoveSpeed)
    {
        SetMoveSpeed(levelMoveSpeed);
        currentSpeed = GetCurrentMoveSpeed();
    }

    private void ApplyLevelMoveSpeed()
    {
        if (levelStats == null)
        {
            return;
        }

        SetMoveSpeed(levelStats.MoveSpeed);
        currentSpeed = GetCurrentMoveSpeed();
    }

    private void OnJoystickAssigned(Joystick assignedJoystick)
    {
        joystick = assignedJoystick;
    }

    private void OnDestroy()
    {
        UnsubscribeLevelMoveSpeed();
        localOwnerControls?.Clear();
        CancelAndDispose(ref speedBoostCts);
        CancelAndDispose(ref controlInversionCts);
        CancelAndDispose(ref teleportLockCts);

        if (teleportStartEffectInstance != null)
        {
            Destroy(teleportStartEffectInstance);
        }

        if (teleportEndEffectInstance != null)
        {
            Destroy(teleportEndEffectInstance);
        }
    }

    private static void CancelAndDispose(ref CancellationTokenSource source)
    {
        if (source == null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
        source = null;
    }
}
