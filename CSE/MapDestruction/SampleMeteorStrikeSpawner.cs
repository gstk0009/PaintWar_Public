using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;

public struct MeteorStrikeRequestBroadcast : IBroadcast
{
    public byte Requested;
}

public struct MeteorStrikeWarningBroadcast : IBroadcast
{
    public int EventId;
    public int[] TargetX;
    public int[] TargetY;
    public float ImpactDelaySeconds;
}

[DisallowMultipleComponent]
public sealed class SampleMeteorStrikeSpawner : MonoBehaviour
{
    [SerializeField] private SampleMeteorProjectile meteorPrefab;
    [SerializeField] private MeteorNotificationView notificationView;
    [SerializeField] private float spawnHeight = 26f;
    [SerializeField] private float impactRadius = 2.5f;
    [SerializeField] private float fallSpeed = 8f;
    [SerializeField] private float impactStunDuration = 2f;
    [SerializeField] private Vector2 entryOffset = new Vector2(-4f, 3f);
    [SerializeField] private int randomEdgePadding = 1;
    [SerializeField] private int randomTargetAttempts = 32;
    [SerializeField] private Vector2Int targetGridIndex = new Vector2Int(-1, -1);

    [Header("Timed Match Event")]
    [SerializeField] private bool enableTimedMeteorEvent = true;
    [SerializeField, Min(1)] private int timedMeteorCount = 5;
    [SerializeField, Min(0f)] private float targetRemainingSeconds = 75f;
    [SerializeField, Min(0f)] private float warningLeadSeconds = 3f;
    [SerializeField, Range(0f, 0.5f)] private float minimumTargetSeparationRatio = 0.18f;
    [SerializeField, Range(0f, 0.5f)] private float conflictTargetClusterRadiusRatio = 0.22f;
    [SerializeField, Range(0f, 0.3f)] private float conflictTargetEdgePaddingRatio = 0.12f;

    private readonly System.Collections.Generic.List<Vector2Int> targetBuffer =
        new System.Collections.Generic.List<Vector2Int>(8);
    private readonly System.Collections.Generic.HashSet<int> handledEventIds =
        new System.Collections.Generic.HashSet<int>();
    private NetworkManager networkManager;
    private Coroutine pendingStrikeRoutine;
    private bool broadcastsRegistered;
    private bool timedEventScheduled;
    private int timedEventRoundIndex = -1;
    private int meteorEventSequence;

    private void Update()
    {
        RegisterBroadcastsIfNeeded();
        UpdateTimedMeteorEvent();
    }

    private void OnDestroy()
    {
        UnregisterBroadcasts();
    }

    private void UpdateTimedMeteorEvent()
    {
        if (!enableTimedMeteorEvent ||
            !IsAuthoritative() ||
            !GameRoundManager.HasInstance ||
            GameRoundManager.Instance.CurrentPhase != GamePhase.Battle)
        {
            return;
        }

        int roundIndex = GameRoundManager.Instance.CurrentRoundIndex;
        if (timedEventRoundIndex != roundIndex)
        {
            timedEventRoundIndex = roundIndex;
            timedEventScheduled = false;
        }

        if (timedEventScheduled)
        {
            return;
        }

        float remaining = GameRoundManager.Instance.PhaseRemainingSeconds;
        if (remaining > targetRemainingSeconds + warningLeadSeconds)
        {
            return;
        }

        timedEventScheduled = true;
        float impactDelay = Mathf.Max(0f, remaining - targetRemainingSeconds);
        TriggerMeteorWarning(roundIndex, impactDelay);
    }

    public void SpawnMeteorAtMapCenter()
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            Debug.LogWarning("[MeteorSample] Tile grid is not ready.");
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int index = GetTargetIndex(tileManager);

        Vector3 targetPosition = tileManager.GetTileCenterWorldPosition(index);
        targetPosition.y = tileManager.GetTileSurfaceY(index);

