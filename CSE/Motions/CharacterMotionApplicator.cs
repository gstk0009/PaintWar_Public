using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class CharacterMotionApplicator : MonoBehaviour
{
    private const string DefaultIdleClipName = "Idle_Look_Around";

    [SerializeField] private Animator animator;
    [SerializeField] private string idleClipName = DefaultIdleClipName;

    private RuntimeAnimatorController baseController;
    private AnimatorOverrideController overrideController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
        new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private string appliedProfileMotionCode;
    private string appliedIdleMotionId;
    private string appliedVictoryMotionId;
    private float appliedIdlePlaybackSpeed = 1f;
    private int idleStateHash;

    public string AppliedProfileMotionCode { get { return appliedProfileMotionCode; } }
    public string AppliedIdleMotionId { get { return appliedIdleMotionId; } }
    public string AppliedVictoryMotionId { get { return appliedVictoryMotionId; } }

    public void ApplyProfileMotion(string motionCode)
    {
        string normalizedCode = CharacterMotionStore.NormalizeProfileMotionCode(motionCode);
        appliedProfileMotionCode = normalizedCode;
        appliedIdleMotionId = CharacterMotionStore.NormalizeIdleMotionId(normalizedCode);
        appliedVictoryMotionId = CharacterMotionStore.NormalizeVictoryMotionId(normalizedCode);
        ApplyIdleClip(appliedIdleMotionId);
    }

    public void ApplyIdleMotion(string motionId)
    {
        string normalizedMotionId = CharacterMotionStore.NormalizeIdleMotionId(motionId);
        appliedProfileMotionCode = normalizedMotionId;
        appliedIdleMotionId = normalizedMotionId;
        appliedVictoryMotionId = string.Empty;
        ApplyIdleClip(normalizedMotionId);
    }

    private void ApplyIdleClip(string motionId)
    {
        EnsureAnimator();
        if (animator == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(motionId))
        {
            RestoreBaseController();
            appliedIdleMotionId = string.Empty;
            appliedIdlePlaybackSpeed = 1f;
            ApplyAnimatorSpeed(1f);
            return;
        }

        LobbyMotionCatalog catalog = LobbyMotionCatalog.LoadDefault();
        if (catalog == null ||
            !catalog.TryGetClip(
                motionId,
                LobbyMotionCatalog.MotionSlot.Idle,
                out AnimationClip clip))
        {
            RestoreBaseController();
            appliedIdleMotionId = string.Empty;
            appliedIdlePlaybackSpeed = 1f;
            ApplyAnimatorSpeed(1f);
            return;
        }

        EnsureOverrideController();
        if (overrideController == null)
        {
            return;
        }

        if (!TryOverrideIdleClip(clip))
        {
            RestoreBaseController();
            appliedIdleMotionId = string.Empty;
            return;
        }

        animator.runtimeAnimatorController = overrideController;
        appliedIdleMotionId = motionId;
        appliedIdlePlaybackSpeed = catalog.GetPlaybackSpeed(
            motionId,
            LobbyMotionCatalog.MotionSlot.Idle);
        idleStateHash = Animator.StringToHash(GetIdleClipName());
        RebindAnimator();
        UpdateAnimatorPlaybackSpeed();
    }

    private void Update()
    {
        UpdateAnimatorPlaybackSpeed();
    }

    private void EnsureAnimator()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (animator != null && baseController == null)
        {
            AnimatorOverrideController currentOverride =
                animator.runtimeAnimatorController as AnimatorOverrideController;
            baseController = currentOverride != null
                ? currentOverride.runtimeAnimatorController
                : animator.runtimeAnimatorController;
        }
    }

    private void EnsureOverrideController()
    {
        EnsureAnimator();
        if (animator == null || baseController == null)
        {
            return;
        }

        if (overrideController != null &&
            overrideController.runtimeAnimatorController == baseController)
        {
            return;
        }

        overrideController = new AnimatorOverrideController(baseController)
        {
            name = baseController.name + "_MotionOverride"
        };
    }

    private bool TryOverrideIdleClip(AnimationClip replacementClip)
    {
        if (overrideController == null || replacementClip == null)
        {
            return false;
        }

        string targetName = GetIdleClipName();

        overrides.Clear();
        overrideController.GetOverrides(overrides);

        bool replaced = false;
        for (int i = 0; i < overrides.Count; i++)
        {
            AnimationClip originalClip = overrides[i].Key;
            if (originalClip == null)
            {
                continue;
            }

            if (IsIdleOverrideTarget(originalClip, targetName))
            {
                overrides[i] =
                    new KeyValuePair<AnimationClip, AnimationClip>(
                        originalClip,
                        replacementClip);
                replaced = true;
            }
        }

        if (!replaced)
        {
            return false;
        }

        overrideController.ApplyOverrides(overrides);
        return true;
    }

    private static bool IsIdleOverrideTarget(AnimationClip clip, string targetName)
    {
        if (clip == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(targetName) &&
            string.Equals(clip.name, targetName, System.StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            clip.name,
            DefaultIdleClipName,
            System.StringComparison.Ordinal);
    }

    private void RestoreBaseController()
    {
        EnsureAnimator();
        if (animator == null || baseController == null)
        {
            return;
        }

        if (animator.runtimeAnimatorController != baseController)
        {
            animator.runtimeAnimatorController = baseController;
            RebindAnimator();
        }

        ApplyAnimatorSpeed(1f);
    }

    private void RebindAnimator()
    {
        if (animator == null || !animator.gameObject.activeInHierarchy)
        {
            return;
        }

        bool isRun = GetBoolParameterIfPresent(animator, "isRun");
        animator.Rebind();
        SetBoolParameterIfPresent(animator, "isRun", isRun);
        animator.Update(0f);
    }

    private void UpdateAnimatorPlaybackSpeed()
    {
        if (animator == null ||
            string.IsNullOrWhiteSpace(appliedIdleMotionId) ||
            Mathf.Approximately(appliedIdlePlaybackSpeed, 1f))
        {
            ApplyAnimatorSpeed(1f);
            return;
        }

        if (!animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null ||
            idleStateHash == 0)
        {
            return;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        bool isIdle =
            currentState.shortNameHash == idleStateHash ||
            nextState.shortNameHash == idleStateHash;
        ApplyAnimatorSpeed(isIdle ? appliedIdlePlaybackSpeed : 1f);
    }

    private void ApplyAnimatorSpeed(float speed)
    {
        if (animator == null)
        {
            return;
        }

        float normalizedSpeed = Mathf.Max(0.01f, speed);
        if (!Mathf.Approximately(animator.speed, normalizedSpeed))
        {
            animator.speed = normalizedSpeed;
        }
    }

    private string GetIdleClipName()
    {
        return string.IsNullOrWhiteSpace(idleClipName)
            ? DefaultIdleClipName
            : idleClipName.Trim();
    }

    private static bool GetBoolParameterIfPresent(Animator target, string parameterName)
    {
        if (target == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = target.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] != null &&
                parameters[i].type == AnimatorControllerParameterType.Bool &&
                parameters[i].name == parameterName)
            {
                return target.GetBool(parameterName);
            }
        }

        return false;
    }

    private static void SetBoolParameterIfPresent(
        Animator target,
        string parameterName,
        bool value)
    {
        if (target == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        AnimatorControllerParameter[] parameters = target.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] != null &&
                parameters[i].type == AnimatorControllerParameterType.Bool &&
                parameters[i].name == parameterName)
            {
                target.SetBool(parameterName, value);
                return;
            }
        }
    }
}
