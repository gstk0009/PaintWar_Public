using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

/// <summary>
/// ????쒖뒪?쒖쑝濡??ㅼ뼱?ㅻ뒗 湲곗〈 public API瑜??좎??섎뒗 facade?낅땲??
/// ?ㅼ젣 ?곗씠?곕뒗 TileGridState, 醫뚰몴 蹂?섏? TileGridMapper, ?쒖떆??TilePaintView,
/// 湲곕낯 ?먮졊 洹쒖튃? TileClaimRules???꾩엫?⑸땲??
/// </summary>
/// <summary>
/// ????쒖뒪?쒖쓽 湲곗〈 public API瑜??좎??섎뒗 facade?낅땲??
/// ?ㅼ젣 ?곗씠?곕뒗 TileGridState, 醫뚰몴 蹂?섏? TileGridMapper, ?붾㈃ ?쒖떆??TilePaintView, ?먮졊 洹쒖튃? TileClaimService/TileClaimRules媛 ?대떦?⑸땲??
/// </summary>
/// <summary>
/// 타일 grid 데이터, world/grid 변환, owner material 조회, local tile owner 적용을 감싸는 facade입니다.
/// 네트워크 모드에서는 플레이어 점령 요청을 local 적용 전에 NetworkTilePaintAuthority로 보냅니다.
/// </summary>
public class TilePaintManager : MonoBehaviour
{
    public static TilePaintManager Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    [Header("Materials (Index match PaintOwner Enum)")]
    [SerializeField] private Material neutralMaterial;
    [SerializeField] private Material[] playerMaterials;

    [Header("Network")]
    // true이면 클라이언트가 local grid를 직접 바꾸지 않고 서버에 점령 요청을 보냅니다.
    [SerializeField] private bool routeClaimsThroughNetworkAuthority;
    [SerializeField] private bool bootstrapRoundSystem = true;
    [Header("Territory Rules")]
    [SerializeField] private TerritoryRuleSettings territoryRules = new TerritoryRuleSettings();
    [SerializeField, Min(0.02f)] private float territoryMaintenanceInterval = 0.1f;
    [Header("Home Wizard Towers")]
    [SerializeField] private GameObject homeWizardTowerPrefab;
    [SerializeField] private Transform homeWizardTowerRoot;

    // 책임을 나눠 public API는 유지하면서 거대한 tile manager 하나로 커지는 것을 막습니다.
    private readonly TileGridMapper mapper = new TileGridMapper();
    private readonly TileGridState state = new TileGridState();
    private readonly ConnectedTerritoryState territory = new ConnectedTerritoryState();
    private readonly TileClaimRules claimRules = new TileClaimRules();
    private TilePaintView view;
    private PaintOwner[] territoryOwnerSnapshot;
    private bool[] territoryDestroyedSnapshot;
    private bool territoryDirty;
    private float nextTerritoryMaintenanceAt;
    private readonly int[] collapsedTerritoryCounts = new int[5];
    private readonly List<GameObject> homeWizardTowers = new List<GameObject>(4);

