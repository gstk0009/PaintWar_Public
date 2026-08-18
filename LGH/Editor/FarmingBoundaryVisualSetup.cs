using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class FarmingBoundaryVisualSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string MaterialFolder = "Assets/LGH/Materials/Round";
    private const string MarkerMaterialPath = MaterialFolder + "/FarmingBoundaryMarker.mat";
    private const string BaseMaterialPath = MaterialFolder + "/FarmingBoundaryBase.mat";
    private const string GlassMaterialPath = MaterialFolder + "/FarmingBoundaryGlass.mat";

    [MenuItem("PaintWar/Round/Setup Farming Boundary Visuals")]
    public static void Setup()
    {
        EnsureFolder("Assets/LGH/Materials");
        EnsureFolder(MaterialFolder);

        Material markerMaterial = LoadOrCreateTransparentMaterial(
            MarkerMaterialPath,
            new Color(1f, 1f, 1f, 0.11f),
            0.72f);
        Material baseMaterial = LoadOrCreateTransparentMaterial(
            BaseMaterialPath,
            new Color(0.06f, 0.06f, 0.07f, 0.76f),
            0.58f);
        Material glassMaterial = LoadOrCreateTransparentMaterial(
            GlassMaterialPath,
            new Color(1f, 1f, 1f, 0.08f),
            0.9f);

        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        GameObject systemsObject = GameObject.Find("Round Game Systems");
        if (systemsObject == null)
        {
            systemsObject = new GameObject("Round Game Systems");
        }

        GameRoundManager roundManager = systemsObject.GetComponent<GameRoundManager>();
        if (roundManager == null)
        {
            systemsObject.AddComponent<GameRoundManager>();
        }

        FarmingBoundaryView view = systemsObject.GetComponent<FarmingBoundaryView>();
        if (view == null)
        {
            view = systemsObject.AddComponent<FarmingBoundaryView>();
        }

        SerializedObject serializedView = new SerializedObject(view);
        serializedView.FindProperty("boundaryBaseMarkerSurfaceOffset").floatValue = 0.5f;
        serializedView.FindProperty("boundaryBaseMarkerSurfacePadding").floatValue = 0.018f;
        serializedView.FindProperty("boundaryMarkerMaterial").objectReferenceValue = markerMaterial;
        serializedView.FindProperty("boundaryBaseMaterial").objectReferenceValue = baseMaterial;
        serializedView.FindProperty("glassPaneMaterial").objectReferenceValue = glassMaterial;
        serializedView.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Material LoadOrCreateTransparentMaterial(string path, Color color, float smoothness)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.name = System.IO.Path.GetFileNameWithoutExtension(path);
        material.SetOverrideTag("RenderType", "Transparent");
        SetColorIfPresent(material, "_BaseColor", color);
        SetColorIfPresent(material, "_Color", color);
        SetFloatIfPresent(material, "_Surface", 1f);
        SetFloatIfPresent(material, "_Blend", 0f);
        SetFloatIfPresent(material, "_AlphaClip", 0f);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
        SetFloatIfPresent(material, "_Smoothness", smoothness);
        SetFloatIfPresent(material, "_Metallic", 0f);
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, name);
    }

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }
}
