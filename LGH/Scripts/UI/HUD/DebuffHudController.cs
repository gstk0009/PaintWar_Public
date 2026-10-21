using UnityEngine;

[DisallowMultipleComponent]
public sealed class DebuffHudController : MonoBehaviour
{
    [SerializeField] private DebuffStatusItem stunItem;
    [SerializeField] private DebuffStatusItem slowItem;
    [SerializeField, Min(0.1f)] private float bindRetryInterval = 0.5f;

    private PlayerStatusEffectController statusEffects;
    private float nextBindTime;

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (statusEffects == null)
        {
            TryBindLocalPlayer();
        }

        RefreshItems();
    }

    private void TryBindLocalPlayer()
    {
        if (Time.unscaledTime < nextBindTime)
        {
            return;
        }

        nextBindTime = Time.unscaledTime + bindRetryInterval;
        WeaponRuntimeController weaponController =
            WeaponRuntimeController.FindLocalPlayerController();
        if (weaponController == null)
        {
            return;
        }

        PlayerStatusEffectController found =
            weaponController.GetComponent<PlayerStatusEffectController>();
        if (found == statusEffects)
        {
            return;
        }

        Unbind();
        statusEffects = found;
    }

    private void Unbind()
    {
        statusEffects = null;
    }

    private void RefreshItems()
    {
        RefreshItem(stunItem, StatusEffectType.Root);
        RefreshItem(slowItem, StatusEffectType.Slow);
    }

    private void RefreshItem(DebuffStatusItem item, StatusEffectType type)
    {
        if (item == null)
        {
            return;
        }

        float remaining = 0f;
        float duration = 0f;
        bool active = statusEffects != null &&
            statusEffects.TryGetEffectStatus(
                type,
                out remaining,
                out duration,
                out _);
        item.Refresh(active, active ? remaining : 0f, active ? duration : 0f);
    }
}
