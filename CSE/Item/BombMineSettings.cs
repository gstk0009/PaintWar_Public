using UnityEngine;

[CreateAssetMenu(
    fileName = "BombMineSettings",
    menuName = "PaintWar/Items/Bomb Mine Settings")]
public sealed class BombMineSettings : ScriptableObject
{
    private static readonly int[] DefaultLevelMaxActiveMines = { 2, 3, 3, 4, 4 };
    private static readonly int[] DefaultLevelCrossRadii = { 2, 3, 4, 4, 4 };
    private static readonly float[] DefaultComboStunSeconds = { 0.5f, 1f, 2f };

    public const float DefaultFuseSeconds = 10f;
    public const int DefaultCrossRadius = 2;
    public const int DefaultMaxActiveMines = 2;
    public const float DefaultChainExplosionDelaySeconds = 0.5f;
    public const float DefaultChainLinkWidthRatio = 0.045f;
    public const float DefaultChainLinkPulseSpeed = 4f;
    public const float DefaultComboGlowWidthRatio = 0.075f;
    public const float DefaultComboGlowPulseSpeed = 6.5f;
    public const float DefaultHitStunSeconds = 0.5f;
    public const int DefaultNeutralizeOwnedTilesOnHit = 3;

    [SerializeField, Min(0.1f)] private float fuseSeconds = DefaultFuseSeconds;
    [SerializeField, Min(1)] private int crossRadius = DefaultCrossRadius;
    [SerializeField] private int[] levelMaxActiveMines =
        (int[])DefaultLevelMaxActiveMines.Clone();
    [SerializeField] private int[] levelCrossRadii =
        (int[])DefaultLevelCrossRadii.Clone();
    [SerializeField, Min(0f)] private float chainExplosionDelaySeconds =
        DefaultChainExplosionDelaySeconds;
    [SerializeField, Min(0.005f)] private float chainLinkWidthRatio =
        DefaultChainLinkWidthRatio;
    [SerializeField, Min(0.1f)] private float chainLinkPulseSpeed =
        DefaultChainLinkPulseSpeed;
    [SerializeField, Min(0.005f)] private float comboGlowWidthRatio =
        DefaultComboGlowWidthRatio;
    [SerializeField, Min(0.1f)] private float comboGlowPulseSpeed =
        DefaultComboGlowPulseSpeed;
    [SerializeField] private float[] comboStunSeconds =
        (float[])DefaultComboStunSeconds.Clone();
    [SerializeField, Min(0)] private int neutralizeOwnedTilesOnHit = DefaultNeutralizeOwnedTilesOnHit;
    [SerializeField] private GameObject visualPrefab;

    public float FuseSeconds { get { return Mathf.Max(0.1f, fuseSeconds); } }
    public int CrossRadius { get { return Mathf.Max(1, crossRadius); } }
    public float ChainExplosionDelaySeconds
    {
        get { return Mathf.Max(0f, chainExplosionDelaySeconds); }
    }
    public float ChainLinkWidthRatio
    {
        get { return Mathf.Max(0.005f, chainLinkWidthRatio); }
    }
    public float ChainLinkPulseSpeed
    {
        get { return Mathf.Max(0.1f, chainLinkPulseSpeed); }
    }
    public float ComboGlowWidthRatio
    {
        get { return Mathf.Max(0.005f, comboGlowWidthRatio); }
    }
    public float ComboGlowPulseSpeed
    {
        get { return Mathf.Max(0.1f, comboGlowPulseSpeed); }
    }
    public int NeutralizeOwnedTilesOnHit { get { return Mathf.Max(0, neutralizeOwnedTilesOnHit); } }
    public GameObject VisualPrefab { get { return visualPrefab; } }

    public int GetMaxActiveMinesForLevel(int level)
    {
        return GetLevelValue(
            levelMaxActiveMines,
            DefaultLevelMaxActiveMines,
            level,
            DefaultMaxActiveMines);
    }

    public int GetCrossRadiusForLevel(int level)
    {
        return GetLevelValue(
            levelCrossRadii,
            DefaultLevelCrossRadii,
            level,
            DefaultCrossRadius);
    }

    public float GetStunSecondsForCombo(int comboDepth)
    {
        return GetComboValue(
            comboStunSeconds,
            DefaultComboStunSeconds,
            comboDepth,
            DefaultHitStunSeconds);
    }

    public static int GetDefaultMaxActiveMinesForLevel(int level)
    {
        return GetLevelValue(
            null,
            DefaultLevelMaxActiveMines,
            level,
            DefaultMaxActiveMines);
    }

    public static int GetDefaultCrossRadiusForLevel(int level)
    {
        return GetLevelValue(
            null,
            DefaultLevelCrossRadii,
            level,
            DefaultCrossRadius);
    }

    public static float GetDefaultStunSecondsForCombo(int comboDepth)
    {
        return GetComboValue(
            null,
            DefaultComboStunSeconds,
            comboDepth,
            DefaultHitStunSeconds);
    }

    private static int GetLevelValue(
        int[] values,
        int[] defaults,
        int level,
        int fallback)
    {
        int index = Mathf.Max(1, level) - 1;
        if (values != null &&
            values.Length > 0 &&
            index < values.Length)
        {
            return Mathf.Max(1, values[index]);
        }

        if (defaults != null &&
            defaults.Length > 0)
        {
            int defaultIndex = Mathf.Min(index, defaults.Length - 1);
            return Mathf.Max(1, defaults[defaultIndex]);
        }

        return Mathf.Max(1, fallback);
    }

    private static float GetComboValue(
        float[] values,
        float[] defaults,
        int comboDepth,
        float fallback)
    {
        int index = Mathf.Max(1, comboDepth) - 1;
        if (values != null &&
            values.Length > 0 &&
            index < values.Length)
        {
            return Mathf.Max(0f, values[index]);
        }

        if (defaults != null &&
            defaults.Length > 0)
        {
            int defaultIndex = Mathf.Min(index, defaults.Length - 1);
            return Mathf.Max(0f, defaults[defaultIndex]);
        }

        return Mathf.Max(0f, fallback);
    }
}
