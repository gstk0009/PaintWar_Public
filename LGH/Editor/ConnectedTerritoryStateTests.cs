#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class ConnectedTerritoryStateTests
{
    private const int Size = 10;
    private ConnectedTerritoryState territory;
    private PaintOwner[] owners;
    private bool[] destroyed;

    [SetUp]
    public void SetUp()
    {
        territory = new ConnectedTerritoryState();
        territory.Prepare(Size);
        territory.SetHome(PaintOwner.Player1, new Vector2Int(2, 2));
        owners = new PaintOwner[Size * Size];
        destroyed = new bool[owners.Length];
        for (int x = 0; x <= 4; x++) for (int y = 0; y <= 4; y++)
            owners[Flat(x, y)] = PaintOwner.Player1;
    }

    [Test]
    public void NewlyPaintedDistantTile_RemainsOwnedUnconnectedPastGrace()
    {
        Recalculate(0f);
        owners[Flat(8, 8)] = PaintOwner.Player1;
        Recalculate(1f);

        Assert.AreEqual(TerritoryTileState.OwnedUnconnected,
            territory.GetTileState(new Vector2Int(8, 8), PaintOwner.Player1));
        Assert.AreEqual(25, territory.GetConnectedCount(PaintOwner.Player1));
        Assert.AreEqual(0, territory.CollectExpired(owners, 5f, 3f).Count);
        Assert.AreEqual(PaintOwner.Player1, owners[Flat(8, 8)]);
    }

    [Test]
    public void CuttingConnectedBridge_OnlyCutOffPartBecomesPending()
    {
        AddBranch(2);
        Recalculate(0f);
        owners[Flat(5, 2)] = PaintOwner.Neutral;
        Recalculate(1f);

        Assert.AreEqual(25, territory.GetConnectedCount(PaintOwner.Player1));
        Assert.AreEqual(TerritoryTileState.IsolatedPending,
            territory.GetTileState(new Vector2Int(6, 2), PaintOwner.Player1));
        Assert.AreEqual(TerritoryTileState.ConnectedValid,
            territory.GetTileState(new Vector2Int(4, 2), PaintOwner.Player1));
    }

    [Test]
    public void ReconnectBeforeGrace_RestoresCountAndCancelsNeutralization()
    {
        AddBranch(2);
        Recalculate(0f);
        owners[Flat(5, 2)] = PaintOwner.Neutral;
        Recalculate(1f);
        owners[Flat(5, 2)] = PaintOwner.Player1;
        Recalculate(2.9f);

        Assert.AreEqual(28, territory.GetConnectedCount(PaintOwner.Player1));
        Assert.AreEqual(TerritoryTileState.ConnectedValid,
            territory.GetTileState(new Vector2Int(7, 2), PaintOwner.Player1));
        Assert.AreEqual(0, territory.CollectExpired(owners, 4.1f, 3f).Count);
    }

    [Test]
    public void FailedRecovery_ExpiresOnlySameOwnerDisconnectedTiles()
    {
        AddBranch(2);
        Recalculate(0f);
        owners[Flat(5, 2)] = PaintOwner.Neutral;
        Recalculate(1f);

        List<int> expired = territory.CollectExpired(owners, 4.01f, 3f);
        Assert.AreEqual(2, expired.Count);
        CollectionAssert.Contains(expired, Flat(6, 2));
        CollectionAssert.Contains(expired, Flat(7, 2));
    }

    [Test]
    public void PartialRecovery_RestoresOneComponentAndExpiresTheOther()
    {
        AddBranch(2);
        AddBranch(4);
        Recalculate(0f);
        owners[Flat(5, 2)] = PaintOwner.Neutral;
        owners[Flat(5, 4)] = PaintOwner.Neutral;
        Recalculate(1f);
        owners[Flat(5, 2)] = PaintOwner.Player1;
        Recalculate(2f);

        Assert.AreEqual(TerritoryTileState.ConnectedValid,
            territory.GetTileState(new Vector2Int(7, 2), PaintOwner.Player1));
        Assert.AreEqual(TerritoryTileState.IsolatedPending,
            territory.GetTileState(new Vector2Int(7, 4), PaintOwner.Player1));
        List<int> expired = territory.CollectExpired(owners, 4.01f, 3f);
        Assert.AreEqual(2, expired.Count);
        CollectionAssert.Contains(expired, Flat(6, 4));
        CollectionAssert.Contains(expired, Flat(7, 4));
    }

    [Test]
    public void EnemyClaimOnPendingTile_IsNotNeutralizedByPreviousOwnerTimer()
    {
        AddBranch(2);
        Recalculate(0f);
        owners[Flat(5, 2)] = PaintOwner.Neutral;
        Recalculate(1f);
        owners[Flat(7, 2)] = PaintOwner.Player2;
        Recalculate(2f);

        List<int> expired = territory.CollectExpired(owners, 4.01f, 3f);
        CollectionAssert.DoesNotContain(expired, Flat(7, 2));
        Assert.AreEqual(PaintOwner.Player2, owners[Flat(7, 2)]);
    }

    private void AddBranch(int y)
    {
        owners[Flat(5, y)] = PaintOwner.Player1;
        owners[Flat(6, y)] = PaintOwner.Player1;
        owners[Flat(7, y)] = PaintOwner.Player1;
    }

    private void Recalculate(float now) => territory.Recalculate(owners, destroyed, now);
    private static int Flat(int x, int y) => x * Size + y;
}
#endif
