using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct ObstacleSnapshotRequestBroadcast : IBroadcast
{
    public byte Requested;
}

public struct ObstacleSnapshotBroadcast : IBroadcast
{
    public int LayoutSeed;
    public int[] DestroyedFlatIndices;
    public byte FinalCountdownActive;
}

public struct ObstacleDestroyedBatchBroadcast : IBroadcast
{
    public int[] FlatIndices;
    public byte SpawnContainedItems;
    public byte FinalCountdownActive;
}

// Builds a deterministic obstacle field from the server match seed.
// Only the server may mutate the field; clients receive destruction batches/snapshots.
[DisallowMultipleComponent]
public sealed class ObstacleFieldManager : MonoBehaviour
{
    public static ObstacleFieldManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    private const int ItemSeedSalt = 0x4954454D;

    [Header("Authored References")]
    [SerializeField] private TilePaintManager tileManager;
    [SerializeField] private GameObject obstaclePrefab;
    [SerializeField] private GameObject unbreakableObstaclePrefab;
    [SerializeField] private GameObject finalBlackHolePrefab;
    [SerializeField] private Transform obstacleRoot;

    [Header("Distribution")]
    [SerializeField] private MapGenerationConfig mapGeneration = new MapGenerationConfig();

    [Header("Placement")]
    [SerializeField, Min(0f)] private float surfaceOffset = 0f;
    [SerializeField, Range(0.9f, 1.05f)] private float visualFootprintFill = 1f;
    [SerializeField, Min(0.02f)] private float networkMaintenanceInterval = 0.1f;
    [SerializeField, Min(0.1f)] private float snapshotRetrySeconds = 0.5f;

    [Header("Final Countdown Black Hole")]
    [SerializeField, Min(0.1f)] private float finalCollapseDuration = 1.1f;
    [SerializeField, Min(0f)] private float finalCollapseStagger = 0.38f;
    [SerializeField, Min(0f)] private float finalItemRevealDelay = 0.18f;
    [SerializeField, Min(0f)] private float finalBlackHoleLeadIn = 0.18f;
    [SerializeField, Min(0f)] private float finalBlackHoleHeight = 7f;
    [SerializeField, Min(0.1f)] private float finalBlackHoleScale = 2.2f;
    [SerializeField, Range(0f, 1080f)] private float finalSuctionTurns = 540f;
    [SerializeField, Range(0f, 360f)] private float finalCollapseRotation = 180f;

    private readonly List<GameObject> obstaclePool = new List<GameObject>();
    private readonly List<GameObject> unbreakableObstaclePool = new List<GameObject>();
    private int nextObstaclePoolIndex;
    private int nextUnbreakablePoolIndex;
    private readonly List<int> activeFlatIndices = new List<int>();
    private readonly List<int> unbreakableFlatIndices = new List<int>();
    private readonly List<int> destroyedFlatIndices = new List<int>();
    private readonly List<int> destructionBuffer = new List<int>(32);
    private readonly List<PendingDestruction> pendingDestructions =
        new List<PendingDestruction>();
    private readonly List<FinalCollapseEntry> finalCollapseEntries =
        new List<FinalCollapseEntry>();
    private readonly List<FinalItemCollapseEntry> finalItemCollapseEntries =
        new List<FinalItemCollapseEntry>();
    private readonly List<GameObject> activeItemBuffer = new List<GameObject>(16);
    private readonly HashSet<int> finalRevealedItemFlatIndices = new HashSet<int>();

    private ObstacleRecord[] recordsByFlatIndex;
    private bool[] movementBlockedByFlatIndex;
    private bool[] unbreakableByFlatIndex;
    private float[] obstacleSurfaceYByFlatIndex;
    private NetworkManager networkManager;
    private int synchronizedSeed;
    private int builtSeed;
    private int builtGridSize;
    private bool broadcastsRegistered;
    private bool snapshotReceived;
    private bool finalCountdownActive;
    private float nextMaintenanceAt;
    private float nextSnapshotRequestAt;
    private Coroutine finalCollapseRoutine;
    private GameObject finalBlackHoleInstance;
    private Vector3 finalBlackHoleTarget;

    public float ObstacleCoveragePercent => mapGeneration != null && mapGeneration.gridSize > 0
        ? mapGeneration.breakableCount * 100f / (mapGeneration.gridSize * mapGeneration.gridSize) : 0f;
    public float UnbreakableCoveragePercent => mapGeneration != null && mapGeneration.gridSize > 0
        ? mapGeneration.unbreakableCount * 100f / (mapGeneration.gridSize * mapGeneration.gridSize) : 0f;
    public float ContainedItemPercent => mapGeneration != null && mapGeneration.breakableCount > 0
        ? mapGeneration.containedItemCount * 100f / mapGeneration.breakableCount : 0f;
    public int ActiveObstacleCount => activeFlatIndices.Count - destroyedFlatIndices.Count;
    public int TotalObstacleCount => activeFlatIndices.Count;
    public int UnbreakableObstacleCount => unbreakableFlatIndices.Count;
    public int ContainedItemCount { get; private set; }
    public MapGenerationReport LastGenerationReport { get; private set; }
    public bool IsBuilt => recordsByFlatIndex != null && builtGridSize > 0;

    public bool IsTileMovementBlocked(Vector2Int index)
    {
        if (!IsBuilt ||
            movementBlockedByFlatIndex == null ||
            index.x < 0 || index.y < 0 ||
            index.x >= builtGridSize || index.y >= builtGridSize)
        {
            return false;
        }

        return movementBlockedByFlatIndex[index.x * builtGridSize + index.y];
    }

