using UnityEngine;
using System.Collections.Generic;

public class ItemSpawner : MonoBehaviour
{
    public static ItemSpawner Instance { get; private set; }

    [Header("Round Spawn")]
    [SerializeField] private int roundSpawnCount = 10;
    [SerializeField] private float groundedSpawnOffset = 0.03f;
    [SerializeField] private bool avoidDuplicateTiles = true;

    private const int SeedSalt = 7919;
    private static int synchronizedSessionSeed;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void SetSynchronizedSessionSeed(int seed)
    {
        synchronizedSessionSeed = seed;
    }

    public static void SpawnRoundItemsForCurrentScene(int roundIndex)
    {
        ItemSpawner spawner = Instance;
        if (spawner == null)
        {
            return;
        }

        spawner.SpawnRoundItems(roundIndex);
    }

    public static void ClearAllItemsForCurrentScene()
    {
        if (ItemPool.Instance != null)
        {
            ItemPool.Instance.ReturnAllActiveItems();
        }
    }

    public static void SpawnContainedItemAtTile(Vector2Int tileIndex, int deterministicSeed)
    {
        if (Instance != null)
        {
            Instance.SpawnItemAtTile(tileIndex, deterministicSeed);
        }
    }

    public void SpawnItem()
    {
        if (!TryGetRandomTile(out Vector2Int tileIndex))
        {
            return;
        }

        SpawnItemAtTile(tileIndex);
    }

    public void SpawnRoundItems(int roundIndex)
    {
        if (ItemPool.Instance == null || roundSpawnCount <= 0)
        {
            return;
        }

        Random.State previousState = Random.state;
        Random.InitState(GetRoundSeed(roundIndex));

        ItemPool.Instance.ReturnAllActiveItems();
        HashSet<Vector2Int> usedTiles = new HashSet<Vector2Int>();
        int maxAttempts = Mathf.Max(roundSpawnCount * 8, 16);
        int spawnedCount = 0;
        int attempts = 0;

        while (spawnedCount < roundSpawnCount && attempts < maxAttempts)
        {
            attempts++;
            if (!TryGetRandomTile(out Vector2Int tileIndex))
            {
                break;
            }

            if (avoidDuplicateTiles && !usedTiles.Add(tileIndex))
            {
                continue;
            }

            SpawnItemAtTile(tileIndex);
            spawnedCount++;
        }

        Random.state = previousState;
    }

    private bool TryGetRandomTile(out Vector2Int tileIndex)
    {
        tileIndex = Vector2Int.zero;
        int gridSize = GetGridSize();
        if (gridSize <= 0 || ItemPool.Instance == null)
        {
            return false;
        }

        int attempts = Mathf.Max(16, gridSize);
        for (int i = 0; i < attempts; i++)
        {
            tileIndex = new Vector2Int(
                Random.Range(0, gridSize),
                Random.Range(0, gridSize));
            if (!TilePaintManager.HasInstance ||
                !TilePaintManager.Instance.IsGridReady ||
                !TilePaintManager.Instance.IsTileDestroyed(tileIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetRoundSeed(int roundIndex)
    {
        int roundSeed = roundIndex * SeedSalt;
        return synchronizedSessionSeed != 0
            ? roundSeed ^ synchronizedSessionSeed
            : roundSeed;
    }

    private void SpawnItemAtTile(Vector2Int tileIndex)
    {
        SpawnItemAtTile(tileIndex, 0);
    }

    private void SpawnItemAtTile(Vector2Int tileIndex, int deterministicSeed)
    {
        if (ItemPool.Instance == null)
        {
            return;
        }

        Vector3 spawnPos = GetTileWorldPosition(tileIndex);
        float surfaceY = GetTileSurfaceY(tileIndex);
        spawnPos.y = surfaceY + groundedSpawnOffset;

        GameObject item = deterministicSeed == 0
            ? ItemPool.Instance.GetFromPool(spawnPos)
            : ItemPool.Instance.GetFromPool(spawnPos, deterministicSeed);
        DropItem dropItem = item != null ? item.GetComponent<DropItem>() : null;
        if (dropItem != null)
        {
            dropItem.PlaceOnGround(surfaceY, groundedSpawnOffset);
        }
    }

    private int GetGridSize()
    {
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            return TilePaintManager.Instance.GridSize;
        }

        return TileGenerator.Instance != null ? TileGenerator.Instance.gridSize : 0;
    }

    private Vector3 GetTileWorldPosition(Vector2Int tileIndex)
    {
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            return TilePaintManager.Instance.GetTileCenterWorldPosition(tileIndex);
        }

        if (TileGenerator.Instance == null)
        {
            return Vector3.zero;
        }

        return TileGenerator.Instance.gridOrigin + new Vector3(
            tileIndex.x * TileGenerator.Instance.tileSize,
            0f,
            tileIndex.y * TileGenerator.Instance.tileSize);
    }

    private float GetTileSurfaceY(Vector2Int tileIndex)
    {
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            return TilePaintManager.Instance.GetTileSurfaceY(tileIndex);
        }

        return GetTileWorldPosition(tileIndex).y;
    }
}
