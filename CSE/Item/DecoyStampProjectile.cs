using System.Collections.Generic;
using UnityEngine;

public sealed class DecoyStampProjectile : MonoBehaviour
{
    private const int MaxPoolSize = 16;
    private static readonly Stack<DecoyStampProjectile> Pool = new Stack<DecoyStampProjectile>();
    private const float DefaultSpeed = 5.8f;
    private const float SpawnForwardOffset = 1.0f;
    private const float SpawnHeight = 0.7f;
    private const float ArriveDistance = 0.35f;
    private const float RetargetInterval = 0.85f;
    private const float TurnSpeed = 8f;
    private const int TargetSearchRadius = 5;
    private const int TargetSampleCount = 18;

    private PaintOwner owner;
    private Vector3 direction;
    private float speed;
    private float lifetime;
    private float collisionRadius;
    private bool finished;
    private float nextRetargetTime;
    private Vector2Int targetIndex;
    private Vector2Int lastPaintedIndex = new Vector2Int(int.MinValue, int.MinValue);
    private readonly HashSet<Vector2Int> paintedIndices = new HashSet<Vector2Int>();
    private readonly RaycastHit[] pathHits = new RaycastHit[16];
    private System.Random rng;
    private GameObject visualRoot;
    private bool usesCharacterVisual;

    public static DecoyStampProjectile Spawn(
        Vector3 position,
        Vector3 direction,
        PaintOwner owner,
        float duration,
        float radius,
        int paintRadius,
        GameObject sourceCharacter = null)
    {
        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = Vector3.forward;
        }

        flatDirection.Normalize();

        DecoyStampProjectile decoy = Rent();
        GameObject decoyObject = decoy.gameObject;
        decoyObject.name = "Decoy Stamp";
        decoyObject.SetActive(true);
        decoyObject.transform.position =
            position +
            flatDirection * SpawnForwardOffset +
            Vector3.up * (sourceCharacter != null ? 0f : SpawnHeight);
        decoyObject.transform.rotation = Quaternion.LookRotation(flatDirection);
        decoyObject.transform.localScale = Vector3.one;

        PlayerPaintIdentity identity = decoyObject.GetComponent<PlayerPaintIdentity>();
        identity.SetOwner(owner);

        decoy.Configure(
            owner,
            flatDirection,
            Mathf.Max(0.5f, duration),
            Mathf.Clamp(radius, 0.35f, 1.25f),
            Mathf.Max(0, paintRadius));
        decoy.ConfigureVisual(sourceCharacter);

