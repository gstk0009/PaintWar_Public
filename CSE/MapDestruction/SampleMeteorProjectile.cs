using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SampleMeteorProjectile : MonoBehaviour
{
    [SerializeField] private float defaultImpactRadius = 2.5f;
    [SerializeField] private float defaultFallSpeed = 8f;
    [SerializeField] private float spinDegreesPerSecond = 240f;
    [SerializeField] private float impactOffsetY = 0.15f;
    [SerializeField] private float maxLifetime = 8f;
    [SerializeField] private bool autoCreateMeteorVfx = true;
    [SerializeField] private Color fireColor = new Color(0.58f, 0.14f, 1f, 1f);
    [SerializeField] private Color emberColor = new Color(0.9f, 0.58f, 1f, 1f);
    [SerializeField] private Color smokeColor = new Color(0.2f, 0.07f, 0.28f, 0.62f);
    [SerializeField] private float trailTime = 0.85f;
    [SerializeField] private float trailStartWidth = 1.05f;
    [SerializeField] private float trailEndWidth = 0.08f;
    [SerializeField] private float impactVfxLifetime = 2.4f;
    [SerializeField] private float impactStunDuration = 2f;
    [SerializeField] private float safeTileClearancePadding = 0.35f;
    [SerializeField] private bool showImpactWarning = true;
    [SerializeField] private float impactWarningLeadTime = 1.1f;
    [SerializeField] private Color impactWarningColor = new Color(0.58f, 0.16f, 1f, 0.78f);
    [SerializeField] private float impactWarningTileFill = 0.92f;
    [SerializeField] private float impactWarningHeight = 0.035f;
    [SerializeField] private float impactWarningSurfaceOffset = 0.045f;
    [SerializeField] private float impactWarningPulseScale = 0.14f;

    private static Material fireMaterial;
    private static Material smokeMaterial;
    private static Material impactWarningMaterial;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly List<GameObject> ImpactTargets = new List<GameObject>(8);
    private static readonly HashSet<GameObject> ImpactTargetSet = new HashSet<GameObject>();
    private static readonly List<Vector2Int> ImpactTileIndices = new List<Vector2Int>(32);
    private static readonly Collider[] ObstacleOverlapBuffer = new Collider[48];
    private static readonly HashSet<GameObject> LocalObstacleSet = new HashSet<GameObject>();

    private Vector3 targetPosition;
    private float impactRadius;
    private float fallSpeed;
    private float aliveTime;
    private bool initialized;
    private bool impacted;
    private TrailRenderer fireTrail;
    private ParticleSystem flameParticles;
    private ParticleSystem smokeParticles;
    private GameObject impactWarningRoot;
    private MaterialPropertyBlock impactWarningProperties;
    private readonly List<Transform> impactWarningTiles = new List<Transform>();
    private readonly List<Renderer> impactWarningRenderers = new List<Renderer>();
    private readonly List<Vector2Int> impactWarningIndices = new List<Vector2Int>(32);
    private bool impactWarningShown;
    private bool applyGameplayEffects = true;

    public void Initialize(Vector3 impactPosition, float radius, float speed)
    {
        Initialize(impactPosition, radius, speed, true);
    }

    public void Initialize(Vector3 impactPosition, float radius, float speed, bool shouldApplyGameplayEffects)
    {
        targetPosition = impactPosition;
        impactRadius = radius > 0f ? radius : defaultImpactRadius;
        fallSpeed = speed > 0f ? speed : defaultFallSpeed;
        applyGameplayEffects = shouldApplyGameplayEffects;
        initialized = true;
    }

    private void Awake()
    {
        impactRadius = defaultImpactRadius;
        fallSpeed = defaultFallSpeed;
        EnsureMeteorVfx();
    }

    private void Update()
    {
        if (impacted)
        {
            return;
        }

        EnsureMeteorVfx();

        aliveTime += Time.deltaTime;
        if (aliveTime >= maxLifetime)
        {
            Impact(transform.position);
            return;
        }

        if (!initialized)
        {
            targetPosition = transform.position + Vector3.down * Mathf.Max(1f, transform.position.y);
            initialized = true;
        }

        transform.Rotate(Vector3.one, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        Vector3 target = targetPosition + Vector3.up * impactOffsetY;
        UpdateImpactWarning(targetPosition, target);
        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            fallSpeed * Time.deltaTime);

        if ((transform.position - target).sqrMagnitude <= 0.01f)
        {
            Impact(targetPosition);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (impacted)
        {
            return;
        }

        Vector3 impactPosition = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;
        Impact(impactPosition);
    }

    private void Impact(Vector3 impactPosition)
    {
        if (impacted)
        {
            return;
        }

        impacted = true;
        int neutralizedTiles = 0;
        int destroyedObstacles = 0;
        int displacedParticipants = 0;
        if (applyGameplayEffects)
        {
            ApplyGameplayAtWorldPosition(
                impactPosition,
                impactRadius,
                impactStunDuration,
                impactWarningIndices,
                out neutralizedTiles,
                out destroyedObstacles,
                out displacedParticipants);
        }
        else
        {
            DestroyLooseSceneObstaclesAtWorldPosition(
                impactPosition,
                impactRadius,
                impactWarningIndices);
        }

        Debug.Log(
            "[MeteorSample] Impact neutralized tiles=" + neutralizedTiles +
            " destroyedObstacles=" + destroyedObstacles + ".");
        if (displacedParticipants > 0)
        {
            Debug.Log("[MeteorSample] Impact stunned participants=" + displacedParticipants + ".");
        }

        DestroyImpactWarning();
        SpawnImpactVfx(impactPosition);
        Destroy(gameObject);
    }

    public static void ApplyGameplayAtWorldPosition(
        Vector3 impactPosition,
        float radius,
        float stunDuration,
        out int neutralizedTiles,
        out int destroyedObstacles,
        out int stunnedParticipants)
    {
        ApplyGameplayAtWorldPosition(
            impactPosition,
            radius,
            stunDuration,
            null,
            out neutralizedTiles,
            out destroyedObstacles,
            out stunnedParticipants);
    }

    private static void ApplyGameplayAtWorldPosition(
        Vector3 impactPosition,
        float radius,
        float stunDuration,
        IReadOnlyList<Vector2Int> preferredImpactIndices,
        out int neutralizedTiles,
        out int destroyedObstacles,
        out int stunnedParticipants)
    {
        neutralizedTiles = 0;
        destroyedObstacles = 0;
        stunnedParticipants = 0;
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        ImpactTileIndices.Clear();
        if (preferredImpactIndices != null && preferredImpactIndices.Count > 0)
        {
            for (int i = 0; i < preferredImpactIndices.Count; i++)
            {
                ImpactTileIndices.Add(preferredImpactIndices[i]);
            }
        }
        else
        {
            CollectImpactTileIndices(tileManager, impactPosition, radius, ImpactTileIndices);
        }

        for (int i = 0; i < ImpactTileIndices.Count; i++)
        {
            if (NeutralizeTile(tileManager, ImpactTileIndices[i]))
            {
                neutralizedTiles++;
            }
        }

        if (ObstacleFieldManager.HasInstance)
        {
            destroyedObstacles =
                ObstacleFieldManager.Instance.ServerDestroyAtGridIndicesIncludingUnbreakable(
                    ImpactTileIndices);
        }

        destroyedObstacles += DestroyLooseSceneObstacles(tileManager, ImpactTileIndices);
        stunnedParticipants = StunParticipantsOnImpactTiles(
            tileManager,
            ImpactTileIndices,
            stunDuration);
    }

    private static void CollectImpactTileIndices(
        TilePaintManager tileManager,
        Vector3 worldPosition,
        float radius,
        List<Vector2Int> output)
    {
        output.Clear();
        if (tileManager == null || !tileManager.IsGridReady || radius <= 0f)
        {
            return;
        }

        float radiusSqr = radius * radius;
        int tileRadius = Mathf.CeilToInt(radius / Mathf.Max(0.001f, tileManager.TileSize));
        Vector2Int centerIndex = tileManager.GetGridIndex(worldPosition);

        for (int x = centerIndex.x - tileRadius; x <= centerIndex.x + tileRadius; x++)
        {
            for (int y = centerIndex.y - tileRadius; y <= centerIndex.y + tileRadius; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                if (IsInsideImpactWarningArea(tileManager, index, worldPosition, radiusSqr))
                {
                    output.Add(index);
                }
            }
        }
    }

    private static bool NeutralizeTile(TilePaintManager tileManager, Vector2Int index)
    {
        if (tileManager == null ||
            !tileManager.TryGetTileOwner(index, out PaintOwner owner) ||
            owner == PaintOwner.Neutral)
        {
            return false;
        }

        bool networkRequestSent = false;
        if (NetworkTilePaintAuthority.HasInstance)
        {
            networkRequestSent =
                NetworkTilePaintAuthority.Instance.ServerForceSetOwnerIgnoringProtection(
                    index,
                    PaintOwner.Neutral);
        }

        if (!networkRequestSent ||
            tileManager.TryGetTileOwner(index, out PaintOwner currentOwner) &&
            currentOwner != PaintOwner.Neutral)
        {
            return tileManager.ForceSetTileOwnerIgnoringProtectionLocal(index, PaintOwner.Neutral);
        }

        return true;
    }

    private static int StunParticipantsOnImpactTiles(
        TilePaintManager tileManager,
        IReadOnlyList<Vector2Int> impactIndices,
        float stunDuration)
    {
        if (tileManager == null ||
            impactIndices == null ||
            impactIndices.Count == 0 ||
            stunDuration <= 0f)
        {
            return 0;
        }

        CollectImpactTargets();
        int stunnedCount = 0;
        for (int i = 0; i < ImpactTargets.Count; i++)
        {
            GameObject target = ImpactTargets[i];
            if (target == null)
            {
                continue;
            }

            PlayerPaintIdentity identity = target.GetComponent<PlayerPaintIdentity>();
            if (identity == null || identity.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            Vector2Int currentIndex = tileManager.GetGridIndex(target.transform.position);
            if (!ContainsGridIndex(impactIndices, currentIndex))
            {
                continue;
            }

            ApplyImpactStun(target, stunDuration);
            stunnedCount++;
        }

        ImpactTargets.Clear();
        ImpactTargetSet.Clear();
        return stunnedCount;
    }

    private static bool ContainsGridIndex(IReadOnlyList<Vector2Int> indices, Vector2Int target)
    {
        for (int i = 0; i < indices.Count; i++)
        {
            if (indices[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static int DestroyLooseSceneObstaclesAtWorldPosition(
        Vector3 impactPosition,
        float radius,
        IReadOnlyList<Vector2Int> preferredImpactIndices)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return 0;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        ImpactTileIndices.Clear();
        if (preferredImpactIndices != null && preferredImpactIndices.Count > 0)
        {
            for (int i = 0; i < preferredImpactIndices.Count; i++)
            {
                ImpactTileIndices.Add(preferredImpactIndices[i]);
            }
        }
        else
        {
            CollectImpactTileIndices(tileManager, impactPosition, radius, ImpactTileIndices);
        }

        return DestroyLooseSceneObstacles(tileManager, ImpactTileIndices);
    }

    private static int DestroyLooseSceneObstacles(
        TilePaintManager tileManager,
        IReadOnlyList<Vector2Int> impactIndices)
    {
        if (tileManager == null || impactIndices == null || impactIndices.Count == 0)
        {
            return 0;
        }

        LocalObstacleSet.Clear();
        float tileSize = Mathf.Max(0.001f, tileManager.TileSize);
        Vector3 halfExtents = new Vector3(tileSize * 0.49f, 2.5f, tileSize * 0.49f);
        for (int i = 0; i < impactIndices.Count; i++)
        {
            Vector2Int index = impactIndices[i];
            if (!tileManager.IsInsideGrid(index))
            {
                continue;
            }

            Vector3 center = tileManager.GetTileCenterWorldPosition(index);
            center.y += halfExtents.y;
            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                ObstacleOverlapBuffer,
                Quaternion.identity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                GameObject obstacle = GetMeteorAffectedObstacle(ObstacleOverlapBuffer[hitIndex]);
                if (obstacle != null)
                {
                    LocalObstacleSet.Add(obstacle);
                }
            }
        }

        int destroyedCount = 0;
        foreach (GameObject obstacle in LocalObstacleSet)
        {
            if (obstacle == null || !obstacle.activeSelf)
            {
                continue;
            }

            obstacle.SetActive(false);
            destroyedCount++;
        }

        LocalObstacleSet.Clear();
        return destroyedCount;
    }

    private static GameObject GetMeteorAffectedObstacle(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return null;
        }

        if (hitCollider.GetComponentInParent<PlayerPaintIdentity>() != null ||
            hitCollider.GetComponentInParent<SampleMeteorProjectile>() != null)
        {
            return null;
        }

        DestructibleObstacle destructible = hitCollider.GetComponentInParent<DestructibleObstacle>();
        if (destructible != null)
        {
            return destructible.gameObject;
        }

        GameObject candidate = hitCollider.attachedRigidbody != null
            ? hitCollider.attachedRigidbody.gameObject
            : hitCollider.gameObject;
        string candidateName = candidate.name;
        if (candidateName.Contains("MapObstacleCube") ||
            candidateName.Contains("UnbreakableObstacleCube") ||
            candidateName.Contains("Unbreakable Cube"))
        {
            return candidate;
        }

        return null;
    }

    private void OnDestroy()
    {
        DestroyImpactWarning();
    }

    private int DisplaceParticipantsFromImpact(Vector3 impactPosition)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return 0;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        CollectImpactTargets();

        int displacedCount = 0;
        for (int i = 0; i < ImpactTargets.Count; i++)
        {
            GameObject target = ImpactTargets[i];
            if (target == null)
            {
                continue;
            }

            PlayerPaintIdentity identity = target.GetComponent<PlayerPaintIdentity>();
            if (identity == null || identity.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            Transform targetTransform = target.transform;
            Vector2Int currentIndex = tileManager.GetGridIndex(targetTransform.position);
            if (!ShouldDisplaceParticipant(tileManager, currentIndex, impactPosition))
            {
                continue;
            }

            ApplyImpactStun(target);
            if (!TryFindNearestStandableTile(tileManager, targetTransform.position, impactPosition, out Vector2Int safeIndex))
            {
                displacedCount++;
                continue;
            }

            Vector3 safeCenter = tileManager.GetTileCenterWorldPosition(safeIndex);
            Vector3 safePosition = new Vector3(
                safeCenter.x,
                targetTransform.position.y,
                safeCenter.z);
            MoveParticipantToSafeTile(target, safePosition);
            displacedCount++;
        }

        ImpactTargets.Clear();
        ImpactTargetSet.Clear();
        return displacedCount;
    }

    private static void CollectImpactTargets()
    {
        ImpactTargets.Clear();
        ImpactTargetSet.Clear();

        if (GameRoundManager.HasInstance)
        {
            System.Collections.Generic.IReadOnlyList<GameParticipant> participants =
                GameRoundManager.Instance.Participants;
            for (int i = 0; i < participants.Count; i++)
            {
                GameParticipant participant = participants[i];
                if (participant == null ||
                    participant.IsEliminated ||
                    participant.Transform == null)
                {
                    continue;
                }

                AddImpactTarget(participant.Transform.gameObject);
            }
        }

        PlayerPaintIdentity[] identities = FindObjectsByType<PlayerPaintIdentity>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < identities.Length; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity == null || identity.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            AddImpactTarget(identity.gameObject);
        }
    }

    private static void AddImpactTarget(GameObject target)
    {
        if (target == null || ImpactTargetSet.Contains(target))
        {
            return;
        }

        ImpactTargetSet.Add(target);
        ImpactTargets.Add(target);
    }

    private bool ShouldDisplaceParticipant(
        TilePaintManager tileManager,
        Vector2Int currentIndex,
        Vector3 impactPosition)
    {
        if (!tileManager.IsInsideGrid(currentIndex))
        {
            return false;
        }

        if (ImpactTileIndices.Contains(currentIndex))
        {
            return true;
        }

        return IsInsideImpactWarningArea(
            tileManager,
            currentIndex,
            impactPosition,
            impactRadius * impactRadius);
    }

    private bool TryFindNearestStandableTile(
        TilePaintManager tileManager,
        Vector3 participantPosition,
        Vector3 impactPosition,
        out Vector2Int safeIndex)
    {
        safeIndex = Vector2Int.zero;
        float bestDistanceSqr = float.MaxValue;
        float clearanceRadius = impactRadius +
            Mathf.Max(0f, safeTileClearancePadding) * Mathf.Max(0.001f, tileManager.TileSize);
        float clearanceRadiusSqr = clearanceRadius * clearanceRadius;
        Vector2 participant2D = new Vector2(participantPosition.x, participantPosition.z);
        Vector2 impact2D = new Vector2(impactPosition.x, impactPosition.z);

        for (int x = 0; x < tileManager.GridSize; x++)
        {
            for (int y = 0; y < tileManager.GridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
                Vector2 tile2D = new Vector2(tileCenter.x, tileCenter.z);
                if ((tile2D - impact2D).sqrMagnitude <= clearanceRadiusSqr)
                {
                    continue;
                }

                float distanceSqr = (tile2D - participant2D).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                safeIndex = index;
            }
        }

        return bestDistanceSqr < float.MaxValue;
    }

    private static void MoveParticipantToSafeTile(GameObject participant, Vector3 safePosition)
    {
        if (participant == null)
        {
            return;
        }

        PlayerMovement playerMovement = participant.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.ApplyExternalTileDisplacement(safePosition);
        }
        else
        {
            ApplyTransformDisplacement(participant, safePosition);
        }

        NetworkBotController networkBot = participant.GetComponent<NetworkBotController>();
        if (networkBot != null)
        {
            networkBot.ResetAfterExternalTileDisplacement();
        }

        BotPlayerController localBot = participant.GetComponent<BotPlayerController>();
        if (localBot != null)
        {
            localBot.ResetAfterExternalTileDisplacement();
        }
    }

    private static void ApplyTransformDisplacement(GameObject participant, Vector3 safePosition)
    {
        CharacterController controller = participant.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }

        participant.transform.position = safePosition;

        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }

        FishNet.Component.Transforming.NetworkTransform networkTransform =
            participant.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
        if (networkTransform != null)
        {
            networkTransform.Teleport();
            networkTransform.ForceSend();
        }
    }

    private void ApplyImpactStun(GameObject participant)
    {
        ApplyImpactStun(participant, impactStunDuration);
    }

    private static void ApplyImpactStun(GameObject participant, float stunDuration)
    {
        if (participant == null || stunDuration <= 0f)
        {
            return;
        }

        PlayerStatusEffectController statusEffects =
            participant.GetComponent<PlayerStatusEffectController>();
        if (statusEffects == null)
        {
            statusEffects = participant.AddComponent<PlayerStatusEffectController>();
        }

        StatusEffectSpec effect = new StatusEffectSpec(
            StatusEffectType.Root,
            0f,
            stunDuration,
            StatusEffectStackMode.RefreshDuration);
        statusEffects.ApplyStatusEffect(effect);

        PlayerPaintIdentity identity = participant.GetComponent<PlayerPaintIdentity>();
        if (identity != null &&
            identity.Owner != PaintOwner.Neutral &&
            NetworkWeaponStatusEffectSync.HasInstance)
        {
            NetworkWeaponStatusEffectSync.Instance.BroadcastEffectToOwner(identity.Owner, effect);
        }
    }

    private void EnsureMeteorVfx()
    {
        if (!autoCreateMeteorVfx || fireTrail != null)
        {
            return;
        }

        fireTrail = gameObject.AddComponent<TrailRenderer>();
        fireTrail.time = trailTime;
        fireTrail.startWidth = trailStartWidth;
        fireTrail.endWidth = trailEndWidth;
        fireTrail.minVertexDistance = 0.08f;
        fireTrail.alignment = LineAlignment.View;
        fireTrail.textureMode = LineTextureMode.Stretch;
        fireTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fireTrail.receiveShadows = false;
        fireTrail.material = GetFireMaterial();
        fireTrail.colorGradient = CreateGradient(
            emberColor,
            fireColor,
            new Color(fireColor.r, fireColor.g, fireColor.b, 0f));

        flameParticles = CreateLoopingParticles(
            "Meteor Fire Trail",
            transform,
            Vector3.zero,
            58f,
            new ParticleSystem.MinMaxCurve(0.28f, 0.62f),
            new ParticleSystem.MinMaxCurve(0.25f, 0.72f),
            new ParticleSystem.MinMaxCurve(0.4f, 1.4f),
            CreateGradient(emberColor, fireColor, new Color(0.18f, 0.02f, 0.32f, 0f)),
            GetFireMaterial());

        smokeParticles = CreateLoopingParticles(
            "Meteor Smoke Trail",
            transform,
            Vector3.zero,
            28f,
            new ParticleSystem.MinMaxCurve(0.55f, 1.05f),
            new ParticleSystem.MinMaxCurve(0.28f, 0.72f),
            new ParticleSystem.MinMaxCurve(0.25f, 0.85f),
            CreateGradient(
                new Color(0.42f, 0.12f, 0.62f, 0.68f),
                smokeColor,
                new Color(smokeColor.r, smokeColor.g, smokeColor.b, 0f)),
            GetSmokeMaterial());

        Light glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = fireColor;
        glow.range = 4.5f;
        glow.intensity = 2.2f;
    }

    private void SpawnImpactVfx(Vector3 impactPosition)
    {
        if (!autoCreateMeteorVfx)
        {
            return;
        }

        GameObject root = new GameObject("Meteor Impact VFX");
        root.transform.position = impactPosition + Vector3.up * 0.12f;

        ParticleSystem fireBurst = CreateBurstParticles(
            "Meteor Impact Fire",
            root.transform,
            42,
            new ParticleSystem.MinMaxCurve(0.35f, 0.7f),
            new ParticleSystem.MinMaxCurve(0.35f, 0.9f),
            new ParticleSystem.MinMaxCurve(2.6f, 5.4f),
            CreateGradient(emberColor, fireColor, new Color(fireColor.r, fireColor.g, fireColor.b, 0f)),
            GetFireMaterial());
        fireBurst.Play(true);

        ParticleSystem smokeBurst = CreateBurstParticles(
            "Meteor Impact Smoke",
            root.transform,
            54,
            new ParticleSystem.MinMaxCurve(1.1f, 2.1f),
            new ParticleSystem.MinMaxCurve(0.42f, 1.08f),
            new ParticleSystem.MinMaxCurve(1.2f, 3.1f),
            CreateGradient(
                new Color(0.46f, 0.12f, 0.68f, 0.78f),
                smokeColor,
                new Color(smokeColor.r, smokeColor.g, smokeColor.b, 0f)),
            GetSmokeMaterial());
        smokeBurst.Play(true);

        Light flash = root.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = fireColor;
        flash.range = 6f;
        flash.intensity = 3.8f;

        Destroy(root, impactVfxLifetime);
    }

    private void UpdateImpactWarning(Vector3 impactPosition, Vector3 flightTarget)
    {
        if (!showImpactWarning || impactWarningLeadTime <= 0f)
        {
            return;
        }

        float remainingTime = fallSpeed > 0f
            ? Vector3.Distance(transform.position, flightTarget) / fallSpeed
            : 0f;

        if (!impactWarningShown && remainingTime <= impactWarningLeadTime)
        {
            ShowImpactWarning(impactPosition);
        }

        if (impactWarningShown)
        {
            PulseImpactWarning(remainingTime);
        }
    }

    private void ShowImpactWarning(Vector3 impactPosition)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        impactWarningShown = true;
        impactWarningIndices.Clear();
        TilePaintManager tileManager = TilePaintManager.Instance;
        float tileSize = Mathf.Max(0.001f, tileManager.TileSize);
        float radiusSqr = impactRadius * impactRadius;
        int indexRadius = Mathf.CeilToInt(impactRadius / tileSize) + 1;
        Vector2Int centerIndex = tileManager.GetGridIndex(impactPosition);
        float surfaceY = tileManager.GetTileSurfaceY(centerIndex);

        impactWarningRoot = new GameObject("Meteor Impact Warning");
        for (int x = centerIndex.x - indexRadius; x <= centerIndex.x + indexRadius; x++)
        {
            for (int y = centerIndex.y - indexRadius; y <= centerIndex.y + indexRadius; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                if (!IsInsideImpactWarningArea(tileManager, index, impactPosition, radiusSqr))
                {
                    continue;
                }

                Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
                impactWarningIndices.Add(index);
                CreateImpactWarningTile(tileCenter, surfaceY, tileSize);
            }
        }
    }

    private static bool IsInsideImpactWarningArea(
        TilePaintManager tileManager,
        Vector2Int index,
        Vector3 impactPosition,
        float radiusSqr)
    {
        Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
        Vector2 tile2D = new Vector2(tileCenter.x, tileCenter.z);
        Vector2 impact2D = new Vector2(impactPosition.x, impactPosition.z);
        return (tile2D - impact2D).sqrMagnitude <= radiusSqr;
    }

    private void CreateImpactWarningTile(Vector3 tileCenter, float surfaceY, float tileSize)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = "Meteor Impact Warning Tile";
        tile.transform.SetParent(impactWarningRoot.transform, false);
        tileCenter.y = surfaceY + impactWarningSurfaceOffset;
        tile.transform.position = tileCenter;
        tile.transform.localScale = new Vector3(
            tileSize * Mathf.Clamp(impactWarningTileFill, 0.05f, 1.5f),
            Mathf.Max(0.001f, impactWarningHeight),
            tileSize * Mathf.Clamp(impactWarningTileFill, 0.05f, 1.5f));

        Collider collider = tile.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            DestroyRuntimeObject(collider);
        }

        Renderer renderer = tile.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetImpactWarningMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            impactWarningRenderers.Add(renderer);
            SetImpactWarningColor(renderer, impactWarningColor);
        }

        impactWarningTiles.Add(tile.transform);
    }

    private void PulseImpactWarning(float remainingTime)
    {
        float normalizedTime = 1f - Mathf.Clamp01(remainingTime / Mathf.Max(0.001f, impactWarningLeadTime));
        float flash = (Mathf.Sin(Time.time * Mathf.Lerp(8f, 18f, normalizedTime)) + 1f) * 0.5f;
        float pulseScale = 1f + flash * Mathf.Max(0f, impactWarningPulseScale);
        Color color = Color.Lerp(impactWarningColor, new Color(0.86f, 0.28f, 1f, 0.96f), normalizedTime * 0.75f + flash * 0.25f);

        for (int i = 0; i < impactWarningTiles.Count; i++)
        {
            Transform tile = impactWarningTiles[i];
            if (tile != null)
            {
                tile.localScale = new Vector3(
                    tile.localScale.x,
                    Mathf.Max(0.001f, impactWarningHeight) * pulseScale,
                    tile.localScale.z);
            }
        }

        for (int i = 0; i < impactWarningRenderers.Count; i++)
        {
            if (impactWarningRenderers[i] != null)
            {
                SetImpactWarningColor(impactWarningRenderers[i], color);
            }
        }
    }

    private void DestroyImpactWarning()
    {
        impactWarningTiles.Clear();
        impactWarningRenderers.Clear();
        impactWarningIndices.Clear();
        if (impactWarningRoot != null)
        {
            DestroyRuntimeObject(impactWarningRoot);
            impactWarningRoot = null;
        }
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void SetImpactWarningColor(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        if (impactWarningProperties == null)
        {
            impactWarningProperties = new MaterialPropertyBlock();
        }

        targetRenderer.GetPropertyBlock(impactWarningProperties);
        impactWarningProperties.SetColor(BaseColorPropertyId, color);
        impactWarningProperties.SetColor(ColorPropertyId, color);
        targetRenderer.SetPropertyBlock(impactWarningProperties);
    }

    private ParticleSystem CreateLoopingParticles(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        float emissionRate,
        ParticleSystem.MinMaxCurve lifetime,
        ParticleSystem.MinMaxCurve size,
        ParticleSystem.MinMaxCurve speed,
        Gradient color,
        Material material)
    {
        ParticleSystem particles = CreateParticleSystem(objectName, parent, localPosition, material);
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = lifetime;
        main.startSize = size;
        main.startSpeed = speed;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = emissionRate;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.28f;

        ApplyColorOverLifetime(particles, color);
        particles.Play(true);
        return particles;
    }

    private ParticleSystem CreateBurstParticles(
        string objectName,
        Transform parent,
        short burstCount,
        ParticleSystem.MinMaxCurve lifetime,
        ParticleSystem.MinMaxCurve size,
        ParticleSystem.MinMaxCurve speed,
        Gradient color,
        Material material)
    {
        ParticleSystem particles = CreateParticleSystem(objectName, parent, Vector3.zero, material);
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.duration = 0.35f;
        main.startLifetime = lifetime;
        main.startSize = size;
        main.startSpeed = speed;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(64, burstCount * 2);

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = Mathf.Max(0.35f, impactRadius * 0.35f);
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ApplyColorOverLifetime(particles, color);
        return particles;
    }

    private static ParticleSystem CreateParticleSystem(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Material material)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;

        ParticleSystem particles = obj.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystemRenderer particleRenderer =
            obj.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.material = material;
        particleRenderer.sortingFudge = 1f;
        return particles;
    }

    private static void ApplyColorOverLifetime(
        ParticleSystem particles,
        Gradient color)
    {
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
            particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = color;
    }

    private static Gradient CreateGradient(Color start, Color middle, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(middle, 0.35f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(middle.a, 0.35f),
                new GradientAlphaKey(end.a, 1f)
            });
        return gradient;
    }

    private static Material GetFireMaterial()
    {
        if (fireMaterial == null)
        {
            fireMaterial = CreateParticleMaterial(
                "Runtime Meteor Fire Material",
                new Color(0.64f, 0.16f, 1f, 1f));
        }

        return fireMaterial;
    }

    private static Material GetSmokeMaterial()
    {
        if (smokeMaterial == null)
        {
            smokeMaterial = CreateParticleMaterial(
                "Runtime Meteor Smoke Material",
                new Color(0.24f, 0.07f, 0.34f, 0.65f));
        }

        return smokeMaterial;
    }

    private static Material GetImpactWarningMaterial()
    {
        if (impactWarningMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            impactWarningMaterial = new Material(shader);
            impactWarningMaterial.name = "Runtime Meteor Impact Warning Material";
            impactWarningMaterial.color = new Color(0.58f, 0.16f, 1f, 0.78f);
            impactWarningMaterial.renderQueue = 3000;
            if (impactWarningMaterial.HasProperty("_Surface"))
            {
                impactWarningMaterial.SetFloat("_Surface", 1f);
            }

            if (impactWarningMaterial.HasProperty("_SrcBlend"))
            {
                impactWarningMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (impactWarningMaterial.HasProperty("_DstBlend"))
            {
                impactWarningMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (impactWarningMaterial.HasProperty("_ZWrite"))
            {
                impactWarningMaterial.SetFloat("_ZWrite", 0f);
            }

            impactWarningMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return impactWarningMaterial;
    }

    private static Material CreateParticleMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        return material;
    }
}
