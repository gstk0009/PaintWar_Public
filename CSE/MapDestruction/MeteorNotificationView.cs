using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeteorNotificationView : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text secondaryText;
    [SerializeField, Min(0.1f)] private float defaultDisplaySeconds = 3f;

    private float hideAt;

    private void OnEnable()
    {
        SetVisible(false);
    }

    private void Update()
    {
        if (hideAt > 0f && Time.unscaledTime >= hideAt)
        {
            hideAt = 0f;
            SetVisible(false);
        }
    }

    public void Show(string message, float displaySeconds = 0f)
    {
        if (messageText != null)
        {
            messageText.text = string.IsNullOrWhiteSpace(message)
                ? "Warning Warning!!"
                : message;
        }

        if (secondaryText != null)
        {
            secondaryText.gameObject.SetActive(false);
        }

        hideAt = Time.unscaledTime + Mathf.Max(
            0.1f,
            displaySeconds > 0f ? displaySeconds : defaultDisplaySeconds);
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (panel == null)
        {
            return;
        }

        panel.alpha = visible ? 1f : 0f;
        panel.interactable = false;
        panel.blocksRaycasts = false;
    }
}
