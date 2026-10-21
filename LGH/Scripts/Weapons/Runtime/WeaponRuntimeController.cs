using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections.Generic;
using UnityEngine;

// Runtime weapon controller for players and bots.
// Core ownership/input logic stays here; cooldown, placement and UI live in partial files.
public partial class WeaponRuntimeController : NetworkBehaviour
{
    private static readonly Dictionary<string, WeaponData> WeaponLookup = new Dictionary<string, WeaponData>();
    private readonly List<Vector2Int> obstacleImpactBuffer = new List<Vector2Int>(25);

    // Local owner cache used by UI binding and input checks.
    public static WeaponRuntimeController Local { get; private set; }
    [SerializeField] private Transform playerTransform;
    [SerializeField] private PlayerPaintIdentity playerIdentity;
    [SerializeField] private PlayerLevelStats levelStats;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerStatusEffectController statusEffects;
    [SerializeField] private List<WeaponData> weapons = new List<WeaponData>();
    [SerializeField] private int equippedIndex;
    [SerializeField] private WeaponVisualSettings visualSettings;
    public readonly SyncVar<string> equippedWeaponId = new SyncVar<string>(string.Empty);

    private Player player;

    // Skills have one use followed by one cooldown. Placement is still validated
    // by server/tile authority.
    private float cooldownStartedAt;
    private float activeCooldownDuration;
    private float cooldownEndsAt;
    private bool localUiInitialized;
    private float nextLocalUiInitializeTime;

    public IReadOnlyList<WeaponData> Weapons { get { return weapons; } }
    public WeaponData EquippedWeapon
    {
        get
        {
            if (weapons == null || weapons.Count == 0 || equippedIndex < 0 || equippedIndex >= weapons.Count)
            {
                return null;
            }

            return weapons[equippedIndex];
        }
    }

    public int EquippedIndex { get { return equippedIndex; } }
    public PaintOwner CurrentOwner
    {
        get { return playerIdentity != null ? playerIdentity.Owner : PaintOwner.Player1; }
    }
    public PlayerLevelStats LevelStats { get { return levelStats; } }
    public bool IsSkillReady { get { RefreshCooldownState(); return IsCooldownComplete; } }
    public WeaponVisualSettings VisualSettings { get { return GetVisualSettings(); } }
    public float PlacementDelaySeconds { get { return VisualSettings.PlacementDelaySeconds; } }
    public float DangerPreviewLeadTime { get { return VisualSettings.RangePreviewLeadTime; } }
    public float GameplayApplyDelaySeconds { get { return PlacementDelaySeconds + VisualSettings.GameplayApplyDelayAfterImpact; } }
    public float ImpactEffectDelaySeconds { get { return GetImpactEffectDelaySeconds(EquippedWeapon, VisualSettings); } }
    public bool CanReceiveLocalInput
    {
        get
        {
            if (playerMovement != null && playerMovement.IsTeleportLocked)
            {
                return false;
            }

            if (statusEffects != null && statusEffects.IsStunned)
            {
                return false;
            }

            // During ownership handoff, fall back to comparing the owner connection.
            if (!IsClientInitialized || IsOwner)
            {
                return true;
            }

            if (InstanceFinder.NetworkManager == null ||
                InstanceFinder.NetworkManager.ClientManager == null ||
                InstanceFinder.NetworkManager.ClientManager.Connection == null ||
                !Owner.IsValid)
            {
                return false;
            }

            return Owner.ClientId == InstanceFinder.NetworkManager.ClientManager.Connection.ClientId;
        }
    }

    public static float GetImpactEffectDelaySeconds(WeaponData weapon, WeaponVisualSettings visualSettings)
    {
        WeaponVisualSettings settings = visualSettings != null
            ? visualSettings
            : WeaponVisualSettings.GetOrLoadDefault();
        float placementDelay = settings != null ? settings.PlacementDelaySeconds : 0f;
        MagicWeaponVfxProfile vfxProfile = weapon != null ? weapon.VfxProfile : null;
        float impactLeadSeconds = vfxProfile != null ? vfxProfile.ImpactVisualLeadSeconds : 0f;
        return Mathf.Max(0f, placementDelay - impactLeadSeconds);
    }
    public float SkillLockRemaining
    {
        get
        {
            float teleportLock = playerMovement != null ? playerMovement.GetTeleportLockRemaining() : 0f;
            float castLock = localSkillState == LocalSkillState.Casting
                ? Mathf.Max(0f, skillCastUnlockTime - Time.time)
                : 0f;
            return Mathf.Max(teleportLock, castLock);
        }
    }

