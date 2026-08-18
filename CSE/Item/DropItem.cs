using UnityEngine;

public enum DropItemEffectType
{
    None = -1,
    SpeedBoost = 0,
    Experience = 1,
    CooldownReset = 2,
    ReverseControlsBomb = 3,
    TeleportLock = 4,
    PaintShell = 5,
    DecoyStamp = 6,
    BombMine = 7,
    Shield = 8
}

[System.Serializable]
public struct DropItemSpawnConfig
{
    [SerializeField] private DropItemEffectType effectType;
    [SerializeField] private GameObject prefab;
    [SerializeField] private float weight;
    [SerializeField] private float speedMultiplier;
    [SerializeField] private float duration;
    [SerializeField] private int experienceAmount;
    [SerializeField] private float cooldownReductionSeconds;
    [SerializeField] private bool clearCooldown;
    [SerializeField] private bool grantPickupExperience;
    [SerializeField] private float useRadius;
    [SerializeField] private int paintRadius;

    public DropItemEffectType EffectType => effectType;
    public GameObject Prefab => prefab;
    public float Weight => weight <= 0f ? 1f : weight;
    public float SpeedMultiplier => speedMultiplier <= 0f ? 1.5f : speedMultiplier;
    public float Duration => duration <= 0f ? GetDefaultDuration(effectType) : duration;
    public int ExperienceAmount => Mathf.Max(0, experienceAmount);
    public float CooldownReductionSeconds => Mathf.Max(0f, cooldownReductionSeconds);
    public bool ClearCooldown => clearCooldown;
    public bool GrantPickupExperience => grantPickupExperience;
    public float UseRadius => useRadius <= 0f ? 5f : useRadius;
    public int PaintRadius =>
        effectType == DropItemEffectType.DecoyStamp
            ? Mathf.Max(0, paintRadius)
            : effectType == DropItemEffectType.BombMine
                ? paintRadius <= 0 ? BombMineSettings.DefaultCrossRadius : paintRadius
            : paintRadius <= 0 ? 1 : paintRadius;

    public DropItemSpawnConfig WithBombMineSettings(BombMineSettings settings)
    {
        if (effectType != DropItemEffectType.BombMine)
        {
            return this;
        }

        DropItemSpawnConfig copy = this;
        copy.duration = settings != null
            ? settings.FuseSeconds
            : BombMineSettings.DefaultFuseSeconds;
        copy.paintRadius = settings != null
            ? settings.CrossRadius
            : BombMineSettings.DefaultCrossRadius;
        return copy;
    }

    public static DropItemSpawnConfig Create(
        DropItemEffectType effectType,
        GameObject prefab,
        float weight,
        float speedMultiplier,
        float duration,
        int experienceAmount,
        float cooldownReductionSeconds,
        bool clearCooldown,
        bool grantPickupExperience,
        float useRadius,
        int paintRadius)
    {
        return new DropItemSpawnConfig
        {
            effectType = effectType,
            prefab = prefab,
            weight = weight,
            speedMultiplier = speedMultiplier,
            duration = duration,
            experienceAmount = experienceAmount,
            cooldownReductionSeconds = cooldownReductionSeconds,
            clearCooldown = clearCooldown,
            grantPickupExperience = grantPickupExperience,
            useRadius = useRadius,
            paintRadius = paintRadius
        };
    }

    private static float GetDefaultDuration(DropItemEffectType itemType)
    {
        return itemType == DropItemEffectType.BombMine
            ? BombMineSettings.DefaultFuseSeconds
            : itemType == DropItemEffectType.Shield
                ? PlayerShieldController.DefaultShieldSeconds
            : 3f;
    }

