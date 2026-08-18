using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameplayEmojiHud : MonoBehaviour
{
    private static GameplayEmojiHud instance;
    private const int FirstPlayerOwnerIndex = (int)PaintOwner.Player1;
    private const int LastPlayerOwnerIndex = (int)PaintOwner.Player4;

    [SerializeField] private LobbyEmojiCatalog catalog;
    [SerializeField] private RoundHudView roundHud;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private Image[] slotImages;
    [SerializeField] private float visibleSeconds = 2.2f;
    [SerializeField] private float useCooldownSeconds = 0.5f;

    private readonly UnityEngine.Events.UnityAction[] slotActions =
        new UnityEngine.Events.UnityAction[LobbyEmojiLoadoutStore.SlotCount];

    private PaintOwner localOwner = PaintOwner.Neutral;
    private readonly OwnerEmojiState[] emojiStates =
        new OwnerEmojiState[LastPlayerOwnerIndex - FirstPlayerOwnerIndex + 1];
    private float nextUseAllowedAt;
    private float nextOwnerResolveAt;
    private bool initialized;

    private void Awake()
    {
        if (!EnsureInitialized(true))
        {
            enabled = false;
            return;
        }

        instance = this;
    }

    private void OnEnable()
    {
        if (!EnsureInitialized(false))
        {
            return;
        }

        instance = this;
        LobbyEmojiLoadoutStore.Changed += RefreshSlotImages;
        RefreshSlotImages();
        ResolveLocalOwner(true);
    }

    private void OnDisable()
    {
        LobbyEmojiLoadoutStore.Changed -= RefreshSlotImages;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (slotButtons == null)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Length && i < slotActions.Length; i++)
        {
            if (slotButtons[i] != null && slotActions[i] != null)
            {
                slotButtons[i].onClick.RemoveListener(slotActions[i]);
            }
        }
    }

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < emojiStates.Length; i++)
        {
            OwnerEmojiState state = emojiStates[i];
            if (state == null || !state.IsActive)
            {
                continue;
            }

            if (now >= state.HideAt)
            {
                HideEmoji(state);
                continue;
            }

            if (now < state.NextRefreshAt)
            {
                continue;
            }

            state.NextRefreshAt = now + 0.1f;
            RefreshEmojiBarBinding(state);
        }
    }

    private void BindButtons()
    {
        if (slotButtons == null)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Length && i < slotActions.Length; i++)
        {
            int slotIndex = i;
            slotActions[i] = () => UseSlot(slotIndex);
            if (slotButtons[i] != null)
            {
                slotButtons[i].onClick.AddListener(slotActions[i]);
            }
        }
    }

    private void UseSlot(int slotIndex)
    {
        if (Time.unscaledTime < nextUseAllowedAt ||
            catalog == null ||
            !LobbyEmojiLoadoutStore.TryGetEquippedEntry(
                slotIndex,
                catalog,
                out LobbyEmojiCatalog.EmojiEntry entry))
        {
            return;
        }

        nextUseAllowedAt = Time.unscaledTime + Mathf.Max(0.1f, useCooldownSeconds);
        ResolveLocalOwner(true);
        bool submitted = Player.SubmitLocalGameplayEmoji(entry.Id);
        if (localOwner != PaintOwner.Neutral)
        {
            ShowEmojiForOwner(localOwner, entry.Sprite);
        }
        else if (!submitted)
        {
            ResolveLocalOwner(true);
            if (localOwner != PaintOwner.Neutral)
            {
                ShowEmojiForOwner(localOwner, entry.Sprite);
            }
        }
    }

    public static void ShowNetworkEmoji(PaintOwner owner, string emojiId)
    {
        GameplayEmojiHud activeInstance = ResolveActiveInstance();
        if (activeInstance == null)
        {
            return;
        }

        activeInstance.ShowEmojiForOwner(owner, emojiId);
    }

    private static GameplayEmojiHud ResolveActiveInstance()
    {
        if (IsUsableInstance(instance))
        {
            return instance;
        }

        GameplayEmojiHud[] huds = FindObjectsByType<GameplayEmojiHud>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < huds.Length; i++)
        {
            GameplayEmojiHud hud = huds[i];
            if (IsUsableInstance(hud))
            {
                instance = hud;
                return hud;
            }
        }

        instance = null;
        return null;
    }

    private static bool IsUsableInstance(GameplayEmojiHud hud)
    {
        return hud != null &&
            hud.isActiveAndEnabled &&
            hud.EnsureInitialized(false);
    }

    private bool EnsureInitialized(bool logError)
    {
        if (initialized)
        {
            return true;
        }

        if (catalog == null || roundHud == null)
        {
            if (logError)
            {
                Debug.LogError(
                    "[GameplayEmojiHud] Catalog and RoundHud references must be assigned in the scene.",
                    this);
            }

            return false;
        }

        InitializeEmojiStates();
        BindButtons();
        HideAllEmojiImmediate();
        RefreshSlotImages();
        initialized = true;
        return true;
    }

    private void ShowEmojiForOwner(PaintOwner owner, string emojiId)
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (catalog == null ||
            string.IsNullOrWhiteSpace(emojiId) ||
            !catalog.TryGetSprite(emojiId.Trim(), out Sprite sprite))
        {
            return;
        }

        ShowEmojiForOwner(owner, sprite);
    }

    private void ShowEmojiForOwner(PaintOwner owner, Sprite sprite)
    {
        OwnerEmojiState state = GetEmojiState(owner);
        if (state == null || sprite == null)
        {
            return;
        }

        state.Sprite = sprite;
        state.HideAt = Time.unscaledTime + Mathf.Max(0.1f, visibleSeconds);
        state.NextRefreshAt = 0f;
        state.IsActive = true;
        RefreshEmojiBarBinding(state);
    }

    private void InitializeEmojiStates()
    {
        for (int ownerIndex = FirstPlayerOwnerIndex; ownerIndex <= LastPlayerOwnerIndex; ownerIndex++)
        {
            int stateIndex = ownerIndex - FirstPlayerOwnerIndex;
            if (emojiStates[stateIndex] == null)
            {
                emojiStates[stateIndex] = new OwnerEmojiState
                {
                    Owner = (PaintOwner)ownerIndex
                };
            }
        }
    }

    private OwnerEmojiState GetEmojiState(PaintOwner owner)
    {
        int ownerIndex = (int)owner;
        if (ownerIndex < FirstPlayerOwnerIndex ||
            ownerIndex > LastPlayerOwnerIndex)
        {
            return null;
        }

        return emojiStates[ownerIndex - FirstPlayerOwnerIndex];
    }

    private void HideAllEmojiImmediate()
    {
        for (int i = 0; i < emojiStates.Length; i++)
        {
            HideEmoji(emojiStates[i]);
        }
    }

    private static void HideEmoji(OwnerEmojiState state)
    {
        if (state == null)
        {
            return;
        }

        state.IsActive = false;
        state.Sprite = null;
        state.NextRefreshAt = 0f;
        if (state.Bar != null)
        {
            state.Bar.HideEmoji(state.Owner);
            state.Bar = null;
        }
    }

    private void RefreshSlotImages()
    {
        if (catalog == null || slotImages == null)
        {
            return;
        }

        for (int i = 0; i < slotImages.Length; i++)
        {
            Image image = slotImages[i];
            bool hasEntry = LobbyEmojiLoadoutStore.TryGetEquippedEntry(
                i,
                catalog,
                out LobbyEmojiCatalog.EmojiEntry entry);

            if (image != null)
            {
                image.sprite = hasEntry ? entry.Sprite : null;
                image.enabled = hasEntry && entry.Sprite != null;
            }

            if (slotButtons != null && i < slotButtons.Length && slotButtons[i] != null)
            {
                slotButtons[i].interactable = hasEntry;
            }
        }
    }

    private void RefreshEmojiBarBinding(OwnerEmojiState state)
    {
        if (state == null ||
            roundHud == null ||
            state.Owner == PaintOwner.Neutral ||
            state.Sprite == null)
        {
            return;
        }

        if (!roundHud.TryGetBarForOwner(state.Owner, out PlayerHUDBar bar))
        {
            if (state.Bar != null)
            {
                state.Bar.HideEmoji(state.Owner);
                state.Bar = null;
            }

            return;
        }

        if (state.Bar != null && state.Bar != bar)
        {
            state.Bar.HideEmoji(state.Owner);
        }

        state.Bar = bar;
        bar.ShowEmoji(state.Owner, state.Sprite);
    }

    private void ResolveLocalOwner(bool force)
    {
        if (!force && localOwner != PaintOwner.Neutral && Time.unscaledTime < nextOwnerResolveAt)
        {
            return;
        }

        nextOwnerResolveAt = Time.unscaledTime + 0.5f;
        if (TryResolveSingleHumanParticipant(out PaintOwner owner) ||
            TryResolveFromLocalPlayer(out owner) ||
            TryResolveFromLocalMovement(out owner) ||
            TryResolveFromRoundParticipants(out owner))
        {
            localOwner = owner;
        }
    }

    private static bool TryResolveSingleHumanParticipant(out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        if (!GameRoundManager.HasInstance)
        {
            return false;
        }

        System.Collections.Generic.IReadOnlyList<GameParticipant> participants =
            GameRoundManager.Instance.Participants;
        int humanCount = 0;
        PaintOwner onlyHumanOwner = PaintOwner.Neutral;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null &&
                !participant.IsBot &&
                participant.Owner != PaintOwner.Neutral)
            {
                humanCount++;
                onlyHumanOwner = participant.Owner;
            }
        }

        if (humanCount != 1)
        {
            return false;
        }

        owner = onlyHumanOwner;
        return true;
    }

    private static bool TryResolveFromLocalPlayer(out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        Player[] players = FindObjectsByType<Player>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            PlayerPaintIdentity identity =
                player.GetComponent<PlayerPaintIdentity>();
            if (identity != null && identity.Owner != PaintOwner.Neutral)
            {
                owner = identity.Owner;
                return true;
            }

            PaintOwner playerOwner = player.myPaintOwner.Value;
            if (playerOwner != PaintOwner.Neutral)
            {
                owner = playerOwner;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromLocalMovement(out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        PlayerMovement[] movements = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerMovement movement = movements[i];
            if (movement == null || !movement.IsOwner)
            {
                continue;
            }

            PlayerPaintIdentity identity = movement.GetComponent<PlayerPaintIdentity>();
            if (identity != null && identity.Owner != PaintOwner.Neutral)
            {
                owner = identity.Owner;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromRoundParticipants(out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        if (!GameRoundManager.HasInstance)
        {
            return false;
        }

        System.Collections.Generic.IReadOnlyList<GameParticipant> participants =
            GameRoundManager.Instance.Participants;
        int humanCount = 0;
        PaintOwner onlyHumanOwner = PaintOwner.Neutral;
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant != null && !participant.IsBot && participant.Owner != PaintOwner.Neutral)
            {
                humanCount++;
                onlyHumanOwner = participant.Owner;
            }
        }

        if (humanCount == 1)
        {
            owner = onlyHumanOwner;
            return true;
        }

        return false;
    }

    private sealed class OwnerEmojiState
    {
        public PaintOwner Owner;
        public Sprite Sprite;
        public PlayerHUDBar Bar;
        public float HideAt;
        public float NextRefreshAt;
        public bool IsActive;
    }
}
