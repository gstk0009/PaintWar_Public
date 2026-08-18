using System.Collections.Generic;
using FishNet.Object;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SkillVfxTestSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string TestScenePath = "Assets/Scenes/SkillTestScene.unity";

    [MenuItem("Tools/PaintWar/Weapons/Create Or Rebuild Skill VFX Test Scene")]
    public static void CreateOrRebuild()
    {
        Scene sourceScene = SceneManager.GetActiveScene();
        if (!sourceScene.isLoaded || sourceScene.path != GameScenePath)
        {
            Debug.LogError(
                "[SkillVfxTestSceneSetup] Open Assets/Scenes/GameScene.unity first.");
            return;
        }

        TilePaintManager sourceTileManager =
            Object.FindFirstObjectByType<TilePaintManager>(FindObjectsInactive.Include);
        GameObject playerPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CSE/Prerfabs/MyPlayer.prefab");
        WeaponRuntimeController sourceWeaponController =
            playerPrefab != null ? playerPrefab.GetComponent<WeaponRuntimeController>() : null;
        if (sourceTileManager == null || sourceWeaponController == null)
        {
            Debug.LogError(
                "[SkillVfxTestSceneSetup] GameScene tile materials or MyPlayer weapon loadout is missing.");
            return;
        }

        SerializedObject sourceTiles = new SerializedObject(sourceTileManager);
        Material neutralMaterial =
            sourceTiles.FindProperty("neutralMaterial").objectReferenceValue as Material;
        List<Material> playerMaterials = ReadMaterials(
            sourceTiles.FindProperty("playerMaterials"));

        SerializedObject sourceWeapons = new SerializedObject(sourceWeaponController);
        List<WeaponData> weapons = ReadWeapons(sourceWeapons.FindProperty("weapons"));
        WeaponVisualSettings visualSettings =
            sourceWeapons.FindProperty("visualSettings").objectReferenceValue
                as WeaponVisualSettings;
        PlayerLevelStatsConfig statsConfig = FindStatsConfig();
        if (neutralMaterial == null || weapons.Count == 0)
        {
            Debug.LogError(
                "[SkillVfxTestSceneSetup] Required material or WeaponData references are missing.");
            return;
        }

        EditorSceneManager.SaveScene(sourceScene);
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "SkillTestScene";

        GameObject root = new GameObject("Skill VFX Test");
        CreateCameraAndLight(root.transform);

        TilePaintManager tileManager;
        Renderer[] tileRenderers = CreateGrid(
            root.transform,
            neutralMaterial,
            playerMaterials,
            out tileManager);

        WeaponRuntimeController controller;
        PlayerLevelStats levelStats;
        CreateCaster(
            root.transform,
            playerMaterials.Count > 0 ? playerMaterials[0] : neutralMaterial,
            weapons,
            visualSettings,
            statsConfig,
            out controller,
            out levelStats);

        Canvas canvas = CreateCanvas(root.transform);
        CreateUi(
            root,
            canvas.transform,
            tileManager,
            tileRenderers,
            controller,
            levelStats,
            weapons);
        CreateEventSystem(root.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TestScenePath);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath));
        Debug.Log(
            "[SkillVfxTestSceneSetup] Saved editor-authored skill preview scene: " +
            TestScenePath);
    }

    private static Renderer[] CreateGrid(
        Transform parent,
        Material neutralMaterial,
        List<Material> playerMaterials,
        out TilePaintManager tileManager)
    {
        const int gridSize = 11;
        const float tileSize = 1f;
        Vector3 origin = new Vector3(-5f, 0f, -5f);

        GameObject managerObject = new GameObject("Tile Paint Manager");
        managerObject.transform.SetParent(parent, false);
        tileManager = managerObject.AddComponent<TilePaintManager>();
        SerializedObject serializedManager = new SerializedObject(tileManager);
        serializedManager.FindProperty("neutralMaterial").objectReferenceValue =
            neutralMaterial;
        SerializedProperty materialsProperty =
            serializedManager.FindProperty("playerMaterials");
        materialsProperty.arraySize = playerMaterials.Count;
        for (int i = 0; i < playerMaterials.Count; i++)
        {
            materialsProperty.GetArrayElementAtIndex(i).objectReferenceValue =
                playerMaterials[i];
        }

        serializedManager.FindProperty("routeClaimsThroughNetworkAuthority").boolValue =
            false;
        serializedManager.FindProperty("bootstrapRoundSystem").boolValue = false;
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        GameObject gridRoot = new GameObject("Preview Grid");
        gridRoot.transform.SetParent(parent, false);
        Renderer[] renderers = new Renderer[gridSize * gridSize];
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "Tile_" + x + "_" + z;
                tile.transform.SetParent(gridRoot.transform, false);
                tile.transform.position =
                    origin + new Vector3(x * tileSize, -0.05f, z * tileSize);
                tile.transform.localScale =
                    new Vector3(tileSize * 0.96f, 0.1f, tileSize * 0.96f);
                Object.DestroyImmediate(tile.GetComponent<Collider>());
                Renderer renderer = tile.GetComponent<Renderer>();
                renderer.sharedMaterial = neutralMaterial;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderers[x * gridSize + z] = renderer;
            }
        }

        return renderers;
    }

    private static void CreateCaster(
        Transform parent,
        Material casterMaterial,
        List<WeaponData> weapons,
        WeaponVisualSettings visualSettings,
        PlayerLevelStatsConfig statsConfig,
        out WeaponRuntimeController controller,
        out PlayerLevelStats levelStats)
    {
        GameObject caster = new GameObject("Preview Caster");
        caster.transform.SetParent(parent, false);
        caster.transform.position = Vector3.zero;
        caster.transform.rotation = Quaternion.identity;
        caster.AddComponent<NetworkObject>();
        PlayerPaintIdentity identity = caster.AddComponent<PlayerPaintIdentity>();
        levelStats = caster.AddComponent<PlayerLevelStats>();
        PlayerStatusEffectController statusEffects =
            caster.AddComponent<PlayerStatusEffectController>();
        controller = caster.AddComponent<WeaponRuntimeController>();

        SerializedObject serializedIdentity = new SerializedObject(identity);
        serializedIdentity.FindProperty("owner").enumValueIndex =
            (int)PaintOwner.Player1;
        serializedIdentity.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedStats = new SerializedObject(levelStats);
        serializedStats.FindProperty("statsConfig").objectReferenceValue = statsConfig;
        serializedStats.FindProperty("fallbackOwner").enumValueIndex =
            (int)PaintOwner.Player1;
        serializedStats.FindProperty("level").intValue = 1;
        serializedStats.FindProperty("currentExp").intValue = 0;
        serializedStats.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("playerTransform").objectReferenceValue =
            caster.transform;
        serializedController.FindProperty("playerIdentity").objectReferenceValue =
            identity;
        serializedController.FindProperty("levelStats").objectReferenceValue =
            levelStats;
        serializedController.FindProperty("statusEffects").objectReferenceValue =
            statusEffects;
        serializedController.FindProperty("visualSettings").objectReferenceValue =
            visualSettings;
        SerializedProperty weaponsProperty =
            serializedController.FindProperty("weapons");
        weaponsProperty.arraySize = weapons.Count;
        for (int i = 0; i < weapons.Count; i++)
        {
            weaponsProperty.GetArrayElementAtIndex(i).objectReferenceValue = weapons[i];
        }

        serializedController.FindProperty("equippedIndex").intValue = 0;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Caster Body";
        body.transform.SetParent(caster.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.65f, 1f, 0.65f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial = casterMaterial;

        GameObject direction = GameObject.CreatePrimitive(PrimitiveType.Cube);
        direction.name = "Cast Direction";
        direction.transform.SetParent(caster.transform, false);
        direction.transform.localPosition = new Vector3(0f, 0.42f, 0.72f);
        direction.transform.localScale = new Vector3(0.18f, 0.18f, 1.25f);
        Object.DestroyImmediate(direction.GetComponent<Collider>());
        direction.GetComponent<Renderer>().sharedMaterial = casterMaterial;
    }

    private static void CreateCameraAndLight(Transform parent)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(8.2f, 9.5f, -9.4f);
        cameraObject.transform.rotation = Quaternion.LookRotation(
            new Vector3(0f, 0.25f, 1f) - cameraObject.transform.position,
            Vector3.up);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(0x12, 0x16, 0x24, 0xFF);
        camera.fieldOfView = 50f;

        GameObject lightObject = new GameObject("Directional Light");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color32(0xFF, 0xED, 0xD8, 0xFF);
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
    }

    private static Canvas CreateCanvas(Transform parent)
    {
        GameObject canvasObject = new GameObject(
            "Skill Test Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void CreateUi(
        GameObject root,
        Transform canvas,
        TilePaintManager tileManager,
        Renderer[] tileRenderers,
        WeaponRuntimeController controller,
        PlayerLevelStats levelStats,
        List<WeaponData> weapons)
    {
        GameObject panel = CreateUiObject("Control Panel", canvas);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(410f, 0f);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.035f, 0.045f, 0.075f, 0.96f);

        TMP_Text title = CreateText(
            "Title",
            panel.transform,
            "SKILL VFX TEST",
            new Vector2(20f, -22f),
            new Vector2(370f, 52f),
            30f);
        title.fontStyle = FontStyles.Bold;

        TMP_Text help = CreateText(
            "Help",
            panel.transform,
            "1) CHOOSE LEVEL  2) CHOOSE WEAPON  3) PREVIEW\n" +
            "SELECT VFX SO, edit it during Play Mode, then preview again.",
            new Vector2(20f, -76f),
            new Vector2(370f, 54f),
            13f);
        help.color = new Color32(0xB8, 0xC2, 0xDC, 0xFF);

        TMP_Text levelHeader = CreateText(
            "Level Header",
            panel.transform,
            "LEVEL",
            new Vector2(20f, -146f),
            new Vector2(100f, 28f),
            16f);
        levelHeader.fontStyle = FontStyles.Bold;

        int[] levels = { 1, 3, 5 };
        Button[] levelButtons = new Button[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            levelButtons[i] = CreateButton(
                "Level " + levels[i],
                panel.transform,
                "LV." + levels[i],
                new Vector2(20f + i * 126f, -178f),
                new Vector2(116f, 42f),
                new Color32(0x25, 0x67, 0xB2, 0xFF));
        }

        TMP_Text weaponHeader = CreateText(
            "Weapon Header",
            panel.transform,
            "WEAPON",
            new Vector2(20f, -236f),
            new Vector2(150f, 28f),
            16f);
        weaponHeader.fontStyle = FontStyles.Bold;

        Button[] weaponButtons = new Button[weapons.Count];
        for (int i = 0; i < weapons.Count; i++)
        {
            int column = i % 2;
            int row = i / 2;
            string label = weapons[i] != null
                ? weapons[i].DisplayName
                : "MISSING";
            weaponButtons[i] = CreateButton(
                "Weapon " + i,
                panel.transform,
                label,
                new Vector2(20f + column * 190f, -270f - row * 54f),
                new Vector2(180f, 44f),
                new Color32(0x3D, 0x36, 0x76, 0xFF));
        }

        TMP_Text ownerHeader = CreateText(
            "Owner Header",
            panel.transform,
            "PLAYER COLOR",
            new Vector2(20f, -500f),
            new Vector2(180f, 28f),
            16f);
        ownerHeader.fontStyle = FontStyles.Bold;

        Color32[] ownerColors =
        {
            new Color32(0xE2, 0x4B, 0x4A, 0xFF),
            new Color32(0x37, 0x8A, 0xDD, 0xFF),
            new Color32(0xEF, 0x9F, 0x27, 0xFF),
            new Color32(0x1D, 0x9E, 0x75, 0xFF)
        };
        Button[] ownerButtons = new Button[4];
        for (int i = 0; i < ownerButtons.Length; i++)
        {
            ownerButtons[i] = CreateButton(
                "Owner " + (i + 1),
                panel.transform,
                "P" + (i + 1),
                new Vector2(20f + i * 94f, -534f),
                new Vector2(84f, 40f),
                ownerColors[i]);
        }

        Button previewButton = CreateButton(
            "Preview Button",
            panel.transform,
            "PREVIEW SKILL",
            new Vector2(20f, -600f),
            new Vector2(370f, 62f),
            new Color32(0xE3, 0x3D, 0x68, 0xFF));
        Button selectButton = CreateButton(
            "Select VFX SO Button",
            panel.transform,
            "SELECT VFX SO",
            new Vector2(20f, -676f),
            new Vector2(180f, 46f),
            new Color32(0x25, 0x87, 0x82, 0xFF));
        Button resetButton = CreateButton(
            "Reset Grid Button",
            panel.transform,
            "RESET GRID",
            new Vector2(210f, -676f),
            new Vector2(180f, 46f),
            new Color32(0x55, 0x5D, 0x72, 0xFF));

        TMP_Text status = CreateText(
            "Status",
            panel.transform,
            "READY",
            new Vector2(20f, -744f),
            new Vector2(370f, 150f),
            16f);
        status.color = new Color32(0xE8, 0xEC, 0xF8, 0xFF);

        SkillVfxTestController testController =
            root.AddComponent<SkillVfxTestController>();
        SerializedObject serialized = new SerializedObject(testController);
        serialized.FindProperty("tileManager").objectReferenceValue = tileManager;
        SetObjectArray(serialized.FindProperty("tileRenderers"), tileRenderers);
        serialized.FindProperty("gridSize").intValue = 11;
        serialized.FindProperty("tileSize").floatValue = 1f;
        serialized.FindProperty("gridOrigin").vector3Value =
            new Vector3(-5f, 0f, -5f);
        serialized.FindProperty("weaponController").objectReferenceValue = controller;
        serialized.FindProperty("levelStats").objectReferenceValue = levelStats;
        SetObjectArray(serialized.FindProperty("weaponButtons"), weaponButtons);
        SetObjectArray(serialized.FindProperty("levelButtons"), levelButtons);
        SerializedProperty levelValues = serialized.FindProperty("levelValues");
        levelValues.arraySize = levels.Length;
        for (int i = 0; i < levels.Length; i++)
        {
            levelValues.GetArrayElementAtIndex(i).intValue = levels[i];
        }

        SetObjectArray(serialized.FindProperty("ownerButtons"), ownerButtons);
        serialized.FindProperty("previewButton").objectReferenceValue = previewButton;
        serialized.FindProperty("selectProfileButton").objectReferenceValue =
            selectButton;
        serialized.FindProperty("resetTilesButton").objectReferenceValue =
            resetButton;
        serialized.FindProperty("statusText").objectReferenceValue = status;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateEventSystem(Transform parent)
    {
        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(parent, false);
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Button button = buttonObject.AddComponent<Button>();

        TMP_Text labelText = CreateText(
            "Label",
            buttonObject.transform,
            label,
            Vector2.zero,
            Vector2.zero,
            Mathf.Min(16f, size.y * 0.34f));
        Stretch(labelText.rectTransform);
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.fontStyle = FontStyles.Bold;
        return button;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize)
    {
        GameObject textObject = CreateUiObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static List<Material> ReadMaterials(SerializedProperty property)
    {
        List<Material> result = new List<Material>();
        if (property == null || !property.isArray)
        {
            return result;
        }

        for (int i = 0; i < property.arraySize; i++)
        {
            Material material =
                property.GetArrayElementAtIndex(i).objectReferenceValue as Material;
            if (material != null)
            {
                result.Add(material);
            }
        }

        return result;
    }

    private static List<WeaponData> ReadWeapons(SerializedProperty property)
    {
        List<WeaponData> result = new List<WeaponData>();
        if (property == null || !property.isArray)
        {
            return result;
        }

        for (int i = 0; i < property.arraySize; i++)
        {
            WeaponData weapon =
                property.GetArrayElementAtIndex(i).objectReferenceValue as WeaponData;
            if (weapon != null)
            {
                result.Add(weapon);
            }
        }

        return result;
    }

    private static PlayerLevelStatsConfig FindStatsConfig()
    {
        string[] guids = AssetDatabase.FindAssets("t:PlayerLevelStatsConfig");
        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<PlayerLevelStatsConfig>(
                AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    private static void SetObjectArray<T>(
        SerializedProperty property,
        T[] values)
        where T : Object
    {
        property.arraySize = values != null ? values.Length : 0;
        for (int i = 0; values != null && i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
