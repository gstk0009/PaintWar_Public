// 봇이 현재 수행 중인 전략 목표입니다.
public enum BotState
{
    Idle,
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
