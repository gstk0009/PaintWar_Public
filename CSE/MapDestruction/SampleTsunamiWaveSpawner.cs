using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SampleTsunamiWaveSpawner : MonoBehaviour
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");
    private static Material waveMaterial;
    private static Material particleMaterial;
    private static Material pathMaterial;
    private static Mesh curvedWaveMesh;

    [Header("Path")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private Vector2Int startGridIndex = new Vector2Int(-1, -1);
    [SerializeField] private Vector2Int endGridIndex = new Vector2Int(-1, -1);
    [SerializeField] private bool randomizeMapCrossingPath = true;

    [Header("Wave")]
    [SerializeField, Min(0.1f)] private float waveWidth = 3.8f;
    [SerializeField, Min(0.1f)] private float waveThickness = 1.45f;
    [SerializeField, Min(0.1f)] private float travelSpeed = 11.5f;
    [SerializeField, Min(0.1f)] private float visualHeight = 1.75f;
    [SerializeField] private Color waveColor = new Color(0.48f, 0.12f, 1f, 0.82f);
    [SerializeField] private bool allowOverlap = false;

    [Header("Multi Wave")]
    [SerializeField, Min(1)] private int waveLaneCount = 3;
    [SerializeField, Min(1f)] private float waveDirectionSpreadDegrees = 120f;
    [SerializeField, Min(0f)] private float waveLaneDelay = 0.32f;

    [Header("Path Preview")]
    [SerializeField] private bool showPathPreview = true;
    [SerializeField] private Color pathColor = new Color(0.5f, 0.1f, 1f, 0.36f);
    [SerializeField] private float pathTileFill = 0.9f;
    [SerializeField] private float pathIndicatorHeight = 0.035f;
    [SerializeField] private float pathSurfaceOffset = 0.04f;

    [Header("Impact")]
    [SerializeField] private float knockbackDistance = 2.4f;
    [SerializeField, Range(0f, 1f)] private float slowMultiplier = 0.45f;
    [SerializeField] private float slowDuration = 2.2f;

    private readonly HashSet<Vector2Int> clearedTiles = new HashSet<Vector2Int>();
    private readonly HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private readonly List<GameObject> impactTargets = new List<GameObject>(8);
    private readonly List<Coroutine> activeLaneWaves = new List<Coroutine>(3);
    private readonly List<GameObject> activeWaveVisuals = new List<GameObject>(3);
    private readonly List<GameObject> activePathPreviews = new List<GameObject>(3);
    private Coroutine activeWave;
    private int runningWaveLanes;

    private struct WavePath
    {
        public Vector3 Start;
        public Vector3 Direction;
        public Vector3 Right;
        public float Length;
    }

    public void SpawnTsunamiAcrossMap()
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            Debug.LogWarning("[TsunamiSample] Tile grid is not ready.");
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector3 start;
        Vector3 end;
        if (ShouldUseManualPath())
        {
            start = ResolveStartPosition(tileManager);
            end = ResolveEndPosition(tileManager);
        }
        else
        {
            ResolveRandomCrossingPath(tileManager, out start, out end);
        }

        if ((end - start).sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("[TsunamiSample] Start and end points are too close.");
            return;
        }

        SpawnTsunami(start, end);
    }

    public void SpawnTsunami(Vector3 start, Vector3 end)
    {
        if (!allowOverlap && activeWave != null)
        {
            return;
        }

        if (activeWave != null)
        {
            StopActiveWaveCoroutines();
        }

        activeWave = StartCoroutine(RunWaveSequence(start, end));
    }

    private IEnumerator RunWaveSequence(Vector3 start, Vector3 end)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        clearedTiles.Clear();
        hitTargets.Clear();
        CollectImpactTargets();

        Vector3 flatStart = new Vector3(start.x, 0f, start.z);
        Vector3 flatEnd = new Vector3(end.x, 0f, end.z);
        Vector3 path = flatEnd - flatStart;
        float pathLength = path.magnitude;
        Vector3 direction = path / pathLength;
        int waveCount = Mathf.Max(1, waveLaneCount);
        runningWaveLanes = 0;

        for (int i = 0; i < waveCount; i++)
        {
            WavePath wavePath = waveCount == 1
                ? CreateWavePath(flatStart, direction, pathLength)
                : CreateMapCrossingWavePath(
                    tileManager,
                    Quaternion.AngleAxis(GetDistributedWaveAngle(i), Vector3.up) * direction);
            Coroutine laneWave = StartCoroutine(RunWaveLane(
                tileManager,
                wavePath.Start,
                wavePath.Direction,
                wavePath.Right,
                wavePath.Length));
            activeLaneWaves.Add(laneWave);
            runningWaveLanes++;

            if (i < waveCount - 1 && waveLaneDelay > 0f)
            {
                yield return new WaitForSeconds(waveLaneDelay);
            }
        }

        while (runningWaveLanes > 0)
        {
            yield return null;
        }

        activeLaneWaves.Clear();
        activeWave = null;
    }

    private IEnumerator RunWaveLane(
        TilePaintManager tileManager,
        Vector3 flatStart,
        Vector3 direction,
        Vector3 right,
        float pathLength)
    {
        GameObject pathPreview = CreatePathPreview(tileManager, flatStart, direction, pathLength);
        GameObject visual = CreateWaveVisual(tileManager, flatStart, direction, right, pathLength);
        TrackActiveVisual(activePathPreviews, pathPreview);
        TrackActiveVisual(activeWaveVisuals, visual);
        float travelled = 0f;

        while (travelled < pathLength)
        {
            float previous = travelled;
            travelled = Mathf.Min(pathLength, travelled + travelSpeed * Time.deltaTime);
            UpdateWaveVisual(visual, tileManager, flatStart, direction, right, travelled);
            ClearSweptTiles(tileManager, flatStart, direction, previous, travelled, pathLength);
            AffectTargetsInWave(tileManager, flatStart, direction, previous, travelled, pathLength);
            yield return null;
        }

        ClearSweptTiles(
            tileManager,
            flatStart,
            direction,
            Mathf.Max(0f, pathLength - waveThickness),
            pathLength,
            pathLength);
        ClearPathTiles(tileManager, flatStart, direction, pathLength);
        AffectTargetsInWave(
            tileManager,
            flatStart,
            direction,
            Mathf.Max(0f, pathLength - waveThickness),
            pathLength,
            pathLength);

        DestroyLaneVisuals(visual, pathPreview);
        runningWaveLanes = Mathf.Max(0, runningWaveLanes - 1);
    }

    private WavePath CreateMapCrossingWavePath(TilePaintManager tileManager, Vector3 direction)
    {
        Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z).normalized;
        if (flatDirection.sqrMagnitude <= 0.0001f)
        {
            flatDirection = Vector3.forward;
        }

        Vector3 min = tileManager.GetTileCenterWorldPosition(Vector2Int.zero);
        Vector3 max = tileManager.GetTileCenterWorldPosition(
            new Vector2Int(tileManager.GridSize - 1, tileManager.GridSize - 1));
        Vector3 center = (min + max) * 0.5f;
        center.y = 0f;
        float halfDiagonal = Vector3.Distance(
            new Vector3(min.x, 0f, min.z),
            new Vector3(max.x, 0f, max.z)) * 0.5f + tileManager.TileSize * 1.5f;
        Vector3 start = center - flatDirection * halfDiagonal;
        return CreateWavePath(start, flatDirection, halfDiagonal * 2f);
    }

    private static WavePath CreateWavePath(Vector3 start, Vector3 direction, float length)
    {
        Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z).normalized;
        if (flatDirection.sqrMagnitude <= 0.0001f)
        {
            flatDirection = Vector3.forward;
        }

        return new WavePath
        {
            Start = start,
            Direction = flatDirection,
            Right = new Vector3(-flatDirection.z, 0f, flatDirection.x),
            Length = Mathf.Max(0.001f, length)
        };
    }

    private float GetDistributedWaveAngle(int waveIndex)
    {
        if (waveIndex == 0)
        {
            return 0f;
        }

        int pairIndex = (waveIndex + 1) / 2;
        float sign = waveIndex % 2 == 1 ? 1f : -1f;
        return sign * waveDirectionSpreadDegrees * pairIndex;
    }

    private void OnDestroy()
    {
        DestroyActiveVisuals();
    }

    private Vector3 ResolveStartPosition(TilePaintManager tileManager)
    {
        if (startPoint != null)
        {
            return startPoint.position;
        }

        if (startGridIndex.x >= 0 && startGridIndex.y >= 0)
        {
            Vector2Int index = tileManager.ClampIndex(startGridIndex);
            return tileManager.GetTileCenterWorldPosition(index);
        }

        int center = Mathf.Clamp(tileManager.GridSize / 2, 0, tileManager.GridSize - 1);
        return tileManager.GetTileCenterWorldPosition(new Vector2Int(0, center));
    }

    private Vector3 ResolveEndPosition(TilePaintManager tileManager)
    {
        if (endPoint != null)
        {
            return endPoint.position;
        }

        if (endGridIndex.x >= 0 && endGridIndex.y >= 0)
        {
            Vector2Int index = tileManager.ClampIndex(endGridIndex);
            return tileManager.GetTileCenterWorldPosition(index);
        }

        int center = Mathf.Clamp(tileManager.GridSize / 2, 0, tileManager.GridSize - 1);
        return tileManager.GetTileCenterWorldPosition(
            new Vector2Int(tileManager.GridSize - 1, center));
    }

    private bool ShouldUseManualPath()
    {
        return !randomizeMapCrossingPath ||
               startPoint != null ||
               endPoint != null ||
               IsValidSerializedIndex(startGridIndex) ||
               IsValidSerializedIndex(endGridIndex);
    }

    private static bool IsValidSerializedIndex(Vector2Int index)
    {
        return index.x >= 0 && index.y >= 0;
    }

    private static void ResolveRandomCrossingPath(
        TilePaintManager tileManager,
        out Vector3 start,
        out Vector3 end)
    {
        int max = Mathf.Max(0, tileManager.GridSize - 1);
        bool horizontal = Random.value < 0.5f;
        bool reverse = Random.value < 0.5f;
        Vector2Int startIndex;
        Vector2Int endIndex;

        if (horizontal)
        {
            startIndex = new Vector2Int(0, Random.Range(0, max + 1));
            endIndex = new Vector2Int(max, Random.Range(0, max + 1));
        }
        else
        {
            startIndex = new Vector2Int(Random.Range(0, max + 1), 0);
            endIndex = new Vector2Int(Random.Range(0, max + 1), max);
        }

        if (reverse)
        {
            Vector2Int temp = startIndex;
            startIndex = endIndex;
            endIndex = temp;
        }

        start = tileManager.GetTileCenterWorldPosition(startIndex);
        end = tileManager.GetTileCenterWorldPosition(endIndex);
    }

    private GameObject CreateWaveVisual(
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        Vector3 right,
        float pathLength)
    {
        GameObject visual = new GameObject("Sample Tsunami Curved Wave Visual");
        MeshFilter meshFilter = visual.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetCurvedWaveMesh();

        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetWaveMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetRendererColor(renderer, waveColor);
        }

        CreateWaveParticles(visual.transform);
        UpdateWaveVisual(visual, tileManager, start, direction, right, 0f);
        return visual;
    }

    private void UpdateWaveVisual(
        GameObject visual,
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        Vector3 right,
        float travelled)
    {
        if (visual == null)
        {
            return;
        }

        float front = Mathf.Max(0f, travelled);
        float depth = Mathf.Max(0.05f, Mathf.Min(waveThickness, front + waveThickness));
        Vector3 center = start + direction * Mathf.Max(0f, front - depth * 0.5f);
        Vector2Int index = tileManager.ClampIndex(tileManager.GetGridIndex(center));
        float surfaceY = tileManager.GetTileSurfaceY(index);

        visual.transform.position = new Vector3(center.x, surfaceY, center.z);
        visual.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        visual.transform.localScale = new Vector3(waveWidth, visualHeight, depth);
    }

    private GameObject CreatePathPreview(
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        float pathLength)
    {
        if (!showPathPreview)
        {
            return null;
        }

        GameObject root = new GameObject("Sample Tsunami Path Preview");
        float halfWidth = Mathf.Max(0.05f, waveWidth * 0.5f);
        float tileReach = Mathf.Max(0.001f, tileManager.TileSize * 0.7072f);
        float tileFill = Mathf.Clamp(pathTileFill, 0.05f, 1.5f);

        for (int x = 0; x < tileManager.GridSize; x++)
        {
            for (int y = 0; y < tileManager.GridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
                Vector3 offset = new Vector3(tileCenter.x - start.x, 0f, tileCenter.z - start.z);
                float along = Vector3.Dot(offset, direction);
                if (along < -tileReach || along > pathLength + tileReach)
                {
                    continue;
                }

                float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
                if (lateral > halfWidth + tileReach)
                {
                    continue;
                }

                CreatePathPreviewTile(root.transform, tileManager, index, tileFill);
            }
        }

        return root;
    }

    private void CreatePathPreviewTile(
        Transform parent,
        TilePaintManager tileManager,
        Vector2Int index,
        float tileFill)
    {
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        indicator.name = "Tsunami Path Preview Tile";
        indicator.transform.SetParent(parent, false);
        Vector3 center = tileManager.GetTileCenterWorldPosition(index);
        center.y = tileManager.GetTileSurfaceY(index) + pathSurfaceOffset;
        float tileSize = Mathf.Max(0.001f, tileManager.TileSize);
        indicator.transform.position = center;
        indicator.transform.localScale = new Vector3(
            tileSize * tileFill,
            Mathf.Max(0.001f, pathIndicatorHeight),
            tileSize * tileFill);

        Collider collider = indicator.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            DestroyRuntimeObject(collider);
        }

        Renderer renderer = indicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetPathMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetRendererColor(renderer, pathColor);
        }
    }

    private void CreateWaveParticles(Transform parent)
    {
        CreateFoamCrestRibbons(parent);

        ParticleSystem spray = CreateWaveParticleStream(
            "Purple Tsunami Foam Spray",
            parent,
            new Vector3(0f, 0.45f, 0.42f),
            90f,
            new ParticleSystem.MinMaxCurve(0.35f, 0.75f),
            new ParticleSystem.MinMaxCurve(0.08f, 0.22f),
            new ParticleSystem.MinMaxCurve(1.6f, 3.4f),
            CreateGradient(
                new Color(1f, 0.9f, 1f, 0.95f),
                new Color(0.76f, 0.32f, 1f, 0.7f),
                new Color(0.54f, 0.1f, 1f, 0f)),
            new Vector3(1.12f, 0.12f, 0.08f),
            new Vector3(0f, 0.8f, -1.35f),
            ParticleSystemRenderMode.Billboard,
            1f,
            0f,
            180);

        ParticleSystem.MainModule sprayMain = spray.main;
        sprayMain.gravityModifier = 0.08f;

        CreateWaveParticleStream(
            "Purple Tsunami Water Streaks",
            parent,
            new Vector3(0f, 0.08f, 0.18f),
            140f,
            new ParticleSystem.MinMaxCurve(0.32f, 0.68f),
            new ParticleSystem.MinMaxCurve(0.06f, 0.16f),
            new ParticleSystem.MinMaxCurve(2.2f, 4.2f),
            CreateGradient(
                new Color(0.92f, 0.78f, 1f, 0.86f),
                new Color(0.56f, 0.16f, 1f, 0.58f),
                new Color(0.42f, 0.05f, 0.95f, 0f)),
            new Vector3(1.05f, 0.08f, 0.16f),
            new Vector3(0f, 0.12f, -2.2f),
            ParticleSystemRenderMode.Stretch,
            2.25f,
            0.45f,
            220);

        CreateWaveParticleStream(
            "Purple Tsunami Crest Foam",
            parent,
            new Vector3(0f, 0.58f, 0.34f),
            55f,
            new ParticleSystem.MinMaxCurve(0.28f, 0.55f),
            new ParticleSystem.MinMaxCurve(0.1f, 0.24f),
            new ParticleSystem.MinMaxCurve(0.75f, 1.8f),
            CreateGradient(
                new Color(1f, 0.96f, 1f, 0.98f),
                new Color(0.86f, 0.48f, 1f, 0.68f),
                new Color(0.65f, 0.16f, 1f, 0f)),
            new Vector3(1.18f, 0.08f, 0.06f),
            new Vector3(0f, 0.55f, -0.65f),
            ParticleSystemRenderMode.Billboard,
            1f,
            0f,
            140);
    }

    private static void CreateFoamCrestRibbons(Transform parent)
    {
        CreateFoamRibbon(parent, "Purple Tsunami High Crest", new Vector3(0f, 0.52f, 0.44f), 0.92f, 0.035f);
        CreateFoamRibbon(parent, "Purple Tsunami Mid Crest", new Vector3(0f, 0.35f, 0.3f), 0.78f, 0.025f);
        CreateFoamRibbon(parent, "Purple Tsunami Low Surge", new Vector3(0f, 0.16f, 0.12f), 0.88f, 0.03f);
    }

    private static void CreateFoamRibbon(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        float width,
        float depth)
    {
        GameObject ribbon = new GameObject(objectName);
        ribbon.name = objectName;
        ribbon.transform.SetParent(parent, false);
        ribbon.transform.localPosition = localPosition;
        ribbon.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);

        MeshFilter meshFilter = ribbon.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = BuildFoamRibbonMesh(width, depth);

        MeshRenderer renderer = ribbon.AddComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetParticleMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetRendererColor(renderer, new Color(0.95f, 0.72f, 1f, 0.62f));
        }
    }

    private static Mesh GetCurvedWaveMesh()
    {
        if (curvedWaveMesh == null)
        {
            curvedWaveMesh = BuildCurvedWaveMesh();
            curvedWaveMesh.name = "Runtime Purple Tsunami Curved Wave Mesh";
        }

        return curvedWaveMesh;
    }

    private static Mesh BuildCurvedWaveMesh()
    {
        const int widthSegments = 14;
        const int heightSegments = 8;
        int widthVertices = widthSegments + 1;
        int heightVertices = heightSegments + 1;
        Vector3[] vertices = new Vector3[widthVertices * heightVertices];
        Vector2[] uvs = new Vector2[vertices.Length];
        List<int> triangles = new List<int>(widthSegments * heightSegments * 12);

        for (int y = 0; y < heightVertices; y++)
        {
            float t = y / (float)heightSegments;
            float rise = Mathf.Pow(t, 0.82f);
            float curl = Mathf.Max(0f, t - 0.58f);
            float localZ = -0.48f + 0.7f * t + curl * curl * 1.15f;

            for (int x = 0; x < widthVertices; x++)
            {
                float u = x / (float)widthSegments;
                float xCentered = u - 0.5f;
                float edgeFalloff = Mathf.Sin(u * Mathf.PI);
                float crestRipple = Mathf.Sin((u * Mathf.PI * 4f) + t * Mathf.PI) * 0.035f * t;
                float sideDip = Mathf.Abs(xCentered) * 0.08f * t;
                vertices[y * widthVertices + x] = new Vector3(
                    xCentered,
                    Mathf.Clamp01(rise + crestRipple - sideDip),
                    localZ + Mathf.Sin(u * Mathf.PI * 3f) * 0.025f * edgeFalloff * t);
                uvs[y * widthVertices + x] = new Vector2(u, t);
            }
        }

        for (int y = 0; y < heightSegments; y++)
        {
            for (int x = 0; x < widthSegments; x++)
            {
                int a = y * widthVertices + x;
                int b = a + 1;
                int c = a + widthVertices;
                int d = c + 1;

                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(d);
                triangles.Add(c);
            }
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildFoamRibbonMesh(float width, float depth)
    {
        const int segments = 16;
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 12];
        float halfDepth = Mathf.Max(0.001f, depth) * 0.5f;

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float x = (u - 0.5f) * width;
            float wave = Mathf.Sin(u * Mathf.PI * 4f) * 0.025f;
            int index = i * 2;
            vertices[index] = new Vector3(x, wave, -halfDepth);
            vertices[index + 1] = new Vector3(x, -wave, halfDepth);
            uvs[index] = new Vector2(u, 0f);
            uvs[index + 1] = new Vector2(u, 1f);
        }

        for (int i = 0; i < segments; i++)
        {
            int vertex = i * 2;
            int triangle = i * 12;
            int a = vertex;
            int b = vertex + 1;
            int c = vertex + 2;
            int d = vertex + 3;

            triangles[triangle] = a;
            triangles[triangle + 1] = b;
            triangles[triangle + 2] = c;
            triangles[triangle + 3] = c;
            triangles[triangle + 4] = b;
            triangles[triangle + 5] = d;

            triangles[triangle + 6] = a;
            triangles[triangle + 7] = c;
            triangles[triangle + 8] = b;
            triangles[triangle + 9] = c;
            triangles[triangle + 10] = d;
            triangles[triangle + 11] = b;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Runtime Purple Tsunami Foam Ribbon Mesh";
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static ParticleSystem CreateWaveParticleStream(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        float emissionRate,
        ParticleSystem.MinMaxCurve lifetime,
        ParticleSystem.MinMaxCurve size,
        ParticleSystem.MinMaxCurve speed,
        Gradient color,
        Vector3 shapeScale,
        Vector3 localVelocity,
        ParticleSystemRenderMode renderMode,
        float lengthScale,
        float velocityScale,
        int maxParticles)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;

        ParticleSystem particles = obj.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = lifetime;
        main.startSize = size;
        main.startSpeed = speed;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = emissionRate;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = shapeScale;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = localVelocity.x;
        velocity.y = localVelocity.y;
        velocity.z = localVelocity.z;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = color;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.18f, 1f),
                new Keyframe(1f, 0.08f)));

        ParticleSystemRenderer renderer = obj.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = renderMode;
        renderer.sharedMaterial = GetParticleMaterial();
        renderer.lengthScale = lengthScale;
        renderer.velocityScale = velocityScale;
        renderer.sortingFudge = 1.2f;

        particles.Play(true);
        return particles;
    }

    private void ClearSweptTiles(
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        float previousDistance,
        float currentDistance,
        float pathLength)
    {
        float halfWidth = Mathf.Max(0.05f, waveWidth * 0.5f);
        float tileReach = Mathf.Max(0.001f, tileManager.TileSize * 0.7072f);
        float clearFrom = Mathf.Max(0f, previousDistance - waveThickness);
        float clearTo = Mathf.Min(pathLength, currentDistance + waveThickness);

        for (int x = 0; x < tileManager.GridSize; x++)
        {
            for (int y = 0; y < tileManager.GridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (clearedTiles.Contains(index) || tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
                Vector3 offset = new Vector3(tileCenter.x - start.x, 0f, tileCenter.z - start.z);
                float along = Vector3.Dot(offset, direction);
                if (along < clearFrom - tileReach || along > clearTo + tileReach)
                {
                    continue;
                }

                float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
                if (lateral > halfWidth + tileReach)
                {
                    continue;
                }

                ClearTile(tileManager, index);
                clearedTiles.Add(index);
            }
        }
    }

    private void AffectTargetsInWave(
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        float previousDistance,
        float currentDistance,
        float pathLength)
    {
        float halfWidth = Mathf.Max(0.05f, waveWidth * 0.5f);
        float targetReach = Mathf.Max(0.35f, tileManager.TileSize * 0.55f);
        float hitFrom = Mathf.Max(0f, previousDistance - waveThickness);
        float hitTo = Mathf.Min(pathLength, currentDistance + waveThickness);

        for (int i = 0; i < impactTargets.Count; i++)
        {
            GameObject target = impactTargets[i];
            if (target == null || hitTargets.Contains(target))
            {
                continue;
            }

            Vector3 position = target.transform.position;
            Vector3 offset = new Vector3(position.x - start.x, 0f, position.z - start.z);
            float along = Vector3.Dot(offset, direction);
            if (along < hitFrom - targetReach || along > hitTo + targetReach)
            {
                continue;
            }

            float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
            if (lateral > halfWidth + targetReach)
            {
                continue;
            }

            HitTargetWithWave(tileManager, target, direction);
            hitTargets.Add(target);
        }
    }

    private void HitTargetWithWave(
        TilePaintManager tileManager,
        GameObject target,
        Vector3 direction)
    {
        if (target == null)
        {
            return;
        }

        Vector3 desired = target.transform.position + direction.normalized * Mathf.Max(0f, knockbackDistance);
        Vector2Int safeIndex;
        if (!TryFindNearestStandableTile(tileManager, desired, out safeIndex))
        {
            safeIndex = tileManager.ClampIndex(tileManager.GetGridIndex(target.transform.position));
        }

        if (tileManager.IsInsideGrid(safeIndex) && !tileManager.IsTileDestroyed(safeIndex))
        {
            Vector3 safeCenter = tileManager.GetTileCenterWorldPosition(safeIndex);
            Vector3 safePosition = new Vector3(safeCenter.x, target.transform.position.y, safeCenter.z);
            MoveParticipantToSafeTile(target, safePosition);
        }

        ApplySlow(target);
    }

    private static bool TryFindNearestStandableTile(
        TilePaintManager tileManager,
        Vector3 desiredPosition,
        out Vector2Int safeIndex)
    {
        safeIndex = Vector2Int.zero;
        if (tileManager == null || !tileManager.IsGridReady)
        {
            return false;
        }

        Vector2Int desiredIndex = tileManager.ClampIndex(tileManager.GetGridIndex(desiredPosition));
        float bestDistanceSqr = float.MaxValue;
        Vector2 desired2D = new Vector2(desiredPosition.x, desiredPosition.z);
        int searchRadius = Mathf.CeilToInt(3f / Mathf.Max(0.001f, tileManager.TileSize)) + 2;

        for (int x = desiredIndex.x - searchRadius; x <= desiredIndex.x + searchRadius; x++)
        {
            for (int y = desiredIndex.y - searchRadius; y <= desiredIndex.y + searchRadius; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                Vector3 center = tileManager.GetTileCenterWorldPosition(index);
                Vector2 center2D = new Vector2(center.x, center.z);
                float distanceSqr = (center2D - desired2D).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                safeIndex = index;
            }
        }

        return bestDistanceSqr < float.MaxValue;
    }

    private static void MoveParticipantToSafeTile(GameObject participant, Vector3 safePosition)
    {
        if (participant == null)
        {
            return;
        }

        PlayerMovement playerMovement = participant.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.ApplyExternalTileDisplacement(safePosition);
        }
        else
        {
            ApplyTransformDisplacement(participant, safePosition);
        }

        NetworkBotController networkBot = participant.GetComponent<NetworkBotController>();
        if (networkBot != null)
        {
            networkBot.ResetAfterExternalTileDisplacement();
        }

        BotPlayerController localBot = participant.GetComponent<BotPlayerController>();
        if (localBot != null)
        {
            localBot.ResetAfterExternalTileDisplacement();
        }
    }

    private static void ApplyTransformDisplacement(GameObject participant, Vector3 safePosition)
    {
        CharacterController controller = participant.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }

        participant.transform.position = safePosition;

        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }

        FishNet.Component.Transforming.NetworkTransform networkTransform =
            participant.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
        if (networkTransform != null)
        {
            networkTransform.Teleport();
            networkTransform.ForceSend();
        }
    }

    private void ApplySlow(GameObject participant)
    {
        if (participant == null || slowDuration <= 0f)
        {
            return;
        }

        PlayerStatusEffectController statusEffects =
            participant.GetComponent<PlayerStatusEffectController>();
        if (statusEffects == null)
        {
            statusEffects = participant.AddComponent<PlayerStatusEffectController>();
        }

        statusEffects.ApplyStatusEffect(new StatusEffectSpec(
            StatusEffectType.Slow,
            slowMultiplier,
            slowDuration,
            StatusEffectStackMode.RefreshDuration));
    }

    private void CollectImpactTargets()
    {
        impactTargets.Clear();

        if (GameRoundManager.HasInstance)
        {
            System.Collections.Generic.IReadOnlyList<GameParticipant> participants =
                GameRoundManager.Instance.Participants;
            for (int i = 0; i < participants.Count; i++)
            {
                GameParticipant participant = participants[i];
                if (participant == null ||
                    participant.IsEliminated ||
                    participant.Transform == null)
                {
                    continue;
                }

                AddImpactTarget(participant.Transform.gameObject);
            }
        }

        PlayerPaintIdentity[] identities = FindObjectsByType<PlayerPaintIdentity>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < identities.Length; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity == null || identity.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            AddImpactTarget(identity.gameObject);
        }
    }

    private void AddImpactTarget(GameObject target)
    {
        if (target == null || impactTargets.Contains(target))
        {
            return;
        }

        impactTargets.Add(target);
    }

    private void ClearPathTiles(
        TilePaintManager tileManager,
        Vector3 start,
        Vector3 direction,
        float pathLength)
    {
        float halfWidth = Mathf.Max(0.05f, waveWidth * 0.5f);
        float tileReach = Mathf.Max(0.001f, tileManager.TileSize * 0.7072f);

        for (int x = 0; x < tileManager.GridSize; x++)
        {
            for (int y = 0; y < tileManager.GridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                if (tileManager.IsTileDestroyed(index))
                {
                    continue;
                }

                Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(index);
                Vector3 offset = new Vector3(tileCenter.x - start.x, 0f, tileCenter.z - start.z);
                float along = Vector3.Dot(offset, direction);
                if (along < -tileReach || along > pathLength + tileReach)
                {
                    continue;
                }

                float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
                if (lateral > halfWidth + tileReach)
                {
                    continue;
                }

                ClearTile(tileManager, index);
            }
        }
    }

    private static void ClearTile(TilePaintManager tileManager, Vector2Int index)
    {
        if (!tileManager.TryGetTileOwner(index, out PaintOwner owner))
        {
            return;
        }

        bool networkRequestSent = false;
        if (owner != PaintOwner.Neutral && NetworkTilePaintAuthority.HasInstance)
        {
            networkRequestSent =
                NetworkTilePaintAuthority.Instance.RequestForceSetOwner(index, PaintOwner.Neutral);
        }

        if (!networkRequestSent ||
            tileManager.TryGetTileOwner(index, out PaintOwner currentOwner) &&
            currentOwner != PaintOwner.Neutral)
        {
            tileManager.ForceSetTileOwnerLocal(index, PaintOwner.Neutral);
        }
    }

    private void DestroyActiveVisuals()
    {
        for (int i = activeWaveVisuals.Count - 1; i >= 0; i--)
        {
            DestroyRuntimeObject(activeWaveVisuals[i]);
        }

        for (int i = activePathPreviews.Count - 1; i >= 0; i--)
        {
            DestroyRuntimeObject(activePathPreviews[i]);
        }

        activeWaveVisuals.Clear();
        activePathPreviews.Clear();
    }

    private void DestroyLaneVisuals(GameObject visual, GameObject pathPreview)
    {
        activeWaveVisuals.Remove(visual);
        activePathPreviews.Remove(pathPreview);
        DestroyRuntimeObject(visual);
        DestroyRuntimeObject(pathPreview);
    }

    private void StopActiveWaveCoroutines()
    {
        if (activeWave != null)
        {
            StopCoroutine(activeWave);
            activeWave = null;
        }

        for (int i = 0; i < activeLaneWaves.Count; i++)
        {
            if (activeLaneWaves[i] != null)
            {
                StopCoroutine(activeLaneWaves[i]);
            }
        }

        activeLaneWaves.Clear();
        runningWaveLanes = 0;
        DestroyActiveVisuals();
    }

    private static void TrackActiveVisual(List<GameObject> activeVisuals, GameObject visual)
    {
        if (visual != null)
        {
            activeVisuals.Add(visual);
        }
    }

    private static void SetRendererColor(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(properties);
        properties.SetColor(BaseColorProperty, color);
        properties.SetColor(ColorProperty, color);
        properties.SetColor(EmissionColorProperty, color * 0.45f);
        targetRenderer.SetPropertyBlock(properties);
    }

    private static Gradient CreateGradient(Color start, Color middle, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(middle, 0.45f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(middle.a, 0.45f),
                new GradientAlphaKey(end.a, 1f)
            });
        return gradient;
    }

    private static Material GetWaveMaterial()
    {
        if (waveMaterial == null)
        {
            waveMaterial = CreateTransparentMaterial(
                "Runtime Purple Tsunami Wave Material",
                new Color(0.48f, 0.12f, 1f, 0.34f));
        }

        return waveMaterial;
    }

    private static Material GetPathMaterial()
    {
        if (pathMaterial == null)
        {
            pathMaterial = CreateTransparentMaterial(
                "Runtime Purple Tsunami Path Material",
                new Color(0.5f, 0.1f, 1f, 0.36f));
        }

        return pathMaterial;
    }

    private static Material GetParticleMaterial()
    {
        if (particleMaterial == null)
        {
            particleMaterial = CreateTransparentMaterial(
                "Runtime Purple Tsunami Particle Material",
                new Color(0.76f, 0.35f, 1f, 0.82f));
        }

        return particleMaterial;
    }

    private static Material CreateTransparentMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        material.renderQueue = 3000;
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