    public void Initialize(Transform newPlayerTransform, PlayerPaintIdentity newPlayerIdentity, List<WeaponData> newWeapons)
    {
        // Bootstrap code can wire existing prefab/asset references without creating runtime UI.
        playerTransform = newPlayerTransform;
        playerIdentity = newPlayerIdentity;
        if (levelStats == null && playerTransform != null)
        {
            playerTransform.TryGetComponent(out levelStats);
        }
        if (playerMovement == null && playerTransform != null)
        {
            playerTransform.TryGetComponent(out playerMovement);
        }
        if (player == null && playerTransform != null)
        {
            playerTransform.TryGetComponent(out player);
        }
        EnsureStatusEffects();

        weapons = newWeapons ?? new List<WeaponData>();
        equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, weapons.Count - 1));
        RegisterWeapons();
        ResetCooldown();
    }

    private void Awake()
    {
        equippedWeaponId.OnChange += OnEquippedWeaponIdChanged;
        if (playerTransform == null)
        {
            playerTransform = transform;
        }

        if (player == null)
        {
            playerTransform.TryGetComponent(out player);
        }

        if (playerIdentity == null)
        {
            playerTransform.TryGetComponent(out playerIdentity);
        }

        if (levelStats == null)
        {
            playerTransform.TryGetComponent(out levelStats);
        }
        if (playerMovement == null)
        {
            playerTransform.TryGetComponent(out playerMovement);
        }
        EnsureStatusEffects();

        ResetCooldown();
        RegisterWeapons();
    }

    private void EnsureStatusEffects()
    {
        if (statusEffects != null || playerTransform == null)
        {
            return;
        }

        if (!playerTransform.TryGetComponent(out statusEffects))
        {
            statusEffects = playerTransform.gameObject.AddComponent<PlayerStatusEffectController>();
        }
    }

    private void Update()
    {
        RefreshCooldownState();
        TickLocalSkillState();
        TryInitializeLocalUiOnRetry();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!string.IsNullOrWhiteSpace(equippedWeaponId.Value))
        {
            ApplyEquippedWeaponId(equippedWeaponId.Value);
        }

        if (IsOwner)
        {
            Local = this;
            InitializeLocalUi();
        }
    }

    public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        if (IsOwner)
        {
            Local = this;
            InitializeLocalUi();
        }
        else if (Local == this)
        {
            CancelLocalSkillState();
            Local = null;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        CancelLocalSkillState();
        if (Local == this) Local = null;
    }

    private void OnDestroy()
    {
        equippedWeaponId.OnChange -= OnEquippedWeaponIdChanged;
        CancelLocalSkillState();
        if (skillAimAreaVisual != null) Destroy(skillAimAreaVisual.gameObject);
        if (Local == this) Local = null;
        UnregisterWeapons();
    }

    public static bool TryFindWeaponData(string weaponId, out WeaponData weapon)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            weapon = null;
            return false;
        }

        if (WeaponLookup.TryGetValue(weaponId, out weapon) && weapon != null)
        {
            return true;
        }

        WeaponRuntimeController[] controllers = FindObjectsByType<WeaponRuntimeController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            WeaponRuntimeController controller = controllers[i];
            if (controller == null || controller.weapons == null)
            {
                continue;
            }

            controller.RegisterWeapons();
            if (WeaponLookup.TryGetValue(weaponId, out weapon) && weapon != null)
            {
                return true;
            }
        }

        weapon = null;
        return false;
    }

    public static WeaponRuntimeController FindLocalPlayerController()
    {
        // UI can ask before OnStartClient finishes, so scan once and cache a safe fallback.
        if (Local != null)
        {
            return Local;
        }

        bool networkActive = InstanceFinder.NetworkManager != null &&
                             (InstanceFinder.NetworkManager.IsClientStarted ||
                              InstanceFinder.NetworkManager.IsServerStarted);

        WeaponRuntimeController[] controllers =
            FindObjectsByType<WeaponRuntimeController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        WeaponRuntimeController offlineFallback = null;
        for (int i = 0; i < controllers.Length; i++)
        {
            WeaponRuntimeController candidate = controllers[i];
            if (candidate == null || candidate.GetComponent<NetworkBotController>() != null)
            {
                continue;
            }

            if (candidate.IsOwner)
            {
                Local = candidate;
                return candidate;
            }

            Player player = candidate.GetComponent<Player>();
            if (player != null &&
                player.Owner.IsValid &&
                InstanceFinder.NetworkManager != null &&
                InstanceFinder.NetworkManager.ClientManager.Connection != null &&
                player.Owner.ClientId == InstanceFinder.NetworkManager.ClientManager.Connection.ClientId)
            {
                Local = candidate;
                return candidate;
            }

            if (!networkActive &&
                !candidate.IsClientInitialized &&
                !candidate.IsServerInitialized &&
                player != null)
            {
                offlineFallback = candidate;
            }
        }

        if (offlineFallback != null) Local = offlineFallback;
        return offlineFallback;
    }

    public void SetOwner(PaintOwner owner)
    {
        if (playerIdentity != null)
        {
            playerIdentity.SetOwner(owner);
        }
    }

    public void Equip(int index)
    {
        if (weapons == null || index < 0 || index >= weapons.Count)
        {
            return;
        }

        CancelLocalSkillState();
        equippedIndex = index;
        WeaponData weapon = weapons[index];
        if (weapon != null && (IsServerInitialized || !IsClientInitialized))
        {
            equippedWeaponId.Value = weapon.WeaponId;
        }
        ResetCooldown();
    }

    public bool EquipById(string weaponId)
    {
        if (weapons == null || string.IsNullOrWhiteSpace(weaponId))
        {
            return false;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon != null && string.Equals(
                    weapon.WeaponId,
                    weaponId.Trim(),
                    System.StringComparison.Ordinal))
            {
                Equip(i);
                return true;
            }
        }

        return false;
    }

    private void OnEquippedWeaponIdChanged(string previous, string next, bool asServer)
    {
        ApplyEquippedWeaponId(next);
    }

    private void ApplyEquippedWeaponId(string weaponId)
    {
        if (weapons == null || string.IsNullOrWhiteSpace(weaponId))
        {
            return;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon != null && string.Equals(
                    weapon.WeaponId,
                    weaponId,
                    System.StringComparison.Ordinal))
            {
                CancelLocalSkillState();
                equippedIndex = i;
                ResetCooldown();
                return;
            }
        }
    }

    public bool EquipRandomWeapon()
    {
        if (weapons == null || weapons.Count == 0)
        {
            return false;
        }

        Equip(UnityEngine.Random.Range(0, weapons.Count));
        return true;
    }

    public bool TryUseEquippedWeapon()
    {
        return TryPlaceEquippedWeapon();
    }

    // 네트워크 권한이나 재사용 대기시간 없이 실제 배치 VFX 경로를 재생합니다.
    // SkillTestScene의 에셋 조정 용도이며 게임 타일 소유권은 변경하지 않습니다.
    public bool PreviewEquippedWeaponVisual(int playerLevel)
    {
        WeaponData weapon = EquippedWeapon;
        if (weapon == null || playerTransform == null || !TilePaintManager.HasInstance)
        {
            return false;
        }

        SpawnPlacedWeaponVisual(
            weapon,
            playerTransform.position,
            playerTransform.forward,
            CurrentOwner,
            Mathf.Max(1, playerLevel));
        return true;
    }

    public bool TryPlaceEquippedWeapon()
    {
        return TryExecuteEquippedWeaponAt(
            playerTransform != null ? playerTransform.position : transform.position,
            playerTransform != null ? playerTransform.forward : transform.forward);
    }

    private bool TryExecuteEquippedWeaponAt(
        Vector3 origin,
        Vector3 forward,
        bool requireLocalInput = true)
    {
        if (requireLocalInput && !CanReceiveLocalInput)
        {
            return false;
        }

        WeaponData weapon = EquippedWeapon;
        if (weapon == null || playerTransform == null || !IsSkillReady)
        {
            return false;
        }

        if (!TilePaintManager.HasInstance)
        {
            return false;
        }

        // Start cooldown before visual/effect work to prevent same-frame input spam.
        StartCooldown();

        // Capture the owner at cast time so delayed impact does not inherit later identity changes.
        PaintOwner sourceOwner = CurrentOwner;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = playerTransform.forward;
        }
        forward.Normalize();
        int playerLevel = GetPlayerLevel();

        if (IsServerInitialized)
        {
            StartPlacedWeaponOnServer(weapon, origin, forward, playerLevel);
        }
        else if (IsClientInitialized)
        {
            SpawnPlacedWeaponVisual(weapon, origin, forward, sourceOwner, playerLevel);
            DetonateLocalMinesAfterWeaponImpactDelayAsync(
                weapon,
                origin,
                forward,
                playerLevel,
                GetImpactEffectDelaySeconds(weapon, GetVisualSettings()),
                this.GetCancellationTokenOnDestroy()).Forget();
            RequestNetworkVisualIfAvailable(weapon, origin, forward, sourceOwner, playerLevel);
            // 원격 플레이의 도색/장애물 파괴는 요청을 받은 서버가 한 번만 처리합니다.
            // 클라이언트 중복 claim은 적 타일을 한 번에 두 단계 변경시킬 수 있습니다.
            RequestNetworkStatusEffectAfterDelayAsync(
                weapon,
                origin,
                forward,
                sourceOwner,
                playerLevel,
                GameplayApplyDelaySeconds,
                this.GetCancellationTokenOnDestroy()).Forget();
        }
        else
        {
            SpawnPlacedWeaponVisual(weapon, origin, forward, sourceOwner, playerLevel);
            DetonateLocalMinesAfterWeaponImpactDelayAsync(
                weapon,
                origin,
                forward,
                playerLevel,
                GetImpactEffectDelaySeconds(weapon, GetVisualSettings()),
                this.GetCancellationTokenOnDestroy()).Forget();
            ClaimAfterDelayAsync(
                weapon,
                origin,
                forward,
                sourceOwner,
                playerLevel,
                GameplayApplyDelaySeconds,
                this.GetCancellationTokenOnDestroy()).Forget();
        }

        return true;
    }



    private int GetPlayerLevel()
    {
        return levelStats != null ? Mathf.Max(1, levelStats.Level) : 1;
    }

    private WeaponVisualSettings GetVisualSettings()
    {
        if (visualSettings == null)
        {
            visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        }

        return visualSettings;
    }


}
