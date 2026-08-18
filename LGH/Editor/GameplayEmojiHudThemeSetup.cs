using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayEmojiHudThemeSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string FramePath = "Assets/LGH/UI/EmojiHud/EmojiQuickFrame.png";
    private static readonly Vector2 PanelSize = new Vector2(300f, 100f);
    private static readonly float[] SlotCenterX = { 58f, 150f, 242f };

    [MenuItem("PaintWar/UI/Apply Gameplay Emoji HUD Theme")]
    public static void ApplyTheme()
    {
        if (SceneManager.GetActiveScene().path != GameScenePath)
        {
            Debug.LogError("[GameplayEmojiHudThemeSetup] Open GameScene before applying the theme.");
            return;
        }

        ConfigureFrameImporter();
        Sprite frame = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        if (frame == null)
        {
            Debug.LogError("[GameplayEmojiHudThemeSetup] Emoji frame sprite is missing: " + FramePath);
            return;
        }

        GameObject panelObject = GameObject.Find("Gameplay Emoji Slots");
        if (panelObject == null)
        {
            Debug.LogError("[GameplayEmojiHudThemeSetup] Gameplay Emoji Slots is missing.");
            return;
        }

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        Image panelImage = panelObject.GetComponent<Image>();
        Outline oldOutline = panelObject.GetComponent<Outline>();
        if (panelRect == null || panelImage == null)
        {
            Debug.LogError("[GameplayEmojiHudThemeSetup] Emoji panel components are incomplete.");
            return;
        }

        Undo.RecordObjects(
            oldOutline != null
                ? new Object[] { panelRect, panelImage, oldOutline }
                : new Object[] { panelRect, panelImage },
            "Apply Gameplay Emoji HUD Theme");

        panelRect.sizeDelta = PanelSize;
        panelImage.sprite = frame;
        panelImage.type = Image.Type.Simple;
        panelImage.preserveAspect = false;
        panelImage.color = Color.white;
        panelImage.raycastTarget = false;
        if (oldOutline != null)
        {
            oldOutline.enabled = false;
        }

        for (int i = 0; i < SlotCenterX.Length; i++)
        {
            Transform slotTransform = panelObject.transform.Find("EmojiSlot" + (i + 1));
            if (slotTransform == null)
            {
                Debug.LogError("[GameplayEmojiHudThemeSetup] Emoji slot " + (i + 1) + " is missing.");
                return;
            }

            ConfigureSlot(slotTransform, SlotCenterX[i]);
        }

        EditorUtility.SetDirty(panelRect);
        EditorUtility.SetDirty(panelImage);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[GameplayEmojiHudThemeSetup] Applied simplified 300x100 emoji panel with three aligned slots.");
    }

    private static void ConfigureSlot(Transform slotTransform, float centerX)
    {
        RectTransform slotRect = slotTransform as RectTransform;
        Image slotImage = slotTransform.GetComponent<Image>();
        Button button = slotTransform.GetComponent<Button>();
        if (slotRect == null || slotImage == null || button == null)
        {
            Debug.LogError(
                "[GameplayEmojiHudThemeSetup] Slot components are incomplete: " +
                slotTransform.name);
            return;
        }

        Undo.RecordObjects(
            new Object[] { slotRect, slotImage, button },
            "Style Gameplay Emoji Slot");

        slotRect.anchorMin = new Vector2(0f, 0.5f);
        slotRect.anchorMax = new Vector2(0f, 0.5f);
        slotRect.pivot = new Vector2(0.5f, 0.5f);
        slotRect.anchoredPosition = new Vector2(centerX, 0f);
        slotRect.sizeDelta = new Vector2(70f, 60f);
        slotRect.localScale = Vector3.one;
        slotRect.localRotation = Quaternion.identity;

        slotImage.sprite = null;
        slotImage.type = Image.Type.Simple;
        slotImage.color = new Color32(0x07, 0x0C, 0x2D, 0xD8);
        slotImage.raycastTarget = true;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.72f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.92f, 0.62f, 1f, 1f);
        colors.selectedColor = new Color(0.82f, 0.94f, 1f, 1f);
        colors.disabledColor = new Color(0.42f, 0.48f, 0.62f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = colors;
        button.targetGraphic = slotImage;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        Transform iconTransform = slotTransform.Find("Icon");
        if (iconTransform == null)
        {
            return;
        }

        RectTransform iconRect = iconTransform as RectTransform;
        Image iconImage = iconTransform.GetComponent<Image>();
        if (iconRect == null || iconImage == null)
        {
            return;
        }

        Undo.RecordObjects(
            new Object[] { iconRect, iconImage },
            "Fit Gameplay Emoji Icon");
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(62f, 62f);
        iconRect.localScale = Vector3.one;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
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
