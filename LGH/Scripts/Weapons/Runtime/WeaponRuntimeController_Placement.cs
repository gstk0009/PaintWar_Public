using Cysharp.Threading.Tasks;
using FishNet.Object;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public partial class WeaponRuntimeController
{
    private async UniTaskVoid ClaimAfterDelayAsync(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner sourceOwner,
        int playerLevel,
        float delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ClaimPattern(weapon, origin, forward, sourceOwner, playerLevel);
    }

    private void StartPlacedWeaponOnServer(WeaponData weapon, Vector3 origin, Vector3 forward, int playerLevel)
    {
        if (weapon == null)
        {
            return;
        }

        PaintOwner owner = CurrentOwner;
        int authoritativeLevel = levelStats != null ? Mathf.Max(1, levelStats.Level) : Mathf.Max(1, playerLevel);
        ObserversSpawnPlacedWeaponRpc(weapon.WeaponId, origin, forward, owner, authoritativeLevel);
        DetonateLocalMinesAfterWeaponImpactDelayAsync(
            weapon,
            origin,
            forward,
            authoritativeLevel,
            GetImpactEffectDelaySeconds(weapon, GetVisualSettings()),
            this.GetCancellationTokenOnDestroy()).Forget();
        ClaimAfterDelayAsync(
            weapon,
            origin,
            forward,
            owner,
            authoritativeLevel,
            GameplayApplyDelaySeconds,
            this.GetCancellationTokenOnDestroy()).Forget();
    }

    [ObserversRpc(ExcludeServer = true)]
    private void ObserversSpawnPlacedWeaponRpc(string weaponId, Vector3 origin, Vector3 forward, PaintOwner owner, int playerLevel)
    {
        WeaponData weapon = FindWeapon(weaponId);
        SpawnPlacedWeaponVisual(weapon, origin, forward, owner, Mathf.Max(1, playerLevel));
        DetonateLocalMinesAfterWeaponImpactDelayAsync(
            weapon,
            origin,
            forward,
            playerLevel,
            GetImpactEffectDelaySeconds(weapon, GetVisualSettings()),
            this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void SpawnPlacedWeaponVisual(WeaponData weapon, Vector3 origin, Vector3 forward, PaintOwner owner, int playerLevel)
    {
        WeaponPlacementVisual.Create(
            weapon,
            origin,
            forward,
            owner,
            Mathf.Max(1, playerLevel),
            GetVisualSettings());
    }

    private async UniTaskVoid DetonateLocalMinesAfterWeaponImpactDelayAsync(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        int playerLevel,
        float delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        List<Vector2Int> affectedTiles = new List<Vector2Int>(25);
        WeaponPatternImpactResolver.DetonateMinesInPattern(
            weapon,
            origin,
            forward,
            playerLevel,
            affectedTiles);
    }

    private void RequestNetworkVisualIfAvailable(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel)
    {
        if (NetworkWeaponPlacementVisualSync.HasInstance)
        {
            NetworkWeaponPlacementVisualSync.Instance.RequestVisual(
                weapon,
                origin,
                forward,
                owner,
                playerLevel);
        }
    }

    private void ClaimPattern(WeaponData weapon, Vector3 origin, Vector3 forward, PaintOwner sourceOwner, int playerLevel)
    {
        if (!TilePaintManager.HasInstance)
        {
            return;
        }

        int authoritativeLevel = Mathf.Max(1, playerLevel);
        WeaponPatternImpactResolver.ApplyAuthoritative(
            weapon, origin, forward, sourceOwner, authoritativeLevel, obstacleImpactBuffer);

        ApplyWeaponStatusEffect(weapon, origin, forward, sourceOwner, authoritativeLevel);
    }

    private async UniTaskVoid RequestNetworkStatusEffectAfterDelayAsync(
        WeaponData weapon, Vector3 origin, Vector3 forward, PaintOwner sourceOwner,
        int playerLevel, float delay, CancellationToken cancellationToken)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RequestNetworkStatusEffectIfAvailable(weapon, origin, forward, sourceOwner, playerLevel);
    }

    private void ApplyWeaponStatusEffect(WeaponData weapon, Vector3 origin, Vector3 forward, PaintOwner sourceOwner, int playerLevel)
    {
        if (weapon == null || !weapon.HasStatusEffect)
        {
            return;
        }

        if (IsServerInitialized)
        {
            ApplyWeaponStatusEffectOnServer(weapon, origin, forward, sourceOwner, playerLevel);
        }
        else if (IsClientInitialized)
        {
            RequestNetworkStatusEffectIfAvailable(weapon, origin, forward, sourceOwner, playerLevel);
        }
        else
        {
            WeaponStatusEffectService.Apply(weapon, sourceOwner, origin, forward, playerLevel);
        }
    }

    private void ApplyWeaponStatusEffectOnServer(WeaponData weapon, Vector3 origin, Vector3 forward, PaintOwner sourceOwner, int playerLevel)
    {
        int authoritativeLevel = Mathf.Max(1, playerLevel);
        List<PlayerPaintIdentity> targets = WeaponStatusEffectService.Apply(weapon, sourceOwner, origin, forward, authoritativeLevel);
        StatusEffectSpec effect = weapon.StatusEffect;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null || targets[i].Owner == sourceOwner)
            {
                continue;
            }

            SyncWeaponStatusEffectObserversRpc(targets[i].Owner, effect.Type, effect.Magnitude, effect.Duration, effect.StackMode);
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    private void SyncWeaponStatusEffectObserversRpc(PaintOwner targetOwner, StatusEffectType type, float magnitude, float duration, StatusEffectStackMode stackMode)
    {
        WeaponStatusEffectService.ApplyToOwner(targetOwner, new StatusEffectSpec(type, magnitude, duration, stackMode));
    }

    private WeaponData FindWeapon(string weaponId)
    {
        if (TryFindWeaponData(weaponId, out WeaponData cachedWeapon))
        {
            return cachedWeapon;
        }

        if (weapons == null)
        {
            return null;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon != null && string.Equals(weapon.WeaponId, weaponId, StringComparison.Ordinal))
            {
                return weapon;
            }
        }

        return null;
    }

    private void RegisterWeapons()
    {
        if (weapons == null)
        {
            return;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.WeaponId))
            {
                continue;
            }

            WeaponLookup[weapon.WeaponId] = weapon;
        }
    }

    private void UnregisterWeapons()
    {
        if (weapons == null)
        {
            return;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.WeaponId))
            {
                continue;
            }

            if (WeaponLookup.TryGetValue(weapon.WeaponId, out WeaponData cachedWeapon) && cachedWeapon == weapon)
            {
                WeaponLookup.Remove(weapon.WeaponId);
            }
        }
    }

    private void RequestNetworkStatusEffectIfAvailable(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel)
    {
        if (NetworkWeaponStatusEffectSync.HasInstance)
        {
            NetworkWeaponStatusEffectSync.Instance.RequestEffect(
                weapon,
                origin,
                forward,
                owner,
                playerLevel);
        }
    }
}
