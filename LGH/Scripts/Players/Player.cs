using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// 네트워크 플레이어의 공통 lifecycle과 SyncVar를 소유합니다.
/// 세부 역할은 Player.* partial 파일에서 나눠 관리합니다.
/// </summary>
public partial class Player : NetworkBehaviour
{
    // 스킨드 메시가 카메라 밖으로 잘못 판정되어 사라지지 않도록 넉넉한 bounds를 씁니다.
    private static readonly Bounds CharacterRenderBounds =
        new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(5f, 5f, 5f));
    // 머티리얼 색상 프로퍼티 ID입니다. 문자열 lookup 반복을 피하기 위해 캐시합니다.
    private static readonly int PlayerColorProperty = Shader.PropertyToID("_PlayerColor");
    private static Player localOwnedGameplayPlayer;

    // 서버가 소유하는 플레이어 색상/owner 값입니다.
    public readonly SyncVar<PaintOwner> myPaintOwner = new SyncVar<PaintOwner>(PaintOwner.Neutral);
    // 서버가 소유하는 외형 코드입니다. 클라이언트는 SyncVar 변경으로 외형을 맞춥니다.
    public readonly SyncVar<string> appearanceCode = new SyncVar<string>(string.Empty);
    public readonly SyncVar<string> motionCode = new SyncVar<string>(string.Empty);

    // 대표 renderer입니다. 비어 있으면 Awake에서 자식 renderer를 찾아 캐시합니다.
    [SerializeField] private Renderer playerRenderer;
    // 다른 코드가 현재 플레이어 머티리얼을 읽을 때 사용하는 runtime 참조입니다.
    public Material playerMaterial;

    // 플레이어별 색상만 MaterialPropertyBlock으로 전달해 공유 머티리얼을 유지합니다.
    private MaterialPropertyBlock playerAppearanceProperties;
    // PlayerLevelStats는 일반 MonoBehaviour라 Player가 observer RPC 전달 통로 역할을 합니다.
    private PlayerLevelStats levelStats;
    // 캐릭터 파츠/코스메틱 적용기입니다. 오래된 prefab에는 없을 수 있어 필요 시 생성합니다.
    private CharacterAppearanceApplicator appearanceApplicator;
    private CharacterMotionApplicator motionApplicator;
    // owner 머티리얼을 다시 적용해야 하는지 표시합니다.
    private bool paintMaterialDirty = true;
    // 라운드 상태보다 탈락 RPC를 먼저 받은 late join client는 적용을 잠시 보류합니다.
    private bool pendingEliminatedApply;
    private bool loggedPendingEliminatedApply;

    /// <summary>
    /// 컴포넌트를 캐시하고 SyncVar 변경 callback을 연결합니다.
    /// </summary>
    private void Awake()
    {
        if (playerRenderer == null)
        {
            playerRenderer = GetComponentInChildren<Renderer>();
        }

        levelStats = GetComponent<PlayerLevelStats>();
        appearanceApplicator = GetComponent<CharacterAppearanceApplicator>();
        motionApplicator = GetComponent<CharacterMotionApplicator>();
        myPaintOwner.OnChange += OnPaintOwnerChanged;
        appearanceCode.OnChange += OnAppearanceCodeChanged;
        motionCode.OnChange += OnMotionCodeChanged;

        ConfigureAnimatorCulling();
        InitializeSkillAimEffect();
    }

    /// <summary>
    /// SyncVar callback과 runtime material을 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        DisposeSkillAimEffect();

        myPaintOwner.OnChange -= OnPaintOwnerChanged;
        appearanceCode.OnChange -= OnAppearanceCodeChanged;
        motionCode.OnChange -= OnMotionCodeChanged;

        if (localOwnedGameplayPlayer == this)
        {
            localOwnedGameplayPlayer = null;
        }
    }

    /// <summary>
    /// 서버 시작 hook입니다. 현재는 FishNet lifecycle 확장 지점으로 유지합니다.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
    }

    /// <summary>
    /// 클라이언트 시작 시 현재 외형과 renderer 상태를 적용합니다.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        paintMaterialDirty = true;

        ApplyAppearance(appearanceCode.Value);
        ApplyMotion(motionCode.Value);
        EnablePlayerRenderers();

        RefreshSkillAimEffectFromNetwork();

        if (IsOwner)
        {
            localOwnedGameplayPlayer = this;
            string localAppearanceCode =
                CharacterAppearanceStore.NormalizeCode(CharacterAppearanceStore.CurrentCode);
            string localMotionCode =
                CharacterMotionStore.NormalizeProfileMotionCode(
                    CharacterMotionStore.GetCurrentProfileMotionCode());
            ApplyAppearance(localAppearanceCode);
            ApplyMotion(localMotionCode);
            ServerSubmitCosmeticProfile(
                localAppearanceCode,
                localMotionCode);
        }
    }

    public override void OnStopClient()
    {
        StopSkillAimEffectForShutdown();

        base.OnStopClient();

        if (localOwnedGameplayPlayer == this)
        {
            localOwnedGameplayPlayer = null;
        }
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);

        if (IsOwner)
        {
            localOwnedGameplayPlayer = this;
        }
        else if (localOwnedGameplayPlayer == this)
        {
            localOwnedGameplayPlayer = null;
        }
    }

    /// <summary>
    /// 시작 시점에 owner material을 다시 적용하도록 표시합니다.
    /// </summary>
    private void Start()
    {
        paintMaterialDirty = true;
    }

    /// <summary>
    /// 보류된 탈락 적용과 owner material 재적용만 처리합니다.
    /// </summary>
    private void Update()
    {
        if (pendingEliminatedApply && CanApplyEliminatedNow())
        {
            pendingEliminatedApply = false;
            loggedPendingEliminatedApply = false;
            ApplyEliminatedLocal(true);
            return;
        }

        if (paintMaterialDirty)
        {
            if (TryApplyPaintOwnerMaterial())
            {
                ApplyCosmeticTint();
            }
        }
    }

    /// <summary>
    /// owner 클라이언트가 선택한 외형 코드를 서버에 제출합니다.
    /// FishNet RPC codegen 안정성을 위해 RPC 메서드는 Player.cs 본체에 둡니다.
    /// </summary>
    [ServerRpc]
    private void ServerSubmitCosmeticProfile(
        string submittedAppearanceCode,
        string submittedMotionCode)
    {
        ServerSetAppearanceCode(submittedAppearanceCode);
        ServerSetMotionCode(submittedMotionCode);
    }

    public static bool SubmitLocalGameplayEmoji(string emojiId)
    {
        string normalizedEmojiId = string.IsNullOrWhiteSpace(emojiId)
            ? string.Empty
            : emojiId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmojiId) ||
            !TryGetLocalOwnedGameplayPlayer(out Player player))
        {
            return false;
        }

        player.ServerSubmitGameplayEmoji(normalizedEmojiId);
        return true;
    }

    private static bool TryGetLocalOwnedGameplayPlayer(out Player player)
    {
        player = localOwnedGameplayPlayer;
        if (player != null && player.isActiveAndEnabled && player.IsOwner)
        {
            return true;
        }

        localOwnedGameplayPlayer = null;
        Player[] players = FindObjectsByType<Player>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Player candidate = players[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.IsOwner)
            {
                continue;
            }

            localOwnedGameplayPlayer = candidate;
            player = candidate;
            return true;
        }

        player = null;
        return false;
    }

    [ServerRpc]
    private void ServerSubmitGameplayEmoji(string emojiId, NetworkConnection sender = null)
    {
        if (sender != null && sender != Owner)
        {
            return;
        }

        string normalizedEmojiId = string.IsNullOrWhiteSpace(emojiId)
            ? string.Empty
            : emojiId.Trim();
        LobbyEmojiCatalog catalog = LobbyEmojiCatalog.LoadDefault();
        if (string.IsNullOrWhiteSpace(normalizedEmojiId) ||
            catalog == null ||
            !catalog.Contains(normalizedEmojiId))
        {
            return;
        }

        PaintOwner owner = ResolveGameplayEmojiOwner(sender);
        if (owner == PaintOwner.Neutral)
        {
            return;
        }

        ObserversShowGameplayEmoji(owner, normalizedEmojiId);
    }

    private PaintOwner ResolveGameplayEmojiOwner(NetworkConnection sender)
    {
        if (NetworkRoundController.HasInstance &&
            NetworkRoundController.Instance.TryGetOwner(sender, out PaintOwner registeredOwner))
        {
            return registeredOwner;
        }

        PaintOwner owner = myPaintOwner.Value;
        if (owner != PaintOwner.Neutral)
        {
            return owner;
        }

        PlayerPaintIdentity identity = GetComponent<PlayerPaintIdentity>();
        return identity != null ? identity.Owner : PaintOwner.Neutral;
    }

    [ObserversRpc]
    private void ObserversShowGameplayEmoji(PaintOwner owner, string emojiId)
    {
        GameplayEmojiHud.ShowNetworkEmoji(owner, emojiId);
    }

    /// <summary>
    /// 클라이언트가 owner 변경을 요청할 때 사용하는 RPC입니다.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ServerCmdChangePaintOwner(PaintOwner newOwner)
    {
        myPaintOwner.Value = newOwner;
    }

    /// <summary>
    /// 서버 오브젝트에서 현재 level stats snapshot을 observer에게 전파합니다.
    /// </summary>
    public void ServerBroadcastLevelStats(int currentExp, int claimedTiles, float lastClaimTimestamp)
    {
        if (!IsServerInitialized)
        {
            return;
        }

        ObserversApplyLevelStats(currentExp, claimedTiles, lastClaimTimestamp);
    }

    /// <summary>
    /// observer 클라이언트의 PlayerLevelStats에 서버 snapshot을 적용합니다.
    /// </summary>
    [ObserversRpc(BufferLast = true)]
    private void ObserversApplyLevelStats(int currentExp, int claimedTiles, float lastClaimTimestamp)
    {
        ApplyLevelStatsSnapshot(currentExp, claimedTiles, lastClaimTimestamp);
    }

    /// <summary>
    /// 탈락 상태를 로컬에 적용하고 서버라면 observer에게도 전파합니다.
    /// </summary>
    public void ServerSetEliminated(bool eliminated)
    {
        if (IsServerInitialized)
        {
            ObserversApplyEliminated(eliminated);
        }

        ApplyEliminatedLocal(eliminated);
    }

    /// <summary>
    /// observer 클라이언트에 탈락 상태를 적용합니다.
    /// </summary>
    [ObserversRpc(BufferLast = true)]
    private void ObserversApplyEliminated(bool eliminated)
    {
        ApplyEliminatedLocal(eliminated);
    }

    public void ServerClearStoredItemForRoundEnd()
    {
        if (IsServerInitialized)
        {
            ObserversClearStoredItemForRoundEnd();
        }

        ClearStoredItemForRoundEndLocal();
    }

    [ObserversRpc(ExcludeServer = true)]
    private void ObserversClearStoredItemForRoundEnd()
    {
        ClearStoredItemForRoundEndLocal();
    }

    private void ClearStoredItemForRoundEndLocal()
    {
        PlayerStoredItemController storedItem = GetComponent<PlayerStoredItemController>();
        if (storedItem != null)
        {
            storedItem.ClearForRoundEnd();
        }
    }

    /// <summary>
    /// 서버 권한 아이템 사용 진입점입니다. 서버가 owner를 검증한 뒤 아이템 효과를 생성합니다.
    /// </summary>
    [ServerRpc]
    public void ServerUseStoredItem(
        DropItemEffectType itemType,
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius,
        NetworkConnection sender = null)
    {
        if (NetworkRoundController.HasInstance &&
            !NetworkRoundController.Instance.IsOwnerAllowed(sender, owner))
        {
            return;
        }

        switch (itemType)
        {
            case DropItemEffectType.ReverseControlsBomb:
                ObserversShowControlInversionBombIndicators(
                    position,
                    owner,
                    duration,
                    radius);
                PlayerStoredItemController.ServerApplyControlInversionBomb(
                    position,
                    owner,
                    duration,
                    radius);
                break;
            case DropItemEffectType.TeleportLock:
                if (PlayerStoredItemController.ServerUseTeleportLock(
                    position,
                    owner,
                    duration,
                    out PaintOwner targetOwner))
                {
                    ObserversSpawnTeleportLock(
                        position,
                        owner,
                        targetOwner);
                }
                break;
            case DropItemEffectType.PaintShell:
                Debug.LogWarning("[Item] PaintShell use ignored because the item is disabled.");
                break;
            case DropItemEffectType.DecoyStamp:
                ObserversSpawnDecoyStamp(
                    position,
                    direction,
                    owner,
                    duration,
                    radius,
                    paintRadius);
                break;
            case DropItemEffectType.BombMine:
                if (ServerTryPlaceBombMine(position, owner, duration, paintRadius) &&
                    sender != null)
                {
                    TargetConfirmBombMinePlaced(sender);
                }
                break;
            case DropItemEffectType.Shield:
                PlayerStoredItemController.ApplyShieldToOwnerLocal(owner, duration);
                ObserversApplyShield(owner, duration);
                break;
        }
    }

    public bool ServerTryPlaceBombMineForAi(
        Vector3 position,
        PaintOwner owner,
        float fuseSeconds,
        int paintRadius)
    {
        if (!IsServerInitialized)
        {
            return false;
        }

        return ServerTryPlaceBombMine(position, owner, fuseSeconds, paintRadius);
    }

    private bool ServerTryPlaceBombMine(
        Vector3 position,
        PaintOwner owner,
        float fuseSeconds,
        int paintRadius)
    {
        PaintOwner serverOwner = myPaintOwner.Value;
        if (serverOwner != PaintOwner.Neutral && serverOwner != owner)
        {
            return false;
        }

        PlayerStoredItemController storedItem =
            GetComponent<PlayerStoredItemController>();
        int serverPaintRadius = storedItem != null
            ? storedItem.DefaultBombMineCrossRadius
            : Mathf.Max(1, paintRadius);
        int serverOwnerLevel = GetBombMineOwnerLevel();
        int serverMaxActiveMines = storedItem != null
            ? storedItem.DefaultBombMineMaxCount
            : BombMineSettings.GetDefaultMaxActiveMinesForLevel(serverOwnerLevel);

        if (BombMine.Place(
            position,
            owner,
            fuseSeconds,
            serverPaintRadius,
            serverOwnerLevel,
            serverMaxActiveMines) == null)
        {
            Debug.Log(
                "[Item] Server rejected Bomb Mine placement. owner=" +
                owner +
                " level=" + serverOwnerLevel +
                " active=" + BombMine.GetActiveMineCount(owner) +
                "/" + serverMaxActiveMines +
                " position=" + position + ".");
            return false;
        }

        ObserversPlaceBombMine(
            position,
            owner,
            fuseSeconds,
            serverPaintRadius,
            serverOwnerLevel,
            serverMaxActiveMines);
        return true;
    }

    private int GetBombMineOwnerLevel()
    {
        PlayerLevelStats levelStats = GetComponent<PlayerLevelStats>();
        return levelStats != null ? Mathf.Max(1, levelStats.Level) : 1;
    }

    [ObserversRpc]
    private void ObserversShowControlInversionBombIndicators(
        Vector3 position,
        PaintOwner owner,
        float duration,
        float radius)
    {
        PlayerStoredItemController.ShowControlInversionBombIndicatorsLocal(
            position,
            owner,
            duration,
            radius);
    }

    [ObserversRpc]
    private void ObserversSpawnPaintShell(
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius)
    {
        PaintShellProjectile.Spawn(
            position,
            direction,
            owner,
            duration,
            radius,
            paintRadius);
    }

    [ObserversRpc]
    private void ObserversSpawnDecoyStamp(
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius)
    {
        DecoyStampProjectile.Spawn(
            position,
            direction,
            owner,
            duration,
            radius,
            paintRadius,
            gameObject);
    }

    [ObserversRpc]
    private void ObserversSpawnTeleportLock(
        Vector3 position,
        PaintOwner owner,
        PaintOwner targetOwner)
    {
        TeleportLockProjectile.Spawn(
            position,
            owner,
            targetOwner);
    }

    [ObserversRpc]
    private void ObserversApplyShield(PaintOwner owner, float duration)
    {
        PlayerStoredItemController.ApplyShieldToOwnerLocal(owner, duration);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void ObserversPlaceBombMine(
        Vector3 position,
        PaintOwner owner,
        float fuseSeconds,
        int paintRadius,
        int ownerLevel,
        int maxActiveMines)
    {
        BombMine.Place(
            position,
            owner,
            fuseSeconds,
            paintRadius,
            ownerLevel,
            maxActiveMines);
    }

    [TargetRpc]
    private void TargetConfirmBombMinePlaced(NetworkConnection connection)
    {
        PlayerStoredItemController storedItem =
            GetComponent<PlayerStoredItemController>();
        if (storedItem != null)
        {
            storedItem.ConfirmBombMinePlaced();
        }
    }
}
