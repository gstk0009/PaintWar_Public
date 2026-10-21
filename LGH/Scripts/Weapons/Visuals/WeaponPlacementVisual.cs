using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 배치형 무기의 임시 클라이언트 측 예고 표시입니다.
// marker를 즉시 보여주고 폭발 직전에 영향 타일을 표시한 뒤 작은 pool로 돌아갑니다.
public sealed class WeaponPlacementVisual : MonoBehaviour
{
    private const int MaxPooledRangeIndicators = 128;
    private const int MaxPooledPlacementVisualsPerType = 16;
    // 모바일 active battle 중 primitive 반복 생성을 피하기 위한 pool입니다.
    private static readonly Stack<GameObject> RangeIndicatorPool = new Stack<GameObject>();
    private static readonly Dictionary<GameObject, Renderer[]> RangeIndicatorRendererCache =
        new Dictionary<GameObject, Renderer[]>();
    private static readonly Dictionary<GameObject, Stack<GameObject>> PrefabPools =
        new Dictionary<GameObject, Stack<GameObject>>();
    private static readonly Dictionary<PrimitiveType, Stack<WeaponPlacementVisual>> VisualPools =
        new Dictionary<PrimitiveType, Stack<WeaponPlacementVisual>>();
    private static readonly Dictionary<GameObject, Renderer[]> RendererCache =
        new Dictionary<GameObject, Renderer[]>();
    private static readonly Dictionary<GameObject, Collider[]> ColliderCache =
        new Dictionary<GameObject, Collider[]>();
    private static readonly Dictionary<GameObject, TrailRenderer[]> TrailCache =
        new Dictionary<GameObject, TrailRenderer[]>();
    private static readonly Dictionary<GameObject, Light[]> LightCache =
        new Dictionary<GameObject, Light[]>();
    private static readonly Dictionary<GameObject, ParticleSystem[]> ParticleCache =
        new Dictionary<GameObject, ParticleSystem[]>();
    private static readonly Dictionary<GameObject, ParticleTintSnapshot> ParticleTintCache =
        new Dictionary<GameObject, ParticleTintSnapshot>();
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    private WeaponData weapon;
    private PaintOwner owner;
    private Vector3 origin;
    private Vector3 forward;
    private int playerLevel;
    private float delay;
    private float dangerLeadTime;
    private float startTime;
    private MaterialPropertyBlock materialProperties;
    private Renderer markerRenderer;
    private bool rangeShown;
    private bool impactSpawned;
    private WeaponVisualSettings visualSettings;
    private Transform markerTransform;
    private PrimitiveType primitiveType;
    private MagicWeaponVfxProfile vfxProfile;
    private GameObject markerPrefabSource;
    private GameObject markerPrefabInstance;
    private readonly List<GameObject> rentedRangeIndicators = new List<GameObject>();

    public static void Create(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel,
        WeaponVisualSettings visualSettings)
    {
        // dedicated server에는 시각적 예고 표시가 필요 없습니다.
        if (Application.isBatchMode)
        {
            return;
        }

        if (weapon == null)
        {
            return;
        }

        WeaponPlacementVisual visual = RentVisual(GetPrimitiveForWeapon(weapon));
        visual.gameObject.name = "PlacedWeapon_" + weapon.WeaponId;
        visual.Initialize(weapon, origin, forward, owner, playerLevel, visualSettings);
    }

