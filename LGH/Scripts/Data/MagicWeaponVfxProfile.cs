using UnityEngine;

[CreateAssetMenu(menuName = "LGH/Weapons/Magic Weapon VFX Profile", fileName = "MagicWeaponVfxProfile")]
public sealed class MagicWeaponVfxProfile : ScriptableObject
{
    [Header("Prefabs")]
    [SerializeField] private GameObject castMarkerPrefab;
    [SerializeField] private GameObject telegraphTilePrefab;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private GameObject casterAuraPrefab;
    [SerializeField] private GameObject hitPrefab;

    [Header("Audio")]
    [SerializeField] private AudioClip markerAudioClip;
    [SerializeField] private AudioClip impactAudioClip;

    [Header("Tint")]
    [SerializeField] private Color elementColor = Color.white;
    [Range(0f, 1f)]
    [SerializeField] private float ownerTintAmount = 0.4f;
    [Range(0f, 0.5f)]
    [SerializeField] private float ownerTintVisibilityBoost = 0.15f;
    [Range(0.4f, 1f)]
    [SerializeField] private float effectTintMaxBrightness = 0.78f;
    [Range(0.4f, 1f)]
    [SerializeField] private float colorBlendLuminanceMultiplier = 1f;

    [Header("Scale")]
    [SerializeField] private float castMarkerScale = 1f;
    [SerializeField] private float telegraphTileScale = 1f;
    [SerializeField] private float impactScale = 1f;
    [SerializeField] private float impactLevel3ScaleMultiplier = 1.25f;
    [SerializeField] private float impactLevel5ScaleMultiplier = 1.5f;
    [SerializeField] private float auraScale = 1f;

    [Header("Impact Placement")]
    [SerializeField] private float impactSurfaceOffset = 0.03f;
    [SerializeField] private float impactPlaybackSpeed = 1f;
    [SerializeField] private float impactVisualLeadSeconds;

    [Header("Lifetime")]
    [SerializeField] private float impactLifetimeSeconds = 2f;
    [SerializeField] private float auraLifetimeSeconds = 1f;

    public GameObject CastMarkerPrefab { get { return castMarkerPrefab; } }
    public GameObject TelegraphTilePrefab { get { return telegraphTilePrefab; } }
    public GameObject ImpactPrefab { get { return impactPrefab; } }
    public GameObject CasterAuraPrefab { get { return casterAuraPrefab; } }
    public GameObject HitPrefab { get { return hitPrefab; } }
    public AudioClip MarkerAudioClip { get { return markerAudioClip; } }
    public AudioClip ImpactAudioClip { get { return impactAudioClip; } }
    public Color ElementColor { get { return elementColor; } }
    public float OwnerTintAmount { get { return Mathf.Clamp01(ownerTintAmount); } }
    public float OwnerTintVisibilityBoost
    {
        get { return Mathf.Clamp(ownerTintVisibilityBoost, 0f, 0.5f); }
    }
    public float EffectTintMaxBrightness
    {
        get { return Mathf.Clamp(effectTintMaxBrightness, 0.4f, 1f); }
    }
    public float ColorBlendLuminanceMultiplier
    {
        get { return Mathf.Clamp(colorBlendLuminanceMultiplier, 0.4f, 1f); }
    }
    public float CastMarkerScale { get { return Mathf.Max(0.01f, castMarkerScale); } }
    public float TelegraphTileScale { get { return Mathf.Max(0.01f, telegraphTileScale); } }
    public float ImpactScale { get { return Mathf.Max(0.01f, impactScale); } }
    public float ImpactLevel3ScaleMultiplier { get { return Mathf.Max(0.01f, impactLevel3ScaleMultiplier); } }
    public float ImpactLevel5ScaleMultiplier { get { return Mathf.Max(0.01f, impactLevel5ScaleMultiplier); } }
    public float AuraScale { get { return Mathf.Max(0.01f, auraScale); } }
    public float ImpactSurfaceOffset { get { return impactSurfaceOffset; } }
    public float ImpactPlaybackSpeed { get { return Mathf.Max(0.05f, impactPlaybackSpeed); } }
    public float ImpactVisualLeadSeconds { get { return Mathf.Clamp(impactVisualLeadSeconds, 0f, 0.05f); } }
    public float ImpactLifetimeSeconds { get { return Mathf.Max(0.05f, impactLifetimeSeconds); } }
    public float AuraLifetimeSeconds { get { return Mathf.Max(0.05f, auraLifetimeSeconds); } }

    public Color GetTintedColor(Color ownerColor)
    {
        float tintAmount = Mathf.Lerp(OwnerTintAmount, 1f, OwnerTintVisibilityBoost);
        Color.RGBToHSV(ownerColor, out float ownerHue, out float ownerSaturation, out float ownerValue);
        Color.RGBToHSV(elementColor, out _, out float elementSaturation, out float elementValue);

        // Preserve player identity: profiles may adjust the feel slightly, but hue stays
        // exact and saturation/value cannot drift far enough to become another color.
        float saturation = Mathf.Clamp(
            Mathf.Lerp(elementSaturation, ownerSaturation, tintAmount),
            ownerSaturation * 0.96f,
            Mathf.Min(1f, ownerSaturation * 1.04f));
        float minimumValue = ownerValue * 0.96f;
        float maximumValue = Mathf.Min(
            1f,
            Mathf.Min(ownerValue * 1.05f, Mathf.Max(EffectTintMaxBrightness, minimumValue)));
        float value = Mathf.Clamp(
            Mathf.Lerp(elementValue, ownerValue, tintAmount),
            minimumValue,
            maximumValue);
        Color result = Color.HSVToRGB(
            ownerHue,
            Mathf.Clamp01(saturation),
            Mathf.Clamp01(value));
        result.a = Mathf.Max(elementColor.a, ownerColor.a);
        return result;
    }
}
