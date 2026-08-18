using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyCharacterShopItemButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image thumbnailImage;
    [SerializeField] private TextMeshProUGUI silhouetteText;
    [SerializeField] private TextMeshProUGUI slotText;
    [SerializeField] private TextMeshProUGUI itemText;
    [SerializeField] private GameObject selectedIndicator;

    public void BindSceneReferences(
        Button sceneButton,
        Image sceneThumbnailImage,
        TextMeshProUGUI sceneSilhouetteText,
        TextMeshProUGUI sceneSlotText,
        TextMeshProUGUI sceneItemText,
        GameObject sceneSelectedIndicator)
    {
        button = sceneButton;
        thumbnailImage = sceneThumbnailImage;
        silhouetteText = sceneSilhouetteText;
        slotText = sceneSlotText;
        itemText = sceneItemText;
        selectedIndicator = sceneSelectedIndicator;
    }

    public void Configure(
        string slotName,
        string itemName,
        string silhouette,
        bool selected,
        Action onClick)
    {
        Configure(slotName, itemName, silhouette, null, selected, onClick);
    }

    public void Configure(
        string slotName,
        string itemName,
        string silhouette,
        Sprite thumbnailSprite,
        bool selected,
        Action onClick)
    {
        if (silhouetteText != null)
        {
            silhouetteText.text = silhouette;
            silhouetteText.enabled = thumbnailSprite == null;
        }

        if (thumbnailImage != null)
        {
            thumbnailImage.sprite = thumbnailSprite;
            thumbnailImage.preserveAspect = true;
            thumbnailImage.enabled = thumbnailSprite != null;
        }

        if (slotText != null)
        {
            slotText.text = slotName;
        }

        if (itemText != null)
        {
            itemText.text = itemName;
        }

        if (selectedIndicator != null)
        {
            selectedIndicator.SetActive(selected);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
