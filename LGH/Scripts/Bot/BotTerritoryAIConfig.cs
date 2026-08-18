using UnityEngine;

[CreateAssetMenu(menuName = "PaintWar/Bot/Territory AI Config", fileName = "BotTerritoryAIConfig")]
public sealed class BotTerritoryAIConfig : ScriptableObject
{
    [Min(1)] public int repairImmediateThreshold = 10;
    [Min(1)] public int repairHighThreshold = 30;
    [Min(1)] public int repairCriticalThreshold = 80;
    [Min(1)] public int victoryWarningCount = 400;
    [Min(1)] public int victoryUrgentCount = 450;
    [Min(1)] public int finishVictoryCount = 450;
    [Min(1)] public int criticalConnectionLoss = 40;
    [Min(1)] public int pressureStartCount = 250;
    [Min(1)] public int threatRadius = 8;
    [Min(8)] public int maximumScanCandidates = 96;

    [Header("Opponent Selection")]
    [Min(0.5f)] public float opponentCommitSeconds = 4f;
    [Min(1f)] public float opponentReservationSeconds = 6f;
    [Min(0f)] public float opponentDistanceWeight = 2f;
    [Min(0f)] public float opponentTerritoryWeight = 0.35f;
    [Min(0f)] public float opponentConnectionWeight = 0.8f;
    [Min(0f)] public float leaderBonus = 18f;
    [Min(0f)] public float urgentLeaderBonus = 90f;
    [Min(0f)] public float reservationPenalty = 140f;
    [Min(0f)] public float repeatedTargetPenalty = 22f;

    [Header("Adaptive Behaviour")]
    [Min(1f)] public float rivalryMemorySeconds = 12f;
    [Range(0f, 0.25f)] public float personalityInfluence = 0.12f;
    [Min(1f)] public float finalCountdownThreshold = 30f;

    private static BotTerritoryAIConfig cached;

    public static BotTerritoryAIConfig LoadDefault()
    {
        if (cached == null) cached = Resources.Load<BotTerritoryAIConfig>("BotTerritoryAIConfig");
        if (cached == null) cached = CreateInstance<BotTerritoryAIConfig>();
        return cached;
    }
}
