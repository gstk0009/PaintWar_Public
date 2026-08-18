using UnityEngine;
using TMPro;
using System;
using Cysharp.Threading.Tasks;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class PlayerStoredItemController : MonoBehaviour
{
    public static event Action<bool> LocalHeldItemAvailabilityChanged;
    public static event Action LocalStoredItemSlotsChanged;
    private const int MaxStoredItemSlots = 3;
    public const int StoredItemSlotCount = MaxStoredItemSlots;
    private const int DefaultBombMineCount = BombMineSettings.DefaultMaxActiveMines;
    private const float DefaultBombMineRadius = 0f;
    private const float BombMineUiRefreshInterval = 0.1f;

    [Header("Bomb Mine")]
    [SerializeField] private BombMineSettings bombMineSettings;

    [Header("SO Catalog")]
    [SerializeField] private DropItemCatalog itemCatalog;

    [Header("UI")]
    [SerializeField] private TMP_Text heldItemText;
    [SerializeField] private string heldItemTextObjectName = "HeldItemText";
    [SerializeField] private bool autoFindHeldItemText = true;
    [SerializeField] private string emptyItemLabel = "Item: None";

    [Header("Status Indicators")]
    [SerializeField] private GameObject controlInversionIndicatorPrefab;
    [SerializeField] private GameObject teleportLockIndicatorPrefab;

    [Header("Held Item Range Preview")]
    [SerializeField] private HeldItemRangePreview heldItemRangePreview;
    [SerializeField] private bool autoCreateHeldItemRangePreview = true;

    [Header("Debug")]
    [SerializeField] private bool enableShieldTestKey = true;
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key shieldTestKey = Key.E;
#else
    [SerializeField] private KeyCode shieldTestKey = KeyCode.E;
#endif
    [SerializeField, Min(0.1f)] private float shieldTestDuration = PlayerShieldController.DefaultShieldSeconds;

    private readonly StoredItemSlot[] storedItemSlots = new StoredItemSlot[MaxStoredItemSlots];
    private DropItemEffectType heldItemType = DropItemEffectType.None;
    private float heldDuration;
    private float heldRadius;
    private int heldPaintRadius;
    private int bombMineCharges = DefaultBombMineCount;
    private PlayerPaintIdentity identity;
    private Player player;
    private PlayerMovement movement;
    private PlayerLevelStats levelStats;
    private PlayerLevelStats subscribedLevelStats;
    private PlayerStatusEffectController statusEffects;
    private float nextBombMineUiRefreshAt;
    private bool storedItemSlotsInitialized;
    private static PlayerStoredItemController localOwnerController;

    public bool HasItem { get { return heldItemType != DropItemEffectType.None; } }
    public DropItemEffectType HeldItemType { get { return heldItemType; } }
    public float HeldRadius { get { return heldRadius; } }
    public int HeldPaintRadius { get { return heldPaintRadius; } }
    public int BombMineCharges { get { return bombMineCharges; } }
    public int DefaultBombMineCrossRadius { get { return GetBombMineCrossRadius(); } }
    public int DefaultBombMineMaxCount { get { return GetEffectiveBombMineCapacity(); } }
    public bool CanUseBombMineAbility
    {
        get { return !IsStunned && bombMineCharges > 0; }
    }
    public static bool LocalOwnerHasItem
    {
        get
        {
            return localOwnerController != null &&
                   localOwnerController.HasVisibleItemSlot;
        }
    }

    public static DropItemEffectType LocalOwnerHeldItemType
    {
        get
        {
            return localOwnerController != null
                ? localOwnerController.ActiveUseItemType
                : DropItemEffectType.None;
        }
    }

    public static bool LocalOwnerCanUseItem
    {
        get
        {
            return localOwnerController != null &&
                localOwnerController.CanUseAnyItem &&
                !localOwnerController.IsStunned &&
                LoadingTransitionController.IsGameplayReleased;
        }
    }

    public static bool LocalOwnerCanUseBombMineAbility
    {
        get
        {
            if (localOwnerController != null)
            {
                localOwnerController.SyncBombMineCapacityWithCurrentLevel();
            }

            return localOwnerController != null &&
                   localOwnerController.CanUseBombMineAbility &&
                   LoadingTransitionController.IsGameplayReleased;
        }
    }

    public static bool TryGetLocalPaintOwner(out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        if (localOwnerController == null)
        {
            return false;
        }

        owner = localOwnerController.GetOwner();
        return owner != PaintOwner.Neutral;
    }

    public static bool TryGetLocalStoredItemSlot(
        int slotIndex,
        out DropItemEffectType itemType,
        out bool canUse)
    {
        itemType = DropItemEffectType.None;
        canUse = false;

        if (localOwnerController == null ||
            !localOwnerController.TryGetStoredItemSlot(slotIndex, out StoredItemSlot slot))
        {
            return false;
        }

        itemType = slot.ItemType;
        canUse = localOwnerController.CanUseStoredItemSlot(slotIndex) &&
                 LoadingTransitionController.IsGameplayReleased;
        return true;
    }

    public static bool TryUseLocalStoredItemSlot(int slotIndex)
    {
        if (localOwnerController == null ||
            localOwnerController.IsStunned ||
            !LoadingTransitionController.IsGameplayReleased)
        {
            return false;
        }

        return localOwnerController.TryUseStoredItemSlot(
            slotIndex,
            localOwnerController.GetUseDirection());
    }

    public static bool TryGetLocalBombMineStatus(
        out int charges,
        out int maxCharges,
        out float rechargeProgress,
        out float remainingSeconds)
    {
        charges = 0;
        maxCharges = localOwnerController != null
            ? localOwnerController.GetEffectiveBombMineCapacity()
            : DefaultBombMineCount;
        rechargeProgress = 0f;
        remainingSeconds = 0f;

        if (localOwnerController == null)
        {
            return false;
        }

        localOwnerController.SyncBombMineCapacityWithCurrentLevel();
        charges = localOwnerController.bombMineCharges;
        if (charges >= maxCharges)
        {
            rechargeProgress = 1f;
            return true;
        }

        PaintOwner owner = localOwnerController.GetOwner();
        if (!BombMine.TryGetNextRechargeRemaining(owner, out remainingSeconds))
        {
            rechargeProgress = 0f;
            return true;
        }

        float totalSeconds = Mathf.Max(
            0.01f,
            localOwnerController.GetConfiguredBombMineFuseSeconds());
        rechargeProgress = Mathf.Clamp01(1f - remainingSeconds / totalSeconds);
        return true;
    }

    private bool IsStunned
    {
        get { return statusEffects != null && statusEffects.IsStunned; }
    }

    private bool CanUseCurrentItem
    {
        get { return HasItem; }
    }

    private bool CanUseFallbackBombMine
    {
        get { return !HasItem && bombMineCharges > 0; }
    }

    private bool CanUseAnyItem
    {
        get { return CanUseCurrentItem || CanUseFallbackBombMine; }
    }

    private bool HasVisibleItemSlot
    {
        get { return HasItem || HasBombMineAbility; }
    }

    private bool HasBombMineAbility
    {
        get { return true; }
    }

    private DropItemEffectType ActiveUseItemType
    {
        get
        {
            return HasItem
                ? heldItemType
                : DropItemEffectType.BombMine;
        }
    }

    [Serializable]
    private struct StoredItemSlot
    {
        [SerializeField] private DropItemEffectType itemType;
        [SerializeField] private float duration;
        [SerializeField] private float radius;
        [SerializeField] private int paintRadius;

        public DropItemEffectType ItemType { get { return itemType; } }
        public float Duration { get { return duration; } }
        public float Radius { get { return radius; } }
        public int PaintRadius { get { return paintRadius; } }
        public bool HasItem { get { return itemType != DropItemEffectType.None; } }

        public static StoredItemSlot Create(
            DropItemEffectType itemType,
            float duration,
            float radius,
            int paintRadius)
        {
            return new StoredItemSlot
            {
                itemType = itemType,
                duration = duration,
                radius = radius,
                paintRadius = paintRadius
            };
        }

        public void Clear()
        {
            itemType = DropItemEffectType.None;
            duration = 0f;
            radius = 0f;
            paintRadius = 0;
        }
    }

    private void Awake()
    {
        identity = GetComponent<PlayerPaintIdentity>();
        player = GetComponent<Player>();
        movement = GetComponent<PlayerMovement>();
        RefreshLevelStatsReference();
        RegisterControlInversionIndicatorPrefab();
        RegisterTeleportLockPrefab();
        RegisterBombMineVisualPrefab();
        statusEffects = GetComponent<PlayerStatusEffectController>();
        if (statusEffects == null)
        {
            statusEffects = gameObject.AddComponent<PlayerStatusEffectController>();
        }
        InitializeStoredItemSlotsIfNeeded();
        SyncBombMineCapacityWithCurrentLevel();
        ClampBombMineChargesToLevel();
        RefreshHeldItemText();
    }

    private void OnEnable()
    {
        BombMine.MineExploded -= OnBombMineExploded;
        BombMine.MineExploded += OnBombMineExploded;
        SubscribeLevelStats();
        ClampBombMineChargesToLevel();
        TryRegisterLocalOwner();
        RefreshHeldItemText();
        RefreshHeldItemRangePreview();
    }

    private void OnValidate()
    {
        RegisterControlInversionIndicatorPrefab();
        RegisterTeleportLockPrefab();
        RegisterBombMineVisualPrefab();
    }

    private void OnDisable()
    {
        BombMine.MineExploded -= OnBombMineExploded;
        UnsubscribeLevelStats();
        if (heldItemRangePreview != null)
        {
            heldItemRangePreview.Hide();
        }

        if (localOwnerController == this)
        {
            localOwnerController = null;
            LocalHeldItemAvailabilityChanged?.Invoke(false);
            LocalStoredItemSlotsChanged?.Invoke();
        }
    }

    private void Update()
    {
        if (movement == null ||
            !movement.IsOwner ||
            !LoadingTransitionController.IsGameplayReleased)
        {
            RefreshHeldItemRangePreview();
            return;
        }

        TryRegisterLocalOwner();
        HandleShieldTestKey();
        RefreshHeldItemRangePreview();
        RefreshBombMineUiIfNeeded();

        if (IsStunned)
        {
            return;
        }
    }

    private void RefreshBombMineUiIfNeeded()
    {
        if (!HasVisibleItemSlot ||
            Time.unscaledTime < nextBombMineUiRefreshAt)
        {
            return;
        }

        SyncBombMineCapacityWithCurrentLevel();
        nextBombMineUiRefreshAt = Time.unscaledTime + BombMineUiRefreshInterval;
        RefreshHeldItemText();
    }

    public bool Store(
        DropItemEffectType itemType,
        float duration,
        float radius,
        int paintRadius)
    {
        if (!IsStoredType(itemType))
        {
            Debug.LogWarning("[Item] Store failed. " + itemType + " is not a stored item.");
            return false;
        }

        if (itemType == DropItemEffectType.BombMine)
        {
            return StoreBombMine(duration, radius, paintRadius);
        }

        InitializeStoredItemSlotsIfNeeded();
        if (!TryFindEmptyStoredItemSlot(out int slotIndex))
        {
            Debug.LogWarning(
                "[Item] Store failed. Stored item slots are full, cannot store " +
                itemType + ".");
            return false;
        }

        int normalizedPaintRadius = itemType == DropItemEffectType.DecoyStamp
            ? Mathf.Max(0, paintRadius)
            : Mathf.Max(1, paintRadius);
        storedItemSlots[slotIndex] = StoredItemSlot.Create(
            itemType,
            duration,
            radius,
            normalizedPaintRadius);
        SyncHeldItemCache();
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();

        Debug.Log(
            "[Item] Stored item acquired: " + itemType +
            " slot=" + slotIndex +
            " duration=" + duration +
            " radius=" + radius + ".");
        return true;
    }

    private bool StoreBombMine(float duration, float radius, int paintRadius)
    {
        int maxBombMineCount = GetEffectiveBombMineCapacity();
        if (bombMineCharges >= maxBombMineCount)
        {
            Debug.Log("[Item] Bomb Mine pickup consumed because charges are already full.");
            return true;
        }

        bombMineCharges = maxBombMineCount;
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();

        Debug.Log(
            "[Item] Bomb Mine acquired. charges=" + bombMineCharges +
            "/" + maxBombMineCount +
            " fuse=" + GetBombMineFuseSeconds().ToString("0.00") +
            " paintRadius=" + GetBombMinePaintRadius() + ".");
        return true;
    }

    public static bool TryUseLocalHeldItem()
    {
        if (localOwnerController == null ||
            !localOwnerController.CanUseAnyItem ||
            localOwnerController.IsStunned ||
            !LoadingTransitionController.IsGameplayReleased)
        {
            return false;
        }

        localOwnerController.UseHeldItem();
        return true;
    }

    public static bool TryUseLocalBombMineAbility()
    {
        if (localOwnerController == null)
        {
            return false;
        }

        localOwnerController.SyncBombMineCapacityWithCurrentLevel();
        if (!localOwnerController.CanUseBombMineAbility ||
            localOwnerController.IsStunned ||
            !LoadingTransitionController.IsGameplayReleased)
        {
            return false;
        }

        return localOwnerController.TryUseBombMineAbility(
            localOwnerController.GetUseDirection());
    }

    public void UseHeldItem()
    {
        UseHeldItem(GetUseDirection());
    }

    public void UseHeldItem(Vector3 useDirection)
    {
        if (IsStunned || !CanUseAnyItem)
        {
            return;
        }

        if (TryFindFirstStoredItemSlot(out int slotIndex))
        {
            TryUseStoredItemSlot(slotIndex, useDirection);
            return;
        }

        TryUseBombMineAbility(useDirection);
    }

    private void HandleShieldTestKey()
    {
        if (!enableShieldTestKey ||
            IsStunned ||
            !LoadingTransitionController.IsGameplayReleased ||
            !WasShieldTestKeyPressed())
        {
            return;
        }

        PaintOwner owner = GetOwner();
        if (owner == PaintOwner.Neutral)
        {
            return;
        }

        float duration = Mathf.Max(0.1f, shieldTestDuration);
        if (player != null && player.IsClientInitialized)
        {
            player.ServerUseStoredItem(
                DropItemEffectType.Shield,
                transform.position,
                GetUseDirection(),
                owner,
                duration,
                0f,
                0);
            return;
        }

        ApplyShieldToOwnerLocal(owner, duration);
    }

    private bool WasShieldTestKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[shieldTestKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(shieldTestKey);
#endif
    }

    public bool TryUseStoredItemSlot(int slotIndex, Vector3 useDirection)
    {
        if (IsStunned ||
            !TryGetStoredItemSlot(slotIndex, out StoredItemSlot slot))
        {
            return false;
        }

        DropItemEffectType itemType = slot.ItemType;
        float duration = slot.Duration;
        float radius = slot.Radius;
        int paintRadius = slot.PaintRadius;
        PaintOwner owner = GetOwner();
        Vector3 position = transform.position;
        Vector3 direction = NormalizeUseDirection(useDirection);

        ClearStoredItemSlot(slotIndex);

        Debug.Log(
            "[Item] Stored item used: " + itemType +
            " slot=" + slotIndex +
            " owner=" + owner +
            " position=" + position +
            " duration=" + duration +
            " radius=" + radius + ".");

        if (player != null && player.IsClientInitialized)
        {
            player.ServerUseStoredItem(
                itemType,
                position,
                direction,
                owner,
                duration,
                radius,
                paintRadius);
            return true;
        }

        UseStoredItemLocal(
            itemType,
            position,
            direction,
            owner,
            duration,
            radius,
            paintRadius,
            gameObject);
        return true;
    }

    public bool TryUseBombMineAbility(Vector3 useDirection)
    {
        SyncBombMineCapacityWithCurrentLevel();
        if (!CanUseBombMineAbility)
        {
            return false;
        }

        PaintOwner owner = GetOwner();
        Vector3 position = transform.position;
        float duration = GetBombMineFuseSeconds();
        int paintRadius = GetBombMinePaintRadius();
        int ownerLevel = GetOwnerLevel();
        int maxBombMineCount = GetEffectiveBombMineCapacity();
        if (!BombMine.CanPlace(position, owner, maxBombMineCount))
        {
            return false;
        }

        Vector3 direction = NormalizeUseDirection(useDirection);
        if (player != null && player.IsServerInitialized)
        {
            if (!player.ServerTryPlaceBombMineForAi(position, owner, duration, paintRadius))
            {
                return false;
            }

            ConfirmBombMinePlaced();
            return true;
        }

        if (player != null && player.IsClientInitialized)
        {
            player.ServerUseStoredItem(
                DropItemEffectType.BombMine,
                position,
                direction,
                owner,
                duration,
                GetBombMineRadius(),
                paintRadius);
            return true;
        }

        if (BombMine.Place(
            position,
            owner,
            duration,
            paintRadius,
            ownerLevel,
            maxBombMineCount) == null)
        {
            return false;
        }

        ConfirmBombMinePlaced();
        return true;
    }

    public static void ServerApplyControlInversionBomb(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        int hitCount = 0;
        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerMovement movement = movements[i];
            if (movement == null)
            {
                continue;
            }

            PlayerPaintIdentity targetIdentity =
                movement.GetComponent<PlayerPaintIdentity>();
            if (targetIdentity == null ||
                targetIdentity.Owner == owner ||
                targetIdentity.Owner == PaintOwner.Neutral ||
                PlayerShieldController.IsProtected(targetIdentity))
            {
                continue;
            }

            float sqrDistance = (movement.transform.position - position).sqrMagnitude;
            if (sqrDistance > radius * radius)
            {
                continue;
            }

            if (movement.Owner.IsValid)
            {
                movement.ObserversShowControlInversionIndicator(duration);
                movement.TargetApplyControlInversion(movement.Owner, duration);
            }
            else
            {
                movement.ApplyControlInversion(duration);
            }

            hitCount++;
            Debug.Log(
                "[Item] ReverseControlsBomb hit player owner=" +
                targetIdentity.Owner +
                " distance=" +
                Mathf.Sqrt(sqrDistance).ToString("0.00") + ".");
        }

        hitCount += ApplyControlInversionToNetworkBots(position, owner, duration, radius);
        hitCount += ApplyControlInversionToLocalBots(position, owner, duration, radius);

        Debug.Log(
            "[Item] ReverseControlsBomb resolved. owner=" + owner +
            " hits=" + hitCount +
            " radius=" + radius + ".");
    }

    public static bool ServerUseTeleportLock(
        Vector3 position,
        PaintOwner owner,
        float duration,
        out PaintOwner targetOwner)
    {
        if (!TryFindLeadingOpponent(owner, out PlayerMovement target, out targetOwner))
        {
            Debug.Log("[Item] TeleportLock found no eligible leading opponent.");
            return false;
        }

        float travelDelay = TeleportLockProjectile.GetEstimatedTravelDelay(
            position,
            target.transform);
        ApplyTeleportLockAfterDelayAsync(
            target,
            targetOwner,
            duration,
            travelDelay).Forget();

        Debug.Log(
            "[Item] TeleportLock launched. owner=" + owner +
            " target=" + targetOwner +
            " delay=" + travelDelay.ToString("0.00") +
            " duration=" + duration.ToString("0.00") + ".");
        return true;
    }

    private static int ApplyControlInversionToNetworkBots(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        int hitCount = 0;
        NetworkBotController[] bots = FindObjectsByType<NetworkBotController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < bots.Length; i++)
        {
            PlayerPaintIdentity identity =
                bots[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(identity, owner))
            {
                continue;
            }

            float sqrDistance = (bots[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                bots[i].ApplyControlInversion(duration);
                hitCount++;
                Debug.Log(
                    "[Item] ReverseControlsBomb hit network bot owner=" +
                    identity.Owner +
                    " distance=" +
                    Mathf.Sqrt(sqrDistance).ToString("0.00") + ".");
            }
        }

        return hitCount;
    }

    private static int ApplyControlInversionToLocalBots(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        int hitCount = 0;
        BotPlayerController[] bots = FindObjectsByType<BotPlayerController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < bots.Length; i++)
        {
            PlayerPaintIdentity identity =
                bots[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(identity, owner))
            {
                continue;
            }

            float sqrDistance = (bots[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                bots[i].ApplyControlInversion(duration);
                hitCount++;
                Debug.Log(
                    "[Item] ReverseControlsBomb hit local bot owner=" +
                    identity.Owner +
                    " distance=" +
                    Mathf.Sqrt(sqrDistance).ToString("0.00") + ".");
            }
        }

        return hitCount;
    }

    public static void UseStoredItemLocal(
        DropItemEffectType itemType,
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius,
        GameObject sourceCharacter = null)
    {
        switch (itemType)
        {
            case DropItemEffectType.ReverseControlsBomb:
                ApplyControlInversionBombLocal(position, owner, duration, radius);
                break;
            case DropItemEffectType.TeleportLock:
                UseTeleportLockLocal(position, owner, duration);
                break;
            case DropItemEffectType.DecoyStamp:
                DecoyStampProjectile.Spawn(
                    position,
                    direction,
                    owner,
                    duration,
                    radius,
                    paintRadius,
                    sourceCharacter);
                break;
            case DropItemEffectType.BombMine:
                BombMine.Place(
                    position,
                    owner,
                    duration,
                    paintRadius);
                break;
            case DropItemEffectType.Shield:
                ApplyShieldToOwnerLocal(owner, duration);
                break;
        }
    }

    public static bool ApplyShieldToOwnerLocal(PaintOwner owner, float duration)
    {
        return PlayerShieldController.ApplyShieldToOwner(owner, duration);
    }

    public static void ApplyControlInversionBombLocal(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        int hitCount = 0;
        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerPaintIdentity targetIdentity =
                movements[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(targetIdentity, owner))
            {
                continue;
            }

            float sqrDistance = (movements[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                movements[i].ApplyControlInversion(duration);
                hitCount++;
                Debug.Log(
                    "[Item] ReverseControlsBomb hit local player owner=" +
                    targetIdentity.Owner +
                    " distance=" +
                    Mathf.Sqrt(sqrDistance).ToString("0.00") + ".");
            }
        }

        hitCount += ApplyControlInversionToNetworkBots(position, owner, duration, radius);
        hitCount += ApplyControlInversionToLocalBots(position, owner, duration, radius);

        Debug.Log(
            "[Item] ReverseControlsBomb resolved locally. owner=" + owner +
            " hits=" + hitCount +
            " radius=" + radius + ".");
    }

    public static void ShowControlInversionBombIndicatorsLocal(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerPaintIdentity targetIdentity =
                movements[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(targetIdentity, owner))
            {
                continue;
            }

            float sqrDistance = (movements[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                ShowControlInversionIndicator(movements[i].gameObject, duration);
            }
        }

        ShowControlInversionIndicatorsOnNetworkBots(position, owner, duration, radius);
        ShowControlInversionIndicatorsOnLocalBots(position, owner, duration, radius);
    }

    private static void ShowControlInversionIndicatorsOnNetworkBots(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        NetworkBotController[] bots = FindObjectsByType<NetworkBotController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < bots.Length; i++)
        {
            PlayerPaintIdentity identity =
                bots[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(identity, owner))
            {
                continue;
            }

            float sqrDistance = (bots[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                ShowControlInversionIndicator(bots[i].gameObject, duration);
            }
        }
    }

    private static void ShowControlInversionIndicatorsOnLocalBots(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        BotPlayerController[] bots = FindObjectsByType<BotPlayerController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < bots.Length; i++)
        {
            PlayerPaintIdentity identity =
                bots[i].GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(identity, owner))
            {
                continue;
            }

            float sqrDistance = (bots[i].transform.position - position).sqrMagnitude;
            if (sqrDistance <= radius * radius)
            {
                ShowControlInversionIndicator(bots[i].gameObject, duration);
            }
        }
    }

    public static bool TryFindLeadingOpponentOwner(
        PaintOwner owner,
        out PaintOwner targetOwner)
    {
        return TryFindLeadingOpponent(owner, out PlayerMovement _, out targetOwner);
    }

    private static void UseTeleportLockLocal(
        Vector3 position,
        PaintOwner owner,
        float duration)
    {
        if (!TryFindLeadingOpponent(owner, out PlayerMovement target, out PaintOwner targetOwner))
        {
            Debug.Log("[Item] TeleportLock local use found no eligible leading opponent.");
            return;
        }

        TeleportLockProjectile.Spawn(position, owner, targetOwner);
        float travelDelay = TeleportLockProjectile.GetEstimatedTravelDelay(
            position,
            target.transform);
        ApplyTeleportLockAfterDelayAsync(
            target,
            targetOwner,
            duration,
            travelDelay).Forget();
    }

    private static async UniTaskVoid ApplyTeleportLockAfterDelayAsync(
        PlayerMovement target,
        PaintOwner expectedOwner,
        float duration,
        float delay)
    {
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, delay)));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (target == null)
        {
            return;
        }

        PlayerPaintIdentity targetIdentity =
            target.GetComponent<PlayerPaintIdentity>();
        if (targetIdentity == null || targetIdentity.Owner != expectedOwner)
        {
            return;
        }

        if (PlayerShieldController.IsProtected(targetIdentity))
        {
            Debug.Log("[Item] TeleportLock blocked by shield. target=" + expectedOwner + ".");
            return;
        }

        if (target.Owner.IsValid)
        {
            target.ObserversShowTeleportLockIndicator(duration);
            target.TargetApplyTeleportLock(target.Owner, duration);
        }
        else
        {
            target.ApplyTeleportLock(duration);
        }
    }

    private static bool TryFindLeadingOpponent(
        PaintOwner owner,
        out PlayerMovement target,
        out PaintOwner targetOwner)
    {
        target = null;
        targetOwner = PaintOwner.Neutral;

        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        PlayerLevelStats bestStats = null;

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerMovement candidate = movements[i];
            if (candidate == null)
            {
                continue;
            }

            PlayerPaintIdentity candidateIdentity =
                candidate.GetComponent<PlayerPaintIdentity>();
            if (!CanAffect(candidateIdentity, owner))
            {
                continue;
            }

            PlayerLevelStats candidateStats =
                candidate.GetComponent<PlayerLevelStats>();
            if (target == null ||
                IsBetterLeaderboardTarget(candidateStats, bestStats))
            {
                target = candidate;
                targetOwner = candidateIdentity.Owner;
                bestStats = candidateStats;
            }
        }

        return target != null && targetOwner != PaintOwner.Neutral;
    }

    private static bool IsBetterLeaderboardTarget(
        PlayerLevelStats candidate,
        PlayerLevelStats currentBest)
    {
        if (currentBest == null)
        {
            return candidate != null;
        }

        if (candidate == null)
        {
            return false;
        }

        if (candidate.ClaimedTiles != currentBest.ClaimedTiles)
        {
            return candidate.ClaimedTiles > currentBest.ClaimedTiles;
        }

        if (candidate.CurrentExp != currentBest.CurrentExp)
        {
            return candidate.CurrentExp > currentBest.CurrentExp;
        }

        if (candidate.Level != currentBest.Level)
        {
            return candidate.Level > currentBest.Level;
        }

        return candidate.LastClaimTimestamp > currentBest.LastClaimTimestamp;
    }

    private PaintOwner GetOwner()
    {
        if (identity == null)
        {
            identity = GetComponent<PlayerPaintIdentity>();
        }

        return identity != null ? identity.Owner : PaintOwner.Neutral;
    }

    private Vector3 GetUseDirection()
    {
        return NormalizeUseDirection(transform.forward);
    }

    private static Vector3 NormalizeUseDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        return direction.normalized;
    }

    private void InitializeStoredItemSlotsIfNeeded()
    {
        if (storedItemSlotsInitialized)
        {
            return;
        }

        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            storedItemSlots[i].Clear();
        }

        storedItemSlotsInitialized = true;
        SyncHeldItemCache();
    }

    private bool TryGetStoredItemSlot(int slotIndex, out StoredItemSlot slot)
    {
        InitializeStoredItemSlotsIfNeeded();
        slot = new StoredItemSlot();

        if (slotIndex < 0 || slotIndex >= storedItemSlots.Length)
        {
            return false;
        }

        slot = storedItemSlots[slotIndex];
        return slot.HasItem;
    }

    private bool CanUseStoredItemSlot(int slotIndex)
    {
        return !IsStunned &&
               TryGetStoredItemSlot(slotIndex, out StoredItemSlot slot) &&
               slot.ItemType != DropItemEffectType.BombMine;
    }

    private bool TryFindFirstStoredItemSlot(out int slotIndex)
    {
        InitializeStoredItemSlotsIfNeeded();

        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            if (storedItemSlots[i].HasItem)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    private bool TryFindFirstStoredItemSlot(
        DropItemEffectType itemType,
        out int slotIndex)
    {
        InitializeStoredItemSlotsIfNeeded();

        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            if (storedItemSlots[i].ItemType == itemType)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    private bool TryFindEmptyStoredItemSlot(out int slotIndex)
    {
        InitializeStoredItemSlotsIfNeeded();

        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            if (!storedItemSlots[i].HasItem)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    private int GetStoredItemCount()
    {
        InitializeStoredItemSlotsIfNeeded();

        int count = 0;
        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            if (storedItemSlots[i].HasItem)
            {
                count++;
            }
        }

        return count;
    }

    private void SyncHeldItemCache()
    {
        heldItemType = DropItemEffectType.None;
        heldDuration = 0f;
        heldRadius = 0f;
        heldPaintRadius = 0;

        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            StoredItemSlot slot = storedItemSlots[i];
            if (!slot.HasItem)
            {
                continue;
            }

            heldItemType = slot.ItemType;
            heldDuration = slot.Duration;
            heldRadius = slot.Radius;
            heldPaintRadius = slot.PaintRadius;
            return;
        }
    }

    private void ClearStoredItemSlot(int slotIndex)
    {
        InitializeStoredItemSlotsIfNeeded();

        if (slotIndex < 0 || slotIndex >= storedItemSlots.Length)
        {
            return;
        }

        storedItemSlots[slotIndex].Clear();
        SyncHeldItemCache();
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();
    }

    private float GetBombMineFuseSeconds()
    {
        return GetConfiguredBombMineFuseSeconds();
    }

    private float GetBombMineRadius()
    {
        return DefaultBombMineRadius;
    }

    private int GetBombMinePaintRadius()
    {
        return GetBombMineCrossRadius();
    }

    private float GetConfiguredBombMineFuseSeconds()
    {
        return bombMineSettings != null
            ? bombMineSettings.FuseSeconds
            : BombMineSettings.DefaultFuseSeconds;
    }

    private int GetBombMineCrossRadius()
    {
        return bombMineSettings != null
            ? bombMineSettings.GetCrossRadiusForLevel(GetOwnerLevel())
            : BombMineSettings.GetDefaultCrossRadiusForLevel(GetOwnerLevel());
    }

    private int GetMaxBombMineCount()
    {
        return bombMineSettings != null
            ? bombMineSettings.GetMaxActiveMinesForLevel(GetOwnerLevel())
            : BombMineSettings.GetDefaultMaxActiveMinesForLevel(GetOwnerLevel());
    }

    private int GetEffectiveBombMineCapacity()
    {
        return Mathf.Max(
            DefaultBombMineCount,
            GetMaxBombMineCount());
    }

    private int GetOwnerLevel()
    {
        RefreshLevelStatsReference();

        return levelStats != null ? Mathf.Max(1, levelStats.Level) : 1;
    }

    private void ClampBombMineChargesToLevel()
    {
        bombMineCharges = Mathf.Clamp(bombMineCharges, 0, GetEffectiveBombMineCapacity());
    }

    private void RefreshLevelStatsReference()
    {
        if (levelStats == null)
        {
            levelStats = GetComponent<PlayerLevelStats>();
        }
    }

    private void SubscribeLevelStats()
    {
        RefreshLevelStatsReference();
        if (levelStats == null)
        {
            return;
        }

        if (subscribedLevelStats == levelStats)
        {
            SyncBombMineCapacityWithCurrentLevel();
            return;
        }

        UnsubscribeLevelStats();
        subscribedLevelStats = levelStats;
        subscribedLevelStats.LevelMoveSpeedChanged += OnOwnerLevelChanged;
        SyncBombMineCapacityWithCurrentLevel();
    }

    private void UnsubscribeLevelStats()
    {
        if (subscribedLevelStats == null)
        {
            return;
        }

        subscribedLevelStats.LevelMoveSpeedChanged -= OnOwnerLevelChanged;
        subscribedLevelStats = null;
    }

    private void OnOwnerLevelChanged(int level, float moveSpeed)
    {
        SyncBombMineCapacityWithCurrentLevel();
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();
    }

    private void SyncBombMineCapacityWithCurrentLevel()
    {
        int currentMax = GetEffectiveBombMineCapacity();
        int activeMineCount = BombMine.GetActiveMineCount(GetOwner());
        bombMineCharges = Mathf.Clamp(currentMax - activeMineCount, 0, currentMax);
    }

    public void ClearForRoundEnd()
    {
        bool hadItem = HasItem;
        if (hadItem)
        {
            ClearHeldItem();
        }

        int maxBombMineCount = GetEffectiveBombMineCapacity();
        if (bombMineCharges != maxBombMineCount)
        {
            bombMineCharges = maxBombMineCount;
            RefreshHeldItemText();
            NotifyLocalHeldItemChanged();
        }

        if (hadItem)
        {
            Debug.Log("[Item] Stored item cleared for round end.", this);
        }
    }

    private void ClearHeldItem()
    {
        InitializeStoredItemSlotsIfNeeded();
        for (int i = 0; i < storedItemSlots.Length; i++)
        {
            storedItemSlots[i].Clear();
        }

        SyncHeldItemCache();
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();
    }

    private void ConsumeHeldItemUse(DropItemEffectType itemType)
    {
        if (itemType != DropItemEffectType.BombMine)
        {
            if (TryFindFirstStoredItemSlot(itemType, out int slotIndex))
            {
                ClearStoredItemSlot(slotIndex);
            }
            return;
        }

        bombMineCharges = Mathf.Max(0, bombMineCharges - 1);
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();
    }

    public void ConfirmBombMinePlaced()
    {
        if (bombMineCharges <= 0)
        {
            return;
        }

        ConsumeHeldItemUse(DropItemEffectType.BombMine);
    }

    private void OnBombMineExploded(PaintOwner mineOwner)
    {
        if (!ShouldRestoreBombMineCharge(mineOwner) ||
            bombMineCharges >= GetEffectiveBombMineCapacity())
        {
            return;
        }

        bombMineCharges++;
        ClampBombMineChargesToLevel();
        RefreshHeldItemText();
        NotifyLocalHeldItemChanged();
        Debug.Log(
            "[Item] Bomb Mine charge restored. charges=" +
            bombMineCharges + "/" + GetEffectiveBombMineCapacity() + ".");
    }

    private bool ShouldRestoreBombMineCharge(PaintOwner mineOwner)
    {
        if (GetOwner() != mineOwner)
        {
            return false;
        }

        if (IsLocalOwner())
        {
            return true;
        }

        if (GetComponent<BotPlayerController>() != null)
        {
            return true;
        }

        NetworkBotController networkBot = GetComponent<NetworkBotController>();
        return networkBot != null &&
               player != null &&
               player.IsServerInitialized;
    }

    private bool IsLocalOwner()
    {
        return movement != null && movement.IsOwner;
    }

    private void TryRegisterLocalOwner()
    {
        if (!IsLocalOwner())
        {
            return;
        }

        if (localOwnerController != this)
        {
            localOwnerController = this;
            LocalHeldItemAvailabilityChanged?.Invoke(HasVisibleItemSlot);
            LocalStoredItemSlotsChanged?.Invoke();
        }
    }

    private void NotifyLocalHeldItemChanged()
    {
        if (!IsLocalOwner())
        {
            RefreshHeldItemRangePreview();
            return;
        }

        TryRegisterLocalOwner();
        LocalHeldItemAvailabilityChanged?.Invoke(HasVisibleItemSlot);
        LocalStoredItemSlotsChanged?.Invoke();
        RefreshHeldItemRangePreview();
    }

    private void EnsureHeldItemRangePreview()
    {
        if (heldItemRangePreview != null || !autoCreateHeldItemRangePreview)
        {
            return;
        }

        heldItemRangePreview = GetComponent<HeldItemRangePreview>();
        if (heldItemRangePreview == null)
        {
            heldItemRangePreview = gameObject.AddComponent<HeldItemRangePreview>();
        }
    }

    private void RefreshHeldItemRangePreview()
    {
        bool shouldShow =
            IsLocalOwner() &&
            heldItemType == DropItemEffectType.ReverseControlsBomb &&
            heldRadius > 0f &&
            LoadingTransitionController.IsGameplayReleased;

        if (!shouldShow)
        {
            if (heldItemRangePreview != null)
            {
                heldItemRangePreview.Hide();
            }

            return;
        }

        EnsureHeldItemRangePreview();
        if (heldItemRangePreview != null)
        {
            heldItemRangePreview.Show(transform, heldRadius, GetOwner());
        }
    }

    private static bool IsStoredType(DropItemEffectType itemType)
    {
        return itemType == DropItemEffectType.ReverseControlsBomb ||
               itemType == DropItemEffectType.TeleportLock ||
               itemType == DropItemEffectType.DecoyStamp ||
               itemType == DropItemEffectType.BombMine ||
               itemType == DropItemEffectType.Shield;
    }

    private static bool CanAffect(PlayerPaintIdentity targetIdentity, PaintOwner owner)
    {
        return targetIdentity != null &&
               targetIdentity.Owner != owner &&
               targetIdentity.Owner != PaintOwner.Neutral &&
               !PlayerShieldController.IsProtected(targetIdentity);
    }

    private static void ShowControlInversionIndicator(GameObject target, float duration)
    {
        if (target == null)
        {
            return;
        }

        ControlInversionIndicator indicator =
            target.GetComponent<ControlInversionIndicator>();
        if (indicator == null)
        {
            indicator = target.AddComponent<ControlInversionIndicator>();
        }

        if (localOwnerController != null &&
            localOwnerController.controlInversionIndicatorPrefab != null)
        {
            indicator.SetIndicatorPrefab(localOwnerController.controlInversionIndicatorPrefab);
        }

        indicator.Show(duration);
    }

    private void RegisterControlInversionIndicatorPrefab()
    {
        if (controlInversionIndicatorPrefab != null)
        {
            ControlInversionIndicator.SetSharedIndicatorPrefab(
                controlInversionIndicatorPrefab);
        }
    }

    private void RegisterTeleportLockPrefab()
    {
        if (teleportLockIndicatorPrefab != null)
        {
            TeleportLockIndicator.SetSharedIndicatorPrefab(teleportLockIndicatorPrefab);
            TeleportLockProjectile.SetSharedVisualPrefab(teleportLockIndicatorPrefab);
        }
    }

    private void RegisterBombMineVisualPrefab()
    {
        if (bombMineSettings != null)
        {
            BombMine.ApplySettings(bombMineSettings);
        }
    }

    private void RefreshHeldItemText()
    {
        if (!ShouldUpdateHeldItemText())
        {
            return;
        }

        EnsureHeldItemText();
        if (heldItemText == null)
        {
            return;
        }

        if (!HasVisibleItemSlot)
        {
            heldItemText.text = emptyItemLabel;
            return;
        }

        DropItemEffectType displayType = ActiveUseItemType;
        string text = displayType == DropItemEffectType.BombMine
            ? "Mine " + GetBombMineUiStatus()
            : "Items " + GetStoredItemCount() + "/" + StoredItemSlotCount +
              ": " + GetHeldItemDisplayName(displayType) +
              " | Mine " + GetBombMineUiStatus();

        if (heldItemText.text != text)
        {
            heldItemText.text = text;
        }
    }

    private string GetBombMineUiStatus()
    {
        int maxBombMineCount = GetEffectiveBombMineCapacity();
        string status = "x" + bombMineCharges + "/" + maxBombMineCount;
        if (bombMineCharges >= maxBombMineCount)
        {
            return status;
        }

        if (BombMine.TryGetNextRechargeRemaining(GetOwner(), out float remainingSeconds))
        {
            return status + " (+1 in " + remainingSeconds.ToString("0.0") + "s)";
        }

        return status + " (+1 pending)";
    }

    private bool ShouldUpdateHeldItemText()
    {
        return movement == null || movement.IsOwner;
    }

    private void EnsureHeldItemText()
    {
        if (heldItemText != null || !autoFindHeldItemText)
        {
            return;
        }

        TMP_Text[] texts = FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == heldItemTextObjectName)
            {
                heldItemText = texts[i];
                break;
            }
        }
    }

    private string GetHeldItemDisplayName(DropItemEffectType itemType)
    {
        if (itemCatalog != null)
        {
            return itemCatalog.GetDisplayName(itemType);
        }

        switch (itemType)
        {
            case DropItemEffectType.ReverseControlsBomb:
                return "Reverse Controls Bomb";
            case DropItemEffectType.TeleportLock:
                return "Teleport Lock";
            case DropItemEffectType.DecoyStamp:
                return "Decoy Stamp";
            case DropItemEffectType.BombMine:
                return "Bomb Mine";
            case DropItemEffectType.Shield:
                return "Shield";
            default:
                return itemType.ToString();
        }
    }

}
