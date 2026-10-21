using UnityEngine;

public partial class PlayerMovement
{
    private static readonly int SkillPrepareHash = Animator.StringToHash("SkillPrepare");
    private static readonly int SkillCastHash = Animator.StringToHash("SkillCast");
    private static readonly int SkillCastDirectHash = Animator.StringToHash("SkillCastDirect");
    private static readonly int SkillCancelHash = Animator.StringToHash("SkillCancel");

    public void BeginSkillAimAnimation()
    {
        SetNetworkAnimationTrigger(SkillPrepareHash);
    }

    public void ConfirmSkillCastAnimation()
    {
        SetNetworkAnimationTrigger(SkillCastHash);
    }

    public void PlayImmediateSkillCastAnimation()
    {
        SetNetworkAnimationTrigger(SkillCastDirectHash);
    }

    public void CancelSkillAnimation()
    {
        SetNetworkAnimationTrigger(SkillCancelHash);
    }

    private void SetNetworkAnimationTrigger(int parameterHash)
    {
        CacheComponents();
        if (animator == null)
        {
            return;
        }

        if (networkAnimator != null && (IsClientInitialized || IsServerInitialized))
        {
            networkAnimator.SetTrigger(parameterHash);
            return;
        }

        animator.SetTrigger(parameterHash);
    }
}
