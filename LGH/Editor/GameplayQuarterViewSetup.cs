#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayQuarterViewSetup
{
    private const string MenuPath = "PaintWar/Camera/Apply Gameplay Quarter View";
    private const float Pitch = 43f;
    private const float Yaw = 0f;
    private const float FieldOfView = 55f;
    private static readonly Vector3 FollowOffset = new Vector3(0f, 7.4f, -9.62f);

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/GameScene.unity",
        "Assets/Scenes/SkillTestScene.unity"
    };

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        int updatedCameraCount = 0;
        for (int i = 0; i < ScenePaths.Length; i++)
        {
            updatedCameraCount += UpdateScene(ScenePaths[i]);
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            "[GameplayQuarterViewSetup] Applied quarter view to " +
            updatedCameraCount + " gameplay camera(s).");
    }

    private static int UpdateScene(string scenePath)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            return 0;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        int updatedCount = 0;
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                CameraFollow[] follows =
                    roots[rootIndex].GetComponentsInChildren<CameraFollow>(true);
                for (int followIndex = 0; followIndex < follows.Length; followIndex++)
                {
                    CameraFollow follow = follows[followIndex];
                    Transform cameraTransform = follow.transform;
                    cameraTransform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);

                    SerializedObject serializedFollow = new SerializedObject(follow);
                    serializedFollow.FindProperty("offset").vector3Value = FollowOffset;
                    serializedFollow.ApplyModifiedPropertiesWithoutUndo();

                    Camera camera = follow.GetComponent<Camera>();
                    if (camera != null)
                    {
                        camera.fieldOfView = FieldOfView;
                    }

                    EditorUtility.SetDirty(follow);
                    EditorUtility.SetDirty(cameraTransform);
                    if (camera != null)
                    {
                        EditorUtility.SetDirty(camera);
                    }

                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        return updatedCount;
    }
}
#endif
