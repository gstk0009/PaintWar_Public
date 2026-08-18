using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed partial class BombMine : MonoBehaviour
{
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
    private static readonly int SurfacePropertyId = Shader.PropertyToID("_Surface");
    private static readonly int BlendPropertyId = Shader.PropertyToID("_Blend");
    private const int MaxPoolSize = 16;
    private const int MaxChainLinkPoolSize = 72;
    private const int MaxComboDepth = 3;
    private const int LevelFourCornerFillRadius = 1;
    private const int LevelFiveOuterCornerOffset = 2;
    private const int ComboEndpointBurstRadius = 1;
    private const float SurfaceOffset = 0f;
    private const float PrimitiveBaseScaleXz = 0.52f;
    private const float PrimitiveBaseScaleY = 0.22f;
    private const float BlockingColliderTileFill = 0.94f;
    private const float BlockingColliderHeightRatio = 0.45f;
    private const float BlockingOccupancyCheckInterval = 0.08f;
    private const float BlockingOccupancyCheckHalfExtentTiles = 1.1f;
    private const float BlockingOtherTileArrivalToleranceRatio = 0.18f;
    private const float ChainLinkAnchorHeightRatio = 0.32f;
    private const float ChainLinkArcHeightRatio = 0.18f;
    private const int ChainLinkLightningPointCount = 7;
    private const float ChainLinkLightningJitterRatio = 0.22f;
    private const float ChainLinkEmissionIntensity = 10f;
    private const float ChainLinkReferenceEmissionIntensity = 30f;
    private const float ChainLineOwnerWhiteBlendScale = 0.22f;
    private const float ChainLineOwnerMinSaturation = 0.78f;
    private const float ScheduledChainLinkWidthMultiplier = 5.4f;
    private const int ComboGlowSegmentCount = 40;
    private const float ComboGlowHeightRatio = 0.22f;

    private const float OwnerBodyEmissionScale = 0.55f;
    private const float ComboOwnerBodyEmissionScale = 1.75f;
    private const float ComboEffectMinIntensity = 4.4f;
    private const float ComboEffectMaxIntensity = 8.5f;
    private static readonly Color BodyColor = new Color(0.08f, 0.085f, 0.09f, 1f);
    private static readonly Color NeutralAccentColor = new Color(0.8f, 0.82f, 0.86f, 1f);
    private static readonly Color[] ComboGlowDepthColors =
    {
        new Color(0.86f, 0.38f, 1f, 1f),
        new Color(0.3f, 0.72f, 1f, 1f),
        new Color(0.33f, 1f, 0.45f, 1f),
        new Color(1f, 0.62f, 0.16f, 1f),
        new Color(1f, 0.26f, 0.28f, 1f),
        new Color(1f, 0.22f, 0.86f, 1f)
    };
    private const float WarningLeadSeconds = 1.25f;
    private const float WarningSurfaceOffset = 0.055f;
    private const float WarningOutlineTileFill = 0.88f;
    private const float WarningOutlineWidthRatio = 0.09f;
    private const float WarningOutlineDarkenBlend = 0.32f;
    private static readonly List<BombMine> ActiveBombs = new List<BombMine>(16);
    private static readonly Stack<BombMine> Pool = new Stack<BombMine>();
    private static readonly List<ChainLinkVisual> ActiveChainLinks =
        new List<ChainLinkVisual>(48);
    private static readonly Stack<ChainLinkVisual> ChainLinkPool =
        new Stack<ChainLinkVisual>(48);
    private static readonly List<BombMine> PreviewComboFrontier =
        new List<BombMine>(16);
    private static readonly List<Vector2Int> OwnedTileCandidates = new List<Vector2Int>(128);
    private static readonly Collider[] BlockingOccupancyHits = new Collider[24];
    private static Material bodyMaterial;
    private static GameObject sharedVisualPrefab;
    private static BombMineSettings activeSettings;
    private static float chainExplosionDelaySeconds =
        BombMineSettings.DefaultChainExplosionDelaySeconds;
    private static float chainLinkWidthRatio =
        BombMineSettings.DefaultChainLinkWidthRatio;
    private static float chainLinkPulseSpeed =
        BombMineSettings.DefaultChainLinkPulseSpeed;
    private static float comboGlowWidthRatio =
        BombMineSettings.DefaultComboGlowWidthRatio;
    private static float comboGlowPulseSpeed =
        BombMineSettings.DefaultComboGlowPulseSpeed;
    private static int lastChainLinkVisualUpdateFrame = -1;
    private static Material chainLightningMaterial;
    private static MaterialPropertyBlock chainLinkMaterialProperties;
    private static int neutralizeOwnedTilesOnHit =
        BombMineSettings.DefaultNeutralizeOwnedTilesOnHit;

    public static event Action<PaintOwner, int, Vector2Int> ChainResolved;
    public static event Action<PaintOwner> MineExploded;

    private PaintOwner owner;
    private PaintOwner originalOwner;
    private PaintOwner chargeOwner;
    private Vector2Int gridIndex;
    private int paintRadius = 1;
    private int mineLevel = 1;
    private float fuseDuration = BombMineSettings.DefaultFuseSeconds;
    private float explodeAt;
    private bool armed;
    private ChainContext scheduledChainContext;
    private int scheduledComboDepth;
    private float scheduledExplodeAt;
    private Vector3 scheduledChainSourcePosition;
    private PaintOwner scheduledChainSourceOwner;
    private LineRenderer scheduledChainLinkRenderer;
    private LineRenderer comboGlowRenderer;
    private LineRenderer comboGlowOuterRenderer;
    private bool comboGlowVisible;
    private int previewComboDepth;
    private bool warningVisible;
    private int warningComboDepth;
    private Renderer cachedRenderer;
    private Renderer accentRenderer;
    private GameObject visualInstance;
    private Renderer[] visualRenderers;
    private ParticleSystem[] emphasisParticles;
    private ParticleSystemRenderer[] emphasisParticleRenderers;
    private ParticleSystem.MinMaxGradient[] emphasisStartColors;
    private ParticleSystem.MinMaxGradient[] emphasisLifetimeColors;
    private float[] emphasisStartSizeMultipliers;
    private float[] emphasisStartSpeedMultipliers;
    private float[] emphasisEmissionRateOverTimeMultipliers;
    private float[] emphasisEmissionRateOverDistanceMultipliers;
    private bool[] emphasisLifetimeEnabled;
    private Collider blockingCollider;
    private bool blockingColliderArmed;
    private float nextBlockingOccupancyCheckAt;
    private MaterialPropertyBlock warningMaterialProperties;
    private MaterialPropertyBlock comboGlowMaterialProperties;
    private readonly List<LineRenderer> warningMarkers = new List<LineRenderer>(5);
    private readonly List<Vector2Int> warningExplosionIndices = new List<Vector2Int>(17);
    private readonly List<Vector2Int> resolvedExplosionIndices = new List<Vector2Int>(17);

    public static BombMine Place(
        Vector3 worldPosition,
        PaintOwner mineOwner,
        float fuseSeconds,
        int crossRadius,
        int ownerLevel = 0,
        int maxActiveMines = 0)
    {
        if (!TryResolvePlacement(worldPosition, mineOwner, maxActiveMines, out Vector2Int index))
        {
            return null;
        }

        BombMine mine = Rent();
        mine.gameObject.name = "Bomb Mine";
        mine.gameObject.SetActive(true);
        mine.Configure(
            mineOwner,
            index,
            Mathf.Max(
                0.1f,
                fuseSeconds <= 0f
                    ? BombMineSettings.DefaultFuseSeconds
                    : fuseSeconds),
            Mathf.Max(1, crossRadius),
            ownerLevel > 0 ? ownerLevel : GetOwnerLevel(mineOwner));
        return mine;
    }

    public static bool CanPlace(
        Vector3 worldPosition,
        PaintOwner mineOwner,
        int maxActiveMines = 0)
    {
        return TryResolvePlacement(worldPosition, mineOwner, maxActiveMines, out Vector2Int _);
    }

    public static int GetActiveMineCount(PaintOwner mineOwner)
    {
        return mineOwner == PaintOwner.Neutral ? 0 : CountActiveMines(mineOwner);
    }

    public static bool IsBlockingTile(Vector2Int index)
    {
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null &&
                mine.armed &&
                mine.gridIndex == index)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryDetonateAt(Vector2Int index, bool forceSingleCombo = false)
    {
        for (int i = ActiveBombs.Count - 1; i >= 0; i--)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null ||
                !mine.armed ||
                mine.gridIndex != index)
            {
                continue;
            }

            if (forceSingleCombo)
            {
                mine.ExplodeAsSingleCombo();
            }
            else
            {
                mine.Explode(null);
            }
            return true;
        }

        return false;
    }

    public static int DetonateAtIndices(IEnumerable<Vector2Int> indices, bool forceSingleCombo = false)
    {
        if (indices == null)
        {
            return 0;
        }

        int detonatedCount = 0;
        foreach (Vector2Int index in indices)
        {
            if (TryDetonateAt(index, forceSingleCombo))
            {
                detonatedCount++;
            }
        }

        return detonatedCount;
    }

    public static int ConvertMinesAlongTeleportPath(
        Vector3 startWorldPosition,
        Vector3 endWorldPosition,
        PaintOwner newOwner)
    {
        if (newOwner == PaintOwner.Neutral ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return 0;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        float distance = Vector3.Distance(startWorldPosition, endWorldPosition);
        float sampleDistance = Mathf.Max(0.05f, tileManager.TileSize * 0.25f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / sampleDistance));
        int convertedCount = 0;
        Vector2Int previousIndex = new Vector2Int(int.MinValue, int.MinValue);

        for (int i = 0; i <= steps; i++)
        {
            Vector3 sample = Vector3.Lerp(startWorldPosition, endWorldPosition, i / (float)steps);
            Vector2Int sampleIndex = tileManager.GetGridIndex(sample);
            if (sampleIndex == previousIndex)
            {
                continue;
            }

            previousIndex = sampleIndex;
            convertedCount += ConvertMineAt(sampleIndex, newOwner) ? 1 : 0;
        }

        return convertedCount;
    }

    public static bool TryFindStealableMineOnTeleportLine(
        PaintOwner newOwner, Vector3 fromWorldPosition, int maxTileSteps, out Vector3 mineWorldPosition)
    {
        mineWorldPosition = Vector3.zero;
        if (newOwner == PaintOwner.Neutral || !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady) return false;

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int from = tileManager.GetGridIndex(fromWorldPosition);
        int bestSteps = int.MaxValue;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null || !mine.armed || mine.owner == newOwner) continue;
            Vector2Int delta = mine.gridIndex - from;
            int x = Mathf.Abs(delta.x);
            int y = Mathf.Abs(delta.y);
            bool aligned = x == 0 || y == 0 || x == y;
            int steps = Mathf.Max(x, y);
            // 폭탄 칸은 blocking이므로 착지점이 아니라 텔레포트 도중에 통과해야 합니다.
            if (!aligned || steps <= 0 || steps >= maxTileSteps || steps >= bestSteps) continue;
            bestSteps = steps;
            mineWorldPosition = tileManager.GetTileCenterWorldPosition(mine.gridIndex);
        }
        return bestSteps != int.MaxValue;
    }

    public static bool HasStealableMineOnTeleportLine(
        PaintOwner newOwner, Vector2Int fromIndex, int maxTileSteps)
    {
        if (newOwner == PaintOwner.Neutral) return false;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null || !mine.armed || mine.owner == newOwner) continue;
            Vector2Int delta = mine.gridIndex - fromIndex;
            int x = Mathf.Abs(delta.x);
            int y = Mathf.Abs(delta.y);
            int steps = Mathf.Max(x, y);
            if ((x == 0 || y == 0 || x == y) && steps > 0 && steps < maxTileSteps) return true;
        }
        return false;
    }

    public static bool IsEnemyBlastDangerTile(Vector2Int index, PaintOwner observerOwner)
    {
        if (observerOwner == PaintOwner.Neutral) return false;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null || !mine.armed || mine.owner == observerOwner) continue;
            Vector2Int delta = index - mine.gridIndex;
            Vector2Int direction;
            int distance;
            if (delta.x == 0)
            {
                direction = delta.y >= 0 ? Vector2Int.up : Vector2Int.down;
                distance = Mathf.Abs(delta.y);
            }
            else if (delta.y == 0)
            {
                direction = delta.x >= 0 ? Vector2Int.right : Vector2Int.left;
                distance = Mathf.Abs(delta.x);
            }
            else continue;

            if (distance == 0 || distance > mine.paintRadius) continue;
            bool blocked = false;
            for (int step = 1; step < distance; step++)
            {
                Vector2Int sample = mine.gridIndex + direction * step;
                if (WeaponPatternTraversal.IsUnbreakable(sample) ||
                    WeaponPatternTraversal.IsBreakable(sample))
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked) return true;
        }
        return false;
    }

    public static void SetSharedVisualPrefab(GameObject visualPrefab)
    {
        if (visualPrefab != null)
        {
            sharedVisualPrefab = visualPrefab;
        }
    }

    public static void ApplySettings(BombMineSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        activeSettings = settings;
        SetSharedVisualPrefab(settings.VisualPrefab);
        chainExplosionDelaySeconds = settings.ChainExplosionDelaySeconds;
        chainLinkWidthRatio = settings.ChainLinkWidthRatio;
        chainLinkPulseSpeed = settings.ChainLinkPulseSpeed;
        comboGlowWidthRatio = settings.ComboGlowWidthRatio;
        comboGlowPulseSpeed = settings.ComboGlowPulseSpeed;
        neutralizeOwnedTilesOnHit = settings.NeutralizeOwnedTilesOnHit;
    }

    public static bool TryGetNextRechargeRemaining(
        PaintOwner mineOwner,
        out float remainingSeconds)
    {
        remainingSeconds = 0f;
        if (mineOwner == PaintOwner.Neutral)
        {
            return false;
        }

        float shortestRemaining = float.MaxValue;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null ||
                !mine.armed ||
                mine.chargeOwner != mineOwner)
            {
                continue;
            }

            shortestRemaining = Mathf.Min(
                shortestRemaining,
                mine.GetScheduledExplosionTime() - Time.time);
        }

        if (shortestRemaining == float.MaxValue)
        {
            return false;
        }

        remainingSeconds = Mathf.Max(0f, shortestRemaining);
        return true;
    }

    public static void ClearAllActive()
    {
        for (int i = ActiveBombs.Count - 1; i >= 0; i--)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null)
            {
                mine.ReturnToPool();
            }
        }

        ActiveBombs.Clear();
        ClearChainLinkVisuals();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetChainLightningRuntimeState()
    {
        if (chainLightningMaterial != null)
        {
            Destroy(chainLightningMaterial);
            chainLightningMaterial = null;
        }
    }

    private void Configure(
        PaintOwner mineOwner,
        Vector2Int mineIndex,
        float fuseSeconds,
        int crossRadius,
        int ownerLevel)
    {
        owner = mineOwner;
        originalOwner = mineOwner;
        chargeOwner = mineOwner;
        gridIndex = mineIndex;
        paintRadius = crossRadius;
        mineLevel = Mathf.Max(1, ownerLevel);
        fuseDuration = Mathf.Max(0.1f, fuseSeconds);
        explodeAt = Time.time + fuseDuration;
        armed = true;
        scheduledChainContext = null;
        scheduledComboDepth = 0;
        previewComboDepth = 0;
        scheduledExplodeAt = 0f;
        scheduledChainSourcePosition = Vector3.zero;
        scheduledChainSourceOwner = PaintOwner.Neutral;
        SetScheduledChainLinkVisible(false);
        SetComboGlowVisible(false);
        warningVisible = false;
        warningComboDepth = 0;
        SetWarningVisible(false);
        if (!ActiveBombs.Contains(this))
        {
            ActiveBombs.Add(this);
        }

        PlaceOnTile();
        ApplyMineMaterials();
        RebuildChainLinkVisuals();
    }

    private bool ConvertOwner(PaintOwner newOwner)
    {
        if (!armed ||
            newOwner == PaintOwner.Neutral ||
            owner == newOwner)
        {
            return false;
        }

        owner = newOwner;
        ApplyMineMaterials();
        if (warningVisible)
        {
            RefreshWarningMarkers();
            SetWarningVisible(true);
        }

        RebuildChainLinkVisuals();

        return true;
    }

    private void Update()
    {
        if (!armed)
        {
            return;
        }

        if (!IsGameplayPhaseActive())
        {
            return;
        }

        UpdateChainLinkVisuals();
        UpdateScheduledChainLinkVisual();
        UpdateComboGlowVisual();
        float remaining = GetScheduledExplosionTime() - Time.time;
        AnimateFuse(Mathf.Max(0f, remaining));
        UpdateBlockingColliderArming();
        UpdateWarning(remaining);
        if (remaining <= 0f)
        {
            Explode(scheduledChainContext, scheduledComboDepth);
        }
    }

    private float GetScheduledExplosionTime()
    {
        return scheduledChainContext != null ? scheduledExplodeAt : explodeAt;
    }

    private void Explode(ChainContext context, int comboDepth = 1)
    {
        Explode(context, comboDepth, true, true);
    }

    private void ExplodeAsSingleCombo()
    {
        CancelScheduledChainRegistration();
        Explode(null, 1, false, false);
    }

    private void Explode(
        ChainContext context,
        int comboDepth,
        bool triggerLinkedBombs,
        bool useScheduledContext)
    {
        if (!armed)
        {
            return;
        }

        if (useScheduledContext && context == null && scheduledChainContext != null)
        {
            context = scheduledChainContext;
            comboDepth = scheduledComboDepth;
        }

        if (context == null)
        {
            context = new ChainContext(owner, gridIndex);
            comboDepth = 1;
            context.RegisterMine();
        }

        PaintOwner attributionOwner = GetAttributionOwner();
        comboDepth = Mathf.Clamp(comboDepth, 1, MaxComboDepth);
        armed = false;
        ActiveBombs.Remove(this);
        scheduledChainContext = null;
        scheduledComboDepth = 0;
        previewComboDepth = 0;
        scheduledExplodeAt = 0f;
        SetScheduledChainLinkVisible(false);
        SetComboGlowVisible(false);
        MineExploded?.Invoke(attributionOwner);

        PaintCrossArea(context, comboDepth);
        ApplyExplosionHits(comboDepth);
        if (triggerLinkedBombs)
        {
            TriggerLinkedBombs(context, comboDepth);
        }
        RebuildChainLinkVisuals();

        ReturnToPool();
        CompleteChainMine(context, comboDepth);
    }

    private void CancelScheduledChainRegistration()
    {
        if (scheduledChainContext == null)
        {
            return;
        }

        ChainContext context = scheduledChainContext;
        scheduledChainContext = null;
        scheduledComboDepth = 0;
        scheduledExplodeAt = 0f;
        if (context.CancelMine())
        {
            ResolveCompletedChain(context);
        }
    }

    private void UpdateWarning(float remaining)
    {
        bool shouldShow = remaining <= WarningLeadSeconds;
        if (warningVisible == shouldShow)
        {
            if (shouldShow)
            {
                int currentWarningComboDepth = GetWarningComboDepth();
                if (warningComboDepth != currentWarningComboDepth)
                {
                    RefreshWarningMarkers();
                }

                PulseWarningMarkers();
            }

            return;
        }

        warningVisible = shouldShow;
        SetWarningVisible(shouldShow);
        if (shouldShow)
        {
            RefreshWarningMarkers();
        }
        else
        {
            warningComboDepth = 0;
        }
    }

    private void PaintCrossArea(ChainContext context, int comboDepth)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        resolvedExplosionIndices.Clear();
        AddResolvedExplosionIndex(gridIndex);
        ResolveExplosionTile(gridIndex, comboDepth);
        ResolveExplosionRay(Vector2Int.right, context, comboDepth);
        ResolveExplosionRay(Vector2Int.left, context, comboDepth);
        ResolveExplosionRay(Vector2Int.up, context, comboDepth);
        ResolveExplosionRay(Vector2Int.down, context, comboDepth);
        PaintLevelBonusArea(comboDepth);
    }

    private void PaintLevelBonusArea(int comboDepth)
    {
        if (mineLevel >= 4)
        {
            PaintLevelFourCornerFill(comboDepth);
        }

        if (mineLevel >= 5)
        {
            PaintLevelFiveCornerBlocks(comboDepth);
        }
    }

    private void ApplyExplosionHits(int comboDepth)
    {
        float stunSeconds = GetStunSecondsForCombo(comboDepth);
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady ||
            (stunSeconds <= 0f &&
             comboDepth != 2 &&
             (comboDepth != 1 || neutralizeOwnedTilesOnHit <= 0)))
        {
            return;
        }

        IReadOnlyList<PlayerPaintIdentity> identities =
            PlayerPaintIdentity.ActivePlayers;
        int hitCount = 0;
        for (int i = 0; i < identities.Count; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity == null ||
                !identity.isActiveAndEnabled ||
                identity.Owner == PaintOwner.Neutral ||
                PlayerShieldController.IsProtected(identity))
            {
                continue;
            }

            Vector2Int playerIndex =
                TilePaintManager.Instance.GetGridIndex(identity.transform.position);
            if (!resolvedExplosionIndices.Contains(playerIndex))
            {
                continue;
            }

            ApplyHitStun(identity.gameObject, stunSeconds);
            if (comboDepth == 2)
            {
                ApplyTeleportLock(identity.gameObject, stunSeconds);
            }

            int neutralizedCount = comboDepth == 1
                ? NeutralizeOwnedTiles(
                    identity.Owner,
                    identity.transform.position,
                    neutralizeOwnedTilesOnHit)
                : 0;
            hitCount++;
            Debug.Log(
                "[Item] Bomb Mine hit player. mineOwner=" + owner +
                " originalOwner=" + GetAttributionOwner() +
                " targetOwner=" + identity.Owner +
                " comboDepth=" + comboDepth +
                " neutralizedTiles=" + neutralizedCount + ".");
        }

        if (hitCount > 0)
        {
            Debug.Log(
                "[Item] Bomb Mine explosion hits resolved. owner=" + owner +
                " originalOwner=" + GetAttributionOwner() +
                " hits=" + hitCount +
                " comboDepth=" + comboDepth +
                " stun=" + stunSeconds.ToString("0.00") +
                " neutralizePerHit=" + neutralizeOwnedTilesOnHit + ".");
        }
    }

    private void ResolveExplosionRay(
        Vector2Int direction,
        ChainContext context,
        int comboDepth)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int lastResolvedIndex = gridIndex;
        bool hasResolvedEndpoint = false;
        for (int distance = 1; distance <= paintRadius; distance++)
        {
            Vector2Int index = gridIndex + direction * distance;
            if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
            {
                break;
            }

            if (ObstacleFieldManager.HasInstance &&
                ObstacleFieldManager.Instance.IsTileUnbreakable(index))
            {
                break;
            }

            if (ObstacleFieldManager.HasInstance &&
                ObstacleFieldManager.Instance.IsTileMovementBlocked(index))
            {
                AddResolvedExplosionIndex(index);
                ObstacleFieldManager.Instance.ServerDestroyAtGridIndex(index);
                ResolveExplosionTile(index, comboDepth);
                lastResolvedIndex = index;
                hasResolvedEndpoint = true;
                break;
            }

            AddResolvedExplosionIndex(index);
            ResolveExplosionTile(index, comboDepth);
            lastResolvedIndex = index;
            hasResolvedEndpoint = true;
            // Mines do not block the blast influence. If multiple same-owner
            // mines are inside this mine's blast, they all receive the same
            // next combo depth instead of becoming a sequential 1->2->3 chain.
        }

        if (comboDepth >= MaxComboDepth && hasResolvedEndpoint)
        {
            PaintComboEndpointBurst(lastResolvedIndex, comboDepth);
        }
    }

    private void PaintLevelFourCornerFill(int comboDepth)
    {
        for (int x = -LevelFourCornerFillRadius; x <= LevelFourCornerFillRadius; x += 2)
        {
            for (int y = -LevelFourCornerFillRadius; y <= LevelFourCornerFillRadius; y += 2)
            {
                Vector2Int index = gridIndex + new Vector2Int(x, y);
                ResolveLevelBonusTile(index, comboDepth);
            }
        }
    }

    private void PaintLevelFiveCornerBlocks(int comboDepth)
    {
        for (int signX = -1; signX <= 1; signX += 2)
        {
            for (int signY = -1; signY <= 1; signY += 2)
            {
                Vector2Int index = gridIndex + new Vector2Int(
                    signX * LevelFiveOuterCornerOffset,
                    signY * LevelFiveOuterCornerOffset);
                ResolveLevelBonusTile(index, comboDepth);
            }
        }
    }

    private void ResolveLevelBonusTile(Vector2Int index, int comboDepth)
    {
        if (!TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index) ||
            WeaponPatternTraversal.IsUnbreakable(index))
        {
            return;
        }

        if (ObstacleFieldManager.HasInstance &&
            ObstacleFieldManager.Instance.IsTileMovementBlocked(index))
        {
            ObstacleFieldManager.Instance.ServerDestroyAtGridIndex(index);
        }

        AddResolvedExplosionIndex(index);
        ResolveExplosionTile(index, comboDepth);
    }

    private void AddResolvedExplosionIndex(Vector2Int index)
    {
        if (!resolvedExplosionIndices.Contains(index))
        {
            resolvedExplosionIndices.Add(index);
        }
    }

    private void PaintComboEndpointBurst(Vector2Int center, int comboDepth)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        for (int x = -ComboEndpointBurstRadius; x <= ComboEndpointBurstRadius; x++)
        {
            for (int y = -ComboEndpointBurstRadius; y <= ComboEndpointBurstRadius; y++)
            {
                Vector2Int index = center + new Vector2Int(x, y);
                if (!tileManager.IsInsideGrid(index) ||
                    tileManager.IsTileDestroyed(index) ||
                    WeaponPatternTraversal.IsUnbreakable(index))
                {
                    continue;
                }

                if (ObstacleFieldManager.HasInstance &&
                    ObstacleFieldManager.Instance.IsTileMovementBlocked(index))
                {
                    ObstacleFieldManager.Instance.ServerDestroyAtGridIndex(index);
                }

                AddResolvedExplosionIndex(index);
                ResolveExplosionTile(index, comboDepth);
            }
        }
    }

    private void TriggerLinkedBombs(ChainContext context, int comboDepth)
    {
        if (context == null ||
            context.RootOwner == PaintOwner.Neutral ||
            comboDepth >= MaxComboDepth)
        {
            return;
        }

        for (int i = ActiveBombs.Count - 1; i >= 0; i--)
        {
            BombMine other = ActiveBombs[i];
            if (other == null ||
                other == this ||
                !other.armed ||
                other.owner != context.RootOwner ||
                !resolvedExplosionIndices.Contains(other.gridIndex))
            {
                continue;
            }

            other.TryScheduleChainExplosion(this, context, comboDepth + 1);
        }
    }

    private bool TryScheduleChainExplosion(
        BombMine sourceMine,
        ChainContext context,
        int comboDepth)
    {
        if (!armed ||
            sourceMine == null ||
            context == null ||
            context.RootOwner == PaintOwner.Neutral ||
            owner != context.RootOwner)
        {
            return false;
        }

        int nextComboDepth = Mathf.Clamp(comboDepth, 2, MaxComboDepth);
        if (comboDepth > MaxComboDepth)
        {
            return false;
        }
        float nextExplodeAt = Time.time + chainExplosionDelaySeconds;
        if (scheduledChainContext != null)
        {
            if (scheduledChainContext != context ||
                nextComboDepth >= scheduledComboDepth)
            {
                return false;
            }

            scheduledComboDepth = nextComboDepth;
            scheduledExplodeAt = Mathf.Min(scheduledExplodeAt, nextExplodeAt);
            SetScheduledChainSource(sourceMine);
            return false;
        }

        scheduledChainContext = context;
        scheduledComboDepth = nextComboDepth;
        scheduledExplodeAt = nextExplodeAt;
        SetScheduledChainSource(sourceMine);
        context.RegisterMine();
        return true;
    }

    private void SetScheduledChainSource(BombMine sourceMine)
    {
        scheduledChainSourcePosition = sourceMine.GetChainLinkAnchorPosition();
        scheduledChainSourceOwner = sourceMine.owner;
        EnsureScheduledChainLinkRenderer();
        ApplyChainLineMaterial(scheduledChainLinkRenderer, scheduledChainSourceOwner);
        RefreshScheduledChainLinkPositions();
        SetScheduledChainLinkVisible(true);
        UpdateComboGlowVisual();
    }

    private void EnsureScheduledChainLinkRenderer()
    {
        if (scheduledChainLinkRenderer != null)
        {
            return;
        }

        GameObject linkObject = new GameObject("Bomb Mine Incoming Chain Link");
        linkObject.transform.SetParent(transform, false);
        scheduledChainLinkRenderer = linkObject.AddComponent<LineRenderer>();
        ConfigureChainLineRenderer(scheduledChainLinkRenderer);
        scheduledChainLinkRenderer.enabled = false;
        linkObject.SetActive(false);
    }

    private void SetScheduledChainLinkVisible(bool visible)
    {
        if (scheduledChainLinkRenderer == null)
        {
            return;
        }

        scheduledChainLinkRenderer.gameObject.SetActive(visible);
        scheduledChainLinkRenderer.enabled = visible;
    }

    private void RefreshScheduledChainLinkPositions()
    {
        if (scheduledChainLinkRenderer == null)
        {
            return;
        }

        Vector3 targetPosition = GetChainLinkAnchorPosition();
        SetChainLinePositions(
            scheduledChainLinkRenderer,
            scheduledChainSourcePosition,
            targetPosition,
            1.35f,
            scheduledComboDepth);
    }

    private void UpdateScheduledChainLinkVisual()
    {
        if (scheduledChainContext == null || scheduledChainLinkRenderer == null)
        {
            return;
        }

        float pulse = (Mathf.Sin(
            Time.time * chainLinkPulseSpeed * 2.4f + scheduledComboDepth) + 1f) * 0.5f;
        float alpha = 1f;
        float widthScale = ScheduledChainLinkWidthMultiplier * Mathf.Lerp(1.25f, 1.9f, pulse);
        ApplyChainLineAppearance(
            scheduledChainLinkRenderer,
            scheduledChainSourceOwner,
            owner,
            alpha,
            widthScale,
            0.58f,
            2.8f);
        RefreshScheduledChainLinkPositions();
    }

    private void UpdateComboGlowVisual()
    {
        bool isScheduledCombo =
            scheduledChainContext != null && scheduledComboDepth > 1;
        int displayComboDepth = isScheduledCombo
            ? scheduledComboDepth
            : previewComboDepth;
        if (displayComboDepth <= 1)
        {
            SetComboGlowVisible(false);
            Color ownerColor = GetMineAccentColor();
            ApplyVisualOwnerBodyColor(ownerColor);
            ApplyEmphasisIntensity(ownerColor, 1f);
            return;
        }

        SetComboGlowVisible(false);

        float pulse = (Mathf.Sin(
            Time.time * comboGlowPulseSpeed + displayComboDepth * 0.77f) + 1f) * 0.5f;
        float depthFactor = Mathf.Clamp01((displayComboDepth - 2) / 3f);
        float scheduledScale = isScheduledCombo ? 1f : 0.72f;
        float maxComboIntensity = Mathf.Lerp(
            ComboEffectMinIntensity,
            ComboEffectMaxIntensity,
            pulse);
        float comboIntensity = displayComboDepth >= 3
            ? maxComboIntensity
            : Mathf.Lerp(1f, maxComboIntensity, 0.5f);
        comboIntensity *= scheduledScale;
        comboIntensity = Mathf.Max(1f, comboIntensity);

        Color comboOwnerColor = GetMineAccentColor();
        ApplyVisualOwnerBodyColor(
            comboOwnerColor,
            Mathf.Lerp(
                OwnerBodyEmissionScale,
                ComboOwnerBodyEmissionScale + depthFactor * 0.45f,
                pulse) * scheduledScale);
        ApplyEmphasisIntensity(comboOwnerColor, comboIntensity);
    }

    private void EnsureComboGlowRenderers()
    {
        if (comboGlowRenderer == null)
        {
            comboGlowRenderer = CreateComboGlowRenderer("Bomb Mine Combo Glow Ring", 6);
        }

        if (comboGlowOuterRenderer == null)
        {
            comboGlowOuterRenderer = CreateComboGlowRenderer("Bomb Mine Combo Glow Aura", 5);
        }
    }

    private LineRenderer CreateComboGlowRenderer(string objectName, int sortingOrder)
    {
        GameObject ringObject = new GameObject(objectName);
        ringObject.transform.SetParent(transform, false);
        LineRenderer renderer = ringObject.AddComponent<LineRenderer>();
        renderer.useWorldSpace = true;
        renderer.loop = true;
        renderer.positionCount = ComboGlowSegmentCount;
        renderer.numCornerVertices = 6;
        renderer.numCapVertices = 6;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = LineAlignment.View;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.sortingOrder = sortingOrder;
        renderer.enabled = false;
        ringObject.SetActive(false);
        ApplyChainLineMaterial(renderer, owner);
        return renderer;
    }

    private void SetComboGlowVisible(bool visible)
    {
        if (comboGlowVisible == visible)
        {
            return;
        }

        comboGlowVisible = visible;
        SetComboGlowRendererVisible(comboGlowRenderer, visible);
        SetComboGlowRendererVisible(comboGlowOuterRenderer, visible);
        if (!visible)
        {
            ApplyMineMaterials();
        }
    }

    private static void SetComboGlowRendererVisible(LineRenderer renderer, bool visible)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.gameObject.SetActive(visible);
        renderer.enabled = visible;
    }

    private void RefreshComboGlowRingPositions(
        LineRenderer renderer,
        float baseRadiusRatio,
        float radiusPulse,
        float angleOffset)
    {
        if (renderer == null)
        {
            return;
        }

        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        Vector3 center = transform.position +
                         Vector3.up * (tileSize * ComboGlowHeightRatio);
        float radius = tileSize * baseRadiusRatio * radiusPulse;
        for (int i = 0; i < ComboGlowSegmentCount; i++)
        {
            float angle = angleOffset + (Mathf.PI * 2f * i) / ComboGlowSegmentCount;
            renderer.SetPosition(
                i,
                center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius));
        }
    }

    private void ApplyComboGlowAppearance(
        LineRenderer renderer,
        Color color,
        float widthScale,
        float emissionScale)
    {
        if (renderer == null)
        {
            return;
        }

        if (renderer.sharedMaterial == null)
        {
            ApplyChainLineMaterial(renderer, owner);
        }

        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        renderer.widthMultiplier = Mathf.Max(
            0.01f,
            tileSize * comboGlowWidthRatio * widthScale);
        renderer.startColor = color;
        renderer.endColor = color;
        SetRendererGlowColor(renderer, color, emissionScale);
    }

    private void ApplyMineComboGlow(
        Color glowColor,
        float pulse,
        float depthFactor,
        float intensityScale)
    {
        float bodyBlend = Mathf.Lerp(0.18f, 0.34f + depthFactor * 0.12f, pulse);
        float accentBlend = Mathf.Lerp(0.58f, 0.9f, pulse);
        float emissionScale =
            Mathf.Lerp(0.55f, 1.45f + depthFactor * 0.7f, pulse) *
            intensityScale;
        bodyBlend *= intensityScale;
        accentBlend *= Mathf.Lerp(0.65f, 1f, intensityScale);

        if (cachedRenderer != null && cachedRenderer.enabled)
        {
            ApplyRendererComboGlow(
                cachedRenderer,
                BodyColor,
                glowColor,
                bodyBlend,
                emissionScale * 0.58f);
        }

        if (visualRenderers != null)
        {
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                ApplyRendererComboGlow(
                    renderer,
                    BodyColor,
                    glowColor,
                    bodyBlend,
                    emissionScale * 0.58f);
            }
        }

        if (accentRenderer != null)
        {
            ApplyRendererComboGlow(
                accentRenderer,
                GetMineAccentColor(),
                glowColor,
                accentBlend,
                emissionScale);
        }
    }

    private void ApplyRendererComboGlow(
        Renderer renderer,
        Color baseColor,
        Color glowColor,
        float blend,
        float emissionScale)
    {
        if (renderer == null)
        {
            return;
        }

        Color color = Color.Lerp(baseColor, glowColor, Mathf.Clamp01(blend));
        color.a = 1f;
        SetRendererGlowColor(renderer, color, emissionScale);
    }

    private void SetRendererGlowColor(
        Renderer renderer,
        Color color,
        float emissionScale)
    {
        if (renderer == null)
        {
            return;
        }

        if (comboGlowMaterialProperties == null)
        {
            comboGlowMaterialProperties = new MaterialPropertyBlock();
        }

        Color emissionColor = color * Mathf.Max(0f, emissionScale);
        emissionColor.a = color.a;
        comboGlowMaterialProperties.Clear();
        comboGlowMaterialProperties.SetColor(BaseColorPropertyId, color);
        comboGlowMaterialProperties.SetColor(ColorPropertyId, color);
        comboGlowMaterialProperties.SetColor(TintColorPropertyId, color);
        comboGlowMaterialProperties.SetColor(EmissionColorPropertyId, emissionColor);
        renderer.SetPropertyBlock(comboGlowMaterialProperties);
    }

    private Color GetComboGlowColor(int comboDepth, float alpha)
    {
        int index = Mathf.Clamp(
            comboDepth - 1,
            0,
            ComboGlowDepthColors.Length - 1);
        Color depthColor = ComboGlowDepthColors[index];
        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        Color ownerColor = visualSettings != null
            ? visualSettings.GetIndicatorColor(owner, alpha)
            : Color.white;
        Color color = Color.Lerp(ownerColor, depthColor, 0.82f);
        color.a = alpha;
        return color;
    }

    private Vector3 GetChainLinkAnchorPosition()
    {
        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        return transform.position + Vector3.up * (tileSize * ChainLinkAnchorHeightRatio);
    }

    private static void RebuildChainLinkVisuals()
    {
        ClearChainLinkVisuals();
        ResetPreviewComboDepths();
        if (!Application.isPlaying)
        {
            return;
        }

        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine first = ActiveBombs[i];
            if (first == null || !first.armed)
            {
                continue;
            }

            for (int j = i + 1; j < ActiveBombs.Count; j++)
            {
                BombMine second = ActiveBombs[j];
                if (second == null ||
                    !second.armed ||
                    first.owner != second.owner ||
                    (!first.IsInCrossArea(second.gridIndex) &&
                     !second.IsInCrossArea(first.gridIndex)))
                {
                    continue;
                }

                ChainLinkVisual link = RentChainLinkVisual();
                link.First = first;
                link.Second = second;
                link.Phase = (first.gridIndex.x * 0.73f) +
                             (first.gridIndex.y * 1.13f) +
                             (second.gridIndex.x * 1.37f) +
                             (second.gridIndex.y * 0.91f);
                SetChainLinePositions(
                    link.Renderer,
                    first.GetChainLinkAnchorPosition(),
                    second.GetChainLinkAnchorPosition(),
                    1f,
                    link.Phase);
                ApplyChainLineMaterial(link.Renderer, first.owner);
                link.Renderer.gameObject.SetActive(true);
                link.Renderer.enabled = true;
                ActiveChainLinks.Add(link);
            }
        }

        RefreshPreviewComboDepths();
        lastChainLinkVisualUpdateFrame = -1;
        UpdateChainLinkVisuals();
    }

    private static void ResetPreviewComboDepths()
    {
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine != null)
            {
                mine.previewComboDepth = 0;
            }
        }
    }

    private static void RefreshPreviewComboDepths()
    {
        while (true)
        {
            BombMine root = FindEarliestUnassignedPreviewRoot();
            if (root == null)
            {
                return;
            }

            root.previewComboDepth = 1;
            PreviewComboFrontier.Clear();
            PreviewComboFrontier.Add(root);
            for (int i = 0; i < PreviewComboFrontier.Count; i++)
            {
                BombMine source = PreviewComboFrontier[i];
                if (source == null || !source.armed)
                {
                    continue;
                }

                if (source.previewComboDepth >= MaxComboDepth)
                {
                    continue;
                }

                int nextComboDepth = Mathf.Max(2, source.previewComboDepth + 1);
                for (int j = 0; j < ActiveBombs.Count; j++)
                {
                    BombMine candidate = ActiveBombs[j];
                    if (candidate == null ||
                        candidate.previewComboDepth > 0 ||
                        !source.CanTriggerMine(candidate))
                    {
                        continue;
                    }

                    candidate.previewComboDepth = nextComboDepth;
                    PreviewComboFrontier.Add(candidate);
                }
            }
        }
    }

    private static BombMine FindEarliestUnassignedPreviewRoot()
    {
        BombMine root = null;
        float earliestTime = float.MaxValue;
        for (int i = 0; i < ActiveBombs.Count; i++)
        {
            BombMine mine = ActiveBombs[i];
            if (mine == null ||
                !mine.armed ||
                mine.previewComboDepth > 0)
            {
                continue;
            }

            float explodeTime = mine.GetScheduledExplosionTime();
            if (explodeTime >= earliestTime)
            {
                continue;
            }

            earliestTime = explodeTime;
            root = mine;
        }

        return root;
    }

    private static void UpdateChainLinkVisuals()
    {
        if (lastChainLinkVisualUpdateFrame == Time.frameCount)
        {
            return;
        }

        lastChainLinkVisualUpdateFrame = Time.frameCount;
        for (int i = 0; i < ActiveChainLinks.Count; i++)
        {
            ChainLinkVisual link = ActiveChainLinks[i];
            if (link == null ||
                link.Renderer == null ||
                link.First == null ||
                link.Second == null ||
                !link.First.armed ||
                !link.Second.armed)
            {
                continue;
            }

            float pulse = (Mathf.Sin(
                Time.time * chainLinkPulseSpeed + link.Phase) + 1f) * 0.5f;
            Vector3 start = link.First.GetChainLinkAnchorPosition();
            Vector3 end = link.Second.GetChainLinkAnchorPosition();
            SetChainLinePositions(
                link.Renderer,
                start,
                end,
                1f,
                link.Phase);
            ApplyChainLineAppearance(
                link.Renderer,
                link.First.owner,
                link.Second.owner,
                1f,
                Mathf.Lerp(4.2f, 6.2f, pulse),
                0.52f,
                2.6f);
        }
    }

    private static ChainLinkVisual RentChainLinkVisual()
    {
        while (ChainLinkPool.Count > 0)
        {
            ChainLinkVisual pooled = ChainLinkPool.Pop();
            if (pooled != null && pooled.Renderer != null)
            {
                return pooled;
            }
        }

        GameObject linkObject = new GameObject("Bomb Mine Potential Chain Link");
        LineRenderer renderer = linkObject.AddComponent<LineRenderer>();
        ConfigureChainLineRenderer(renderer);
        renderer.enabled = false;
        linkObject.SetActive(false);
        return new ChainLinkVisual(renderer);
    }

    private static void ClearChainLinkVisuals()
    {
        for (int i = ActiveChainLinks.Count - 1; i >= 0; i--)
        {
            ChainLinkVisual link = ActiveChainLinks[i];
            if (link == null || link.Renderer == null)
            {
                continue;
            }

            link.Renderer.enabled = false;
            link.Renderer.gameObject.SetActive(false);
            link.First = null;
            link.Second = null;
            if (ChainLinkPool.Count < MaxChainLinkPoolSize)
            {
                ChainLinkPool.Push(link);
            }
            else
            {
                Destroy(link.Renderer.gameObject);
            }
        }

        ActiveChainLinks.Clear();
    }

    private static void ConfigureChainLineRenderer(LineRenderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.useWorldSpace = true;
        renderer.loop = false;
        renderer.positionCount = ChainLinkLightningPointCount;
        renderer.numCornerVertices = 1;
        renderer.numCapVertices = 3;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = LineAlignment.View;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.sortingOrder = 3;
    }

    private static void SetChainLinePositions(
        LineRenderer renderer,
        Vector3 start,
        Vector3 end,
        float arcScale,
        float phase)
    {
        if (renderer == null)
        {
            return;
        }

        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        renderer.positionCount = ChainLinkLightningPointCount;
        Vector3 delta = end - start;
        Vector3 flatDirection = new Vector3(delta.x, 0f, delta.z);
        Vector3 side = flatDirection.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, flatDirection.normalized)
            : Vector3.right;

        for (int i = 0; i < ChainLinkLightningPointCount; i++)
        {
            float t = i / (float)(ChainLinkLightningPointCount - 1);
            Vector3 position = Vector3.Lerp(start, end, t);
            float edgeFade = Mathf.Sin(t * Mathf.PI);
            float arc = tileSize * ChainLinkArcHeightRatio * arcScale * edgeFade;
            float wave = Mathf.Sin(Time.time * chainLinkPulseSpeed * 9f + phase + i * 1.73f);
            float zigzag = i % 2 == 0 ? 1f : -1f;
            position += Vector3.up * arc;
            position += side *
                        (tileSize * ChainLinkLightningJitterRatio * edgeFade * zigzag *
                         Mathf.Lerp(0.45f, 1f, Mathf.Abs(wave)));
            renderer.SetPosition(i, position);
        }
    }

    private static void ApplyChainLineMaterial(LineRenderer renderer, PaintOwner owner)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = GetChainLightningMaterial();
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Material GetChainLightningMaterial()
    {
        if (chainLightningMaterial != null)
        {
            return chainLightningMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        chainLightningMaterial = new Material(shader)
        {
            name = "Bomb Mine Chain Lightning Material"
        };
        if (chainLightningMaterial.HasProperty(SurfacePropertyId))
        {
            chainLightningMaterial.SetFloat(SurfacePropertyId, 0f);
        }

        if (chainLightningMaterial.HasProperty(BlendPropertyId))
        {
            chainLightningMaterial.SetFloat(BlendPropertyId, 0f);
        }

        return chainLightningMaterial;
    }

    private static Color GetChainLineOwnerColor(PaintOwner owner, float alpha, float whiteBlend)
    {
        if (owner == PaintOwner.Neutral)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        Color color = visualSettings != null
            ? visualSettings.GetIndicatorColor(owner, alpha)
            : new Color(1f, 1f, 1f, alpha);
        Color.RGBToHSV(color, out float hue, out float saturation, out _);
        color = Color.HSVToRGB(hue, Mathf.Max(saturation, ChainLineOwnerMinSaturation), 1f);
        color = Color.Lerp(color, Color.white, Mathf.Clamp01(whiteBlend * ChainLineOwnerWhiteBlendScale));
        color.a = alpha;
        return color;
    }

    private static void ApplyChainLineAppearance(
        LineRenderer renderer,
        PaintOwner startOwner,
        PaintOwner endOwner,
        float alpha,
        float widthScale,
        float whiteBlend,
        float brightnessMultiplier)
    {
        if (renderer == null)
        {
            return;
        }

        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        renderer.widthMultiplier = Mathf.Max(
            0.01f,
            tileSize * chainLinkWidthRatio * widthScale);

        Color startColor = GetChainLineOwnerColor(startOwner, alpha, whiteBlend);
        Color endColor = GetChainLineOwnerColor(endOwner, alpha, whiteBlend);
        float intensityScale = Mathf.Max(0f, ChainLinkEmissionIntensity) /
                               ChainLinkReferenceEmissionIntensity;
        float brightness = 3.5f * Mathf.Max(0.1f, brightnessMultiplier) * intensityScale;
        Color brightStartColor = startColor * brightness;
        Color brightEndColor = endColor * brightness;
        brightStartColor.a = alpha;
        brightEndColor.a = alpha;
        renderer.startColor = brightStartColor;
        renderer.endColor = brightEndColor;

        Color materialColor = Color.Lerp(brightStartColor, brightEndColor, 0.5f);
        materialColor.a = alpha;
        if (chainLinkMaterialProperties == null)
        {
            chainLinkMaterialProperties = new MaterialPropertyBlock();
        }

        chainLinkMaterialProperties.Clear();
        chainLinkMaterialProperties.SetColor(BaseColorPropertyId, materialColor);
        chainLinkMaterialProperties.SetColor(ColorPropertyId, materialColor);
        chainLinkMaterialProperties.SetColor(TintColorPropertyId, materialColor);
        Color emissionColor = materialColor * Mathf.Max(1f, ChainLinkEmissionIntensity);
        emissionColor.a = materialColor.a;
        chainLinkMaterialProperties.SetColor(EmissionColorPropertyId, emissionColor);
        renderer.SetPropertyBlock(chainLinkMaterialProperties);
    }

    private static void CompleteChainMine(ChainContext context, int comboDepth)
    {
        if (context == null || !context.CompleteMine(comboDepth))
        {
            return;
        }

        ResolveCompletedChain(context);
    }

    private static void ResolveCompletedChain(ChainContext context)
    {
        if (context == null)
        {
            return;
        }

        Debug.Log(
            "[Item] Bomb Mine chain resolved. owner=" + context.RootOwner +
            " comboDepth=" + context.MaxComboDepth +
            " explodedMines=" + context.ExplodedMineCount +
            " root=" + context.RootIndex + ".");
        ChainResolved?.Invoke(
            context.RootOwner,
            context.MaxComboDepth,
            context.RootIndex);
    }

    private bool IsInCrossArea(Vector2Int otherIndex)
    {
        Vector2Int delta = otherIndex - gridIndex;
        int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
        if (distance <= 0 ||
            distance > paintRadius ||
            (delta.x != 0 && delta.y != 0))
        {
            return false;
        }

        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return true;
        }

        Vector2Int direction = new Vector2Int(
            Math.Sign(delta.x),
            Math.Sign(delta.y));
        for (int step = 1; step < distance; step++)
        {
            Vector2Int index = gridIndex + direction * step;
            if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                TilePaintManager.Instance.IsTileDestroyed(index) ||
                WeaponPatternTraversal.IsUnbreakable(index) ||
                WeaponPatternTraversal.IsBreakable(index))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanTriggerMine(BombMine other)
    {
        return other != null &&
               other != this &&
               armed &&
               other.armed &&
               owner != PaintOwner.Neutral &&
               other.owner == owner &&
               IsInCrossArea(other.gridIndex);
    }

    private void ResolveExplosionTile(Vector2Int index, int comboDepth)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return;
        }

        if (!TilePaintManager.Instance.TryGetTileOwner(index, out PaintOwner currentOwner))
        {
            return;
        }

        PaintOwner targetOwner = ResolveExplosionTargetOwner(currentOwner, comboDepth);
        ForceSetTileOwner(index, targetOwner);
    }

    private PaintOwner ResolveExplosionTargetOwner(
        PaintOwner currentOwner,
        int comboDepth)
    {
        if (comboDepth <= 1 &&
            currentOwner != PaintOwner.Neutral &&
            currentOwner != owner)
        {
            return PaintOwner.Neutral;
        }

        return owner;
    }

    private PaintOwner GetAttributionOwner()
    {
        if (originalOwner != PaintOwner.Neutral)
        {
            return originalOwner;
        }

        return chargeOwner != PaintOwner.Neutral ? chargeOwner : owner;
    }

    private static void ForceSetTileOwner(Vector2Int index, PaintOwner targetOwner)
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            return;
        }

        if (targetOwner != PaintOwner.Neutral &&
            GameRoundManager.HasInstance &&
            !GameRoundManager.Instance.CanOwnerClaimTile(targetOwner, index))
        {
            return;
        }

        if (NetworkTilePaintAuthority.HasInstance)
        {
            NetworkTilePaintAuthority.Instance.RequestForceSetOwner(index, targetOwner);
            return;
        }

        TilePaintManager.Instance.ForceSetTileOwnerLocal(index, targetOwner);
    }

    private static void ApplyHitStun(GameObject target, float stunSeconds)
    {
        if (target == null || stunSeconds <= 0f)
        {
            return;
        }

        PlayerStatusEffectController statusEffects =
            target.GetComponent<PlayerStatusEffectController>();
        if (statusEffects == null)
        {
            statusEffects = target.AddComponent<PlayerStatusEffectController>();
        }

        statusEffects.ApplyStatusEffect(new StatusEffectSpec(
            StatusEffectType.Root,
            0f,
            stunSeconds,
            StatusEffectStackMode.RefreshDuration));
    }

    private static void ApplyTeleportLock(GameObject target, float duration)
    {
        if (target == null || duration <= 0f)
        {
            return;
        }

        PlayerMovement movement = target.GetComponent<PlayerMovement>();
        if (movement == null)
        {
            return;
        }

        movement.ApplyTeleportLock(duration);
    }

    private static float GetStunSecondsForCombo(int comboDepth)
    {
        return activeSettings != null
            ? activeSettings.GetStunSecondsForCombo(comboDepth)
            : BombMineSettings.GetDefaultStunSecondsForCombo(comboDepth);
    }

    private static int NeutralizeOwnedTiles(
        PaintOwner targetOwner,
        Vector3 hitPosition,
        int maxCount)
    {
        if (targetOwner == PaintOwner.Neutral ||
            maxCount <= 0 ||
            !TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return 0;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        OwnedTileCandidates.Clear();
        int gridSize = tileManager.GridSize;
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (tileManager.IsTileDestroyed(index) ||
                    !tileManager.TryGetTileOwner(index, out PaintOwner currentOwner) ||
                    currentOwner != targetOwner)
                {
                    continue;
                }

                OwnedTileCandidates.Add(index);
            }
        }

        int neutralizedCount = 0;
        while (neutralizedCount < maxCount && OwnedTileCandidates.Count > 0)
        {
            int bestCandidateIndex = FindNearestOwnedTileCandidate(
                tileManager,
                hitPosition);
            Vector2Int neutralizeIndex = OwnedTileCandidates[bestCandidateIndex];
            OwnedTileCandidates[bestCandidateIndex] =
                OwnedTileCandidates[OwnedTileCandidates.Count - 1];
            OwnedTileCandidates.RemoveAt(OwnedTileCandidates.Count - 1);

            if (NetworkTilePaintAuthority.HasInstance)
            {
                if (NetworkTilePaintAuthority.Instance.RequestForceSetOwner(
                    neutralizeIndex,
                    PaintOwner.Neutral))
                {
                    neutralizedCount++;
                }

                continue;
            }

            if (tileManager.ForceSetTileOwnerLocal(neutralizeIndex, PaintOwner.Neutral))
            {
                neutralizedCount++;
            }
        }

        OwnedTileCandidates.Clear();
        return neutralizedCount;
    }

    private static int FindNearestOwnedTileCandidate(
        TilePaintManager tileManager,
        Vector3 hitPosition)
    {
        int bestIndex = 0;
        float bestDistanceSqr = float.MaxValue;
        for (int i = 0; i < OwnedTileCandidates.Count; i++)
        {
            Vector3 tileCenter =
                tileManager.GetTileCenterWorldPosition(OwnedTileCandidates[i]);
            Vector3 offset = tileCenter - hitPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestIndex = i;
        }

        return bestIndex;
    }

    private void PlaceOnTile()
    {
        if (!TilePaintManager.HasInstance)
        {
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector3 position = tileManager.GetTileCenterWorldPosition(gridIndex);
        position.y = tileManager.GetTileSurfaceY(gridIndex) + SurfaceOffset;
        transform.position = position;
        transform.rotation = Quaternion.identity;
        transform.localScale = HasVisualPrefab()
            ? Vector3.one
            : new Vector3(PrimitiveBaseScaleXz, PrimitiveBaseScaleY, PrimitiveBaseScaleXz);
        ConfigureBlockingCollider(tileManager.TileSize);
    }

    private void AnimateFuse(float remaining)
    {
        float pulse = 1f + Mathf.Sin(Time.time * 14f) * Mathf.Lerp(
            0.02f,
            0.12f,
            Mathf.Clamp01(1f - remaining / Mathf.Max(0.1f, fuseDuration)));
        transform.localScale = HasVisualPrefab()
            ? Vector3.one
            : new Vector3(
                PrimitiveBaseScaleXz * pulse,
                PrimitiveBaseScaleY,
                PrimitiveBaseScaleXz * pulse);
    }

    private void ApplyMineMaterials()
    {
        bool hasVisualPrefab = EnsureVisualPrefabInstance();
        Color ownerColor = GetMineAccentColor();

        if (cachedRenderer == null)
        {
            cachedRenderer = GetComponent<Renderer>();
        }

        if (cachedRenderer != null)
        {
            cachedRenderer.enabled = !hasVisualPrefab;
            cachedRenderer.sharedMaterial = GetBodyMaterial();
            cachedRenderer.shadowCastingMode = ShadowCastingMode.On;
            cachedRenderer.receiveShadows = true;
            SetRendererColor(
                cachedRenderer,
                ownerColor,
                ownerColor * OwnerBodyEmissionScale);
        }

        ApplyVisualOwnerBodyColor(ownerColor);
        DisableAccentVisual();
        ApplyEmphasisEffect(ownerColor);
    }

    private void ConfigureBlockingCollider(float tileSize)
    {
        BoxCollider boxCollider = GetOrCreateNonPhysicalCollider();
        boxCollider.isTrigger = false;
        boxCollider.enabled = false;
        blockingCollider = boxCollider;
        blockingColliderArmed = false;
        nextBlockingOccupancyCheckAt = 0f;

        float inverseScaleX = Mathf.Approximately(transform.localScale.x, 0f)
            ? 1f
            : 1f / transform.localScale.x;
        float inverseScaleY = Mathf.Approximately(transform.localScale.y, 0f)
            ? 1f
            : 1f / transform.localScale.y;
        float inverseScaleZ = Mathf.Approximately(transform.localScale.z, 0f)
            ? 1f
            : 1f / transform.localScale.z;

        float worldSize = Mathf.Max(0.2f, tileSize * BlockingColliderTileFill);
        float worldHeight = Mathf.Max(0.35f, tileSize * BlockingColliderHeightRatio);
        boxCollider.center = new Vector3(0f, worldHeight * 0.5f * inverseScaleY, 0f);
        boxCollider.size = new Vector3(
            worldSize * inverseScaleX,
            worldHeight * inverseScaleY,
            worldSize * inverseScaleZ);
    }

    private BoxCollider GetOrCreateNonPhysicalCollider()
    {
        BoxCollider selectedBoxCollider = null;
        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            BoxCollider boxCollider = collider as BoxCollider;
            if (boxCollider != null && selectedBoxCollider == null)
            {
                selectedBoxCollider = boxCollider;
                continue;
            }

            collider.isTrigger = true;
            collider.enabled = false;
            Destroy(collider);
        }

        if (selectedBoxCollider == null)
        {
            selectedBoxCollider = gameObject.AddComponent<BoxCollider>();
        }

        return selectedBoxCollider;
    }

    private void UpdateBlockingColliderArming()
    {
        if (blockingColliderArmed ||
            blockingCollider == null ||
            Time.time < nextBlockingOccupancyCheckAt)
        {
            return;
        }

        nextBlockingOccupancyCheckAt =
            Time.time + BlockingOccupancyCheckInterval;
        if (IsPlayerOccupyingMineTile())
        {
            return;
        }

        blockingCollider.enabled = true;
        blockingColliderArmed = true;
    }

    private bool IsPlayerOccupyingMineTile()
    {
        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector3 center = tileManager.GetTileCenterWorldPosition(gridIndex);
        center.y = tileManager.GetTileSurfaceY(gridIndex) + 1f;
        float tileSize = Mathf.Max(0.1f, tileManager.TileSize);
        Vector3 halfExtents = new Vector3(
            tileSize * BlockingOccupancyCheckHalfExtentTiles,
            1.4f,
            tileSize * BlockingOccupancyCheckHalfExtentTiles);

        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            BlockingOccupancyHits,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = BlockingOccupancyHits[i];
            BlockingOccupancyHits[i] = null;
            if (hit == null)
            {
                continue;
            }

            PlayerMovement movement = hit.GetComponentInParent<PlayerMovement>();
            if (movement == null)
            {
                continue;
            }

            Vector2Int playerIndex =
                tileManager.GetGridIndex(movement.transform.position);
            if (playerIndex == gridIndex)
            {
                return true;
            }

            Vector3 otherTileCenter =
                tileManager.GetTileCenterWorldPosition(playerIndex);
            Vector3 offsetFromOtherTileCenter =
                movement.transform.position - otherTileCenter;
            offsetFromOtherTileCenter.y = 0f;
            float arrivalTolerance =
                tileSize * BlockingOtherTileArrivalToleranceRatio;
            if (offsetFromOtherTileCenter.sqrMagnitude >
                arrivalTolerance * arrivalTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasVisualPrefab()
    {
        return sharedVisualPrefab != null || visualInstance != null;
    }

    private bool EnsureVisualPrefabInstance()
    {
        if (sharedVisualPrefab == null)
        {
            return false;
        }

        if (visualInstance == null)
        {
            visualInstance = Instantiate(sharedVisualPrefab, transform, false);
            visualInstance.name = "Bomb Mine Model";
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            DisablePrefabRuntimeBehaviours(visualInstance);
            SetPrefabCollidersEnabled(visualInstance, false);
            visualRenderers = visualInstance.GetComponentsInChildren<Renderer>(true);
            CacheEmphasisEffectComponents(visualInstance);
        }

        visualInstance.SetActive(true);
        return true;
    }

    private void ApplyEmphasisEffect(Color ownerColor)
    {
        if (!HasEmphasisEffect())
        {
            return;
        }

        Color effectColor = GetBrightOwnerEffectColor(ownerColor);
        effectColor.a = 1f;

        if (emphasisParticles != null)
        {
            for (int i = 0; i < emphasisParticles.Length; i++)
            {
                ParticleSystem particle = emphasisParticles[i];
                if (particle == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = particle.main;
                main.startColor = TintGradientPreservingShade(
                    emphasisStartColors[i],
                    effectColor);
                ApplyEmphasisLifetimeColor(particle, i, effectColor);
                ConfigureEmphasisParticleForMine(particle);
                particle.Play(true);
            }
        }

        ApplyEmphasisIntensity(ownerColor, 1f);
    }

    private void ApplyVisualOwnerBodyColor(Color ownerColor)
    {
        ApplyVisualOwnerBodyColor(ownerColor, OwnerBodyEmissionScale);
    }

    private void ApplyVisualOwnerBodyColor(Color ownerColor, float emissionScale)
    {
        if (visualRenderers == null)
        {
            return;
        }

        Color bodyColor = Color.Lerp(ownerColor, Color.white, 0.08f);
        bodyColor.a = 1f;
        Color emissionColor = ownerColor * Mathf.Max(0f, emissionScale);
        emissionColor.a = 1f;

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer renderer = visualRenderers[i];
            if (!ShouldTintAsOwnerBody(renderer))
            {
                continue;
            }

            SetRendererColor(renderer, bodyColor, emissionColor);
        }
    }

    private void ApplyEmphasisIntensity(Color ownerColor, float intensity)
    {
        if (!HasEmphasisEffect())
        {
            return;
        }

        float clampedIntensity = Mathf.Clamp(intensity, 1f, 8.5f);
        float intensityRatio = Mathf.InverseLerp(1f, 8.5f, clampedIntensity);
        float sizeScale = Mathf.Lerp(1f, 2.8f, intensityRatio);
        float speedScale = Mathf.Lerp(1f, 1.45f, intensityRatio);
        Color rendererGlowColor = GetBrightOwnerEffectColor(ownerColor);
        rendererGlowColor.a = 1f;

        for (int i = 0; i < emphasisParticles.Length; i++)
        {
            ParticleSystem particle = emphasisParticles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            if (emphasisStartSizeMultipliers != null &&
                i < emphasisStartSizeMultipliers.Length)
            {
                main.startSizeMultiplier =
                    emphasisStartSizeMultipliers[i] * sizeScale;
            }

            if (emphasisStartSpeedMultipliers != null &&
                i < emphasisStartSpeedMultipliers.Length)
            {
                main.startSpeedMultiplier =
                    emphasisStartSpeedMultipliers[i] * speedScale;
            }

            ParticleSystem.EmissionModule emission = particle.emission;
            if (emphasisEmissionRateOverTimeMultipliers != null &&
                i < emphasisEmissionRateOverTimeMultipliers.Length)
            {
                emission.rateOverTimeMultiplier =
                    emphasisEmissionRateOverTimeMultipliers[i] * clampedIntensity;
            }

            if (emphasisEmissionRateOverDistanceMultipliers != null &&
                i < emphasisEmissionRateOverDistanceMultipliers.Length)
            {
                emission.rateOverDistanceMultiplier =
                    emphasisEmissionRateOverDistanceMultipliers[i] * clampedIntensity;
            }
        }

        if (emphasisParticleRenderers == null)
        {
            return;
        }

        for (int i = 0; i < emphasisParticleRenderers.Length; i++)
        {
            SetParticleRendererGlowColor(
                emphasisParticleRenderers[i],
                rendererGlowColor,
                clampedIntensity);
        }
    }

    private void SetParticleRendererGlowColor(
        Renderer renderer,
        Color color,
        float intensity)
    {
        if (renderer == null)
        {
            return;
        }

        if (comboGlowMaterialProperties == null)
        {
            comboGlowMaterialProperties = new MaterialPropertyBlock();
        }

        float hdrScale = Mathf.Lerp(
            2.75f,
            7.25f,
            Mathf.InverseLerp(1f, 8.5f, Mathf.Clamp(intensity, 1f, 8.5f)));
        Color hdrColor = color * hdrScale;
        hdrColor.a = 1f;
        Color emissionColor = color * (hdrScale * 1.25f);
        emissionColor.a = 1f;

        comboGlowMaterialProperties.Clear();
        comboGlowMaterialProperties.SetColor(BaseColorPropertyId, hdrColor);
        comboGlowMaterialProperties.SetColor(ColorPropertyId, hdrColor);
        comboGlowMaterialProperties.SetColor(TintColorPropertyId, hdrColor);
        comboGlowMaterialProperties.SetColor(EmissionColorPropertyId, emissionColor);
        renderer.SetPropertyBlock(comboGlowMaterialProperties);
    }

    private static bool ShouldTintAsOwnerBody(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
        {
            return false;
        }

        Transform current = renderer.transform;
        while (current != null)
        {
            string objectName = current.name;
            if (objectName.IndexOf(
                    "Contrast",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "Highlight",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "Rim",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "Plate",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            current = current.parent;
        }

        return true;
    }

    private void CacheEmphasisEffectComponents(GameObject root)
    {
        if (root == null)
        {
            emphasisParticles = null;
            emphasisParticleRenderers = null;
            emphasisStartColors = null;
            emphasisLifetimeColors = null;
            emphasisStartSizeMultipliers = null;
            emphasisStartSpeedMultipliers = null;
            emphasisEmissionRateOverTimeMultipliers = null;
            emphasisEmissionRateOverDistanceMultipliers = null;
            emphasisLifetimeEnabled = null;
            return;
        }

        emphasisParticles = root.GetComponentsInChildren<ParticleSystem>(true);
        emphasisParticleRenderers =
            root.GetComponentsInChildren<ParticleSystemRenderer>(true);
        CacheOriginalEmphasisColors();
        ConfigureEmphasisParticlesForMine();
    }

    private void CacheOriginalEmphasisColors()
    {
        if (emphasisParticles == null)
        {
            emphasisStartColors = null;
            emphasisLifetimeColors = null;
            emphasisStartSizeMultipliers = null;
            emphasisStartSpeedMultipliers = null;
            emphasisEmissionRateOverTimeMultipliers = null;
            emphasisEmissionRateOverDistanceMultipliers = null;
            emphasisLifetimeEnabled = null;
            return;
        }

        int count = emphasisParticles.Length;
        emphasisStartColors = new ParticleSystem.MinMaxGradient[count];
        emphasisLifetimeColors = new ParticleSystem.MinMaxGradient[count];
        emphasisStartSizeMultipliers = new float[count];
        emphasisStartSpeedMultipliers = new float[count];
        emphasisEmissionRateOverTimeMultipliers = new float[count];
        emphasisEmissionRateOverDistanceMultipliers = new float[count];
        emphasisLifetimeEnabled = new bool[count];
        for (int i = 0; i < count; i++)
        {
            ParticleSystem particle = emphasisParticles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            emphasisStartColors[i] = main.startColor;
            emphasisStartSizeMultipliers[i] = main.startSizeMultiplier;
            emphasisStartSpeedMultipliers[i] = main.startSpeedMultiplier;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                particle.colorOverLifetime;
            emphasisLifetimeEnabled[i] = colorOverLifetime.enabled;
            emphasisLifetimeColors[i] = colorOverLifetime.color;
            ParticleSystem.EmissionModule emission = particle.emission;
            emphasisEmissionRateOverTimeMultipliers[i] =
                emission.rateOverTimeMultiplier;
            emphasisEmissionRateOverDistanceMultipliers[i] =
                emission.rateOverDistanceMultiplier;
        }
    }

    private bool HasEmphasisEffect()
    {
        return emphasisParticles != null && emphasisParticles.Length > 0;
    }

    private void ApplyEmphasisLifetimeColor(
        ParticleSystem particle,
        int particleIndex,
        Color ownerColor)
    {
        if (particle == null ||
            emphasisLifetimeEnabled == null ||
            emphasisLifetimeColors == null ||
            particleIndex < 0 ||
            particleIndex >= emphasisLifetimeEnabled.Length ||
            particleIndex >= emphasisLifetimeColors.Length)
        {
            return;
        }

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
            particle.colorOverLifetime;
        colorOverLifetime.enabled = emphasisLifetimeEnabled[particleIndex];
        if (!emphasisLifetimeEnabled[particleIndex])
        {
            return;
        }

        colorOverLifetime.color = TintGradientPreservingShade(
            emphasisLifetimeColors[particleIndex],
            ownerColor);
    }

    private void ConfigureEmphasisParticlesForMine()
    {
        if (emphasisParticles == null)
        {
            return;
        }

        for (int i = 0; i < emphasisParticles.Length; i++)
        {
            ConfigureEmphasisParticleForMine(emphasisParticles[i]);
        }
    }

    private static void ConfigureEmphasisParticleForMine(ParticleSystem particle)
    {
        if (particle == null)
        {
            return;
        }

        ParticleSystem.MainModule main = particle.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = particle.emission;
        emission.enabled = true;
    }

    private static ParticleSystem.MinMaxGradient TintGradientPreservingShade(
        ParticleSystem.MinMaxGradient source,
        Color tintColor)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(
                    TintColorPreservingShade(source.color, tintColor));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(
                    TintColorPreservingShade(source.colorMin, tintColor),
                    TintColorPreservingShade(source.colorMax, tintColor));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(
                    TintGradientPreservingShade(source.gradient, tintColor));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(
                    TintGradientPreservingShade(source.gradientMin, tintColor),
                    TintGradientPreservingShade(source.gradientMax, tintColor));
            case ParticleSystemGradientMode.RandomColor:
                ParticleSystem.MinMaxGradient randomColor =
                    new ParticleSystem.MinMaxGradient(
                        TintGradientPreservingShade(source.gradient, tintColor));
                randomColor.mode = ParticleSystemGradientMode.RandomColor;
                return randomColor;
            default:
                return source;
        }
    }

    private static Gradient TintGradientPreservingShade(
        Gradient source,
        Color tintColor)
    {
        if (source == null)
        {
            return null;
        }

        Gradient tinted = new Gradient();
        GradientColorKey[] colorKeys = source.colorKeys;
        for (int i = 0; i < colorKeys.Length; i++)
        {
            colorKeys[i].color =
                TintColorPreservingShade(colorKeys[i].color, tintColor);
        }

        GradientAlphaKey[] alphaKeys = source.alphaKeys;
        for (int i = 0; i < alphaKeys.Length; i++)
        {
            if (alphaKeys[i].alpha > 0.001f)
            {
                alphaKeys[i].alpha = Mathf.Clamp01(
                    Mathf.Max(alphaKeys[i].alpha * 1.75f, 0.35f));
            }
        }

        tinted.SetKeys(colorKeys, alphaKeys);
        return tinted;
    }

    private static Color TintColorPreservingShade(Color source, Color tintColor)
    {
        Color.RGBToHSV(tintColor, out float hue, out float saturation, out _);
        Color.RGBToHSV(source, out _, out _, out float value);
        float brightValue = Mathf.Lerp(Mathf.Max(value, 0.96f), 1f, 0.72f);
        Color tinted = Color.HSVToRGB(
            hue,
            Mathf.Clamp(saturation * 0.6f, 0.32f, 0.72f),
            brightValue);
        tinted.a = source.a <= 0.001f
            ? 0f
            : Mathf.Clamp01(Mathf.Max(source.a, 0.42f));
        return tinted;
    }

    private static Color GetBrightOwnerEffectColor(Color ownerColor)
    {
        Color brightColor = Color.Lerp(ownerColor, Color.white, 0.68f);
        brightColor.a = 1f;
        return brightColor;
    }

    private void SetEmphasisEffectVisible(bool visible)
    {
        if (!HasEmphasisEffect())
        {
            return;
        }

        if (emphasisParticles != null)
        {
            for (int i = 0; i < emphasisParticles.Length; i++)
            {
                if (emphasisParticles[i] == null)
                {
                    continue;
                }

                if (visible)
                {
                    emphasisParticles[i].Play(true);
                }
                else
                {
                    emphasisParticles[i].Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
    }

    private static void DisablePrefabRuntimeBehaviours(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                behaviours[i].enabled = false;
            }
        }
    }

    private static void SetPrefabCollidersEnabled(GameObject root, bool enabled)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = enabled;
            }
        }
    }

    private void EnsureAccentVisual()
    {
        if (accentRenderer != null)
        {
            return;
        }

        Transform existing = transform.Find("Bomb Mine Accent");
        if (existing != null)
        {
            accentRenderer = existing.GetComponent<Renderer>();
            return;
        }

        GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        accent.name = "Bomb Mine Accent";
        accent.transform.SetParent(transform, false);
        accent.transform.localPosition = Vector3.up * 1.08f;
        accent.transform.localRotation = Quaternion.identity;
        accent.transform.localScale = new Vector3(0.82f, 0.08f, 0.82f);

        Collider collider = accent.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        accentRenderer = accent.GetComponent<Renderer>();
        if (accentRenderer != null)
        {
            accentRenderer.shadowCastingMode = ShadowCastingMode.Off;
            accentRenderer.receiveShadows = false;
        }
    }

    private void DisableAccentVisual()
    {
        if (accentRenderer == null)
        {
            Transform existing = transform.Find("Bomb Mine Accent");
            if (existing != null)
            {
                accentRenderer = existing.GetComponent<Renderer>();
            }
        }

        if (accentRenderer != null)
        {
            accentRenderer.enabled = false;
        }
    }

    private Color GetMineAccentColor()
    {
        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        if (visualSettings != null)
        {
            return Color.Lerp(visualSettings.GetIndicatorColor(owner, 1f), Color.white, 0.1f);
        }

        return NeutralAccentColor;
    }

    private static Material GetBodyMaterial()
    {
        if (bodyMaterial != null)
        {
            return bodyMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        bodyMaterial = new Material(shader);
        bodyMaterial.name = "BombMineBody_EmissiveNeutral";
        bodyMaterial.EnableKeyword("_EMISSION");
        if (bodyMaterial.HasProperty(BaseColorPropertyId))
        {
            bodyMaterial.SetColor(BaseColorPropertyId, BodyColor);
        }

        if (bodyMaterial.HasProperty(ColorPropertyId))
        {
            bodyMaterial.SetColor(ColorPropertyId, BodyColor);
        }

        return bodyMaterial;
    }

}
