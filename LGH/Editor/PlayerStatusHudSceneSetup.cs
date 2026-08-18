#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PlayerStatusHudSceneSetup
{
    [MenuItem("PaintWar/UI/Create Player Status HUD")]
    public static void CreateOrSelect()
    {
        PlayerStatusHud existing = Object.FindFirstObjectByType<PlayerStatusHud>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            return;
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[PlayerStatusHud] A Canvas is required in the active scene.");
            return;
        }

        GameObject root = CreateUiObject("PlayerStatusHud", canvas.transform);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(28f, -28f);
        rootRect.sizeDelta = new Vector2(360f, 128f);

        Image background = root.AddComponent<Image>();
        background.color = new Color32(22, 25, 45, 230);
        background.raycastTarget = false;

        TMP_Text level = CreateText("LevelText", root.transform, "LV. 1", 30f, FontStyles.Bold);
        SetRect(level.rectTransform, new Vector2(18f, -14f), new Vector2(150f, 42f));

        Image gaugeBack = CreateImage("ExperienceGaugeBackground", root.transform, new Color32(7, 10, 24, 230));
        SetRect(gaugeBack.rectTransform, new Vector2(18f, -67f), new Vector2(324f, 38f));

        Image fill = CreateImage("ExperienceGaugeFill", gaugeBack.transform, new Color32(96, 224, 255, 255));
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;

        TMP_Text experience = CreateText("ExperienceText", gaugeBack.transform, "0 / 500 EXP", 17f, FontStyles.Bold);
        experience.alignment = TextAlignmentOptions.Center;
        RectTransform experienceRect = experience.rectTransform;
        experienceRect.anchorMin = Vector2.zero;
        experienceRect.anchorMax = Vector2.one;
        experienceRect.offsetMin = Vector2.zero;
        experienceRect.offsetMax = Vector2.zero;

        PlayerStatusHud hud = root.AddComponent<PlayerStatusHud>();
        SerializedObject serializedHud = new SerializedObject(hud);
        serializedHud.FindProperty("experienceFill").objectReferenceValue = fill;
        serializedHud.FindProperty("levelText").objectReferenceValue = level;
        serializedHud.FindProperty("experienceText").objectReferenceValue = experience;
        serializedHud.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(root, "Create Player Status HUD");
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = root;
        Debug.Log("[PlayerStatusHud] Created editor-authored HUD at the upper left. Adjust it directly in the scene.");
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = CreateUiObject(name, parent);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
    {
        GameObject obj = CreateUiObject(name, parent);
        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
#endif
