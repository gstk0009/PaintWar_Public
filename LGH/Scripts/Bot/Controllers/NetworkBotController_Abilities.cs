public partial class NetworkBotController
{
    private bool TryUseBotAbilities(GamePhase phase, bool isStuck)
    {
        if (tacticalAbilityHelper == null)
        {
            return false;
        }

        bool readySkillPlan = activeWeaponPlan.IsValid && weaponController != null &&
                              weaponController.IsSkillReady;
        if (!readySkillPlan && useBombMines && tacticalAbilityHelper.TryUseBombMine(phase, isStuck))
        {
            activeWeaponPlan = BotWeaponPlan.None;
            ForceRetarget(phase);
            return true;
        }

        if (useTeleport && tacticalAbilityHelper.TryUseTeleport(phase, isStuck, targetIndex))
        {
            movementMotor.ResetStuckTimer();
            if (teleportAbility.TryConsumeFollowThroughTarget(out UnityEngine.Vector2Int followTarget))
            {
                activeWeaponPlan = BotWeaponPlan.None;
                targetIndex = followTarget;
                teleportFollowThroughActive = true;
            }

            return true;
        }

        if (useTacticalItems)
        {
            tacticalAbilityHelper.TryUseTacticalItem(phase);
        }

        return false;
    }
}
