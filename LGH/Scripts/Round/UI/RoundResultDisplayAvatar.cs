using System.Collections.Generic;
using FishNet.Component.Animating;
using FishNet.Component.Transforming;
using UnityEngine;

// Editor-authored stage anchor which temporarily presents the real gameplay player.
// It never creates or skins a second character.
[DisallowMultipleComponent]
public sealed class RoundResultDisplayAvatar : MonoBehaviour
{
    private static readonly Dictionary<Transform, RoundResultDisplayAvatar> BoundPlayers =
        new Dictionary<Transform, RoundResultDisplayAvatar>(4);

    [SerializeField] private RuntimeAnimatorController presentationController;

    private Transform liveTransform;
    private Animator liveAnimator;
    private AnimatorOverrideController presentationOverrideController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> presentationOverrides =
        new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private PlayerMovement playerMovement;
    private BotPlayerController botController;
    private NetworkBotController networkBotController;
    private NetworkTransform networkTransform;
    private NetworkAnimator networkAnimator;
    private CharacterController characterController;
    private Rigidbody rigidbodyComponent;

    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private Vector3 savedLocalScale;
    private string liveMotionCode;
    private RuntimeAnimatorController savedAnimatorController;
    private AnimatorCullingMode savedAnimatorCullingMode;
    private bool savedApplyRootMotion;
    private bool savedPlayerMovementEnabled;
    private bool savedBotControllerEnabled;
    private bool savedNetworkBotControllerEnabled;
    private bool savedNetworkTransformEnabled;
    private bool savedNetworkAnimatorEnabled;
    private bool savedCharacterControllerEnabled;
    private bool savedRigidbodyKinematic;
    private bool savedRigidbodyDetectCollisions;
    private bool savedRigidbodyUseGravity;
    private Vector3 savedRigidbodyLinearVelocity;
    private Vector3 savedRigidbodyAngularVelocity;
    private int loopingVictoryStateHash;
    private bool loopWinnerPose;

