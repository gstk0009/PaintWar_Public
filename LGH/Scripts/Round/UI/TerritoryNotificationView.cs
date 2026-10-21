using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 씬에서 제작한 영토 알림 UI에 게임 상태만 바인딩합니다.
public sealed class TerritoryNotificationView : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text recoveryTimerText;
    [SerializeField] private PaintOwner localOwner = PaintOwner.Player1;
    [SerializeField, Min(0.1f)] private float displaySeconds = 1.75f;
    [SerializeField, Min(0.1f)] private float sampleInterval = 0.1f;
    [SerializeField, Min(0f)] private float milestoneCooldownSeconds = 10f;
    [SerializeField, Min(0f)] private float mergeWindowSeconds = 0.5f;
    [SerializeField, Min(1)] private int minimumTerritoryChangeToNotify = 10;

    private readonly Queue<Notice> notices = new Queue<Notice>(8);
    private readonly int[] lastCounts = new int[5];
    private readonly float[] lastMilestoneAt = { 0f, -100f, -100f, -100f, -100f };
    private TilePaintManager subscribedManager;
    private Notice current;
    private TerritoryChange pendingChange;
    private bool hasPendingChange;
    private float pendingChangeEndsAt;
    private float currentEndsAt;
    private float nextSampleAt;
    private bool localOwnerResolved;

    private void OnEnable()
    {
        TrySubscribe();
        ResolveLocalOwner();
        SetVisible(false);
    }

    private void OnDisable()
    {
        if (subscribedManager != null) subscribedManager.TerritoryChanged -= OnTerritoryChanged;
        subscribedManager = null;
    }

    private void Update()
    {
        TrySubscribe();
        if (!localOwnerResolved) ResolveLocalOwner();
        if (subscribedManager == null) return;

        float now = Time.unscaledTime;
        if (hasPendingChange && now >= pendingChangeEndsAt) FlushPendingChange();
        if (now >= nextSampleAt)
        {
            nextSampleAt = now + sampleInterval;
            SampleMilestones();
        }
        if (currentEndsAt > now)
        {
            if (recoveryTimerText != null && current.ShowRecoveryTimer)
                recoveryTimerText.SetText("{0:0.0}", Mathf.Max(0f, currentEndsAt - now));
            return;
        }
        if (notices.Count == 0) { SetVisible(false); return; }
        current = notices.Dequeue();
        currentEndsAt = now + (current.ShowRecoveryTimer
            ? subscribedManager.TerritoryRules.isolationGraceSeconds : displaySeconds);
        if (messageText != null) messageText.text = current.Message;
        if (recoveryTimerText != null) recoveryTimerText.gameObject.SetActive(current.ShowRecoveryTimer);
        SetVisible(true);
    }

    private void TrySubscribe()
    {
        if (subscribedManager != null || !TilePaintManager.HasInstance) return;
        subscribedManager = TilePaintManager.Instance;
        subscribedManager.TerritoryChanged += OnTerritoryChanged;
    }

    private void ResolveLocalOwner()
    {
        IReadOnlyList<PlayerPaintIdentity> players = PlayerPaintIdentity.ActivePlayers;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerPaintIdentity identity = players[i];
            Player player = identity != null ? identity.GetComponent<Player>() : null;
            if (player != null && player.IsOwner)
            {
                localOwner = identity.Owner;
                localOwnerResolved = localOwner != PaintOwner.Neutral;
                return;
            }
        }
    }

    private void OnTerritoryChanged(TerritoryChange change)
    {
        if (change.Owner == localOwner && change.Kind == TerritoryChangeKind.Restored)
        {
            hasPendingChange = false;
            notices.Clear();
            currentEndsAt = 0f;
            SetVisible(false);
        }
        if (hasPendingChange && pendingChange.Owner == change.Owner && pendingChange.Kind == change.Kind &&
            Time.unscaledTime < pendingChangeEndsAt)
        {
            pendingChange.TileCount += change.TileCount;
            return;
        }
        if (hasPendingChange) FlushPendingChange();
        pendingChange = change;
        hasPendingChange = true;
        pendingChangeEndsAt = Time.unscaledTime + mergeWindowSeconds;
    }

    private void FlushPendingChange()
    {
        TerritoryChange change = pendingChange;
        hasPendingChange = false;
        if (change.TileCount < Mathf.Max(1, minimumTerritoryChangeToNotify)) return;
        string verb = change.Kind == TerritoryChangeKind.Isolated ? "고립" :
            change.Kind == TerritoryChangeKind.Restored ? "복구" : "붕괴";
        bool ownIsolation = change.Owner == localOwner && change.Kind == TerritoryChangeKind.Isolated;
        string message = OwnerName(change.Owner) + " 영토 " + change.TileCount + "칸 " + verb + "!";
        if (ownIsolation) message += "\n연결을 복구하세요";
        notices.Enqueue(new Notice(message, ownIsolation));
    }

    private void SampleMilestones()
    {
        int target = subscribedManager.TerritoryRules.territoryTargetCount;
        int first = Mathf.RoundToInt(target * 0.8f);
        int urgent = Mathf.RoundToInt(target * 0.9f);
        for (int i = 1; i <= 4; i++)
        {
            PaintOwner owner = (PaintOwner)i;
            int count = subscribedManager.CountConnectedTerritory(owner);
            bool crossed = (lastCounts[i] < first && count >= first) ||
                           (lastCounts[i] < urgent && count >= urgent);
            if (crossed && Time.unscaledTime - lastMilestoneAt[i] >= milestoneCooldownSeconds)
            {
                notices.Enqueue(new Notice(OwnerName(owner) + " " + count + " / " + target + " 도달!", false));
                lastMilestoneAt[i] = Time.unscaledTime;
            }
            lastCounts[i] = count;
        }
    }

    private void SetVisible(bool visible)
    {
        if (panel == null) return;
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = false;
        panel.blocksRaycasts = false;
    }

    private static string OwnerName(PaintOwner owner)
    {
        switch (owner)
        {
            case PaintOwner.Player1: return "파랑";
            case PaintOwner.Player2: return "빨강";
            case PaintOwner.Player3: return "초록";
            case PaintOwner.Player4: return "노랑";
            default: return "중립";
        }
    }

    private struct Notice
    {
        public readonly string Message;
        public readonly bool ShowRecoveryTimer;
        public Notice(string message, bool showRecoveryTimer)
        { Message = message; ShowRecoveryTimer = showRecoveryTimer; }
    }
}
