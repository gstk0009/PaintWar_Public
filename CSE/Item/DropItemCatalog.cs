using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "DropItemCatalog",
    menuName = "PaintWar/Items/Drop Item Catalog")]
public sealed class DropItemCatalog : ScriptableObject
{
    [SerializeField] private DropItemDefinition[] items;

    public bool HasItems
    {
        get { return items != null && items.Length > 0; }
    }

    public DropItemDefinition[] Items { get { return items; } }

    public DropItemSpawnConfig PickConfig(BombMineSettings bombMineSettings)
    {
        DropItemDefinition definition = PickDefinition();
        return definition != null
            ? definition.ToSpawnConfig(bombMineSettings)
            : DropItemSpawnConfig.SpeedDefault(null);
    }

    public DropItemDefinition PickDefinition()
    {
        if (items == null || items.Length == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        for (int i = 0; i < items.Length; i++)
        {
            if (IsSpawnable(items[i]))
            {
                totalWeight += Mathf.Max(0f, items[i].Weight);
            }
        }

        if (totalWeight <= 0f)
        {
            return PickAnyDefinition();
        }

        float roll = Random.value * totalWeight;
        DropItemDefinition lastValid = null;
        for (int i = 0; i < items.Length; i++)
        {
            DropItemDefinition definition = items[i];
            if (!IsSpawnable(definition))
            {
                continue;
            }

            lastValid = definition;
            roll -= Mathf.Max(0f, definition.Weight);
            if (roll <= 0f)
            {
                return definition;
            }
        }

        return lastValid;
    }

    public bool TryGetDefinition(
        DropItemEffectType effectType,
        out DropItemDefinition definition)
    {
        definition = null;
        if (items == null)
        {
            return false;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (IsSpawnable(items[i]) && items[i].EffectType == effectType)
            {
                definition = items[i];
                return true;
            }
        }

        return false;
    }

    public Sprite GetIcon(DropItemEffectType effectType)
    {
        return TryGetDefinition(effectType, out DropItemDefinition definition)
            ? definition.Icon
            : null;
    }

    public string GetDisplayName(DropItemEffectType effectType)
    {
        return TryGetDefinition(effectType, out DropItemDefinition definition)
            ? definition.DisplayName
            : effectType.ToString();
    }

    public void AddConfiguredPrefabs(List<GameObject> prefabs)
    {
        if (prefabs == null || items == null)
        {
            return;
        }

        for (int i = 0; i < items.Length; i++)
        {
            GameObject prefab = IsSpawnable(items[i]) ? items[i].Prefab : null;
            if (prefab != null && !prefabs.Contains(prefab))
            {
                prefabs.Add(prefab);
            }
        }
    }

    public GameObject GetFirstPrefab()
    {
        if (items == null)
        {
            return null;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (IsSpawnable(items[i]) && items[i].Prefab != null)
            {
                return items[i].Prefab;
            }
        }

        return null;
    }

    private DropItemDefinition PickAnyDefinition()
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (IsSpawnable(items[i]))
            {
                return items[i];
            }
        }

        return null;
    }

    private static bool IsSpawnable(DropItemDefinition definition)
    {
        return definition != null &&
               definition.EffectType != DropItemEffectType.PaintShell &&
               definition.EffectType != DropItemEffectType.BombMine;
    }
}
