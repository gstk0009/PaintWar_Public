using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using UnityEngine;

// 매칭, Farming/Battle phase, 탈락, 최종 승자를 관리하는 권한 있는 라운드 상태 머신입니다.
// 네트워크 모드에서는 서버가 규칙을 실행하고 클라이언트는 RoundStateBroadcast snapshot을 반영합니다.
public class GameRoundManager : MonoBehaviour
{
    public static GameRoundManager Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    private const string RoundFlowPrefix = "[RoundFlow] ";
    private const int MaxPlayers = 4;
    private static readonly PaintOwner[] OwnerOrder =
    {
        PaintOwner.Player1,
        PaintOwner.Player2,
        PaintOwner.Player3,
        PaintOwner.Player4
    };

    [Header("Match")]
    [SerializeField] private float matchmakingWaitSeconds = 60f;
    // 에디터/오프라인 테스트용 로컬 시뮬레이션 보조 옵션입니다. 네트워크 모드는 NetworkRoundController가 봇을 채웁니다.
    [SerializeField] private bool fillBotsImmediatelyForLocalTest = true;
    [SerializeField] private GameObject playerTemplate;
    [SerializeField] private bool autoStartLocalSimulation = true;

    [Header("Match Flow")]
    [SerializeField, Min(1f)] private float matchSeconds = 150f;
    [SerializeField, Min(0f)] private float overtimeDurationSeconds = 15f;
    [SerializeField, Min(0f)] private float farmingBoundaryDuration = 0f;
    [SerializeField, Min(0f)] private float finalCountdownSeconds = 30f;
    [SerializeField] private float roundResultSeconds = 3f;
    [SerializeField] private float roundTransitionFadeSeconds = 0.45f;
    [SerializeField] private float nextRoundStartHoldSeconds = 0.65f;
    [SerializeField, Min(0f)] private float networkRoundStartReleaseGraceSeconds = 0.35f;

    private readonly List<GameParticipant> participants = new List<GameParticipant>();
    // 이전 라운드 탈락자 순서 목록입니다. 최종 결과나 이후 ranking UI에 사용할 수 있습니다.
    private readonly List<GameParticipant> eliminationOrder = new List<GameParticipant>();
    private readonly List<GameParticipant> finalRanking = new List<GameParticipant>(MaxPlayers);
    private readonly List<GameParticipant> rankingRevealOrder = new List<GameParticipant>(MaxPlayers - 1);
    private List<BoundarySegment> cachedBoundarySegments = new List<BoundarySegment>();

    private RoundParticipantRegistry participantRegistry;
    private RoundTransitionPresenter transitionPresenter;
    private FarmingBoundaryView boundaryView;
    private CancellationTokenSource gameRoutineCts;
    private CancellationTokenSource networkRoundEndCts;
    private CancellationTokenSource networkRoundStartCts;
    private bool networkRoundEndPlaying;
    private bool networkRoundStartPlaying;
    private float phaseEndTime;
    private float matchStartTime;
    private int currentRoundIndex;
    private GameParticipant roundResultEliminated;
    private GameParticipant winner;
    // 로컬 자동 시뮬레이션이 서버 권한 흐름과 충돌하지 않도록 NetworkRoundController가 설정합니다.
    private bool networkControlled;
    private int lastShownNetworkResultKey = -1;
    private int lastShownNetworkStartKey = -1;
    // 서버와 클라이언트가 같은 아이템/장애물 배치를 만들도록 공유하는 seed입니다.
    private int itemSpawnSeed;
    private bool finalCountdownActive;
    private bool overtimeActive;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.MatchWaiting;
    public int CurrentRoundIndex { get { return currentRoundIndex; } }
    public float PhaseRemainingSeconds { get { return Mathf.Max(0f, phaseEndTime - Time.time); } }
    public float MatchWaitRemainingSeconds { get { return Mathf.Max(0f, matchStartTime - Time.time); } }
    public IReadOnlyList<GameParticipant> Participants { get { return participants; } }
    public GameParticipant Winner { get { return winner; } }
    public GameParticipant RoundResultEliminated { get { return roundResultEliminated; } }
    public int ItemSpawnSeed { get { return itemSpawnSeed; } }
    public bool IsFinalCountdownActive { get { return finalCountdownActive; } }
    public PaintOwner GetFinalRankOwner(int zeroBasedRank)
    {
        return zeroBasedRank >= 0 && zeroBasedRank < finalRanking.Count
            ? finalRanking[zeroBasedRank].Owner
            : PaintOwner.Neutral;
    }
    public bool IsFinalResultPresentationActive
    {
        get { return transitionPresenter != null && transitionPresenter.IsFinalResultActive; }
    }

