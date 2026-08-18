using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class MagicWeaponFiveLoadoutSetup
{
    private static readonly string[] KeptWeaponIds =
    {
        "meteor_cross",
        "fire_surge",
        "arcane_lance",
        "frost_wall",
        "lightning_chain",
        "crystal_bloom",
        "ground_rupture",
        "red_energy_burst"
    };

    private static readonly string[] RemovedWeaponIds =
    {
        "tidal_lance",
        "crystal_spike",
        "wind_slash"
    };

    [MenuItem("Tools/PaintWar/Weapons/Apply Eight Magic Loadout")]
    public static void ApplyFiveMagicLoadout()
    {
        KeepOnlyFiveWeaponsInPlayerPrefab();
        UseReadableTelegraphs();
        DeleteRemovedWeaponAssets();
        EnsureGameSceneUiAndCleanup();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MagicWeaponFiveLoadoutSetup] Applied eight magic loadout, readable telegraphs, test UI, and scene cleanup.");
    }

    private static void KeepOnlyFiveWeaponsInPlayerPrefab()
    {
        const string prefabPath = "Assets/CSE/Prerfabs/MyPlayer.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("[MagicWeaponFiveLoadoutSetup] Missing prefab: " + prefabPath);
            return;
        }

        WeaponRuntimeController controller = prefab.GetComponent<WeaponRuntimeController>();
        if (controller == null)
        {
            Debug.LogError("[MagicWeaponFiveLoadoutSetup] MyPlayer prefab has no WeaponRuntimeController.");
            return;
        }

        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty weapons = serialized.FindProperty("weapons");
        weapons.arraySize = KeptWeaponIds.Length;
        for (int i = 0; i < KeptWeaponIds.Length; i++)
        {
            weapons.GetArrayElementAtIndex(i).objectReferenceValue = LoadWeapon(KeptWeaponIds[i]);
        }

        serialized.FindProperty("equippedIndex").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(prefab);
    }

    private static void UseReadableTelegraphs()
    {
        for (int i = 0; i < KeptWeaponIds.Length; i++)
        {
            MagicWeaponVfxProfile profile = AssetDatabase.LoadAssetAtPath<MagicWeaponVfxProfile>(
                "Assets/LGH/Data/WeaponVfxProfiles/" + KeptWeaponIds[i] + "_vfx.asset");
            if (profile == null)
            {
                continue;
            }

            SerializedObject serialized = new SerializedObject(profile);
            serialized.FindProperty("telegraphTilePrefab").objectReferenceValue = null;
            serialized.FindProperty("telegraphTileScale").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }
    }

    private static void DeleteRemovedWeaponAssets()
    {
        for (int i = 0; i < RemovedWeaponIds.Length; i++)
        {
            DeleteAssetIfExists("Assets/LGH/Data/Weapons/" + RemovedWeaponIds[i] + ".asset");
            DeleteAssetIfExists("Assets/LGH/Data/WeaponVfxProfiles/" + RemovedWeaponIds[i] + "_vfx.asset");
        }
    }

    private static void EnsureGameSceneUiAndCleanup()
    {
        const string scenePath = "Assets/Scenes/GameScene.unity";
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != scenePath)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[MagicWeaponFiveLoadoutSetup] GameScene has no Canvas.");
            return;
        }

        EnsureMagicTestPanel(canvas.transform);
        RemoveEmptyLayer1();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureMagicTestPanel(Transform canvasTransform)
    {
        Transform existing = canvasTransform.Find("MagicWeaponTestPanel");
        GameObject panel = existing != null ? existing.gameObject : new GameObject("MagicWeaponTestPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(300f, 300f);
        panelRect.anchoredPosition = new Vector2(-160f, -160f);

        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.06f, 0.07f, 0.12f, 0.72f);
        panelImage.raycastTarget = true;

        TMP_Text cooldown = EnsureText(panel.transform, "Status", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(-16f, 26f), 13f);
        cooldown.alignment = TextAlignmentOptions.Center;
        cooldown.text = "Magic Ready";

        Button[] buttons = new Button[KeptWeaponIds.Length];
        for (int i = 0; i < KeptWeaponIds.Length; i++)
        {
            buttons[i] = EnsureButton(panel.transform, i, GetDisplayName(KeptWeaponIds[i]));
        }

        WeaponTestPanel testPanel = panel.GetComponent<WeaponTestPanel>();
        if (testPanel == null)
        {
            testPanel = panel.AddComponent<WeaponTestPanel>();
        }

        SerializedObject serialized = new SerializedObject(testPanel);
        serialized.FindProperty("cooldownText").objectReferenceValue = cooldown;
        SerializedProperty weaponButtons = serialized.FindProperty("weaponButtons");
        weaponButtons.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            weaponButtons.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        }

        serialized.FindProperty("ownerButtons").arraySize = 0;
        serialized.FindProperty("actionHud").objectReferenceValue = Object.FindFirstObjectByType<WeaponActionHud>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);
    }

    private static TMP_Text EnsureText(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        float fontSize)
    {
        Transform existing = parent.Find(name);
        GameObject obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = 8f;
        text.fontSizeMax = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button EnsureButton(Transform parent, int index, string label)
    {
        string name = "MagicButton_" + index;
        Transform existing = parent.Find(name);
        GameObject obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -44f - index * 29f);
        rect.sizeDelta = new Vector2(-18f, 26f);

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0.13f, 0.15f, 0.25f, 0.92f);
        image.raycastTarget = true;

        TMP_Text text = EnsureText(obj.transform, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 11f);
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;

        Button button = obj.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.75f, 0.85f, 1f, 1f);
        colors.pressedColor = new Color(0.55f, 0.65f, 0.9f, 1f);
        button.colors = colors;
        return button;
    }

    private static void RemoveEmptyLayer1()
    {
        GameObject layer = null;
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null &&
                transforms[i].name == "Layer1" &&
                transforms[i].parent != null &&
                transforms[i].parent.name == "Tiles")
            {
                layer = transforms[i].gameObject;
                break;
            }
        }

        if (layer == null)
        {
            return;
        }

        Tilemap tilemap = layer.GetComponent<Tilemap>();
        bool hasTiles = tilemap != null && tilemap.cellBounds.allPositionsWithin.GetEnumerator().MoveNext() && tilemap.GetUsedTilesCount() > 0;
        if (!layer.activeSelf && !hasTiles)
        {
            Object.DestroyImmediate(layer);
        }
    }

    private static WeaponData LoadWeapon(string id)
    {
        WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/LGH/Data/Weapons/" + id + ".asset");
        if (weapon == null)
        {
            Debug.LogError("[MagicWeaponFiveLoadoutSetup] Missing weapon: " + id);
        }

        return weapon;
    }

    private static string GetDisplayName(string id)
    {
        WeaponData weapon = LoadWeapon(id);
        return weapon != null ? weapon.DisplayName : id;
    }

    private static void DeleteAssetIfExists(string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
    }
}
