#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class GameplayHudSceneSetup
{
    private const string ProgressionFramePath =
        "Assets/LGH/UI/Generated/ProgressionHudFrame.png";
    private const string DebuffFramePath =
        "Assets/LGH/UI/Generated/DebuffStatusFrame.png";
    private const string StunIconPath =
        "Assets/LGH/UI/Generated/StunIcon.png";
    private const string SlowIconPath =
        "Assets/LGH/UI/Generated/SlowIcon.png";
    private const string ExperienceFillSpritePath =
        "Assets/LGH/UI/Generated/ExperienceFillRect.asset";

    [MenuItem("PaintWar/UI/Create Or Update Gameplay Status HUD")]
    public static void Configure()
    {
        ImportUiSprite(ProgressionFramePath, 1024);
        ImportUiSprite(DebuffFramePath, 1024);
        ImportUiSprite(StunIconPath, 256);
        ImportUiSprite(SlowIconPath, 256);

        PlayerStatusHud statusHud =
            Object.FindFirstObjectByType<PlayerStatusHud>(FindObjectsInactive.Include);
        if (statusHud == null)
        {
            Debug.LogError("[GameplayHudSetup] GameScene needs PlayerStatusHud.");
            return;
        }

        Canvas gameplayCanvas = statusHud.GetComponentInParent<Canvas>();
        if (gameplayCanvas == null)
        {
            Debug.LogError("[GameplayHudSetup] PlayerStatusHud is not under a Canvas.");
            return;
        }

        Sprite experienceFillSprite = GetOrCreateExperienceFillSprite();
        ConfigureProgressionHud(statusHud, experienceFillSprite);
        DebuffHudController debuffHud = ConfigureDebuffHud(gameplayCanvas.transform);

        EditorUtility.SetDirty(statusHud);
        EditorUtility.SetDirty(debuffHud);
        EditorSceneManager.MarkSceneDirty(statusHud.gameObject.scene);
        EditorSceneManager.SaveScene(statusHud.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[GameplayHudSetup] Gameplay HUD and debuffs updated.");
    }

    private static void ConfigureProgressionHud(
        PlayerStatusHud hud,
        Sprite experienceFillSprite)
    {
        // Existing HUD layout and styling are designer-authored. This setup command
        // only repairs the data-driven EXP fill and must never restyle the scene.
        SerializedObject serializedHud = new SerializedObject(hud);
        Image fill =
            serializedHud.FindProperty("experienceFill").objectReferenceValue as Image;
        if (fill != null)
        {
            fill.sprite = experienceFillSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillClockwise = true;
            EditorUtility.SetDirty(fill);
        }
    }

    private static DebuffHudController ConfigureDebuffHud(Transform canvas)
    {
        RectTransform root = canvas.Find("DebuffHud") as RectTransform;
        if (root == null)
        {
            Debug.LogError("[GameplayHudSetup] Missing editor-authored DebuffHud.");
            return null;
        }

        root.gameObject.SetActive(true);
        Transform stunRoot = root.Find("StunDebuff");
        Transform slowRoot = root.Find("SlowDebuff");
        DebuffStatusItem stun =
            stunRoot != null ? stunRoot.GetComponent<DebuffStatusItem>() : null;
        DebuffStatusItem slow =
            slowRoot != null ? slowRoot.GetComponent<DebuffStatusItem>() : null;
        if (stun == null || slow == null)
        {
            Debug.LogError("[GameplayHudSetup] Missing editor-authored debuff items.");
            return null;
        }

        DebuffHudController controller = GetOrAdd<DebuffHudController>(root.gameObject);
        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("stunItem").objectReferenceValue = stun;
        serialized.FindProperty("slowItem").objectReferenceValue = slow;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(true);
        return controller;
    }

    private static void ImportUiSprite(string path, int maxSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }

        if (importer == null)
        {
            Debug.LogError("[GameplayHudSetup] Missing generated UI image: " + path);
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Sprite GetOrCreateExperienceFillSprite()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ExperienceFillSpritePath);
        Texture2D texture = null;
        Sprite sprite = null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (texture == null)
            {
                texture = assets[i] as Texture2D;
            }

            if (sprite == null)
            {
                sprite = assets[i] as Sprite;
            }
        }

        if (texture == null)
        {
            texture = new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "ExperienceFillRectTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, ExperienceFillSpritePath);
        }

        if (sprite == null)
        {
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                4f,
                0u,
                SpriteMeshType.FullRect,
                Vector4.zero,
                false);
            sprite.name = "ExperienceFillRect";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            AssetDatabase.ImportAsset(
                ExperienceFillSpritePath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        return sprite;
    }

    private static T GetOrAdd<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        return component != null ? component : obj.AddComponent<T>();
    }

}
#endif
