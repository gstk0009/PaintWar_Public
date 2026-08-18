using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MobileItemUseButton : MonoBehaviour
{
    [SerializeField] private Button useButton;
    [SerializeField] private Image iconImage;
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private DropItemCatalog itemCatalog;
    [SerializeField] private ItemUseButtonSprite[] itemSprites;

    private Sprite initialSprite;

    [System.Serializable]
    private struct ItemUseButtonSprite
    {
        [SerializeField] private DropItemEffectType itemType;
        [SerializeField] private Sprite sprite;

        public DropItemEffectType ItemType { get { return itemType; } }
        public Sprite Sprite { get { return sprite; } }
    }

    private bool? displayedInteractable;
    private Sprite displayedSprite;
    private float nextFallbackRefreshAt;
    private const float FallbackRefreshInterval = 0.1f;

    private void Awake()
    {
        if (iconImage == null && useButton != null)
        {
            iconImage = useButton.targetGraphic as Image;
        }

        if (iconImage != null)
        {
            initialSprite = iconImage.sprite;
            iconImage.preserveAspect = true;
        }

        if (useButton != null)
        {
            useButton.onClick.AddListener(UseLocalItem);
        }
    }

    private void OnEnable()
    {
        PlayerStoredItemController.LocalHeldItemAvailabilityChanged += Refresh;
        Refresh(PlayerStoredItemController.LocalOwnerHasItem);
    }

    private void OnDisable()
    {
        PlayerStoredItemController.LocalHeldItemAvailabilityChanged -= Refresh;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextFallbackRefreshAt)
        {
            return;
        }

        nextFallbackRefreshAt = Time.unscaledTime + FallbackRefreshInterval;
        Refresh(PlayerStoredItemController.LocalOwnerHasItem);
    }

    private void OnDestroy()
    {
        if (useButton != null)
        {
            useButton.onClick.RemoveListener(UseLocalItem);
        }
    }

    private void UseLocalItem()
    {
        PlayerStoredItemController.TryUseLocalBombMineAbility();
        Refresh(PlayerStoredItemController.LocalOwnerHasItem);
    }

    private void Refresh(bool hasItem)
    {
        RefreshIcon(hasItem);

        bool interactable = PlayerStoredItemController.LocalOwnerCanUseBombMineAbility;
        if (useButton != null && displayedInteractable != interactable)
        {
            useButton.interactable = interactable;
        }

        displayedInteractable = interactable;
    }

    private void RefreshIcon(bool hasItem)
    {
        if (iconImage == null)
        {
            return;
        }

        Sprite sprite = GetSprite(DropItemEffectType.BombMine);
        if (displayedSprite == sprite)
        {
            return;
        }

        displayedSprite = sprite;
        iconImage.sprite = sprite;
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

        return GetEmptySprite();
    }

    private Sprite GetEmptySprite()
    {
        return emptySprite != null ? emptySprite : initialSprite;
    }
}
