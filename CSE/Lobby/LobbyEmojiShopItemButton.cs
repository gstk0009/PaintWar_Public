using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyEmojiShopItemButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private GameObject lockedOverlay;

    private LobbyEmojiCatalog.EmojiEntry boundEntry;
    private bool hasState;
    private string lastStateText;
    private bool lastSelected;
    private bool lastOverlayActive;
    private bool lastInteractable;

    public void BindSceneReferences(
        Button sceneButton,
        Image sceneIconImage,
        TextMeshProUGUI sceneNameText,
        TextMeshProUGUI sceneStateText,
        GameObject sceneSelectedIndicator,
        GameObject sceneLockedOverlay)
    {
        button = sceneButton;
        iconImage = sceneIconImage;
        nameText = sceneNameText;
        stateText = sceneStateText;
        selectedIndicator = sceneSelectedIndicator;
        lockedOverlay = sceneLockedOverlay;
    }

    public void BindEntry(LobbyEmojiCatalog.EmojiEntry entry, Action onClick)
    {
        boundEntry = entry;
        hasState = false;

        if (iconImage != null)
        {
            iconImage.sprite = entry != null ? entry.Sprite : null;
            iconImage.preserveAspect = true;
            iconImage.enabled = entry != null && entry.Sprite != null;
        }

        if (nameText != null)
        {
            nameText.text = entry != null ? entry.DisplayName : string.Empty;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }

    public void RefreshState(
        bool unlocked,
        bool selected,
        bool selectable,
        string unavailableText)
    {
        string nextStateText = BuildStateText(boundEntry, unlocked, selected, selectable, unavailableText);
        if (stateText != null && (!hasState || !string.Equals(lastStateText, nextStateText, StringComparison.Ordinal)))
        {
            stateText.text = nextStateText;
            lastStateText = nextStateText;
        }

        if (selectedIndicator != null && (!hasState || lastSelected != selected))
        {
            selectedIndicator.SetActive(selected);
            lastSelected = selected;
        }

        bool overlayActive = !unlocked;
        if (lockedOverlay != null && (!hasState || lastOverlayActive != overlayActive))
        {
            lockedOverlay.SetActive(overlayActive);
            lastOverlayActive = overlayActive;
        }

        const bool interactable = true;
        if (button != null && (!hasState || lastInteractable != interactable))
        {
            button.interactable = interactable;
            lastInteractable = interactable;
        }

        hasState = true;
    }

    public void Configure(
        LobbyEmojiCatalog.EmojiEntry entry,
        bool unlocked,
        bool selected,
        bool selectable,
        string unavailableText,
        Action onClick)
    {
        if (!ReferenceEquals(boundEntry, entry))
        {
            BindEntry(entry, onClick);
        }

        RefreshState(unlocked, selected, selectable, unavailableText);
    }

    private static string BuildStateText(
        LobbyEmojiCatalog.EmojiEntry entry,
        bool unlocked,
        bool selected,
        bool selectable,
        string unavailableText)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        if (unlocked)
        {
            if (selected)
            {
                return "Registered";
            }

            return selectable ? "Unlocked" : unavailableText;
        }

        return entry.PointCost > 0
            ? "Locked " + entry.PointCost + "P"
            : "Locked";
    }
}
