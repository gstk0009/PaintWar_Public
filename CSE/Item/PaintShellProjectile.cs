using System.Collections.Generic;
using UnityEngine;

public sealed class PaintShellProjectile : MonoBehaviour
{
    private const int MaxPoolSize = 24;
    private static readonly Stack<PaintShellProjectile> Pool = new Stack<PaintShellProjectile>();
    private const float DefaultSpeed = 12f;
    private const float SpawnForwardOffset = 1.1f;
    private const float SpawnHeight = 0.45f;

    private PaintOwner owner;
    private Vector3 direction;
    private float speed;
    private float lifetime;
    private float collisionRadius;
    private int explosionPaintRadius;
    private bool exploded;
    private Vector3 previousPosition;
    private Vector2Int lastPaintedIndex = new Vector2Int(int.MinValue, int.MinValue);
    private readonly HashSet<Vector2Int> paintedIndices = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> explosionMineDetonationIndices = new List<Vector2Int>(32);
    private readonly RaycastHit[] pathHits = new RaycastHit[16];

    public static PaintShellProjectile Spawn(
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius)
    {
        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = Vector3.forward;
        }

        flatDirection.Normalize();

        PaintShellProjectile projectile = Rent();
        GameObject shellObject = projectile.gameObject;
        shellObject.name = "Paint Shell Projectile";
        shellObject.SetActive(true);
        shellObject.transform.position =
            position + flatDirection * SpawnForwardOffset + Vector3.up * SpawnHeight;
        shellObject.transform.localScale = Vector3.one * 0.45f;

