using System.Collections.Generic;
using UnityEngine;

// 타일 Renderer 캐시와 owner별 Material 적용만 담당합니다.
// 타일 소유자 데이터와 점령 규칙은 TileGridState/TileClaimRules가 처리합니다.
public sealed class TilePaintView
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly Material neutralMaterial;
    private readonly Material[] playerMaterials;
    private Renderer[,] renderers;
    private PaintableTile[,] paintableTiles;
    private Collider[,] tileColliders;
    private int gridSize;
    private int registeredRendererCount;
    private readonly List<IsolationVisual> isolatedVisuals = new List<IsolationVisual>(256);
    private readonly MaterialPropertyBlock isolationBlock = new MaterialPropertyBlock();
    private float nextIsolationPulseAt;
    private const float IsolationPulseInterval = 0.05f;

    public TilePaintView(Material neutralMaterial, Material[] playerMaterials)
    {
        this.neutralMaterial = neutralMaterial;
        this.playerMaterials = playerMaterials;
    }

    public bool IsReady
    {
        get
        {
            return renderers != null && gridSize > 0 &&
                   (Application.isBatchMode || registeredRendererCount >= gridSize * gridSize);
        }
    }

    public void Prepare(int size)
    {
        gridSize = Mathf.Max(0, size);
        renderers = new Renderer[gridSize, gridSize];
        paintableTiles = new PaintableTile[gridSize, gridSize];
        tileColliders = new Collider[gridSize, gridSize];
        registeredRendererCount = 0;
        isolatedVisuals.Clear();
        nextIsolationPulseAt = 0f;
    }

    public void RegisterTileRenderer(Vector2Int index, Renderer renderer)
    {
        if (!IsValidIndex(index) || renderer == null)
        {
            return;
        }

        if (renderers[index.x, index.y] == null)
        {
            registeredRendererCount++;
        }

        renderers[index.x, index.y] = renderer;
        paintableTiles[index.x, index.y] = renderer.GetComponent<PaintableTile>();
        tileColliders[index.x, index.y] = renderer.GetComponent<Collider>();
        renderer.gameObject.SetActive(true);
        renderer.sharedMaterial = GetOwnerMaterial(PaintOwner.Neutral);
    }

    public void ApplyOwner(Vector2Int index, PaintOwner owner)
    {
        if (!IsValidIndex(index) || renderers[index.x, index.y] == null)
        {
            return;
        }

        Renderer renderer = renderers[index.x, index.y];
        renderer.SetPropertyBlock(null);
        Material ownerMaterial = GetOwnerMaterial(owner);
        PaintableTile paintableTile = paintableTiles[index.x, index.y];
        if (paintableTile != null)
        {
            paintableTile.SetOwner(owner, ownerMaterial);
            return;
        }

        renderer.sharedMaterial = ownerMaterial;
    }

    public void RebuildIsolationVisuals(ConnectedTerritoryState territory)
    {
        for (int i = 0; i < isolatedVisuals.Count; i++)
            if (isolatedVisuals[i].Renderer != null) isolatedVisuals[i].Renderer.SetPropertyBlock(null);
        isolatedVisuals.Clear();
        if (territory == null || !territory.HasIsolatedTiles) return;

        for (int x = 0; x < gridSize; x++) for (int y = 0; y < gridSize; y++)
        {
            int flat = x * gridSize + y;
            Renderer renderer = renderers[x, y];
            if (!territory.IsIsolatedFlat(flat) || renderer == null || !renderer.gameObject.activeInHierarchy) continue;
            Material material = renderer.sharedMaterial;
            Color baseColor = Color.white;
            if (material != null)
            {
                if (material.HasProperty(BaseColorId)) baseColor = material.GetColor(BaseColorId);
                else if (material.HasProperty(ColorId)) baseColor = material.GetColor(ColorId);
            }
            isolatedVisuals.Add(new IsolationVisual(renderer, baseColor));
        }
    }

    public void UpdateIsolationPulse(float unscaledTime)
    {
        if (isolatedVisuals.Count == 0 || unscaledTime < nextIsolationPulseAt) return;
        nextIsolationPulseAt = unscaledTime + IsolationPulseInterval;
        float pulse = 0.28f + Mathf.PingPong(unscaledTime * 3.5f, 0.52f);
        for (int i = 0; i < isolatedVisuals.Count; i++)
        {
            IsolationVisual visual = isolatedVisuals[i];
            if (visual.Renderer == null) continue;
            Color color = Color.Lerp(visual.BaseColor * 0.32f, visual.BaseColor, pulse);
            color.a = visual.BaseColor.a;
            isolationBlock.Clear();
            isolationBlock.SetColor(BaseColorId, color);
            isolationBlock.SetColor(ColorId, color);
            visual.Renderer.SetPropertyBlock(isolationBlock);
        }
    }

    public void ApplySnapshot(PaintOwner[] owners, bool[] destroyed)
    {
        if (!IsReady || owners == null)
        {
            return;
        }

        int total = gridSize * gridSize;
        for (int i = 0; i < total; i++)
        {
            int x = i / gridSize;
            int y = i % gridSize;
            Vector2Int index = new Vector2Int(x, y);
            bool isDestroyed = destroyed != null &&
                i < destroyed.Length &&
                destroyed[i];
            SetDestroyed(index, isDestroyed);
            ApplyOwner(index, i < owners.Length ? owners[i] : PaintOwner.Neutral);
        }
    }

    public void ResetAll(PaintOwner owner = PaintOwner.Neutral)
    {
        if (!IsReady)
        {
            return;
        }

        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                Vector2Int index = new Vector2Int(x, y);
                SetDestroyed(index, false);
                ApplyOwner(index, owner);
            }
        }
    }

    public Material GetOwnerMaterial(PaintOwner owner)
    {
        if (owner == PaintOwner.Neutral)
        {
            return neutralMaterial;
        }

        int materialIndex = (int)owner - 1;
        if (playerMaterials != null && materialIndex >= 0 && materialIndex < playerMaterials.Length)
        {
            return playerMaterials[materialIndex];
        }

        return neutralMaterial;
    }

    public bool TryGetTileSurfaceY(Vector2Int index, out float surfaceY)
    {
        surfaceY = 0f;
        if (!IsValidIndex(index))
        {
            return false;
        }

        Renderer renderer = renderers[index.x, index.y];
        if (renderer == null)
        {
            return false;
        }

        Collider collider = tileColliders[index.x, index.y];
        surfaceY = collider != null ? collider.bounds.max.y : renderer.bounds.max.y;
        return true;
    }

    public bool SetDestroyed(Vector2Int index, bool destroyed)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        Renderer renderer = renderers[index.x, index.y];
        if (renderer == null)
        {
            return false;
        }

        if (renderer.gameObject.activeSelf == !destroyed)
        {
            return false;
        }

        renderer.gameObject.SetActive(!destroyed);
        return true;
    }

    private bool IsValidIndex(Vector2Int index)
    {
        return renderers != null
            && index.x >= 0
            && index.x < gridSize
            && index.y >= 0
            && index.y < gridSize;
    }

    private readonly struct IsolationVisual
    {
        public readonly Renderer Renderer;
        public readonly Color BaseColor;
        public IsolationVisual(Renderer renderer, Color baseColor)
        { Renderer = renderer; BaseColor = baseColor; }
    }
}
