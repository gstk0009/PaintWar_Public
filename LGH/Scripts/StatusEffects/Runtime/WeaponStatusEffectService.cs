using System.Collections.Generic;
using UnityEngine;

public static class WeaponStatusEffectService
{
    public static List<PlayerPaintIdentity> Apply(WeaponData weapon, PaintOwner sourceOwner, Vector3 origin, Vector3 forward, int playerLevel)
    {
        if (weapon == null || !weapon.HasStatusEffect)
        {
            return new List<PlayerPaintIdentity>();
        }

        IEnumerable<Vector2Int> offsets = WeaponPatternResolver.GetOffsets(weapon, forward, playerLevel);
        List<PlayerPaintIdentity> targets = WeaponEffectTargetResolver.FindTargets(sourceOwner, origin, offsets);
        List<PlayerPaintIdentity> appliedTargets = new List<PlayerPaintIdentity>();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null || targets[i].Owner == sourceOwner)
            {
                continue;
            }

            ApplyToTarget(targets[i], weapon.StatusEffect);
            appliedTargets.Add(targets[i]);
        }

        return appliedTargets;
    }

    public static void ApplyToOwner(PaintOwner targetOwner, StatusEffectSpec effect)
    {
        if (TryFindTargetByOwner(targetOwner, out PlayerPaintIdentity target))
        {
            ApplyToTarget(target, effect);
        }
    }

    private static bool TryFindTargetByOwner(PaintOwner targetOwner, out PlayerPaintIdentity target)
    {
        if (PlayerPaintIdentity.TryGetByOwner(targetOwner, out target))
        {
            return true;
        }

        if (GameRoundManager.HasInstance)
        {
            GameParticipant participant = GameRoundManager.Instance.FindParticipant(targetOwner);
            if (participant != null && !participant.IsEliminated)
            {
                target = participant.Identity;
                if (target == null && participant.Transform != null)
                {
                    participant.Transform.TryGetComponent(out target);
                }

                if (target != null && target.isActiveAndEnabled)
                {
                    return true;
                }
            }
        }

        IReadOnlyList<PlayerPaintIdentity> identities = PlayerPaintIdentity.ActivePlayers;
        for (int i = 0; i < identities.Count; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity != null && identity.isActiveAndEnabled && identity.Owner == targetOwner)
            {
                target = identity;
                return true;
            }
        }

        target = null;
        return false;
    }

    private static void ApplyToTarget(PlayerPaintIdentity target, StatusEffectSpec effect)
    {
        if (target == null)
        {
            return;
        }

        if (PlayerShieldController.IsProtected(target))
        {
            Debug.Log(
                "[Shield] Weapon status effect blocked. target=" +
                target.Owner + ".");
            return;
        }

        PlayerStatusEffectController receiver = target.GetComponent<PlayerStatusEffectController>();
        if (receiver == null)
        {
            receiver = target.gameObject.AddComponent<PlayerStatusEffectController>();
        }

        receiver.ApplyStatusEffect(effect);
    }
}