    public int GridSize { get { return mapper.GridSize; } }
    public float TileSize { get { return mapper.TileSize; } }
    public Vector3 GridOrigin { get { return mapper.GridOrigin; } }
    public int TotalTileCount { get { return state.TotalTileCount; } }
    public TerritoryRuleSettings TerritoryRules { get { return territoryRules; } }
    public event Action<TerritoryChange> TerritoryChanged
    {
        add { territory.Changed += value; }
        remove { territory.Changed -= value; }
    }
    public int ActiveTileCount
    {
        get
        {
            int unpaintableCount = ObstacleFieldManager.HasInstance
                ? ObstacleFieldManager.Instance.UnbreakableObstacleCount
                : 0;
            return Mathf.Max(0, state.ActiveTileCount - unpaintableCount);
        }
    }
    public bool IsGridReady { get { return state.IsReady && view != null && view.IsReady && mapper.IsReady; } }
    public string DebugGridStatus
    {
        get
        {
            return "gridReady=" + IsGridReady +
                   " stateReady=" + state.IsReady +
                   " viewReady=" + (view != null && view.IsReady) +
                   " mapperReady=" + mapper.IsReady +
                   " gridSize=" + GridSize +
                   " tileSize=" + TileSize +
                   " totalTiles=" + TotalTileCount;
        }
    }
    public bool ShouldRouteClaimsThroughNetworkAuthority
    {
        get
        {
            // ?ㅽ듃?뚰겕 紐⑤뱶?먯꽌???대씪?댁뼵?몄쓽 吏곸젒 ?먮졊??留됯퀬 NetworkTilePaintAuthority濡??붿껌??蹂대깄?덈떎.
            // 네트워크 모드에서는 클라이언트가 NetworkTilePaintAuthority로 점령 요청을 보냅니다.
            return routeClaimsThroughNetworkAuthority
                && NetworkTilePaintAuthority.HasInstance
                && NetworkTilePaintAuthority.Instance.ShouldRouteClientClaims;
        }
    }

    private void Awake()
    {
        //if (Instance != null && Instance != this)
        //{
        //    Destroy(gameObject);
        //    return;
        //}
        Application.targetFrameRate = 60;
        Instance = this;
        EnsureView();
    }

    private void Start()
    {
        if (!bootstrapRoundSystem)
        {
            return;
        }

        // tile manager가 있으면 이 씬에 round system도 준비되어 있는지 확인합니다.
        // ???留ㅻ땲?媛 議댁옱?섎뒗 ?ъ씠硫??쇱슫???쒖뒪?쒕룄 ?먮룞?쇰줈 以鍮꾪빀?덈떎.
        RoundSystemBootstrap.EnsureForActiveScene();
    }

    private void LateUpdate()
    {
        if (!state.IsReady) return;
        if (territoryDirty) RecalculateTerritories();
        view.UpdateIsolationPulse(Time.unscaledTime);
        if (!IsAuthoritative() || !territory.HasIsolatedTiles ||
            Time.unscaledTime < nextTerritoryMaintenanceAt) return;
        nextTerritoryMaintenanceAt = Time.unscaledTime +
            Mathf.Max(0.02f, territoryMaintenanceInterval);
        List<int> expired = territory.CollectExpired(territoryOwnerSnapshot, Time.time, territoryRules.isolationGraceSeconds);
        if (expired.Count == 0) return;
        Array.Clear(collapsedTerritoryCounts, 0, collapsedTerritoryCounts.Length);
        for (int i = 0; i < expired.Count; i++)
        {
            int flat = expired[i];
            PaintOwner owner = territoryOwnerSnapshot[flat];
            Vector2Int index = new Vector2Int(flat / GridSize, flat % GridSize);
            bool changed;
            if (NetworkTilePaintAuthority.HasInstance)
                changed = NetworkTilePaintAuthority.Instance.RequestForceSetOwner(index, PaintOwner.Neutral);
            else
            {
                changed = state.SetOwner(index, PaintOwner.Neutral);
                if (changed) view.ApplyOwner(index, PaintOwner.Neutral);
            }
            if (!changed) continue;
            collapsedTerritoryCounts[(int)owner]++;
        }
        RecalculateTerritories();
        for (int i = 1; i <= 4; i++) territory.PublishCollapsed((PaintOwner)i, collapsedTerritoryCounts[i], territoryRules.isolationGraceSeconds);
    }

    public float GetTileSize()
    {
        return TileSize;
    }

    public Vector2Int GetGridIndex(Vector3 worldPos)
    {
        return mapper.GetGridIndex(worldPos);
    }

