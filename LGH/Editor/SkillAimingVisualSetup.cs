#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SkillAimingVisualSetup
{
    private const string PrefabFolder = "Assets/LGH/Prefabs/SkillAiming";
    private const string TilePrefabPath = PrefabFolder + "/SkillAimTileIndicator.prefab";
    private const string AreaPrefabPath = PrefabFolder + "/SkillAimAreaVisual.prefab";
    private const string PlayerPrefabPath = "Assets/CSE/Prerfabs/MyPlayer.prefab";
    private const string RangeMaterialPath = "Assets/LGH/Materials/WeaponRangeIndicator.mat";

    [MenuItem("Tools/PaintWar/Weapons/Create Skill Aiming Area Prefabs")]
    public static void CreateAndAssign()
    {
        EnsureFolder(PrefabFolder);
        GameObject tilePrefab = CreateTilePrefab();
        SkillAimAreaVisual areaPrefab = CreateAreaPrefab(tilePrefab);
        AssignToPlayerPrefab(areaPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SkillAimingVisualSetup] Created and assigned skill aiming prefabs.");
    }

    private static GameObject CreateTilePrefab()
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = "Skill Aim Tile Indicator";
        Object.DestroyImmediate(tile.GetComponent<Collider>());

        Renderer renderer = tile.GetComponent<Renderer>();
        Material rangeMaterial = AssetDatabase.LoadAssetAtPath<Material>(RangeMaterialPath);
        if (rangeMaterial == null)
        {
            WeaponVisualSettings settings = AssetDatabase.LoadAssetAtPath<WeaponVisualSettings>(
                "Assets/LGH/Resources/WeaponVisualSettings.asset");
            rangeMaterial = settings != null ? settings.RangeIndicatorMaterial : null;
        }

        if (renderer != null && rangeMaterial != null)
        {
            renderer.sharedMaterial = rangeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tile, TilePrefabPath);
        Object.DestroyImmediate(tile);
        return prefab;
    }

    private static SkillAimAreaVisual CreateAreaPrefab(GameObject tilePrefab)
    {
        GameObject root = new GameObject("Skill Aim Area Visual");
        SkillAimAreaVisual visual = root.AddComponent<SkillAimAreaVisual>();
        SerializedObject serialized = new SerializedObject(visual);
        serialized.FindProperty("tileIndicatorPrefab").objectReferenceValue = tilePrefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, AreaPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<SkillAimAreaVisual>();
    }

    private static void AssignToPlayerPrefab(SkillAimAreaVisual areaPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            WeaponRuntimeController controller = root.GetComponent<WeaponRuntimeController>();
            if (controller == null)
            {
                throw new MissingComponentException(
                    "MyPlayer prefab does not contain WeaponRuntimeController.");
            }

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("skillAimAreaVisualPrefab").objectReferenceValue = areaPrefab;
            serialized.FindProperty("skillAimCursorSpeed").floatValue = 7f;
            serialized.FindProperty("skillAimViewportPadding").floatValue = 0.055f;
            serialized.FindProperty("skillCastMovementLockSeconds").floatValue = 1f;
            serialized.FindProperty("skillAimUpdatesPerSecond").floatValue = 30f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
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
#endif
