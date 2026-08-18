#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

public sealed class BotGoalEvaluatorTests
{
    private BotTerritoryAIConfig config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<BotTerritoryAIConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(config);
    }

    [Test]
    public void IsolationAlwaysPrecedesExpansionAndAttack()
    {
        BotTerritorySnapshot snapshot = new BotTerritorySnapshot
        {
            OwnConnected = 449,
            OwnIsolated = 1,
            BestEnemyConnected = 499
        };
        Assert.AreEqual(BotGoal.RepairTerritory, BotGoalEvaluator.Evaluate(snapshot, config));
    }

    [Test]
    public void FinishPrecedesEnemyWarningBelowUrgentThreshold()
    {
        BotTerritorySnapshot snapshot = new BotTerritorySnapshot
        {
            OwnConnected = 450,
            BestEnemyConnected = 449
        };
        Assert.AreEqual(BotGoal.FinishVictory, BotGoalEvaluator.Evaluate(snapshot, config));
    }

    [Test]
    public void LargeDeficitTriggersConnectionAttackBeforeLateGameThreshold()
    {
        BotTerritorySnapshot snapshot = new BotTerritorySnapshot
        {
            OwnConnected = 100,
            BestEnemyConnected = 141,
            LeadingEnemy = PaintOwner.Player2
        };
        Assert.AreEqual(BotGoal.AttackConnection, BotGoalEvaluator.Evaluate(snapshot, config));
    }

    [Test]
    public void EstablishedTerritoryTransitionsToEnemyPressure()
    {
        BotTerritorySnapshot snapshot = new BotTerritorySnapshot
        {
            OwnConnected = 250,
            BestEnemyConnected = 245,
            LeadingEnemy = PaintOwner.Player2
        };
        Assert.AreEqual(BotGoal.PressureEnemy, BotGoalEvaluator.Evaluate(snapshot, config));
    }

    [TestCase("GameOver", true)]
    [TestCase("gameover", true)]
    [TestCase("Stopping", true)]
    [TestCase("Battle", false)]
    [TestCase("starting", false)]
    public void TerminalRoomStateControlsReconnectEligibility(string state, bool expected)
    {
        Assert.AreEqual(expected, RoomProcessLauncher.IsTerminalRoomState(state));
    }

    [Test]
    public void ThousandDeterministicStrategicEvaluationsRespectPriority()
    {
        System.Random random = new System.Random(20260805);
        for (int i = 0; i < 1000; i++)
        {
            BotTerritorySnapshot snapshot = new BotTerritorySnapshot
            {
                OwnConnected = random.Next(0, 501),
                OwnIsolated = random.Next(0, 100),
                BestEnemyConnected = random.Next(0, 501),
                UnderImmediateDanger = i % 97 == 0
            };
            BotGoal goal = BotGoalEvaluator.Evaluate(snapshot, config);
            if (snapshot.UnderImmediateDanger) Assert.AreEqual(BotGoal.EvadeDanger, goal);
            else if (snapshot.OwnIsolated > 0) Assert.AreEqual(BotGoal.RepairTerritory, goal);
            else Assert.AreNotEqual(BotGoal.RepairTerritory, goal);
        }
    }

    [Test]
    public void OpponentReservationsPreventThreeBotPileOn()
    {
        BotOpponentCoordinator.Clear();
        const float now = 10f;
        Assert.IsTrue(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player1, PaintOwner.Player4, 2, now, 6f));
        Assert.IsTrue(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player2, PaintOwner.Player4, 2, now, 6f));
        Assert.IsFalse(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player3, PaintOwner.Player4, 2, now, 6f));
    }

    [Test]
    public void NormalReservationAllowsOnlyOneBotPerOpponent()
    {
        BotOpponentCoordinator.Clear();
        const float now = 20f;
        Assert.IsTrue(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player1, PaintOwner.Player3, 1, now, 5f));
        Assert.IsFalse(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player2, PaintOwner.Player3, 1, now, 5f));
    }

    [Test]
    public void ExpiredReservationDoesNotBlockAnotherBot()
    {
        BotOpponentCoordinator.Clear();
        Assert.IsTrue(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player1, PaintOwner.Player4, 1, 30f, 2f));
        Assert.IsTrue(BotOpponentCoordinator.TryReserve(
            PaintOwner.Player2, PaintOwner.Player4, 1, 32.1f, 2f));
    }
}
#endif
