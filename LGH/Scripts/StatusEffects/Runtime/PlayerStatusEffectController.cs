using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("LGH/Status Effects/Player Status Effect Controller")]
public sealed class PlayerStatusEffectController : MonoBehaviour, IStatusEffectReceiver
{
    private static readonly int IsStunnedHash = Animator.StringToHash("IsStunned");

    private sealed class ActiveEffect
    {
        public StatusEffectSpec Spec;
        public float ExpiresAt;
        public int StackCount;
    }

    private readonly Dictionary<StatusEffectType, ActiveEffect> activeEffects =
        new Dictionary<StatusEffectType, ActiveEffect>();
    private readonly List<StatusEffectType> expiredEffects = new List<StatusEffectType>(4);
    private Animator animator;
    private bool displayedStunned;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        RefreshStunAnimation();
    }

    private void Update()
    {
        if (activeEffects.Count > 0)
        {
            RemoveExpiredEffects();
        }
    }

    public event Action EffectsChanged;

    public bool IsStunned
    {
        get
        {
            RemoveExpiredEffects();
            return activeEffects.ContainsKey(StatusEffectType.Root);
        }
    }

    public float MovementSpeedMultiplier
    {
        get
        {
            RemoveExpiredEffects();
            float multiplier = 1f;
            foreach (ActiveEffect active in activeEffects.Values)
            {
                float value = active.Spec.Type == StatusEffectType.Root
                    ? 0f
                    : Mathf.Clamp01(active.Spec.Magnitude);
                int count = active.Spec.StackMode == StatusEffectStackMode.StackMultiplicative
                    ? active.StackCount
                    : 1;
                multiplier *= Mathf.Pow(value, count);
            }

            return Mathf.Clamp01(multiplier);
        }
    }

    public void ApplyStatusEffect(StatusEffectSpec effect)
    {
        if (!effect.IsValid)
        {
            return;
        }

        if (PlayerShieldController.IsProtected(gameObject))
        {
            Debug.Log("[Shield] Status effect blocked: " + effect.Type + ".");
            return;
        }

        RemoveExpiredEffects();

        if (!activeEffects.ContainsKey(effect.Type))
        {
            activeEffects.Add(effect.Type, new ActiveEffect
            {
                Spec = effect,
                ExpiresAt = Time.time + effect.Duration,
                StackCount = 1
            });
            RefreshStunAnimation();
            EffectsChanged?.Invoke();
            return;
        }

        // While the same debuff type is active, later hits do not refresh or stack its duration.
        // The first hit owns the expiry time, preventing repeated hits from causing permanent CC.
    }

    public void Clear(StatusEffectType type)
    {
        if (activeEffects.Remove(type))
        {
            RefreshStunAnimation();
            EffectsChanged?.Invoke();
        }
    }

    public void ClearAll()
    {
        if (activeEffects.Count == 0)
        {
            return;
        }

        activeEffects.Clear();
        RefreshStunAnimation();
        EffectsChanged?.Invoke();
    }

    public bool TryGetEffectStatus(
        StatusEffectType type,
        out float remainingSeconds,
        out float durationSeconds,
        out float magnitude)
    {
        RemoveExpiredEffects();
        if (activeEffects.TryGetValue(type, out ActiveEffect active))
        {
            remainingSeconds = Mathf.Max(0f, active.ExpiresAt - Time.time);
            durationSeconds = Mathf.Max(0.01f, active.Spec.Duration);
            magnitude = active.Spec.Magnitude;
            return remainingSeconds > 0f;
        }

        remainingSeconds = 0f;
        durationSeconds = 0f;
        magnitude = 0f;
        return false;
    }

    private void RemoveExpiredEffects()
    {
        if (activeEffects.Count == 0)
        {
            return;
        }

        expiredEffects.Clear();
        foreach (KeyValuePair<StatusEffectType, ActiveEffect> pair in activeEffects)
        {
            if (Time.time < pair.Value.ExpiresAt)
            {
                continue;
            }

            expiredEffects.Add(pair.Key);
        }

        if (expiredEffects.Count == 0)
        {
            return;
        }

        for (int i = 0; i < expiredEffects.Count; i++)
        {
            activeEffects.Remove(expiredEffects[i]);
        }

        expiredEffects.Clear();
        RefreshStunAnimation();
        EffectsChanged?.Invoke();
    }

    private void RefreshStunAnimation()
    {
        bool stunned = activeEffects.ContainsKey(StatusEffectType.Root);
        if (displayedStunned == stunned)
        {
            return;
        }

        displayedStunned = stunned;
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator != null)
        {
            animator.SetBool(IsStunnedHash, stunned);
        }
    }
}
