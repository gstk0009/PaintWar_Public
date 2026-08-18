using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyEmojiShopUI : MonoBehaviour
{
    [SerializeField] private LobbyEmojiCatalog catalog;
    [SerializeField] private Button shopButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button randomUnlockButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private Image[] slotImages;
    [SerializeField] private GameObject[] slotSelectedIndicators;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private ScrollRect itemScrollRect;
    [SerializeField] private RectTransform itemContentRoot;
    [SerializeField] private LobbyEmojiShopItemButton itemButtonPrefab;
    [SerializeField] private bool fitPanelToScreen = true;
    [SerializeField] private Vector2 fitPanelPadding = new Vector2(72f, 72f);
    [SerializeField, Min(0.1f)] private float minPanelSizeScale = 0.1f;
    [SerializeField, Min(0f)] private float maxPanelSizeScale;

    private readonly List<LobbyEmojiShopItemButton> itemButtons =
        new List<LobbyEmojiShopItemButton>();
    private readonly List<LobbyEmojiCatalog.EmojiEntry> itemEntries =
        new List<LobbyEmojiCatalog.EmojiEntry>();

    private int selectedSlotIndex;
    private Vector2 authoredPanelSize;
    private float lastAppliedPanelSizeScale = -1f;
    private readonly List<RectTransformFitSnapshot> responsiveRects =
        new List<RectTransformFitSnapshot>();
    private readonly List<TextFitSnapshot> responsiveTexts =
        new List<TextFitSnapshot>();
    private readonly List<LayoutGroupFitSnapshot> responsiveLayoutGroups =
        new List<LayoutGroupFitSnapshot>();
    private readonly List<LineLayoutFitSnapshot> responsiveLineLayouts =
        new List<LineLayoutFitSnapshot>();
    private readonly List<GridLayoutFitSnapshot> responsiveGrids =
        new List<GridLayoutFitSnapshot>();
    private readonly List<LayoutElementFitSnapshot> responsiveLayoutElements =
        new List<LayoutElementFitSnapshot>();
    private readonly List<ShadowFitSnapshot> responsiveShadows =
        new List<ShadowFitSnapshot>();
    private Vector2 lastFitRootSize = new Vector2(-1f, -1f);
    private Rect lastFitSafeArea;

    private void Awake()
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (shopButton != null)
        {
            shopButton.onClick.AddListener(OpenPanel);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }

        if (randomUnlockButton != null)
        {
            randomUnlockButton.onClick.AddListener(UnlockRandomEmoji);
        }

        if (slotButtons != null)
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slotIndex = i;
                Button slotButton = slotButtons[i];
                if (slotButton != null)
                {
                    slotButton.onClick.AddListener(() => SelectSlot(slotIndex));
                }
            }
        }

        RefreshSlotViews();
        ClosePanel();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
        ApplyResponsivePanelFit(true);
    }

    private void OnEnable()
    {
        LobbyButtonController.MatchmakingViewStateChanged += ApplyMatchmakingVisibility;
        LobbyEmojiLoadoutStore.Changed += RefreshAll;
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnDisable()
    {
        LobbyButtonController.MatchmakingViewStateChanged -= ApplyMatchmakingVisibility;
        LobbyEmojiLoadoutStore.Changed -= RefreshAll;
    }

    private void Update()
    {
        ApplyResponsivePanelFit(false);
    }

    private void OnDestroy()
    {
        if (shopButton != null)
        {
            shopButton.onClick.RemoveListener(OpenPanel);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(ClosePanel);
        }

        if (randomUnlockButton != null)
        {
            randomUnlockButton.onClick.RemoveListener(UnlockRandomEmoji);
        }

        if (slotButtons == null)
        {
            return;
        }

        for (int i = 0; i < slotButtons.Length; i++)
        {
            if (slotButtons[i] != null)
            {
                slotButtons[i].onClick.RemoveAllListeners();
            }
        }
    }

    public void BindSceneReferences(
        LobbyEmojiCatalog sceneCatalog,
        Button sceneShopButton,
        Button sceneCloseButton,
        Button sceneRandomUnlockButton,
        RectTransform scenePanel,
        Button[] sceneSlotButtons,
        Image[] sceneSlotImages,
        GameObject[] sceneSlotSelectedIndicators,
        TextMeshProUGUI sceneStatusText,
        ScrollRect sceneItemScrollRect,
        RectTransform sceneItemContentRoot,
        LobbyEmojiShopItemButton sceneItemButtonPrefab)
    {
        catalog = sceneCatalog;
        shopButton = sceneShopButton;
        closeButton = sceneCloseButton;
        randomUnlockButton = sceneRandomUnlockButton;
        panel = scenePanel;
        slotButtons = sceneSlotButtons;
        slotImages = sceneSlotImages;
        slotSelectedIndicators = sceneSlotSelectedIndicators;
        statusText = sceneStatusText;
        itemScrollRect = sceneItemScrollRect;
        itemContentRoot = sceneItemContentRoot;
        itemButtonPrefab = sceneItemButtonPrefab;
    }

    public void OpenPanel()
    {
        if (catalog == null)
        {
            catalog = LobbyEmojiCatalog.LoadDefault();
        }

        if (panel == null)
        {
            return;
        }

        panel.gameObject.SetActive(true);
        ApplyResponsivePanelFit(true);
        RefreshAll();
        RefreshCatalogLayout();
        if (itemScrollRect != null)
        {
            itemScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    public void ClosePanel()
    {
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
        }
    }

    private void SelectSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= LobbyEmojiLoadoutStore.SlotCount)
        {
            return;
        }

        selectedSlotIndex = slotIndex;
        RefreshAll();
    }

    private void SelectEmoji(LobbyEmojiCatalog.EmojiEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        if (!LobbyEmojiLoadoutStore.IsUnlocked(entry, catalog))
        {
            SetStatus("Locked. Use Random Unlock.");
            return;
        }

        if (LobbyEmojiLoadoutStore.EquipSlot(selectedSlotIndex, entry.Id, catalog))
        {
            RefreshItemStates();
            SetStatus("Slot " + (selectedSlotIndex + 1) + " registered: " + entry.DisplayName);
        }
    }

    private void UnlockRandomEmoji()
    {
        if (!TryGetRandomLockedEntry(out LobbyEmojiCatalog.EmojiEntry unlockedEntry))
        {
            SetStatus("All emojis unlocked!");
            RefreshItemStates();
            RefreshRandomUnlockButtonState();
            return;
        }

        LobbyEmojiLoadoutStore.UnlockForLocalProfile(unlockedEntry.Id, catalog);
        RebuildItemGrid();
        RefreshRandomUnlockButtonState();
        SetStatus("You Got " + unlockedEntry.DisplayName + "!");
    }

    private void RefreshAll()
    {
        RefreshSlotViews();
        RebuildItemGrid();
        RefreshRandomUnlockButtonState();
    }

    private void RefreshSlotViews()
    {
        if (slotImages != null)
        {
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

        if (slotSelectedIndicators != null)
        {
            for (int i = 0; i < slotSelectedIndicators.Length; i++)
            {
                GameObject indicator = slotSelectedIndicators[i];
                if (indicator != null)
                {
                    indicator.SetActive(i == selectedSlotIndex);
                }
            }
        }

        SetStatus("Select a slot, then choose an unlocked emoji.");
    }

    private void RebuildItemGrid()
    {
        if (itemContentRoot == null || itemButtonPrefab == null)
        {
            SetStatus("Emoji shop list is not assigned.");
            return;
        }

        if (catalog == null || catalog.Entries == null)
        {
            SetStatus("Emoji catalog is empty.");
            return;
        }

        EnsureItemButtons();
        RefreshCatalogLayout();
        RefreshItemStates();
    }

    private void RefreshItemStates()
    {
        string selectedEmojiId =
            LobbyEmojiLoadoutStore.GetEquippedEmojiId(selectedSlotIndex, catalog);
        int count = Mathf.Min(itemEntries.Count, itemButtons.Count);
        for (int i = 0; i < count; i++)
        {
            LobbyEmojiCatalog.EmojiEntry entry = itemEntries[i];
            LobbyEmojiShopItemButton itemButton = itemButtons[i];
            if (entry == null || itemButton == null)
            {
                continue;
            }

            bool unlocked = LobbyEmojiLoadoutStore.IsUnlocked(entry, catalog);
            bool selected = string.Equals(
                selectedEmojiId,
                entry.Id,
                System.StringComparison.Ordinal);
            int equippedSlot = LobbyEmojiLoadoutStore.FindEquippedSlot(entry.Id, catalog);
            bool equippedInOtherSlot = equippedSlot >= 0 && equippedSlot != selectedSlotIndex;
            bool selectable = unlocked && !equippedInOtherSlot;
            string unavailableText = equippedInOtherSlot
                ? "In Slot " + (equippedSlot + 1)
                : "Unavailable";

            itemButton.RefreshState(unlocked, selected, selectable, unavailableText);
        }
    }

    private void RefreshRandomUnlockButtonState()
    {
        if (randomUnlockButton != null)
        {
            randomUnlockButton.interactable = HasLockedEmoji();
        }
    }

    private bool HasLockedEmoji()
    {
        if (catalog == null || catalog.Entries == null)
        {
            return false;
        }

        LobbyEmojiCatalog.EmojiEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry entry = entries[i];
            if (entry != null &&
                entry.IsValid &&
                !LobbyEmojiLoadoutStore.IsUnlocked(entry, catalog))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetRandomLockedEntry(out LobbyEmojiCatalog.EmojiEntry entry)
    {
        entry = null;
        if (catalog == null || catalog.Entries == null)
        {
            return false;
        }

        int lockedCount = 0;
        LobbyEmojiCatalog.EmojiEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry candidate = entries[i];
            if (candidate != null &&
                candidate.IsValid &&
                !LobbyEmojiLoadoutStore.IsUnlocked(candidate, catalog))
            {
                lockedCount++;
            }
        }

        if (lockedCount <= 0)
        {
            return false;
        }

        int selectedLockedIndex = Random.Range(0, lockedCount);
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry candidate = entries[i];
            if (candidate == null ||
                !candidate.IsValid ||
                LobbyEmojiLoadoutStore.IsUnlocked(candidate, catalog))
            {
                continue;
            }

            if (selectedLockedIndex == 0)
            {
                entry = candidate;
                return true;
            }

            selectedLockedIndex--;
        }

        return false;
    }

    private void EnsureItemButtons()
    {
        if (catalog == null || catalog.Entries == null)
        {
            return;
        }

        itemButtons.Clear();
        itemEntries.Clear();
        itemContentRoot.gameObject.SetActive(true);

        int childIndex = 0;
        LobbyEmojiCatalog.EmojiEntry[] entries = catalog.Entries;
        AppendItemButtons(entries, true, ref childIndex);
        AppendItemButtons(entries, false, ref childIndex);

        for (int i = itemContentRoot.childCount - 1; i >= childIndex; i--)
        {
            Transform child = itemContentRoot.GetChild(i);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        RefreshCatalogLayout();
    }

    private void AppendItemButtons(
        LobbyEmojiCatalog.EmojiEntry[] entries,
        bool unlockedEntries,
        ref int childIndex)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry entry = entries[i];
            if (entry == null || !entry.IsValid)
            {
                continue;
            }

            bool unlocked = LobbyEmojiLoadoutStore.IsUnlocked(entry, catalog);
            if (unlocked != unlockedEntries)
            {
                continue;
            }

            LobbyEmojiShopItemButton itemButton = GetOrCreateItemButton(childIndex);
            if (itemButton == null)
            {
                continue;
            }

            itemButton.gameObject.name = "EmojiItem_" + entry.Id;
            itemButton.gameObject.SetActive(true);
            itemButton.transform.SetSiblingIndex(childIndex);
            LobbyEmojiCatalog.EmojiEntry capturedEntry = entry;
            itemButton.BindEntry(capturedEntry, () => SelectEmoji(capturedEntry));
            itemButtons.Add(itemButton);
            itemEntries.Add(capturedEntry);
            childIndex++;
        }
    }

    private LobbyEmojiShopItemButton GetOrCreateItemButton(int childIndex)
    {
        if (itemContentRoot == null)
        {
            return null;
        }

        LobbyEmojiShopItemButton existing = GetReusableItemButton(childIndex);
        if (existing != null)
        {
            return existing;
        }

        if (itemButtonPrefab == null)
        {
            return null;
        }

        LobbyEmojiShopItemButton created = Instantiate(itemButtonPrefab, itemContentRoot);
        created.gameObject.SetActive(true);
        return created;
    }

    private LobbyEmojiShopItemButton GetReusableItemButton(int startChildIndex)
    {
        if (itemContentRoot == null)
        {
            return null;
        }

        for (int i = Mathf.Max(0, startChildIndex); i < itemContentRoot.childCount; i++)
        {
            Transform child = itemContentRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            LobbyEmojiShopItemButton itemButton =
                child.GetComponent<LobbyEmojiShopItemButton>();
            if (itemButton != null)
            {
                return itemButton;
            }
        }

        return null;
    }

    private void RefreshCatalogLayout()
    {
        if (itemContentRoot == null)
        {
            return;
        }

        EnsureItemScrollReferences();
        EnsureItemContentSize();
        LayoutRebuilder.ForceRebuildLayoutImmediate(itemContentRoot);
        Canvas.ForceUpdateCanvases();
        EnsureItemContentSize();
        LayoutRebuilder.ForceRebuildLayoutImmediate(itemContentRoot);
    }

    private void EnsureItemScrollReferences()
    {
        if (itemScrollRect == null)
        {
            itemScrollRect = itemContentRoot.GetComponentInParent<ScrollRect>(true);
        }

        if (itemScrollRect != null && itemScrollRect.content == null)
        {
            itemScrollRect.content = itemContentRoot;
        }
    }

    private void EnsureItemContentSize()
    {
        GridLayoutGroup grid = itemContentRoot.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return;
        }

        int visibleItemCount = 0;
        for (int i = 0; i < itemContentRoot.childCount; i++)
        {
            Transform child = itemContentRoot.GetChild(i);
            if (child != null &&
                child.gameObject.activeSelf &&
                child.GetComponent<LobbyEmojiShopItemButton>() != null)
            {
                visibleItemCount++;
            }
        }

        if (visibleItemCount <= 0)
        {
            return;
        }

        ApplyEvenColumnCount(grid, visibleItemCount);
        int columnCount = CalculateGridColumnCount(grid);
        int rowCount = Mathf.CeilToInt(visibleItemCount / (float)columnCount);
        float requiredHeight =
            grid.padding.top +
            grid.padding.bottom +
            rowCount * grid.cellSize.y +
            Mathf.Max(0, rowCount - 1) * grid.spacing.y;

        Vector2 sizeDelta = itemContentRoot.sizeDelta;
        itemContentRoot.sizeDelta = new Vector2(
            sizeDelta.x,
            Mathf.Max(sizeDelta.y, requiredHeight));
    }

    private static void ApplyEvenColumnCount(GridLayoutGroup grid, int visibleItemCount)
    {
        if (grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount ||
            visibleItemCount <= 0)
        {
            return;
        }

        int maxColumnCount = Mathf.Max(1, grid.constraintCount);
        int evenColumnCount = 1;
        for (int columnCount = maxColumnCount; columnCount >= 1; columnCount--)
        {
            if (visibleItemCount % columnCount == 0)
            {
                evenColumnCount = columnCount;
                break;
            }
        }

        if (grid.constraintCount != evenColumnCount)
        {
            grid.constraintCount = evenColumnCount;
        }
    }

    private int CalculateGridColumnCount(GridLayoutGroup grid)
    {
        if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
        {
            return Mathf.Max(1, grid.constraintCount);
        }

        if (grid.constraint == GridLayoutGroup.Constraint.FixedRowCount)
        {
            int visibleItemCount = Mathf.Max(1, CountValidEntries());
            int rowCount = Mathf.Max(1, grid.constraintCount);
            return Mathf.Max(1, Mathf.CeilToInt(visibleItemCount / (float)rowCount));
        }

        float width = itemContentRoot.rect.width;
        if (width <= 0f && itemContentRoot.parent is RectTransform viewport)
        {
            width = viewport.rect.width;
        }

        float usableWidth = Mathf.Max(1f, width - grid.padding.left - grid.padding.right);
        float cellWidth = Mathf.Max(1f, grid.cellSize.x + grid.spacing.x);
        return Mathf.Max(1, Mathf.FloorToInt((usableWidth + grid.spacing.x) / cellWidth));
    }

    private int CountValidEntries()
    {
        if (catalog == null || catalog.Entries == null)
        {
            return 0;
        }

        int count = 0;
        LobbyEmojiCatalog.EmojiEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyEmojiCatalog.EmojiEntry entry = entries[i];
            if (entry != null && entry.IsValid)
            {
                count++;
            }
        }

        return count;
    }

    private void ApplyMatchmakingVisibility(bool matchmakingActive)
    {
        if (shopButton != null)
        {
            shopButton.gameObject.SetActive(!matchmakingActive);
        }

        if (matchmakingActive)
        {
            ClosePanel();
        }
    }

    private void ApplyResponsivePanelFit(bool force)
    {
        if (!fitPanelToScreen || panel == null)
        {
            return;
        }

        RectTransform root = panel.parent as RectTransform;
        if (root == null)
        {
            root = transform as RectTransform;
        }

        if (root == null)
        {
            return;
        }

        CacheAuthoredPanelLayout();

        Vector2 rootSize = root.rect.size;
        Rect safeArea = Screen.safeArea;
        if (!force && rootSize == lastFitRootSize && safeArea == lastFitSafeArea)
        {
            return;
        }

        lastFitRootSize = rootSize;
        lastFitSafeArea = safeArea;

        Vector2 availableSize = GetSafeAreaCanvasSize(rootSize, safeArea);
        availableSize.x = Mathf.Max(1f, availableSize.x - Mathf.Max(0f, fitPanelPadding.x) * 2f);
        availableSize.y = Mathf.Max(1f, availableSize.y - Mathf.Max(0f, fitPanelPadding.y) * 2f);

        float maxSizeScale = Mathf.Max(0.1f, maxPanelSizeScale > 0f ? maxPanelSizeScale : 1f);
        Vector2 maxPanelSize = authoredPanelSize * maxSizeScale;
        maxPanelSize.x = Mathf.Max(1f, maxPanelSize.x);
        maxPanelSize.y = Mathf.Max(1f, maxPanelSize.y);

        float widthFit = availableSize.x / maxPanelSize.x;
        float heightFit = availableSize.y / maxPanelSize.y;
        float fitScale = Mathf.Min(1f, widthFit, heightFit);
        float lowerScale = Mathf.Min(fitScale, Mathf.Max(0.1f, minPanelSizeScale));
        fitScale = Mathf.Clamp(fitScale, lowerScale, 1f);

        ApplyPanelSizeScale(maxSizeScale * fitScale);
    }

    private void CacheAuthoredPanelLayout()
    {
        if (authoredPanelSize.x > 0f && authoredPanelSize.y > 0f)
        {
            return;
        }

        Vector2 panelSize = panel.sizeDelta;
        Vector3 panelScale = panel.localScale;
        authoredPanelSize = new Vector2(
            Mathf.Max(1f, Mathf.Abs(panelSize.x) * Mathf.Max(0.1f, Mathf.Abs(panelScale.x))),
            Mathf.Max(1f, Mathf.Abs(panelSize.y) * Mathf.Max(0.1f, Mathf.Abs(panelScale.y))));

        RectTransform[] rects = panel.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect == panel)
            {
                continue;
            }

            responsiveRects.Add(new RectTransformFitSnapshot(rect));
        }

        TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null)
            {
                responsiveTexts.Add(new TextFitSnapshot(texts[i]));
            }
        }

        LayoutGroup[] layoutGroups = panel.GetComponentsInChildren<LayoutGroup>(true);
        for (int i = 0; i < layoutGroups.Length; i++)
        {
            LayoutGroup layoutGroup = layoutGroups[i];
            if (layoutGroup == null)
            {
                continue;
            }

            responsiveLayoutGroups.Add(new LayoutGroupFitSnapshot(layoutGroup));
            HorizontalOrVerticalLayoutGroup lineLayout =
                layoutGroup as HorizontalOrVerticalLayoutGroup;
            if (lineLayout != null)
            {
                responsiveLineLayouts.Add(new LineLayoutFitSnapshot(lineLayout));
            }
        }

        GridLayoutGroup[] grids = panel.GetComponentsInChildren<GridLayoutGroup>(true);
        for (int i = 0; i < grids.Length; i++)
        {
            if (grids[i] != null)
            {
                responsiveGrids.Add(new GridLayoutFitSnapshot(grids[i]));
            }
        }

        LayoutElement[] layoutElements = panel.GetComponentsInChildren<LayoutElement>(true);
        for (int i = 0; i < layoutElements.Length; i++)
        {
            if (layoutElements[i] != null)
            {
                responsiveLayoutElements.Add(new LayoutElementFitSnapshot(layoutElements[i]));
            }
        }

        Shadow[] shadows = panel.GetComponentsInChildren<Shadow>(true);
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] != null)
            {
                responsiveShadows.Add(new ShadowFitSnapshot(shadows[i]));
            }
        }
    }

    private void ApplyPanelSizeScale(float panelSizeScale)
    {
        panelSizeScale = Mathf.Max(0.1f, panelSizeScale);
        Vector2 targetSize = authoredPanelSize * panelSizeScale;
        Vector3 targetScale = new Vector3(1f, 1f, panel.localScale.z);
        bool scaleChanged =
            !Mathf.Approximately(panel.localScale.x, targetScale.x) ||
            !Mathf.Approximately(panel.localScale.y, targetScale.y);
        bool sizeChanged =
            !Mathf.Approximately(panel.sizeDelta.x, targetSize.x) ||
            !Mathf.Approximately(panel.sizeDelta.y, targetSize.y);

        if (!scaleChanged &&
            !sizeChanged &&
            Mathf.Approximately(lastAppliedPanelSizeScale, panelSizeScale))
        {
            return;
        }

        panel.localScale = targetScale;
        panel.sizeDelta = targetSize;
        lastAppliedPanelSizeScale = panelSizeScale;

        for (int i = 0; i < responsiveRects.Count; i++)
        {
            responsiveRects[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveTexts.Count; i++)
        {
            responsiveTexts[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveLayoutGroups.Count; i++)
        {
            responsiveLayoutGroups[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveLineLayouts.Count; i++)
        {
            responsiveLineLayouts[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveGrids.Count; i++)
        {
            responsiveGrids[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveLayoutElements.Count; i++)
        {
            responsiveLayoutElements[i].Apply(panelSizeScale);
        }

        for (int i = 0; i < responsiveShadows.Count; i++)
        {
            responsiveShadows[i].Apply(panelSizeScale);
        }
    }

    private static Vector2 GetSafeAreaCanvasSize(Vector2 canvasSize, Rect safeArea)
    {
        int screenWidth = Mathf.Max(1, Screen.width);
        int screenHeight = Mathf.Max(1, Screen.height);
        if (safeArea.width <= 0f || safeArea.height <= 0f)
        {
            return canvasSize;
        }

        return new Vector2(
            canvasSize.x * Mathf.Clamp01(safeArea.width / screenWidth),
            canvasSize.y * Mathf.Clamp01(safeArea.height / screenHeight));
    }

    private readonly struct RectTransformFitSnapshot
    {
        private readonly RectTransform rect;
        private readonly Vector2 offsetMin;
        private readonly Vector2 offsetMax;

        public RectTransformFitSnapshot(RectTransform rect)
        {
            this.rect = rect;
            offsetMin = rect.offsetMin;
            offsetMax = rect.offsetMax;
        }

        public void Apply(float scale)
        {
            if (rect == null)
            {
                return;
            }

            rect.offsetMin = offsetMin * scale;
            rect.offsetMax = offsetMax * scale;
        }
    }

    private readonly struct TextFitSnapshot
    {
        private readonly TMP_Text text;
        private readonly float fontSize;
        private readonly float fontSizeMin;
        private readonly float fontSizeMax;

        public TextFitSnapshot(TMP_Text text)
        {
            this.text = text;
            fontSize = text.fontSize;
            fontSizeMin = text.fontSizeMin;
            fontSizeMax = text.fontSizeMax;
        }

        public void Apply(float scale)
        {
            if (text == null)
            {
                return;
            }

            text.fontSize = fontSize * scale;
            text.fontSizeMin = fontSizeMin * scale;
            text.fontSizeMax = fontSizeMax * scale;
        }
    }

    private readonly struct LayoutGroupFitSnapshot
    {
        private readonly LayoutGroup layoutGroup;
        private readonly int left;
        private readonly int right;
        private readonly int top;
        private readonly int bottom;

        public LayoutGroupFitSnapshot(LayoutGroup layoutGroup)
        {
            this.layoutGroup = layoutGroup;
            RectOffset padding = layoutGroup.padding;
            left = padding != null ? padding.left : 0;
            right = padding != null ? padding.right : 0;
            top = padding != null ? padding.top : 0;
            bottom = padding != null ? padding.bottom : 0;
        }

        public void Apply(float scale)
        {
            if (layoutGroup == null || layoutGroup.padding == null)
            {
                return;
            }

            RectOffset padding = layoutGroup.padding;
            padding.left = Mathf.RoundToInt(left * scale);
            padding.right = Mathf.RoundToInt(right * scale);
            padding.top = Mathf.RoundToInt(top * scale);
            padding.bottom = Mathf.RoundToInt(bottom * scale);
        }
    }

    private readonly struct LineLayoutFitSnapshot
    {
        private readonly HorizontalOrVerticalLayoutGroup layoutGroup;
        private readonly float spacing;

        public LineLayoutFitSnapshot(HorizontalOrVerticalLayoutGroup layoutGroup)
        {
            this.layoutGroup = layoutGroup;
            spacing = layoutGroup.spacing;
        }

        public void Apply(float scale)
        {
            if (layoutGroup != null)
            {
                layoutGroup.spacing = spacing * scale;
            }
        }
    }

    private readonly struct GridLayoutFitSnapshot
    {
        private readonly GridLayoutGroup grid;
        private readonly Vector2 cellSize;
        private readonly Vector2 spacing;

        public GridLayoutFitSnapshot(GridLayoutGroup grid)
        {
            this.grid = grid;
            cellSize = grid.cellSize;
            spacing = grid.spacing;
        }

        public void Apply(float scale)
        {
            if (grid == null)
            {
                return;
            }

            grid.cellSize = cellSize * scale;
            grid.spacing = spacing * scale;
        }
    }

    private readonly struct LayoutElementFitSnapshot
    {
        private readonly LayoutElement layoutElement;
        private readonly float minWidth;
        private readonly float preferredWidth;
        private readonly float minHeight;
        private readonly float preferredHeight;

        public LayoutElementFitSnapshot(LayoutElement layoutElement)
        {
            this.layoutElement = layoutElement;
            minWidth = layoutElement.minWidth;
            preferredWidth = layoutElement.preferredWidth;
            minHeight = layoutElement.minHeight;
            preferredHeight = layoutElement.preferredHeight;
        }

        public void Apply(float scale)
        {
            if (layoutElement == null)
            {
                return;
            }

            if (minWidth >= 0f)
            {
                layoutElement.minWidth = minWidth * scale;
            }

            if (preferredWidth >= 0f)
            {
                layoutElement.preferredWidth = preferredWidth * scale;
            }

            if (minHeight >= 0f)
            {
                layoutElement.minHeight = minHeight * scale;
            }

            if (preferredHeight >= 0f)
            {
                layoutElement.preferredHeight = preferredHeight * scale;
            }
        }
    }

    private readonly struct ShadowFitSnapshot
    {
        private readonly Shadow shadow;
        private readonly Vector2 effectDistance;

        public ShadowFitSnapshot(Shadow shadow)
        {
            this.shadow = shadow;
            effectDistance = shadow.effectDistance;
        }

        public void Apply(float scale)
        {
            if (shadow != null)
            {
                shadow.effectDistance = effectDistance * scale;
            }
        }
    }

    private void SetStatus(string value)
    {
        if (statusText != null)
        {
            statusText.text = value;
        }
    }
}
