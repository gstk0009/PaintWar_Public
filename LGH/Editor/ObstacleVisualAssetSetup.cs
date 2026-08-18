using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ObstacleVisualAssetSetup
{
    private const string MeshPath =
        "Assets/LGH/Prefabs/Obstacles/UnbreakableObstacleBeveled.asset";
    private const string PrefabPath =
        "Assets/LGH/Prefabs/Obstacles/UnbreakableObstacleCube.prefab";
    private const string BlackHoleMeshPath =
        "Assets/LGH/Prefabs/Obstacles/FinalBlackHoleTorus.asset";
    private const string BlackHoleCoreMaterialPath =
        "Assets/LGH/Prefabs/Obstacles/FinalBlackHoleCore.mat";
    private const string BlackHoleRingMaterialPath =
        "Assets/LGH/Prefabs/Obstacles/FinalBlackHoleRing.mat";
    private const string BlackHolePrefabPath =
        "Assets/LGH/Prefabs/Obstacles/FinalBlackHole.prefab";

    [MenuItem("Tools/PaintWar/Setup Obstacle Visual Assets")]
    public static void Setup()
    {
        Mesh generated = BuildBeveledCube(0.07f);
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(generated, MeshPath);
            saved = generated;
        }
        else
        {
            EditorUtility.CopySerialized(generated, saved);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(saved);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        MeshFilter meshFilter = root.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            PrefabUtility.UnloadPrefabContents(root);
            throw new MissingComponentException(
                "Unbreakable obstacle prefab requires a MeshFilter.");
        }

        meshFilter.sharedMesh = saved;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        SetupBlackHole();
        AssetDatabase.SaveAssets();
    }

    private static void SetupBlackHole()
    {
        Mesh torus = SaveOrReplaceMesh(
            BuildTorus("FinalBlackHoleTorus", 0.72f, 0.15f, 48, 8),
            BlackHoleMeshPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            throw new System.InvalidOperationException("URP Unlit shader was not found.");
        }

        Material coreMaterial = LoadOrCreateMaterial(BlackHoleCoreMaterialPath, shader);
        coreMaterial.SetColor("_BaseColor", new Color(0.001f, 0f, 0.004f, 1f));
        coreMaterial.enableInstancing = true;
        EditorUtility.SetDirty(coreMaterial);

        Material ringMaterial = LoadOrCreateMaterial(BlackHoleRingMaterialPath, shader);
        ringMaterial.SetColor("_BaseColor", new Color(0.5f, 0.025f, 1f, 1f));
        ringMaterial.DisableKeyword("_EMISSION");
        ringMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        ringMaterial.enableInstancing = true;
        EditorUtility.SetDirty(ringMaterial);

        GameObject root = new GameObject("FinalBlackHole");
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Core";
        core.transform.SetParent(root.transform, false);
        core.transform.localScale = Vector3.one * 0.82f;
        Object.DestroyImmediate(core.GetComponent<Collider>());
        MeshRenderer coreRenderer = core.GetComponent<MeshRenderer>();
        coreRenderer.sharedMaterial = coreMaterial;
        coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coreRenderer.receiveShadows = false;

        Transform innerRing = CreateBlackHoleRing(
            root.transform,
            "InnerRing",
            torus,
            ringMaterial,
            Vector3.one,
            new Vector3(0f, 0f, 0f));
        Transform outerRing = CreateBlackHoleRing(
            root.transform,
            "OuterRing",
            torus,
            ringMaterial,
            new Vector3(1.42f, 0.82f, 1.42f),
            new Vector3(17f, 0f, 11f));

        FinalBlackHoleVfx vfx = root.AddComponent<FinalBlackHoleVfx>();
        SerializedObject serializedVfx = new SerializedObject(vfx);
        serializedVfx.FindProperty("innerRing").objectReferenceValue = innerRing;
        serializedVfx.FindProperty("outerRing").objectReferenceValue = outerRing;
        serializedVfx.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, BlackHolePrefabPath);
        Object.DestroyImmediate(root);
    }

    private static Transform CreateBlackHoleRing(
        Transform parent,
        string name,
        Mesh mesh,
        Material material,
        Vector3 scale,
        Vector3 rotation)
    {
        GameObject ring = new GameObject(name);
        ring.transform.SetParent(parent, false);
        ring.transform.localScale = scale;
        ring.transform.localEulerAngles = rotation;
        ring.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ring.transform;
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Mesh SaveOrReplaceMesh(Mesh generated, string path)
    {
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        EditorUtility.CopySerialized(generated, saved);
        Object.DestroyImmediate(generated);
        EditorUtility.SetDirty(saved);
        return saved;
    }

    private static Mesh BuildTorus(
        string name,
        float majorRadius,
        float tubeRadius,
        int majorSegments,
        int tubeSegments)
    {
        Vector3[] vertices = new Vector3[majorSegments * tubeSegments];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[majorSegments * tubeSegments * 6];
        for (int major = 0; major < majorSegments; major++)
        {
            float u = major / (float)majorSegments;
            float majorAngle = u * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(majorAngle), 0f, Mathf.Sin(majorAngle));
            Vector3 center = radial * majorRadius;
            for (int tube = 0; tube < tubeSegments; tube++)
            {
                float v = tube / (float)tubeSegments;
                float tubeAngle = v * Mathf.PI * 2f;
                Vector3 normal = radial * Mathf.Cos(tubeAngle) + Vector3.up * Mathf.Sin(tubeAngle);
                int index = major * tubeSegments + tube;
                vertices[index] = center + normal * tubeRadius;
                normals[index] = normal;
                uvs[index] = new Vector2(u, v);

                int nextMajor = ((major + 1) % majorSegments) * tubeSegments;
                int nextTube = (tube + 1) % tubeSegments;
                int triangle = index * 6;
                triangles[triangle] = index;
                triangles[triangle + 1] = nextMajor + nextTube;
                triangles[triangle + 2] = nextMajor + tube;
                triangles[triangle + 3] = index;
                triangles[triangle + 4] = major * tubeSegments + nextTube;
                triangles[triangle + 5] = nextMajor + nextTube;
            }
        }

        Mesh mesh = new Mesh { name = name };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildBeveledCube(float bevel)
    {
        const float half = 0.5f;
        float inner = half - Mathf.Clamp(bevel, 0.001f, 0.24f);
        List<Vector3> vertices = new List<Vector3>(96);
        List<Vector3> normals = new List<Vector3>(96);
        List<Vector2> uvs = new List<Vector2>(96);
        List<int> triangles = new List<int>(132);

        for (int sign = -1; sign <= 1; sign += 2)
        {
            float s = sign;
            AddFace(vertices, normals, uvs, triangles, Vector3.right * s,
                new Vector3(s * half, -inner, -inner),
                new Vector3(s * half, inner, -inner),
                new Vector3(s * half, inner, inner),
                new Vector3(s * half, -inner, inner));
            AddFace(vertices, normals, uvs, triangles, Vector3.up * s,
                new Vector3(-inner, s * half, -inner),
                new Vector3(-inner, s * half, inner),
                new Vector3(inner, s * half, inner),
                new Vector3(inner, s * half, -inner));
            AddFace(vertices, normals, uvs, triangles, Vector3.forward * s,
                new Vector3(-inner, -inner, s * half),
                new Vector3(inner, -inner, s * half),
                new Vector3(inner, inner, s * half),
                new Vector3(-inner, inner, s * half));
        }

        for (int a = -1; a <= 1; a += 2)
        {
            for (int b = -1; b <= 1; b += 2)
            {
                float sa = a;
                float sb = b;
                AddFace(vertices, normals, uvs, triangles,
                    new Vector3(sa, sb, 0f).normalized,
                    new Vector3(sa * half, sb * inner, -inner),
                    new Vector3(sa * inner, sb * half, -inner),
                    new Vector3(sa * inner, sb * half, inner),
                    new Vector3(sa * half, sb * inner, inner));
                AddFace(vertices, normals, uvs, triangles,
                    new Vector3(sa, 0f, sb).normalized,
                    new Vector3(sa * half, -inner, sb * inner),
                    new Vector3(sa * inner, -inner, sb * half),
                    new Vector3(sa * inner, inner, sb * half),
                    new Vector3(sa * half, inner, sb * inner));
                AddFace(vertices, normals, uvs, triangles,
                    new Vector3(0f, sa, sb).normalized,
                    new Vector3(-inner, sa * half, sb * inner),
                    new Vector3(-inner, sa * inner, sb * half),
                    new Vector3(inner, sa * inner, sb * half),
                    new Vector3(inner, sa * half, sb * inner));
            }
        }

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    float sx = x;
                    float sy = y;
                    float sz = z;
                    AddFace(vertices, normals, uvs, triangles,
                        new Vector3(sx, sy, sz).normalized,
                        new Vector3(sx * half, sy * inner, sz * inner),
                        new Vector3(sx * inner, sy * half, sz * inner),
                        new Vector3(sx * inner, sy * inner, sz * half));
                }
            }
        }

        Mesh mesh = new Mesh { name = "UnbreakableObstacleBeveled" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddFace(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 normal,
        params Vector3[] points)
    {
        if (Vector3.Dot(
                Vector3.Cross(points[1] - points[0], points[2] - points[0]),
                normal) < 0f)
        {
            System.Array.Reverse(points);
        }

        int start = vertices.Count;
        for (int i = 0; i < points.Length; i++)
        {
            vertices.Add(points[i]);
            normals.Add(normal);
            uvs.Add(points.Length == 3
                ? (i == 0 ? Vector2.zero : i == 1 ? Vector2.right : new Vector2(0.5f, 1f))
                : (i == 0 ? Vector2.zero : i == 1 ? Vector2.up :
                   i == 2 ? Vector2.one : Vector2.right));
        }

        for (int i = 1; i < points.Length - 1; i++)
        {
            triangles.Add(start);
            triangles.Add(start + i);
            triangles.Add(start + i + 1);
        }
    }
}