    /// <summary>
    /// 媛쒕퀎 ????뚮뜑?щ? ?깅줉?섍린 ?꾩뿉 TileGenerator媛 ?몄텧?⑸땲??
    /// </summary>
    public void PrepareGrid(int size, float tSize, Vector3 origin)
    {
        // TileGenerator가 개별 renderer를 등록하기 전에 호출합니다.
        mapper.Configure(size, tSize, origin);
        state.Prepare(size);
        territory.Prepare(size);
        territoryOwnerSnapshot = new PaintOwner[size * size];
        territoryDestroyedSnapshot = new bool[size * size];
        territoryDirty = true;
        EnsureView();
        view.Prepare(size);
    }

    /// <summary>
    /// ?앹꽦??????뚮뜑?щ? 醫뚰몴??留욊쾶 ?깅줉?⑸땲??
    /// ?댄썑 ?먮졊 ?됱긽 蹂寃쎌? TilePaintView??罹먯떆???뚮뜑?щ? ?듯빐 泥섎━?⑸땲??
    /// </summary>
    public void RegisterTileRenderer(int x, int z, Renderer rend)
    {
        // owner 변경 시 매 프레임 검색 없이 material을 바꿀 수 있도록 renderer를 한 번 cache합니다.
        EnsureView();
        view.RegisterTileRenderer(new Vector2Int(x, z), rend);
    }

    public bool TryClaimByWorldPos(Vector3 worldPosition, PaintOwner attacker)
    {
        if (attacker == PaintOwner.Neutral)
        {
            return false;
        }

        if (ShouldRouteClaimsThroughNetwork())
        {
            return NetworkTilePaintAuthority.Instance.RequestClaimByWorldPos(worldPosition, attacker);
        }

        return TryClaimByWorldPosLocal(worldPosition, attacker);
    }

    public bool TryClaimByWorldPosLocal(Vector3 worldPosition, PaintOwner attacker)
    {
        if (attacker == PaintOwner.Neutral)
        {
            return false;
        }

        return TryClaimNeutralByGridIndexLocal(GetGridIndex(worldPosition), attacker);
    }

    /// <summary>
    /// Movement-only claim path. Walking can claim neutral tiles but can never
    /// modify a tile already owned by any player.
    /// </summary>
    public bool TryClaimNeutralByGridIndexLocal(Vector2Int index, PaintOwner attacker)
    {
        if (attacker == PaintOwner.Neutral || !IsTilePaintable(index))
        {
            return false;
        }

        if (GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(attacker, index))
        {
            return false;
        }

        if (!state.TryGetOwner(index, out PaintOwner beforeOwner) ||
            beforeOwner != PaintOwner.Neutral)
        {
            return false;
        }

        return SetTileOwnerLocal(index, attacker);
    }

    /// <summary>
    /// 湲곕낯 2?④퀎 ???洹쒖튃???곸슜?⑸땲??
    /// 以묐┰ -> 怨듦꺽?? ?????-> 以묐┰, ?????-> 蹂寃??놁쓬 ?쒖꽌?낅땲??
    /// 寃쏀뿕移? ??????쒕젅???깆? TileClaimService?먯꽌 泥섎━?⑸땲??
    /// </summary>
    public bool TryClaimByGridIndex(Vector2Int index, PaintOwner attacker)
    {
        // 이동 polling이 같은 내 타일을 여러 번 찍을 수 있으므로 no-op 요청은 일찍 건너뜁니다.
        if (attacker != PaintOwner.Neutral &&
            state.TryGetOwner(index, out PaintOwner currentOwner) &&
            currentOwner == attacker)
        {
            return false;
        }

        if (ShouldRouteClaimsThroughNetwork())
        {
            return NetworkTilePaintAuthority.Instance.RequestClaimByGridIndex(index, attacker);
        }

        return TryClaimByGridIndexLocal(index, attacker);
    }