    public float RoundResultPromptDelaySeconds
    {
        get { return roundResultSeconds + roundTransitionFadeSeconds + 0.1f; }
    }

    public string RoundDisplayName
    {
        get
        {
            if (CurrentPhase == GamePhase.MatchWaiting)
            {
                return "Matching";
            }

            if (CurrentPhase == GamePhase.GameOver)
            {
                return "Game Over";
            }

            return "Match";
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        participantRegistry = new RoundParticipantRegistry(participants, OwnerOrder, playerTemplate);
        transitionPresenter = new RoundTransitionPresenter();
        boundaryView = GetComponent<FarmingBoundaryView>() ?? gameObject.AddComponent<FarmingBoundaryView>();
    }

    private async void Start()
    {
        await UniTask.Yield(PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());

        if (autoStartLocalSimulation && !ShouldUseNetworkControl())
        {
            StartLocalSimulation();
        }
    }

    private void OnDestroy()
    {
        CancelGameRoutine();
        CancelNetworkRoundEndTransition();
        CancelNetworkRoundStartTransition();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetNetworkControlled(bool value)
    {
        networkControlled = value;
        if (networkControlled && gameRoutineCts != null && CurrentPhase == GamePhase.MatchWaiting)
        {
            CancelGameRoutine();
        }
    }

    private bool ShouldUseNetworkControl()
    {
        if (networkControlled)
        {
            return true;
        }

        FishNet.Managing.NetworkManager manager = InstanceFinder.NetworkManager;
        return manager != null &&
               (manager.IsClientStarted || manager.IsServerStarted);
    }

    public void StartLocalSimulation()
    {
        StartGameRoutine(LocalSimulationAsync);
    }

    public void EnterNetworkMatchWaiting(float waitSeconds)
    {
        SetNetworkControlled(true);
        CurrentPhase = GamePhase.MatchWaiting;
        currentRoundIndex = 0;
        winner = null;
        phaseEndTime = Time.time;
        matchStartTime = Time.time + Mathf.Max(0f, waitSeconds);
    }

    public void StartNetworkRounds()
    {
        SetNetworkControlled(true);
        LogRoundFlow(
            "StartNetworkRounds requested. phase=" + CurrentPhase +
            " participants=" + participants.Count +
            " alive=" + CountAliveParticipants() +
            " " + GetTileGridDebugStatus() + ".");
        // seed는 라운드 콘텐츠 생성 전에 매치당 한 번만 생성됩니다.
        EnsureItemSpawnSeed();
        ItemSpawner.SetSynchronizedSessionSeed(itemSpawnSeed);
        ObstacleFieldManager.SetSynchronizedSessionSeed(itemSpawnSeed);
        participantRegistry.RefreshParticipantStatRegistrations();
        StartGameRoutine(RunRoundsAsync);
    }

    public void RegisterNetworkHumanPlayer(PaintOwner owner, string displayName, GameObject playerObject)
    {
        participantRegistry.RegisterExistingParticipant(owner, displayName, false, playerObject);
        participantRegistry.RefreshParticipantStatRegistrations();
    }

    public void RegisterNetworkBot(PaintOwner owner, string displayName, GameObject botObject)
    {
        participantRegistry.RegisterExistingParticipant(owner, displayName, true, botObject);
        participantRegistry.RefreshParticipantStatRegistrations();
    }

    public void ApplyNetworkState(RoundStateBroadcast state)
    {
        // 클라이언트 mirror 경로입니다. 여기서는 gameplay 판단 없이 표시/상태 복원만 합니다.
        GamePhase previousPhase = CurrentPhase;
        int previousRoundIndex = currentRoundIndex;
        int previousAliveMask = GetAliveParticipantMask();
        bool wasFinalCountdownActive = finalCountdownActive;

        CurrentPhase = (GamePhase)state.Phase;
        currentRoundIndex = state.RoundIndex;
        phaseEndTime = Time.time + Mathf.Max(0f, state.PhaseRemainingSeconds);
        matchStartTime = Time.time + Mathf.Max(0f, state.MatchWaitRemainingSeconds);
        itemSpawnSeed = state.ItemSpawnSeed;
        ItemSpawner.SetSynchronizedSessionSeed(itemSpawnSeed);
        ObstacleFieldManager.SetSynchronizedSessionSeed(itemSpawnSeed);
        finalCountdownActive = state.FinalCountdownActive != 0;
        participantRegistry.ApplyNetworkParticipantMask(state);
        ApplyNetworkTileCounts(state);
        int currentAliveMask = GetAliveParticipantMask();
        UpdateNetworkRoundResult(state);
        bool enteredNewRoundStart = false;

        // Battle은 기존 하위 시스템과의 호환을 위해 유지한 이름이며, 이제 4분 단판 전체를 뜻합니다.
        if (finalCountdownActive && !wasFinalCountdownActive)
        {
            ApplyFinalCountdownPresentation();
        }
        else if (!finalCountdownActive && wasFinalCountdownActive)
        {
            RestoreRegularMatchPresentation();
        }

        // 생존자 구성이 바뀌거나 새 Farming phase가 시작되면 경계 표시를 다시 만들어야 합니다.
        bool shouldRebuildBoundary = CurrentPhase == GamePhase.Farming
            && (previousPhase != GamePhase.Farming
                || previousRoundIndex != currentRoundIndex
                || previousAliveMask != currentAliveMask
                || cachedBoundarySegments == null
                || cachedBoundarySegments.Count == 0);

        bool shouldConfigureHomes = CurrentPhase == GamePhase.Battle &&
            (previousPhase != GamePhase.Battle || previousRoundIndex != currentRoundIndex);
        if (shouldConfigureHomes && TilePaintManager.HasInstance)
        {
            AssignZonesForAlivePlayers();
            TilePaintManager.Instance.ConfigureHomeTerritories(participants);
        }

        if (shouldRebuildBoundary && TilePaintManager.HasInstance)
        {
            AssignZonesForAlivePlayers();
            RebuildBoundary();
            PlayNetworkRoundStartTransitionIfNeeded(
                enteredNewRoundStart,
                previousRoundIndex);
            return;
        }

        SetBoundaryVisible(CurrentPhase == GamePhase.Farming);
        PlayNetworkRoundStartTransitionIfNeeded(
            enteredNewRoundStart,
            previousRoundIndex);
    }

    public void ApplyNetworkRoster(RoundRosterBroadcast roster)
    {
        participantRegistry.ApplyNetworkRoster(roster);
    }

    private void ApplyNetworkTileCounts(RoundStateBroadcast state)
    {
        ApplyNetworkTileCount(PaintOwner.Player1, state.Player1TileCount);
        ApplyNetworkTileCount(PaintOwner.Player2, state.Player2TileCount);
        ApplyNetworkTileCount(PaintOwner.Player3, state.Player3TileCount);
        ApplyNetworkTileCount(PaintOwner.Player4, state.Player4TileCount);
    }

    private void ApplyNetworkTileCount(PaintOwner owner, int tileCount)
    {
        GameParticipant participant = FindParticipant(owner);
        if (participant != null)
        {
            participant.SetNetworkTileCount(tileCount);
        }
    }

    public bool CanOwnerClaimTile(PaintOwner owner, Vector2Int index)
    {
        // Battle은 전체 점령 가능이고, Farming은 배정된 zone 안에서만 점령할 수 있습니다.
        if (owner == PaintOwner.Neutral)
        {
            return false;
        }

        GameParticipant participant = FindParticipant(owner);
        if (participant == null || participant.IsEliminated)
        {
            return false;
        }

        if (CurrentPhase == GamePhase.Battle)
        {
            return true;
        }

        if (CurrentPhase != GamePhase.Farming)
        {
            return false;
        }

        return participant.Zone != null && participant.Zone.Contains(index);
    }

    public void ClampTransformToOwnerZone(PaintOwner owner, Transform target)
    {
        // 현재 이동 제한은 FarmingBoundaryView collider가 담당합니다.
    }

    public GameParticipant FindParticipant(PaintOwner owner)
    {
        return participantRegistry != null ? participantRegistry.FindParticipant(owner) : null;
    }

    public IReadOnlyList<GameParticipant> GetAliveParticipants()
    {
        List<GameParticipant> alive = new List<GameParticipant>();
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && !participant.IsEliminated)
            {
                alive.Add(participant);
            }
        }

        return alive;
    }

