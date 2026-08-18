using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class BombMine
{
    private void SetWarningVisible(bool visible)
    {
        for (int i = 0; i < warningMarkers.Count; i++)
        {
            if (warningMarkers[i] != null)
            {
                warningMarkers[i].gameObject.SetActive(visible);
                warningMarkers[i].enabled = visible;
            }
        }
    }

    private void EnsureWarningMarkers(int requiredCount)
    {
        while (warningMarkers.Count < requiredCount)
        {
            GameObject marker = new GameObject("Bomb Mine Warning Outline");
            marker.transform.SetParent(transform, true);
            LineRenderer line = marker.AddComponent<LineRenderer>();
            line.enabled = false;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            warningMarkers.Add(line);
        }
    }

    private void RefreshWarningMarkers()
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            SetWarningVisible(false);
            warningComboDepth = 0;
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        float tileSize = Mathf.Max(0.1f, tileManager.TileSize);
        warningComboDepth = GetWarningComboDepth();
        CollectWarningExplosionIndices(warningComboDepth, warningExplosionIndices);
        EnsureWarningMarkers(warningExplosionIndices.Count);
        int markerIndex = 0;
        for (int i = 0; i < warningExplosionIndices.Count; i++)
        {
            PlaceWarningMarker(markerIndex++, warningExplosionIndices[i], tileSize);
        }

        for (int i = markerIndex; i < warningMarkers.Count; i++)
        {
            if (warningMarkers[i] != null)
            {
                warningMarkers[i].gameObject.SetActive(false);
                warningMarkers[i].enabled = false;
            }
        }

        ApplyWarningMaterial();
    }

    private int GetWarningComboDepth()
    {
        return scheduledChainContext != null
            ? Mathf.Clamp(scheduledComboDepth, 1, MaxComboDepth)
            : 1;
    }

    private void CollectWarningExplosionIndices(int comboDepth, List<Vector2Int> output)
    {
        output.Clear();
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        AddUniqueIndex(output, gridIndex);
        CollectWarningRay(Vector2Int.right, comboDepth, output);
        CollectWarningRay(Vector2Int.left, comboDepth, output);
        CollectWarningRay(Vector2Int.up, comboDepth, output);
        CollectWarningRay(Vector2Int.down, comboDepth, output);
        CollectWarningLevelBonusArea(output);
    }

    private void CollectWarningLevelBonusArea(List<Vector2Int> output)
    {
        if (mineLevel >= 4)
        {
            CollectWarningLevelFourCornerFill(output);
        }

        if (mineLevel >= 5)
        {
            CollectWarningLevelFiveCornerBlocks(output);
        }
    }

    private void CollectWarningRay(Vector2Int direction, int comboDepth, List<Vector2Int> output)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int lastResolvedIndex = gridIndex;
        bool hasResolvedEndpoint = false;
        for (int distance = 1; distance <= paintRadius; distance++)
        {
            Vector2Int index = gridIndex + direction * distance;
            if (!tileManager.IsInsideGrid(index) ||
                tileManager.IsTileDestroyed(index) ||
                WeaponPatternTraversal.IsUnbreakable(index))
            {
                break;
            }

            AddUniqueIndex(output, index);
            lastResolvedIndex = index;
            hasResolvedEndpoint = true;
            if (WeaponPatternTraversal.IsBreakable(index))
            {
                break;
            }
        }

        if (comboDepth >= MaxComboDepth && hasResolvedEndpoint)
        {
            CollectWarningEndpointBurst(lastResolvedIndex, output);
        }
    }

    private void CollectWarningLevelFourCornerFill(List<Vector2Int> output)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        for (int x = -LevelFourCornerFillRadius; x <= LevelFourCornerFillRadius; x += 2)
        {
            for (int y = -LevelFourCornerFillRadius; y <= LevelFourCornerFillRadius; y += 2)
            {
                Vector2Int index = gridIndex + new Vector2Int(x, y);
                AddWarningLevelBonusTile(tileManager, output, index);
            }
        }
    }

    private void CollectWarningLevelFiveCornerBlocks(List<Vector2Int> output)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        for (int signX = -1; signX <= 1; signX += 2)
        {
            for (int signY = -1; signY <= 1; signY += 2)
            {
                Vector2Int index = gridIndex + new Vector2Int(
                    signX * LevelFiveOuterCornerOffset,
                    signY * LevelFiveOuterCornerOffset);
                AddWarningLevelBonusTile(tileManager, output, index);
            }
        }
    }

    private static void AddWarningLevelBonusTile(
        TilePaintManager tileManager,
        List<Vector2Int> output,
        Vector2Int index)
    {
        if (!tileManager.IsInsideGrid(index) ||
            tileManager.IsTileDestroyed(index) ||
            WeaponPatternTraversal.IsUnbreakable(index))
        {
            return;
        }

        AddUniqueIndex(output, index);
    }

    private void CollectWarningEndpointBurst(Vector2Int center, List<Vector2Int> output)
    {
        TilePaintManager tileManager = TilePaintManager.Instance;
        for (int x = -ComboEndpointBurstRadius; x <= ComboEndpointBurstRadius; x++)
        {
            for (int y = -ComboEndpointBurstRadius; y <= ComboEndpointBurstRadius; y++)
            {
                Vector2Int index = center + new Vector2Int(x, y);
                if (!tileManager.IsInsideGrid(index) ||
                    tileManager.IsTileDestroyed(index) ||
                    WeaponPatternTraversal.IsUnbreakable(index))
                {
                    continue;
                }

                AddUniqueIndex(output, index);
            }
        }
    }

    private static void AddUniqueIndex(List<Vector2Int> output, Vector2Int index)
    {
        if (!output.Contains(index))
        {
            output.Add(index);
        }
    }

    private void PlaceWarningMarker(int markerListIndex, Vector2Int index, float tileSize)
    {
        if (markerListIndex < 0 || markerListIndex >= warningMarkers.Count)
        {
            return;
        }

        LineRenderer marker = warningMarkers[markerListIndex];
        if (marker == null ||
            !TilePaintManager.Instance.IsInsideGrid(index) ||
            TilePaintManager.Instance.IsTileDestroyed(index))
        {
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                marker.enabled = false;
            }

            return;
        }

        Vector3 position = TilePaintManager.Instance.GetTileCenterWorldPosition(index);
        position.y = TilePaintManager.Instance.GetTileSurfaceY(index) + WarningSurfaceOffset;
        float halfSize = tileSize * WarningOutlineTileFill * 0.5f;
        marker.SetPosition(0, new Vector3(position.x - halfSize, position.y, position.z - halfSize));
        marker.SetPosition(1, new Vector3(position.x - halfSize, position.y, position.z + halfSize));
        marker.SetPosition(2, new Vector3(position.x + halfSize, position.y, position.z + halfSize));
        marker.SetPosition(3, new Vector3(position.x + halfSize, position.y, position.z - halfSize));
        marker.widthMultiplier = Mathf.Max(0.01f, tileSize * WarningOutlineWidthRatio);
        marker.gameObject.SetActive(true);
        marker.enabled = true;
    }

    private void ApplyWarningMaterial()
    {
        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        Material rangeMaterial = visualSettings != null
            ? visualSettings.RangeIndicatorMaterial
            : null;
        Material ownerMaterial = TilePaintManager.HasInstance
            ? TilePaintManager.Instance.GetOwnerMaterial(owner)
            : null;
        Material fallbackMaterial = ownerMaterial != null
            ? SolidColorMaterialUtility.Create(ownerMaterial, "_BombWarningOutline")
            : null;
        Color outlineColor = GetWarningOutlineColor(0.96f);

        for (int i = 0; i < warningMarkers.Count; i++)
        {
            LineRenderer marker = warningMarkers[i];
            if (marker == null)
            {
                continue;
            }

            if (rangeMaterial != null)
            {
                marker.sharedMaterial = rangeMaterial;
            }
            else if (fallbackMaterial != null)
            {
                marker.sharedMaterial = fallbackMaterial;
            }

            SetRendererColor(marker, outlineColor);
        }
    }

    private void PulseWarningMarkers()
    {
        float pulse = (Mathf.Sin(Time.time * 18f) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(0.62f, 0.98f, pulse);
        float widthPulse = Mathf.Lerp(0.9f, 1.18f, pulse);
        float tileSize = TilePaintManager.HasInstance
            ? Mathf.Max(0.1f, TilePaintManager.Instance.TileSize)
            : 1f;
        Color outlineColor = GetWarningOutlineColor(alpha);

        for (int i = 0; i < warningMarkers.Count; i++)
        {
            LineRenderer marker = warningMarkers[i];
            if (marker == null || !marker.gameObject.activeSelf)
            {
                continue;
            }

            marker.widthMultiplier =
                Mathf.Max(0.01f, tileSize * WarningOutlineWidthRatio * widthPulse);
            SetRendererColor(marker, outlineColor);
        }
    }

    private Color GetWarningOutlineColor(float alpha)
    {
        WeaponVisualSettings visualSettings = WeaponVisualSettings.GetOrLoadDefault();
        Color color = visualSettings != null
            ? visualSettings.GetIndicatorColor(owner, 1f)
            : Color.white;
        color = Color.Lerp(color, Color.black, WarningOutlineDarkenBlend);
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private void SetRendererColor(Renderer target, Color color)
    {
        SetRendererColor(target, color, Color.black);
    }

    private void SetRendererColor(Renderer target, Color color, Color emissionColor)
    {
        if (target == null)
        {
            return;
        }

        if (warningMaterialProperties == null)
        {
            warningMaterialProperties = new MaterialPropertyBlock();
        }

        target.GetPropertyBlock(warningMaterialProperties);
        warningMaterialProperties.SetColor(BaseColorPropertyId, color);
        warningMaterialProperties.SetColor(ColorPropertyId, color);
        warningMaterialProperties.SetColor(TintColorPropertyId, color);
        warningMaterialProperties.SetColor(EmissionColorPropertyId, emissionColor);
        target.SetPropertyBlock(warningMaterialProperties);
    }
}
