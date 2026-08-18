#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class RoundedTileVisualSetup
{
    private const string MenuPath = "PaintWar/Tiles/Apply Optimized Rounded Tile Visuals";
    private const string MeshFolder = "Assets/CSE/TileControl/Generated";
    private const string MeshPath = MeshFolder + "/RoundedTopTile.asset";
    private const string MaterialFolder = "Assets/CSE/Materials/Tiles";
    private const string CubePrefabPath = "Assets/CSE/Prerfabs/Cube.prefab";
    private const string ShaderName = "PaintWar/Rounded Tile Surface";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/GameScene.unity",
        "Assets/Scenes/SkillTestScene.unity"
    };

    private static readonly string[] MaterialNames =
    {
        "TileNeutral",
        "TileRed",
        "TileBlue",
        "TileYellow",
        "TileGreen"
    };

    private static readonly Color[] MaterialColors =
    {
        new Color(0.9f, 0.92f, 0.96f, 1f),
        new Color(1f, 0.035f, 0.02f, 1f),
        new Color(0.015f, 0.16f, 1f, 1f),
        new Color(1f, 0.78f, 0.02f, 1f),
        new Color(0.06f, 0.72f, 0.015f, 1f)
    };

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        EnsureFolder("Assets/CSE/TileControl", "Generated");
        EnsureFolder("Assets/CSE/Materials", "Tiles");

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            throw new System.InvalidOperationException("Missing shader: " + ShaderName);
        }

        Mesh mesh = CreateOrUpdateMesh();
        Material[] materials = CreateOrUpdateMaterials(shader);
        UpdateCubePrefab(mesh, materials[0]);
        for (int i = 0; i < ScenePaths.Length; i++)
        {
            UpdateSceneMaterials(ScenePaths[i], materials);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = mesh;
        Debug.Log(
            "[RoundedTileVisualSetup] Applied optimized rounded tile visuals. " +
            "vertices=" + mesh.vertexCount + " triangles=" + mesh.triangles.Length / 3 + ".");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static Mesh CreateOrUpdateMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        Mesh mesh = existing != null ? existing : new Mesh();
        mesh.name = "RoundedTopTile";
        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt16;

        const float halfSize = 0.4875f;
        const float topY = 0.5f;
        const float shoulderY = 0.42f;
        const float bottomY = 0.18f;
        const float cornerRadius = 0.08f;
        const float bevelInset = 0.065f;
        const int cornerSegments = 2;

        List<Vector3> perimeter = new List<Vector3>();
        List<Vector3> outward = new List<Vector3>();
        BuildRoundedPerimeter(
            halfSize,
            cornerRadius,
            cornerSegments,
            perimeter,
            outward);

        int ringCount = perimeter.Count;
        List<Vector3> vertices = new List<Vector3>(ringCount * 6 + 2);
        List<Vector3> normals = new List<Vector3>(ringCount * 6 + 2);
        List<int> triangles = new List<int>(ringCount * 18);

        int topCenter = vertices.Count;
        vertices.Add(new Vector3(0f, topY, 0f));
        normals.Add(Vector3.up);
        int topRingStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 inner = perimeter[i] - outward[i] * bevelInset;
            inner.y = topY;
            vertices.Add(inner);
            normals.Add(Vector3.up);
        }
        AddFan(triangles, topCenter, topRingStart, ringCount, true);

        int bevelOuterStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 outer = perimeter[i];
            outer.y = shoulderY;
            vertices.Add(outer);
            normals.Add((outward[i] * 0.82f + Vector3.up * 0.57f).normalized);
        }

        int bevelInnerStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 inner = perimeter[i] - outward[i] * bevelInset;
            inner.y = topY;
            vertices.Add(inner);
            normals.Add((outward[i] * 0.38f + Vector3.up * 0.93f).normalized);
        }
        AddRing(triangles, bevelOuterStart, bevelInnerStart, ringCount, false);

        int sideBottomStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 bottom = perimeter[i];
            bottom.y = bottomY;
            vertices.Add(bottom);
            normals.Add(outward[i]);
        }

        int sideTopStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 top = perimeter[i];
            top.y = shoulderY;
            vertices.Add(top);
            normals.Add(outward[i]);
        }
        AddRing(triangles, sideBottomStart, sideTopStart, ringCount, false);

        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, bottomY, 0f));
        normals.Add(Vector3.down);
        int bottomRingStart = vertices.Count;
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 bottom = perimeter[i];
            bottom.y = bottomY;
            vertices.Add(bottom);
            normals.Add(Vector3.down);
        }
        AddFan(triangles, bottomCenter, bottomRingStart, ringCount, false);

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0, true);
        mesh.bounds = new Bounds(
            new Vector3(0f, (topY + bottomY) * 0.5f, 0f),
            new Vector3(halfSize * 2f, topY - bottomY, halfSize * 2f));

        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        return mesh;
    }

    private static void BuildRoundedPerimeter(
        float halfSize,
        float radius,
        int cornerSegments,
        List<Vector3> points,
        List<Vector3> normals)
    {
        Vector2[] centers =
        {
            new Vector2(halfSize - radius, halfSize - radius),
            new Vector2(-halfSize + radius, halfSize - radius),
            new Vector2(-halfSize + radius, -halfSize + radius),
            new Vector2(halfSize - radius, -halfSize + radius)
        };
        float[] startAngles = { 0f, 90f, 180f, 270f };

        for (int corner = 0; corner < centers.Length; corner++)
        {
            for (int segment = 0; segment <= cornerSegments; segment++)
            {
                float angle = (startAngles[corner] +
                               90f * segment / cornerSegments) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = centers[corner] + direction * radius;
                points.Add(new Vector3(point.x, 0f, point.y));
                normals.Add(new Vector3(direction.x, 0f, direction.y));
            }
        }
    }

    private static void AddFan(
        List<int> triangles,
        int center,
        int ringStart,
        int count,
        bool reverse)
    {
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            if (reverse)
            {
                triangles.Add(center);
                triangles.Add(ringStart + next);
                triangles.Add(ringStart + i);
            }
            else
            {
                triangles.Add(center);
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + next);
            }
        }
    }

    private static void AddRing(
        List<int> triangles,
        int lowerStart,
        int upperStart,
        int count,
        bool reverse)
    {
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int lower = lowerStart + i;
            int lowerNext = lowerStart + next;
            int upper = upperStart + i;
            int upperNext = upperStart + next;

            if (reverse)
            {
                triangles.Add(lower);
                triangles.Add(upperNext);
                triangles.Add(upper);
                triangles.Add(lower);
                triangles.Add(lowerNext);
                triangles.Add(upperNext);
            }
            else
            {
                triangles.Add(lower);
                triangles.Add(upper);
                triangles.Add(upperNext);
                triangles.Add(lower);
                triangles.Add(upperNext);
                triangles.Add(lowerNext);
            }
        }
    }

    private static Material[] CreateOrUpdateMaterials(Shader shader)
    {
        Material[] materials = new Material[MaterialNames.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            string path = MaterialFolder + "/" + MaterialNames[i] + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = MaterialNames[i]
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.enableInstancing = true;
            material.SetColor("_BaseColor", MaterialColors[i]);
            material.SetColor("_Color", MaterialColors[i]);
            material.SetFloat("_Smoothness", 0.72f);
            material.SetFloat("_WideHighlightStrength", 0.1f);
            material.SetFloat("_SpecularStrength", 0.56f);
            material.SetFloat("_RimStrength", 0.045f);
            material.SetFloat("_SideShade", 0.74f);
            EditorUtility.SetDirty(material);
            materials[i] = material;
        }

        return materials;
    }

    private static void UpdateCubePrefab(Mesh mesh, Material neutralMaterial)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CubePrefabPath);
        try
        {
            MeshFilter meshFilter = root.GetComponent<MeshFilter>();
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            if (meshFilter == null || renderer == null)
            {
                throw new System.InvalidOperationException(
                    "Cube prefab must keep its root MeshFilter and MeshRenderer.");
            }

            meshFilter.sharedMesh = mesh;
            renderer.sharedMaterial = neutralMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(root, CubePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void UpdateSceneMaterials(string scenePath, Material[] materials)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            bool changed = false;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                TilePaintManager[] managers =
                    roots[i].GetComponentsInChildren<TilePaintManager>(true);
                for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++)
                {
                    SerializedObject serialized = new SerializedObject(managers[managerIndex]);
                    serialized.FindProperty("neutralMaterial").objectReferenceValue = materials[0];
                    SerializedProperty players = serialized.FindProperty("playerMaterials");
                    players.arraySize = 4;
                    for (int materialIndex = 0; materialIndex < 4; materialIndex++)
                    {
                        players.GetArrayElementAtIndex(materialIndex).objectReferenceValue =
                            materials[materialIndex + 1];
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