    /// <summary>
    /// ?ㅽ듃?뚰겕 RPC瑜?嫄곗튂吏 ?딄퀬 ?꾩옱 濡쒖뺄 ?곹깭??利됱떆 湲곕낯 ?먮졊 洹쒖튃???곸슜?⑸땲??
    /// NetworkTilePaintAuthority???쒕쾭 寃利앷낵 TileClaimService ?대? 泥섎━?먯꽌 ?ъ슜?⑸땲??
    /// </summary>
    public bool TryClaimByGridIndexLocal(Vector2Int index, PaintOwner attacker)
    {
        // 서버, 오프라인 테스트, 검증된 네트워크 delta가 사용하는 local 적용 경로입니다.
        if (attacker == PaintOwner.Neutral || !IsTilePaintable(index))
        {
            return false;
        }

        if (GameRoundManager.HasInstance && !GameRoundManager.Instance.CanOwnerClaimTile(attacker, index))
        {
            return false;
        }

        if (!state.TryGetOwner(index, out PaintOwner beforeOwner))
        {
            return false;
        }

        TileClaimResult result = claimRules.Evaluate(beforeOwner, attacker);
        if (!result.Changed)
        {
            return false;
        }

        return SetTileOwnerLocal(index, result.AfterOwner);
    }

    public bool TryGetTileOwner(Vector2Int index, out PaintOwner owner)
    {
        return state.TryGetOwner(index, out owner);
    }

    public bool IsInsideGrid(Vector2Int index)
    {
        return state.IsValidIndex(index);
    }

    public bool IsTileDestroyed(Vector2Int index)
    {
        return state.IsDestroyed(index);
    }

    public bool IsTileWalkable(Vector2Int index)
    {
        return state.IsValidIndex(index) &&
               !state.IsDestroyed(index) &&
               (!ObstacleFieldManager.HasInstance ||
                !ObstacleFieldManager.Instance.IsTileMovementBlocked(index));
    }

    public bool IsTilePaintable(Vector2Int index)
    {
        return state.IsValidIndex(index) &&
               !state.IsDestroyed(index) &&
               (!ObstacleFieldManager.HasInstance ||
                !ObstacleFieldManager.Instance.IsTileUnbreakable(index));
    }

    public Vector3 GetWorldPosition(Vector2Int index)
    {
        return mapper.GetWorldPosition(index);
    }

    public Vector3 GetTileCenterWorldPosition(Vector2Int index)
    {
        return mapper.GetTileCenterWorldPosition(index);
    }

    public float GetTileSurfaceY(Vector2Int index)
    {
        EnsureView();
        if (view.TryGetTileSurfaceY(index, out float surfaceY))
        {
            return surfaceY;
        }

        return mapper.GetTileCenterWorldPosition(index).y + TileSize * 0.5f;
    }

    public Vector2Int ClampIndex(Vector2Int index)
    {
        return mapper.ClampIndex(index);
    }

    public int CountOwnedTiles(PaintOwner owner)
    {
        return state.CountOwnedTiles(owner);
    }

    public int CountConnectedTerritory(PaintOwner owner) => territory.GetConnectedCount(owner);
    public int CountIsolatedTerritory(PaintOwner owner) => territory.GetIsolatedCount(owner);

    public bool IsTileConnectedTerritory(Vector2Int index, PaintOwner owner)
    {
        return owner != PaintOwner.Neutral && territory.IsConnected(index) &&
               state.TryGetOwner(index, out PaintOwner actual) && actual == owner;
    }

    public bool IsTileIsolated(Vector2Int index) => territory.IsIsolated(index);
    public TerritoryTileState GetTerritoryTileState(Vector2Int index)
    {
        return state.TryGetOwner(index, out PaintOwner owner)
            ? territory.GetTileState(index, owner)
            : TerritoryTileState.Neutral;
    }
    public bool IsHomeProtected(Vector2Int index) => territory.GetProtectedOwner(index) != PaintOwner.Neutral;