    public bool IsTileUnbreakable(Vector2Int index)
    {
        if (!IsBuilt ||
            unbreakableByFlatIndex == null ||
            index.x < 0 || index.y < 0 ||
            index.x >= builtGridSize || index.y >= builtGridSize)
        {
            return false;
        }

        return unbreakableByFlatIndex[index.x * builtGridSize + index.y];
    }

    public bool TryGetObstacleSurfaceY(Vector2Int index, out float surfaceY)
    {
        surfaceY = 0f;
        if (!IsTileMovementBlocked(index) || obstacleSurfaceYByFlatIndex == null)
        {
            return false;
        }

        int flatIndex = index.x * builtGridSize + index.y;
        surfaceY = obstacleSurfaceYByFlatIndex[flatIndex];
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CacheReferences();
    }

    private void Update()
    {
        if (synchronizedSeed != 0 && !IsBuilt)
        {
            TryBuildField(synchronizedSeed);
        }

        if (Time.unscaledTime < nextMaintenanceAt)
        {
            return;
        }

        nextMaintenanceAt = Time.unscaledTime + networkMaintenanceInterval;
        CacheNetworkManager();
        RegisterBroadcasts();
        RequestSnapshotIfNeeded();

        if (broadcastsRegistered &&
            IsBuilt &&
            finalCollapseRoutine == null &&
            (networkManager == null ||
             networkManager.IsServerStarted ||
             snapshotReceived))
        {
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        UnregisterBroadcasts();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void SetSynchronizedSessionSeed(int seed)
    {
        if (seed == 0)
        {
            return;
        }

        if (Instance != null)
        {
            Instance.PrepareForMatch(seed);
        }
    }

    public void PrepareForMatch(int seed)
    {
        if (seed == 0)
        {
            return;
        }

        bool seedChanged = synchronizedSeed != seed;
        synchronizedSeed = seed;
        enabled = true;
        if (seedChanged)
        {
            finalCountdownActive = false;
            snapshotReceived = false;
            pendingDestructions.Clear();
        }

        TryBuildField(seed);
    }

    public void ResetForNewMatch(int seed)
    {
        if (seed == 0)
        {
            return;
        }

        synchronizedSeed = seed;
        enabled = true;
        builtSeed = 0;
        finalCountdownActive = false;
        snapshotReceived = false;
        pendingDestructions.Clear();
        TryBuildField(seed);
    }

    public bool ServerDestroyObstacle(DestructibleObstacle obstacle)
    {
        if (obstacle == null || !CanMutateAuthoritatively())
        {
            return false;
        }

        int flatIndex = ToFlatIndex(obstacle.TileIndex);
        if (!TryDestroyLocal(flatIndex, true))
        {
            return false;
        }

        BroadcastDestroyedBatch(new[] { flatIndex }, true, false);
        return true;
    }

    // Server-side bomb integration point. Call once when an authoritative explosion resolves.
    public int ServerDestroyInRadius(Vector3 worldPosition, float radius)
    {
        if (!CanMutateAuthoritatively() ||
            !IsBuilt ||
            tileManager == null ||
            radius <= 0f ||
            finalCountdownActive)
        {
            return 0;
        }

        int tileRadius = Mathf.CeilToInt(radius / Mathf.Max(0.001f, tileManager.TileSize));
        float radiusSqr = radius * radius;
        Vector2Int center = tileManager.GetGridIndex(worldPosition);
        destructionBuffer.Clear();

        for (int x = center.x - tileRadius; x <= center.x + tileRadius; x++)
        {
            for (int y = center.y - tileRadius; y <= center.y + tileRadius; y++)
            {
                if (x < 0 || y < 0 || x >= builtGridSize || y >= builtGridSize)
                {
                    continue;
                }

                Vector2Int index = new Vector2Int(x, y);
                Vector3 obstaclePosition = tileManager.GetTileCenterWorldPosition(index);
                Vector2 offset = new Vector2(
                    obstaclePosition.x - worldPosition.x,
                    obstaclePosition.z - worldPosition.z);
                if (offset.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                int flatIndex = x * builtGridSize + y;
                if (TryDestroyLocal(flatIndex, true))
                {
                    destructionBuffer.Add(flatIndex);
                }
            }
        }

        if (destructionBuffer.Count > 0)
        {
            BroadcastDestroyedBatch(destructionBuffer.ToArray(), true, false);
        }

        return destructionBuffer.Count;
    }

    // Server-side skill integration point. Destroys breakable obstacles on the
    // exact tiles resolved by the authoritative skill pattern and broadcasts once.
    public int ServerDestroyAtGridIndices(IEnumerable<Vector2Int> gridIndices)
    {
        if (!CanMutateAuthoritatively() ||
            !IsBuilt ||
            gridIndices == null ||
            finalCountdownActive)
        {
            return 0;
        }

        destructionBuffer.Clear();
        foreach (Vector2Int index in gridIndices)
        {
            if (index.x < 0 || index.y < 0 ||
                index.x >= builtGridSize || index.y >= builtGridSize)
            {
                continue;
            }

            int flatIndex = ToFlatIndex(index);
            if (IsFlatIndexUnbreakable(flatIndex))
            {
                continue;
            }

            if (TryDestroyLocal(flatIndex, true))
            {
                destructionBuffer.Add(flatIndex);
            }
        }

        if (destructionBuffer.Count > 0)
        {
            BroadcastDestroyedBatch(destructionBuffer.ToArray(), true, false);
        }

        return destructionBuffer.Count;
    }

    public int ServerDestroyAtGridIndicesIncludingUnbreakable(IEnumerable<Vector2Int> gridIndices)
    {
        if (!CanMutateAuthoritatively() ||
            !IsBuilt ||
            gridIndices == null ||
            finalCountdownActive)
        {
            return 0;
        }

        destructionBuffer.Clear();
        foreach (Vector2Int index in gridIndices)
        {
            if (index.x < 0 || index.y < 0 ||
                index.x >= builtGridSize || index.y >= builtGridSize)
            {
                continue;
            }

            int flatIndex = ToFlatIndex(index);
            if (TryDestroyLocal(flatIndex, !IsFlatIndexUnbreakable(flatIndex)))
            {
                destructionBuffer.Add(flatIndex);
            }
        }

        if (destructionBuffer.Count > 0)
        {
            BroadcastDestroyedBatch(destructionBuffer.ToArray(), true, false);
        }

        return destructionBuffer.Count;
    }

    public bool ServerDestroyAtGridIndex(Vector2Int index)
    {
        if (!CanMutateAuthoritatively() ||
            !IsBuilt ||
            finalCountdownActive ||
            index.x < 0 || index.y < 0 ||
            index.x >= builtGridSize || index.y >= builtGridSize)
        {
            return false;
        }

        int flatIndex = ToFlatIndex(index);
        if (!TryDestroyLocal(flatIndex, true))
        {
            return false;
        }

        BroadcastDestroyedBatch(new[] { flatIndex }, true, false);
        return true;
    }

    public void ServerDestroyAllForFinalCountdown()
    {
        if (!CanMutateAuthoritatively() || finalCountdownActive)
        {
            return;
        }

        finalCountdownActive = true;
        finalRevealedItemFlatIndices.Clear();
        List<int> destroyed = new List<int>(ActiveObstacleCount);
        for (int i = 0; i < activeFlatIndices.Count; i++)
        {
            int flatIndex = activeFlatIndices[i];
            if (TryDestroyLocal(flatIndex, true, true))
            {
                destroyed.Add(flatIndex);
            }
        }

        StartFinalCollapseEffect();
        BroadcastDestroyedBatch(destroyed.ToArray(), true, true);
    }

    public void ApplyFinalCountdownState()
    {
        finalCountdownActive = true;
        if (!IsBuilt)
        {
            return;
        }

        finalRevealedItemFlatIndices.Clear();
        for (int i = 0; i < activeFlatIndices.Count; i++)
        {
            TryDestroyLocal(activeFlatIndices[i], true, true);
        }

        StartFinalCollapseEffect();
    }

    private bool TryBuildField(int seed)
    {
        CacheReferences();
        bool dataOnlyServer = Application.isBatchMode;
        if (tileManager == null ||
            !tileManager.IsGridReady ||
            (!dataOnlyServer && obstaclePrefab == null) ||
            (!dataOnlyServer && unbreakableObstaclePrefab == null) ||
            seed == 0)
        {
            return false;
        }

        int gridSize = tileManager.GridSize;
        if (IsBuilt && builtSeed == seed && builtGridSize == gridSize)
        {
            return true;
        }

        ReturnAllObstaclesToPool();
        builtSeed = seed;
        builtGridSize = gridSize;
        recordsByFlatIndex = new ObstacleRecord[gridSize * gridSize];
        movementBlockedByFlatIndex = new bool[gridSize * gridSize];
        unbreakableByFlatIndex = new bool[gridSize * gridSize];
        obstacleSurfaceYByFlatIndex = new float[gridSize * gridSize];
        activeFlatIndices.Clear();
        unbreakableFlatIndices.Clear();
        destroyedFlatIndices.Clear();
        mapGeneration = mapGeneration ?? MapGenerationConfig.CreateDefault();
        mapGeneration.Sanitize(gridSize);
        SymmetricMapGenerator generator = new SymmetricMapGenerator();
        MapGenerationLayout generated = generator.Generate(seed, mapGeneration);
        LastGenerationReport = generated.Report;
        if (!LastGenerationReport.IsValid)
        {
            Debug.LogError("[MapGeneration] No valid layout was produced. " +
                           LastGenerationReport + " failures=" +
                           string.Join(" | ", LastGenerationReport.FailureReasons));
            recordsByFlatIndex = null;
            movementBlockedByFlatIndex = null;
            unbreakableByFlatIndex = null;
            obstacleSurfaceYByFlatIndex = null;
            builtSeed = 0;
            builtGridSize = 0;
            return false;
        }
        if (LastGenerationReport.Attempts > 1 || LastGenerationReport.UsedFallback)
        {
            Debug.LogWarning("[MapGeneration] Regenerated layout. " + LastGenerationReport +
                             " priorFailures=" +
                             string.Join(" || ", LastGenerationReport.AttemptFailureReasons.ToArray()));
        }

        List<int> selected = new List<int>(mapGeneration.breakableCount);
        List<int> unbreakableSelection = new List<int>(mapGeneration.unbreakableCount);
        HashSet<int> itemIndices = new HashSet<int>();
        for (int flatIndex = 0; flatIndex < generated.Cells.Length; flatIndex++)
        {
            if (generated.Cells[flatIndex] == GeneratedCellType.Breakable) selected.Add(flatIndex);
            else if (generated.Cells[flatIndex] == GeneratedCellType.Unbreakable) unbreakableSelection.Add(flatIndex);
            if (generated.ContainsItem[flatIndex]) itemIndices.Add(flatIndex);
        }
        int obstacleCount = selected.Count;
        int unbreakableCount = unbreakableSelection.Count;
        ContainedItemCount = itemIndices.Count;

        for (int i = 0; i < selected.Count; i++)
        {
            int flatIndex = selected[i];
            Vector2Int tileIndex = FromFlatIndex(flatIndex);
            bool containsItem = itemIndices.Contains(flatIndex);
            GameObject obstacle = dataOnlyServer ? null : AcquireObstacle();
            if (obstacle != null)
            {
                PlaceObstacle(obstacle, tileIndex, containsItem);
            }
            recordsByFlatIndex[flatIndex] = new ObstacleRecord
            {
                Instance = obstacle,
                ContainsItem = containsItem,
                Destroyed = false
            };
            activeFlatIndices.Add(flatIndex);
            movementBlockedByFlatIndex[flatIndex] = true;
            obstacleSurfaceYByFlatIndex[flatIndex] = obstacle != null
                ? GetObstacleSurfaceY(obstacle)
                : tileManager.GetTileSurfaceY(tileIndex);
        }

        for (int i = 0; i < unbreakableSelection.Count; i++)
        {
            int flatIndex = unbreakableSelection[i];
            Vector2Int tileIndex = FromFlatIndex(flatIndex);
            GameObject obstacle = dataOnlyServer ? null : AcquireUnbreakableObstacle();
            if (obstacle != null)
            {
                PlaceUnbreakableObstacle(obstacle, tileIndex);
            }
            tileManager.ForceSetTileOwnerLocal(tileIndex, PaintOwner.Neutral);
            recordsByFlatIndex[flatIndex] = new ObstacleRecord
            {
                Instance = obstacle,
                ContainsItem = false,
                Destroyed = false
            };
            unbreakableFlatIndices.Add(flatIndex);
            movementBlockedByFlatIndex[flatIndex] = true;
            unbreakableByFlatIndex[flatIndex] = true;
            obstacleSurfaceYByFlatIndex[flatIndex] = obstacle != null
                ? GetObstacleSurfaceY(obstacle)
                : tileManager.GetTileSurfaceY(tileIndex);
        }

        ApplyPendingDestructions();
        Debug.Log(
            "[Obstacles] Built validated symmetric field. seed=" + seed +
            " obstacles=" + obstacleCount +
            " unbreakable=" + unbreakableCount +
            " containedItems=" + ContainedItemCount +
            " generation={" + LastGenerationReport + "}");
        return true;
    }

    private GameObject AcquireObstacle()
    {
        while (nextObstaclePoolIndex < obstaclePool.Count)
        {
            GameObject pooled = obstaclePool[nextObstaclePoolIndex++];
            if (pooled != null && !pooled.activeSelf)
            {
                return pooled;
            }
        }

        GameObject created = Instantiate(
            obstaclePrefab,
            obstacleRoot != null ? obstacleRoot : transform);
        obstaclePool.Add(created);
        nextObstaclePoolIndex = obstaclePool.Count;
        return created;
    }

    private GameObject AcquireUnbreakableObstacle()
    {
        while (nextUnbreakablePoolIndex < unbreakableObstaclePool.Count)
        {
            GameObject pooled = unbreakableObstaclePool[nextUnbreakablePoolIndex++];
            if (pooled != null && !pooled.activeSelf)
            {
                return pooled;
            }
        }

        GameObject created = Instantiate(
            unbreakableObstaclePrefab,
            obstacleRoot != null ? obstacleRoot : transform);
        unbreakableObstaclePool.Add(created);
        nextUnbreakablePoolIndex = unbreakableObstaclePool.Count;
        return created;
    }

    private void PlaceObstacle(GameObject obstacle, Vector2Int index, bool containsItem)
    {
        PlaceAtTileSurface(obstacle, index);

        DestructibleObstacle destructible = obstacle.GetComponent<DestructibleObstacle>();
        if (destructible == null)
        {
            Debug.LogError("[Obstacles] Obstacle prefab needs DestructibleObstacle.");
            obstacle.SetActive(false);
            return;
        }

        destructible.Configure(this, index, containsItem);
    }

    private void PlaceUnbreakableObstacle(GameObject obstacle, Vector2Int index)
    {
        PlaceAtTileSurface(obstacle, index);
    }

    private void PlaceAtTileSurface(GameObject obstacle, Vector2Int index)
    {
        Vector3 position = tileManager.GetTileCenterWorldPosition(index);
        BoxCollider boxCollider = obstacle.GetComponent<BoxCollider>();
        MatchObstacleFootprintToTile(obstacle.transform, boxCollider);
        if (boxCollider != null)
        {
            boxCollider.enabled = true;
        }
        obstacle.SetActive(true);
        float localBottomOffset = 0f;
        if (boxCollider != null)
        {
            float scaledCenterY = boxCollider.center.y * obstacle.transform.lossyScale.y;
            float scaledHalfHeight =
                Mathf.Abs(boxCollider.size.y * obstacle.transform.lossyScale.y) * 0.5f;
            localBottomOffset = scaledCenterY - scaledHalfHeight;
        }

        position.y = tileManager.GetTileSurfaceY(index) + surfaceOffset - localBottomOffset;
        obstacle.transform.position = position;
    }

    private void MatchObstacleFootprintToTile(
        Transform obstacleTransform,
        BoxCollider boxCollider)
    {
        if (obstacleTransform == null || boxCollider == null || tileManager == null)
        {
            return;
        }

        Transform parent = obstacleTransform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        float colliderWidth = Mathf.Abs(boxCollider.size.x);
        float colliderDepth = Mathf.Abs(boxCollider.size.z);
        if (colliderWidth <= 0.0001f || colliderDepth <= 0.0001f)
        {
            return;
        }

        float targetSize = Mathf.Max(0.001f, tileManager.TileSize);
        Vector3 localScale = obstacleTransform.localScale;
        Renderer visualRenderer = obstacleTransform.GetComponentInChildren<Renderer>(true);
        if (visualRenderer != null &&
            visualRenderer.bounds.size.x > 0.0001f &&
            visualRenderer.bounds.size.z > 0.0001f)
        {
            float visualTargetSize = targetSize * Mathf.Clamp(visualFootprintFill, 0.9f, 1.05f);
            localScale.x *= visualTargetSize / visualRenderer.bounds.size.x;
            localScale.z *= visualTargetSize / visualRenderer.bounds.size.z;
        }
        else
        {
            localScale.x = targetSize /
                (colliderWidth * Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)));
            localScale.z = targetSize /
                (colliderDepth * Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }

        obstacleTransform.localScale = localScale;

        // Keep collision exactly tile-sized even when the authored mesh is narrower.
        Vector3 colliderSize = boxCollider.size;
        colliderSize.x = targetSize / Mathf.Max(0.0001f, Mathf.Abs(obstacleTransform.lossyScale.x));
        colliderSize.z = targetSize / Mathf.Max(0.0001f, Mathf.Abs(obstacleTransform.lossyScale.z));
        boxCollider.size = colliderSize;
    }

    private static float GetObstacleSurfaceY(GameObject obstacle)
    {
        if (obstacle == null)
        {
            return 0f;
        }

        Renderer renderer = obstacle.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            return renderer.bounds.max.y;
        }

        Collider obstacleCollider = obstacle.GetComponentInChildren<Collider>(true);
        return obstacleCollider != null ? obstacleCollider.bounds.max.y : obstacle.transform.position.y;
    }

    private bool TryDestroyLocal(
        int flatIndex,
        bool spawnContainedItem,
        bool playFinalCollapse = false)
    {
        if (!IsBuilt ||
            flatIndex < 0 ||
            flatIndex >= recordsByFlatIndex.Length)
        {
            return false;
        }

        ObstacleRecord record = recordsByFlatIndex[flatIndex];
        if (record == null || record.Destroyed)
        {
            return false;
        }

        record.Destroyed = true;
        movementBlockedByFlatIndex[flatIndex] = false;
        if (unbreakableByFlatIndex != null)
        {
            unbreakableByFlatIndex[flatIndex] = false;
        }
        if (obstacleSurfaceYByFlatIndex != null)
        {
            obstacleSurfaceYByFlatIndex[flatIndex] = 0f;
        }
        if (record.Instance != null)
        {
            if (playFinalCollapse && record.Instance.activeSelf)
            {
                QueueFinalCollapse(record.Instance, flatIndex);
            }
            else
            {
                record.Instance.SetActive(false);
            }
        }

        destroyedFlatIndices.Add(flatIndex);
        if (spawnContainedItem && record.ContainsItem)
        {
            ItemSpawner.SpawnContainedItemAtTile(
                FromFlatIndex(flatIndex),
                builtSeed ^ flatIndex ^ ItemSeedSalt);
            if (playFinalCollapse)
            {
                finalRevealedItemFlatIndices.Add(flatIndex);
            }
        }

        return true;
    }

    private bool IsFlatIndexUnbreakable(int flatIndex)
    {
        return unbreakableByFlatIndex != null &&
               flatIndex >= 0 &&
               flatIndex < unbreakableByFlatIndex.Length &&
               unbreakableByFlatIndex[flatIndex];
    }

    private void ReturnAllObstaclesToPool()
    {
        CancelFinalCollapseEffect();
        nextObstaclePoolIndex = 0;
        nextUnbreakablePoolIndex = 0;
        for (int i = 0; i < obstaclePool.Count; i++)
        {
            if (obstaclePool[i] != null)
            {
                obstaclePool[i].SetActive(false);
            }
        }

        for (int i = 0; i < unbreakableObstaclePool.Count; i++)
        {
            if (unbreakableObstaclePool[i] != null)
            {
                unbreakableObstaclePool[i].SetActive(false);
            }
        }
    }

    private void QueueFinalCollapse(GameObject obstacle, int flatIndex)
    {
        Transform obstacleTransform = obstacle.transform;
        BoxCollider boxCollider = obstacle.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            boxCollider.enabled = false;
        }

        int staggerSlot = Mathf.Abs(flatIndex * 31) % 9;
        float staggerDelay = finalBlackHoleLeadIn +
            finalCollapseStagger * (staggerSlot / 8f);
        Vector3 rotationAxis = new Vector3(
            (flatIndex & 1) == 0 ? 1f : -1f,
            0.35f,
            (flatIndex & 2) == 0 ? -1f : 1f).normalized;
        finalCollapseEntries.Add(new FinalCollapseEntry
        {
            Instance = obstacle,
            Transform = obstacleTransform,
            Collider = boxCollider,
            Position = obstacleTransform.position,
            Rotation = obstacleTransform.rotation,
            Scale = obstacleTransform.localScale,
            RotationAxis = rotationAxis,
            Delay = staggerDelay,
            FlatIndex = flatIndex
        });
    }

