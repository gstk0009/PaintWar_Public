using UnityEngine;

public enum BotGoal
{
    EvadeDanger,
    RepairTerritory,
    FinishVictory,
    DenyEnemyVictory,
    DefendConnection,
    AttackConnection,
    ExpandTerritory,
    ReinforceTerritory,
    FarmResource,
    PressureEnemy,
    Stuck
}

public struct BotTerritorySnapshot
{
    public int OwnConnected;
    public int OwnIsolated;
    public int BestEnemyConnected;
    public PaintOwner LeadingEnemy;
    public bool UnderImmediateDanger;
}

public struct BotActionPlan
{
    public BotGoal Goal;
    public PaintOwner TargetOwner;
    public Vector2Int TargetIndex;
    public bool HasTarget;
    public bool AllowWeapon;
}

public static class BotGoalEvaluator
{
    public static BotGoal Evaluate(BotTerritorySnapshot snapshot, BotTerritoryAIConfig config)
    {
        if (snapshot.UnderImmediateDanger) return BotGoal.EvadeDanger;
        if (snapshot.OwnIsolated > 0) return BotGoal.RepairTerritory;
        if (snapshot.OwnConnected >= config.finishVictoryCount) return BotGoal.FinishVictory;
        if (snapshot.BestEnemyConnected >= config.victoryUrgentCount) return BotGoal.DenyEnemyVictory;
        if (snapshot.BestEnemyConnected - snapshot.OwnConnected >= config.criticalConnectionLoss)
            return BotGoal.AttackConnection;
        if (snapshot.BestEnemyConnected >= config.victoryWarningCount) return BotGoal.AttackConnection;
        if (snapshot.OwnConnected >= config.pressureStartCount && snapshot.LeadingEnemy != PaintOwner.Neutral)
            return BotGoal.PressureEnemy;
        return BotGoal.ExpandTerritory;
    }

    public static BotState ToState(BotGoal goal) => (BotState)((int)goal + 1);
}
