using System;
using UnityEngine;

[Serializable]
public struct StatusEffectSpec
{
    [SerializeField] private StatusEffectType type;
    [SerializeField] private float magnitude;
    [SerializeField] private float duration;
    [SerializeField] private StatusEffectStackMode stackMode;

    public StatusEffectType Type { get { return type; } }
    public float Magnitude { get { return magnitude; } }
    public float Duration { get { return duration; } }
    public StatusEffectStackMode StackMode { get { return stackMode; } }
    public bool IsValid { get { return type != StatusEffectType.None && duration > 0f; } }

    public StatusEffectSpec(StatusEffectType type, float magnitude, float duration, StatusEffectStackMode stackMode)
    {
        this.type = type;
        this.magnitude = Mathf.Clamp01(magnitude);
        this.duration = Mathf.Max(0f, duration);
        this.stackMode = stackMode;
    }
}
