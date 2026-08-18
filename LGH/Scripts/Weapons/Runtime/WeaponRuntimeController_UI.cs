using UnityEngine;

public partial class WeaponRuntimeController
{
    private void TryInitializeLocalUiOnRetry()
    {
        if (localUiInitialized || !IsOwner || Time.unscaledTime < nextLocalUiInitializeTime)
        {
            return;
        }

        nextLocalUiInitializeTime = Time.unscaledTime + 0.25f;
        InitializeLocalUi();
    }

    private void InitializeLocalUi()
    {
        bool boundAny = false;

        WeaponTestPanel testPanel = FindFirstObjectByType<WeaponTestPanel>();
        if (testPanel != null)
        {
            testPanel.InitializePanel(this);
            boundAny = true;
        }

        WeaponActionHud actionHud = WeaponActionHud.FindExisting();
        if (actionHud != null)
        {
            actionHud.Initialize(this);
            boundAny = true;
        }

        localUiInitialized = boundAny;
    }
}
