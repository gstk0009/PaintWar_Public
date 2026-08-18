using UnityEngine;

public partial class WeaponRuntimeController
{
    private bool IsCooldownComplete =>
        EquippedWeapon != null && cooldownEndsAt <= 0f;

    public float GetCooldownRemaining()
    {
        RefreshCooldownState();
        return cooldownEndsAt > 0f
            ? Mathf.Max(0f, cooldownEndsAt - Time.time)
            : 0f;
    }

    public float GetCooldownTotal()
    {
        WeaponData weapon = EquippedWeapon;
        return weapon != null ? GetActualCooldown(weapon) : 0f;
    }

    public float GetCooldownProgress()
    {
        RefreshCooldownState();
        if (cooldownEndsAt <= 0f || activeCooldownDuration <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp01(
            (Time.time - cooldownStartedAt) / activeCooldownDuration);
    }

    public void ResetCooldown()
    {
        ClearCooldownSchedule();
    }

    public void ClearCooldown()
    {
        ClearCooldownSchedule();
    }

    public void ReduceCooldown(float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        RefreshCooldownState();
        if (cooldownEndsAt <= 0f)
        {
            return;
        }

        float reducedSeconds = Mathf.Min(
            seconds,
            Mathf.Max(0f, cooldownEndsAt - Time.time));
        cooldownStartedAt -= reducedSeconds;
        cooldownEndsAt -= reducedSeconds;
        RefreshCooldownState();
    }

    private void StartCooldown()
    {
        activeCooldownDuration = Mathf.Max(0.01f, GetCooldownTotal());
        cooldownStartedAt = Time.time;
        cooldownEndsAt = cooldownStartedAt + activeCooldownDuration;
    }

    private void RefreshCooldownState()
    {
        if (EquippedWeapon == null)
        {
            ClearCooldownSchedule();
            return;
        }

        if (cooldownEndsAt <= 0f)
        {
            return;
        }

        if (Time.time >= cooldownEndsAt)
        {
            ClearCooldownSchedule();
            return;
        }

        float currentDuration = Mathf.Max(0.01f, GetCooldownTotal());
        if (Mathf.Approximately(currentDuration, activeCooldownDuration))
        {
            return;
        }

        float previousDuration = Mathf.Max(0.01f, activeCooldownDuration);
        float progress = Mathf.Clamp01(
            (Time.time - cooldownStartedAt) / previousDuration);
        activeCooldownDuration = currentDuration;
        cooldownStartedAt = Time.time - activeCooldownDuration * progress;
        cooldownEndsAt = cooldownStartedAt + activeCooldownDuration;
    }

    private void ClearCooldownSchedule()
    {
        cooldownStartedAt = 0f;
        activeCooldownDuration = 0f;
        cooldownEndsAt = 0f;
    }

    private float GetActualCooldown(WeaponData weapon)
    {
        if (weapon == null)
        {
            return 0f;
        }

        float multiplier = levelStats != null ? levelStats.CooldownMultiplier : 1f;
        return weapon.BaseCooldown * multiplier;
    }
}
