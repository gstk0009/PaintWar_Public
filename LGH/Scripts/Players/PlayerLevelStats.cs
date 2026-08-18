using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 플레이어 성장 상태를 저장하고 level 기반 gameplay 보너스를 제공합니다.
/// 밸런스 값은 PlayerLevelStatsConfig에서 가져와 코드 수정 없이 조정할 수 있습니다.
/// </summary>
public class PlayerLevelStats : MonoBehaviour
{
    // 전역 owner lookup 덕분에 타일/아이템 시스템이 씬 검색 없이 EXP를 지급할 수 있습니다.
    private static readonly Dictionary<PaintOwner, PlayerLevelStats> StatsByOwner = new Dictionary<PaintOwner, PlayerLevelStats>();
    // late join snapshot 전송 때 임시 목록을 재사용해서 GC 할당을 피합니다.
    private static readonly List<PlayerLevelStats> BroadcastSnapshotBuffer = new List<PlayerLevelStats>(4);

    [SerializeField] private PlayerLevelStatsConfig statsConfig;
    [SerializeField] private PaintOwner fallbackOwner = PaintOwner.Player1;
    // level은 runtime에 currentExp에서 계산되며, serialized 값은 에디터/default 시작값일 뿐입니다.
    [SerializeField] private int level = 1;
    [SerializeField] private int currentExp;
    // 라운드별 점수 데이터입니다. EXP는 라운드 사이에도 유지되고 claimedTiles는 매 라운드 초기화됩니다.
    [SerializeField] private int claimedTiles;
    [SerializeField] private float lastClaimTimestamp;
    [SerializeField] private bool enableLevelBoostTestKey = true;
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key levelBoostTestKey = Key.A;
#else
    [SerializeField] private KeyCode levelBoostTestKey = KeyCode.A;
#endif

    private PlayerPaintIdentity identity;
    private Player player;
    private PlayerMovement movement;
    private PaintOwner registeredOwner = PaintOwner.Neutral;

    public event Action<int, float> LevelMoveSpeedChanged;

    public int Level { get { return level; } }
    public int CurrentExp { get { return currentExp; } }
    public int ClaimedTiles { get { return claimedTiles; } }
    public float LastClaimTimestamp { get { return lastClaimTimestamp; } }
    public PaintOwner Owner { get { return identity != null ? identity.Owner : fallbackOwner; } }
    public PlayerLevelStatsConfig StatsConfig { get { return GetStatsConfig(); } }
    public int NextLevelRequiredExp { get { return StatsConfig.GetNextRequiredExp(level); } }
    public bool IsMaxLevel { get { return level >= StatsConfig.MaxLevel; } }
    public float CooldownMultiplier { get { return StatsConfig.GetCooldownMultiplier(level); } }
    public float MoveSpeed { get { return StatsConfig.GetMoveSpeed(level); } }

    public static int MaxLevel { get { return PlayerLevelStatsConfig.GetOrLoadDefault().MaxLevel; } }

    private void Awake()
    {
        identity = GetComponent<PlayerPaintIdentity>();
        player = GetComponent<Player>();
        movement = GetComponent<PlayerMovement>();
        level = Mathf.Clamp(level, 1, StatsConfig.MaxLevel);
        RefreshLevelFromExp();
    }

    private void OnEnable()
    {
        Register();
    }

    private void OnDisable()
    {
        if (StatsByOwner.TryGetValue(registeredOwner, out PlayerLevelStats stats) && stats == this)
        {
            StatsByOwner.Remove(registeredOwner);
        }
    }

    private void Update()
    {
        // Debug shortcut disabled: pressing A must not grant a level during gameplay.
        // HandleLevelBoostTestKey();
    }