    public static DropItemSpawnConfig SpeedDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.SpeedBoost,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 3f,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 5f,
            paintRadius = 1
        };
    }

    public static DropItemSpawnConfig ExperienceDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.Experience,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 3f,
            experienceAmount = 250,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = false,
            useRadius = 5f,
            paintRadius = 1
        };
    }

    public static DropItemSpawnConfig CooldownResetDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.CooldownReset,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 3f,
            experienceAmount = 0,
            cooldownReductionSeconds = 999f,
            clearCooldown = true,
            grantPickupExperience = true,
            useRadius = 5f,
            paintRadius = 1
        };
    }

    public static DropItemSpawnConfig ReverseControlsBombDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.ReverseControlsBomb,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 8f,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 3.5f,
            paintRadius = 1
        };
    }

    public static DropItemSpawnConfig PaintShellDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.PaintShell,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 3f,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 1.5f,
            paintRadius = 1
        };
    }

    public static DropItemSpawnConfig TeleportLockDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.TeleportLock,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 5f,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 1.0f,
            paintRadius = 0
        };
    }

    public static DropItemSpawnConfig DecoyStampDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.DecoyStamp,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = 4f,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 1.0f,
            paintRadius = 0
        };
    }

    public static DropItemSpawnConfig ShieldDefault(GameObject defaultPrefab)
    {
        return new DropItemSpawnConfig
        {
            effectType = DropItemEffectType.Shield,
            prefab = defaultPrefab,
            weight = 1f,
            speedMultiplier = 1.5f,
            duration = PlayerShieldController.DefaultShieldSeconds,
            experienceAmount = 0,
            cooldownReductionSeconds = 0f,
            clearCooldown = false,
            grantPickupExperience = true,
            useRadius = 1f,
            paintRadius = 0
        };
    }
}

public class DropItem : MonoBehaviour
{
    private const float SpeedBoostTargetSpeed = 11f;

    [Header("Legacy Speed Settings")]
    public float speedMultipler = 1.5f;
    public float duration = 3f;

    [Header("Runtime Item Effect")]
    [SerializeField] private DropItemEffectType effectType = DropItemEffectType.SpeedBoost;
    [SerializeField] private int experienceAmount = 150;
    [SerializeField] private float cooldownReductionSeconds = 999f;
    [SerializeField] private bool clearCooldown = true;
    [SerializeField] private bool grantPickupExperience = true;
    [SerializeField] private float useRadius = 5f;
    [SerializeField] private int paintRadius = 1;
    [SerializeField] private float pickupScanInterval = 0.25f;
    [SerializeField] private float pickupScanPadding = 0.75f;

    private Rigidbody rb;
    private BoxCollider col;
    private bool isLanded;
    private bool isCollected;
    private float nextPickupScanAt;
    private bool isDisabledItem;
    private bool isBeingSucked;
    private readonly Collider[] pickupHits = new Collider[24];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<BoxCollider>();
        isDisabledItem = IsDisabledDropItem(effectType);

