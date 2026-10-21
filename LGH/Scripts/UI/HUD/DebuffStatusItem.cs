using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DebuffStatusItem : MonoBehaviour
{
    [SerializeField] private GameObject viewRoot;
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private TMP_Text timeText;

    private int displayedTenths = -1;
    private bool displayedActive;

    public void Refresh(bool active, float remainingSeconds, float durationSeconds)
    {
        if (viewRoot != null && displayedActive != active)
        {
            viewRoot.SetActive(active);
        }

        displayedActive = active;
        if (!active)
        {
            displayedTenths = -1;
            return;
        }

        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount =
                Mathf.Clamp01(remainingSeconds / Mathf.Max(0.01f, durationSeconds));
        }

        int tenths = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds) * 10f);
        if (timeText != null && tenths != displayedTenths)
        {
            timeText.text = (tenths / 10f).ToString("0.0") + "s";
        }

        displayedTenths = tenths;
    }
}