        SpawnMeteorAtWorldPosition(targetPosition);
    }

    public void SpawnMeteorAtWorldPosition(Vector3 targetPosition)
    {
        SpawnMeteorAtWorldPosition(targetPosition, true);
    }

    public void SpawnMeteorAtWorldPosition(Vector3 targetPosition, bool applyGameplayEffects)
    {
        if (meteorPrefab == null)
        {
            Debug.LogWarning("[MeteorSample] Meteor prefab is not assigned.");
            return;
        }

        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            Debug.LogWarning("[MeteorSample] Tile grid is not ready.");
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int index = tileManager.GetGridIndex(targetPosition);
        if (!tileManager.IsInsideGrid(index))
        {
            Debug.LogWarning("[MeteorSample] Target position is outside the tile grid.");
            return;
        }

        targetPosition.y = tileManager.GetTileSurfaceY(index);
        Vector3 spawnPosition = targetPosition +
            new Vector3(entryOffset.x, Mathf.Max(1f, spawnHeight), entryOffset.y);
        SampleMeteorProjectile meteor = Instantiate(meteorPrefab, spawnPosition, Quaternion.identity);
        meteor.Initialize(targetPosition, impactRadius, fallSpeed, applyGameplayEffects);
    }

    public void TriggerMeteorEventNow()
    {
        if (IsAuthoritative())
        {
            int roundIndex = GameRoundManager.HasInstance
                ? GameRoundManager.Instance.CurrentRoundIndex
                : 0;
            TriggerMeteorWarning(roundIndex, warningLeadSeconds);
            return;
        }

        RequestMeteorEventFromServer();
    }

    private void TriggerMeteorWarning(int roundIndex, float impactDelay)
    {
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        int eventSequence = ++meteorEventSequence;
        GenerateDistributedTargets(timedMeteorCount, roundIndex, eventSequence, targetBuffer);
        if (targetBuffer.Count == 0)
        {
            return;
        }

        int eventId = (roundIndex * 1009) ^
                      (eventSequence * 9176) ^
                      Mathf.RoundToInt(Time.time * 100f);
        int[] targetX = new int[targetBuffer.Count];
        int[] targetY = new int[targetBuffer.Count];
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            targetX[i] = targetBuffer[i].x;
            targetY[i] = targetBuffer[i].y;
        }

        MeteorStrikeWarningBroadcast message = new MeteorStrikeWarningBroadcast
        {
            EventId = eventId,
            TargetX = targetX,
            TargetY = targetY,
            ImpactDelaySeconds = impactDelay
        };

        StartMeteorSequence(message, true);
        BroadcastMeteorWarning(message);
    }

    private Vector2Int GetTargetIndex(TilePaintManager tileManager)
    {
        if (targetGridIndex.x >= 0 && targetGridIndex.y >= 0)
        {
            return tileManager.ClampIndex(targetGridIndex);
        }

        int min = Mathf.Clamp(randomEdgePadding, 0, tileManager.GridSize - 1);
        int maxExclusive = Mathf.Max(min + 1, tileManager.GridSize - min);
        int attempts = Mathf.Max(1, randomTargetAttempts);

        for (int i = 0; i < attempts; i++)
        {
            Vector2Int candidate = new Vector2Int(
                Random.Range(min, maxExclusive),
                Random.Range(min, maxExclusive));

            if (!tileManager.IsTileDestroyed(candidate))
            {
                return candidate;
            }
        }

        return new Vector2Int(
            Random.Range(0, tileManager.GridSize),
            Random.Range(0, tileManager.GridSize));
    }

    private void GenerateDistributedTargets(
        int targetCount,
        int roundIndex,
        int eventSequence,
        System.Collections.Generic.List<Vector2Int> output)
    {
        output.Clear();
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Random.State previousState = Random.state;
        int seed = GameRoundManager.HasInstance && GameRoundManager.Instance.ItemSpawnSeed != 0
            ? GameRoundManager.Instance.ItemSpawnSeed
            : Mathf.RoundToInt(Time.time * 1000f);
        Random.InitState(seed ^
                         (roundIndex * 73856093) ^
                         (eventSequence * 19349663) ^
                         0x4D455445);

        int count = Mathf.Max(1, targetCount);
        int attempts = Mathf.Max(count * Mathf.Max(8, randomTargetAttempts), 32);
        float initialSeparation = Mathf.Max(
            2f,
            tileManager.GridSize * Mathf.Clamp01(minimumTargetSeparationRatio));
        Vector2 conflictCenter = GetConflictTargetCenter(tileManager);
        float clusterRadius = Mathf.Max(
            initialSeparation,
            tileManager.GridSize * Mathf.Clamp01(conflictTargetClusterRadiusRatio));
        int edgePadding = Mathf.Clamp(
            Mathf.RoundToInt(tileManager.GridSize * Mathf.Clamp01(conflictTargetEdgePaddingRatio)),
            randomEdgePadding,
            Mathf.Max(randomEdgePadding, tileManager.GridSize / 3));

        for (int attempt = 0; output.Count < count && attempt < attempts; attempt++)
        {
            Vector2Int candidate = GetConflictWeightedTargetIndex(
                tileManager,
                conflictCenter,
                clusterRadius,
                edgePadding);
            float relaxation = attempt / Mathf.Max(1f, attempts - 1f);
            float separation = Mathf.Lerp(initialSeparation, 1f, relaxation);
            if (IsFarEnoughFromTargets(candidate, output, separation))
            {
                output.Add(candidate);
            }
        }

        for (int attempt = 0; output.Count < count && attempt < attempts; attempt++)
        {
            Vector2Int candidate = GetConflictWeightedTargetIndex(
                tileManager,
                conflictCenter,
                clusterRadius * 1.35f,
                edgePadding);
            if (!output.Contains(candidate))
            {
                output.Add(candidate);
            }
        }

        Random.state = previousState;
    }

    private static Vector2 GetConflictTargetCenter(TilePaintManager tileManager)
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        if (GameRoundManager.HasInstance)
        {
            System.Collections.Generic.IReadOnlyList<GameParticipant> participants =
                GameRoundManager.Instance.Participants;
            for (int i = 0; i < participants.Count; i++)
            {
                GameParticipant participant = participants[i];
                if (participant == null ||
                    participant.IsEliminated ||
                    participant.Transform == null)
                {
                    continue;
                }

                Vector2Int index = tileManager.GetGridIndex(participant.Transform.position);
                if (!tileManager.IsInsideGrid(index))
                {
                    continue;
                }

                sum += new Vector2(index.x, index.y);
                count++;
            }
        }

        if (count <= 0)
        {
            System.Collections.Generic.IReadOnlyList<PlayerPaintIdentity> players =
                PlayerPaintIdentity.ActivePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerPaintIdentity identity = players[i];
                if (identity == null || identity.Owner == PaintOwner.Neutral)
                {
                    continue;
                }

                Vector2Int index = tileManager.GetGridIndex(identity.transform.position);
                if (!tileManager.IsInsideGrid(index))
                {
                    continue;
                }

                sum += new Vector2(index.x, index.y);
                count++;
            }
        }

        if (count > 0)
        {
            return sum / count;
        }

        float center = Mathf.Max(0f, (tileManager.GridSize - 1) * 0.5f);
        return new Vector2(center, center);
    }

    private Vector2Int GetConflictWeightedTargetIndex(
        TilePaintManager tileManager,
        Vector2 conflictCenter,
        float clusterRadius,
        int edgePadding)
    {
        int min = Mathf.Clamp(edgePadding, 0, tileManager.GridSize - 1);
        int max = Mathf.Max(min, tileManager.GridSize - min - 1);
        int attempts = Mathf.Max(1, randomTargetAttempts);

        for (int i = 0; i < attempts; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(Random.value) * Mathf.Max(0.1f, clusterRadius);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            Vector2Int candidate = new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(conflictCenter.x + offset.x), min, max),
                Mathf.Clamp(Mathf.RoundToInt(conflictCenter.y + offset.y), min, max));
            if (!tileManager.IsTileDestroyed(candidate))
            {
                return candidate;
            }
        }

        return tileManager.ClampIndex(new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(conflictCenter.x), min, max),
            Mathf.Clamp(Mathf.RoundToInt(conflictCenter.y), min, max)));
    }

    private static bool IsFarEnoughFromTargets(
        Vector2Int candidate,
        System.Collections.Generic.List<Vector2Int> targets,
        float separation)
    {
        float separationSqr = separation * separation;
        for (int i = 0; i < targets.Count; i++)
        {
            if ((candidate - targets[i]).sqrMagnitude < separationSqr)
            {
                return false;
            }
        }

        return true;
    }

    private void StartMeteorSequence(MeteorStrikeWarningBroadcast message, bool applyGameplayEffects)
    {
        if (!handledEventIds.Add(message.EventId))
        {
            return;
        }

        if (handledEventIds.Count > 32)
        {
            handledEventIds.Clear();
            handledEventIds.Add(message.EventId);
        }

        if (pendingStrikeRoutine != null)
        {
            StopCoroutine(pendingStrikeRoutine);
        }

        if (notificationView != null)
        {
            notificationView.Show("Warning Warning!!", Mathf.Max(0.1f, message.ImpactDelaySeconds));
        }

        pendingStrikeRoutine = StartCoroutine(SpawnMeteorsAfterDelay(message, applyGameplayEffects));
    }

    private System.Collections.IEnumerator SpawnMeteorsAfterDelay(
        MeteorStrikeWarningBroadcast message,
        bool applyGameplayEffects)
    {
        float delay = Mathf.Max(0f, message.ImpactDelaySeconds);
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        pendingStrikeRoutine = null;
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            yield break;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        int count = Mathf.Min(
            message.TargetX != null ? message.TargetX.Length : 0,
            message.TargetY != null ? message.TargetY.Length : 0);
        for (int i = 0; i < count; i++)
        {
            Vector2Int index = tileManager.ClampIndex(
                new Vector2Int(message.TargetX[i], message.TargetY[i]));
            if (!tileManager.IsInsideGrid(index) || tileManager.IsTileDestroyed(index))
            {
                continue;
            }

            Vector3 targetPosition = tileManager.GetTileCenterWorldPosition(index);
            targetPosition.y = tileManager.GetTileSurfaceY(index);
            if (applyGameplayEffects)
            {
                StartCoroutine(ApplyMeteorGameplayAfterFlight(targetPosition));
            }

            SpawnMeteorAtWorldPosition(targetPosition, false);
        }
    }

    private System.Collections.IEnumerator ApplyMeteorGameplayAfterFlight(Vector3 targetPosition)
    {
        float delay = EstimateMeteorFlightSeconds();
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        SampleMeteorProjectile.ApplyGameplayAtWorldPosition(
            targetPosition,
            impactRadius,
            impactStunDuration,
            out int neutralizedTiles,
            out int destroyedObstacles,
            out int stunnedParticipants);
        Debug.Log(
            "[MeteorSample] Authoritative impact neutralized tiles=" + neutralizedTiles +
            " destroyedObstacles=" + destroyedObstacles +
            " stunnedParticipants=" + stunnedParticipants + ".");
    }

    private float EstimateMeteorFlightSeconds()
    {
        float speed = Mathf.Max(0.001f, fallSpeed);
        Vector3 offset = new Vector3(
            entryOffset.x,
            Mathf.Max(1f, spawnHeight) - 0.15f,
            entryOffset.y);
        return offset.magnitude / speed;
    }

    private void BroadcastMeteorWarning(MeteorStrikeWarningBroadcast message)
    {
        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(message);
    }

    private void RequestMeteorEventFromServer()
    {
        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsClientStarted)
        {
            return;
        }

        networkManager.ClientManager.Broadcast(new MeteorStrikeRequestBroadcast
        {
            Requested = 1
        });
    }

    private void OnMeteorStrikeRequest(
        NetworkConnection connection,
        MeteorStrikeRequestBroadcast message,
        Channel channel)
    {
        if (message.Requested == 0 || !IsAuthoritative())
        {
            return;
        }

        int roundIndex = GameRoundManager.HasInstance
            ? GameRoundManager.Instance.CurrentRoundIndex
            : 0;
        TriggerMeteorWarning(roundIndex, warningLeadSeconds);
    }

    private void OnMeteorWarning(MeteorStrikeWarningBroadcast message, Channel channel)
    {
        StartMeteorSequence(message, false);
    }

    private bool IsAuthoritative()
    {
        CacheNetworkManager();
        return networkManager == null ||
               networkManager.IsServerStarted ||
               !networkManager.IsClientStarted;
    }

    private void RegisterBroadcastsIfNeeded()
    {
        CacheNetworkManager();
        if (broadcastsRegistered ||
            networkManager == null ||
            networkManager.ClientManager == null ||
            networkManager.ServerManager == null)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<MeteorStrikeRequestBroadcast>(
            OnMeteorStrikeRequest,
            false);
        networkManager.ClientManager.RegisterBroadcast<MeteorStrikeWarningBroadcast>(OnMeteorWarning);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered ||
            networkManager == null ||
            networkManager.ClientManager == null ||
            networkManager.ServerManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<MeteorStrikeRequestBroadcast>(
            OnMeteorStrikeRequest);
        networkManager.ClientManager.UnregisterBroadcast<MeteorStrikeWarningBroadcast>(OnMeteorWarning);
        broadcastsRegistered = false;
    }

    private void CacheNetworkManager()
    {
        if (networkManager == null)
        {
            networkManager = InstanceFinder.NetworkManager;
        }
    }
}
