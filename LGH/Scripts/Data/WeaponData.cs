using UnityEngine;

[CreateAssetMenu(menuName = "LGH/Weapons/Weapon Data", fileName = "WeaponData")]
public class WeaponData : ScriptableObject
{
    [SerializeField] private string weaponId;
    [SerializeField] private string displayName;
    [SerializeField] private int sortOrder;
    [SerializeField] private string category;
    [SerializeField] private WeaponPatternType patternType;
    [SerializeField] private float baseCooldown = 1f;
    [SerializeField] private int range = 1;
    [SerializeField] private float delaySeconds;
    [SerializeField] private Vector2Int patternPivot = new Vector2Int(2, 2);
    [SerializeField] private string[] patternRows;
    [SerializeField] private Vector2Int level3PatternPivot = new Vector2Int(2, 2);
    [SerializeField] private string[] level3PatternRows;
    [SerializeField] private Vector2Int level5PatternPivot = new Vector2Int(2, 2);
    [SerializeField] private string[] level5PatternRows;
    [TextArea]
    [SerializeField] private string description;
    [SerializeField] private Vector2Int[] customOffsets;
    [Header("Visuals")]
    [SerializeField] private MagicWeaponVfxProfile vfxProfile;
    [Header("Status Effect")]
    [SerializeField] private StatusEffectSpec statusEffect;

    public string WeaponId { get { return weaponId; } }
    public string DisplayName { get { return displayName; } }
    public int SortOrder { get { return sortOrder; } }
    public string Category { get { return category; } }
    public WeaponPatternType PatternType { get { return patternType; } }
    public float BaseCooldown { get { return baseCooldown; } }
    public int Range { get { return Mathf.Max(1, range); } }
    public float DelaySeconds { get { return Mathf.Max(0f, delaySeconds); } }
    public Vector2Int PatternPivot { get { return patternPivot; } }
    public string[] PatternRows { get { return patternRows; } }
    public bool HasPatternRows { get { return patternRows != null && patternRows.Length > 0; } }
    public Vector2Int Level3PatternPivot { get { return level3PatternPivot; } }
    public string[] Level3PatternRows { get { return level3PatternRows; } }
    public bool HasLevel3PatternRows { get { return level3PatternRows != null && level3PatternRows.Length > 0; } }
    public Vector2Int Level5PatternPivot { get { return level5PatternPivot; } }
    public string[] Level5PatternRows { get { return level5PatternRows; } }
    public bool HasLevel5PatternRows { get { return level5PatternRows != null && level5PatternRows.Length > 0; } }
    public string Description { get { return description; } }
    public Vector2Int[] CustomOffsets { get { return customOffsets; } }
    public MagicWeaponVfxProfile VfxProfile { get { return vfxProfile; } }
    public StatusEffectSpec StatusEffect { get { return statusEffect; } }
    public bool HasStatusEffect { get { return statusEffect.IsValid; } }

    public Vector2Int GetPatternPivotForLevel(int level)
    {
        // 현재 레벨에서 사용할 패턴 배열 안의 플레이어 위치(2)를 반환합니다.
        if (level >= 5 && HasLevel5PatternRows)
        {
            return level5PatternPivot;
        }

        if (level >= 3 && HasLevel3PatternRows)
        {
            return level3PatternPivot;
        }

        return patternPivot;
    }

    public string[] GetPatternRowsForLevel(int level)
    {
        // Lv5, Lv3 전용 패턴이 없으면 기본 Lv1 패턴으로 자연스럽게 fallback합니다.
        if (level >= 5 && HasLevel5PatternRows)
        {
            return level5PatternRows;
        }

        if (level >= 3 && HasLevel3PatternRows)
        {
            return level3PatternRows;
        }

        return patternRows;
    }

    public void SetData(
        string newWeaponId,
        string newDisplayName,
        string newCategory,
        WeaponPatternType newPatternType,
        float newBaseCooldown,
        int newRange,
        float newDelaySeconds,
        string newDescription,
        Vector2Int newPatternPivot,
        string[] newPatternRows,
        Vector2Int? newLevel3PatternPivot = null,
        string[] newLevel3PatternRows = null,
        Vector2Int? newLevel5PatternPivot = null,
        string[] newLevel5PatternRows = null,
        Vector2Int[] newCustomOffsets = null,
        StatusEffectSpec? newStatusEffect = null,
        MagicWeaponVfxProfile newVfxProfile = null)
    {
        weaponId = newWeaponId;
        displayName = newDisplayName;
        category = newCategory;
        patternType = newPatternType;
        baseCooldown = Mathf.Max(0.01f, newBaseCooldown);
        range = Mathf.Max(1, newRange);
        delaySeconds = Mathf.Max(0f, newDelaySeconds);
        patternPivot = newPatternPivot;
        patternRows = newPatternRows;
        level3PatternPivot = newLevel3PatternPivot ?? newPatternPivot;
        level3PatternRows = newLevel3PatternRows;
        level5PatternPivot = newLevel5PatternPivot ?? level3PatternPivot;
        level5PatternRows = newLevel5PatternRows;
        description = newDescription;
        customOffsets = newCustomOffsets;
        vfxProfile = newVfxProfile;
        statusEffect = newStatusEffect ?? default(StatusEffectSpec);
    }

    public void SetSortOrder(int newSortOrder)
    {
        sortOrder = newSortOrder;
    }
}