        Collider collider = shellObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = true;
        }

        projectile.Configure(
            owner,
            flatDirection,
            Mathf.Max(0.2f, duration),
            Mathf.Clamp(radius * 0.2f, 0.2f, 0.5f),
            Mathf.Max(0, paintRadius));

        return projectile;
    }

    private void Configure(
        PaintOwner shellOwner,
        Vector3 travelDirection,
        float activeSeconds,
        float hitRadius,
        int blastRadiusInTiles)
    {
        owner = shellOwner;
        exploded = false;
        paintedIndices.Clear();
        explosionMineDetonationIndices.Clear();
        lastPaintedIndex = new Vector2Int(int.MinValue, int.MinValue);
        direction = travelDirection;
        speed = DefaultSpeed;
        lifetime = activeSeconds;
        collisionRadius = hitRadius;
        explosionPaintRadius = blastRadiusInTiles;
        previousPosition = transform.position;
        ApplyOwnerMaterial();
        PaintTrailAt(transform.position, false);
    }

    private void Update()
    {
        if (exploded)
        {
            return;
        }

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Explode(transform.position);
            return;
        }

        Vector3 nextPosition = transform.position + direction * (speed * Time.deltaTime);
        if (CheckPathCollision(transform.position, nextPosition, out Vector3 hitPoint))
        {
            Explode(hitPoint);
            return;
        }

        if (PaintTrailBetween(transform.position, nextPosition))
        {
            return;
        }

        transform.position = nextPosition;
        previousPosition = transform.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (exploded || ShouldIgnoreCollider(other))
        {
            return;
        }

        Explode(transform.position);
    }

    private bool CheckPathCollision(
        Vector3 from,
        Vector3 to,
        out Vector3 hitPoint)
    {
        hitPoint = to;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            from,
            collisionRadius,
            delta / distance,
            pathHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.MaxValue;
        bool hasHit = false;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = pathHits[i].collider;
            if (ShouldIgnoreCollider(hitCollider))
            {
                continue;
            }

            if (pathHits[i].distance < nearestDistance)
            {
                nearestDistance = pathHits[i].distance;
                hitPoint = pathHits[i].point;
                hasHit = true;
            }
        }

        if (hasHit)
        {
            return true;
        }

        return HitMapBoundaryOrObstacle(to);
    }

    private bool HitMapBoundaryOrObstacle(Vector3 position)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        Vector2Int currentIndex = TilePaintManager.Instance.GetGridIndex(position);
        if (!TilePaintManager.Instance.IsInsideGrid(currentIndex))
        {
            return true;
        }

        if (TilePaintManager.Instance.IsTileDestroyed(currentIndex))
        {
            return true;
        }

        if (GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, currentIndex))
        {
            return true;
        }

        if (lastPaintedIndex.x == int.MinValue)
        {
            return false;
        }

        return false;
    }

    private bool ShouldIgnoreCollider(Collider collider)
    {
        if (collider == null || collider.transform == transform)
        {
            return true;
        }

        if (collider.GetComponentInParent<PaintShellProjectile>() == this)
        {
            return true;
        }

        DropItem dropItem = collider.GetComponentInParent<DropItem>();
        if (dropItem != null)
        {
            return true;
        }

        if (collider.CompareTag("Floor") ||
            collider.GetComponentInParent<PaintableTile>() != null ||
            collider.GetComponentInParent<TileGenerator>() != null)
        {
            return true;
        }

        PlayerPaintIdentity identity = collider.GetComponentInParent<PlayerPaintIdentity>();
        return identity != null &&
               (identity.Owner == owner ||
                PlayerShieldController.IsProtected(identity));
    }

    private bool PaintTrailBetween(Vector3 from, Vector3 to)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        float tileSize = Mathf.Max(0.1f, TilePaintManager.Instance.TileSize);
        float distance = Vector3.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (tileSize * 0.5f)));

        for (int i = 1; i <= steps; i++)
        {
            if (PaintTrailAt(Vector3.Lerp(from, to, i / (float)steps), true))
            {
                return true;
            }
        }

        return false;
    }

    private bool PaintTrailAt(Vector3 position, bool detonateMines)
    {
        if (exploded || !TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        Vector2Int index = TilePaintManager.Instance.GetGridIndex(position);
        if (index == lastPaintedIndex ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return false;
        }

        ClaimTile(index);
        lastPaintedIndex = index;
        if (detonateMines && BombMine.TryDetonateAt(index))
        {
            Explode(position);
            return true;
        }

        return false;
    }

    private void Explode(Vector3 center)
    {
        if (exploded)
        {
            return;
        }

        exploded = true;
        PaintExplosion(center);
        ReturnToPool();
    }

    private void PaintExplosion(Vector3 center)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        Vector2Int centerIndex = TilePaintManager.Instance.GetGridIndex(center);
        if (!TilePaintManager.Instance.IsInsideGrid(centerIndex) ||
            TilePaintManager.Instance.IsTileDestroyed(centerIndex))
        {
            return;
        }

        for (int x = -explosionPaintRadius; x <= explosionPaintRadius; x++)
        {
            for (int y = -explosionPaintRadius; y <= explosionPaintRadius; y++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(y) > explosionPaintRadius)
                {
                    continue;
                }

                Vector2Int index = centerIndex + new Vector2Int(x, y);
                if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                    TilePaintManager.Instance.IsTileDestroyed(index))
                {
                    continue;
                }

                explosionMineDetonationIndices.Add(index);
                ClaimTile(index);
            }
        }

        BombMine.DetonateAtIndices(explosionMineDetonationIndices);
        explosionMineDetonationIndices.Clear();
    }

    private void ClaimTile(Vector2Int index)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index) ||
            paintedIndices.Contains(index))
        {
            return;
        }

        paintedIndices.Add(index);

        if (NetworkTilePaintAuthority.HasInstance)
        {
            NetworkTilePaintAuthority.Instance.RequestClaimByGridIndex(index, owner);
            return;
        }

        if (GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, index))
        {
            return;
        }

        if (TileClaimService.HasInstance)
        {
            TileClaimService.Instance.TryClaimByGridIndexLocal(index, owner);
        }
        else
        {
            TilePaintManager.Instance.TryClaimByGridIndexLocal(index, owner);
        }
    }

    private void ApplyOwnerMaterial()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null || !TilePaintManager.HasInstance)
        {
            return;
        }

        Material material = TilePaintManager.Instance.GetOwnerMaterial(owner);
        if (material != null)
        {
            renderer.sharedMaterial = SolidColorMaterialUtility.Create(material, "_Projectile");
        }
    }

    private static PaintShellProjectile Rent()
    {
        while (Pool.Count > 0)
        {
            PaintShellProjectile pooled = Pool.Pop();
            if (pooled != null) return pooled;
        }

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Rigidbody body = obj.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        return obj.AddComponent<PaintShellProjectile>();
    }

    private void ReturnToPool()
    {
        gameObject.SetActive(false);
        if (Pool.Count < MaxPoolSize) Pool.Push(this);
        else Destroy(gameObject);
    }
}
