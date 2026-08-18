using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CrystalBloomPlayerColorSetup
{
    private const string SourcePrefabGuid = "b1ae5055f08b8284d9e996465d1d37e3";
    private const string ProfilePath =
        "Assets/LGH/Data/WeaponVfxProfiles/crystal_bloom_vfx.asset";
    private const string MaterialFolder =
        "Assets/LGH/Materials/WeaponVfx/CrystalBloom";
    private const string PrefabFolder =
        "Assets/LGH/Prefabs/WeaponVfx";
    private const string PrefabPath =
        PrefabFolder + "/CrystalBloomImpact_PlayerColor.prefab";

    [MenuItem("Tools/LGH/Weapons/Configure Crystal Bloom Player Colors")]
    public static void Configure()
    {
        string sourcePrefabPath = AssetDatabase.GUIDToAssetPath(SourcePrefabGuid);
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
        MagicWeaponVfxProfile profile =
            AssetDatabase.LoadAssetAtPath<MagicWeaponVfxProfile>(ProfilePath);
        if (sourcePrefab == null || profile == null)
        {
            Debug.LogError(
                "Crystal Bloom player-color setup could not find its source prefab or VFX profile.");
            return;
        }

        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        try
        {
            Dictionary<Material, Material> variants = new Dictionary<Material, Material>();
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material source = materials[materialIndex];
                    if (source == null)
                    {
                        continue;
                    }

                    if (!variants.TryGetValue(source, out Material variant))
                    {
                        variant = CreateOrUpdateColorBlendMaterial(source);
                        variants.Add(source, variant);
                    }

                    materials[materialIndex] = variant;
                    changed = true;
                }

                if (changed)
                {
                    renderers[i].sharedMaterials = materials;
                }
            }

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            SerializedObject serializedProfile = new SerializedObject(profile);
            serializedProfile.FindProperty("impactPrefab").objectReferenceValue = savedPrefab;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Crystal Bloom player-color prefab and material variants are configured.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static Material CreateOrUpdateColorBlendMaterial(Material source)
    {
        string safeName = source.name.Replace('/', '_').Replace('\\', '_');
        string path = MaterialFolder + "/" + safeName + "_PlayerColor.mat";
        Material variant = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (variant == null)
        {
            variant = new Material(source);
            AssetDatabase.CreateAsset(variant, path);
        }
        else
        {
            EditorUtility.CopySerialized(source, variant);
        }

        variant.name = safeName + "_PlayerColor";
        if (variant.HasProperty("_ColorMode"))
        {
            variant.SetFloat("_ColorMode", 4f);
        }

        variant.DisableKeyword("_COLOROVERLAY_ON");
        variant.DisableKeyword("_COLORADDSUBDIFF_ON");
        variant.EnableKeyword("_COLORCOLOR_ON");
        EditorUtility.SetDirty(variant);
        return variant;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