    private void StartGameRoutine(Func<CancellationToken, UniTask> routine)
    {
        // 라운드 루프는 한 번에 하나만 실행됩니다. 새 루프를 시작하면 이전 task를 취소합니다.
        CancelGameRoutine();
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        gameRoutineCts = source;
        RunGameRoutineAsync(routine, source).Forget();
    }

    private async UniTask RunGameRoutineAsync(Func<CancellationToken, UniTask> routine, CancellationTokenSource source)
    {
        try
        {
            await routine(source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogError(RoundFlowPrefix + "Game round routine failed before completion.");
            Debug.LogException(ex);
        }
        finally
        {
            if (gameRoutineCts == source)
            {
                gameRoutineCts = null;
                source.Dispose();
            }
        }
    }

    private void CancelGameRoutine()
    {
        if (gameRoutineCts == null)
        {
            return;
        }

        CancellationTokenSource source = gameRoutineCts;
        gameRoutineCts = null;
        source.Cancel();
        source.Dispose();
    }

    private int GetAliveParticipantMask()
    {
        int mask = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.IsEliminated || participant.Owner == PaintOwner.Neutral)
            {
                continue;
            }

            mask |= 1 << ((int)participant.Owner - 1);
        }

        return mask;
    }

    private async UniTask LocalSimulationAsync(CancellationToken token)
    {
        // 오프라인/에디터 흐름은 grid 대기, 로컬 플레이어 등록, 빈 slot 채우기 후 일반 라운드를 실행합니다.
        CurrentPhase = GamePhase.MatchWaiting;
        await UniTask.WaitUntil(IsGridReady, cancellationToken: token);

        participantRegistry.RegisterLocalHumanPlayer();

        float waitSeconds = fillBotsImmediatelyForLocalTest ? 0f : matchmakingWaitSeconds;
        matchStartTime = Time.time + waitSeconds;

        while (Time.time < matchStartTime && participants.Count < MaxPlayers)
        {
            participantRegistry.RegisterLocalHumanPlayer();
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        participantRegistry.FillEmptySlotsWithBots();
        participantRegistry.RefreshParticipantStatRegistrations();
        EnsureItemSpawnSeed();
        ItemSpawner.SetSynchronizedSessionSeed(itemSpawnSeed);
        Debug.Log("[Round] Local round started with " + participants.Count + " participants.");
        await RunRoundsAsync(token);
    }

    private async UniTask RunRoundsAsync(CancellationToken token)
    {
        LogRoundFlow("RunRoundsAsync waiting for tile grid. " + GetTileGridDebugStatus() + ".");
        await UniTask.WaitUntil(IsGridReady, cancellationToken: token);
        LogRoundFlow(
            "RunRoundsAsync grid ready. participants=" + participants.Count +
            " alive=" + CountAliveParticipants() + ".");

        winner = null;
        eliminationOrder.Clear();
        finalRanking.Clear();
        finalCountdownActive = false;
        currentRoundIndex = 1;

        LogRoundFlow(
            "Single match setup starting. participants=" + participants.Count +
            " alive=" + CountAliveParticipants() + ".");
        BeginRound();
        float farmingSeconds = Mathf.Clamp(farmingBoundaryDuration, 0f, matchSeconds);
        float battleSeconds = Mathf.Max(0f, matchSeconds - farmingSeconds);
        LogRoundFlow("Single match setup complete. Starting protected farming, then open battle.");
        if (farmingSeconds > 0f)
        {
            await RunTimedPhaseAsync(GamePhase.Farming, farmingSeconds, true, token);
        }
        await RunTimedPhaseAsync(GamePhase.Battle, battleSeconds, false, token);
        if (winner == null && HasTopTie())
        {
            overtimeActive = true;
            finalCountdownActive = false;
            await RunTimedPhaseAsync(GamePhase.Battle, overtimeDurationSeconds, false, token);
            overtimeActive = false;
        }
        await EndSingleMatchAsync(token);
    }

    private async UniTask RunTimedPhaseAsync(GamePhase phase, float duration, bool showBoundary, CancellationToken token)
    {
        CurrentPhase = phase;
        SetBoundaryVisible(showBoundary);
        phaseEndTime = Time.time + duration;
        BroadcastRoundStateIfServer();
        LogRoundFlow(
            "Phase started. round=" + currentRoundIndex +
            " phase=" + phase +
            " duration=" + duration.ToString("0.##") +
            " boundaryVisible=" + showBoundary + ".");
        await WaitForPhaseEndAsync(token);
    }

    private void BeginRound()
    {
        LogRoundFlow(
            "BeginRound entered. round=" + currentRoundIndex +
            " participants=" + participants.Count +
            " alive=" + CountAliveParticipants() +
            " " + GetTileGridDebugStatus() + ".");
        if (participants.Count == 0)
        {
            Debug.LogError(RoundFlowPrefix + "BeginRound has no participants. Check player registration before round start.");
        }

        EnsureItemSpawnSeed();
        ItemSpawner.SetSynchronizedSessionSeed(itemSpawnSeed);
        ObstacleFieldManager.SetSynchronizedSessionSeed(itemSpawnSeed);
        roundResultEliminated = null;
        finalCountdownActive = false;

        // 라운드 시작이 막혔을 때 서버 로그로 원인을 찾기 쉽도록 setup을 단계별 로그로 나눴습니다.
        RunRoundSetupStep("reset tiles", ResetRoundTiles);
        RunRoundSetupStep("reset claim service", ResetTileClaimService);
        RunRoundSetupStep("reset participants", ResetParticipantRoundState);
        RunRoundSetupStep("assign zones", AssignZonesForAlivePlayers);
        RunRoundSetupStep("initialize protected homes", InitializeProtectedHomes);
        RunRoundSetupStep("clear items", ItemSpawner.ClearAllItemsForCurrentScene);
        RunRoundSetupStep("build obstacles", PrepareObstacleField);
        RunRoundSetupStep("place players", PlaceAlivePlayersForRound);
        RunRoundSetupStep("rebuild farming boundary", RebuildBoundary);

        LogRoundFlow(
            "BeginRound finished. round=" + currentRoundIndex +
            " participants=" + participants.Count +
            " alive=" + CountAliveParticipants() + ".");
    }

    private void ResetRoundTiles()
    {
        if (NetworkTilePaintAuthority.HasInstance)
        {
            NetworkTilePaintAuthority.Instance.ServerResetAllTiles();
            return;
        }

        if (TilePaintManager.HasInstance)
        {
            TilePaintManager.Instance.ResetAllTiles();
        }
    }

    private void ResetTileClaimService()
    {
        if (TileClaimService.HasInstance)
        {
            TileClaimService.Instance.ResetRoundState();
        }
    }

    private void PlaceAlivePlayersForRound()
    {
        if (!TilePaintManager.HasInstance)
        {
            Debug.LogWarning(RoundFlowPrefix + "Player placement skipped because TilePaintManager is missing.");
            return;
        }

        RoundPlayerPlacement.PlaceAlivePlayersAtZoneCenters(participants, TilePaintManager.Instance);
    }

    private void RunRoundSetupStep(string stepName, Action step)
    {
        try
        {
            LogRoundFlow("BeginRound step: " + stepName + ".");
            step();
        }
        catch (Exception ex)
        {
            Debug.LogError(RoundFlowPrefix + "BeginRound step failed: " + stepName + ".");
            Debug.LogException(ex);
        }
    }

    private void EnsureItemSpawnSeed()
    {
        if (itemSpawnSeed != 0)
        {
            return;
        }

        itemSpawnSeed = UnityEngine.Random.Range(int.MinValue + 1, int.MaxValue);
        if (itemSpawnSeed == 0)
        {
            itemSpawnSeed = 1;
        }
    }

    private void InitializeProtectedHomes()
    {
        if (TilePaintManager.HasInstance)
            TilePaintManager.Instance.ConfigureHomeTerritories(participants);
    }

    private void PrepareObstacleField()
    {
        if (ObstacleFieldManager.HasInstance)
        {
            ObstacleFieldManager.Instance.ResetForNewMatch(itemSpawnSeed);
        }
    }

    private void ResetParticipantRoundState()
    {
        // EXP/level은 라운드 사이에도 유지하고, 라운드별 타일 점령 카운터만 초기화합니다.
        foreach (GameParticipant participant in participants)
        {
            if (participant.Identity != null)
            {
                participant.Identity.SetOwner(participant.Owner);
            }

            if (participant.Stats != null)
            {
                participant.Stats.ResetRoundClaimStats();
            }
        }
    }

    private void AssignZonesForAlivePlayers()
    {
        if (!TilePaintManager.HasInstance)
        {
            return;
        }

        cachedBoundarySegments = RoundZoneAssigner.AssignZones(participants, TilePaintManager.Instance);
    }

    private void RebuildBoundary()
    {
        boundaryView.Rebuild(cachedBoundarySegments, TilePaintManager.HasInstance ? TilePaintManager.Instance : null);
    }

    private async UniTask ResolveRoundEliminationAsync(CancellationToken token)
    {
        // 최종 전 라운드는 타일 수가 가장 낮은 참가자를 탈락시키고 다음 Farming phase로 넘어갑니다.
        CurrentPhase = GamePhase.RoundResult;
        ClearParticipantStoredItems();
        GameParticipant loser = RoundEliminationResolver.FindRoundLoser(participants);
        roundResultEliminated = loser;

        BroadcastRoundStateIfServer();
        await PlayRoundEndTransitionAsync(loser, token);
        RoundEliminationResolver.Eliminate(loser, eliminationOrder);
        BroadcastRoundStateIfServer();
    }

    private async UniTask EndSingleMatchAsync(CancellationToken token)
    {
        // 4분 종료 시 서버가 전체 순위를 한 번 확정하고 모든 클라이언트가 같은 순서로 공개합니다.
        CurrentPhase = GamePhase.RoundResult;
        ClearParticipantStoredItems();
        RoundEliminationResolver.BuildFinalRanking(participants, finalRanking);
        winner = finalRanking.Count > 0 ? finalRanking[0] : null;
        roundResultEliminated = finalRanking.Count > 1 ? finalRanking[finalRanking.Count - 1] : null;

        rankingRevealOrder.Clear();
        for (int rankIndex = finalRanking.Count - 1; rankIndex >= 1; rankIndex--)
        {
            GameParticipant eliminated = finalRanking[rankIndex];
            rankingRevealOrder.Add(eliminated);
        }

        BroadcastRoundStateIfServer();

        // FinalResult presentation starts now. Block reconnect assignment immediately,
        // while keeping the room process alive long enough to show the result UI.
        if (ServerLaunchMode.IsRoomServer)
        {
            RoomServerLifecycle
                .EnsureInstance()
                .NotifyFinalResultStarted();
        }

        await PlayFinalResultTransitionAsync(winner, rankingRevealOrder, token);
        eliminationOrder.AddRange(rankingRevealOrder);

        CurrentPhase = GamePhase.GameOver;
        SetBoundaryVisible(false);
        roundResultEliminated = null;
        phaseEndTime = Time.time;
        BroadcastRoundStateIfServer();
        if (RoomServerLifecycle.Instance != null)
        {
            RoomServerLifecycle.Instance.NotifyGameOver();
        }

    }

    private async UniTask PlayRoundEndTransitionAsync(GameParticipant eliminated, CancellationToken token)
    {
        phaseEndTime = Time.time + roundResultSeconds + roundTransitionFadeSeconds;
        await transitionPresenter.PlayRoundEndAsync(currentRoundIndex, eliminated, roundTransitionFadeSeconds, roundResultSeconds, token);
    }

    private async UniTask PlayFinalResultTransitionAsync(
        GameParticipant finalWinner,
        IReadOnlyList<GameParticipant> rankedEliminated,
        CancellationToken token)
    {
        phaseEndTime = Time.time + roundResultSeconds * Mathf.Max(1, rankedEliminated.Count + 1)
            + roundTransitionFadeSeconds * 2f;
        await transitionPresenter.PlayMatchResultsAsync(
            finalWinner,
            rankedEliminated,
            roundTransitionFadeSeconds,
            roundResultSeconds,
            token);
    }

    private static void BroadcastRoundStateIfServer()
    {
        if (NetworkRoundController.HasInstance)
        {
            NetworkRoundController.Instance.BroadcastRoundStateNow();
        }
    }

    private void UpdateNetworkRoundResult(RoundStateBroadcast state)
    {
        // 반복 state broadcast로 같은 round-result 전환이 재생되지 않도록 클라이언트가 key를 사용합니다.
        if (CurrentPhase != GamePhase.RoundResult)
        {
            if (CurrentPhase == GamePhase.GameOver &&
                transitionPresenter.IsFinalResultActive)
            {
                return;
            }

            if (CurrentPhase != GamePhase.RoundStarting &&
                !networkRoundEndPlaying &&
                !networkRoundStartPlaying)
            {
                transitionPresenter.HideImmediate();
            }

            return;
        }

        if (state.WinnerOwner <= 0)
        {
            return;
        }

        int resultKey = state.WinnerOwner;
        unchecked
        {
            resultKey = resultKey * 397 ^ state.SecondPlaceOwner;
            resultKey = resultKey * 397 ^ state.ThirdPlaceOwner;
            resultKey = resultKey * 397 ^ state.FourthPlaceOwner;
        }
        if (lastShownNetworkResultKey == resultKey)
        {
            return;
        }

        lastShownNetworkResultKey = resultKey;
        GameParticipant finalWinner = ResolveResultParticipant(state.WinnerOwner, false);
        rankingRevealOrder.Clear();
        AddResultParticipant(rankingRevealOrder, state.FourthPlaceOwner);
        AddResultParticipant(rankingRevealOrder, state.ThirdPlaceOwner);
        AddResultParticipant(rankingRevealOrder, state.SecondPlaceOwner);

        CancelNetworkRoundEndTransition();
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        networkRoundEndCts = source;
        PlayNetworkRoundEndTransitionAsync(finalWinner, rankingRevealOrder, source).Forget();
    }

    private async UniTask PlayNetworkRoundEndTransitionAsync(
        GameParticipant finalWinner,
        IReadOnlyList<GameParticipant> rankedEliminated,
        CancellationTokenSource source)
    {
        try
        {
            networkRoundEndPlaying = true;
            await transitionPresenter.PlayMatchResultsAsync(
                finalWinner,
                rankedEliminated,
                roundTransitionFadeSeconds,
                roundResultSeconds,
                source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            networkRoundEndPlaying = false;
            if (networkRoundEndCts == source)
            {
                networkRoundEndCts = null;
                source.Dispose();
            }
        }
    }

    private void CancelNetworkRoundEndTransition()
    {
        if (networkRoundEndCts == null)
        {
            return;
        }

        CancellationTokenSource source = networkRoundEndCts;
        networkRoundEndCts = null;
        networkRoundEndPlaying = false;
        source.Cancel();
        source.Dispose();
    }

    private void PlayNetworkRoundStartTransitionIfNeeded(
        bool shouldPlay,
        int previousRoundIndex)
    {
        // round-start overlay는 클라이언트 표시용이며 새 Farming round마다 한 번만 재생되어야 합니다.
        if (!shouldPlay)
        {
            return;
        }

        int startKey = currentRoundIndex;
        if (lastShownNetworkStartKey == startKey)
        {
            return;
        }

        lastShownNetworkStartKey = startKey;
        CancelNetworkRoundStartTransition();
        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        networkRoundStartCts = source;
        networkRoundStartPlaying = true;
        PlayNetworkRoundStartTransitionAsync(
            currentRoundIndex,
            previousRoundIndex,
            source).Forget();
    }

    private async UniTask PlayNetworkRoundStartTransitionAsync(
        int roundIndex,
        int previousRoundIndex,
        CancellationTokenSource source)
    {
        try
        {
            if (previousRoundIndex > 0)
            {
                float waitDeadline = Time.realtimeSinceStartup
                    + Mathf.Max(0.5f, roundResultSeconds + roundTransitionFadeSeconds + 0.5f);
                while (networkRoundEndPlaying && Time.realtimeSinceStartup < waitDeadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, source.Token);
                }
            }

            await transitionPresenter.PlayRoundStartAsync(
                roundIndex,
                Mathf.Max(1.35f, nextRoundStartHoldSeconds),
                roundTransitionFadeSeconds,
                networkRoundStartReleaseGraceSeconds,
                null,
                source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            networkRoundStartPlaying = false;
            if (networkRoundStartCts == source)
            {
                networkRoundStartCts = null;
                source.Dispose();
            }
        }
    }

    private void CancelNetworkRoundStartTransition()
    {
        if (networkRoundStartCts == null)
        {
            return;
        }

        CancellationTokenSource source = networkRoundStartCts;
        networkRoundStartCts = null;
        networkRoundStartPlaying = false;
        source.Cancel();
        source.Dispose();
    }

    private async UniTask PlayRoundStartTransitionAsync(CancellationToken token)
    {
        await transitionPresenter.PlayRoundStartAsync(
            currentRoundIndex,
            Mathf.Max(1.35f, nextRoundStartHoldSeconds),
            roundTransitionFadeSeconds,
            networkRoundStartReleaseGraceSeconds,
            PrepareRoundBehindBlackAsync,
            token);
    }

    private void ClearParticipantStoredItems()
    {
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null || participant.Transform == null)
            {
                continue;
            }

            Player player = participant.Transform.GetComponent<Player>();
            if (player != null)
            {
                player.ServerClearStoredItemForRoundEnd();
                continue;
            }

            PlayerStoredItemController storedItem =
                participant.Transform.GetComponent<PlayerStoredItemController>();
            if (storedItem != null)
            {
                storedItem.ClearForRoundEnd();
            }
        }
    }

    private void AddResultParticipant(List<GameParticipant> destination, int ownerValue)
    {
        GameParticipant participant = ResolveResultParticipant(ownerValue, true);
        if (participant != null)
        {
            destination.Add(participant);
        }
    }

    private GameParticipant ResolveResultParticipant(int ownerValue, bool eliminated)
    {
        if (ownerValue <= 0)
        {
            return null;
        }

        GameParticipant participant = FindParticipant((PaintOwner)ownerValue);
        if (participant != null)
        {
            return participant;
        }

        return new GameParticipant
        {
            Owner = (PaintOwner)ownerValue,
            DisplayName = "P" + ownerValue,
            IsEliminated = eliminated
        };
    }

    private UniTask PrepareRoundBehindBlackAsync(CancellationToken token)
    {
        // The result stage has released the live characters, while the blackout
        // still covers both the map reset and the new farming spawn placement.
        BeginRound();
        return UniTask.CompletedTask;
    }

    private async UniTask WaitForPhaseEndAsync(CancellationToken token)
    {
        while (Time.time < phaseEndTime)
        {
            GameParticipant immediateWinner = FindTerritoryTargetWinner();
            if (immediateWinner != null)
            {
                winner = immediateWinner;
                break;
            }
            if (overtimeActive && TryGetSoleLeader(out GameParticipant overtimeWinner))
            {
                winner = overtimeWinner;
                break;
            }
            if (!finalCountdownActive &&
                CurrentPhase == GamePhase.Battle &&
                PhaseRemainingSeconds <= finalCountdownSeconds)
            {
                ActivateFinalCountdown();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }

    private GameParticipant FindTerritoryTargetWinner()
    {
        int target = TilePaintManager.HasInstance
            ? TilePaintManager.Instance.TerritoryRules.territoryTargetCount : 500;
        for (int i = 0; i < participants.Count; i++)
            if (participants[i] != null && !participants[i].IsEliminated && participants[i].CurrentTileCount >= target)
                return participants[i];
        return null;
    }

    private bool HasTopTie()
    {
        if (!TryGetSoleLeader(out _)) return true;
        return false;
    }

    private bool TryGetSoleLeader(out GameParticipant leader)
    {
        leader = null;
        int best = -1, ties = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant p = participants[i];
            if (p == null || p.IsEliminated) continue;
            int count = p.CurrentTileCount;
            if (count > best) { best = count; leader = p; ties = 1; }
            else if (count == best) ties++;
        }
        if (ties == 1) return true;
        leader = null;
        return false;
    }

    private void ActivateFinalCountdown()
    {
        if (finalCountdownActive)
        {
            return;
        }

        finalCountdownActive = true;
        if (ObstacleFieldManager.HasInstance)
        {
            ObstacleFieldManager.Instance.ServerDestroyAllForFinalCountdown();
        }

        ApplyFinalCountdownPresentation();
        BroadcastRoundStateIfServer();
        Debug.Log("[Round] Final countdown started. Occupation HUD hidden and obstacles cleared.");
    }

    private static void ApplyFinalCountdownPresentation()
    {
        if (ObstacleFieldManager.HasInstance)
        {
            ObstacleFieldManager.Instance.ApplyFinalCountdownState();
        }

        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.PlayFinalCountdownBgm();
        }
    }

    private static void RestoreRegularMatchPresentation()
    {
        if (CSE.SoundManager.Instance != null)
        {
            CSE.SoundManager.Instance.RestoreGameBgm();
        }
    }

    private void SetBoundaryVisible(bool isVisible)
    {
        if (boundaryView != null)
        {
            boundaryView.SetVisible(isVisible);
        }
    }

    private bool IsGridReady()
    {
        return TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady;
    }

    private int CountAliveParticipants()
    {
        int count = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && !participant.IsEliminated)
            {
                count++;
            }
        }

        return count;
    }

    private static string GetTileGridDebugStatus()
    {
        if (!TilePaintManager.HasInstance)
        {
            return "TilePaintManager=missing";
        }

        return TilePaintManager.Instance.DebugGridStatus;
    }

    private static void LogRoundFlow(string message)
    {
        Debug.Log(RoundFlowPrefix + message);
    }
}