        Renderer itemRenderer = GetComponent<Renderer>();
        if (itemRenderer != null && itemRenderer.sharedMaterial != null &&
            itemRenderer.sharedMaterial.shader.name == "PaintWar/Tile Grid Surface")
        {
            itemRenderer.sharedMaterial =
                SolidColorMaterialUtility.Create(itemRenderer.sharedMaterial, "_Item");
        }
    }

    public bool Configure(DropItemSpawnConfig config)
    {
        effectType = config.EffectType;
        isDisabledItem = IsDisabledDropItem(effectType);
        speedMultipler = config.SpeedMultiplier;
        duration = config.Duration;
        experienceAmount = config.ExperienceAmount;
        cooldownReductionSeconds = config.CooldownReductionSeconds;
        clearCooldown = config.ClearCooldown;
        grantPickupExperience = config.GrantPickupExperience;
        useRadius = config.UseRadius;
        paintRadius = config.PaintRadius;
        return !isDisabledItem;
    }

    public void PlaceOnGround(float surfaceY, float surfaceOffset)
    {
        EnsureCachedComponents();

        if (rb != null)
        {
            rb.useGravity = false;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
        }

        if (col != null)
        {
            col.isTrigger = true;
        }

        isLanded = true;
        AlignBottomToSurface(surfaceY + Mathf.Max(0f, surfaceOffset));
    }

    public bool BeginBlackHoleSuction()
    {
        if (isCollected || isBeingSucked || !gameObject.activeInHierarchy)
        {
            return false;
        }

        EnsureCachedComponents();
        isBeingSucked = true;
        isLanded = false;
        if (rb != null)
        {
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (col != null)
        {
            col.enabled = false;
        }

        return true;
    }

    public void CancelBlackHoleSuction()
    {
        if (!isBeingSucked)
        {
            return;
        }

        EnsureCachedComponents();
        isBeingSucked = false;
        isLanded = true;
        nextPickupScanAt = Time.time + Mathf.Max(0.02f, pickupScanInterval);
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = true;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isLanded || !collision.gameObject.CompareTag("Floor"))
        {
            return;
        }

        isLanded = true;
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (TryRemoveDisabledItem())
        {
            return;
        }

        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (TryRemoveDisabledItem())
        {
            return;
        }

        TryCollect(other);
    }

    private void Update()
    {
        if (isBeingSucked)
        {
            return;
        }

        if (TryRemoveDisabledItem())
        {
            return;
        }

        if (!isLanded ||
            isCollected ||
            Time.time < nextPickupScanAt)
        {
            return;
        }

        nextPickupScanAt = Time.time + Mathf.Max(0.02f, pickupScanInterval);
        ScanForNearbyCollectors();
    }

    private void ScanForNearbyCollectors()
    {
        float radius = GetPickupScanRadius();
        Vector3 center = GetPickupScanCenter();
        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            pickupHits,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = pickupHits[i];
            pickupHits[i] = null;
            if (TryCollect(hit))
            {
                return;
            }
        }
    }

    private bool TryRemoveDisabledItem()
    {
        if (!isDisabledItem || isCollected || isBeingSucked)
        {
            return false;
        }

        isCollected = true;
        Debug.LogWarning("[Item] Disabled drop was removed from world. type=" + effectType + ".");
        if (ItemPool.Instance != null)
        {
            ItemPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }

        return true;
    }

    private bool TryCollect(Collider other)
    {
        if (isCollected || isBeingSucked || other == null)
        {
            return false;
        }

        PlayerMovement movement = other.GetComponentInParent<PlayerMovement>();
        PlayerPaintIdentity identity = other.GetComponentInParent<PlayerPaintIdentity>();
        if (movement == null && identity == null)
        {
            return false;
        }

        bool isTaggedPlayer =
            other.CompareTag("Player") ||
            (identity != null && identity.CompareTag("Player"));
        if (movement == null && !isTaggedPlayer)
        {
            return false;
        }

        if (IsStoredEffect() && movement != null && !movement.IsOwner)
        {
            Debug.Log(
                "[Item] Stored item pickup ignored on non-owner object: " +
                effectType + ".");
            return false;
        }

        if (!ApplyEffect(other, movement, identity))
        {
            Debug.LogWarning(
                "[Item] Pickup failed, item remains in world. type=" +
                effectType +
                " collector=" + other.name + ".");
            return false;
        }

        isCollected = true;
        Debug.Log(
            "[Item] Item collected and removed from world. type=" +
            effectType +
            " collectorOwner=" +
            (identity != null ? identity.Owner.ToString() : "Unknown") + ".");
        GrantPickupExpIfNeeded(identity);

        if (ItemPool.Instance != null)
        {
            ItemPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }

        return true;
    }

    private bool ApplyEffect(
        Collider collector,
        PlayerMovement movement,
        PlayerPaintIdentity identity)
    {
        switch (effectType)
        {
            case DropItemEffectType.Experience:
                ApplyExperience(identity);
                return true;
            case DropItemEffectType.CooldownReset:
                ApplyCooldownReset(collector);
                return true;
            case DropItemEffectType.PaintShell:
            case DropItemEffectType.BombMine:
                return false;
            case DropItemEffectType.ReverseControlsBomb:
            case DropItemEffectType.TeleportLock:
            case DropItemEffectType.DecoyStamp:
            case DropItemEffectType.Shield:
                return StoreItem(collector);
            case DropItemEffectType.SpeedBoost:
            default:
                return ApplySpeedBoost(collector, movement);
        }
    }

    private bool StoreItem(Collider collector)
    {
        PlayerStoredItemController itemController =
            collector.GetComponentInParent<PlayerStoredItemController>();
        if (itemController == null)
        {
            Player player = collector.GetComponentInParent<Player>();
            if (player == null)
            {
                return false;
            }

            itemController = player.gameObject.AddComponent<PlayerStoredItemController>();
        }

        return itemController != null &&
               itemController.Store(
                   effectType,
                   duration,
                   useRadius,
                   paintRadius);
    }

    private bool ApplySpeedBoost(Collider collector, PlayerMovement movement)
    {
        if (movement != null)
        {
            Debug.Log("[Item] Speed boost acquired.");
            movement.ApplySpeedBoostToSpeed(SpeedBoostTargetSpeed, duration);
            return true;
        }

        NetworkBotController networkBot =
            collector.GetComponentInParent<NetworkBotController>();
        if (networkBot != null)
        {
            Debug.Log("[Item] Speed boost acquired by network bot.");
            networkBot.ApplySpeedBoostToSpeed(SpeedBoostTargetSpeed, duration);
            return true;
        }

        BotPlayerController localBot =
            collector.GetComponentInParent<BotPlayerController>();
        if (localBot != null)
        {
            Debug.Log("[Item] Speed boost acquired by local bot.");
            localBot.ApplySpeedBoostToSpeed(SpeedBoostTargetSpeed, duration);
            return true;
        }

        return false;
    }

    private void ApplyExperience(PlayerPaintIdentity identity)
    {
        if (identity == null || experienceAmount <= 0)
        {
            return;
        }

        Debug.Log($"[Item] EXP item acquired: +{experienceAmount}.");
        PlayerLevelStats.AddExp(identity.Owner, experienceAmount);
    }

    private void ApplyCooldownReset(Collider collector)
    {
        WeaponRuntimeController weaponController =
            collector.GetComponentInParent<WeaponRuntimeController>();
        if (weaponController == null)
        {
            return;
        }

        if (clearCooldown)
        {
            Debug.Log("[Item] Weapon cooldown cleared.");
            weaponController.ClearCooldown();
            return;
        }

        Debug.Log($"[Item] Weapon cooldown reduced by {cooldownReductionSeconds} seconds.");
        weaponController.ReduceCooldown(cooldownReductionSeconds);
    }

    private void GrantPickupExpIfNeeded(PlayerPaintIdentity identity)
    {
        if (!grantPickupExperience || identity == null || !TileClaimService.HasInstance)
        {
            return;
        }

        TileClaimService.Instance.AddItemPickupExp(identity.Owner);
    }

    private bool IsStoredEffect()
    {
        return effectType == DropItemEffectType.ReverseControlsBomb ||
               effectType == DropItemEffectType.TeleportLock ||
               effectType == DropItemEffectType.DecoyStamp ||
               effectType == DropItemEffectType.Shield;
    }

    private static bool IsDisabledDropItem(DropItemEffectType itemType)
    {
        return itemType == DropItemEffectType.PaintShell ||
               itemType == DropItemEffectType.BombMine;
    }

    private void EnsureCachedComponents()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (col == null)
        {
            col = GetComponent<BoxCollider>();
        }
    }

    private void AlignBottomToSurface(float targetBottomY)
    {
        Physics.SyncTransforms();

        float bottomY = transform.position.y;
        if (col != null)
        {
            bottomY = col.bounds.min.y;
        }
        else
        {
            Renderer itemRenderer = GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                bottomY = itemRenderer.bounds.min.y;
            }
        }

        Vector3 position = transform.position;
        position.y += targetBottomY - bottomY;
        transform.position = position;
        Physics.SyncTransforms();
    }

    private Vector3 GetPickupScanCenter()
    {
        if (col != null)
        {
            return col.bounds.center;
        }

        Renderer itemRenderer = GetComponent<Renderer>();
        return itemRenderer != null ? itemRenderer.bounds.center : transform.position;
    }

    private float GetPickupScanRadius()
    {
        float radius = 0.35f;
        if (col != null)
        {
            Bounds bounds = col.bounds;
            radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        }
        else
        {
            Renderer itemRenderer = GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                Bounds bounds = itemRenderer.bounds;
                radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            }
        }

        return radius + Mathf.Max(0.05f, pickupScanPadding);
    }

    private void OnEnable()
    {
        isLanded = false;
        isCollected = false;
        isBeingSucked = false;
        nextPickupScanAt = 0f;
        isDisabledItem = IsDisabledDropItem(effectType);

        if (rb != null)
        {
            rb.useGravity = true;
            rb.isKinematic = false;
        }

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = false;
        }

        TryRemoveDisabledItem();
    }
}