        return decoy;
    }

    private void Configure(
        PaintOwner decoyOwner,
        Vector3 travelDirection,
        float activeSeconds,
        float hitRadius,
        int paintRadius)
    {
        owner = decoyOwner;
        finished = false;
        paintedIndices.Clear();
        lastPaintedIndex = new Vector2Int(int.MinValue, int.MinValue);
        direction = travelDirection;
        speed = DefaultSpeed;
        lifetime = activeSeconds;
        collisionRadius = hitRadius;
        rng = new System.Random(CreateSeed(transform.position, owner));
        PaintSingleTileAt(transform.position);
        ForceRetarget();
    }

    private void Update()
    {
        if (finished)
        {
            return;
        }

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Finish();
            return;
        }

        RetargetIfNeeded();
        Vector3 desiredDirection = GetDesiredDirection();
        if (desiredDirection.sqrMagnitude > 0.001f)
        {
            direction = Vector3.Slerp(
                direction,
                desiredDirection,
                Time.deltaTime * TurnSpeed).normalized;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        Vector3 nextPosition = transform.position + direction * (speed * Time.deltaTime);
        CollisionResult collision = CheckPathCollision(transform.position, nextPosition);
        if (collision == CollisionResult.BlockedByPlayer)
        {
            PaintSingleTileAt(nextPosition);
            Finish();
            return;
        }

        if (collision == CollisionResult.BlockedByWorld)
        {
            ForceRetarget();
            return;
        }

        PaintSingleTileAt(nextPosition);
        transform.position = nextPosition;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!finished && !ShouldIgnoreCollider(collision.collider))
        {
            if (IsPlayerCollider(collision.collider))
            {
                Finish();
                return;
            }

            ForceRetarget();
        }
    }

    private CollisionResult CheckPathCollision(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
        {
            return CollisionResult.None;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            from,
            collisionRadius,
            delta / distance,
            pathHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = pathHits[i].collider;
            if (ShouldIgnoreCollider(hitCollider))
            {
                continue;
            }

            return IsPlayerCollider(hitCollider)
                ? CollisionResult.BlockedByPlayer
                : CollisionResult.BlockedByWorld;
        }

        return HitMapBoundaryOrObstacle(to)
            ? CollisionResult.BlockedByWorld
            : CollisionResult.None;
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

        if (collider.GetComponentInParent<DecoyStampProjectile>() == this)
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

    private static bool IsPlayerCollider(Collider collider)
    {
        return collider != null &&
               collider.GetComponentInParent<PlayerPaintIdentity>() != null;
    }

    private void RetargetIfNeeded()
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        Vector2Int currentIndex =
            TilePaintManager.Instance.GetGridIndex(transform.position);
        if (Time.time >= nextRetargetTime ||
            TilePaintManager.Instance.IsTileDestroyed(targetIndex) ||
            currentIndex == targetIndex ||
            Vector3.Distance(
                transform.position,
                TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex)) <=
            ArriveDistance)
        {
            ForceRetarget();
        }
    }

    private void ForceRetarget()
    {
        nextRetargetTime = Time.time + RetargetInterval + NextRandomFloat(0f, 0.35f);

        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        Vector2Int currentIndex =
            TilePaintManager.Instance.GetGridIndex(transform.position);
        Vector2Int bestIndex = currentIndex;
        int bestScore = int.MinValue;

        for (int i = 0; i < TargetSampleCount; i++)
        {
            Vector2Int candidate = currentIndex + new Vector2Int(
                rng.Next(-TargetSearchRadius, TargetSearchRadius + 1),
                rng.Next(-TargetSearchRadius, TargetSearchRadius + 1));

            int score = ScoreTarget(currentIndex, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = candidate;
            }
        }

        if (bestScore > int.MinValue)
        {
            targetIndex = bestIndex;
        }
    }

    private int ScoreTarget(Vector2Int currentIndex, Vector2Int candidate)
    {
        if (!TilePaintManager.Instance.IsInsideGrid(candidate) ||
            TilePaintManager.Instance.IsTileDestroyed(candidate))
        {
            return int.MinValue;
        }

        if (GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(owner, candidate))
        {
            return int.MinValue;
        }

        int distance = Mathf.Abs(candidate.x - currentIndex.x) +
            Mathf.Abs(candidate.y - currentIndex.y);
        int score = distance;

        if (TilePaintManager.Instance.TryGetTileOwner(candidate, out PaintOwner tileOwner))
        {
            if (tileOwner == PaintOwner.Neutral)
            {
                score += 10;
            }
            else if (tileOwner != owner)
            {
                score += 24;
            }
            else
            {
                score -= 8;
            }
        }

        return score;
    }

    private Vector3 GetDesiredDirection()
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return direction;
        }

        Vector3 targetPosition =
            TilePaintManager.Instance.GetTileCenterWorldPosition(targetIndex);
        if (TilePaintManager.Instance.IsTileDestroyed(targetIndex))
        {
            return direction;
        }

        Vector3 desired = targetPosition - transform.position;
        desired.y = 0f;
        return desired.sqrMagnitude > 0.001f ? desired.normalized : direction;
    }

    private void PaintSingleTileAt(Vector3 position)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        Vector2Int centerIndex = TilePaintManager.Instance.GetGridIndex(position);
        if (centerIndex == lastPaintedIndex ||
            !TilePaintManager.Instance.IsInsideGrid(centerIndex) ||
            TilePaintManager.Instance.IsTileDestroyed(centerIndex))
        {
            return;
        }

        Vector2Int paintIndex = ResolveNextSingleTileIndex(centerIndex);
        if (!TilePaintManager.Instance.IsInsideGrid(paintIndex) ||
            TilePaintManager.Instance.IsTileDestroyed(paintIndex))
        {
            return;
        }

        ClaimTile(paintIndex);
        lastPaintedIndex = paintIndex;
    }

    private Vector2Int ResolveNextSingleTileIndex(Vector2Int targetIndex)
    {
        if (lastPaintedIndex.x == int.MinValue)
        {
            return targetIndex;
        }

        Vector2Int delta = targetIndex - lastPaintedIndex;
        if (delta == Vector2Int.zero)
        {
            return lastPaintedIndex;
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) && delta.x != 0)
        {
            return lastPaintedIndex + new Vector2Int(Sign(delta.x), 0);
        }

        return lastPaintedIndex + new Vector2Int(0, Sign(delta.y));
    }

    private static int Sign(int value)
    {
        if (value > 0)
        {
            return 1;
        }

        return value < 0 ? -1 : 0;
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

    private void Finish()
    {
        if (finished)
        {
            return;
        }

        finished = true;
        ReturnToPool();
    }

    private void ApplyOwnerMaterial()
    {
        if (usesCharacterVisual || visualRoot == null || !TilePaintManager.HasInstance)
        {
            return;
        }

        Material material = TilePaintManager.Instance.GetOwnerMaterial(owner);
        if (material == null)
        {
            return;
        }

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].sharedMaterial =
                    SolidColorMaterialUtility.Create(material, "_Projectile");
            }
        }
    }

    private void ConfigureVisual(GameObject sourceCharacter)
    {
        ClearVisual();

        Transform sourceRoot = ResolveVisualSourceRoot(sourceCharacter);
        if (sourceRoot != null)
        {
            visualRoot = CreateCharacterVisualClone(sourceRoot);
            usesCharacterVisual = visualRoot != null;
        }

        if (visualRoot == null)
        {
            visualRoot = CreateFallbackCapsuleVisual();
            usesCharacterVisual = false;
        }

        ApplyOwnerMaterial();
    }

    private void ClearVisual()
    {
        usesCharacterVisual = false;
        if (visualRoot == null)
        {
            return;
        }

        Destroy(visualRoot);
        visualRoot = null;
    }

    private static Transform ResolveVisualSourceRoot(GameObject sourceCharacter)
    {
        if (sourceCharacter == null)
        {
            return null;
        }

        Animator sourceAnimator =
            sourceCharacter.GetComponentInChildren<Animator>(true);
        return sourceAnimator != null ? sourceAnimator.transform : sourceCharacter.transform;
    }

    private GameObject CreateCharacterVisualClone(Transform sourceRoot)
    {
        Dictionary<Transform, Transform> transformMap =
            new Dictionary<Transform, Transform>();
        Transform cloneRoot = CloneTransformHierarchy(
            sourceRoot,
            transform,
            transformMap,
            true);
        if (cloneRoot == null)
        {
            return null;
        }

        cloneRoot.gameObject.name = "Decoy Character Model";
        CopyRenderers(sourceRoot, transformMap);
        CopyAnimator(sourceRoot, transformMap);
        SetLayerRecursively(cloneRoot.gameObject, sourceRoot.gameObject.layer);
        return cloneRoot.gameObject;
    }

    private static Transform CloneTransformHierarchy(
        Transform source,
        Transform parent,
        Dictionary<Transform, Transform> transformMap,
        bool isRoot)
    {
        if (source == null)
        {
            return null;
        }

        GameObject cloneObject = new GameObject(source.name);
        cloneObject.SetActive(source.gameObject.activeSelf);
        cloneObject.layer = source.gameObject.layer;

        Transform clone = cloneObject.transform;
        clone.SetParent(parent, false);
        clone.localPosition = isRoot ? Vector3.zero : source.localPosition;
        clone.localRotation = isRoot ? Quaternion.identity : source.localRotation;
        clone.localScale = source.localScale;
        transformMap[source] = clone;

        for (int i = 0; i < source.childCount; i++)
        {
            CloneTransformHierarchy(source.GetChild(i), clone, transformMap, false);
        }

        return clone;
    }

    private static void CopyRenderers(
        Transform sourceRoot,
        Dictionary<Transform, Transform> transformMap)
    {
        Renderer[] sourceRenderers =
            sourceRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            Renderer sourceRenderer = sourceRenderers[i];
            if (sourceRenderer == null ||
                sourceRenderer.GetComponentInParent<Canvas>() != null ||
                !transformMap.TryGetValue(sourceRenderer.transform, out Transform target))
            {
                continue;
            }

            if (sourceRenderer is SkinnedMeshRenderer sourceSkinned)
            {
                CopySkinnedRenderer(sourceSkinned, target.gameObject, transformMap);
                continue;
            }

            if (sourceRenderer is MeshRenderer sourceMeshRenderer)
            {
                CopyMeshRenderer(sourceMeshRenderer, target.gameObject);
            }
        }
    }

    private static void CopySkinnedRenderer(
        SkinnedMeshRenderer source,
        GameObject targetObject,
        Dictionary<Transform, Transform> transformMap)
    {
        SkinnedMeshRenderer target =
            targetObject.AddComponent<SkinnedMeshRenderer>();
        CopyRendererSettings(source, target);

        target.sharedMesh = source.sharedMesh;
        target.quality = source.quality;
        target.updateWhenOffscreen = true;
        target.localBounds = source.localBounds;

        Transform[] sourceBones = source.bones;
        Transform[] targetBones = new Transform[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
            Transform sourceBone = sourceBones[i];
            targetBones[i] =
                sourceBone != null &&
                transformMap.TryGetValue(sourceBone, out Transform targetBone)
                    ? targetBone
                    : null;
        }

        target.bones = targetBones;
        if (source.rootBone != null &&
            transformMap.TryGetValue(source.rootBone, out Transform rootBone))
        {
            target.rootBone = rootBone;
        }
    }

    private static void CopyMeshRenderer(
        MeshRenderer source,
        GameObject targetObject)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter != null)
        {
            MeshFilter targetFilter = targetObject.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;
        }

        MeshRenderer target = targetObject.AddComponent<MeshRenderer>();
        CopyRendererSettings(source, target);
    }

    private static void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.enabled = source.enabled;
        target.sharedMaterials = source.sharedMaterials;
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        source.GetPropertyBlock(propertyBlock);
        target.SetPropertyBlock(propertyBlock);
    }

    private static void CopyAnimator(
        Transform sourceRoot,
        Dictionary<Transform, Transform> transformMap)
    {
        Animator sourceAnimator = sourceRoot.GetComponent<Animator>();
        if (sourceAnimator == null ||
            !transformMap.TryGetValue(sourceRoot, out Transform cloneRoot))
        {
            return;
        }

        Animator cloneAnimator = cloneRoot.gameObject.AddComponent<Animator>();
        cloneAnimator.runtimeAnimatorController =
            sourceAnimator.runtimeAnimatorController;
        cloneAnimator.avatar = sourceAnimator.avatar;
        cloneAnimator.applyRootMotion = false;
        cloneAnimator.updateMode = sourceAnimator.updateMode;
        cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        cloneAnimator.enabled = sourceAnimator.enabled;

        if (!cloneAnimator.enabled ||
            cloneAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        cloneAnimator.Rebind();
        CopyAnimatorParameters(sourceAnimator, cloneAnimator);
        CopyAnimatorLayerStates(sourceAnimator, cloneAnimator);
        SetBoolParameterIfPresent(cloneAnimator, "isRun", true);
        cloneAnimator.Update(0f);
    }

    private static void CopyAnimatorParameters(Animator source, Animator target)
    {
        AnimatorControllerParameter[] parameters = source.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    target.SetBool(parameter.nameHash, source.GetBool(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Float:
                    target.SetFloat(parameter.nameHash, source.GetFloat(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Int:
                    target.SetInteger(parameter.nameHash, source.GetInteger(parameter.nameHash));
                    break;
            }
        }
    }

    private static void CopyAnimatorLayerStates(Animator source, Animator target)
    {
        int layerCount = Mathf.Min(source.layerCount, target.layerCount);
        for (int layer = 0; layer < layerCount; layer++)
        {
            target.SetLayerWeight(layer, source.GetLayerWeight(layer));
            AnimatorStateInfo state = source.GetCurrentAnimatorStateInfo(layer);
            target.Play(state.fullPathHash, layer, state.normalizedTime);
        }
    }

    private static void SetBoolParameterIfPresent(
        Animator animator,
        string parameterName,
        bool value)
    {
        int hash = Animator.StringToHash(parameterName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash &&
                parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(hash, value);
                return;
            }
        }
    }

    private GameObject CreateFallbackCapsuleVisual()
    {
        GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        fallback.name = "Decoy Fallback Capsule";
        fallback.transform.SetParent(transform, false);
        fallback.transform.localPosition = Vector3.zero;
        fallback.transform.localRotation = Quaternion.identity;
        fallback.transform.localScale = new Vector3(0.65f, 0.9f, 0.65f);

        Collider fallbackCollider = fallback.GetComponent<Collider>();
        if (fallbackCollider != null)
        {
            Destroy(fallbackCollider);
        }

        return fallback;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
        {
            return;
        }

        target.layer = layer;
        Transform targetTransform = target.transform;
        for (int i = 0; i < targetTransform.childCount; i++)
        {
            SetLayerRecursively(targetTransform.GetChild(i).gameObject, layer);
        }
    }

    private float NextRandomFloat(float min, float max)
    {
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    private static DecoyStampProjectile Rent()
    {
        while (Pool.Count > 0)
        {
            DecoyStampProjectile pooled = Pool.Pop();
            if (pooled != null) return pooled;
        }

        GameObject obj = new GameObject("Decoy Stamp");
        obj.AddComponent<PlayerPaintIdentity>();
        return obj.AddComponent<DecoyStampProjectile>();
    }

    private void ReturnToPool()
    {
        ClearVisual();
        gameObject.SetActive(false);
        if (Pool.Count < MaxPoolSize) Pool.Push(this);
        else Destroy(gameObject);
    }

    private static int CreateSeed(Vector3 position, PaintOwner owner)
    {
        unchecked
        {
            int seed = 17;
            seed = seed * 31 + Mathf.RoundToInt(position.x * 10f);
            seed = seed * 31 + Mathf.RoundToInt(position.z * 10f);
            seed = seed * 31 + (int)owner;
            return seed;
        }
    }

    private enum CollisionResult
    {
        None,
        BlockedByWorld,
        BlockedByPlayer
    }
}
