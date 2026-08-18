using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyMotionShopItemButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI iconText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private GameObject lockedOverlay;

    private LobbyMotionCatalog.MotionEntry boundEntry;

    public void BindSceneReferences(
        Button sceneButton,
        TextMeshProUGUI sceneIconText,
        TextMeshProUGUI sceneNameText,
        TextMeshProUGUI sceneStateText,
        GameObject sceneSelectedIndicator,
        GameObject sceneLockedOverlay)
    {
        button = sceneButton;
        iconText = sceneIconText;
        nameText = sceneNameText;
        stateText = sceneStateText;
        selectedIndicator = sceneSelectedIndicator;
        lockedOverlay = sceneLockedOverlay;
    }

    public void Configure(
        LobbyMotionCatalog.MotionEntry entry,
        bool unlocked,
        bool selected,
        Action onClick)
    {
        boundEntry = entry;

        if (iconText != null)
        {
            iconText.text = string.Empty;
            iconText.gameObject.SetActive(false);
        }

        if (nameText != null)
        {
            nameText.text = entry != null ? entry.DisplayName : string.Empty;
        }

        if (stateText != null)
        {
            stateText.text = string.Empty;
            stateText.gameObject.SetActive(false);
        }

        if (selectedIndicator != null)
        {
            selectedIndicator.SetActive(selected);
        }

        if (lockedOverlay != null)
        {
            lockedOverlay.SetActive(!unlocked);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = unlocked;
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }

}
