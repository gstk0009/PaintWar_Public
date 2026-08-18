#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponActionHudSceneSetup
{
    [MenuItem("PaintWar/UI/Create Or Update Weapon Action HUD")]
    public static void CreateOrUpdate()
    {
        WeaponActionHud hud = Object.FindFirstObjectByType<WeaponActionHud>();
        if (hud == null)
        {
            Debug.LogWarning("[WeaponActionHud] Add and configure WeaponActionHud in the scene hierarchy first.");
            return;
        }

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeObject = hud.gameObject;
        EditorGUIUtility.PingObject(hud.gameObject);
        Debug.Log("[WeaponActionHud] Selected the editor-authored WeaponActionHud. Layout is no longer changed at runtime.");
    }
}
#endif
