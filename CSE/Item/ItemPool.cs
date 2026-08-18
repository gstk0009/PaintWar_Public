using System.Collections.Generic;
using UnityEngine;

public class ItemPool : MonoBehaviour
{
    public static ItemPool Instance;

    [Header("Legacy Default")]
    public GameObject itemPrefab;
    public int poolSize = 10;
    [SerializeField, Min(0)] private int maxInitialPoolSizePerPrefab = 6;

    [Header("Visuals")]
    [SerializeField] private float targetVisualBoundsSize = 0.65f;
    [SerializeField] private float visualFloatHeight = 0.16f;

    [Header("Shared Item Settings")]
    [SerializeField] private BombMineSettings bombMineSettings;

    [Header("SO Catalog")]
    [SerializeField] private DropItemCatalog itemCatalog;

    [Header("Legacy Spawn Configs")]
    [SerializeField] private DropItemSpawnConfig[] itemConfigs;

    private readonly Dictionary<GameObject, Queue<GameObject>> pools =
        new Dictionary<GameObject, Queue<GameObject>>();
    private readonly List<GameObject> activeItems = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
        BombMine.ApplySettings(bombMineSettings);
        InitializePool();
    }

    private void InitializePool()
    {
        List<GameObject> prefabs = GetConfiguredPrefabs();
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[ItemPool] No item prefab is assigned.");
            return;
        }

        int initialPerPrefab = Application.isBatchMode
            ? Mathf.Min(1, poolSize)
            : Mathf.Min(Mathf.Max(0, poolSize), maxInitialPoolSizePerPrefab);
        for (int i = 0; i < prefabs.Count; i++)
        {
            Queue<GameObject> pool = GetPool(prefabs[i]);
            for (int j = 0; j < initialPerPrefab; j++)
            {
                GameObject item = CreateItem(prefabs[i]);
                item.SetActive(false);
                pool.Enqueue(item);
            }
        }
    }

    public GameObject GetFromPool(Vector3 position)
    {
        DropItemSpawnConfig config = ApplySharedItemSettings(PickConfig());
        GameObject prefab = GetPrefab(config);
        if (prefab == null)
        {
            Debug.LogWarning("[ItemPool] Cannot spawn item because no prefab is available.");
            return null;
        }

        Queue<GameObject> pool = GetPool(prefab);
        GameObject item = pool.Count > 0
            ? pool.Dequeue()
            : CreateItem(prefab);

        item.transform.position = position;
        if (!ConfigureItem(item, config))
        {
            item.SetActive(false);
            item.transform.SetParent(transform);
            pool.Enqueue(item);
            Debug.LogWarning("[ItemPool] Skipped disabled item type: " + config.EffectType + ".");
            return null;
        }

        item.SetActive(true);
        if (!activeItems.Contains(item))
        {
            activeItems.Add(item);
        }

        return item;
    }

    public GameObject GetFromPool(Vector3 position, int deterministicSeed)
    {
        Random.State previousState = Random.state;
        Random.InitState(deterministicSeed == 0 ? 1 : deterministicSeed);
        GameObject item = GetFromPool(position);
        Random.state = previousState;
        return item;
    }

    public void ReturnToPool(GameObject item)
    {
        if (item == null)
        {
            return;
        }

        activeItems.Remove(item);
        item.SetActive(false);
        item.transform.SetParent(transform);

        PooledDropItem pooledItem = item.GetComponent<PooledDropItem>();
        GameObject sourcePrefab = pooledItem != null
            ? pooledItem.SourcePrefab
            : GetFallbackPrefab();
        GetPool(sourcePrefab).Enqueue(item);
    }

    public void ReturnAllActiveItems()
    {
        if (activeItems.Count == 0)
        {
            return;
        }

        while (activeItems.Count > 0)
        {
            ReturnToPool(activeItems[activeItems.Count - 1]);
        }
    }

    public void CopyActiveItemsTo(List<GameObject> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        for (int i = 0; i < activeItems.Count; i++)
        {
            GameObject item = activeItems[i];
            if (item != null && item.activeInHierarchy)
            {
                destination.Add(item);
            }
        }
    }

    private DropItemSpawnConfig PickConfig()
    {
        if (itemCatalog != null && itemCatalog.HasItems)
        {
            return itemCatalog.PickConfig(bombMineSettings);
        }

        if (itemConfigs == null || itemConfigs.Length == 0)
        {
            return PickBuiltInConfig();
        }

        float totalWeight = 0f;
        for (int i = 0; i < itemConfigs.Length; i++)
        {
            if (IsSpawnable(itemConfigs[i]))
            {
                totalWeight += Mathf.Max(0f, itemConfigs[i].Weight);
            }
        }

        if (totalWeight <= 0f)
        {
            return PickBuiltInConfig();
        }

        float roll = Random.value * totalWeight;
        DropItemSpawnConfig lastValid = default;
        bool hasLastValid = false;
        for (int i = 0; i < itemConfigs.Length; i++)
        {
            if (!IsSpawnable(itemConfigs[i]))
            {
                continue;
            }

            lastValid = itemConfigs[i];
            hasLastValid = true;
            roll -= Mathf.Max(0f, itemConfigs[i].Weight);
            if (roll <= 0f)
            {
                return itemConfigs[i];
            }
        }

        return hasLastValid ? lastValid : PickBuiltInConfig();
    }

    private DropItemSpawnConfig PickBuiltInConfig()
    {
        switch (Random.Range(0, 7))
        {
            case 1:
                return DropItemSpawnConfig.ExperienceDefault(itemPrefab);
            case 2:
                return DropItemSpawnConfig.CooldownResetDefault(itemPrefab);
            case 3:
                return DropItemSpawnConfig.ReverseControlsBombDefault(itemPrefab);
            case 4:
                return DropItemSpawnConfig.TeleportLockDefault(itemPrefab);
            case 5:
                return DropItemSpawnConfig.DecoyStampDefault(itemPrefab);
            case 6:
                return DropItemSpawnConfig.ShieldDefault(itemPrefab);
            case 0:
            default:
                return DropItemSpawnConfig.SpeedDefault(itemPrefab);
        }
    }

    private GameObject GetPrefab(DropItemSpawnConfig config)
    {
        if (config.Prefab != null)
        {
            return config.Prefab;
        }

        return GetFallbackPrefab();
    }

    private GameObject GetFallbackPrefab()
    {
        if (itemPrefab != null)
        {
            return itemPrefab;
        }

        if (itemConfigs != null)
        {
            for (int i = 0; i < itemConfigs.Length; i++)
            {
                if (itemConfigs[i].Prefab != null)
                {
                    return itemConfigs[i].Prefab;
                }
            }
        }

        if (itemCatalog != null)
        {
            GameObject catalogPrefab = itemCatalog.GetFirstPrefab();
            if (catalogPrefab != null)
            {
                return catalogPrefab;
            }
        }

        return null;
    }

    private DropItemSpawnConfig ApplySharedItemSettings(DropItemSpawnConfig config)
    {
        return config.EffectType == DropItemEffectType.BombMine
            ? config.WithBombMineSettings(bombMineSettings)
            : config;
    }

    private List<GameObject> GetConfiguredPrefabs()
    {
        List<GameObject> prefabs = new List<GameObject>();
        AddUniquePrefab(prefabs, itemPrefab);

        if (itemCatalog != null)
        {
            itemCatalog.AddConfiguredPrefabs(prefabs);
        }

        if (itemConfigs != null)
        {
            for (int i = 0; i < itemConfigs.Length; i++)
            {
                if (IsSpawnable(itemConfigs[i]))
                {
                    AddUniquePrefab(prefabs, itemConfigs[i].Prefab);
                }
            }
        }

        return prefabs;
    }

    private static bool IsSpawnable(DropItemSpawnConfig config)
    {
        return config.EffectType != DropItemEffectType.PaintShell &&
               config.EffectType != DropItemEffectType.BombMine;
    }

    private static void AddUniquePrefab(
        List<GameObject> prefabs,
        GameObject prefab)
    {
        if (prefab != null && !prefabs.Contains(prefab))
        {
            prefabs.Add(prefab);
        }
    }

    private Queue<GameObject> GetPool(GameObject prefab)
    {
        if (prefab == null)
        {
            prefab = GetFallbackPrefab();
        }

        if (!pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            pools[prefab] = pool;
        }

        return pool;
    }

    private GameObject CreateItem(GameObject prefab)
    {
        GameObject item = new GameObject(prefab.name + " DropItem");
        item.transform.SetParent(transform, false);

        if (!Application.isBatchMode)
        {
            GameObject visualScaleRoot = new GameObject("Visual Scale Root");
            visualScaleRoot.transform.SetParent(item.transform, false);

            GameObject visual = Instantiate(prefab, visualScaleRoot.transform);
            visual.name = prefab.name + " Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            RemoveVisualPhysics(visual);
            ConfigureVisualAnimation(visual);
            ScaleVisualToTargetBounds(visualScaleRoot.transform, visual);
        }
        EnsureDropItemPhysics(item);

        PooledDropItem pooledItem = item.GetComponent<PooledDropItem>();
        if (pooledItem == null)
        {
            pooledItem = item.AddComponent<PooledDropItem>();
        }

        pooledItem.SourcePrefab = prefab;
        return item;
    }

    private void ConfigureVisualAnimation(GameObject visual)
    {
        if (visual == null)
        {
            return;
        }

        Benjathemaker.SimpleGemsAnim[] anims =
            visual.GetComponentsInChildren<Benjathemaker.SimpleGemsAnim>(true);
        for (int i = 0; i < anims.Length; i++)
        {
            if (anims[i] != null && anims[i].isFloating)
            {
                anims[i].floatHeight = Mathf.Max(0f, visualFloatHeight);
            }
        }
    }

    private void ScaleVisualToTargetBounds(Transform visualScaleRoot, GameObject visual)
    {
        if (visualScaleRoot == null || visual == null || targetVisualBoundsSize <= 0f)
        {
            return;
        }

        Physics.SyncTransforms();
        Bounds visualBounds;
        if (!TryGetRendererBounds(visual, out visualBounds))
        {
            return;
        }

        Vector3 size = visualBounds.size;
        float maxSize = Mathf.Max(size.x, size.y, size.z);
        if (maxSize <= 0.001f)
        {
            return;
        }

        float scale = targetVisualBoundsSize / maxSize;
        visualScaleRoot.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        Physics.SyncTransforms();
    }

    private static void RemoveVisualPhysics(GameObject visual)
    {
        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
            Destroy(colliders[i]);
        }

        Rigidbody[] rigidbodies = visual.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].detectCollisions = false;
            rigidbodies[i].isKinematic = true;
            Destroy(rigidbodies[i]);
        }
    }

    private static void EnsureDropItemPhysics(GameObject item)
    {
        if (item == null)
        {
            return;
        }

        BoxCollider boxCollider = item.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = item.AddComponent<BoxCollider>();
            FitBoxColliderToRenderers(boxCollider, item);
        }

        Rigidbody rigidbody = item.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = item.AddComponent<Rigidbody>();
        }

        rigidbody.useGravity = true;
        rigidbody.isKinematic = false;
    }

    private static void FitBoxColliderToRenderers(
        BoxCollider boxCollider,
        GameObject item)
    {
        Bounds rendererBounds;
        if (!TryGetRendererBounds(item, out rendererBounds))
        {
            boxCollider.center = Vector3.zero;
            boxCollider.size = Vector3.one;
            return;
        }

        Transform itemTransform = item.transform;
        Vector3 localMin = itemTransform.InverseTransformPoint(rendererBounds.min);
        Vector3 localMax = localMin;

        EncapsulateLocalPoint(itemTransform, rendererBounds.min, ref localMin, ref localMax);
        EncapsulateLocalPoint(itemTransform, rendererBounds.max, ref localMin, ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.min.x, rendererBounds.min.y, rendererBounds.max.z),
            ref localMin,
            ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.min.x, rendererBounds.max.y, rendererBounds.min.z),
            ref localMin,
            ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.max.x, rendererBounds.min.y, rendererBounds.min.z),
            ref localMin,
            ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.min.x, rendererBounds.max.y, rendererBounds.max.z),
            ref localMin,
            ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.max.x, rendererBounds.min.y, rendererBounds.max.z),
            ref localMin,
            ref localMax);
        EncapsulateLocalPoint(
            itemTransform,
            new Vector3(rendererBounds.max.x, rendererBounds.max.y, rendererBounds.min.z),
            ref localMin,
            ref localMax);

        Vector3 size = localMax - localMin;
        boxCollider.center = (localMin + localMax) * 0.5f;
        boxCollider.size = new Vector3(
            Mathf.Max(0.1f, Mathf.Abs(size.x)),
            Mathf.Max(0.1f, Mathf.Abs(size.y)),
            Mathf.Max(0.1f, Mathf.Abs(size.z)));
    }

    private static void EncapsulateLocalPoint(
        Transform itemTransform,
        Vector3 worldPoint,
        ref Vector3 localMin,
        ref Vector3 localMax)
    {
        Vector3 localPoint = itemTransform.InverseTransformPoint(worldPoint);
        localMin = Vector3.Min(localMin, localPoint);
        localMax = Vector3.Max(localMax, localPoint);
    }

    private static bool TryGetRendererBounds(GameObject item, out Bounds bounds)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds(item.transform.position, Vector3.one);
        bool hasRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null)
            {
                continue;
            }

            if (!hasRenderer)
            {
                bounds = itemRenderer.bounds;
                hasRenderer = true;
            }
            else
            {
                bounds.Encapsulate(itemRenderer.bounds);
            }
        }

        return hasRenderer;
    }

    private bool ConfigureItem(GameObject item, DropItemSpawnConfig config)
    {
        DropItem dropItem = item.GetComponent<DropItem>();
        if (dropItem == null)
        {
            dropItem = item.AddComponent<DropItem>();
        }

        return dropItem.Configure(config);
    }
}

public sealed class PooledDropItem : MonoBehaviour
{
    public GameObject SourcePrefab;
}
