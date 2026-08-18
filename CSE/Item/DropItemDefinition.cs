using UnityEngine;

[CreateAssetMenu(
    fileName = "DropItemDefinition",
    menuName = "PaintWar/Items/Drop Item Definition")]
public sealed class DropItemDefinition : ScriptableObject
{
    [SerializeField] private string itemId;
    [SerializeField] private string displayName;
    [SerializeField] private DropItemEffectType effectType;
    [SerializeField] private GameObject prefab;
    [SerializeField] private Sprite icon;
    [SerializeField] private float weight = 1f;
    [SerializeField] private float speedMultiplier = 1.5f;
    [SerializeField] private float duration = 3f;
    [SerializeField] private int experienceAmount;
    [SerializeField] private float cooldownReductionSeconds;
    [SerializeField] private bool clearCooldown;
    [SerializeField] private bool grantPickupExperience = true;
    [SerializeField] private float useRadius = 5f;
    [SerializeField] private int paintRadius = 1;

    public string ItemId
    {
        get
        {
            return string.IsNullOrWhiteSpace(itemId)
                ? effectType.ToString()
                : itemId.Trim();
        }
    }

    public string DisplayName
    {
        get
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? effectType.ToString()
                : displayName.Trim();
        }
    }

    public DropItemEffectType EffectType { get { return effectType; } }
    public GameObject Prefab { get { return prefab; } }
    public Sprite Icon { get { return icon; } }
    public float Weight { get { return weight <= 0f ? 1f : weight; } }

    public DropItemSpawnConfig ToSpawnConfig(BombMineSettings bombMineSettings)
    {
        DropItemSpawnConfig config = DropItemSpawnConfig.Create(
            effectType,
            prefab,
            weight,
            speedMultiplier,
            duration,
            experienceAmount,
            cooldownReductionSeconds,
            clearCooldown,
            grantPickupExperience,
            useRadius,
            paintRadius);
        return effectType == DropItemEffectType.BombMine
            ? config.WithBombMineSettings(bombMineSettings)
            : config;
    }
}
