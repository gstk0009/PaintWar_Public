using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// 이동 속도 보너스와 조작 반전 같은 시간 제한 상태 효과를 담당합니다.
/// UniTask와 CancellationTokenSource로 지속시간을 관리합니다.
/// </summary>
public partial class PlayerMovement
{
    public void ApplySpeedBoost(float multiplier, float duration)
    {
        CancelAndDispose(ref speedBoostCts);
        speedBoostCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        SpeedBoostAsync(multiplier, duration, speedBoostCts).Forget();
    }

    public void ApplySpeedBoostToSpeed(float targetSpeed, float duration)
    {
        CancelAndDispose(ref speedBoostCts);
        speedBoostCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        SpeedBoostToSpeedAsync(targetSpeed, duration, speedBoostCts).Forget();
    }

    private async UniTaskVoid SpeedBoostAsync(
        float multiplier,
        float duration,
        CancellationTokenSource source)
    {
        temporarySpeedBonus = moveSpeed * Mathf.Max(0f, multiplier - 1f);

        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, duration)),
                cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (speedBoostCts == source)
            {
                temporarySpeedBonus = 0f;
                source.Dispose();
                speedBoostCts = null;
            }
        }
    }

    private async UniTaskVoid SpeedBoostToSpeedAsync(
        float targetSpeed,
        float duration,
        CancellationTokenSource source)
    {
        float multiplier = statusEffects != null ? statusEffects.MovementSpeedMultiplier : 1f;
        float targetBaseSpeed = Mathf.Max(0f, targetSpeed) / Mathf.Max(0.001f, multiplier);
        temporarySpeedBonus = Mathf.Max(0f, targetBaseSpeed - moveSpeed);

        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, duration)),
                cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (speedBoostCts == source)
            {
                temporarySpeedBonus = 0f;
                source.Dispose();
                speedBoostCts = null;
            }
        }
    }

    private float GetCurrentMoveSpeed()
    {
        // moveSpeed는 level snapshot이 바뀔 때 PlayerLevelStatsConfig의 고정값으로 교체됩니다.
        float statusMultiplier = statusEffects != null ? statusEffects.MovementSpeedMultiplier : 1f;
        float tileMultiplier = 1f;
        if (TilePaintManager.HasInstance && identity != null)
            tileMultiplier = TilePaintManager.Instance.GetTileSpeedMultiplier(
                TilePaintManager.Instance.GetGridIndex(transform.position), identity.Owner);
        return (moveSpeed + temporarySpeedBonus) * tileMultiplier * statusMultiplier;
    }

    public void ApplyControlInversion(float duration)
    {
        if (PlayerShieldController.IsProtected(gameObject))
        {
            Debug.Log("[Shield] Control inversion blocked.");
            return;
        }

        CancelAndDispose(ref controlInversionCts);
        controlInversionCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        ControlInversionAsync(duration, controlInversionCts).Forget();
    }

    private async UniTaskVoid ControlInversionAsync(
        float duration,
        CancellationTokenSource source)
    {
        ShowControlInversionIndicator(duration);
        isControlInverted = true;

        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0.1f, duration)),
                cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (controlInversionCts == source)
            {
                isControlInverted = false;
                HideControlInversionIndicator();
                source.Dispose();
                controlInversionCts = null;
            }
        }
    }

    private void ShowControlInversionIndicator(float duration)
    {
        if (controlInversionIndicator == null)
        {
            controlInversionIndicator = gameObject.AddComponent<ControlInversionIndicator>();
        }

        controlInversionIndicator.Show(duration);
    }

    private void HideControlInversionIndicator()
    {
        if (controlInversionIndicator != null)
        {
            controlInversionIndicator.Hide();
        }
    }

    public void ApplyTeleportLock(float duration)
    {
        if (PlayerShieldController.IsProtected(gameObject))
        {
            Debug.Log("[Shield] Teleport lock blocked.");
            return;
        }

        CancelAndDispose(ref teleportLockCts);
        teleportLockCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        TeleportLockAsync(duration, teleportLockCts).Forget();
    }

    private async UniTaskVoid TeleportLockAsync(
        float duration,
        CancellationTokenSource source)
    {
        float effectDuration = Mathf.Max(0.1f, duration);
        teleportLockEndsAt = Time.time + effectDuration;
        isTeleportLocked = true;
        displayedTeleportCooldownTenths = -1;
        ShowTeleportLockIndicator(effectDuration);
        RefreshTeleportCooldownDisplay(true);

        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(effectDuration),
                cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (teleportLockCts == source)
            {
                isTeleportLocked = false;
                teleportLockEndsAt = -1f;
                displayedTeleportCooldownTenths = -1;
                HideTeleportLockIndicator();
                RefreshTeleportCooldownDisplay(true);
                source.Dispose();
                teleportLockCts = null;
            }
        }
    }

    private void ShowTeleportLockIndicator(float duration)
    {
        if (teleportLockIndicator == null)
        {
            teleportLockIndicator = gameObject.AddComponent<TeleportLockIndicator>();
        }

        teleportLockIndicator.Show(duration);
    }

    private void HideTeleportLockIndicator()
    {
        if (teleportLockIndicator != null)
        {
            teleportLockIndicator.Hide();
        }
    }
}
