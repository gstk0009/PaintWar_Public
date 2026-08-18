using System;
using UnityEngine;

[Serializable]
public sealed class MapGenerationConfig
{
    [Min(4)] public int gridSize = 50;
    [Min(0)] public int breakableCount = 600;
    [Min(0)] public int unbreakableCount = 152;
    [Min(0)] public int containedItemCount = 44;
    [Range(14, 16)] public int centralArenaSize = 16;
    [Range(1, 20)] public int maxGenerationAttempts = 20;
    [Range(0.1f, 0.5f)] public float outerBreakableTarget = 0.28f;
    [Range(0f, 0.2f)] public float outerUnbreakableTarget = 0.07f;
    [Range(0.1f, 0.3f)] public float corridorBreakableTarget = 0.20f;
    [Range(0f, 0.15f)] public float corridorUnbreakableTarget = 0.04f;
    [Range(0.1f, 0.3f)] public float centralBreakableTarget = 0.203125f;
    [Range(0f, 0.1f)] public float centralUnbreakableTarget = 0.046875f;
    [Range(4, 8)] public int encounterBarrierWidth = 6;
    [Min(0)] public int crossBreakableCount = 304;
    [Min(0)] public int crossUnbreakableCount = 64;

    public static MapGenerationConfig CreateDefault()
    {
        return new MapGenerationConfig();
    }

    public void Sanitize(int actualGridSize)
    {
        gridSize = actualGridSize;
        centralArenaSize = Mathf.Clamp(centralArenaSize, 14, 16);
        if ((centralArenaSize & 1) != 0) centralArenaSize++;
        maxGenerationAttempts = Mathf.Clamp(maxGenerationAttempts, 1, 20);
        encounterBarrierWidth = Mathf.Clamp(encounterBarrierWidth, 4, 8);
        int capacity = actualGridSize * actualGridSize;
        unbreakableCount = Mathf.Clamp(unbreakableCount, 0, capacity);
        breakableCount = Mathf.Clamp(breakableCount, 0, capacity - unbreakableCount);
        containedItemCount = Mathf.Clamp(containedItemCount, 0, breakableCount);
    }
}
