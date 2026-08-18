using UnityEngine;

[CreateAssetMenu(menuName = "LGH/Weapons/Weapon Visual Settings", fileName = "WeaponVisualSettings")]
public class WeaponVisualSettings : ScriptableObject
{
    private const string DefaultResourcePath = "WeaponVisualSettings";

    [Header("Timing")]
    [SerializeField] private float placementDelaySeconds = 3f;
    [SerializeField] private float rangePreviewLeadTime = 0.5f;
    [SerializeField] private float gameplayApplyDelayAfterImpact = 0.2f;

    [Header("Marker")]
    [SerializeField] private Material markerMaterial;
    [SerializeField] private float markerMinAlpha = 0.45f;
    [SerializeField] private float markerMaxAlpha = 1f;
    [SerializeField] private float markerSurfaceOffset = 0.14f;
    [SerializeField] private float labelSurfaceOffset = 0.7f;
    [SerializeField] private float pulseMinScale = 0.05f;
    [SerializeField] private float pulseMaxScale = 0.18f;
    [SerializeField, Range(0.1f, 1f)] private float indicatorValueMultiplier = 0.52f;
    [SerializeField, Range(0f, 1f)] private float indicatorMinimumSaturation = 0.86f;
    [SerializeField, Range(0.1f, 1f)] private float warmYellowIndicatorValueMultiplier = 0.78f;
    [SerializeField, Range(0f, 1f)] private float warmYellowIndicatorMinimumValue = 0.68f;
    [SerializeField, Range(0f, 1f)] private float warmYellowIndicatorMaximumValue = 0.82f;

    [Header("Range Preview")]
    [SerializeField] private Material rangeIndicatorMaterial;
    [SerializeField] private float rangeIndicatorAlpha = 0.58f;
    [SerializeField] private float rangeIndicatorTileFill = 0.92f;
    [SerializeField] private float rangeIndicatorHeight = 0.035f;
    [SerializeField] private float rangeIndicatorSurfaceOffset = 0.035f;

    private static WeaponVisualSettings cachedDefault;

    public float PlacementDelaySeconds { get { return Mathf.Max(0.01f, placementDelaySeconds); } }
    public float RangePreviewLeadTime { get { return Mathf.Clamp(rangePreviewLeadTime, 0f, PlacementDelaySeconds); } }
    public float GameplayApplyDelayAfterImpact { get { return Mathf.Max(0f, gameplayApplyDelayAfterImpact); } }
    public float MarkerSurfaceOffset { get { return Mathf.Max(0f, markerSurfaceOffset); } }
    public float LabelSurfaceOffset { get { return Mathf.Max(0f, labelSurfaceOffset); } }
    public float PulseMinScale { get { return Mathf.Max(0f, pulseMinScale); } }
    public float PulseMaxScale { get { return Mathf.Max(PulseMinScale, pulseMaxScale); } }
    public Material MarkerMaterial { get { return markerMaterial; } }
    public Material RangeIndicatorMaterial { get { return rangeIndicatorMaterial; } }
    public float RangeIndicatorTileFill { get { return Mathf.Clamp(rangeIndicatorTileFill, 0.05f, 1.5f); } }
    public float RangeIndicatorHeight { get { return Mathf.Max(0.001f, rangeIndicatorHeight); } }
    public float RangeIndicatorSurfaceOffset { get { return Mathf.Max(0f, rangeIndicatorSurfaceOffset); } }

    public static WeaponVisualSettings GetOrLoadDefault()
    {
        if (cachedDefault != null)
        {
            return cachedDefault;
        }

        cachedDefault = Resources.Load<WeaponVisualSettings>(DefaultResourcePath);
        if (cachedDefault == null)
        {
            cachedDefault = CreateInstance<WeaponVisualSettings>();
        }

        return cachedDefault;
    }

    public Color GetOwnerColor(PaintOwner owner, float alpha)
    {
        Color color = GetOwnerBaseColor(owner);
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    public Color GetIndicatorColor(PaintOwner owner, float alpha)
    {
        Color ownerColor = GetOwnerBaseColor(owner);
        Color.RGBToHSV(ownerColor, out float hue, out float saturation, out float value);
        bool isWarmYellow = hue >= 0.06f && hue <= 0.18f;
        if (!isWarmYellow)
        {
            saturation = Mathf.Max(saturation, Mathf.Clamp01(indicatorMinimumSaturation));
        }

        float valueMultiplier = isWarmYellow
            ? Mathf.Clamp(warmYellowIndicatorValueMultiplier, 0.1f, 1f)
            : Mathf.Clamp(indicatorValueMultiplier, 0.1f, 1f);
        float minimumValue = isWarmYellow
            ? Mathf.Clamp01(warmYellowIndicatorMinimumValue)
            : 0.28f;
        float maximumValue = isWarmYellow
            ? Mathf.Max(minimumValue, Mathf.Clamp01(warmYellowIndicatorMaximumValue))
            : 0.58f;
        value = Mathf.Clamp(
            value * valueMultiplier,
            minimumValue,
            maximumValue);
        Color indicatorColor = Color.HSVToRGB(hue, saturation, value);
        indicatorColor.a = Mathf.Clamp01(alpha);
        return indicatorColor;
    }

    private static Color GetOwnerBaseColor(PaintOwner owner)
    {
        if (TilePaintManager.HasInstance)
        {
            Material ownerMaterial = TilePaintManager.Instance.GetOwnerMaterial(owner);
            if (ownerMaterial != null)
            {
                if (ownerMaterial.HasProperty("_BaseColor"))
                {
                    return ownerMaterial.GetColor("_BaseColor");
                }

                if (ownerMaterial.HasProperty("_Color"))
                {
                    return ownerMaterial.GetColor("_Color");
                }
            }
        }

        switch (owner)
        {
            case PaintOwner.Player1:
                return new Color32(0xE2, 0x4B, 0x4A, 0xFF);
            case PaintOwner.Player2:
                return new Color32(0x37, 0x8A, 0xDD, 0xFF);
            case PaintOwner.Player3:
                return new Color32(0xEF, 0x9F, 0x27, 0xFF);
            case PaintOwner.Player4:
                return new Color32(0x1D, 0x9E, 0x75, 0xFF);
            default:
                return Color.white;
        }
    }

    public Color GetMarkerColor(PaintOwner owner, float normalizedAlpha)
    {
        float alpha = Mathf.Lerp(
            Mathf.Clamp01(markerMinAlpha),
            Mathf.Clamp01(markerMaxAlpha),
            Mathf.Clamp01(normalizedAlpha));
        return GetIndicatorColor(owner, alpha);
    }

    public Color GetRangeIndicatorColor(PaintOwner owner)
    {
        return GetIndicatorColor(owner, rangeIndicatorAlpha);
    }

    public Color GetSkillAimIndicatorColor(PaintOwner owner, bool casting)
    {
        Color ownerColor = GetOwnerColor(owner, casting ? 0.92f : 0.82f);
        Color highlighted = Color.Lerp(ownerColor, Color.white, casting ? 0.16f : 0.06f);
        highlighted.a = ownerColor.a;
        return highlighted;
    }
}
