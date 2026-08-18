using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoundTimerHudThemeSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string FramePath = "Assets/LGH/UI/RoundHud/RoundTimerFrame.png";
    private static readonly Vector2 HudSize = new Vector2(590f, 220f);

    [MenuItem("PaintWar/UI/Apply Round Timer Theme")]
    public static void ApplyTheme()
    {
        if (SceneManager.GetActiveScene().path != GameScenePath)
        {
            Debug.LogError("[RoundTimerHudThemeSetup] Open GameScene before applying the theme.");
            return;
        }

        ConfigureFrameImporter();
        Sprite frame = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        if (frame == null)
        {
            Debug.LogError("[RoundTimerHudThemeSetup] Timer frame sprite is missing: " + FramePath);
            return;
        }

        GameObject timerObject = GameObject.Find("Round Timer HUD");
        RoundHudView roundHud = Object.FindFirstObjectByType<RoundHudView>();
        if (timerObject == null || roundHud == null)
        {
            Debug.LogError("[RoundTimerHudThemeSetup] Round Timer HUD or RoundHudView is missing.");
            return;
        }

        Undo.RecordObject(timerObject, "Apply Round Timer Theme");
        Image background = timerObject.GetComponent<Image>();
        if (background == null)
        {
            background = Undo.AddComponent<Image>(timerObject);
        }

        background.sprite = frame;
        background.type = Image.Type.Simple;
        background.preserveAspect = false;
        background.color = Color.white;
        background.raycastTarget = false;

        RectTransform hudRect = timerObject.GetComponent<RectTransform>();
        Undo.RecordObject(hudRect, "Resize Round Timer HUD");
        hudRect.sizeDelta = HudSize;

        TextMeshProUGUI timerText = FindText(timerObject.transform, "Timer");
        TextMeshProUGUI phaseText = FindText(timerObject.transform, "Phase");
        if (timerText == null || phaseText == null)
        {
            Debug.LogError("[RoundTimerHudThemeSetup] Timer or Phase text is missing.");
            return;
        }

        // Keep the authored 50/40 font sizes, but give both lines enough breathing room.
        Undo.RecordObjects(
            new Object[] { timerText.rectTransform, phaseText.rectTransform },
            "Reposition Round Timer Text");
        ConfigureTextRect(timerText.rectTransform, new Vector2(0f, -62f), new Vector2(540f, 82f));
        ConfigureTextRect(phaseText.rectTransform, new Vector2(0f, -148f), new Vector2(540f, 70f));
        timerText.color = new Color32(0xF7, 0xFA, 0xFF, 0xFF);
        timerText.raycastTarget = false;
        phaseText.raycastTarget = false;

        Shadow timerShadow = timerText.GetComponent<Shadow>();
        if (timerShadow == null)
        {
            timerShadow = Undo.AddComponent<Shadow>(timerText.gameObject);
        }

        timerShadow.effectColor = new Color(0.01f, 0.015f, 0.05f, 0.9f);
        timerShadow.effectDistance = new Vector2(2f, -2f);
        timerShadow.useGraphicAlpha = true;

        Image phaseAccent = EnsurePhaseAccent(timerObject.transform, phaseText.transform);
        SerializedObject serializedHud = new SerializedObject(roundHud);
        serializedHud.FindProperty("phaseAccent").objectReferenceValue = phaseAccent;
        SerializedProperty timerSize = serializedHud.FindProperty("timerSize");
        if (timerSize != null)
        {
            timerSize.vector2Value = HudSize;
        }

        serializedHud.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(background);
        EditorUtility.SetDirty(timerText);
        EditorUtility.SetDirty(phaseText);
        EditorUtility.SetDirty(roundHud);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[RoundTimerHudThemeSetup] Applied 590x220 theme matching PlayerStatusHud; font sizes remain 50/40.");
    }

    private static Image EnsurePhaseAccent(Transform parent, Transform phaseText)
    {
        Transform existing = parent.Find("Phase Accent");
        GameObject accentObject;
        if (existing != null)
        {
            accentObject = existing.gameObject;
        }
        else
        {
            accentObject = new GameObject(
                "Phase Accent",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            Undo.RegisterCreatedObjectUndo(accentObject, "Create Phase Accent");
            accentObject.transform.SetParent(parent, false);
        }

        RectTransform rect = accentObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -148f);
        rect.sizeDelta = new Vector2(360f, 50f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        Image image = accentObject.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = new Color(0.15f, 0.82f, 1f, 0.2f);
        image.raycastTarget = false;

        int phaseIndex = phaseText.GetSiblingIndex();
        accentObject.transform.SetSiblingIndex(Mathf.Max(0, phaseIndex));
        phaseText.SetAsLastSibling();
        return image;
    }

    private static void ConfigureTextRect(
        RectTransform rect,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static TextMeshProUGUI FindText(Transform root, string childName)
    {
        Transform child = root.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private static void ConfigureFrameImporter()
    {
        AssetDatabase.ImportAsset(FramePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(FramePath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }
}
