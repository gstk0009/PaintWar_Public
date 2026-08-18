using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameplayItemHud : MonoBehaviour
{
    private const float RefreshInterval = 0.1f;

    [SerializeField] private Button[] slotButtons;
    [SerializeField] private Image[] slotImages;
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private DropItemCatalog itemCatalog;
    [SerializeField] private ItemSlotSprite[] itemSprites;

    private readonly UnityEngine.Events.UnityAction[] slotActions =
        new UnityEngine.Events.UnityAction[PlayerStoredItemController.StoredItemSlotCount];
    private readonly bool[] displayedHasItem =
        new bool[PlayerStoredItemController.StoredItemSlotCount];
    private readonly bool[] displayedCanUse =
        new bool[PlayerStoredItemController.StoredItemSlotCount];
    private readonly DropItemEffectType[] displayedItemTypes =
        new DropItemEffectType[PlayerStoredItemController.StoredItemSlotCount];
    private bool displayCacheInitialized;
    private float nextRefreshAt;

    [Serializable]
    private struct ItemSlotSprite
    {
        [SerializeField] private DropItemEffectType itemType;
        [SerializeField] private Sprite sprite;

        public DropItemEffectType ItemType { get { return itemType; } }
        public Sprite Sprite { get { return sprite; } }
    }

    private void Awake()
    {
        BindButtons();
        RefreshSlots();
    }

    private void OnEnable()
    {
        PlayerStoredItemController.LocalStoredItemSlotsChanged += RefreshSlots;
        RefreshSlots();
    }

    private void OnDisable()
    {
        PlayerStoredItemController.LocalStoredItemSlotsChanged -= RefreshSlots;
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + RefreshInterval;
        RefreshSlots();
    }

    private void BindButtons()
    {
        if (slotButtons == null)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Length && i < slotActions.Length; i++)
        {
            int slotIndex = i;
            slotActions[i] = () => UseSlot(slotIndex);
            if (slotButtons[i] != null)
            {
                slotButtons[i].onClick.AddListener(slotActions[i]);
            }
        }
    }

    private void UnbindButtons()
    {
        if (slotButtons == null)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Length && i < slotActions.Length; i++)
        {
            if (slotButtons[i] != null && slotActions[i] != null)
            {
                slotButtons[i].onClick.RemoveListener(slotActions[i]);
            }
        }
    }

    private void UseSlot(int slotIndex)
    {
        if (PlayerStoredItemController.TryUseLocalStoredItemSlot(slotIndex))
        {
            RefreshSlots();
        }
    }

    private void RefreshSlots()
    {
        int slotCount = PlayerStoredItemController.StoredItemSlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            bool hasItem = PlayerStoredItemController.TryGetLocalStoredItemSlot(
                i,
                out DropItemEffectType itemType,
                out bool canUse);

            RefreshSlot(i, hasItem, itemType, canUse);
        }

        displayCacheInitialized = true;
    }

    private void RefreshSlot(
        int slotIndex,
        bool hasItem,
        DropItemEffectType itemType,
        bool canUse)
    {
        if (displayCacheInitialized &&
            displayedHasItem[slotIndex] == hasItem &&
            displayedCanUse[slotIndex] == canUse &&
            displayedItemTypes[slotIndex] == itemType)
        {
            return;
        }

        displayedHasItem[slotIndex] = hasItem;
        displayedCanUse[slotIndex] = canUse;
        displayedItemTypes[slotIndex] = itemType;

        Image image = slotImages != null && slotIndex < slotImages.Length
            ? slotImages[slotIndex]
            : null;
        if (image != null)
        {
            image.sprite = hasItem ? GetSprite(itemType) : emptySprite;
            image.enabled = hasItem || emptySprite != null;
            image.preserveAspect = true;
        }

        Button button = slotButtons != null && slotIndex < slotButtons.Length
            ? slotButtons[slotIndex]
            : null;
        if (button != null)
        {
            button.interactable = hasItem && canUse;
        }
    }

    private Sprite GetSprite(DropItemEffectType itemType)
    {
        if (itemCatalog != null)
        {
            Sprite catalogSprite = itemCatalog.GetIcon(itemType);
            if (catalogSprite != null)
            {
                return catalogSprite;
            }
        }

        if (itemSprites != null)
        {
            for (int i = 0; i < itemSprites.Length; i++)
            {
                if (itemSprites[i].ItemType == itemType &&
                    itemSprites[i].Sprite != null)
                {
                    return itemSprites[i].Sprite;
                }
            }
        }

        return emptySprite;
    }
}
