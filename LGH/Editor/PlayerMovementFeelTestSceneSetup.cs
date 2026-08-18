#if UNITY_EDITOR
using FishNet.Object;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerMovementFeelTestSceneSetup
{
    private const string ScenePath = "Assets/Scenes/PlayerMovementFeelTestScene.unity";
    private const string PlayerPrefabPath = "Assets/CSE/Prerfabs/MyPlayer.prefab";
    private const int GridSize = 17;

    [MenuItem("PaintWar/Movement/Create Player Movement Feel Test Scene")]
    public static void CreateScene()
    {
        Scene originalScene = SceneManager.GetActiveScene();
        bool rebuildInPlace = originalScene.path == ScenePath;
        Scene testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            rebuildInPlace ? NewSceneMode.Single : NewSceneMode.Additive);
        SceneManager.SetActiveScene(testScene);

        try
        {
            BuildLightingAndCamera();
            BuildArena();
            GameObject player = BuildPlayer();
            BuildInterface(
                out Joystick joystick,
                out Button fourButton,
                out Button eightButton,
                out Button freeButton,
                out Button resetButton,
                out TMP_Text selectedLabel,
                out TMP_Text telemetryLabel);
            BuildController(
                player,
                joystick,
                fourButton,
                eightButton,
                freeButton,
                resetButton,
                selectedLabel,
                telemetryLabel);

            EditorSceneManager.SaveScene(testScene, ScenePath);
            Debug.Log("[PlayerMovementFeelTestSceneSetup] Created " + ScenePath);
        }
        finally
        {
            if (!rebuildInPlace && originalScene.IsValid() && originalScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalScene);
            }

            if (!rebuildInPlace)
            {
                EditorSceneManager.CloseScene(testScene, true);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
    }

    private static void BuildLightingAndCamera()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.3f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.58f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.27f, 0.34f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.13f, 0.15f, 0.2f);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 9.7f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.065f, 0.12f);
        cameraObject.transform.position = new Vector3(10.5f, 14f, -12.5f);
        cameraObject.transform.LookAt(Vector3.zero);
        cameraObject.AddComponent<AudioListener>();
    }

    private static void BuildArena()
    {
        Material tileMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/CSE/Materials/WhiteColor.mat");
        GameObject gridRoot = new GameObject("Authored Movement Test Grid");
        int half = GridSize / 2;

        for (int z = 0; z < GridSize; z++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"Tile {x} {z}";
                tile.transform.SetParent(gridRoot.transform, false);
                tile.transform.position = new Vector3(x - half, -0.08f, z - half);
                tile.transform.localScale = new Vector3(0.94f, 0.16f, 0.94f);
                tile.GetComponent<Renderer>().sharedMaterial = tileMaterial;

                if ((x + z) % 2 == 0)
                {
                    MaterialPropertyBlock properties = new MaterialPropertyBlock();
                    Color tint = new Color(0.72f, 0.79f, 0.88f, 1f);
                    properties.SetColor("_BaseColor", tint);
                    properties.SetColor("_Color", tint);
                    tile.GetComponent<Renderer>().SetPropertyBlock(properties);
                }
            }
        }

        BuildWall("North Wall", new Vector3(0f, 0.6f, half + 0.55f), new Vector3(GridSize, 1.2f, 0.3f));
        BuildWall("South Wall", new Vector3(0f, 0.6f, -half - 0.55f), new Vector3(GridSize, 1.2f, 0.3f));
        BuildWall("East Wall", new Vector3(half + 0.55f, 0.6f, 0f), new Vector3(0.3f, 1.2f, GridSize));
        BuildWall("West Wall", new Vector3(-half - 0.55f, 0.6f, 0f), new Vector3(0.3f, 1.2f, GridSize));

        BuildWall("Slalom A", new Vector3(-3f, 0.45f, 2f), new Vector3(0.8f, 0.9f, 3.8f));
        BuildWall("Slalom B", new Vector3(1f, 0.45f, -2f), new Vector3(0.8f, 0.9f, 3.8f));
        BuildWall("Slalom C", new Vector3(4f, 0.45f, 2.5f), new Vector3(0.8f, 0.9f, 3f));
    }

    private static void BuildWall(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetPositionAndRotation(position, Quaternion.identity);
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/CSE/Materials/BlueColor.mat");
    }

    private static GameObject BuildPlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            throw new System.InvalidOperationException("Missing player prefab: " + PlayerPrefabPath);
        }

        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = "MyPlayer - Movement Feel Test";
        player.transform.SetPositionAndRotation(new Vector3(0f, 0.12f, 0f), Quaternion.identity);

        MonoBehaviour[] behaviours = player.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }

        // Keep the scene instance linked to the production prefab while making this
        // comparison harness deliberately offline. A non-zero scene id lets FishNet's
        // Awake validation pass; IsNetworked=false prevents its Start from hiding the
        // object when no client/server has been started.
        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.SetSceneId(0x504D54455354UL);
            networkObject.SetIsNetworked(false);
        }

        Animator animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null || player.GetComponent<CharacterController>() == null)
        {
            throw new System.InvalidOperationException("MyPlayer prefab needs an Animator and CharacterController.");
        }

        animator.enabled = true;
        player.SetActive(true);
        return player;
    }

    private static void BuildController(
        GameObject player,
        Joystick joystick,
        Button fourButton,
        Button eightButton,
        Button freeButton,
        Button resetButton,
        TMP_Text selectedLabel,
        TMP_Text telemetryLabel)
    {
        GameObject controllerObject = new GameObject("Player Movement Feel Test Controller");
        PlayerMovementFeelTestController controller =
            controllerObject.AddComponent<PlayerMovementFeelTestController>();
        SerializedObject serialized = new SerializedObject(controller);
        SetObject(serialized, "playerRoot", player.transform);
        SetObject(serialized, "cameraTransform", Camera.main.transform);
        SetObject(serialized, "characterController", player.GetComponent<CharacterController>());
        SetObject(serialized, "animator", player.GetComponentInChildren<Animator>(true));
        SetObject(serialized, "joystick", joystick);
        SetObject(serialized, "fourDirectionButton", fourButton);
        SetObject(serialized, "eightDirectionButton", eightButton);
        SetObject(serialized, "freeDirectionButton", freeButton);
        SetObject(serialized, "resetButton", resetButton);
        SetObject(serialized, "selectedModeLabel", selectedLabel);
        SetObject(serialized, "telemetryLabel", telemetryLabel);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildInterface(
        out Joystick joystick,
        out Button fourButton,
        out Button eightButton,
        out Button freeButton,
        out Button resetButton,
        out TMP_Text selectedLabel,
        out TMP_Text telemetryLabel)
    {
        GameObject canvasObject = new GameObject(
            "Movement Test Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = CreateUiObject("Comparison Panel", canvasObject.transform);
        SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(1260f, 226f), new Vector2(0.5f, 1f));
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.02f, 0.04f, 0.09f, 0.92f);

        TMP_Text title = CreateText("Title", panel.transform, "PLAYER MOVEMENT FEEL LAB", 38, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(1160f, 48f), new Vector2(0.5f, 1f));

        TMP_Text instructions = CreateText(
            "Instructions", panel.transform,
            "CAMERA-RELATIVE INPUT   |   DRAG JOYSTICK OR WASD   |   1 / 2 / 3 MODE   |   R RESET",
            21, FontStyles.Normal);
        SetRect(instructions.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -62f), new Vector2(1160f, 34f), new Vector2(0.5f, 1f));

        selectedLabel = CreateText("Selected Mode", panel.transform, "SELECTED: 8 DIRECTION", 25, FontStyles.Bold);
        selectedLabel.color = new Color(0.35f, 0.84f, 1f);
        SetRect(selectedLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -98f), new Vector2(1160f, 36f), new Vector2(0.5f, 1f));

        fourButton = CreateButton("4 Direction Button", panel.transform, "1  FOUR", new Color(0.16f, 0.48f, 0.88f));
        eightButton = CreateButton("8 Direction Button", panel.transform, "2  EIGHT", new Color(0.63f, 0.29f, 0.9f));
        freeButton = CreateButton("Free Direction Button", panel.transform, "3  FREE", new Color(0.06f, 0.62f, 0.48f));
        SetRect(fourButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-260f, -139f), new Vector2(240f, 48f), new Vector2(0.5f, 1f));
        SetRect(eightButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -139f), new Vector2(240f, 48f), new Vector2(0.5f, 1f));
        SetRect(freeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(260f, -139f), new Vector2(240f, 48f), new Vector2(0.5f, 1f));

        telemetryLabel = CreateText("Telemetry", panel.transform, "RAW 0.00, 0.00    OUTPUT 0.00, 0.00", 18, FontStyles.Normal);
        SetRect(telemetryLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 13f), new Vector2(1160f, 30f), new Vector2(0.5f, 0f));

        GameObject joystickBackground = CreateUiObject("Joystick Background", canvasObject.transform);
        RectTransform joystickBgRect = joystickBackground.GetComponent<RectTransform>();
        SetRect(joystickBgRect, Vector2.zero, Vector2.zero, new Vector2(190f, 185f), new Vector2(280f, 280f), Vector2.zero);
        Image joystickBgImage = joystickBackground.AddComponent<Image>();
        joystickBgImage.color = new Color(0.08f, 0.12f, 0.2f, 0.82f);

        GameObject handleObject = CreateUiObject("Joystick Handle", joystickBackground.transform);
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        SetRect(handleRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112f, 112f), new Vector2(0.5f, 0.5f));
        Image handleImage = handleObject.AddComponent<Image>();
        handleImage.color = new Color(0.28f, 0.72f, 1f, 0.95f);

        joystick = joystickBackground.AddComponent<Joystick>();
        joystick.joystickBG = joystickBgRect;
        joystick.joystickHandle = handleRect;
        SerializedObject serializedJoystick = new SerializedObject(joystick);
        serializedJoystick.FindProperty("snapToEightDirections").boolValue = false;
        serializedJoystick.FindProperty("snapDeadZone").floatValue = 0f;
        serializedJoystick.ApplyModifiedPropertiesWithoutUndo();

        resetButton = CreateButton("Reset Button", canvasObject.transform, "RESET  (R)", new Color(0.82f, 0.25f, 0.24f));
        SetRect(resetButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
            new Vector2(-54f, -54f), new Vector2(220f, 62f), Vector2.one);

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(canvasObject.transform, false);
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
    {
        TextMeshProUGUI text = CreateUiObject(name, parent).AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, string label, Color color)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = CreateText("Label", buttonObject.transform, label, 22, FontStyles.Bold);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        text.rectTransform.offsetMin = new Vector2(8f, 4f);
        text.rectTransform.offsetMax = new Vector2(-8f, -4f);
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetObject(SerializedObject serialized, string propertyName, Object value)
    {
        serialized.FindProperty(propertyName).objectReferenceValue = value;
    }
}
#endif
