#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RoundTransitionHudSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    static RoundTransitionHudSceneSetup()
    {
        EditorApplication.delayCall += InstallWhenGameSceneIsOpen;
    }

    [MenuItem("PaintWar/UI/Create Round Transition HUD")]
    public static void CreateOrSelect()
    {
        RoundTransitionView existing = Object.FindFirstObjectByType<RoundTransitionView>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            return;
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[RoundTransitionHud] GameScene Canvas is missing.");
            return;
        }

        GameObject root = new GameObject("Round Transition HUD", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = 5;
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        GameObject dimObject = new GameObject("Transition Dim", typeof(RectTransform), typeof(Image));
        dimObject.layer = 5;
        dimObject.transform.SetParent(root.transform, false);
        Stretch(dimObject.GetComponent<RectTransform>());
        Image dimImage = dimObject.GetComponent<Image>();
        dimImage.color = Color.black;
        dimImage.raycastTarget = false;

        GameObject content = new GameObject("Transition Copy", typeof(RectTransform), typeof(VerticalLayoutGroup));
        content.layer = 5;
        content.transform.SetParent(root.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = contentRect.anchorMax = contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(860f, 260f);
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 16f;

        TextMeshProUGUI title = CreateText("Title", content.transform, 70f);
        TextMeshProUGUI detail = CreateText("Detail", content.transform, 38f);
        RoundTransitionView view = root.AddComponent<RoundTransitionView>();
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        serialized.FindProperty("dimImage").objectReferenceValue = dimImage;
        serialized.FindProperty("titleText").objectReferenceValue = title;
        serialized.FindProperty("detailText").objectReferenceValue = detail;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(root, "Create Round Transition HUD");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = root;
    }

    private static void InstallWhenGameSceneIsOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != GameScenePath) return;
        if (Object.FindFirstObjectByType<RoundTransitionView>(FindObjectsInactive.Include) != null) return;
        CreateOrSelect();
        EditorSceneManager.SaveScene(activeScene);
        Debug.Log("[RoundTransitionHud] Saved editor-authored transition HUD into GameScene.");
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float fontSize)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        LayoutElement element = obj.GetComponent<LayoutElement>();
        element.preferredHeight = fontSize + 30f;
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
#endif
