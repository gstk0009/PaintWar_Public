#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 3D 라운드 결과 무대와 표시 전용 캐릭터 프리팹을 에디터에서 생성해 GameScene에 저장합니다.
/// 반복 실행 시 이 도구가 소유한 루트만 다시 만들어 안전하게 갱신합니다.
/// </summary>
public static class RoundResultStageSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string PresentationRootName = "Round Result Presentation";
    private const string DisplayAvatarPrefabPath =
        "Assets/LGH/Prefabs/RoundResult/RoundResultDisplayAvatar.prefab";
    private const string DisplayAnimatorPath =
        "Assets/LGH/Animations/RoundResult/RoundResultDisplay.controller";
    private const string IdleAnimationPath =
        "Assets/ithappy/Creative_Characters_FREE/Animations/Other_Animations/Idle_Look_Around.anim";
    private const string FallingAnimationPath =
        "Assets/LGH/Animations/RoundResult/X Bot@Falling Idle.fbx";
    private const string DefeatedAnimationPath =
        "Assets/LGH/Animations/RoundResult/X Bot@Defeated.fbx";
    private const string VictoryAnimationPath =
        "Assets/LGH/Animations/RoundResult/X Bot@Victory.fbx";
    private const string MaterialFolder = "Assets/LGH/Materials/RoundResult";
    private static readonly string[] GameplayCanvasNames =
    {
        "Canvas",
        "Occupation HUD Canvas",
        "Round Timer Canvas",
        "Joystick Canvas",
        "Weapon Action Canvas",
        "Player Status Canvas",
        "Round Transition Canvas",
        "Gameplay Item Canvas",
        "Gameplay Emoji Canvas",
        "Debuff Canvas",
        "Bomb Mine Combo Canvas",
        "Territory Notification Canvas",
        "Meteor Notification Canvas"
    };

    [MenuItem("PaintWar/UI/Create Or Rebuild 3D Round Result Stage")]
    public static void CreateOrRebuild()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
        {
            Debug.LogError(
                "[RoundResultStageSetup] Open Assets/Scenes/GameScene.unity first.");
            return;
        }

        Camera gameplayCamera = Camera.main;
        Canvas[] gameplayCanvases = FindGameplayCanvases();
        if (gameplayCamera == null || gameplayCanvases.Length != GameplayCanvasNames.Length)
        {
            Debug.LogError(
                "[RoundResultStageSetup] Main Camera or one or more gameplay Canvases are missing.");
            return;
        }

        EnsureFolder("Assets/LGH", "Prefabs");
        EnsureFolder("Assets/LGH/Prefabs", "RoundResult");
        EnsureFolder("Assets/LGH", "Materials");
        EnsureFolder("Assets/LGH/Materials", "RoundResult");

        if (!ConfigureRoundResultAnimationAssets())
        {
            return;
        }

        GameObject displayAvatarPrefab = CreateOrReplaceDisplayAvatarPrefab();
        if (displayAvatarPrefab == null)
        {
            return;
        }

        GameObject existing = GameObject.Find(PresentationRootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject existingOverlay = GameObject.Find("Round Result Overlay");
        if (existingOverlay != null && existingOverlay.transform.parent == null)
        {
            Undo.DestroyObjectImmediate(existingOverlay);
        }

        Material backdropMaterial = GetOrCreateMaterial(
            "RoundResult_Backdrop",
            new Color32(0x12, 0x16, 0x32, 0xFF));
        Material floorMaterial = GetOrCreateMaterial(
            "RoundResult_Floor",
            new Color32(0x24, 0x2B, 0x52, 0xFF));
        Material boardMaterial = GetOrCreateMaterial(
            "RoundResult_Board",
            new Color32(0xD7, 0x35, 0xE8, 0xFF));
        Material platformMaterial = GetOrCreateMaterial(
            "RoundResult_Platform",
            new Color32(0x6E, 0x4B, 0xB6, 0xFF));
        Material goldMaterial = GetOrCreateMaterial(
            "RoundResult_ChampionGold",
            new Color32(0xFF, 0xC5, 0x25, 0xFF),
            true);
        Material particleMaterial = GetOrCreateParticleMaterial();

        GameObject root = new GameObject(PresentationRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create 3D Round Result Stage");
        root.transform.position = new Vector3(0f, -120f, 0f);

        GameObject stageVisualRoot = CreateChild(root.transform, "Stage Visuals");
        GameObject backdrop = CreatePrimitive(
            PrimitiveType.Cube,
            "Backdrop",
            stageVisualRoot.transform,
            new Vector3(0f, 3.1f, 3.4f),
            new Vector3(13f, 7.5f, 0.45f),
            backdropMaterial);
        GameObject floor = CreatePrimitive(
            PrimitiveType.Cube,
            "Stage Floor",
            stageVisualRoot.transform,
            new Vector3(0f, -0.35f, 0f),
            new Vector3(13f, 0.55f, 7.5f),
            floorMaterial);

        TextMeshPro stageHeader = CreateWorldText(
            "Survivor Header",
            stageVisualRoot.transform,
            "SURVIVORS",
            new Vector3(0f, 5.4f, 3.05f),
            5.5f,
            new Vector2(8f, 1f));
        stageHeader.color = new Color32(0xFF, 0xEF, 0xFF, 0xFF);
        stageHeader.gameObject.SetActive(false);

        GameObject roundBoardRoot = CreateChild(stageVisualRoot.transform, "Four Player Board");
        stageHeader.transform.SetParent(roundBoardRoot.transform, true);
        float[] slotPositions = { -4.35f, -1.45f, 1.45f, 4.35f };
        List<RoundResultSlotView> slots = new List<RoundResultSlotView>(4);
        for (int i = 0; i < slotPositions.Length; i++)
        {
            slots.Add(CreateSlot(
                roundBoardRoot.transform,
                displayAvatarPrefab,
                boardMaterial,
                platformMaterial,
                i,
                slotPositions[i]));
        }

        GameObject championRoot = CreateChild(stageVisualRoot.transform, "Champion Showcase");
        GameObject podium = CreatePrimitive(
            PrimitiveType.Cylinder,
            "Champion Podium",
            championRoot.transform,
            new Vector3(0f, 0f, 0.4f),
            new Vector3(2.5f, 0.35f, 2.5f),
            goldMaterial);
        GameObject championAvatarObject =
            (GameObject)PrefabUtility.InstantiatePrefab(displayAvatarPrefab, championRoot.transform);
        championAvatarObject.name = "Champion Display Avatar";
        championAvatarObject.transform.localPosition = new Vector3(0f, 0.38f, 0.1f);
        championAvatarObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        RoundResultDisplayAvatar championAvatar =
            championAvatarObject.GetComponent<RoundResultDisplayAvatar>();

        TextMeshPro championLabel = CreateWorldText(
            "Champion Name",
            championRoot.transform,
            "CHAMPION",
            new Vector3(0f, 3.65f, 2.2f),
            4.5f,
            new Vector2(7f, 0.9f));
        championLabel.color = new Color32(0xFF, 0xD7, 0x4A, 0xFF);
        championLabel.gameObject.SetActive(false);

        ParticleSystem confetti = CreateConfetti(
            championRoot.transform,
            particleMaterial);
        championRoot.SetActive(false);

        CreateStageLights(stageVisualRoot.transform);

        Camera resultCamera = CreateResultCamera(root.transform);
        Transform roundCameraPose = CreateCameraPose(
            root.transform,
            "Camera Pose - Round Board",
            new Vector3(0f, 3.15f, -11.6f),
            new Vector3(0f, 2.15f, 0.8f));
        Transform championCameraStartPose = CreateCameraPose(
            root.transform,
            "Camera Pose - Champion Start",
            new Vector3(0f, 2.7f, -10f),
            new Vector3(0f, 1.65f, 0.4f));
        Transform championCameraEndPose = CreateCameraPose(
            root.transform,
            "Camera Pose - Champion End",
            new Vector3(0f, 2.05f, -6.6f),
            new Vector3(0f, 1.55f, 0.35f));

        Canvas resultCanvas = CreateResultCanvas(
            null,
            out CanvasGroup resultCanvasGroup,
            out GraphicRaycaster resultRaycaster,
            out Image blackoutImage,
            out TextMeshProUGUI titleText,
            out TextMeshProUGUI detailText,
            out Button finalExitButton,
            out CanvasGroup finalExitButtonGroup,
            out TextMeshProUGUI finalExitButtonLabel);

        RoundResultStageView stageView = root.AddComponent<RoundResultStageView>();
        SerializedObject serializedView = new SerializedObject(stageView);
        SetReference(serializedView, "stageVisualRoot", stageVisualRoot);
        SetReference(serializedView, "roundBoardRoot", roundBoardRoot);
        SetReference(serializedView, "championRoot", championRoot);
        SetReference(serializedView, "resultCanvas", resultCanvas);
        SetReference(serializedView, "resultCanvasGroup", resultCanvasGroup);
        SetReference(serializedView, "resultRaycaster", resultRaycaster);
        SetReference(serializedView, "blackoutImage", blackoutImage);
        SetReference(serializedView, "gameplayCamera", gameplayCamera);
        SetReference(serializedView, "resultCamera", resultCamera);
        SerializedProperty gameplayCanvasesProperty =
            serializedView.FindProperty("gameplayCanvases");
        gameplayCanvasesProperty.arraySize = gameplayCanvases.Length;
        for (int i = 0; i < gameplayCanvases.Length; i++)
        {
            gameplayCanvasesProperty.GetArrayElementAtIndex(i).objectReferenceValue =
                gameplayCanvases[i];
        }
        SetReference(serializedView, "roundCameraPose", roundCameraPose);
        SetReference(serializedView, "championCameraStartPose", championCameraStartPose);
        SetReference(serializedView, "championCameraEndPose", championCameraEndPose);
        SetReference(serializedView, "titleText", titleText);
        SetReference(serializedView, "detailText", detailText);
        SetReference(serializedView, "finalExitButton", finalExitButton);
        SetReference(serializedView, "finalExitButtonGroup", finalExitButtonGroup);
        SetReference(serializedView, "finalExitButtonLabel", finalExitButtonLabel);
        SetReference(serializedView, "championAvatar", championAvatar);
        SetReference(serializedView, "championPodium", podium.transform);
        SetReference(serializedView, "championConfetti", confetti);

        SerializedProperty slotsProperty = serializedView.FindProperty("slots");
        slotsProperty.arraySize = slots.Count;
        for (int i = 0; i < slots.Count; i++)
        {
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        }

        serializedView.ApplyModifiedPropertiesWithoutUndo();

        stageVisualRoot.SetActive(false);
        resultCamera.enabled = false;
        resultCanvas.enabled = false;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log(
            "[RoundResultStageSetup] Saved editor-authored 3D result stage into GameScene.");
    }

    private static Canvas[] FindGameplayCanvases()
    {
        List<Canvas> canvases = new List<Canvas>(GameplayCanvasNames.Length);
        for (int i = 0; i < GameplayCanvasNames.Length; i++)
        {
            GameObject canvasObject = GameObject.Find(GameplayCanvasNames[i]);
            Canvas canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
            if (canvas != null)
            {
                canvases.Add(canvas);
            }
        }

        return canvases.ToArray();
    }

    private static GameObject CreateOrReplaceDisplayAvatarPrefab()
    {
        AnimatorController displayController =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(DisplayAnimatorPath);
        if (displayController == null)
        {
            Debug.LogError(
                "[RoundResultStageSetup] Result animator controller was not found: " +
                DisplayAnimatorPath);
            return null;
        }

        GameObject instance = new GameObject("RoundResultLivePlayerAnchor");
        RoundResultDisplayAvatar displayAvatar =
            instance.AddComponent<RoundResultDisplayAvatar>();

        SerializedObject serializedAvatar = new SerializedObject(displayAvatar);
        SetReference(serializedAvatar, "presentationController", displayController);
        serializedAvatar.ApplyModifiedPropertiesWithoutUndo();

        GameObject savedPrefab =
            PrefabUtility.SaveAsPrefabAsset(instance, DisplayAvatarPrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        return savedPrefab;
    }

    private static bool ConfigureRoundResultAnimationAssets()
    {
        AnimationClip fallingClip = ConfigureMixamoAnimation(
            FallingAnimationPath,
            "Eliminated",
            true);
        AnimationClip defeatedClip = ConfigureMixamoAnimation(
            DefeatedAnimationPath,
            "Defeated",
            false);
        AnimationClip victoryClip = ConfigureMixamoAnimation(
            VictoryAnimationPath,
            "Victory",
            true);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleAnimationPath);

        if (fallingClip == null ||
            defeatedClip == null ||
            victoryClip == null ||
            idleClip == null)
        {
            Debug.LogError(
                "[RoundResultStageSetup] One or more result animation clips are missing.");
            return false;
        }

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(DisplayAnimatorPath);
        if (controller == null)
        {
            controller =
                AnimatorController.CreateAnimatorControllerAtPath(DisplayAnimatorPath);
        }

        if (controller == null || controller.layers.Length == 0)
        {
            Debug.LogError(
                "[RoundResultStageSetup] Could not create the result Animator Controller.");
            return false;
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        ChildAnimatorState[] existingStates = stateMachine.states;
        for (int i = existingStates.Length - 1; i >= 0; i--)
        {
            stateMachine.RemoveState(existingStates[i].state);
        }

        AnimatorState idleState = stateMachine.AddState("Idle", new Vector3(220f, 40f));
        idleState.motion = idleClip;
        AnimatorState eliminatedState =
            stateMachine.AddState("Eliminated", new Vector3(460f, -40f));
        eliminatedState.motion = fallingClip;
        AnimatorState defeatedState =
            stateMachine.AddState("Defeated", new Vector3(460f, 80f));
        defeatedState.motion = defeatedClip;
        AnimatorState victoryState =
            stateMachine.AddState("Victory", new Vector3(460f, 200f));
        victoryState.motion = victoryClip;
        stateMachine.defaultState = idleState;

        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static AnimationClip ConfigureMixamoAnimation(
        string assetPath,
        string clipName,
        bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError(
                "[RoundResultStageSetup] Mixamo FBX was not found: " + assetPath);
            return null;
        }

        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.clipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            Debug.LogError(
                "[RoundResultStageSetup] No animation take was found in: " + assetPath);
            return null;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = i == 0 ? clipName : clipName + " " + (i + 1);
            clips[i].loop = loop;
            clips[i].loopTime = loop;
            clips[i].loopPose = loop;
            clips[i].keepOriginalOrientation = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].lockRootRotation = true;
            clips[i].lockRootHeightY = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].heightFromFeet = true;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < importedAssets.Length; i++)
        {
            AnimationClip clip = importedAssets[i] as AnimationClip;
            if (clip != null &&
                !clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }

        Debug.LogError(
            "[RoundResultStageSetup] Imported animation clip could not be loaded: " +
            assetPath);
        return null;
    }

    private static RoundResultSlotView CreateSlot(
        Transform parent,
        GameObject displayAvatarPrefab,
        Material boardMaterial,
        Material platformMaterial,
        int index,
        float xPosition)
    {
        GameObject slotRoot = CreateChild(parent, "Player Slot " + (index + 1));
        slotRoot.transform.localPosition = new Vector3(xPosition, 0f, 0f);

        GameObject backplate = CreatePrimitive(
            PrimitiveType.Cube,
            "Editable Backplate",
            slotRoot.transform,
            new Vector3(0f, 2.25f, 2.15f),
            new Vector3(2.55f, 4.8f, 0.38f),
            boardMaterial);
        GameObject platform = CreatePrimitive(
            PrimitiveType.Cube,
            "Drop Platform",
            slotRoot.transform,
            new Vector3(0f, 0f, 0.2f),
            new Vector3(2.2f, 0.26f, 2.2f),
            platformMaterial);

        GameObject avatar =
            (GameObject)PrefabUtility.InstantiatePrefab(displayAvatarPrefab, slotRoot.transform);
        avatar.name = "Display Avatar";
        avatar.transform.localPosition = new Vector3(0f, 0.2f, 0.1f);
        avatar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        TextMeshPro nameText = CreateWorldText(
            "Player Name",
            slotRoot.transform,
            "PLAYER " + (index + 1),
            new Vector3(0f, 4.05f, 1.85f),
            3f,
            new Vector2(2.35f, 0.65f));

        RoundResultSlotView slotView = slotRoot.AddComponent<RoundResultSlotView>();
        SerializedObject serializedSlot = new SerializedObject(slotView);
        serializedSlot.FindProperty("owner").enumValueIndex = index + 1;
        SetReference(serializedSlot, "backplateRenderer", backplate.GetComponent<Renderer>());
        SetReference(serializedSlot, "platform", platform.transform);
        SetReference(
            serializedSlot,
            "avatar",
            avatar.GetComponent<RoundResultDisplayAvatar>());
        SetReference(serializedSlot, "nameText", nameText);
        serializedSlot.ApplyModifiedPropertiesWithoutUndo();
        return slotView;
    }

    private static Camera CreateResultCamera(Transform parent)
    {
        GameObject cameraObject = CreateChild(parent, "Round Result Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(0x0B, 0x0E, 0x22, 0xFF);
        camera.fieldOfView = 48f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 45f;
        camera.depth = 50f;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.enabled = false;
        return camera;
    }

    private static Transform CreateCameraPose(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 localLookTarget)
    {
        GameObject pose = CreateChild(parent, name);
        pose.transform.localPosition = localPosition;
        Vector3 worldTarget = parent.TransformPoint(localLookTarget);
        pose.transform.rotation =
            Quaternion.LookRotation(worldTarget - pose.transform.position, Vector3.up);
        return pose.transform;
    }

    private static Canvas CreateResultCanvas(
        Transform parent,
        out CanvasGroup canvasGroup,
        out GraphicRaycaster raycaster,
        out Image blackoutImage,
        out TextMeshProUGUI titleText,
        out TextMeshProUGUI detailText,
        out Button finalExitButton,
        out CanvasGroup finalExitButtonGroup,
        out TextMeshProUGUI finalExitButtonLabel)
    {
        GameObject canvasObject =
            new GameObject("Round Result Overlay", typeof(RectTransform), typeof(Canvas));
        if (parent != null)
        {
            canvasObject.transform.SetParent(parent, false);
        }
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        raycaster = canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        GameObject blackout = CreateUiObject("Blackout", canvasObject.transform);
        Stretch(blackout.GetComponent<RectTransform>());
        blackoutImage = blackout.AddComponent<Image>();
        blackoutImage.color = Color.black;
        blackoutImage.raycastTarget = false;

        titleText = CreateOverlayText(
            "Result Title",
            canvasObject.transform,
            new Vector2(0.5f, 0.86f),
            new Vector2(1300f, 130f),
            76f);
        titleText.text = "ROUND RESULTS";
        titleText.color = Color.white;

        detailText = CreateOverlayText(
            "Result Detail",
            canvasObject.transform,
            new Vector2(0.5f, 0.75f),
            new Vector2(1100f, 90f),
            42f);
        detailText.text = "WHO WILL SURVIVE?";
        detailText.color = new Color32(0xFF, 0xDA, 0x52, 0xFF);

        GameObject exitObject = CreateUiObject("FinalResultExitButton", canvasObject.transform);
        RectTransform exitRect = exitObject.GetComponent<RectTransform>();
        exitRect.anchorMin = new Vector2(0.5f, 0.08f);
        exitRect.anchorMax = new Vector2(0.5f, 0.08f);
        exitRect.pivot = new Vector2(0.5f, 0.5f);
        exitRect.anchoredPosition = Vector2.zero;
        exitRect.sizeDelta = new Vector2(360f, 92f);
        Image exitImage = exitObject.AddComponent<Image>();
        exitImage.color = new Color32(0xE7, 0x36, 0x4B, 0xF2);
        exitImage.raycastTarget = true;
        finalExitButton = exitObject.AddComponent<Button>();
        finalExitButtonGroup = exitObject.AddComponent<CanvasGroup>();
        finalExitButtonGroup.alpha = 0f;
        finalExitButtonGroup.interactable = false;
        finalExitButtonGroup.blocksRaycasts = false;

        finalExitButtonLabel = CreateOverlayText(
            "Label",
            exitObject.transform,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            38f);
        Stretch(finalExitButtonLabel.rectTransform);
        finalExitButtonLabel.text = "EXIT  30";
        finalExitButtonLabel.color = Color.white;
        finalExitButtonLabel.raycastTarget = false;
        finalExitButton.interactable = false;

        canvas.enabled = true;
        canvasGroup.alpha = 0f;
        return canvas;
    }

    private static TextMeshProUGUI CreateOverlayText(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 size,
        float fontSize)
    {
        GameObject textObject = CreateUiObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static TextMeshPro CreateWorldText(
        string name,
        Transform parent,
        string value,
        Vector3 localPosition,
        float fontSize,
        Vector2 size)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = size;
        return text;
    }

    private static void CreateStageLights(Transform parent)
    {
        GameObject keyObject = CreateChild(parent, "Result Key Light");
        keyObject.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);
        Light keyLight = keyObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color32(0xFF, 0xE4, 0xD0, 0xFF);
        keyLight.intensity = 1.25f;
        keyLight.shadows = LightShadows.None;

        GameObject rimObject = CreateChild(parent, "Champion Rim Light");
        rimObject.transform.localPosition = new Vector3(0f, 5f, 1.8f);
        rimObject.transform.localRotation = Quaternion.Euler(70f, 180f, 0f);
        Light rimLight = rimObject.AddComponent<Light>();
        rimLight.type = LightType.Spot;
        rimLight.color = new Color32(0xFF, 0xC8, 0x3D, 0xFF);
        rimLight.intensity = 5f;
        rimLight.range = 14f;
        rimLight.spotAngle = 55f;
        rimLight.shadows = LightShadows.None;
    }

    private static ParticleSystem CreateConfetti(
        Transform parent,
        Material particleMaterial)
    {
        GameObject particleObject = CreateChild(parent, "Champion Confetti");
        particleObject.transform.localPosition = new Vector3(0f, 4.5f, 0f);
        particleObject.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.duration = 2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.gravityModifier = 0.75f;
        main.maxParticles = 96;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 72) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 1.2f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
            particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color32(0xFF, 0xD4, 0x32, 0xFF), 0f),
                new GradientColorKey(new Color32(0xF0, 0x46, 0xD8, 0xFF), 0.5f),
                new GradientColorKey(new Color32(0x45, 0xD9, 0xFF, 0xFF), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer =
            particleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sharedMaterial = particleMaterial;
        return particles;
    }

    private static GameObject CreatePrimitive(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject target = GameObject.CreatePrimitive(type);
        target.name = name;
        target.transform.SetParent(parent, false);
        target.transform.localPosition = localPosition;
        target.transform.localRotation = Quaternion.identity;
        target.transform.localScale = localScale;
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return target;
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = 5;
        child.transform.SetParent(parent, false);
        return child;
    }

    private static Material GetOrCreateMaterial(
        string name,
        Color color,
        bool emission = false)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader != null && material.shader != unlitShader)
        {
            material.shader = unlitShader;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (emission && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.6f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GetOrCreateParticleMaterial()
    {
        string path = MaterialFolder + "/RoundResult_Confetti.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        material = new Material(shader) { name = "RoundResult_Confetti" };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string fullPath = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static void SetReference(
        SerializedObject serializedObject,
        string propertyName,
        Object value)
    {
        serializedObject.FindProperty(propertyName).objectReferenceValue = value;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        for (int i = 0; i < target.transform.childCount; i++)
        {
            SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
        }
    }
}
#endif