    private void Initialize(
        WeaponData newWeapon,
        Vector3 newOrigin,
        Vector3 newForward,
        PaintOwner newOwner,
        int newPlayerLevel,
        WeaponVisualSettings newVisualSettings)
    {
        EnsureMaterialProperties();
        weapon = newWeapon;
        owner = newOwner;
        visualSettings = newVisualSettings != null ? newVisualSettings : WeaponVisualSettings.GetOrLoadDefault();
        vfxProfile = weapon != null ? weapon.VfxProfile : null;
        // marker와 지연 점령이 같은 grid origin을 쓰도록 타일 중심에 snap합니다.
        origin = GetSnappedOrigin(newOrigin);
        forward = newForward.sqrMagnitude > 0.001f ? newForward.normalized : Vector3.forward;
        playerLevel = Mathf.Max(1, newPlayerLevel);
        delay = visualSettings.PlacementDelaySeconds;
        dangerLeadTime = visualSettings.RangePreviewLeadTime;
        startTime = Time.time;
        rangeShown = false;
        impactSpawned = false;
        gameObject.SetActive(true);

        transform.position = origin;
        // The old post-confirm marker has been retired. Aiming is now shown before
        // confirmation by SkillAimAreaVisual; this visual keeps only impact warning/VFX.
        ReleaseMarker();
        PlayProfileSfx(vfxProfile != null ? vfxProfile.MarkerAudioClip : null, origin, 0.94f);
    }

    private void Update()
    {
        float elapsed = Time.time - startTime;
        float remaining = delay - elapsed;

        // 전체 delay 동안 화면을 어지럽히지 않으면서 위험 신호를 주기 위해 range indicator는 늦게 표시합니다.
        if (!rangeShown && remaining <= dangerLeadTime)
        {
            ShowRangeIndicators();
        }

        float impactTime = Mathf.Max(0f, delay - GetImpactVisualLeadSeconds());
        if (!impactSpawned && elapsed >= impactTime)
        {
            SpawnImpactEffect();
            impactSpawned = true;
        }

        if (elapsed >= delay + visualSettings.GameplayApplyDelayAfterImpact)
        {
            ReturnToPool();
        }
    }

    private void EnsureMarker()
    {
        GameObject desiredPrefab =
            vfxProfile != null ? vfxProfile.CastMarkerPrefab : null;
        if (markerTransform != null && markerPrefabSource == desiredPrefab)
        {
            return;
        }

        ReleaseMarker();
        BuildMarker(desiredPrefab);
    }

