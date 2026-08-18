using System.Collections.Generic;
using UnityEngine;

public sealed class TeleportLockProjectile : MonoBehaviour
{
    private const int MaxPoolSize = 12;
    private const float DefaultSpeed = 10.5f;
    private const float SpawnHeight = 0.75f;
    private const float SpawnForwardOffset = 0.9f;
    private const float TargetHeight = 1.75f;
    private const float ArriveDistance = 0.22f;
    private const float MaxLifetime = 4f;
    private const float TargetVisualBoundsSize = 0.55f;

    private static readonly Stack<TeleportLockProjectile> Pool =
        new Stack<TeleportLockProjectile>();
    private static GameObject sharedVisualPrefab;

    private PaintOwner targetOwner;
    private Transform target;
    private GameObject visualObject;
    private float speed;
    private float lifetime;
    private bool arrived;

    public static void SetSharedVisualPrefab(GameObject prefab)
    {
        if (prefab != null)
        {
            sharedVisualPrefab = prefab;
        }
    }

    public static float GetEstimatedTravelDelay(Vector3 startPosition, Transform targetTransform)
    {
        if (targetTransform == null)
        {
            return 0.4f;
        }

        Vector3 start = startPosition + Vector3.up * SpawnHeight;
        Vector3 destination = targetTransform.position + Vector3.up * TargetHeight;
        float distance = Vector3.Distance(start, destination);
        return Mathf.Clamp(distance / DefaultSpeed, 0.25f, MaxLifetime);
    }

    public static TeleportLockProjectile Spawn(
        Vector3 position,
        PaintOwner sourceOwner,
        PaintOwner targetOwner)
    {
        if (!TryFindTarget(targetOwner, out Transform targetTransform))
        {
            Debug.Log("[Item] TeleportLock visual target not found: " + targetOwner + ".");
            return null;
        }

        Vector3 direction = targetTransform.position - position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();

        TeleportLockProjectile projectile = Rent();
        projectile.gameObject.name = "Teleport Lock Projectile";
        projectile.gameObject.SetActive(true);
        projectile.transform.position =
            position + direction * SpawnForwardOffset + Vector3.up * SpawnHeight;
        projectile.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        projectile.Configure(targetOwner, targetTransform);
        return projectile;
    }

    private void Configure(PaintOwner newTargetOwner, Transform newTarget)
    {
        targetOwner = newTargetOwner;
        target = newTarget;
        speed = DefaultSpeed;
        lifetime = MaxLifetime;
        arrived = false;
        EnsureVisual();
    }

    private void Update()
    {
        if (arrived)
        {
            return;
        }

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f || target == null)
        {
            ReturnToPool();
            return;
        }

        if (!IsStillTargetOwner(target, targetOwner))
        {
            ReturnToPool();
            return;
        }

        Vector3 destination = target.position + Vector3.up * TargetHeight;
        Vector3 toTarget = destination - transform.position;
        float distance = toTarget.magnitude;
        if (distance <= ArriveDistance)
        {
            arrived = true;
            ReturnToPool();
            return;
        }

        Vector3 direction = toTarget / Mathf.Max(0.001f, distance);
        transform.position += direction * (speed * Time.deltaTime);
        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                Time.deltaTime * 10f);
        }

        if (visualObject != null)
        {
            visualObject.transform.Rotate(
                Vector3.up,
                240f * Time.deltaTime,
                Space.Self);
        }
    }

    private void EnsureVisual()
    {
        if (visualObject != null)
        {
            visualObject.SetActive(true);
            return;
        }

        visualObject = sharedVisualPrefab != null
            ? Instantiate(sharedVisualPrefab, transform)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        visualObject.name = "Teleport Lock Visual";
        visualObject.transform.SetParent(transform, false);
        visualObject.transform.localPosition = Vector3.zero;
        visualObject.transform.localRotation = Quaternion.identity;
        visualObject.transform.localScale = Vector3.one;
        RemoveRuntimeBehaviours(visualObject);
        RemovePhysics(visualObject);
        ScaleVisualToTargetBounds(visualObject);

        if (sharedVisualPrefab == null)
        {
            TintFallbackVisual();
        }
    }

    private static bool TryFindTarget(PaintOwner owner, out Transform targetTransform)
    {
        targetTransform = null;
        PlayerPaintIdentity[] identities = FindObjectsByType<PlayerPaintIdentity>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < identities.Length; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity != null && identity.Owner == owner)
            {
                targetTransform = identity.transform;
                return true;
            }
        }

        return false;
    }

    private static bool IsStillTargetOwner(Transform targetTransform, PaintOwner expectedOwner)
    {
        PlayerPaintIdentity identity =
            targetTransform.GetComponent<PlayerPaintIdentity>();
        return identity != null && identity.Owner == expectedOwner;
    }

    private static TeleportLockProjectile Rent()
    {
        while (Pool.Count > 0)
        {
            TeleportLockProjectile pooled = Pool.Pop();
            if (pooled != null)
            {
                return pooled;
            }
        }

        GameObject obj = new GameObject("Teleport Lock Projectile");
        return obj.AddComponent<TeleportLockProjectile>();
    }

    private void ReturnToPool()
    {
        target = null;
        gameObject.SetActive(false);
        if (Pool.Count < MaxPoolSize)
        {
            Pool.Push(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private static void RemovePhysics(GameObject targetObject)
    {
        Collider[] colliders = targetObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Destroy(colliders[i]);
        }

        Rigidbody[] rigidbodies = targetObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Destroy(rigidbodies[i]);
        }
    }

    private static void RemoveRuntimeBehaviours(GameObject targetObject)
    {
        MonoBehaviour[] behaviours = targetObject.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                Destroy(behaviours[i]);
            }
        }
    }

    private static void ScaleVisualToTargetBounds(GameObject targetObject)
    {
        if (!TryGetRendererBounds(targetObject, out Bounds bounds))
        {
            targetObject.transform.localScale = Vector3.one * 0.35f;
            return;
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize <= 0.001f)
        {
            return;
        }

        float scale = TargetVisualBoundsSize / maxSize;
        targetObject.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
    }

    private static bool TryGetRendererBounds(GameObject targetObject, out Bounds bounds)
    {
        Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds(targetObject.transform.position, Vector3.one);
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

    private void TintFallbackVisual()
    {
        Renderer indicatorRenderer = visualObject.GetComponent<Renderer>();
        if (indicatorRenderer == null)
        {
            return;
        }

        Material indicatorMaterial = new Material(indicatorRenderer.sharedMaterial);
        indicatorMaterial.color = new Color(1f, 0.85f, 0.08f, 1f);
        indicatorRenderer.sharedMaterial = indicatorMaterial;
    }
}
