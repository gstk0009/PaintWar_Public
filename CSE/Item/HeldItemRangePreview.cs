using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class HeldItemRangePreview : MonoBehaviour
{
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private float surfaceOffset = 0.06f;
    [SerializeField] private float ringWidth = 0.12f;
    [SerializeField, Range(24, 160)] private int segmentCount = 96;

    private MaterialPropertyBlock materialProperties;
    private LineRenderer ringRenderer;
    private Transform followTarget;
    private PaintOwner owner;
    private float radius;
    private Vector3[] ringPositions;
    private int currentSegmentCount;
    private bool isVisible;

    public void Show(Transform target, float useRadius, PaintOwner paintOwner)
    {
        if (target == null || useRadius <= 0f)
        {
            Hide();
            return;
        }

        bool changed =
            followTarget != target ||
            !Mathf.Approximately(radius, useRadius) ||
            owner != paintOwner ||
            !isVisible;

        followTarget = target;
        radius = useRadius;
        owner = paintOwner;
        isVisible = true;

        EnsureRingRenderer();
        if (ringRenderer == null)
        {
            return;
        }

        ringRenderer.enabled = true;
        if (changed)
        {
            ApplyVisualSettings();
        }

        RefreshRing();
    }

    public void Hide()
    {
        isVisible = false;
        followTarget = null;
        if (ringRenderer != null)
        {
            ringRenderer.enabled = false;
        }
    }

    private void Update()
    {
        if (!isVisible || followTarget == null)
        {
            return;
        }

        RefreshRing();
    }

    private void EnsureRingRenderer()
    {
        if (ringRenderer != null)
        {
            return;
        }

        GameObject ringObject = new GameObject("HeldItemRangeRing");
        ringObject.transform.SetParent(transform, false);
        ringRenderer = ringObject.AddComponent<LineRenderer>();
        ringRenderer.enabled = false;
        ringRenderer.useWorldSpace = true;
        ringRenderer.loop = true;
        ringRenderer.numCornerVertices = 4;
        ringRenderer.numCapVertices = 4;
        ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;
    }

    private void RefreshRing()
    {
        if (ringRenderer == null || followTarget == null)
        {
            return;
        }

        int segments = Mathf.Max(24, segmentCount);
        if (ringPositions == null || currentSegmentCount != segments)
        {
            currentSegmentCount = segments;
            ringPositions = new Vector3[segments];
            ringRenderer.positionCount = segments;
        }

        Vector3 center = followTarget.position;
        if (TilePaintManager.HasInstance)
        {
            Vector2Int centerIndex = TilePaintManager.Instance.GetGridIndex(center);
            if (TilePaintManager.Instance.IsInsideGrid(centerIndex))
            {
                center.y = TilePaintManager.Instance.GetTileSurfaceY(centerIndex);
            }
        }

        center.y += surfaceOffset;

        float angleStep = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = angleStep * i;
            ringPositions[i] = new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y,
                center.z + Mathf.Sin(angle) * radius);
        }

        ringRenderer.SetPositions(ringPositions);
    }

    private void ApplyVisualSettings()
    {
        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        Material rangeMaterial = visualSettings != null
            ? visualSettings.RangeIndicatorMaterial
            : null;
        Color ringColor = visualSettings != null
            ? Color.Lerp(visualSettings.GetIndicatorColor(owner, 1f), Color.white, 0.08f)
            : new Color(1f, 0.25f, 1f, 1f);
        ringColor.a = 0.96f;

        if (rangeMaterial != null)
        {
            ringRenderer.sharedMaterial = rangeMaterial;
        }

        ringRenderer.widthMultiplier = Mathf.Max(0.01f, ringWidth);
        SetRendererColor(ringRenderer, ringColor);
    }

    private void SetRendererColor(Renderer target, Color color)
    {
        if (target == null)
        {
            return;
        }

        if (materialProperties == null)
        {
            materialProperties = new MaterialPropertyBlock();
        }

        target.GetPropertyBlock(materialProperties);
        materialProperties.SetColor(BaseColorPropertyId, color);
        materialProperties.SetColor(ColorPropertyId, color);
        materialProperties.SetColor(TintColorPropertyId, color);
        materialProperties.SetColor(EmissionColorPropertyId, Color.black);
        target.SetPropertyBlock(materialProperties);
    }

    private void OnDisable()
    {
        Hide();
    }
}
