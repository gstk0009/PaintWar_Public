#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ThemedSkillMarkerPrefabSetup
{
    private const string RootFolder = "Assets/LGH/Prefabs/VFX/SkillMarker";
    private const string PrefabPath =
        RootFolder + "/ThemedSkillMarker3D.prefab";
    private const string CrystalMeshPath =
        RootFolder + "/SkillMarkerCrystal.asset";
    private const string RingMeshPath =
        RootFolder + "/SkillMarkerRing.asset";
    private const string CrystalMaterialPath =
        RootFolder + "/SkillMarkerCrystal.mat";
    private const string RingMaterialPath =
        RootFolder + "/SkillMarkerRing.mat";
    private const string ProfileFolder =
        "Assets/LGH/Data/WeaponVfxProfiles";

    [MenuItem("Tools/PaintWar/Weapons/Create Or Update Themed 3D Skill Marker")]
    public static void CreateOrUpdate()
    {
        EnsureFolders();

        Mesh crystalMesh = CreateOrUpdateCrystalMesh();
        Mesh ringMesh = CreateOrUpdateRingMesh();
        Material crystalMaterial = CreateOrUpdateMaterial(
            CrystalMaterialPath,
            new Color(0.68f, 0.9f, 1f, 1f));
        Material ringMaterial = CreateOrUpdateMaterial(
            RingMaterialPath,
            new Color(0.42f, 0.78f, 1f, 1f));

        GameObject root = new GameObject("ThemedSkillMarker3D");
        try
        {
            Transform core = CreateMeshPart(
                "CrystalCore",
                root.transform,
                crystalMesh,
                crystalMaterial,
                new Vector3(0f, 1.2f, 0f),
                Quaternion.identity,
                new Vector3(0.42f, 0.68f, 0.42f));

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "CoreGlow";
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            glow.transform.localScale = Vector3.one * 0.31f;
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            ConfigureRenderer(glow.GetComponent<MeshRenderer>(), crystalMaterial);

            CreateMeshPart(
                "RuneBaseRing",
                root.transform,
                ringMesh,
                ringMaterial,
                new Vector3(0f, -0.03f, 0f),
                Quaternion.identity,
                new Vector3(1.03f, 1f, 1.03f));
            CreateMeshPart(
                "OrbitA",
                root.transform,
                ringMesh,
                ringMaterial,
                new Vector3(0f, 0.02f, 0f),
                Quaternion.identity,
                Vector3.one * 0.82f);
            CreateMeshPart(
                "OrbitB",
                root.transform,
                ringMesh,
                ringMaterial,
                new Vector3(0f, 0.07f, 0f),
                Quaternion.identity,
                Vector3.one * 0.62f);

            CreateShard(root.transform, crystalMesh, crystalMaterial,
                new Vector3(0.73f, 0.98f, 0f), Quaternion.Euler(0f, 0f, -68f));
            CreateShard(root.transform, crystalMesh, crystalMaterial,
                new Vector3(-0.73f, 0.98f, 0f), Quaternion.Euler(0f, 0f, 68f));
            CreateShard(root.transform, crystalMesh, crystalMaterial,
                new Vector3(0f, 0.98f, 0.73f), Quaternion.Euler(68f, 0f, 0f));
            CreateShard(root.transform, crystalMesh, crystalMaterial,
                new Vector3(0f, 0.98f, -0.73f), Quaternion.Euler(-68f, 0f, 0f));

            Light markerLight = new GameObject("MarkerLight").AddComponent<Light>();
            markerLight.transform.SetParent(root.transform, false);
            markerLight.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            markerLight.type = LightType.Point;
            markerLight.color = new Color(0.55f, 0.82f, 1f);
            markerLight.intensity = 1.15f;
            markerLight.range = 1.8f;
            markerLight.shadows = LightShadows.None;
            markerLight.renderMode = LightRenderMode.ForcePixel;

            SkillMarkerOrbitAnimator animator =
                root.AddComponent<SkillMarkerOrbitAnimator>();
            SerializedObject serializedAnimator = new SerializedObject(animator);
            serializedAnimator.FindProperty("core").objectReferenceValue = core;
            serializedAnimator.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[SkillMarker3DSetup] Failed to save marker prefab.");
                return;
            }

            AssignToVfxProfiles(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[SkillMarker3DSetup] Themed pooled 3D marker created and assigned.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateShard(
        Transform parent,
        Mesh mesh,
        Material material,
        Vector3 position,
        Quaternion rotation)
    {
        Transform shard = CreateMeshPart(
            "OrbitShard",
            parent,
            mesh,
            material,
            position,
            rotation,
            new Vector3(0.13f, 0.28f, 0.13f));
        shard.gameObject.isStatic = false;
    }

    private static Transform CreateMeshPart(
        string name,
        Transform parent,
        Mesh mesh,
        Material material,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        GameObject part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        ConfigureRenderer(part.GetComponent<MeshRenderer>(), material);
        return part.transform;
    }

    private static void ConfigureRenderer(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static Material CreateOrUpdateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh CreateOrUpdateCrystalMesh()
    {
        Mesh mesh = LoadOrCreateMesh(CrystalMeshPath, "SkillMarkerCrystal");
        Vector3 top = new Vector3(0f, 1f, 0f);
        Vector3 bottom = new Vector3(0f, -1f, 0f);
        Vector3 east = new Vector3(1f, 0f, 0f);
        Vector3 north = new Vector3(0f, 0f, 1f);
        Vector3 west = new Vector3(-1f, 0f, 0f);
        Vector3 south = new Vector3(0f, 0f, -1f);
        Vector3[] vertices =
        {
            top, north, east,
            top, west, north,
            top, south, west,
            top, east, south,
            bottom, east, north,
            bottom, north, west,
            bottom, west, south,
            bottom, south, east
        };
        int[] triangles = new int[vertices.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            triangles[i] = i;
        }

        ApplyMesh(mesh, vertices, triangles);
        return mesh;
    }

    private static Mesh CreateOrUpdateRingMesh()
    {
        const int ringSegments = 32;
        const int tubeSegments = 6;
        const float radius = 0.82f;
        const float thickness = 0.055f;
        List<Vector3> vertices =
            new List<Vector3>(ringSegments * tubeSegments);
        List<int> triangles =
            new List<int>(ringSegments * tubeSegments * 6);

        for (int ring = 0; ring < ringSegments; ring++)
        {
            float ringAngle = Mathf.PI * 2f * ring / ringSegments;
            Vector3 outward = new Vector3(
                Mathf.Cos(ringAngle), 0f, Mathf.Sin(ringAngle));
            Vector3 center = outward * radius;
            for (int tube = 0; tube < tubeSegments; tube++)
            {
                float tubeAngle = Mathf.PI * 2f * tube / tubeSegments;
                vertices.Add(
                    center +
                    outward * (Mathf.Cos(tubeAngle) * thickness) +
                    Vector3.up * (Mathf.Sin(tubeAngle) * thickness));
            }
        }

        for (int ring = 0; ring < ringSegments; ring++)
        {
            int nextRing = (ring + 1) % ringSegments;
            for (int tube = 0; tube < tubeSegments; tube++)
            {
                int nextTube = (tube + 1) % tubeSegments;
                int a = ring * tubeSegments + tube;
                int b = nextRing * tubeSegments + tube;
                int c = nextRing * tubeSegments + nextTube;
                int d = ring * tubeSegments + nextTube;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        Mesh mesh = LoadOrCreateMesh(RingMeshPath, "SkillMarkerRing");
        ApplyMesh(mesh, vertices.ToArray(), triangles.ToArray());
        return mesh;
    }

    private static Mesh LoadOrCreateMesh(string path, string name)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null)
        {
            return mesh;
        }

        mesh = new Mesh { name = name };
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void ApplyMesh(Mesh mesh, Vector3[] vertices, int[] triangles)
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
    }

    private static void AssignToVfxProfiles(GameObject prefab)
    {
        string[] guids =
            AssetDatabase.FindAssets("t:MagicWeaponVfxProfile", new[] { ProfileFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            MagicWeaponVfxProfile profile =
                AssetDatabase.LoadAssetAtPath<MagicWeaponVfxProfile>(path);
            if (profile == null)
            {
                continue;
            }

            SerializedObject serialized = new SerializedObject(profile);
            serialized.FindProperty("castMarkerPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("castMarkerScale").floatValue = 0.58f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets/LGH/Prefabs", "VFX");
        EnsureFolder("Assets/LGH/Prefabs/VFX", "SkillMarker");
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