    public void AddExp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        currentExp += amount;
        RefreshLevelFromExp();
        BroadcastNetworkSnapshot();
    }

    public void AddClaimedTile()
    {
        claimedTiles++;
        lastClaimTimestamp = Time.time;
        BroadcastNetworkSnapshot();
    }

    public void AddTileClaimReward(int experienceAmount)
    {
        // 주요 타일 보상 경로입니다. 한 번의 호출로 라운드 점수와 누적 EXP를 함께 갱신합니다.
        if (experienceAmount <= 0)
        {
            return;
        }

        currentExp += experienceAmount;
        claimedTiles++;
        lastClaimTimestamp = Time.time;
        RefreshLevelFromExp();
        BroadcastNetworkSnapshot();
    }

    public void ResetRoundClaimStats()
    {
        claimedTiles = 0;
        lastClaimTimestamp = 0f;
        BroadcastNetworkSnapshot();
    }

    public void ApplyLevelBoost()
    {
        if (level >= StatsConfig.MaxLevel)
        {
            return;
        }

        level++;
        currentExp = Mathf.Max(currentExp, StatsConfig.GetRequiredExpForLevel(level));
        NotifyLevelMoveSpeedChanged();
        BroadcastNetworkSnapshot();
    }

    private void HandleLevelBoostTestKey()
    {
        if (!enableLevelBoostTestKey || !IsLocalControlledPlayer() || !WasLevelBoostTestKeyPressed())
        {
            return;
        }

        RequestLevelBoostForTest();
    }

    private bool IsLocalControlledPlayer()
    {
        if (movement == null)
        {
            movement = GetComponent<PlayerMovement>();
        }

        if (movement != null)
        {
            return movement.IsOwner;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        return player != null && (player.IsOwner || (!player.IsClientInitialized && !player.IsServerInitialized));
    }

    private void RequestLevelBoostForTest()
    {
        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (player != null && player.IsClientInitialized && !player.IsServerInitialized)
        {
            player.ServerRequestLevelBoostForTest();
            return;
        }

        ApplyLevelBoost();
    }

    private bool WasLevelBoostTestKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[levelBoostTestKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(levelBoostTestKey);
#endif
    }

    public void RefreshOwnerRegistration()
    {
        Register();
    }

    public void ApplyNetworkSnapshot(int syncedCurrentExp, int syncedClaimedTiles, float syncedLastClaimTimestamp)
    {
        // late join/재접속 클라이언트는 Player observers RPC를 통해 서버에서 stat snapshot을 받습니다.
        currentExp = Mathf.Max(currentExp, syncedCurrentExp);
        claimedTiles = Mathf.Max(0, syncedClaimedTiles);
        lastClaimTimestamp = syncedLastClaimTimestamp;
        RefreshLevelFromExp();
    }

    public static bool TryGetStats(PaintOwner owner, out PlayerLevelStats stats)
    {
        return StatsByOwner.TryGetValue(owner, out stats) && stats != null;
    }

    public static void AddExp(PaintOwner owner, int amount)
    {
        if (TryGetStats(owner, out PlayerLevelStats stats))
        {
            stats.AddExp(amount);
        }
        else
        {
            Debug.LogWarning("[Stats] EXP target not registered for " + owner + ".");
        }
    }

    public static void AddClaimedTile(PaintOwner owner)
    {
        if (TryGetStats(owner, out PlayerLevelStats stats))
        {
            stats.AddClaimedTile();
        }
        else
        {
            Debug.LogWarning("[Stats] Claimed tile target not registered for " + owner + ".");
        }
    }

    public static void AddTileClaimReward(PaintOwner owner, int experienceAmount)
    {
        if (TryGetStats(owner, out PlayerLevelStats stats))
        {
            stats.AddTileClaimReward(experienceAmount);
        }
        else
        {
            Debug.LogWarning("[Stats] Tile claim reward target not registered for " + owner + ".");
        }
    }

    public static void BroadcastAllNetworkSnapshots()
    {
        // 늦게 들어온 클라이언트가 현재 match snapshot을 받을 때 사용합니다.
        BroadcastSnapshotBuffer.Clear();
        foreach (PlayerLevelStats stats in StatsByOwner.Values)
        {
            if (stats != null)
            {
                BroadcastSnapshotBuffer.Add(stats);
            }
        }

        for (int i = 0; i < BroadcastSnapshotBuffer.Count; i++)
        {
            PlayerLevelStats stats = BroadcastSnapshotBuffer[i];
            if (stats != null)
            {
                stats.BroadcastNetworkSnapshot();
            }
        }

        BroadcastSnapshotBuffer.Clear();
    }

    private void RefreshLevelFromExp()
    {
        int previousLevel = level;
        level = StatsConfig.GetLevelForExp(currentExp);
        if (level != previousLevel)
        {
            NotifyLevelMoveSpeedChanged();
        }
    }

    private void NotifyLevelMoveSpeedChanged()
    {
        LevelMoveSpeedChanged?.Invoke(level, MoveSpeed);
    }

    private PlayerLevelStatsConfig GetStatsConfig()
    {
        if (statsConfig == null)
        {
            statsConfig = PlayerLevelStatsConfig.GetOrLoadDefault();
        }

        return statsConfig;
    }

    private void Register()
    {
        // spawn 뒤 owner가 바뀔 수 있으므로 현재 owner를 등록하기 전에 이전 owner mapping을 제거합니다.
        if (registeredOwner != PaintOwner.Neutral
            && StatsByOwner.TryGetValue(registeredOwner, out PlayerLevelStats stats)
            && stats == this)
        {
            StatsByOwner.Remove(registeredOwner);
        }

        registeredOwner = Owner;
        if (registeredOwner != PaintOwner.Neutral)
        {
            StatsByOwner[registeredOwner] = this;
        }
    }

    private void BroadcastNetworkSnapshot()
    {
        // 서버 오브젝트는 Player를 통해 stat 변경을 전파하고, 로컬/오프라인 호출은 local 상태만 갱신합니다.
        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (player != null)
        {
            player.ServerBroadcastLevelStats(currentExp, claimedTiles, lastClaimTimestamp);
        }
    }
}
