using UnityEngine;

public sealed partial class BombMine
{
    private sealed class ChainLinkVisual
    {
        public readonly LineRenderer Renderer;
        public BombMine First;
        public BombMine Second;
        public float Phase;

        public ChainLinkVisual(LineRenderer renderer)
        {
            Renderer = renderer;
        }
    }

    private sealed class ChainContext
    {
        public readonly PaintOwner RootOwner;
        public readonly Vector2Int RootIndex;
        public int MaxComboDepth { get; private set; }
        public int ExplodedMineCount { get; private set; }

        private int pendingMineCount;

        public ChainContext(PaintOwner rootOwner, Vector2Int rootIndex)
        {
            RootOwner = rootOwner;
            RootIndex = rootIndex;
        }

        public void RegisterMine()
        {
            pendingMineCount++;
        }

        public bool CompleteMine(int comboDepth)
        {
            ExplodedMineCount++;
            MaxComboDepth = Mathf.Max(MaxComboDepth, comboDepth);
            pendingMineCount = Mathf.Max(0, pendingMineCount - 1);
            return pendingMineCount == 0;
        }

        public bool CancelMine()
        {
            pendingMineCount = Mathf.Max(0, pendingMineCount - 1);
            return pendingMineCount == 0;
        }
    }
}
