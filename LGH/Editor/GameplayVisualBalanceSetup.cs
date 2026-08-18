#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class GameplayVisualBalanceSetup
{
    private const string MenuPath =
        "PaintWar/Visual/Apply GameScene Brightness Balance";
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string ProfileFolder = "Assets/CSE/Settings";
    private const string ProfilePath =
        ProfileFolder + "/GameSceneVisualProfile.asset";

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        EnsureFolder("Assets/CSE", "Settings");
        VolumeProfile profile = CreateOrUpdateProfile();
        AssignProfileToGameScene(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = profile;
        Debug.Log(
            "[GameplayVisualBalanceSetup] Preserved the night sky with " +
            "mobile-friendly bloom and no vignette.");
    }

    private static VolumeProfile CreateOrUpdateProfile()
    {
        VolumeProfile profile =
            AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "GameSceneVisualProfile";
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.active = true;
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.Neutral;

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 1.05f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.16f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.42f;
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = false;

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.active = false;

        MotionBlur motionBlur = GetOrAdd<MotionBlur>(profile);
        motionBlur.active = false;

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static T GetOrAdd<T>(VolumeProfile profile)
        where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
        {
            return component;
        }

        component = profile.Add<T>(true);
        if (!AssetDatabase.Contains(component))
        {
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        EditorUtility.SetDirty(component);
        return component;
    }

    private static void AssignProfileToGameScene(VolumeProfile profile)
    {
        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenScene(
            ScenePath,
            OpenSceneMode.Additive);
        try
        {
            EditorSceneManager.SetActiveScene(scene);
            Volume target = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && target == null; i++)
            {
                Volume[] volumes =
                    roots[i].GetComponentsInChildren<Volume>(true);
                for (int j = 0; j < volumes.Length; j++)
                {
                    if (volumes[j].isGlobal)
                    {
                        target = volumes[j];
                        break;
                    }
                }
            }

            if (target == null)
            {
                throw new System.InvalidOperationException(
                    "GameScene is missing its authored global Volume.");
            }

            target.sharedProfile = profile;
            EditorUtility.SetDirty(target);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                EditorSceneManager.SetActiveScene(previousActiveScene);
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