    public bool HasLivePlayer { get { return liveTransform != null; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBoundPlayers()
    {
        BoundPlayers.Clear();
    }

    public static void UpdatePendingGameplayPose(
        Transform playerTransform,
        Vector3 position,
        Quaternion rotation)
    {
        if (playerTransform == null ||
            !BoundPlayers.TryGetValue(playerTransform, out RoundResultDisplayAvatar avatar) ||
            avatar == null ||
            avatar.liveTransform != playerTransform)
        {
            return;
        }

        avatar.savedPosition = position;
        avatar.savedRotation = rotation;
    }

    private void LateUpdate()
    {
        if (liveTransform != null && liveTransform.gameObject.activeInHierarchy)
        {
            liveTransform.SetPositionAndRotation(transform.position, transform.rotation);
            KeepWinnerPoseLooping();
        }
    }

    private void OnDestroy()
    {
        Release();
    }

    public bool Bind(GameParticipant participant)
    {
        Player livePlayer = ResolveLivePlayer(participant);
        if (livePlayer == null || !livePlayer.gameObject.activeInHierarchy)
        {
            Release();
            return false;
        }

        if (liveTransform == livePlayer.transform)
        {
            liveTransform.SetPositionAndRotation(transform.position, transform.rotation);
            loopWinnerPose = false;
            PlayStateIfAvailable("Idle");
            return true;
        }

        Release();
        CacheLivePlayer(livePlayer);
        PauseGameplayState();
        liveTransform.SetPositionAndRotation(transform.position, transform.rotation);
        ConfigurePresentationAnimator();
        loopWinnerPose = false;
        PlayStateIfAvailable("Idle");
        return true;
    }

    public void Release()
    {
        if (liveTransform == null)
        {
            return;
        }

        Transform releasedTransform = liveTransform;
        liveTransform = null;
        if (BoundPlayers.TryGetValue(releasedTransform, out RoundResultDisplayAvatar avatar) &&
            avatar == this)
        {
            BoundPlayers.Remove(releasedTransform);
        }

        releasedTransform.SetPositionAndRotation(savedPosition, savedRotation);
        releasedTransform.localScale = savedLocalScale;

        if (liveAnimator != null)
        {
            liveAnimator.runtimeAnimatorController = savedAnimatorController;
            liveAnimator.applyRootMotion = savedApplyRootMotion;
            liveAnimator.cullingMode = savedAnimatorCullingMode;
            if (liveAnimator.gameObject.activeInHierarchy)
            {
                liveAnimator.Rebind();
                liveAnimator.Update(0f);
            }
        }

        RestoreBehaviour(playerMovement, savedPlayerMovementEnabled);
        RestoreBehaviour(botController, savedBotControllerEnabled);
        RestoreBehaviour(networkBotController, savedNetworkBotControllerEnabled);
        RestoreBehaviour(networkAnimator, savedNetworkAnimatorEnabled);
        RestoreBehaviour(networkTransform, savedNetworkTransformEnabled);

        if (characterController != null)
        {
            characterController.enabled = savedCharacterControllerEnabled;
        }

        if (rigidbodyComponent != null)
        {
            rigidbodyComponent.isKinematic = savedRigidbodyKinematic;
            rigidbodyComponent.detectCollisions = savedRigidbodyDetectCollisions;
            rigidbodyComponent.useGravity = savedRigidbodyUseGravity;
            if (!savedRigidbodyKinematic)
            {
                rigidbodyComponent.linearVelocity = savedRigidbodyLinearVelocity;
                rigidbodyComponent.angularVelocity = savedRigidbodyAngularVelocity;
            }
        }

        ClearCachedComponents();
    }

    public void PlayEliminatedPose()
    {
        loopWinnerPose = false;
        if (!PlayStateIfAvailable("Eliminated"))
        {
            PlayStateIfAvailable("Defeated");
        }
    }

    public void PlayWinnerPose()
    {
        loopWinnerPose = PlayStateIfAvailable("Victory");
    }

    private void CacheLivePlayer(Player livePlayer)
    {
        if (BoundPlayers.TryGetValue(livePlayer.transform, out RoundResultDisplayAvatar previous) &&
            previous != null &&
            previous != this)
        {
            previous.Release();
        }

        liveTransform = livePlayer.transform;
        BoundPlayers[liveTransform] = this;
        savedPosition = liveTransform.position;
        savedRotation = liveTransform.rotation;
        savedLocalScale = liveTransform.localScale;
        liveMotionCode = livePlayer.motionCode.Value;
        if (string.IsNullOrWhiteSpace(liveMotionCode) &&
            livePlayer.TryGetComponent(out CharacterMotionApplicator motionApplicator))
        {
            liveMotionCode = motionApplicator.AppliedProfileMotionCode;
        }

        liveAnimator = livePlayer.GetComponent<Animator>();
        playerMovement = livePlayer.GetComponent<PlayerMovement>();
        botController = livePlayer.GetComponent<BotPlayerController>();
        networkBotController = livePlayer.GetComponent<NetworkBotController>();
        networkTransform = livePlayer.GetComponent<NetworkTransform>();
        networkAnimator = livePlayer.GetComponent<NetworkAnimator>();
        characterController = livePlayer.GetComponent<CharacterController>();
        rigidbodyComponent = livePlayer.GetComponent<Rigidbody>();

        savedPlayerMovementEnabled = IsEnabled(playerMovement);
        savedBotControllerEnabled = IsEnabled(botController);
        savedNetworkBotControllerEnabled = IsEnabled(networkBotController);
        savedNetworkTransformEnabled = IsEnabled(networkTransform);
        savedNetworkAnimatorEnabled = IsEnabled(networkAnimator);
        savedCharacterControllerEnabled =
            characterController != null && characterController.enabled;

        if (liveAnimator != null)
        {
            savedAnimatorController = liveAnimator.runtimeAnimatorController;
            savedApplyRootMotion = liveAnimator.applyRootMotion;
            savedAnimatorCullingMode = liveAnimator.cullingMode;
        }

        if (rigidbodyComponent != null)
        {
            savedRigidbodyKinematic = rigidbodyComponent.isKinematic;
            savedRigidbodyDetectCollisions = rigidbodyComponent.detectCollisions;
            savedRigidbodyUseGravity = rigidbodyComponent.useGravity;
            savedRigidbodyLinearVelocity = rigidbodyComponent.linearVelocity;
            savedRigidbodyAngularVelocity = rigidbodyComponent.angularVelocity;
        }
    }

    private void PauseGameplayState()
    {
        DisableBehaviour(playerMovement);
        DisableBehaviour(botController);
        DisableBehaviour(networkBotController);
        DisableBehaviour(networkTransform);
        DisableBehaviour(networkAnimator);

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        if (rigidbodyComponent != null)
        {
            if (!rigidbodyComponent.isKinematic)
            {
                rigidbodyComponent.linearVelocity = Vector3.zero;
                rigidbodyComponent.angularVelocity = Vector3.zero;
            }

            rigidbodyComponent.useGravity = false;
            rigidbodyComponent.detectCollisions = false;
            rigidbodyComponent.isKinematic = true;
        }
    }

    private void ConfigurePresentationAnimator()
    {
        if (liveAnimator == null)
        {
            return;
        }

        if (presentationController != null)
        {
            liveAnimator.runtimeAnimatorController = BuildPresentationController();
        }

        liveAnimator.applyRootMotion = false;
        liveAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        liveAnimator.Rebind();
        liveAnimator.Update(0f);
    }

    private RuntimeAnimatorController BuildPresentationController()
    {
        if (presentationController == null)
        {
            return null;
        }

        string victoryMotionId =
            CharacterMotionStore.NormalizeVictoryMotionId(liveMotionCode);
        if (string.IsNullOrWhiteSpace(victoryMotionId))
        {
            return presentationController;
        }

        LobbyMotionCatalog catalog = LobbyMotionCatalog.LoadDefault();
        if (catalog == null ||
            !catalog.TryGetClip(
                victoryMotionId,
                LobbyMotionCatalog.MotionSlot.Victory,
                out AnimationClip victoryClip))
        {
            return presentationController;
        }

        presentationOverrideController =
            new AnimatorOverrideController(presentationController)
            {
                name = presentationController.name + "_VictoryMotionOverride"
            };

        presentationOverrides.Clear();
        presentationOverrideController.GetOverrides(presentationOverrides);

        bool replaced = false;
        for (int i = 0; i < presentationOverrides.Count; i++)
        {
            AnimationClip originalClip = presentationOverrides[i].Key;
            if (!IsVictoryOverrideTarget(originalClip))
            {
                continue;
            }

            presentationOverrides[i] =
                new KeyValuePair<AnimationClip, AnimationClip>(
                    originalClip,
                    victoryClip);
            replaced = true;
        }

        if (!replaced)
        {
            presentationOverrideController = null;
            return presentationController;
        }

        presentationOverrideController.ApplyOverrides(presentationOverrides);
        return presentationOverrideController;
    }

    private bool PlayStateIfAvailable(string stateName)
    {
        if (liveAnimator == null || liveAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!liveAnimator.HasState(0, stateHash))
        {
            return false;
        }

        liveAnimator.Play(stateHash, 0, 0f);
        liveAnimator.Update(0f);
        return true;
    }

    private void KeepWinnerPoseLooping()
    {
        if (!loopWinnerPose ||
            liveAnimator == null ||
            liveAnimator.runtimeAnimatorController == null ||
            liveAnimator.IsInTransition(0))
        {
            return;
        }

        if (loopingVictoryStateHash == 0)
        {
            loopingVictoryStateHash = Animator.StringToHash("Victory");
        }

        AnimatorStateInfo state = liveAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != loopingVictoryStateHash ||
            state.normalizedTime < 0.98f)
        {
            return;
        }

        liveAnimator.Play(loopingVictoryStateHash, 0, 0f);
        liveAnimator.Update(0f);
    }

    private void ClearCachedComponents()
    {
        liveAnimator = null;
        presentationOverrideController = null;
        presentationOverrides.Clear();
        playerMovement = null;
        botController = null;
        networkBotController = null;
        networkTransform = null;
        networkAnimator = null;
        characterController = null;
        rigidbodyComponent = null;
        savedAnimatorController = null;
        liveMotionCode = string.Empty;
        loopWinnerPose = false;
    }

    private static Player ResolveLivePlayer(GameParticipant participant)
    {
        if (participant == null)
        {
            return null;
        }

        if (participant.Transform != null &&
            participant.Transform.TryGetComponent(out Player participantPlayer))
        {
            return participantPlayer;
        }

        if (participant.Identity != null &&
            participant.Identity.TryGetComponent(out Player identityPlayer))
        {
            return identityPlayer;
        }

        if (PlayerPaintIdentity.TryGetByOwner(
                participant.Owner,
                out PlayerPaintIdentity liveIdentity) &&
            liveIdentity.TryGetComponent(out Player livePlayer))
        {
            return livePlayer;
        }

        return null;
    }

    private static bool IsEnabled(Behaviour behaviour)
    {
        return behaviour != null && behaviour.enabled;
    }

    private static bool IsVictoryOverrideTarget(AnimationClip clip)
    {
        return clip != null &&
               !string.IsNullOrWhiteSpace(clip.name) &&
               clip.name.IndexOf(
                   "Victory",
                   System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void DisableBehaviour(Behaviour behaviour)
    {
        if (behaviour != null)
        {
            behaviour.enabled = false;
        }
    }

    private static void RestoreBehaviour(Behaviour behaviour, bool wasEnabled)
    {
        if (behaviour != null)
        {
            behaviour.enabled = wasEnabled;
        }
    }
}
