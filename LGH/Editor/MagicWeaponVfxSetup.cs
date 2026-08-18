using UnityEditor;
using UnityEngine;

public static class MagicWeaponVfxSetup
{
    private const string ProfileFolder = "Assets/LGH/Data/WeaponVfxProfiles";
    private const string WeaponFolder = "Assets/LGH/Data/Weapons";
    private const string HovlRoot = "Assets/Hovl Studio/Magic effects pack/Prefabs";
    private const string DefaultCastMarkerPath =
        "Assets/LGH/Prefabs/VFX/SkillMarker/ThemedSkillMarker3D.prefab";

    [MenuItem("Tools/PaintWar/Weapons/Setup Magic Weapon VFX")]
    public static void SetupMagicWeaponVfx()
    {
        EnsureFolder();

        MagicWeaponVfxProfile arcane = CreateOrUpdateProfile(
            "arcane_lance",
            new Color(0.35f, 0.7f, 1f, 1f),
            0.4f,
            null,
            HovlRoot + "/Magic circles/Magic circle.prefab",
            HovlRoot + "/AoE effects/Laser AOE.prefab",
            0.32f,
            0.75f,
            0.58f,
            1.25f,
            1.5f,
            0.08f,
            1f,
            0f,
            1.05f);

        MagicWeaponVfxProfile meteor = CreateOrUpdateProfile(
            "meteor_cross",
            new Color(1f, 0.35f, 0.08f, 1f),
            0.35f,
            null,
            HovlRoot + "/Magic circles/Magic circle 2.prefab",
            HovlRoot + "/AoE effects/Meteors AOE.prefab",
            0.55f,
            0.8f,
            0.58f,
            1.15f,
            1.3f,
            0.06f,
            1f,
            0f,
            1.2f);

        MagicWeaponVfxProfile frost = CreateOrUpdateProfile(
            "frost_wall",
            new Color(0.55f, 0.9f, 1f, 1f),
            0.35f,
            null,
            HovlRoot + "/Magic circles/Freeze circle.prefab",
            HovlRoot + "/AoE effects/Snow AOE.prefab",
            0.34f,
            0.75f,
            0.5f,
            1.2f,
            1.4f,
            0.05f,
            1f,
            0f,
            1f);

        MagicWeaponVfxProfile fire = CreateOrUpdateProfile(
            "fire_surge",
            new Color(1f, 0.22f, 0.03f, 1f),
            0.35f,
            null,
            HovlRoot + "/Magic circles/Magic circle 2.prefab",
            HovlRoot + "/AoE effects/AoE slash orange.prefab",
            0.34f,
            0.72f,
            0.7f,
            1.25f,
            1.5f,
            0.38f,
            1.05f,
            0f,
            1f);

        MagicWeaponVfxProfile lightning = CreateOrUpdateProfile(
            "lightning_chain",
            new Color(0.45f, 0.75f, 1f, 1f),
            0.45f,
            null,
            HovlRoot + "/Magic circles/Magic circle.prefab",
            HovlRoot + "/Character auras/Lightning aura.prefab",
            0.32f,
            0.68f,
            0.75f,
            1.25f,
            1.5f,
            0.08f,
            1.1f,
            0f,
            1f);

        MagicWeaponVfxProfile crystal = CreateOrUpdateProfile(
            "crystal_bloom",
            new Color(0.7f, 0.45f, 1f, 1f),
            0.2f,
            null,
            null,
            HovlRoot + "/AoE effects/Crystals crossfade 2.prefab",
            0.34f,
            1f,
            0.28f,
            1.15f,
            1.3f,
            0.18f,
            1f,
            0f,
            1f);

        MagicWeaponVfxProfile ground = CreateOrUpdateProfile(
            "ground_rupture",
            new Color(0.9f, 0.72f, 0.38f, 1f),
            0.18f,
            null,
            null,
            HovlRoot + "/AoE effects/Ground AOE explosion.prefab",
            0.36f,
            1f,
            0.55f,
            1.2f,
            1.4f,
            0.08f,
            1f,
            0f,
            1f);

        MagicWeaponVfxProfile redEnergy = CreateOrUpdateProfile(
            "red_energy_burst",
            new Color(1f, 0.12f, 0.08f, 1f),
            0.18f,
            null,
            null,
            HovlRoot + "/AoE effects/Red energy explosion.prefab",
            0.36f,
            1f,
            0.55f,
            1.15f,
            1.3f,
            0.08f,
            1.55f,
            0.04f,
            0.85f);


        RenameAndWireWeapon(
            "laser",
            "arcane_lance",
            "Arcane Lance",
            "Arcane",
            "Claims a precise magical beam line across the grid.",
            arcane);
        RenameAndWireWeapon(
            "bomb",
            "meteor_cross",
            "Meteor Cross",
            "Fire",
            "Marks a cross-like meteor strike pattern for heavy area control.",
            meteor);
        RenameAndWireWeapon(
            "freezer",
            "frost_wall",
            "Frost Wall",
            "Frost",
            "Raises a line of icy runes to claim and control space.",
            frost);
        RenameAndWireWeapon(
            "flamethrower",
            "fire_surge",
            "Fire Surge",
            "Fire",
            "Spreads flame runes through a jagged forward tile pattern.",
            fire);
        RenameAndWireWeapon(
            "split_shot",
            "lightning_chain",
            "Lightning Chain",
            "Lightning",
            "Chains lightning through split or zigzag rune tiles.",
            lightning);
        RenameAndWireWeapon(
            "summon_circle",
            "crystal_bloom",
            "Crystal Bloom",
            "Crystal",
            "Blooms crystal energy around the caster in a compact pattern.",
            crystal);
        RenameAndWireWeapon(
            "axe",
            "ground_rupture",
            "Ground Rupture",
            "Earth",
            "Breaks the ground in a staggered magic pattern.",
            ground);
        RenameAndWireWeapon(
            "red_energy_burst",
            "red_energy_burst",
            "Red Energy Burst",
            "Arcane",
            "Releases a volatile red energy burst for heavy area control.",
            redEnergy);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MagicWeaponVfxSetup] Magic weapon VFX profiles created and weapon assets wired.");
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(ProfileFolder))
        {
            AssetDatabase.CreateFolder("Assets/LGH/Data", "WeaponVfxProfiles");
        }
    }

    private static MagicWeaponVfxProfile CreateOrUpdateProfile(
        string id,
        Color elementColor,
        float ownerTint,
        string castPath,
        string telegraphPath,
        string impactPath,
        float castScale,
        float telegraphScale,
        float impactScale,
        float impactLevel3ScaleMultiplier,
        float impactLevel5ScaleMultiplier,
        float impactSurfaceOffset,
        float impactPlaybackSpeed,
        float impactVisualLeadSeconds,
        float impactLifetime)
    {
        string path = ProfileFolder + "/" + id + "_vfx.asset";
        MagicWeaponVfxProfile profile = AssetDatabase.LoadAssetAtPath<MagicWeaponVfxProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<MagicWeaponVfxProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }

        SerializedObject serialized = new SerializedObject(profile);
        GameObject castMarker = LoadPrefab(castPath);
        if (castMarker == null)
        {
            castMarker = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultCastMarkerPath);
        }

        serialized.FindProperty("castMarkerPrefab").objectReferenceValue = castMarker;
        serialized.FindProperty("telegraphTilePrefab").objectReferenceValue = LoadPrefab(telegraphPath);
        serialized.FindProperty("impactPrefab").objectReferenceValue = LoadPrefab(impactPath);
        serialized.FindProperty("casterAuraPrefab").objectReferenceValue = null;
        serialized.FindProperty("hitPrefab").objectReferenceValue = null;
        serialized.FindProperty("elementColor").colorValue = elementColor;
        serialized.FindProperty("ownerTintAmount").floatValue = ownerTint;
        serialized.FindProperty("castMarkerScale").floatValue = castScale;
        serialized.FindProperty("telegraphTileScale").floatValue = telegraphScale;
        serialized.FindProperty("impactScale").floatValue = impactScale;
        serialized.FindProperty("impactLevel3ScaleMultiplier").floatValue = impactLevel3ScaleMultiplier;
        serialized.FindProperty("impactLevel5ScaleMultiplier").floatValue = impactLevel5ScaleMultiplier;
        serialized.FindProperty("auraScale").floatValue = 1f;
        serialized.FindProperty("impactSurfaceOffset").floatValue = impactSurfaceOffset;
        serialized.FindProperty("impactPlaybackSpeed").floatValue = impactPlaybackSpeed;
        serialized.FindProperty("impactVisualLeadSeconds").floatValue = impactVisualLeadSeconds;
        serialized.FindProperty("impactLifetimeSeconds").floatValue = impactLifetime;
        serialized.FindProperty("auraLifetimeSeconds").floatValue = 1f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static GameObject LoadPrefab(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("[MagicWeaponVfxSetup] Missing prefab: " + path);
        }

        return prefab;
    }

    private static void RenameAndWireWeapon(
        string oldId,
        string newId,
        string displayName,
        string category,
        string description,
        MagicWeaponVfxProfile profile)
    {
        string oldPath = WeaponFolder + "/" + oldId + ".asset";
        string newPath = WeaponFolder + "/" + newId + ".asset";
        WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(newPath);
        if (weapon == null)
        {
            weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(oldPath);
            if (weapon != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, newId);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError("[MagicWeaponVfxSetup] Rename failed: " + oldPath + " -> " + newId + ". " + error);
                    return;
                }

                weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(newPath);
            }
        }

        if (weapon == null)
        {
            Debug.LogError("[MagicWeaponVfxSetup] Missing weapon asset for " + oldId + " / " + newId + ".");
            return;
        }

        SerializedObject serialized = new SerializedObject(weapon);
        serialized.FindProperty("weaponId").stringValue = newId;
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("category").stringValue = category;
        serialized.FindProperty("description").stringValue = description;
        serialized.FindProperty("vfxProfile").objectReferenceValue = profile;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        weapon.name = newId;
        EditorUtility.SetDirty(weapon);
    }
}
