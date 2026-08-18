using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

public sealed class LobbyCharacterShopUI : MonoBehaviour
{
    private static readonly Vector3 PreviewWorldPosition = new Vector3(1000f, -1000f, 1000f);
    private static readonly Bounds PreviewCharacterRenderBounds =
        new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(5f, 5f, 5f));

    [SerializeField] private Button shopButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button applyButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI selectedItemText;
    [SerializeField] private RawImage previewViewport;
    [SerializeField] private ScrollRect itemScrollRect;
    [SerializeField] private RectTransform itemContentRoot;
    [SerializeField] private LobbyCharacterShopItemButton itemButtonPrefab;
    [SerializeField] private GameObject previewCharacterPrefab;
    [SerializeField] private Button allCategoryButton;
    [SerializeField] private Button[] slotCategoryButtons;
    [SerializeField] private CharacterCosmeticSlot[] slotCategorySlots;

    private CharacterCosmeticCatalog catalog;
    private string editingAppearanceCode;
    private CharacterCosmeticSlot? selectedCategorySlot;
    private RenderTexture previewTexture;
    private Camera previewCamera;
    private GameObject previewCharacter;
    private CharacterAppearanceApplicator previewApplicator;
    private bool isDraggingPreview;
    private Vector2 lastPreviewPointerPosition;

    private void Awake()
    {
        ResolveSceneReferences();
        BindCategoryButtons();

        catalog = CharacterCosmeticCatalog.LoadDefault();
        if (catalog == null)
        {
            Debug.LogWarning("[Shop] CharacterCosmeticCatalog was not found in Resources.");
        }

        if (titleText != null)
        {
            titleText.text = "Character Shop";
        }

        ClosePanel();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnEnable()
    {
        LobbyButtonController.MatchmakingViewStateChanged += ApplyMatchmakingVisibility;
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnDisable()
    {
        LobbyButtonController.MatchmakingViewStateChanged -= ApplyMatchmakingVisibility;
    }

    private void Update()
    {
        if (panel == null || !panel.gameObject.activeSelf || previewCharacter == null)
        {
            return;
        }

        UpdatePreviewDragRotation();
    }

    private void OnDestroy()
    {
        if (previewCharacter != null)
        {
            Destroy(previewCharacter);
        }

        if (previewCamera != null)
        {
            Destroy(previewCamera.gameObject);
        }

        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }

        UnbindCategoryButtons();
    }

    public void BindSceneReferences(
        Button sceneShopButton,
        Button sceneCloseButton,
        Button sceneApplyButton,
        RectTransform scenePanel,
        RectTransform sceneContentRoot,
        TextMeshProUGUI sceneTitleText,
        TextMeshProUGUI sceneSelectedItemText,
        RawImage scenePreviewViewport,
        ScrollRect sceneItemScrollRect,
        RectTransform sceneItemContentRoot,
        LobbyCharacterShopItemButton sceneItemButtonPrefab,
        Button sceneAllCategoryButton = null,
        Button[] sceneSlotCategoryButtons = null,
        CharacterCosmeticSlot[] sceneSlotCategorySlots = null)
    {
        shopButton = sceneShopButton;
        closeButton = sceneCloseButton;
        applyButton = sceneApplyButton;
        panel = scenePanel;
        contentRoot = sceneContentRoot;
        titleText = sceneTitleText;
        selectedItemText = sceneSelectedItemText;
        previewViewport = scenePreviewViewport;
        itemScrollRect = sceneItemScrollRect;
        itemContentRoot = sceneItemContentRoot;
        itemButtonPrefab = sceneItemButtonPrefab;
        allCategoryButton = sceneAllCategoryButton;
        slotCategoryButtons = sceneSlotCategoryButtons;
        slotCategorySlots = sceneSlotCategorySlots;
    }

    private void ResolveSceneReferences()
    {
        if (contentRoot == null)
        {
            contentRoot = itemContentRoot;
        }
    }

    public void OpenPanel()
    {
        if (catalog == null)
        {
            catalog = CharacterCosmeticCatalog.LoadDefault();
        }

        if (panel == null)
        {
            return;
        }

        editingAppearanceCode = CharacterAppearanceStore.CurrentCode;
        selectedCategorySlot = null;
        panel.gameObject.SetActive(true);
        RefreshCategoryButtonState();
        EnsurePreviewScene();
        RebuildPanelContent();
        if (itemScrollRect != null)
        {
            itemScrollRect.verticalNormalizedPosition = 1f;
        }

        RefreshPreview();
    }

    public void ClosePanel()
    {
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
        }

        if (previewCharacter != null)
        {
            previewCharacter.SetActive(false);
        }

        if (previewCamera != null)
        {
            previewCamera.enabled = false;
        }
    }

    public void ApplySelection()
    {
        CharacterAppearanceStore.Save(editingAppearanceCode);
        CharacterAppearanceStore.SendCurrentProfileToServer();
        ClosePanel();
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

    private void RebuildPanelContent()
    {
        RectTransform targetRoot = itemContentRoot != null ? itemContentRoot : contentRoot;
        if (targetRoot == null || itemButtonPrefab == null)
        {
            RefreshSelectedSummary();
            return;
        }

        for (int i = targetRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(targetRoot.GetChild(i).gameObject);
        }

        if (catalog == null || catalog.Slots == null)
        {
            RefreshSelectedSummary();
            return;
        }

        CharacterCosmeticCatalog.CosmeticSlotGroup[] slots = catalog.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            CharacterCosmeticCatalog.CosmeticSlotGroup group = slots[i];
            if (group == null)
            {
                continue;
            }

            if (!ShouldShowSlot(group.Slot))
            {
                continue;
            }

            if (group.AllowNone)
            {
                CreateItemButton(targetRoot, group, null, string.Empty, "None");
            }

            CharacterCosmeticCatalog.CosmeticItem[] items = group.Items;
            int itemCount = items != null ? items.Length : 0;
            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                CharacterCosmeticCatalog.CosmeticItem item = items[itemIndex];
                if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
                {
                    continue;
                }

                CreateItemButton(targetRoot, group, item, item.ItemId, item.DisplayName);
            }
        }

        RefreshSelectedSummary();
    }

    private void CreateItemButton(
        RectTransform targetRoot,
        CharacterCosmeticCatalog.CosmeticSlotGroup group,
        CharacterCosmeticCatalog.CosmeticItem item,
        string itemId,
        string displayName)
    {
        LobbyCharacterShopItemButton itemButton = Instantiate(itemButtonPrefab, targetRoot);
        itemButton.gameObject.SetActive(true);

        string selectedId = CharacterAppearanceStore.GetSlot(editingAppearanceCode, group.Slot);
        bool isSelected = string.Equals(selectedId, itemId, System.StringComparison.Ordinal);
        itemButton.Configure(
            group.DisplayName,
            displayName,
            GetSlotSilhouette(group.Slot, string.IsNullOrWhiteSpace(itemId)),
            item != null ? item.LoadThumbnailSprite() : null,
            isSelected,
            () => SelectItem(group, itemId));
    }

    private void SelectItem(CharacterCosmeticCatalog.CosmeticSlotGroup group, string itemId)
    {
        editingAppearanceCode =
            CharacterAppearanceStore.SetSlot(editingAppearanceCode, group.Slot, itemId);
        RebuildPanelContent();
        RefreshPreview();
    }

    private void RefreshSelectedSummary()
    {
        if (selectedItemText == null)
        {
            return;
        }

        selectedItemText.text = "Tap an item to preview. Press Apply to equip.";
    }

    private static string GetSlotSilhouette(CharacterCosmeticSlot slot, bool isNone)
    {
        if (isNone)
        {
            return "-";
        }

        switch (slot)
        {
            case CharacterCosmeticSlot.Body:
            case CharacterCosmeticSlot.FullBody:
                return "BODY";
            case CharacterCosmeticSlot.Hat:
                return "HAT";
            case CharacterCosmeticSlot.Accessories:
                return "ACC";
            case CharacterCosmeticSlot.Pants:
                return "PANT";
            case CharacterCosmeticSlot.Faces:
                return "FACE";
            case CharacterCosmeticSlot.Hair:
                return "HAIR";
            case CharacterCosmeticSlot.Outerwear:
                return "OUT";
            case CharacterCosmeticSlot.Shoes:
                return "SHOE";
            case CharacterCosmeticSlot.Glasses:
                return "EYE";
            case CharacterCosmeticSlot.Gloves:
                return "HAND";
            default:
                return "ITEM";
        }
    }

    private void BindCategoryButtons()
    {
        BindCategoryButton(allCategoryButton, null);

        int buttonCount = slotCategoryButtons != null ? slotCategoryButtons.Length : 0;
        int slotCount = slotCategorySlots != null ? slotCategorySlots.Length : 0;
        int count = Mathf.Min(buttonCount, slotCount);
        for (int i = 0; i < count; i++)
        {
            BindCategoryButton(slotCategoryButtons[i], slotCategorySlots[i]);
        }

        RefreshCategoryButtonState();
    }

    private void BindCategoryButton(Button button, CharacterCosmeticSlot? slot)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => SelectCategory(slot));
    }

    private void UnbindCategoryButtons()
    {
        RemoveCategoryButtonListeners(allCategoryButton);

        int buttonCount = slotCategoryButtons != null ? slotCategoryButtons.Length : 0;
        for (int i = 0; i < buttonCount; i++)
        {
            RemoveCategoryButtonListeners(slotCategoryButtons[i]);
        }
    }

    private static void RemoveCategoryButtonListeners(Button button)
    {
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
        }
    }

    private void SelectCategory(CharacterCosmeticSlot? slot)
    {
        if (selectedCategorySlot == slot)
        {
            return;
        }

        selectedCategorySlot = slot;
        RefreshCategoryButtonState();
        RebuildPanelContent();
        if (itemScrollRect != null)
        {
            itemScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void RefreshCategoryButtonState()
    {
        SetCategoryButtonState(allCategoryButton, !selectedCategorySlot.HasValue);

        int buttonCount = slotCategoryButtons != null ? slotCategoryButtons.Length : 0;
        int slotCount = slotCategorySlots != null ? slotCategorySlots.Length : 0;
        int count = Mathf.Min(buttonCount, slotCount);
        for (int i = 0; i < count; i++)
        {
            bool selected =
                selectedCategorySlot.HasValue && selectedCategorySlot.Value == slotCategorySlots[i];
            SetCategoryButtonState(slotCategoryButtons[i], selected);
        }
    }

    private void SetCategoryButtonState(Button button, bool selected)
    {
        if (button != null)
        {
            button.interactable = !selected;
        }
    }

    private bool ShouldShowSlot(CharacterCosmeticSlot slot)
    {
        return !selectedCategorySlot.HasValue || selectedCategorySlot.Value == slot;
    }

    private void EnsurePreviewScene()
    {
        if (previewViewport == null)
        {
            Debug.LogWarning("[Shop] Preview viewport is not assigned.");
            return;
        }

        if (previewTexture == null)
        {
            previewTexture = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32);
            previewTexture.name = "CharacterShopPreviewTexture";
            previewTexture.Create();
        }

        previewViewport.texture = previewTexture;

        if (previewCamera == null)
        {
            GameObject cameraObject = new GameObject("CharacterShopPreviewCamera");
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.12f, 0.10f, 0.13f, 1f);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 1.15f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20f;
            previewCamera.targetTexture = previewTexture;
            previewCamera.transform.position = PreviewWorldPosition + new Vector3(0f, 1.48f, -4f);
            previewCamera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
        }

        previewCamera.enabled = true;

        if (previewCharacter == null && previewCharacterPrefab != null)
        {
            previewCharacter = Instantiate(
                previewCharacterPrefab,
                PreviewWorldPosition,
                Quaternion.Euler(0f, 180f, 0f));
            previewCharacter.name = "CharacterShopPreviewCharacter";
            DisablePreviewRuntimeComponents(previewCharacter);
            ConfigurePreviewRenderers(previewCharacter);
            previewApplicator = previewCharacter.GetComponent<CharacterAppearanceApplicator>();
            if (previewApplicator == null)
            {
                previewApplicator = previewCharacter.AddComponent<CharacterAppearanceApplicator>();
            }
        }

        if (previewCharacter != null)
        {
            previewCharacter.SetActive(true);
        }
    }

    private void RefreshPreview()
    {
        if (previewApplicator != null)
        {
            previewApplicator.ApplyAppearance(editingAppearanceCode);
            if (previewCharacter != null)
            {
                ConfigurePreviewRenderers(previewCharacter);
            }
        }
    }

    private void UpdatePreviewDragRotation()
    {
        if (previewViewport == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        if (!TryGetPreviewPointerState(
                out Vector2 pointerPosition,
                out bool pressedThisFrame,
                out bool isPressed,
                out bool releasedThisFrame))
        {
            return;
        }

        if (pressedThisFrame)
        {
            isDraggingPreview = RectTransformUtility.RectangleContainsScreenPoint(
                previewViewport.rectTransform,
                pointerPosition);
            lastPreviewPointerPosition = pointerPosition;
            return;
        }

        if (isPressed && isDraggingPreview)
        {
            RotatePreviewByPointerDelta(pointerPosition - lastPreviewPointerPosition);
            lastPreviewPointerPosition = pointerPosition;
        }

        if (releasedThisFrame)
        {
            isDraggingPreview = false;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(
                previewViewport.rectTransform,
                touch.position);

            if (touch.phase == TouchPhase.Began)
            {
                isDraggingPreview = inside;
                lastPreviewPointerPosition = touch.position;
                return;
            }

            if (!isDraggingPreview)
            {
                return;
            }

            if (touch.phase == TouchPhase.Moved)
            {
                RotatePreviewByPointerDelta(touch.position - lastPreviewPointerPosition);
                lastPreviewPointerPosition = touch.position;
            }

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                isDraggingPreview = false;
            }

            return;
        }

        Vector2 mousePosition = Input.mousePosition;
        if (Input.GetMouseButtonDown(0))
        {
            isDraggingPreview = RectTransformUtility.RectangleContainsScreenPoint(
                previewViewport.rectTransform,
                mousePosition);
            lastPreviewPointerPosition = mousePosition;
        }

        if (Input.GetMouseButton(0) && isDraggingPreview)
        {
            RotatePreviewByPointerDelta(mousePosition - lastPreviewPointerPosition);
            lastPreviewPointerPosition = mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDraggingPreview = false;
        }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryGetPreviewPointerState(
        out Vector2 pointerPosition,
        out bool pressedThisFrame,
        out bool isPressed,
        out bool releasedThisFrame)
    {
        pointerPosition = Vector2.zero;
        pressedThisFrame = false;
        isPressed = false;
        releasedThisFrame = false;

        if (Touchscreen.current != null)
        {
            TouchControl touch = Touchscreen.current.primaryTouch;
            if (touch.press.wasPressedThisFrame || touch.press.isPressed || touch.press.wasReleasedThisFrame)
            {
                pointerPosition = touch.position.ReadValue();
                pressedThisFrame = touch.press.wasPressedThisFrame;
                isPressed = touch.press.isPressed;
                releasedThisFrame = touch.press.wasReleasedThisFrame;
                return true;
            }
        }

        if (Mouse.current == null)
        {
            return false;
        }

        pointerPosition = Mouse.current.position.ReadValue();
        pressedThisFrame = Mouse.current.leftButton.wasPressedThisFrame;
        isPressed = Mouse.current.leftButton.isPressed;
        releasedThisFrame = Mouse.current.leftButton.wasReleasedThisFrame;
        return pressedThisFrame || isPressed || releasedThisFrame;
    }
#endif

    private void RotatePreviewByPointerDelta(Vector2 pointerDelta)
    {
        if (previewCharacter == null || Mathf.Abs(pointerDelta.x) < 0.01f)
        {
            return;
        }

        previewCharacter.transform.Rotate(Vector3.up, -pointerDelta.x * 0.45f, Space.World);
    }

    private static void DisablePreviewRuntimeComponents(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        CharacterController[] controllers = target.GetComponentsInChildren<CharacterController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            controllers[i].enabled = false;
        }

        Canvas[] canvases = target.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].enabled = false;
        }

        MonoBehaviour[] behaviours = target.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour is CharacterAppearanceApplicator)
            {
                continue;
            }

            behaviour.enabled = false;
        }
    }

    private static void ConfigurePreviewRenderers(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = true;
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
                skinnedRenderer.localBounds = PreviewCharacterRenderBounds;
            }
        }
    }

}
