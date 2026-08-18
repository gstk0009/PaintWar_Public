using System.Collections;
using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;
using UnityEngine;

public class LobbyPlayer : NetworkBehaviour
{
    private const string ScenePreviewName = "LobbyCurrentCharacterPreview";

    private static readonly Bounds CharacterRenderBounds =
        new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(5f, 5f, 5f));

    public readonly SyncVar<int> roomIndex = new SyncVar<int>(-1);

    public readonly SyncVar<double> animStartServerTime = new SyncVar<double>(0d);

    public readonly SyncVar<string> playerName = new SyncVar<string>(string.Empty);

    public readonly SyncVar<bool> isLobbyLeader = new SyncVar<bool>(false);

    public readonly SyncVar<string> appearanceCode = new SyncVar<string>(string.Empty);

    public readonly SyncVar<string> motionCode = new SyncVar<string>(string.Empty);

    [Header("아바타 렌더러 및 애니메이터 설정")]
    [SerializeField] private Renderer myRenderer;
    [SerializeField] private Animator myAnimator;
    [SerializeField] private CharacterMotionApplicator motionApplicator;

    [Header("닉네임 UI 설정")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("대기실 설정")]
    [SerializeField] private Color lobbyWaitingTint = Color.white;
    [SerializeField] private Material lobbyWaitingTintMaterial;
    [SerializeField] private string idleAnimationState = "Idle_Look_Around";
    [SerializeField] private float idleAnimationLength = 3.0f;

    [Header("Lobby Emoji")]
    [SerializeField] private LobbyEmojiCatalog emojiCatalog;
    [SerializeField] private LobbyEmojiBubble emojiBubble;

    [Header("Lobby Placement")]
    [SerializeField] private float lobbyRootHeight = 0.3f;

    private static LobbyPlayer localOwnedLobbyPlayer;

    private Coroutine revealFallbackCoroutine;
    private Coroutine lobbyHeightCorrectionCoroutine;
    private bool hasRevealedPresentation;

    private void Awake()
    {
        if (IsScenePreviewObject())
        {
            enabled = false;
            return;
        }

        roomIndex.OnChange += OnRoomIndexChanged;

        playerName.OnChange += OnPlayerNameChanged;

        appearanceCode.OnChange += OnAppearanceCodeChanged;
        motionCode.OnChange += OnMotionCodeChanged;
        ConfigureAnimatorCulling();
        SetClientPresentation(false);
    }

    private void OnDestroy()
    {
        roomIndex.OnChange -= OnRoomIndexChanged;
        playerName.OnChange -= OnPlayerNameChanged;
        appearanceCode.OnChange -= OnAppearanceCodeChanged;
        motionCode.OnChange -= OnMotionCodeChanged;

        if (localOwnedLobbyPlayer == this)
        {
            localOwnedLobbyPlayer = null;
        }

        if (revealFallbackCoroutine != null)
        {
            StopCoroutine(revealFallbackCoroutine);
            revealFallbackCoroutine = null;
        }

        if (lobbyHeightCorrectionCoroutine != null)
        {
            StopCoroutine(lobbyHeightCorrectionCoroutine);
            lobbyHeightCorrectionCoroutine = null;
        }
    }

    public override void OnStartServer()
    {
        if (IsScenePreviewObject())
        {
            return;
        }

        base.OnStartServer();

        ApplyLobbyRootHeight();

        animStartServerTime.Value = TimeManager.TicksToTime(TimeManager.LocalTick);

        playerName.Value = $"Player {OwnerId + 1}";
    }

    public override void OnStartClient()
    {
        if (IsScenePreviewObject())
        {
            return;
        }

        base.OnStartClient();

        StartLobbyHeightCorrection();

        if (emojiCatalog == null)
        {
            emojiCatalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (emojiBubble == null)
        {
            emojiBubble = GetComponentInChildren<LobbyEmojiBubble>(true);
        }

        StartCoroutine(SyncAnimationProgressRoutine());

        if (nameText != null)
        {
            nameText.text = ResolveDisplayName(playerName.Value);
        }

        string initialAppearanceCode =
            CharacterAppearanceStore.NormalizeCode(appearanceCode.Value);
        if (!string.IsNullOrWhiteSpace(initialAppearanceCode))
        {
            ApplyAppearance(initialAppearanceCode);
            RevealPresentation();
        }
        else
        {
            revealFallbackCoroutine = StartCoroutine(RevealWhenInitialSyncSettles());
        }

        if (IsOwner)
        {
            localOwnedLobbyPlayer = this;
            SubmitLocalProfileToServer();
        }
        else
        {
            ApplyMotion(motionCode.Value);
        }
    }

    public override void OnStopClient()
    {
        if (IsScenePreviewObject())
        {
            return;
        }

        base.OnStopClient();

        if (localOwnedLobbyPlayer == this)
        {
            localOwnedLobbyPlayer = null;
        }
    }

    private bool IsScenePreviewObject()
    {
        return name.StartsWith(ScenePreviewName, StringComparison.Ordinal);
    }

    private IEnumerator SyncAnimationProgressRoutine()
    {
        yield return new WaitForSeconds(0.1f);

        if (myAnimator == null ||
            myAnimator.runtimeAnimatorController == null ||
            string.IsNullOrWhiteSpace(idleAnimationState))
        {
            yield break;
        }

        int idleStateHash = Animator.StringToHash(idleAnimationState);
        if (!myAnimator.HasState(0, idleStateHash))
        {
            Debug.LogWarning(
                $"[Lobby] Animator state '{idleAnimationState}' was not found on {name}.");
            yield break;
        }

        double currentServerTime = TimeManager.TicksToTime(TimeManager.LocalTick);
        double elapsedTime = currentServerTime - animStartServerTime.Value;

        if (elapsedTime < 0) elapsedTime = 0;

        float animationLength = Mathf.Max(0.01f, idleAnimationLength);
        float remainderTime = (float)(elapsedTime % animationLength);
        float normalizedTime = remainderTime / animationLength;

        myAnimator.Play(idleStateHash, 0, normalizedTime);
    }

    private void OnRoomIndexChanged(int prev, int next, bool asServer)
    {
        ApplyLobbyRootHeight();
        ApplyCosmeticTint();

        if (asServer)
        {
            playerName.Value = $"Player {next + 1}";
        }
    }

    private void OnPlayerNameChanged(string prev, string next, bool asServer)
    {
        if (nameText != null)
        {
            nameText.text = ResolveDisplayName(next);
        }
    }

    private void StartLobbyHeightCorrection()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (lobbyHeightCorrectionCoroutine != null)
        {
            StopCoroutine(lobbyHeightCorrectionCoroutine);
        }

        lobbyHeightCorrectionCoroutine = StartCoroutine(ApplyLobbyRootHeightRoutine());
    }

    private IEnumerator ApplyLobbyRootHeightRoutine()
    {
        for (int i = 0; i < 6; i++)
        {
            ApplyLobbyRootHeight();
            yield return null;
        }

        lobbyHeightCorrectionCoroutine = null;
    }

    private void ApplyLobbyRootHeight()
    {
        if (NetworkObject == null || NetworkObject.IsSceneObject)
        {
            return;
        }

        float targetHeight = Mathf.Max(0f, lobbyRootHeight);
        Vector3 position = transform.position;
        if (Mathf.Abs(position.y - targetHeight) <= 0.001f)
        {
            return;
        }

        position.y = targetHeight;
        transform.position = position;
    }

    private void OnAppearanceCodeChanged(string prev, string next, bool asServer)
    {
        if (IsOwner)
        {
            CharacterAppearanceStore.SetEquipped(next);
        }

        ApplyAppearance(next);
        RevealPresentation();
    }

    private void OnMotionCodeChanged(string prev, string next, bool asServer)
    {
        if (IsOwner)
        {
            CharacterMotionStore.SetEquipped(next);
        }

        ApplyMotion(next);
    }

    private void ApplyAppearance(string code)
    {
        CharacterAppearanceApplicator applicator =
            GetComponent<CharacterAppearanceApplicator>();
        if (applicator == null)
        {
            applicator = gameObject.AddComponent<CharacterAppearanceApplicator>();
        }

        applicator.ApplyAppearance(CharacterAppearanceStore.NormalizeCode(code));
        ApplyCosmeticTint(applicator);
        SetClientPresentation(true);
    }

    private void ApplyCosmeticTint(CharacterAppearanceApplicator applicator = null)
    {
        if (applicator == null)
        {
            applicator = GetComponent<CharacterAppearanceApplicator>();
        }

        if (applicator == null)
        {
            return;
        }

        applicator.SetCosmeticTint(
            lobbyWaitingTint,
            lobbyWaitingTintMaterial);
    }

    public static void SubmitLocalProfileToOwnedLobbyPlayer()
    {
        LobbyPlayer[] lobbyPlayers = FindObjectsByType<LobbyPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < lobbyPlayers.Length; i++)
        {
            LobbyPlayer lobbyPlayer = lobbyPlayers[i];
            if (lobbyPlayer != null && lobbyPlayer.IsOwner)
            {
                lobbyPlayer.SubmitLocalProfileToServer();
            }
        }
    }

    public static void SubmitLocalEmoji(string emojiId)
    {
        if (string.IsNullOrWhiteSpace(emojiId) || localOwnedLobbyPlayer == null)
        {
            return;
        }

        localOwnedLobbyPlayer.ServerSubmitEmoji(emojiId.Trim());
    }

    private void SubmitLocalProfileToServer()
    {
        string nickname = PlayerProfile.GetOrCreateNickname();
        string localAppearanceCode =
            CharacterAppearanceStore.NormalizeCode(CharacterAppearanceStore.CurrentCode);
        string localMotionCode =
            CharacterMotionStore.NormalizeProfileMotionCode(
                CharacterMotionStore.GetCurrentProfileMotionCode());
        string localSelectedSkillId = PlayerSkillLoadoutStore.CurrentSkillId;

        ApplyLocalProfile(nickname, localAppearanceCode, localMotionCode);
        ServerSubmitProfile(
            nickname,
            localAppearanceCode,
            localMotionCode,
            localSelectedSkillId);
    }

    [ServerRpc]
    private void ServerSubmitProfile(
        string nickname,
        string profileAppearanceCode,
        string profileMotionCode,
        string profileSelectedSkillId,
        NetworkConnection sender = null)
    {
        NetworkConnection profileConnection = sender ?? Owner;
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.ServerApplyPlayerProfile(
                profileConnection,
                nickname,
                profileAppearanceCode,
                profileMotionCode,
                profileSelectedSkillId);
            return;
        }

        playerName.Value = ResolveDisplayName(nickname);
        appearanceCode.Value = CharacterAppearanceStore.NormalizeCode(profileAppearanceCode);
        motionCode.Value = CharacterMotionStore.NormalizeProfileMotionCode(profileMotionCode);
    }

    [ServerRpc]
    private void ServerSubmitEmoji(
        string emojiId,
        NetworkConnection sender = null)
    {
        if (sender != null && sender != Owner)
        {
            return;
        }

        if (emojiCatalog == null)
        {
            emojiCatalog = LobbyEmojiCatalog.LoadDefault();
        }

        string normalizedEmojiId = string.IsNullOrWhiteSpace(emojiId)
            ? string.Empty
            : emojiId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmojiId) ||
            emojiCatalog == null ||
            !emojiCatalog.Contains(normalizedEmojiId))
        {
            return;
        }

        ShowEmojiObserversRpc(normalizedEmojiId);
    }

    [ObserversRpc]
    private void ShowEmojiObserversRpc(string emojiId)
    {
        ShowEmoji(emojiId);
    }

    private void ApplyLocalProfile(
        string nickname,
        string profileAppearanceCode,
        string profileMotionCode)
    {
        if (nameText != null)
        {
            nameText.text = ResolveDisplayName(nickname);
        }

        CharacterAppearanceStore.SetEquipped(profileAppearanceCode);
        ApplyAppearance(profileAppearanceCode);
        CharacterMotionStore.SetEquippedProfile(profileMotionCode);
        ApplyMotion(profileMotionCode);
        RevealPresentation();
    }

    private void ApplyMotion(string profileMotionCode)
    {
        if (motionApplicator == null)
        {
            motionApplicator = GetComponent<CharacterMotionApplicator>();
        }

        if (motionApplicator == null)
        {
            motionApplicator = gameObject.AddComponent<CharacterMotionApplicator>();
        }

        motionApplicator.ApplyProfileMotion(profileMotionCode);
    }

    private void ShowEmoji(string emojiId)
    {
        if (emojiBubble == null)
        {
            emojiBubble = GetComponentInChildren<LobbyEmojiBubble>(true);
        }

        if (emojiBubble != null)
        {
            emojiBubble.Show(emojiId);
        }
    }

    private IEnumerator RevealWhenInitialSyncSettles()
    {
        yield return new WaitForSeconds(0.25f);

        revealFallbackCoroutine = null;
        if (hasRevealedPresentation)
        {
            yield break;
        }

        ApplyAppearance(appearanceCode.Value);
        RevealPresentation();
    }

    private void RevealPresentation()
    {
        if (revealFallbackCoroutine != null)
        {
            StopCoroutine(revealFallbackCoroutine);
            revealFallbackCoroutine = null;
        }

        hasRevealedPresentation = true;
        SetClientPresentation(true);
    }

    private string ResolveDisplayName(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !CharacterAppearanceStore.HasSelections(value) &&
            !CharacterAppearanceStore.IsCosmeticNameOrId(value))
        {
            return value;
        }

        int slotNumber = roomIndex.Value >= 0 ? roomIndex.Value + 1 : OwnerId + 1;
        return "Player " + Mathf.Max(1, slotNumber);
    }

    private void SetClientPresentation(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null)
            {
                renderer.enabled = visible;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    skinnedRenderer.updateWhenOffscreen = true;
                    skinnedRenderer.localBounds = CharacterRenderBounds;
                }
            }
        }

        if (nameText != null)
        {
            nameText.enabled = visible;
        }
    }

    private void ConfigureAnimatorCulling()
    {
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
            {
                animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }
    }
}
