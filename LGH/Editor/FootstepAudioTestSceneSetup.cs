#if UNITY_EDITOR
using System.IO;
using CSE;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FootstepAudioTestSceneSetup
{
    private const string ScenePath = "Assets/Scenes/FootstepAudioTestScene.unity";
    private const string CandidateFolder = "Assets/Resources/SFX/FootstepCandidates";
    private const int GridSize = 11;
    private const int SampleRate = 44100;
    private const string GelSquishPath =
        CandidateFolder + "/D_soft_gel_squish_oneshot.wav";
    private const string WaxPopPath =
        CandidateFolder + "/E_wax_bubble_pop_oneshot.wav";
    private const string CreamyThockPath =
        CandidateFolder + "/F_creamy_keyboard_thock_oneshot.wav";
    private const string PoppyKeycapPath =
        CandidateFolder + "/G_poppy_keycap_oneshot.wav";

    [MenuItem("PaintWar/Audio/Create Footstep Audio Test Scene")]
    public static void CreateScene()
    {
        GenerateAsmrCandidates();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureCandidateImporter(GelSquishPath);
        ConfigureCandidateImporter(WaxPopPath);
        ConfigureCandidateImporter(CreamyThockPath);
        ConfigureCandidateImporter(PoppyKeycapPath);

        Scene originalScene = SceneManager.GetActiveScene();
        bool rebuildInPlace = originalScene.path == ScenePath;
        Scene testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            rebuildInPlace ? NewSceneMode.Single : NewSceneMode.Additive);
        SceneManager.SetActiveScene(testScene);

        try
        {
            BuildLighting();
            BuildCamera();
            BuildBgm();
            Renderer[] tiles = BuildTileGrid();
            Transform avatarRoot = BuildAvatar(out Transform avatarVisual, out AudioSource audioSource);
            BuildInterface(
                out TMP_Text selectedLabel,
                out TMP_Text statsLabel,
                out Button[] candidateButtons);

            GameObject controllerObject = new GameObject("Footstep Audio Test Controller");
            FootstepAudioTestController controller =
                controllerObject.AddComponent<FootstepAudioTestController>();
            SerializedObject serializedController = new SerializedObject(controller);
            SetObject(serializedController, "avatarRoot", avatarRoot);
            SetObject(serializedController, "avatarVisual", avatarVisual);
            SetObject(serializedController, "movementAudioSource", audioSource);
            SetObject(serializedController, "selectedCandidateLabel", selectedLabel);
            SetObject(serializedController, "playbackStatsLabel", statsLabel);
            SetObjectArray(serializedController, "candidateButtons", candidateButtons);
            SetObjectArray(serializedController, "tileRenderers", tiles);
            SetObjectArray(
                serializedController,
                "candidateClips",
                new AudioClip[]
                {
                    LoadClip(CandidateFolder + "/CURRENT_movement_step_oneshot.wav"),
                    LoadClip(CandidateFolder + "/A_balanced_paint_bounce_oneshot.wav"),
                    LoadClip(CandidateFolder + "/B_weighty_tile_bounce_oneshot.wav"),
                    LoadClip(CandidateFolder + "/C_arcade_score_bounce_oneshot.wav"),
                    LoadClip(GelSquishPath),
                    LoadClip(WaxPopPath),
                    LoadClip(CreamyThockPath),
                    LoadClip(PoppyKeycapPath)
                });
            SetStringArray(
                serializedController,
                "candidateNames",
                new[]
                {
                    "1. CURRENT",
                    "2. CANDIDATE A",
                    "3. CANDIDATE B",
                    "4. CANDIDATE C",
                    "5. D - CLOSE-MIC SQUISHY",
                    "6. E - CRISPY WAXPPU BALL",
                    "7. F - CREAMY KEYBOARD THOCK",
                    "8. G - POPPY KEYCAP"
                });
            SetColorArray(
                serializedController,
                "candidatePaintColors",
                new[]
                {
                    new Color(0.82f, 0.84f, 0.88f, 1f),
                    new Color(0.22f, 0.62f, 1f, 1f),
                    new Color(1f, 0.34f, 0.29f, 1f),
                    new Color(1f, 0.76f, 0.17f, 1f),
                    new Color(1f, 0.38f, 0.72f, 1f),
                    new Color(0.64f, 0.38f, 1f, 1f),
                    new Color(0.19f, 0.88f, 0.73f, 1f),
                    new Color(0.35f, 0.95f, 1f, 1f)
                });
            serializedController.FindProperty("gridSize").intValue = GridSize;
            serializedController.FindProperty("tileSize").floatValue = 1f;
            serializedController.FindProperty("moveSpeed").floatValue = 8.5f;
            serializedController.FindProperty("stepVolume").floatValue = 0.86f;
            serializedController.FindProperty("pitchVariation").floatValue = 0.03f;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(testScene, ScenePath);
            Debug.Log("[FootstepAudioTestSceneSetup] Created " + ScenePath);
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

    private static void BuildLighting()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.35f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.48f, 0.65f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.35f, 0.43f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.25f, 0.3f);
    }

    private static void BuildCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 8.1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.16f, 0.28f);
        cameraObject.transform.position = new Vector3(8.5f, 11.5f, -10.5f);
        cameraObject.transform.LookAt(Vector3.zero);
        cameraObject.AddComponent<AudioListener>();
    }

    private static void BuildBgm()
    {
        AudioClip gameBgm =
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/BGM/GameBGM.mp3");
        if (gameBgm == null)
        {
            throw new System.InvalidOperationException("Missing GameBGM.mp3");
        }

        GameObject bgmObject = new GameObject("Game BGM - Actual Mix Level");
        AudioSource bgmSource = bgmObject.AddComponent<AudioSource>();
        bgmSource.clip = gameBgm;
        bgmSource.playOnAwake = true;
        bgmSource.loop = true;
        bgmSource.volume = 0.448f;
        bgmSource.pitch = 1f;
        bgmSource.spatialBlend = 0f;
    }

    private static Renderer[] BuildTileGrid()
    {
        GameObject gridRoot = new GameObject("Authored Test Tile Grid");
        Renderer[] renderers = new Renderer[GridSize * GridSize];
        Material tileMaterial =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/CSE/Materials/WhiteColor.mat");
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        Color neutral = new Color(0.93f, 0.94f, 0.96f, 1f);
        int halfGrid = GridSize / 2;

        for (int z = 0; z < GridSize; z++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "Tile " + x + " " + z;
                tile.transform.SetParent(gridRoot.transform, false);
                tile.transform.position = new Vector3(x - halfGrid, 0f, z - halfGrid);
                tile.transform.localScale = new Vector3(0.94f, 0.12f, 0.94f);

                Collider collider = tile.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                Renderer renderer = tile.GetComponent<Renderer>();
                renderer.sharedMaterial = tileMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                properties.Clear();
                properties.SetColor("_BaseColor", neutral);
                properties.SetColor("_Color", neutral);
                renderer.SetPropertyBlock(properties);
                renderers[z * GridSize + x] = renderer;
            }
        }

        return renderers;
    }

    private static Transform BuildAvatar(out Transform visual, out AudioSource audioSource)
    {
        GameObject root = new GameObject("Test Walker");
        root.transform.position = Vector3.zero;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Avatar Visual";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.48f, 0.55f, 0.48f);
        body.GetComponent<Renderer>().sharedMaterial =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/CSE/Materials/BlueColor.mat");
        Collider bodyCollider = body.GetComponent<Collider>();
        if (bodyCollider != null)
        {
            Object.DestroyImmediate(bodyCollider);
        }

        GameObject facing = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        facing.name = "Facing Marker";
        facing.transform.SetParent(body.transform, false);
        facing.transform.localPosition = new Vector3(0f, 0.2f, 0.78f);
        facing.transform.localScale = Vector3.one * 0.18f;
        facing.GetComponent<Renderer>().sharedMaterial =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/CSE/Materials/YellowColor.mat");
        Collider facingCollider = facing.GetComponent<Collider>();
        if (facingCollider != null)
        {
            Object.DestroyImmediate(facingCollider);
        }

        audioSource = root.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;

        visual = body.transform;
        return root.transform;
    }

    private static void BuildInterface(
        out TMP_Text selectedLabel,
        out TMP_Text statsLabel,
        out Button[] candidateButtons)
    {
        GameObject canvasObject = new GameObject(
            "Footstep Test Canvas",
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

        GameObject panel = CreateUiObject("Top Panel", canvasObject.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        SetRect(panelRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f),
            new Vector2(1250f, 230f));
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.025f, 0.055f, 0.11f, 0.9f);

        TMP_Text title = CreateText(
            "Title",
            panel.transform,
            "FOOTSTEP SFX WALK TEST",
            38,
            FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(1100f, 52f));

        TMP_Text instructions = CreateText(
            "Instructions",
            panel.transform,
            "WASD / ARROWS: MOVE    1-8: SWITCH SFX    R: RESET",
            23,
            FontStyles.Normal);
        SetRect(instructions.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -70f), new Vector2(1120f, 40f));

        selectedLabel = CreateText(
            "Selected Candidate",
            panel.transform,
            "SELECTED: 1. CURRENT",
            27,
            FontStyles.Bold);
        selectedLabel.color = new Color(0.42f, 0.82f, 1f);
        SetRect(selectedLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -111f), new Vector2(1120f, 42f));

        statsLabel = CreateText(
            "Playback Stats",
            panel.transform,
            "PLAYED 0  |  SKIPPED WHILE BUSY 0",
            20,
            FontStyles.Normal);
        SetRect(statsLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -147f), new Vector2(1120f, 34f));

        string[] labels =
        {
            "1 CURRENT", "2 A", "3 B", "4 C",
            "5 D SQUISH", "6 E WAXPPU", "7 F THOCK", "8 G KEYCAP"
        };
        Color[] colors =
        {
            new Color(0.34f, 0.38f, 0.46f),
            new Color(0.12f, 0.48f, 0.88f),
            new Color(0.8f, 0.2f, 0.16f),
            new Color(0.88f, 0.58f, 0.08f),
            new Color(0.86f, 0.2f, 0.55f),
            new Color(0.5f, 0.24f, 0.82f),
            new Color(0.08f, 0.57f, 0.47f),
            new Color(0.06f, 0.58f, 0.72f)
        };
        candidateButtons = new Button[8];
        for (int i = 0; i < candidateButtons.Length; i++)
        {
            candidateButtons[i] = CreateButton(
                "Candidate " + (i + 1),
                panel.transform,
                labels[i],
                colors[i]);
            RectTransform buttonRect = candidateButtons[i].GetComponent<RectTransform>();
            SetRect(
                buttonRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-507.5f + i * 145f, 18f),
                new Vector2(135f, 48f));
        }

        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(canvasObject.transform, false);
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle)
    {
        GameObject textObject = CreateUiObject(name, parent);
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Color backgroundColor)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = backgroundColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        TMP_Text buttonLabel = CreateText(
            "Label",
            buttonObject.transform,
            label,
            22,
            FontStyles.Bold);
        SetRect(
            buttonLabel.rectTransform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        buttonLabel.rectTransform.offsetMin = new Vector2(8f, 4f);
        buttonLabel.rectTransform.offsetMax = new Vector2(-8f, -4f);
        return button;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, anchorMax.y >= 1f ? 1f : 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
        {
            throw new System.InvalidOperationException("Missing audio clip: " + path);
        }

        return clip;
    }

    private static void GenerateAsmrCandidates()
    {
        Directory.CreateDirectory(CandidateFolder);
        WriteMonoWav(GelSquishPath, CreateSoftGelSquish());
        WriteMonoWav(WaxPopPath, CreateWaxBubblePop());
        WriteMonoWav(CreamyThockPath, CreateCreamyKeyboardThock());
        WriteMonoWav(PoppyKeycapPath, CreatePoppyKeycap());
    }

    private static float[] CreateSoftGelSquish()
    {
        const float duration = 0.19f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(5821);
        float veryLowNoise = 0f;
        float lowNoise = 0f;
        float midNoise = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float rawNoise = (float)(random.NextDouble() * 2.0 - 1.0);
            veryLowNoise += (rawNoise - veryLowNoise) * 0.009f;
            lowNoise += (rawNoise - lowNoise) * 0.045f;
            midNoise += (rawNoise - midNoise) * 0.19f;

            float pressAttack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.009f));
            float pressEnvelope = pressAttack * Mathf.Exp(-time * 17f);
            float compression =
                veryLowNoise * pressEnvelope * 1.15f +
                (lowNoise - veryLowNoise) * pressEnvelope * 0.62f +
                (midNoise - lowNoise) * pressEnvelope * 0.12f;

            float returnTime = Mathf.Max(0f, time - 0.082f);
            float returnAttack =
                time >= 0.082f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnTime / 0.012f))
                    : 0f;
            float returnEnvelope = returnAttack * Mathf.Exp(-returnTime * 20f);
            float foamReturn =
                veryLowNoise * returnEnvelope * 0.8f +
                (lowNoise - veryLowNoise) * returnEnvelope * 0.45f;

            float rubberCreakEnvelope =
                Mathf.Exp(-Mathf.Pow((time - 0.038f) / 0.017f, 2f)) * 0.08f;
            float rubberCreak =
                Mathf.Sin(2f * Mathf.PI * (610f - time * 2100f) * time) *
                rubberCreakEnvelope;

            samples[i] = compression + foamReturn + rubberCreak;
        }

        ApplyShortFade(samples, 0.008f);
        ApplySoftClip(samples, 1.7f);
        return Normalize(samples, 0.86f);
    }

    private static float[] CreateWaxBubblePop()
    {
        const float duration = 0.165f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(9147);
        float[] crackTimes = { 0f, 0.007f, 0.015f, 0.026f, 0.041f, 0.058f };
        float[] crackWeights = { 1f, 0.72f, 0.83f, 0.58f, 0.64f, 0.4f };
        float lowNoise = 0f;
        float smoothNoise = 0f;
        float previousSmoothNoise = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float rawNoise = (float)(random.NextDouble() * 2.0 - 1.0);
            lowNoise += (rawNoise - lowNoise) * 0.035f;
            smoothNoise += (rawNoise - smoothNoise) * 0.34f;
            float crispNoise = smoothNoise - previousSmoothNoise;
            previousSmoothNoise = smoothNoise;

            float crackleEnvelope = 0f;
            float shellResonance = 0f;
            for (int crack = 0; crack < crackTimes.Length; crack++)
            {
                float crackTime = time - crackTimes[crack];
                if (crackTime < 0f)
                {
                    continue;
                }

                float envelope = Mathf.Exp(-crackTime * (190f + crack * 12f));
                crackleEnvelope += envelope * crackWeights[crack];
                shellResonance +=
                    Mathf.Sin(2f * Mathf.PI * (1450f + crack * 170f) * crackTime) *
                    envelope *
                    crackWeights[crack];
            }

            float shellCrack =
                crispNoise * crackleEnvelope * 0.44f +
                shellResonance * 0.12f;

            float coreTime = Mathf.Max(0f, time - 0.032f);
            float coreAttack =
                time >= 0.032f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(coreTime / 0.014f))
                    : 0f;
            float coreEnvelope = coreAttack * Mathf.Exp(-coreTime * 14f);
            float softCore =
                lowNoise * coreEnvelope * 0.85f +
                Mathf.Sin(2f * Mathf.PI * 118f * coreTime) * coreEnvelope * 0.11f;

            samples[i] = shellCrack + softCore;
        }

        ApplyShortFade(samples, 0.007f);
        ApplySoftClip(samples, 1.45f);
        return Normalize(samples, 0.88f);
    }

    private static float[] CreateCreamyKeyboardThock()
    {
        const float duration = 0.135f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(22173);
        float lowNoise = 0f;
        float previousLowNoise = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float downAttack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.0012f));
            float deepDecay = downAttack * Mathf.Exp(-time * 30f);
            float shellDecay = downAttack * Mathf.Exp(-time * 54f);

            float rawNoise = (float)(random.NextDouble() * 2.0 - 1.0);
            lowNoise += (rawNoise - lowNoise) * 0.12f;
            float plasticTransient = lowNoise - previousLowNoise;
            previousLowNoise = lowNoise;

            float bottomOut =
                Mathf.Sin(2f * Mathf.PI * 128f * time + 0.2f) * deepDecay * 0.54f +
                Mathf.Sin(2f * Mathf.PI * 214f * time + 1.1f) * deepDecay * 0.31f +
                Mathf.Sin(2f * Mathf.PI * 365f * time + 0.5f) * shellDecay * 0.2f +
                Mathf.Sin(2f * Mathf.PI * 720f * time + 2.1f) * shellDecay * 0.08f;

            float releaseTime = Mathf.Max(0f, time - 0.072f);
            float releaseAttack =
                time >= 0.072f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(releaseTime / 0.0009f))
                    : 0f;
            float releaseDecay = releaseAttack * Mathf.Exp(-releaseTime * 72f);
            float keyReturn =
                (Mathf.Sin(2f * Mathf.PI * 285f * releaseTime) * 0.22f +
                 Mathf.Sin(2f * Mathf.PI * 910f * releaseTime) * 0.08f) *
                releaseDecay;

            samples[i] =
                bottomOut +
                plasticTransient * shellDecay * 0.16f +
                keyReturn;
        }

        ApplyShortFade(samples, 0.005f);
        return Normalize(samples, 0.86f);
    }

    private static float[] CreatePoppyKeycap()
    {
        const float duration = 0.105f;
        float[] samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        System.Random random = new System.Random(34729);
        float smoothNoise = 0f;
        float previousSmoothNoise = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)SampleRate;
            float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.0008f));
            float bodyDecay = attack * Mathf.Exp(-time * 42f);
            float clickDecay = attack * Mathf.Exp(-time * 92f);

            float rawNoise = (float)(random.NextDouble() * 2.0 - 1.0);
            smoothNoise += (rawNoise - smoothNoise) * 0.28f;
            float snap = smoothNoise - previousSmoothNoise;
            previousSmoothNoise = smoothNoise;

            float press =
                Mathf.Sin(2f * Mathf.PI * 205f * time + 0.4f) * bodyDecay * 0.42f +
                Mathf.Sin(2f * Mathf.PI * 435f * time + 1.4f) * bodyDecay * 0.27f +
                Mathf.Sin(2f * Mathf.PI * 1040f * time + 0.8f) * clickDecay * 0.13f +
                snap * clickDecay * 0.22f;

            float releaseTime = Mathf.Max(0f, time - 0.049f);
            float releaseAttack =
                time >= 0.049f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(releaseTime / 0.0007f))
                    : 0f;
            float releaseDecay = releaseAttack * Mathf.Exp(-releaseTime * 88f);
            float release =
                (Mathf.Sin(2f * Mathf.PI * 520f * releaseTime) * 0.22f +
                 Mathf.Sin(2f * Mathf.PI * 1320f * releaseTime) * 0.09f) *
                releaseDecay;

            samples[i] = press + release;
        }

        ApplyShortFade(samples, 0.003f);
        return Normalize(samples, 0.84f);
    }

    private static void ApplyShortFade(float[] samples, float fadeSeconds)
    {
        int fadeSamples = Mathf.Max(1, Mathf.RoundToInt(fadeSeconds * SampleRate));
        for (int i = 0; i < fadeSamples && i < samples.Length; i++)
        {
            samples[samples.Length - 1 - i] *= i / (float)fadeSamples;
        }
    }

    private static float[] Normalize(float[] samples, float targetPeak)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }

        if (peak <= 0.00001f)
        {
            return samples;
        }

        float scale = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }

        return samples;
    }

    private static void ApplySoftClip(float[] samples, float drive)
    {
        float normalization = (float)System.Math.Tanh(drive);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] =
                (float)System.Math.Tanh(samples[i] * drive) /
                Mathf.Max(0.0001f, normalization);
        }
    }

    private static void WriteMonoWav(string path, float[] samples)
    {
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int dataSize = samples.Length * sizeof(short);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)Mathf.RoundToInt(
                    Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                writer.Write(value);
            }
        }
    }

    private static void ConfigureCandidateImporter(string path)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            return;
        }

        importer.forceToMono = true;
        importer.loadInBackground = false;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.78f;
        settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    private static void SetObject(
        SerializedObject serializedObject,
        string propertyName,
        Object value)
    {
        serializedObject.FindProperty(propertyName).objectReferenceValue = value;
    }

    private static void SetObjectArray<T>(
        SerializedObject serializedObject,
        string propertyName,
        T[] values)
        where T : Object
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    private static void SetStringArray(
        SerializedObject serializedObject,
        string propertyName,
        string[] values)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }

    private static void SetColorArray(
        SerializedObject serializedObject,
        string propertyName,
        Color[] values)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).colorValue = values[i];
        }
    }
}
#endif