    private void StartFinalCollapseEffect()
    {
        if (finalCollapseRoutine != null)
        {
            return;
        }

        ShowFinalBlackHole();
        QueueActiveItemsForFinalCollapse();
        if (finalCollapseEntries.Count == 0 && finalItemCollapseEntries.Count == 0)
        {
            HideFinalBlackHole();
            return;
        }

        enabled = true;
        finalCollapseRoutine = StartCoroutine(PlayFinalCollapseEffect());
    }

    private IEnumerator PlayFinalCollapseEffect()
    {
        float duration = Mathf.Max(0.1f, finalCollapseDuration);
        float totalDuration = duration + Mathf.Max(0f, finalCollapseStagger) +
            Mathf.Max(0f, finalBlackHoleLeadIn) + Mathf.Max(0f, finalItemRevealDelay);
        float elapsed = 0f;
        float nextVisualUpdateAt = 0f;
        const float VisualUpdateInterval = 1f / 30f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < nextVisualUpdateAt)
            {
                yield return null;
                continue;
            }

            nextVisualUpdateAt = elapsed + VisualUpdateInterval;
            for (int i = 0; i < finalCollapseEntries.Count; i++)
            {
                FinalCollapseEntry entry = finalCollapseEntries[i];
                if (entry.Instance == null || !entry.Instance.activeSelf)
                {
                    continue;
                }

                float progress = Mathf.Clamp01((elapsed - entry.Delay) / duration);
                if (progress <= 0f)
                {
                    continue;
                }

                float eased = progress * progress * (3f - 2f * progress);
                float remaining = 1f - progress;
                float scale = Mathf.Max(0.025f, remaining * remaining);
                float spinDirection = (i & 1) == 0 ? 1f : -1f;
                Vector3 toTarget = finalBlackHoleTarget - entry.Position;
                Vector3 horizontalDirection = new Vector3(
                    toTarget.x,
                    0f,
                    toTarget.z).normalized;
                Vector3 sideDirection = Vector3.Cross(Vector3.up, horizontalDirection);
                float sideAmplitude = Mathf.Min(0.65f, toTarget.magnitude * 0.035f);
                float spiral = Mathf.Sin(
                    finalSuctionTurns * progress * Mathf.Deg2Rad) *
                    spinDirection *
                    Mathf.Sin(progress * Mathf.PI) *
                    sideAmplitude;
                entry.Transform.localScale = entry.Scale * scale;
                entry.Transform.position = Vector3.Lerp(
                    entry.Position,
                    finalBlackHoleTarget,
                    eased) + sideDirection * spiral;
                entry.Transform.rotation = entry.Rotation * Quaternion.AngleAxis(
                    eased * finalCollapseRotation,
                    entry.RotationAxis);
            }

