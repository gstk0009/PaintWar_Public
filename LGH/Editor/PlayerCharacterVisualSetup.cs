#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PlayerCharacterVisualSetup
{
    private const string MenuPath =
        "PaintWar/Character/Apply Optimized Skin And Clothing Colors";
    private const string PlayerPrefabPath = "Assets/CSE/Prerfabs/MyPlayer.prefab";
    private const string LobbyPlayerPrefabPath =
        "Assets/CSE/Prerfabs/LobbyPlayer.prefab";
    private const string GeneratedFolder = "Assets/CSE/Player/Generated";
    private const string GeneratedMeshPath =
        GeneratedFolder + "/PlayerSkinClothingMasked.asset";
    private const string MaterialFolder = "Assets/CSE/Materials/Characters";
    private const string MaterialPath =
        MaterialFolder + "/PlayerCharacterShared.mat";
    private const string ShaderName = "PaintWar/Player Character Surface";

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        EnsureFolder("Assets/CSE/Player", "Generated");
        EnsureFolder("Assets/CSE/Materials", "Characters");

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            throw new System.InvalidOperationException("Missing shader: " + ShaderName);
        }

        Material sharedMaterial = CreateOrUpdateMaterial(shader);
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            SkinnedMeshRenderer renderer =
                root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null || renderer.sharedMesh == null)
            {
                throw new System.InvalidOperationException(
                    "MyPlayer prefab is missing its base SkinnedMeshRenderer.");
            }

            Mesh maskedMesh = CreateOrUpdateMaskedMesh(renderer);
            renderer.sharedMesh = maskedMesh;
            renderer.sharedMaterial = sharedMaterial;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Player player = root.GetComponent<Player>();
            if (player == null)
            {
                throw new System.InvalidOperationException(
                    "MyPlayer prefab is missing Player.");
            }

            SerializedObject serializedPlayer = new SerializedObject(player);
            serializedPlayer.FindProperty("playerRenderer").objectReferenceValue = renderer;
            serializedPlayer.FindProperty("playerMaterial").objectReferenceValue =
                sharedMaterial;
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            CharacterAppearanceApplicator applicator =
                root.GetComponent<CharacterAppearanceApplicator>();
            if (applicator == null)
            {
                applicator = root.AddComponent<CharacterAppearanceApplicator>();
            }

            SerializedObject serializedApplicator = new SerializedObject(applicator);
            serializedApplicator.FindProperty("sharedClothingMaterial").objectReferenceValue =
                sharedMaterial;
            serializedApplicator.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EnsureLobbyAppearanceApplicator();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = sharedMaterial;
        Debug.Log(
            "[PlayerCharacterVisualSetup] Applied authored skin/clothing mask, " +
            "shared material, and property-block runtime color path.");
    }

    private static void EnsureLobbyAppearanceApplicator()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LobbyPlayerPrefabPath);
        try
        {
            CharacterAppearanceApplicator applicator =
                root.GetComponent<CharacterAppearanceApplicator>();
            if (applicator == null)
            {
                applicator = root.AddComponent<CharacterAppearanceApplicator>();
            }

            SerializedObject serializedApplicator = new SerializedObject(applicator);
            serializedApplicator.FindProperty("sharedClothingMaterial").objectReferenceValue =
                null;
            serializedApplicator.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, LobbyPlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Material CreateOrUpdateMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = "PlayerCharacterShared"
            };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        material.enableInstancing = false;
        material.SetColor("_SkinColor", new Color32(0xF2, 0xB2, 0x8D, 0xFF));
        material.SetColor("_PlayerColor", new Color32(0x27, 0x5B, 0xCB, 0xFF));
        material.SetColor("_RimColor", new Color32(0x16, 0x22, 0x38, 0xFF));
        material.SetFloat("_RimStrength", 0.42f);
        material.SetFloat("_Smoothness", 0.4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh CreateOrUpdateMaskedMesh(SkinnedMeshRenderer renderer)
    {
        Mesh source = renderer.sharedMesh;
        Mesh generated = Object.Instantiate(source);
        generated.name = "PlayerSkinClothingMasked";
        generated.colors32 = BuildClothingMask(renderer, source);

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(GeneratedMeshPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, GeneratedMeshPath);
            return generated;
        }

        EditorUtility.CopySerialized(generated, existing);
        Object.DestroyImmediate(generated);
        existing.name = "PlayerSkinClothingMasked";
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Color32[] BuildClothingMask(
        SkinnedMeshRenderer renderer,
        Mesh mesh)
    {
        BoneWeight[] weights = mesh.boneWeights;
        Transform[] bones = renderer.bones;
        Color32[] colors = new Color32[mesh.vertexCount];

        for (int vertexIndex = 0; vertexIndex < colors.Length; vertexIndex++)
        {
            BoneWeight weight = weights[vertexIndex];
            float skinWeight =
                GetSkinWeight(bones, weight.boneIndex0, weight.weight0) +
                GetSkinWeight(bones, weight.boneIndex1, weight.weight1) +
                GetSkinWeight(bones, weight.boneIndex2, weight.weight2) +
                GetSkinWeight(bones, weight.boneIndex3, weight.weight3);
            byte clothingMask =
                (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - skinWeight) * byte.MaxValue);
            colors[vertexIndex] =
                new Color32(clothingMask, clothingMask, clothingMask, byte.MaxValue);
        }

        return colors;
    }

    private static float GetSkinWeight(
        Transform[] bones,
        int boneIndex,
        float weight)
    {
        if (weight <= 0f ||
            boneIndex < 0 ||
            boneIndex >= bones.Length ||
            bones[boneIndex] == null)
        {
            return 0f;
        }

        return IsSkinBone(bones[boneIndex].name) ? weight : 0f;
    }

    private static bool IsSkinBone(string boneName)
    {
        string normalized = boneName
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();
        return normalized.Contains("head") ||
               normalized.Contains("neck") ||
               normalized.Contains("forearm") ||
               normalized.Contains("hand") ||
               normalized.Contains("thumb") ||
               normalized.Contains("index") ||
               normalized.Contains("middle") ||
               normalized.Contains("ring") ||
               normalized.Contains("pinky");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
