using UnityEngine;

public class TileGenerator : MonoBehaviour
{
    public static TileGenerator Instance { get; private set; }

    private const string GridLineRootName = "Tile Grid Lines";

    public GameObject tilePrefab;
    public int gridSize = 20;
    public float tileSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("Grid Lines")]
    [SerializeField] private bool showGridLines;
    [SerializeField] private Color gridLineColor = new Color(0.72f, 0.74f, 0.76f, 0.24f);
    [SerializeField] private float gridLineThickness = 0.012f;
    [SerializeField] private float gridLineYOffset = 0.03f;

    [Header("Generation Performance")]
    [SerializeField, Min(16)] private int tilesCreatedPerFrame = 128;

    private bool generated;
    private Material gridLineMaterial;
    private Mesh verticalGridLineMesh;
    private Mesh horizontalGridLineMesh;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TileGenerator] Duplicate TileGenerator found. Disabling duplicate.");
            enabled = false;
            return;
        }

        Instance = this;
    }

    private System.Collections.IEnumerator Start()
    {
        while (TilePaintManager.Instance == null)
        {
            yield return null;
        }

        yield return GenerateAndRegister();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private System.Collections.IEnumerator GenerateAndRegister()
    {
        if (generated)
        {
            yield break;
        }

        if (tilePrefab == null)
        {
            Debug.LogError("[TileGenerator] Tile prefab is missing. Tile grid cannot be generated.");
            yield break;
        }

        if (TilePaintManager.Instance == null)
        {
            Debug.LogError("[TileGenerator] TilePaintManager is missing. Tile grid cannot be generated.");
            yield break;
        }

        generated = true;
        TilePaintManager.Instance.PrepareGrid(gridSize, tileSize, gridOrigin);
        if (Application.isBatchMode)
        {
            CreateServerLogicalFloor();
            Debug.Log("[TileGenerator] Prepared data-only server grid. size=" + gridSize + ".");
            yield break;
        }

        GameObject generatedRoot = new GameObject("Generated Tiles");
        generatedRoot.transform.SetParent(transform, false);
        generatedRoot.SetActive(false);
        int frameBudget = Mathf.Max(16, tilesCreatedPerFrame);
        int createdThisFrame = 0;
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                Vector3 spawnPos = gridOrigin + new Vector3(x * tileSize, 0, z * tileSize);
                GameObject tile = Instantiate(tilePrefab, spawnPos, Quaternion.identity, generatedRoot.transform);

                Renderer targetRenderer = tile.GetComponent<Renderer>();
                if (targetRenderer != null)
                {
                    // Ground tiles receive character/obstacle shadows but must not
                    // render themselves into the directional-light shadow map.
                    targetRenderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    targetRenderer.receiveShadows = true;
                }

                TilePaintManager.Instance.RegisterTileRenderer(x, z, targetRenderer);
                createdThisFrame++;
                if (createdThisFrame >= frameBudget)
                {
                    createdThisFrame = 0;
                    yield return null;
                }
            }
        }

        generatedRoot.SetActive(true);

        GenerateGridLines();

        Debug.Log(
            "[TileGenerator] Generated tile grid. size=" + gridSize +
            " tileSize=" + tileSize +
            " origin=" + gridOrigin + ".");
    }

    private void CreateServerLogicalFloor()
    {
        GameObject floor = new GameObject("Server Logical Floor");
        floor.transform.SetParent(transform, true);
        float fullSize = Mathf.Max(tileSize, gridSize * tileSize);
        float centerOffset = (gridSize - 1) * tileSize * 0.5f;
        floor.transform.position = gridOrigin +
            new Vector3(centerOffset, 0f, centerOffset);
        if (tilePrefab != null)
        {
            floor.layer = tilePrefab.layer;
            floor.tag = tilePrefab.tag;
        }

        BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(fullSize, tileSize, fullSize);
    }

    private void GenerateGridLines()
    {
        if (!showGridLines || Application.isBatchMode || gridSize <= 0 || tileSize <= 0f)
        {
            return;
        }

        Transform existing = transform.Find(GridLineRootName);
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        GameObject root = new GameObject(GridLineRootName);
        root.transform.SetParent(transform, false);

        float fullSize = gridSize * tileSize;
        float halfTile = tileSize * 0.5f;
        float firstBoundaryX = gridOrigin.x - halfTile;
        float firstBoundaryZ = gridOrigin.z - halfTile;
        float centerX = firstBoundaryX + fullSize * 0.5f;
        float centerZ = firstBoundaryZ + fullSize * 0.5f;
        float lineY = gridOrigin.y + halfTile + gridLineYOffset;
        float thickness = Mathf.Max(0.001f, gridLineThickness);
        float length = fullSize + thickness;

        Material material = GetGridLineMaterial();
        GameObject lines = new GameObject("Combined Grid Lines");
        lines.layer = gameObject.layer;
        lines.transform.SetParent(root.transform, false);
        lines.transform.position = new Vector3(centerX, lineY, centerZ);

        MeshFilter meshFilter = lines.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateCombinedGridLineMesh(fullSize, thickness);

        MeshRenderer lineRenderer = lines.AddComponent<MeshRenderer>();
        lineRenderer.sharedMaterial = material;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private Mesh CreateCombinedGridLineMesh(float fullSize, float thickness)
    {
        int lineCount = (gridSize + 1) * 2;
        Vector3[] vertices = new Vector3[lineCount * 4];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[lineCount * 6];

        float firstBoundary = -fullSize * 0.5f;
        int lineIndex = 0;
        for (int i = 0; i <= gridSize; i++)
        {
            float offset = firstBoundary + i * tileSize;
            AddGridLineQuad(
                vertices,
                uvs,
                triangles,
                lineIndex++,
                new Vector2(offset, 0f),
                new Vector2(thickness, fullSize + thickness));
            AddGridLineQuad(
                vertices,
                uvs,
                triangles,
                lineIndex++,
                new Vector2(0f, offset),
                new Vector2(fullSize + thickness, thickness));
        }

        Mesh mesh = new Mesh
        {
            name = "Generated Combined Tile Grid Lines"
        };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void AddGridLineQuad(
        Vector3[] vertices,
        Vector2[] uvs,
        int[] triangles,
        int lineIndex,
        Vector2 center,
        Vector2 size)
    {
        int vertexIndex = lineIndex * 4;
        int triangleIndex = lineIndex * 6;
        float halfWidth = size.x * 0.5f;
        float halfLength = size.y * 0.5f;

        vertices[vertexIndex] = new Vector3(center.x - halfWidth, 0f, center.y - halfLength);
        vertices[vertexIndex + 1] = new Vector3(center.x - halfWidth, 0f, center.y + halfLength);
        vertices[vertexIndex + 2] = new Vector3(center.x + halfWidth, 0f, center.y + halfLength);
        vertices[vertexIndex + 3] = new Vector3(center.x + halfWidth, 0f, center.y - halfLength);

        uvs[vertexIndex] = new Vector2(0f, 0f);
        uvs[vertexIndex + 1] = new Vector2(0f, 1f);
        uvs[vertexIndex + 2] = new Vector2(1f, 1f);
        uvs[vertexIndex + 3] = new Vector2(1f, 0f);

        triangles[triangleIndex] = vertexIndex;
        triangles[triangleIndex + 1] = vertexIndex + 1;
        triangles[triangleIndex + 2] = vertexIndex + 2;
        triangles[triangleIndex + 3] = vertexIndex;
        triangles[triangleIndex + 4] = vertexIndex + 2;
        triangles[triangleIndex + 5] = vertexIndex + 3;
    }

    private void CreateGridLine(Transform parent, string lineName, Vector3 position, Vector2 size, Material material)
    {
        GameObject line = new GameObject(lineName);
        line.name = lineName;
        line.layer = gameObject.layer;
        line.transform.SetParent(parent, false);
        line.transform.position = position;

        MeshFilter meshFilter = line.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetGridLineMesh(size);

        MeshRenderer lineRenderer = line.AddComponent<MeshRenderer>();
        lineRenderer.sharedMaterial = material;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private Mesh GetGridLineMesh(Vector2 size)
    {
        bool vertical = size.x < size.y;
        Mesh cachedMesh = vertical ? verticalGridLineMesh : horizontalGridLineMesh;
        if (cachedMesh != null)
        {
            return cachedMesh;
        }

        float halfWidth = size.x * 0.5f;
        float halfLength = size.y * 0.5f;
        Mesh mesh = new Mesh
        {
            name = vertical ? "Generated Vertical Grid Line Mesh" : "Generated Horizontal Grid Line Mesh"
        };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, 0f, -halfLength),
            new Vector3(-halfWidth, 0f, halfLength),
            new Vector3(halfWidth, 0f, halfLength),
            new Vector3(halfWidth, 0f, -halfLength)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        if (vertical)
        {
            verticalGridLineMesh = mesh;
        }
        else
        {
            horizontalGridLineMesh = mesh;
        }

        return mesh;
    }

    private Material GetGridLineMaterial()
    {
        if (gridLineMaterial != null)
        {
            return gridLineMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        gridLineMaterial = new Material(shader)
        {
            name = "Generated Tile Grid Line Material",
            color = gridLineColor
        };

        ConfigureTransparentMaterial(gridLineMaterial);

        if (gridLineMaterial.HasProperty("_BaseColor"))
        {
            gridLineMaterial.SetColor("_BaseColor", gridLineColor);
        }

        if (gridLineMaterial.HasProperty("_Color"))
        {
            gridLineMaterial.SetColor("_Color", gridLineColor);
        }

        return gridLineMaterial;
    }

    private void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 25;
    }
}