    public void ConfigureHomeTerritories(IReadOnlyList<GameParticipant> participants)
    {
        territory.ClearHomes();
        if (participants == null) return;
        int towerIndex = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.Zone == null || participant.Owner == PaintOwner.Neutral) continue;
            Vector2Int center = participant.Zone.GetSpawnIndex(GridSize);
            territory.SetHome(participant.Owner, center);
            ConfigureHomeWizardTower(towerIndex++, participant.Owner, center);
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
            {
                Vector2Int p = ClampIndex(center + new Vector2Int(dx, dy));
                state.SetOwner(p, participant.Owner);
                if (view != null) view.ApplyOwner(p, participant.Owner);
            }
        }
        for (int i = towerIndex; i < homeWizardTowers.Count; i++)
            if (homeWizardTowers[i] != null) homeWizardTowers[i].SetActive(false);
        territoryDirty = true;
        RecalculateTerritories();
    }

    private void ConfigureHomeWizardTower(int index, PaintOwner owner, Vector2Int center)
    {
        if (homeWizardTowerPrefab == null) return;
        while (homeWizardTowers.Count <= index)
        {
            GameObject tower = Instantiate(homeWizardTowerPrefab, homeWizardTowerRoot);
            tower.name = "HomeWizardTower_" + (homeWizardTowers.Count + 1);
            homeWizardTowers.Add(tower);
        }
        GameObject instance = homeWizardTowers[index];
        instance.SetActive(true);
        Vector3 position = GetTileCenterWorldPosition(center);
        position.y = GetTileSurfaceY(center);
        instance.transform.SetPositionAndRotation(position, Quaternion.identity);
        int materialIndex = (int)owner - 1;
        if (materialIndex < 0 || playerMaterials == null || materialIndex >= playerMaterials.Length) return;
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = playerMaterials[materialIndex];
    }

    public float GetTileSpeedMultiplier(Vector2Int index, PaintOwner mover)
    {
        if (mover == PaintOwner.Neutral || !state.TryGetOwner(index, out PaintOwner owner) || owner == PaintOwner.Neutral) return 1f;
        if (owner == mover) return IsTileConnectedTerritory(index, mover)
            ? territoryRules.connectedTileSpeedMultiplier : 1f;
        return territoryRules.enemyTileSpeedMultiplier;
    }

    public int[] GetOwnerCounts()
    {
        return state.GetOwnerCounts();
    }

    public PaintOwner[] GetFlatGridSnapshot()
    {
        return state.CreateSnapshot();
    }

    public bool[] GetFlatDestroyedSnapshot()
    {
        return state.CreateDestroyedSnapshot();
    }

    public void ApplyTileSnapshotLocal(PaintOwner[] snapshot, bool[] destroyedSnapshot = null)
    {
        // late join 클라이언트는 전체 grid를 한 번 받은 뒤 delta로 이어갑니다.
        state.ApplySnapshot(snapshot, destroyedSnapshot);
        EnsureView();
        view.ApplySnapshot(snapshot, destroyedSnapshot);
        territoryDirty = true;
    }

    public void ResetAllTiles()
    {
        state.ResetAll(PaintOwner.Neutral);
        EnsureView();
        view.ResetAll(PaintOwner.Neutral);
        territoryDirty = true;
    }

    public Material GetOwnerMaterial(PaintOwner owner)
    {
        EnsureView();
        return view.GetOwnerMaterial(owner);
    }

    /// <summary>
    /// ?쒕쾭 RPC 寃곌낵??濡쒖뺄 洹쒖튃 寃곌낵泥섎읆 ?대? 寃利앸맂 ???owner 蹂寃쎌쓣 濡쒖뺄 ?곹깭? ?쒖떆 ?묒そ??諛섏쁺?⑸땲??
    /// </summary>
    public bool SetTileOwnerLocal(Vector2Int index, PaintOwner newOwner)
    {
        // HUD/카운팅 코드와 시각 표시가 어긋나지 않도록 state와 view를 함께 갱신합니다.
        if (!IsTilePaintable(index))
        {
            return false;
        }

        PaintOwner protectedOwner = territory.GetProtectedOwner(index);
        if (protectedOwner != PaintOwner.Neutral && newOwner != protectedOwner) return false;
        bool changed = state.SetOwner(index, newOwner);
        if (!changed)
        {
            return false;
        }

        EnsureView();
        view.ApplyOwner(index, newOwner);
        territoryDirty = true;
        return true;
    }

    public bool ForceSetTileOwnerLocal(Vector2Int index, PaintOwner newOwner)
    {
        if (!IsTilePaintable(index))
        {
            return false;
        }

        PaintOwner protectedOwner = territory.GetProtectedOwner(index);
        if (protectedOwner != PaintOwner.Neutral && newOwner != protectedOwner) return false;
        bool changed = state.SetOwner(index, newOwner);
        EnsureView();
        view.ApplyOwner(index, newOwner);
        if (changed) territoryDirty = true;
        return changed;
    }

    public bool ForceSetTileOwnerIgnoringProtectionLocal(Vector2Int index, PaintOwner newOwner)
    {
        if (!state.IsValidIndex(index) || state.IsDestroyed(index))
        {
            return false;
        }

        bool changed = state.SetOwner(index, newOwner);
        EnsureView();
        view.ApplyOwner(index, newOwner);
        if (changed) territoryDirty = true;
        return changed;
    }

    private void RecalculateTerritories()
    {
        if (territoryOwnerSnapshot == null || territoryOwnerSnapshot.Length != state.TotalTileCount) return;
        state.FillSnapshot(territoryOwnerSnapshot, territoryDestroyedSnapshot);
        territory.Recalculate(territoryOwnerSnapshot, territoryDestroyedSnapshot, Time.time);
        view.RebuildIsolationVisuals(territory);
        territoryDirty = false;
    }

    private static bool IsAuthoritative()
    {
        FishNet.Managing.NetworkManager manager = InstanceFinder.NetworkManager;
        return manager == null || (!manager.IsClientStarted && !manager.IsServerStarted) || manager.IsServerStarted;
    }

    public int DestroyTilesInRadiusLocal(Vector3 worldPosition, float radius)
    {
        if (!IsGridReady || radius <= 0f)
        {
            return 0;
        }

        int destroyedCount = 0;
        float radiusSqr = radius * radius;
        int tileRadius = Mathf.CeilToInt(radius / Mathf.Max(0.001f, TileSize));
        Vector2Int centerIndex = GetGridIndex(worldPosition);

        for (int x = centerIndex.x - tileRadius; x <= centerIndex.x + tileRadius; x++)
        {
            for (int y = centerIndex.y - tileRadius; y <= centerIndex.y + tileRadius; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (!state.IsValidIndex(index) || state.IsDestroyed(index))
                {
                    continue;
                }

                Vector3 tileCenter = GetTileCenterWorldPosition(index);
                Vector2 offset = new Vector2(tileCenter.x - worldPosition.x, tileCenter.z - worldPosition.z);
                if (offset.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                if (SetTileDestroyedLocal(index, true))
                {
                    destroyedCount++;
                }
            }
        }

        return destroyedCount;
    }

    public bool SetTileDestroyedLocal(Vector2Int index, bool destroyed)
    {
        if (destroyed && IsHomeProtected(index)) return false;
        if (!state.SetDestroyed(index, destroyed))
        {
            return false;
        }

        EnsureView();
        view.SetDestroyed(index, destroyed);
        territoryDirty = true;
        return true;
    }

    private void EnsureView()
    {
        if (view == null)
        {
            view = new TilePaintView(neutralMaterial, playerMaterials);
        }
    }

    private bool ShouldRouteClaimsThroughNetwork()
    {
        return ShouldRouteClaimsThroughNetworkAuthority;
    }
}
