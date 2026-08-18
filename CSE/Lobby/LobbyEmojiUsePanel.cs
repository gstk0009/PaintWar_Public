using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyEmojiUsePanel : MonoBehaviour
{
    [SerializeField] private LobbyEmojiCatalog catalog;
    [SerializeField] private GameObject useButtonRoot;
    [SerializeField] private Button useButton;
    [SerializeField] private GameObject slotPanelRoot;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private Image[] slotImages;
    [SerializeField] private float useCooldownSeconds = 1f;

    private float nextUseAllowedAt;

    private void Awake()
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (useButton != null)
        {
            useButton.onClick.AddListener(ToggleSlotPanel);
        }

        if (slotButtons != null)
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slotIndex = i;
                Button slotButton = slotButtons[i];
                if (slotButton != null)
                {
                    slotButton.onClick.AddListener(() => UseSlot(slotIndex));
                }
            }
        }

        RefreshSlotImages();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnEnable()
    {
        LobbyButtonController.MatchmakingViewStateChanged += ApplyMatchmakingVisibility;
        LobbyEmojiLoadoutStore.Changed += RefreshSlotImages;
        RefreshSlotImages();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnDisable()
    {
        LobbyButtonController.MatchmakingViewStateChanged -= ApplyMatchmakingVisibility;
        LobbyEmojiLoadoutStore.Changed -= RefreshSlotImages;
    }

    private void OnDestroy()
    {
        if (useButton != null)
        {
            useButton.onClick.RemoveListener(ToggleSlotPanel);
        }

        if (slotButtons != null)
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                if (slotButtons[i] != null)
                {
                    slotButtons[i].onClick.RemoveAllListeners();
                }
            }
        }
    }

    private void ToggleSlotPanel()
    {
        if (slotPanelRoot != null)
        {
            slotPanelRoot.SetActive(!slotPanelRoot.activeSelf);
        }
    }

    private void UseSlot(int slotIndex)
    {
        if (Time.unscaledTime < nextUseAllowedAt ||
            catalog == null ||
            !LobbyEmojiLoadoutStore.TryGetEquippedEntry(
                slotIndex,
                catalog,
                out LobbyEmojiCatalog.EmojiEntry entry))
        {
            return;
        }

        nextUseAllowedAt = Time.unscaledTime + Mathf.Max(0.1f, useCooldownSeconds);
        if (slotPanelRoot != null)
        {
            slotPanelRoot.SetActive(false);
        }

        LobbyPlayer.SubmitLocalEmoji(entry.Id);
    }

    private void ApplyMatchmakingVisibility(bool matchmakingActive)
    {
        if (useButtonRoot != null)
        {
            useButtonRoot.SetActive(matchmakingActive);
        }

        if (!matchmakingActive && slotPanelRoot != null)
        {
            slotPanelRoot.SetActive(false);
        }
    }

    private void RefreshSlotImages()
    {
        if (catalog == null || slotImages == null)
        {
            return;
        }

        for (int i = 0; i < slotImages.Length; i++)
        {
            Image image = slotImages[i];
            if (image == null)
            {
                continue;
            }

            if (LobbyEmojiLoadoutStore.TryGetEquippedEntry(
                    i,
                    catalog,
                    out LobbyEmojiCatalog.EmojiEntry entry))
            {
                image.sprite = entry.Sprite;
                image.preserveAspect = true;
                image.enabled = true;
            }
            else
            {
                image.sprite = null;
                image.enabled = false;
            }
        }
    }
}