    private void BuildMarker(GameObject markerPrefab)
    {
        GameObject marker;
        markerPrefabSource = markerPrefab;
        if (markerPrefab != null)
        {
            marker = RentPrefab(markerPrefab);
            markerPrefabInstance = marker;
        }
        else
        {
            marker = GameObject.CreatePrimitive(primitiveType);
            DisableAndDestroyCollider(marker);
        }

        marker.name = "Marker";
        marker.transform.SetParent(transform, false);
        marker.transform.localPosition = Vector3.up * GetSurfaceYOffset(visualSettings.MarkerSurfaceOffset);
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = GetMarkerScale();
        markerTransform = marker.transform;
        SetCollidersEnabled(marker, false);

        markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            if (visualSettings.MarkerMaterial != null)
            {
                markerRenderer.sharedMaterial = visualSettings.MarkerMaterial;
            }

            SetRendererColor(markerRenderer, visualSettings.GetIndicatorColor(owner, 0.95f));
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
        }
        else
        {
            ApplyTint(marker, GetIndicatorVisualColor(0.95f));
        }

    }

    private void ConfigureMarker()
    {
        markerTransform.localPosition = Vector3.up * GetSurfaceYOffset(visualSettings.MarkerSurfaceOffset);
        markerTransform.localScale = GetMarkerScale();
        markerTransform.gameObject.SetActive(true);
        if (markerRenderer != null)
        {
            if (visualSettings.MarkerMaterial != null)
            {
                markerRenderer.sharedMaterial = visualSettings.MarkerMaterial;
            }

            SetRendererColor(markerRenderer, visualSettings.GetIndicatorColor(owner, 0.95f));
        }

        if (markerPrefabInstance != null)
        {
            ApplyTint(markerPrefabInstance, GetIndicatorVisualColor(0.95f));
        }
    }

    private void PulseMarker(float normalizedTime, float remaining)
    {
        float flashSpeed = Mathf.Lerp(3f, 16f, normalizedTime);
        float flash = (Mathf.Sin(Time.time * flashSpeed) + 1f) * 0.5f;
        Color color = Color.Lerp(visualSettings.GetMarkerColor(owner, 0f), visualSettings.GetMarkerColor(owner, 1f), flash);

        if (remaining <= dangerLeadTime)
        {
            // 파스텔 타일 위에서도 색이 날아가지 않도록 흰색 플래시 대신
            // 같은 진한 표시색의 불투명도만 높입니다.
            Color alertColor = GetIndicatorVisualColor(1f);
            color = Color.Lerp(color, alertColor, 0.35f + flash * 0.4f);
        }

        if (markerRenderer != null)
        {
            SetRendererColor(markerRenderer, color);
        }

        float scale = 1f + flash * Mathf.Lerp(visualSettings.PulseMinScale, visualSettings.PulseMaxScale, normalizedTime);
        if (markerTransform != null)
        {
            markerTransform.localScale = GetMarkerScale() * scale;
        }

    }

    private void ShowRangeIndicators()
    {
        // 미리보기와 실제 결과가 일치하도록 실제 점령과 같은 pattern resolver를 사용합니다.
        rangeShown = true;
        if (!TilePaintManager.HasInstance)
        {
            return;
        }

        float tileSize = TilePaintManager.Instance.TileSize;
        Vector2Int originIndex = TilePaintManager.Instance.GetGridIndex(origin);
        IEnumerable<Vector2Int> offsets = WeaponPatternResolver.GetOffsets(weapon, forward, playerLevel);
        float surfaceY = GetSurfaceY();
        Material rangeMaterial = visualSettings.RangeIndicatorMaterial;
        Color fillColor = GetRangeFillColor();
        Color outlineColor = GetRangeOutlineColor();

        foreach (Vector2Int offset in offsets)
        {
            Vector2Int index = originIndex + offset;
            if (!TilePaintManager.Instance.IsInsideGrid(index) ||
                TilePaintManager.Instance.IsTileDestroyed(index))
            {
                continue;
            }

            if (GameRoundManager.HasInstance &&
                !GameRoundManager.Instance.CanOwnerClaimTile(owner, index))
            {
                continue;
            }

            GameObject indicator = RentTelegraphIndicator();
            indicator.transform.SetParent(transform, true);
            Vector3 indicatorPosition = TilePaintManager.Instance.GetTileCenterWorldPosition(index);
            indicatorPosition.y = surfaceY + visualSettings.RangeIndicatorSurfaceOffset;
            indicator.transform.position = indicatorPosition;
            indicator.transform.localScale = new Vector3(
                tileSize * visualSettings.RangeIndicatorTileFill,
                visualSettings.RangeIndicatorHeight,
                tileSize * visualSettings.RangeIndicatorTileFill) * GetTelegraphScale();
            SetCollidersEnabled(indicator, false);
            rentedRangeIndicators.Add(indicator);

            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (rangeMaterial != null)
                {
                    renderer.sharedMaterial = rangeMaterial;
                }

                SetRendererColor(renderer, fillColor);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            else
            {
                ApplyTint(indicator, fillColor);
            }

            Renderer[] renderers = GetRangeIndicatorRenderers(indicator);
            if (renderers.Length > 0)
            {
                SetRangeIndicatorColors(renderers, fillColor, outlineColor, rangeMaterial);
            }
        }
    }

    private void OnDestroy()
    {
        ReturnRangeIndicators();
    }

    private static WeaponPlacementVisual RentVisual(PrimitiveType primitive)
    {
        // primitive shape마다 child marker geometry가 달라서 별도 pool을 사용합니다.
        if (VisualPools.TryGetValue(primitive, out Stack<WeaponPlacementVisual> pool))
        {
            while (pool.Count > 0)
            {
                WeaponPlacementVisual pooled = pool.Pop();
                if (pooled != null) return pooled;
            }
        }

        GameObject root = new GameObject("Weapon Placement Visual");
        WeaponPlacementVisual created = root.AddComponent<WeaponPlacementVisual>();
        created.primitiveType = primitive;
        return created;
    }

    private void ReturnToPool()
    {
        ReturnRangeIndicators();
        gameObject.SetActive(false);
        if (!VisualPools.TryGetValue(primitiveType, out Stack<WeaponPlacementVisual> pool))
        {
            pool = new Stack<WeaponPlacementVisual>();
            VisualPools.Add(primitiveType, pool);
        }

        if (pool.Count < MaxPooledPlacementVisualsPerType) pool.Push(this);
        else Destroy(gameObject);
    }

    private void SpawnImpactEffect()
    {
        PlayProfileSfx(vfxProfile != null ? vfxProfile.ImpactAudioClip : null, origin, 1f);

        if (vfxProfile == null || vfxProfile.ImpactPrefab == null)
        {
            return;
        }

        GameObject impact = RentPrefab(vfxProfile.ImpactPrefab);
        if (impact == null)
        {
            return;
        }

        impact.name = "Impact_" + weapon.WeaponId;
        Vector3 impactPosition = origin;
        impactPosition.y = GetSurfaceY() + vfxProfile.ImpactSurfaceOffset;
        impact.transform.position = impactPosition;
        impact.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        impact.transform.localScale = Vector3.one * vfxProfile.ImpactScale * GetImpactLevelScale();
        SetCollidersEnabled(impact, false);
        // 원소 고유색을 유지한 채 VFX 프로필의 ownerTintAmount만큼
        // 플레이어색을 섞어, 반대 계열 색에서도 파티클이 어둡게 죽지 않게 합니다.
        ApplyImpactTint(impact, GetProfileTintColor(1f));
        ApplyPlaybackSpeed(impact, vfxProfile.ImpactPlaybackSpeed);

        MagicWeaponPooledVfxReturn returner = impact.GetComponent<MagicWeaponPooledVfxReturn>();
        if (returner == null)
        {
            returner = impact.AddComponent<MagicWeaponPooledVfxReturn>();
        }

        returner.Initialize(vfxProfile.ImpactPrefab, vfxProfile.ImpactLifetimeSeconds, ReturnPrefab);
    }

    private static GameObject RentRangeIndicator()
    {
        while (RangeIndicatorPool.Count > 0)
        {
            GameObject pooled = RangeIndicatorPool.Pop();
            if (pooled != null)
            {
                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject created = new GameObject("RangeIndicator");
        CreateRangeBorder(created.transform, "RangeBorderNorth", new Vector3(0f, 0.1f, 0.5f), new Vector3(1.08f, 1.25f, 0.055f));
        CreateRangeBorder(created.transform, "RangeBorderSouth", new Vector3(0f, 0.1f, -0.5f), new Vector3(1.08f, 1.25f, 0.055f));
        CreateRangeBorder(created.transform, "RangeBorderEast", new Vector3(0.5f, 0.1f, 0f), new Vector3(0.055f, 1.25f, 1.08f));
        CreateRangeBorder(created.transform, "RangeBorderWest", new Vector3(-0.5f, 0.1f, 0f), new Vector3(0.055f, 1.25f, 1.08f));

        RangeIndicatorRendererCache[created] = created.GetComponentsInChildren<Renderer>(true);
        return created;
    }

    private static void CreateRangeBorder(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
    {
        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
        border.name = name;
        DisableAndDestroyCollider(border);
        border.transform.SetParent(parent, false);
        border.transform.localPosition = localPosition;
        border.transform.localScale = localScale;
    }

    private GameObject RentTelegraphIndicator()
    {
        if (vfxProfile != null && vfxProfile.TelegraphTilePrefab != null)
        {
            return RentPrefab(vfxProfile.TelegraphTilePrefab);
        }

        return RentRangeIndicator();
    }

    private void ReturnRangeIndicators()
    {
        // 다음 marker가 깨끗하게 시작하도록 pooling 전에 indicator를 이 visual에서 분리합니다.
        for (int i = 0; i < rentedRangeIndicators.Count; i++)
        {
            GameObject indicator = rentedRangeIndicators[i];
            if (indicator == null)
            {
                continue;
            }

            indicator.transform.SetParent(null, false);
            indicator.SetActive(false);
            MagicWeaponPooledVfxReturn pooledPrefab = indicator.GetComponent<MagicWeaponPooledVfxReturn>();
            if (pooledPrefab != null && pooledPrefab.SourcePrefab != null)
            {
                ReturnPrefab(pooledPrefab.SourcePrefab, indicator);
            }
            else if (RangeIndicatorPool.Count < MaxPooledRangeIndicators)
            {
                RangeIndicatorPool.Push(indicator);
            }
            else
            {
                Destroy(indicator);
            }
        }

        rentedRangeIndicators.Clear();
    }

    private void ReleaseMarker()
    {
        if (markerTransform == null)
        {
            return;
        }

        if (markerPrefabInstance != null && markerPrefabSource != null)
        {
            markerPrefabInstance.transform.SetParent(null, false);
            ReturnPrefab(markerPrefabSource, markerPrefabInstance);
        }
        else
        {
            Destroy(markerTransform.gameObject);
        }

        markerTransform = null;
        markerRenderer = null;
        markerPrefabSource = null;
        markerPrefabInstance = null;
    }

    private static void DisableAndDestroyCollider(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        Collider collider = target.GetComponent<Collider>();
        if (collider == null)
        {
            return;
        }

        collider.enabled = false;
        Destroy(collider);
    }

    private static void SetCollidersEnabled(GameObject target, bool enabled)
    {
        if (target == null)
        {
            return;
        }

        Collider[] colliders = GetCachedComponents(target, ColliderCache);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
            {
                continue;
            }

            colliders[i].enabled = enabled;
        }
    }

    private Vector3 GetSnappedOrigin(Vector3 worldPosition)
    {
        if (!TilePaintManager.HasInstance)
        {
            return worldPosition;
        }

        Vector2Int index = TilePaintManager.Instance.GetGridIndex(worldPosition);
        return TilePaintManager.Instance.GetTileCenterWorldPosition(index);
    }

    private float GetSurfaceY()
    {
        float tileSize = TilePaintManager.HasInstance ? TilePaintManager.Instance.TileSize : 1f;
        return origin.y + tileSize * 0.5f;
    }

    private float GetSurfaceYOffset(float extra)
    {
        return (TilePaintManager.HasInstance ? TilePaintManager.Instance.TileSize * 0.5f : 0.5f) + extra;
    }

    private static PrimitiveType GetPrimitiveForWeapon(WeaponData weapon)
    {
        return PrimitiveType.Cylinder;
    }

    private Vector3 GetMarkerScale()
    {
        float tileSize = TilePaintManager.HasInstance ? TilePaintManager.Instance.TileSize : 1f;
        float scale = vfxProfile != null ? vfxProfile.CastMarkerScale : 0.42f;
        if (markerPrefabInstance != null)
        {
            return Vector3.one * (tileSize * scale);
        }

        return new Vector3(tileSize * scale, tileSize * 0.06f, tileSize * scale);
    }

    private static void PlayProfileSfx(AudioClip clip, Vector3 worldPosition, float volumeScale)
    {
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.PlaySfxAt(clip, worldPosition, volumeScale);
        }
    }

    private float GetTelegraphScale()
    {
        return vfxProfile != null && vfxProfile.TelegraphTilePrefab != null
            ? vfxProfile.TelegraphTileScale
            : 1f;
    }

    private float GetImpactLevelScale()
    {
        if (vfxProfile == null)
        {
            return 1f;
        }

        if (playerLevel >= 5)
        {
            return vfxProfile.ImpactLevel5ScaleMultiplier;
        }

        if (playerLevel >= 3)
        {
            return vfxProfile.ImpactLevel3ScaleMultiplier;
        }

        return 1f;
    }

    private float GetImpactVisualLeadSeconds()
    {
        return vfxProfile != null ? vfxProfile.ImpactVisualLeadSeconds : 0f;
    }

    private void SetRendererColor(Renderer targetRenderer, Color color)
    {
        // MaterialPropertyBlock으로 미리보기 오브젝트마다 고유 material을 만들지 않고 색을 바꿉니다.
        if (targetRenderer == null) return;
        EnsureMaterialProperties();
        targetRenderer.GetPropertyBlock(materialProperties);
        materialProperties.SetColor(BaseColorPropertyId, color);
        materialProperties.SetColor(ColorPropertyId, color);
        materialProperties.SetColor(TintColorPropertyId, color);
        materialProperties.SetColor(EmissionColorPropertyId, Color.black);
        targetRenderer.SetPropertyBlock(materialProperties);
    }

    private Color GetProfileTintColor(float alpha)
    {
        Color ownerColor = visualSettings.GetOwnerColor(owner, alpha);
        Color tint = vfxProfile != null ? vfxProfile.GetTintedColor(ownerColor) : ownerColor;
        tint.a = alpha;
        return tint;
    }

    private Color GetOwnerVisualColor(float alpha)
    {
        return visualSettings.GetOwnerColor(owner, alpha);
    }

    private Color GetIndicatorVisualColor(float alpha)
    {
        return visualSettings.GetIndicatorColor(owner, alpha);
    }

    private Color GetRangeFillColor()
    {
        Color color = Color.clear;
        return color;
    }

    private Color GetRangeOutlineColor()
    {
        Color color = Color.Lerp(GetIndicatorVisualColor(1f), Color.white, 0.08f);
        color.a = 0.96f;
        return color;
    }

    private void SetMarkerVisible(bool visible)
    {
        if (markerTransform != null)
        {
            markerTransform.gameObject.SetActive(visible);
        }

    }

    private static Renderer[] GetRangeIndicatorRenderers(GameObject indicator)
    {
        if (indicator == null)
        {
            return System.Array.Empty<Renderer>();
        }

        if (RangeIndicatorRendererCache.TryGetValue(indicator, out Renderer[] renderers) && renderers != null)
        {
            return renderers;
        }

        renderers = indicator.GetComponentsInChildren<Renderer>(true);
        RangeIndicatorRendererCache[indicator] = renderers;
        return renderers;
    }

    private void SetRangeIndicatorColors(Renderer[] renderers, Color fillColor, Color outlineColor, Material rangeMaterial)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer target = renderers[i];
            if (target == null)
            {
                continue;
            }

            if (rangeMaterial != null)
            {
                target.sharedMaterial = rangeMaterial;
            }

            bool isOutline = target.gameObject.name.Contains("Outline") ||
                target.gameObject.name.Contains("Border");
            SetRendererColor(target, isOutline ? outlineColor : fillColor);
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
        }
    }

    private void ApplyTint(GameObject root, Color color)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = GetCachedComponents(root, RendererCache);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            SetRendererColor(
                renderers[i],
                UsesParticleHueColorMode(renderers[i])
                    ? GetColorBlendBaseColor(color.a)
                    : color);
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
        }

        // Hovl prefabs rely on authored particle gradients/alpha for readability.
        // Player color is shown through markers, telegraphs, trails and lights instead.

        TrailRenderer[] trails = GetCachedComponents(root, TrailCache);
        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i] == null)
            {
                continue;
            }

            trails[i].startColor = color;
            Color endColor = color;
            endColor.a *= 0.15f;
            trails[i].endColor = endColor;
        }

        Light[] lights = GetCachedComponents(root, LightCache);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null)
            {
                continue;
            }

            lights[i].color = color;
        }
    }

    private Color GetColorBlendBaseColor(float alpha)
    {
        float luminance = vfxProfile != null
            ? vfxProfile.ColorBlendLuminanceMultiplier
            : 1f;
        return new Color(luminance, luminance, luminance, alpha);
    }

    private void ApplyImpactTint(GameObject root, Color color)
    {
        ApplyNeutralParticleColors(root);
        ApplyParticleHueForColorBlendMaterials(root, color);
        ApplyTint(root, color);
    }

    private static void ApplyParticleHueForColorBlendMaterials(GameObject root, Color color)
    {
        ParticleSystem[] particles = GetCachedComponents(root, ParticleCache);
        if (!ParticleTintCache.TryGetValue(root, out ParticleTintSnapshot snapshot) ||
            snapshot == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystemRenderer particleRenderer = particle.GetComponent<ParticleSystemRenderer>();
            if (!UsesParticleHueColorMode(particleRenderer))
            {
                continue;
            }

            ParticleSystem.MinMaxGradient neutralStartColor = snapshot.StartColors[i];
            ParticleSystem.MainModule main = particle.main;
            if (neutralStartColor.mode == ParticleSystemGradientMode.Color)
            {
                main.startColor = new ParticleSystem.MinMaxGradient(
                    MultiplyRgb(neutralStartColor.color, color));
            }
            else if (neutralStartColor.mode == ParticleSystemGradientMode.TwoColors)
            {
                main.startColor = new ParticleSystem.MinMaxGradient(
                    MultiplyRgb(neutralStartColor.colorMin, color),
                    MultiplyRgb(neutralStartColor.colorMax, color));
            }
        }
    }

    private static bool UsesParticleHueColorMode(Renderer targetRenderer)
    {
        if (targetRenderer == null)
        {
            return false;
        }

        Material material = targetRenderer.sharedMaterial;
        return material != null && material.IsKeywordEnabled("_COLORCOLOR_ON");
    }

    private static Color MultiplyRgb(Color intensity, Color tint)
    {
        return new Color(
            intensity.r * tint.r,
            intensity.g * tint.g,
            intensity.b * tint.b,
            intensity.a * tint.a);
    }

    private static void ApplyNeutralParticleColors(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        if (!ParticleTintCache.TryGetValue(root, out ParticleTintSnapshot snapshot) ||
            snapshot == null ||
            snapshot.Particles == null ||
            HasDestroyedComponent(snapshot.Particles))
        {
            snapshot = CreateParticleTintSnapshot(root);
            ParticleTintCache[root] = snapshot;
        }

        for (int i = 0; i < snapshot.Particles.Length; i++)
        {
            ParticleSystem particle = snapshot.Particles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.startColor = snapshot.StartColors[i];

            if (snapshot.HasColorOverLifetime[i])
            {
                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
                colorOverLifetime.color = snapshot.ColorOverLifetime[i];
            }
        }
    }

    private static ParticleTintSnapshot CreateParticleTintSnapshot(GameObject root)
    {
        ParticleSystem[] particles = GetCachedComponents(root, ParticleCache);
        ParticleTintSnapshot snapshot = new ParticleTintSnapshot(particles);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            snapshot.StartColors[i] = CreateNeutralGradient(particle.main.startColor);
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
            snapshot.HasColorOverLifetime[i] = colorOverLifetime.enabled;
            if (colorOverLifetime.enabled)
            {
                snapshot.ColorOverLifetime[i] = CreateNeutralGradient(colorOverLifetime.color);
            }
        }

        return snapshot;
    }

    private static ParticleSystem.MinMaxGradient CreateNeutralGradient(
        ParticleSystem.MinMaxGradient source)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(ToNeutralColor(source.color));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(
                    ToNeutralColor(source.colorMin),
                    ToNeutralColor(source.colorMax));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(ToNeutralGradient(source.gradient));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(
                    ToNeutralGradient(source.gradientMin),
                    ToNeutralGradient(source.gradientMax));
            case ParticleSystemGradientMode.RandomColor:
                ParticleSystem.MinMaxGradient randomColor =
                    new ParticleSystem.MinMaxGradient(ToNeutralGradient(source.gradient));
                randomColor.mode = ParticleSystemGradientMode.RandomColor;
                return randomColor;
            default:
                return source;
        }
    }

    private static Gradient ToNeutralGradient(Gradient source)
    {
        if (source == null)
        {
            return new Gradient();
        }

        GradientColorKey[] sourceColorKeys = source.colorKeys;
        GradientColorKey[] colorKeys = new GradientColorKey[sourceColorKeys.Length];
        for (int i = 0; i < sourceColorKeys.Length; i++)
        {
            colorKeys[i] = new GradientColorKey(
                ToNeutralColor(sourceColorKeys[i].color),
                sourceColorKeys[i].time);
        }

        Gradient result = new Gradient();
        result.mode = source.mode;
        result.SetKeys(colorKeys, source.alphaKeys);
        return result;
    }

    private static Color ToNeutralColor(Color source)
    {
        float intensity = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
        return new Color(intensity, intensity, intensity, source.a);
    }

    private static void ApplyPlaybackSpeed(GameObject root, float speed)
    {
        if (root == null)
        {
            return;
        }

        ParticleSystem[] particles = GetCachedComponents(root, ParticleCache);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particles[i].main;
            main.simulationSpeed = speed;
        }
    }

    private static GameObject RentPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (PrefabPools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject pooled = pool.Pop();
                if (pooled != null)
                {
                    pooled.SetActive(true);
                    return pooled;
                }
            }
        }

        GameObject created = Instantiate(prefab);
        MagicWeaponPooledVfxReturn returner = created.GetComponent<MagicWeaponPooledVfxReturn>();
        if (returner == null)
        {
            returner = created.AddComponent<MagicWeaponPooledVfxReturn>();
        }

        returner.SetSource(prefab);
        return created;
    }

    private static void ReturnPrefab(GameObject prefab, GameObject instance)
    {
        if (prefab == null || instance == null)
        {
            return;
        }

        instance.transform.SetParent(null, false);
        instance.SetActive(false);
        if (!PrefabPools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>();
            PrefabPools.Add(prefab, pool);
        }

        if (pool.Count < MaxPooledRangeIndicators)
        {
            pool.Push(instance);
        }
        else
        {
            RendererCache.Remove(instance);
            ColliderCache.Remove(instance);
            TrailCache.Remove(instance);
            LightCache.Remove(instance);
            ParticleCache.Remove(instance);
            ParticleTintCache.Remove(instance);
            Destroy(instance);
        }
    }

    private void EnsureMaterialProperties()
    {
        if (materialProperties == null)
        {
            materialProperties = new MaterialPropertyBlock();
        }
    }

    private static T[] GetCachedComponents<T>(GameObject root, Dictionary<GameObject, T[]> cache)
        where T : Component
    {
        if (root == null)
        {
            return System.Array.Empty<T>();
        }

        if (cache.TryGetValue(root, out T[] components) &&
            components != null &&
            !HasDestroyedComponent(components))
        {
            return components;
        }

        components = root.GetComponentsInChildren<T>(true);
        cache[root] = components;
        return components;
    }

    private static bool HasDestroyedComponent<T>(T[] components)
        where T : Component
    {
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ParticleTintSnapshot
    {
        public readonly ParticleSystem[] Particles;
        public readonly ParticleSystem.MinMaxGradient[] StartColors;
        public readonly bool[] HasColorOverLifetime;
        public readonly ParticleSystem.MinMaxGradient[] ColorOverLifetime;

        public ParticleTintSnapshot(ParticleSystem[] particles)
        {
            Particles = particles;
            int count = particles != null ? particles.Length : 0;
            StartColors = new ParticleSystem.MinMaxGradient[count];
            HasColorOverLifetime = new bool[count];
            ColorOverLifetime = new ParticleSystem.MinMaxGradient[count];
        }
    }

}
