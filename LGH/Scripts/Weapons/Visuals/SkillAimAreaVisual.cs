using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-authored, pooled tile preview used while the local player aims a skill.
public sealed class SkillAimAreaVisual : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private GameObject tileIndicatorPrefab;
    [SerializeField, Range(0.1f, 2f)] private float aimingPulseSpeed = 0.9f;
    [SerializeField, Range(0.1f, 4f)] private float castingPulseSpeed = 2.4f;

    private readonly List<GameObject> indicators = new List<GameObject>(25);
    private readonly List<Renderer> renderers = new List<Renderer>(25);
    private readonly List<Vector3> baseScales = new List<Vector3>(25);
    private MaterialPropertyBlock properties;
    private WeaponVisualSettings settings;
    private PaintOwner owner;
    private bool casting;

    public bool IsConfigured { get { return tileIndicatorPrefab != null; } }

    private void Awake()
    {
        EnsureProperties();
    }

    public void Show(
        IReadOnlyList<Vector2Int> offsets,
        Vector2Int originIndex,
        TilePaintManager tileManager,
        PaintOwner newOwner,
        WeaponVisualSettings newSettings,
        bool isCasting)
    {
        if (offsets == null || tileManager == null || tileIndicatorPrefab == null)
        {
            Hide();
            return;
        }

        settings = newSettings != null ? newSettings : WeaponVisualSettings.GetOrLoadDefault();
        owner = newOwner;
        casting = isCasting;
        EnsureCapacity(offsets.Count);

        int visibleCount = 0;
        float tileSize = tileManager.TileSize;
        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2Int index = originIndex + offsets[i];
            if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
            {
                continue;
            }

            GameObject indicator = indicators[visibleCount];
            Vector3 position = tileManager.GetTileCenterWorldPosition(index);
            float surfaceY = tileManager.GetTileSurfaceY(index);
            if (ObstacleFieldManager.HasInstance &&
                ObstacleFieldManager.Instance.TryGetObstacleSurfaceY(index, out float obstacleSurfaceY))
            {
                surfaceY = obstacleSurfaceY;
            }

            position.y = surfaceY + settings.RangeIndicatorSurfaceOffset;
            indicator.transform.position = position;
            Vector3 scale = new Vector3(
                tileSize * settings.RangeIndicatorTileFill,
                settings.RangeIndicatorHeight,
                tileSize * settings.RangeIndicatorTileFill);
            indicator.transform.localScale = scale;
            baseScales[visibleCount] = scale;
            indicator.SetActive(true);

            Renderer renderer = renderers[visibleCount];
            if (renderer != null)
            {
                if (settings.RangeIndicatorMaterial != null)
                {
                    renderer.sharedMaterial = settings.RangeIndicatorMaterial;
                }
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                SetRendererColor(renderer, settings.GetSkillAimIndicatorColor(owner, casting));
            }

            visibleCount++;
        }

        for (int i = visibleCount; i < indicators.Count; i++)
        {
            indicators[i].SetActive(false);
        }

        gameObject.SetActive(true);
    }

    public void SetCasting(bool value)
    {
        casting = value;
        if (settings == null)
        {
            return;
        }

        Color color = settings.GetSkillAimIndicatorColor(owner, casting);
        for (int i = 0; i < renderers.Count; i++)
        {
            if (indicators[i].activeSelf && renderers[i] != null)
            {
                SetRendererColor(renderers[i], color);
            }
        }
    }

    public void Hide()
    {
        for (int i = 0; i < indicators.Count; i++)
        {
            indicators[i].SetActive(false);
        }

        gameObject.SetActive(false);
    }

    private void Update()
    {
        float speed = casting ? castingPulseSpeed : aimingPulseSpeed;
        float pulse = 1f + (Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f) + 1f) *
            (casting ? 0.06f : 0.025f);
        for (int i = 0; i < indicators.Count; i++)
        {
            if (indicators[i].activeSelf)
            {
                indicators[i].transform.localScale = baseScales[i] * pulse;
            }
        }
    }

    private void EnsureCapacity(int requiredCount)
    {
        while (indicators.Count < requiredCount)
        {
            GameObject indicator = Instantiate(tileIndicatorPrefab, transform);
            indicator.name = "Aim Tile " + indicators.Count;
            Collider[] colliders = indicator.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Renderer renderer = indicator.GetComponentInChildren<Renderer>(true);
            indicators.Add(indicator);
            renderers.Add(renderer);
            baseScales.Add(Vector3.one);
        }
    }

    private void SetRendererColor(Renderer renderer, Color color)
    {
        EnsureProperties();
        renderer.GetPropertyBlock(properties);
        properties.SetColor(BaseColorId, color);
        properties.SetColor(ColorId, color);
        properties.SetColor(EmissionColorId, color * 1.35f);
        renderer.SetPropertyBlock(properties);
        properties.Clear();
    }

    private void EnsureProperties()
    {
        if (properties == null)
        {
            properties = new MaterialPropertyBlock();
        }
    }
}