            for (int i = 0; i < finalItemCollapseEntries.Count; i++)
            {
                FinalItemCollapseEntry entry = finalItemCollapseEntries[i];
                if (entry.Instance == null || !entry.Instance.activeSelf)
                {
                    continue;
                }

                float progress = Mathf.Clamp01((elapsed - entry.Delay) / duration);
                if (progress <= 0f)
                {
                    continue;
                }

                ApplySuctionTransform(
                    entry.Transform,
                    entry.Position,
                    entry.Rotation,
                    entry.Scale,
                    entry.RotationAxis,
                    entry.SpinDirection,
                    progress);
            }

            yield return null;
        }

        CompleteFinalCollapseEffect();
    }

    private void QueueActiveItemsForFinalCollapse()
    {
        finalItemCollapseEntries.Clear();
        if (ItemPool.Instance == null || tileManager == null)
        {
            finalRevealedItemFlatIndices.Clear();
            return;
        }

        ItemPool.Instance.CopyActiveItemsTo(activeItemBuffer);
        for (int i = 0; i < activeItemBuffer.Count; i++)
        {
            GameObject itemObject = activeItemBuffer[i];
            if (itemObject == null || !itemObject.activeInHierarchy)
            {
                continue;
            }

            Vector2Int tileIndex = tileManager.GetGridIndex(itemObject.transform.position);
            if (tileIndex.x < 0 || tileIndex.y < 0 ||
                tileIndex.x >= builtGridSize || tileIndex.y >= builtGridSize)
            {
                continue;
            }

            int flatIndex = ToFlatIndex(tileIndex);
            if (finalRevealedItemFlatIndices.Contains(flatIndex))
            {
                continue;
            }

            float delay = GetFinalCollapseDelay(flatIndex) + finalItemRevealDelay;

            DropItem dropItem = itemObject.GetComponent<DropItem>();
            if (dropItem == null || !dropItem.BeginBlackHoleSuction())
            {
                continue;
            }

            Transform itemTransform = itemObject.transform;
            Vector3 rotationAxis = new Vector3(
                (flatIndex & 1) == 0 ? -0.4f : 0.4f,
                1f,
                (flatIndex & 2) == 0 ? 0.35f : -0.35f).normalized;
            finalItemCollapseEntries.Add(new FinalItemCollapseEntry
            {
                Instance = itemObject,
                DropItem = dropItem,
                Transform = itemTransform,
                Position = itemTransform.position,
                Rotation = itemTransform.rotation,
                Scale = itemTransform.localScale,
                RotationAxis = rotationAxis,
                Delay = delay,
                SpinDirection = (flatIndex & 1) == 0 ? 1f : -1f
            });
        }

        activeItemBuffer.Clear();
        finalRevealedItemFlatIndices.Clear();
    }

    private float GetFinalCollapseDelay(int flatIndex)
    {
        for (int i = 0; i < finalCollapseEntries.Count; i++)
        {
            if (finalCollapseEntries[i].FlatIndex == flatIndex)
            {
                return finalCollapseEntries[i].Delay;
            }
        }

        int staggerSlot = Mathf.Abs(flatIndex * 31) % 9;
        return finalBlackHoleLeadIn + finalCollapseStagger * (staggerSlot / 8f);
    }

    private void ApplySuctionTransform(
        Transform targetTransform,
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 startScale,
        Vector3 rotationAxis,
        float spinDirection,
        float progress)
    {
        float eased = progress * progress * (3f - 2f * progress);
        float remaining = 1f - progress;
        float scale = Mathf.Max(0.025f, remaining * remaining);
        Vector3 toTarget = finalBlackHoleTarget - startPosition;
        Vector3 horizontalDirection = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
        Vector3 sideDirection = Vector3.Cross(Vector3.up, horizontalDirection);
        float sideAmplitude = Mathf.Min(0.65f, toTarget.magnitude * 0.035f);
        float spiral = Mathf.Sin(finalSuctionTurns * progress * Mathf.Deg2Rad) *
            spinDirection * Mathf.Sin(progress * Mathf.PI) * sideAmplitude;

        targetTransform.localScale = startScale * scale;
        targetTransform.position = Vector3.Lerp(startPosition, finalBlackHoleTarget, eased) +
            sideDirection * spiral;
        targetTransform.rotation = startRotation * Quaternion.AngleAxis(
            eased * finalCollapseRotation,
            rotationAxis);
    }

    private void ReturnCollapsedItemsToPool()
    {
        for (int i = 0; i < finalItemCollapseEntries.Count; i++)
        {
            FinalItemCollapseEntry entry = finalItemCollapseEntries[i];
            if (entry.Instance == null)
            {
                continue;
            }

            entry.Transform.position = entry.Position;
            entry.Transform.rotation = entry.Rotation;
            entry.Transform.localScale = entry.Scale;
            if (ItemPool.Instance != null)
            {
                ItemPool.Instance.ReturnToPool(entry.Instance);
            }
            else
            {
                entry.Instance.SetActive(false);
            }
        }

        finalItemCollapseEntries.Clear();
    }

    private void RestoreCollapsedItems()
    {
        for (int i = 0; i < finalItemCollapseEntries.Count; i++)
        {
            FinalItemCollapseEntry entry = finalItemCollapseEntries[i];
            if (entry.Instance == null)
            {
                continue;
            }

            entry.Transform.position = entry.Position;
            entry.Transform.rotation = entry.Rotation;
            entry.Transform.localScale = entry.Scale;
            if (entry.DropItem != null)
            {
                entry.DropItem.CancelBlackHoleSuction();
            }
        }

        finalItemCollapseEntries.Clear();
    }

    private void CompleteFinalCollapseEffect()
    {
        for (int i = 0; i < finalCollapseEntries.Count; i++)
        {
            RestoreCollapsedObstacle(finalCollapseEntries[i]);
        }

        finalCollapseEntries.Clear();
        ReturnCollapsedItemsToPool();
        finalCollapseRoutine = null;
        HideFinalBlackHole();
    }

    private void CancelFinalCollapseEffect()
    {
        if (finalCollapseRoutine != null)
        {
            StopCoroutine(finalCollapseRoutine);
            finalCollapseRoutine = null;
        }

        for (int i = 0; i < finalCollapseEntries.Count; i++)
        {
            RestoreCollapsedObstacle(finalCollapseEntries[i]);
        }

        finalCollapseEntries.Clear();
        RestoreCollapsedItems();
        HideFinalBlackHole();
    }

    private void ShowFinalBlackHole()
    {
        if (tileManager == null || builtGridSize <= 0)
        {
            return;
        }

        Vector2Int centerIndex = new Vector2Int(
            builtGridSize / 2,
            builtGridSize / 2);
        finalBlackHoleTarget = tileManager.GetTileCenterWorldPosition(centerIndex);
        finalBlackHoleTarget.y =
            tileManager.GetTileSurfaceY(centerIndex) + finalBlackHoleHeight;
        Camera gameplayCamera = Camera.main;
        if (gameplayCamera != null)
        {
            finalBlackHoleTarget = gameplayCamera.ViewportToWorldPoint(
                new Vector3(0.5f, 0.58f, 5f));
        }

        if (finalBlackHolePrefab == null)
        {
            return;
        }

        if (finalBlackHoleInstance == null)
        {
            finalBlackHoleInstance = Instantiate(
                finalBlackHolePrefab,
                obstacleRoot != null ? obstacleRoot : transform);
            finalBlackHoleInstance.SetActive(false);
        }

        finalBlackHoleInstance.transform.position = finalBlackHoleTarget;
        finalBlackHoleInstance.transform.rotation = Quaternion.identity;
        finalBlackHoleInstance.transform.localScale = Vector3.one * finalBlackHoleScale;
        finalBlackHoleInstance.SetActive(true);
    }

    private void HideFinalBlackHole()
    {
        if (finalBlackHoleInstance != null)
        {
            finalBlackHoleInstance.SetActive(false);
        }
    }

    private static void RestoreCollapsedObstacle(FinalCollapseEntry entry)
    {
        if (entry.Instance == null)
        {
            return;
        }

        entry.Instance.SetActive(false);
        entry.Transform.position = entry.Position;
        entry.Transform.rotation = entry.Rotation;
        entry.Transform.localScale = entry.Scale;
        if (entry.Collider != null)
        {
            entry.Collider.enabled = true;
        }
    }

    private void BroadcastDestroyedBatch(
        int[] flatIndices,
        bool spawnItems,
        bool finalCountdown)
    {
        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(new ObstacleDestroyedBatchBroadcast
        {
            FlatIndices = flatIndices,
            SpawnContainedItems = spawnItems ? (byte)1 : (byte)0,
            FinalCountdownActive = finalCountdown ? (byte)1 : (byte)0
        });
    }

    private void OnDestroyedBatch(ObstacleDestroyedBatchBroadcast message, Channel channel)
    {
        bool finalCollapse = message.FinalCountdownActive != 0;
        if (finalCollapse)
        {
            finalCountdownActive = true;
        }

        ApplyOrQueueDestruction(
            message.FlatIndices,
            message.SpawnContainedItems != 0,
            finalCollapse);
    }

    private void OnSnapshotRequest(
        NetworkConnection connection,
        ObstacleSnapshotRequestBroadcast message,
        Channel channel)
    {
        if (!IsBuilt || networkManager == null || !networkManager.IsServerStarted)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(connection, new ObstacleSnapshotBroadcast
        {
            LayoutSeed = builtSeed,
            DestroyedFlatIndices = destroyedFlatIndices.ToArray(),
            FinalCountdownActive = finalCountdownActive ? (byte)1 : (byte)0
        });
    }

    private void OnSnapshot(ObstacleSnapshotBroadcast message, Channel channel)
    {
        snapshotReceived = true;
        synchronizedSeed = message.LayoutSeed;
        finalCountdownActive = message.FinalCountdownActive != 0;
        TryBuildField(message.LayoutSeed);
        ApplyOrQueueDestruction(message.DestroyedFlatIndices, false);
        if (finalCountdownActive)
        {
            ApplyFinalCountdownState();
        }
    }

    private void ApplyOrQueueDestruction(
        int[] flatIndices,
        bool spawnItems,
        bool playFinalCollapse = false)
    {
        if (flatIndices == null || flatIndices.Length == 0)
        {
            return;
        }

        if (!IsBuilt)
        {
            if (playFinalCollapse)
            {
                finalRevealedItemFlatIndices.Clear();
            }

            pendingDestructions.Add(new PendingDestruction
            {
                FlatIndices = flatIndices,
                SpawnItems = spawnItems,
                PlayFinalCollapse = playFinalCollapse
            });
            return;
        }

        if (playFinalCollapse)
        {
            finalRevealedItemFlatIndices.Clear();
        }

        for (int i = 0; i < flatIndices.Length; i++)
        {
            TryDestroyLocal(flatIndices[i], spawnItems, playFinalCollapse);
        }

        if (playFinalCollapse)
        {
            StartFinalCollapseEffect();
        }
    }

    private void ApplyPendingDestructions()
    {
        if (!IsBuilt || pendingDestructions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < pendingDestructions.Count; i++)
        {
            PendingDestruction pending = pendingDestructions[i];
            ApplyOrQueueDestruction(
                pending.FlatIndices,
                pending.SpawnItems,
                pending.PlayFinalCollapse);
        }

        pendingDestructions.Clear();
    }

    private void RequestSnapshotIfNeeded()
    {
        if (snapshotReceived ||
            networkManager == null ||
            !networkManager.IsClientStarted ||
            networkManager.IsServerStarted ||
            Time.unscaledTime < nextSnapshotRequestAt)
        {
            return;
        }

        nextSnapshotRequestAt = Time.unscaledTime + snapshotRetrySeconds;
        networkManager.ClientManager.Broadcast(new ObstacleSnapshotRequestBroadcast
        {
            Requested = 1
        });
    }

    private void RegisterBroadcasts()
    {
        if (broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<ObstacleSnapshotRequestBroadcast>(
            OnSnapshotRequest,
            false);
        networkManager.ClientManager.RegisterBroadcast<ObstacleSnapshotBroadcast>(OnSnapshot);
        networkManager.ClientManager.RegisterBroadcast<ObstacleDestroyedBatchBroadcast>(OnDestroyedBatch);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<ObstacleSnapshotRequestBroadcast>(OnSnapshotRequest);
        networkManager.ClientManager.UnregisterBroadcast<ObstacleSnapshotBroadcast>(OnSnapshot);
        networkManager.ClientManager.UnregisterBroadcast<ObstacleDestroyedBatchBroadcast>(OnDestroyedBatch);
        broadcastsRegistered = false;
    }

    private bool CanMutateAuthoritatively()
    {
        CacheNetworkManager();
        return networkManager == null ||
               networkManager.IsServerStarted ||
               !networkManager.IsClientStarted;
    }

    private void CacheReferences()
    {
        if (tileManager == null && TilePaintManager.HasInstance)
        {
            tileManager = TilePaintManager.Instance;
        }

        if (obstacleRoot == null)
        {
            obstacleRoot = transform;
        }
    }

    private void CacheNetworkManager()
    {
        if (networkManager == null)
        {
            networkManager = FindFirstObjectByType<NetworkManager>();
        }
    }

    private int ToFlatIndex(Vector2Int index)
    {
        return index.x * builtGridSize + index.y;
    }

    private Vector2Int FromFlatIndex(int flatIndex)
    {
        return new Vector2Int(flatIndex / builtGridSize, flatIndex % builtGridSize);
    }

    private sealed class ObstacleRecord
    {
        public GameObject Instance;
        public bool ContainsItem;
        public bool Destroyed;
    }

    private struct PendingDestruction
    {
        public int[] FlatIndices;
        public bool SpawnItems;
        public bool PlayFinalCollapse;
    }

    private struct FinalCollapseEntry
    {
        public GameObject Instance;
        public Transform Transform;
        public BoxCollider Collider;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector3 RotationAxis;
        public float Delay;
        public int FlatIndex;
    }

    private struct FinalItemCollapseEntry
    {
        public GameObject Instance;
        public DropItem DropItem;
        public Transform Transform;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector3 RotationAxis;
        public float Delay;
        public float SpinDirection;
    }
}
