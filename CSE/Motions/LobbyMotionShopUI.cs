using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyMotionShopUI : MonoBehaviour
{
    private static readonly Vector3 PreviewWorldPosition =
        new Vector3(1200f, -1000f, 1000f);
    private static readonly Bounds PreviewCharacterRenderBounds =
        new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(5f, 5f, 5f));
    private const string PreviewIdleClipName = "Idle_Look_Around";
    private const float PreviewStartNormalizedTime = 0.18f;

    [SerializeField] private LobbyMotionCatalog catalog;
    [SerializeField] private Button shopButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button idleCategoryButton;
    [SerializeField] private Button victoryCategoryButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private RawImage previewViewport;
    [SerializeField] private ScrollRect itemScrollRect;
    [SerializeField] private RectTransform itemContentRoot;
    [SerializeField] private LobbyMotionShopItemButton itemButtonPrefab;
    [SerializeField] private GameObject previewCharacterPrefab;

    private LobbyMotionCatalog.MotionSlot selectedSlot =
        LobbyMotionCatalog.MotionSlot.Idle;
    private RenderTexture previewTexture;
    private Camera previewCamera;
    private GameObject previewCharacter;
    private CharacterAppearanceApplicator previewApplicator;
    private Animator previewAnimator;
    private RuntimeAnimatorController previewBaseController;
    private AnimatorOverrideController previewOverrideController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> previewOverrides =
        new List<KeyValuePair<AnimationClip, AnimationClip>>();

    private void Awake()
    {
        if (catalog == null)
        {
            catalog = LobbyMotionCatalog.LoadDefault();
        }

        if (shopButton != null)
        {
            shopButton.onClick.AddListener(OpenPanel);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }

        if (idleCategoryButton != null)
        {
            idleCategoryButton.onClick.AddListener(SelectIdleCategory);
        }

        if (victoryCategoryButton != null)
        {
            victoryCategoryButton.onClick.AddListener(SelectVictoryCategory);
        }

        ClosePanel();
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnEnable()
    {
        LobbyButtonController.MatchmakingViewStateChanged += ApplyMatchmakingVisibility;
        CharacterMotionStore.EquippedChanged += OnMotionChanged;
        CharacterMotionStore.VictoryEquippedChanged += OnMotionChanged;
        ApplyMatchmakingVisibility(LobbyButtonController.IsMatchmakingViewActive);
    }

    private void OnDisable()
    {
        LobbyButtonController.MatchmakingViewStateChanged -= ApplyMatchmakingVisibility;
        CharacterMotionStore.EquippedChanged -= OnMotionChanged;
        CharacterMotionStore.VictoryEquippedChanged -= OnMotionChanged;
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

        if (idleCategoryButton != null)
        {
            idleCategoryButton.onClick.RemoveListener(SelectIdleCategory);
        }

        if (victoryCategoryButton != null)
        {
            victoryCategoryButton.onClick.RemoveListener(SelectVictoryCategory);
        }

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
    }

    public void BindSceneReferences(
        LobbyMotionCatalog sceneCatalog,
        Button sceneShopButton,
        Button sceneCloseButton,
        RectTransform scenePanel,
        TextMeshProUGUI sceneStatusText,
        ScrollRect sceneItemScrollRect,
        RectTransform sceneItemContentRoot,
        LobbyMotionShopItemButton sceneItemButtonPrefab)
    {
        BindSceneReferences(
            sceneCatalog,
            sceneShopButton,
            sceneCloseButton,
            null,
            null,
            scenePanel,
            sceneStatusText,
            null,
            sceneItemScrollRect,
            sceneItemContentRoot,
            sceneItemButtonPrefab,
            null);
    }

    public void BindSceneReferences(
        LobbyMotionCatalog sceneCatalog,
        Button sceneShopButton,
        Button sceneCloseButton,
        Button sceneIdleCategoryButton,
        Button sceneVictoryCategoryButton,
        RectTransform scenePanel,
        TextMeshProUGUI sceneStatusText,
        RawImage scenePreviewViewport,
        ScrollRect sceneItemScrollRect,
        RectTransform sceneItemContentRoot,
        LobbyMotionShopItemButton sceneItemButtonPrefab,
        GameObject scenePreviewCharacterPrefab)
    {
        catalog = sceneCatalog;
        shopButton = sceneShopButton;
        closeButton = sceneCloseButton;
        idleCategoryButton = sceneIdleCategoryButton;
        victoryCategoryButton = sceneVictoryCategoryButton;
        panel = scenePanel;
        statusText = sceneStatusText;
        previewViewport = scenePreviewViewport;
        itemScrollRect = sceneItemScrollRect;
        itemContentRoot = sceneItemContentRoot;
        itemButtonPrefab = sceneItemButtonPrefab;
        previewCharacterPrefab = scenePreviewCharacterPrefab;
    }

    public void OpenPanel()
    {
        if (catalog == null)
        {
            catalog = LobbyMotionCatalog.LoadDefault();
        }

        if (panel == null)
        {
            return;
        }

        panel.gameObject.SetActive(true);
        UpdateCategoryButtons();
        EnsurePreviewScene();
        RebuildItemGrid();
        if (itemScrollRect != null)
        {
            itemScrollRect.verticalNormalizedPosition = 1f;
        }

        RefreshPreview(GetPreviewEntryForSelectedSlot());
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

    private void SelectMotion(LobbyMotionCatalog.MotionEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        if (!CharacterMotionStore.IsMotionUsable(entry, catalog))
        {
            SetStatus("Locked");
            return;
        }

        if (CharacterMotionStore.SaveIdleMotion(entry.Id, catalog))
        {
            RefreshPreview(entry);
            SetStatus("Selected: " + entry.DisplayName);
        }
    }

    private void SelectVictoryMotion(LobbyMotionCatalog.MotionEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        if (!CharacterMotionStore.IsMotionUsable(entry, catalog))
        {
            SetStatus("Locked");
            return;
        }

        if (CharacterMotionStore.SaveVictoryMotion(entry.Id, catalog))
        {
            RefreshPreview(entry);
            SetStatus("Selected: " + entry.DisplayName);
        }
    }

    private void RebuildItemGrid()
    {
        if (itemContentRoot == null || itemButtonPrefab == null)
        {
            return;
        }

        for (int i = itemContentRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = itemContentRoot.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        if (catalog == null || catalog.Entries == null)
        {
            SetStatus("Motion catalog missing.");
            return;
        }

        string equippedMotionId = GetEquippedMotionId();
        bool hasVisibleEntry = false;
        LobbyMotionCatalog.MotionEntry[] entries = catalog.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            LobbyMotionCatalog.MotionEntry entry = entries[i];
            if (entry == null || !entry.IsValid || entry.Slot != selectedSlot)
            {
                continue;
            }

            hasVisibleEntry = true;
            LobbyMotionShopItemButton itemButton =
                Instantiate(itemButtonPrefab, itemContentRoot);
            itemButton.gameObject.SetActive(true);
            LobbyMotionCatalog.MotionEntry capturedEntry = entry;
            bool unlocked = CharacterMotionStore.IsMotionUsable(entry, catalog);
            bool selected = string.Equals(
                equippedMotionId,
                entry.Id,
                System.StringComparison.Ordinal);
            itemButton.Configure(
                capturedEntry,
                unlocked,
                selected,
                () => SelectMotionForSelectedSlot(capturedEntry));
        }

        SetStatus(
            hasVisibleEntry
                ? GetChooseMotionStatus()
                : "No " + GetSelectedSlotLabel().ToLowerInvariant() + " motions.");
    }

    private void OnMotionChanged(string motionId)
    {
        if (panel != null && panel.gameObject.activeSelf)
        {
            RebuildItemGrid();
        }
    }

    private void SelectIdleCategory()
    {
        SelectCategory(LobbyMotionCatalog.MotionSlot.Idle);
    }

    private void SelectVictoryCategory()
    {
        SelectCategory(LobbyMotionCatalog.MotionSlot.Victory);
    }

    private void SelectCategory(LobbyMotionCatalog.MotionSlot slot)
    {
        if (selectedSlot == slot)
        {
            return;
        }

        selectedSlot = slot;
        UpdateCategoryButtons();
        RebuildItemGrid();
        RefreshPreview(GetPreviewEntryForSelectedSlot());
        if (itemScrollRect != null)
        {
            itemScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void SelectMotionForSelectedSlot(LobbyMotionCatalog.MotionEntry entry)
    {
        if (selectedSlot == LobbyMotionCatalog.MotionSlot.Victory)
        {
            SelectVictoryMotion(entry);
            return;
        }

        SelectMotion(entry);
    }

    private string GetEquippedMotionId()
    {
        if (selectedSlot == LobbyMotionCatalog.MotionSlot.Victory)
        {
            string equippedVictoryId = CharacterMotionStore.CurrentVictoryMotionId;
            if (!string.IsNullOrWhiteSpace(equippedVictoryId))
            {
                return equippedVictoryId;
            }

            LobbyMotionCatalog.MotionEntry defaultEntry =
                catalog != null
                    ? catalog.GetDefaultEntry(LobbyMotionCatalog.MotionSlot.Victory)
                    : null;
            return defaultEntry != null ? defaultEntry.Id : string.Empty;
        }

        return CharacterMotionStore.CurrentIdleMotionId;
    }

    private LobbyMotionCatalog.MotionEntry GetPreviewEntryForSelectedSlot()
    {
        if (catalog == null)
        {
            return null;
        }

        string equippedMotionId = GetEquippedMotionId();
        if (!string.IsNullOrWhiteSpace(equippedMotionId) &&
            catalog.TryGetEntryById(
                equippedMotionId,
                selectedSlot,
                out LobbyMotionCatalog.MotionEntry equippedEntry))
        {
            return equippedEntry;
        }

        return catalog.GetDefaultEntry(selectedSlot);
    }

    private string GetSelectedSlotLabel()
    {
        return selectedSlot == LobbyMotionCatalog.MotionSlot.Victory
            ? "Victory"
            : "Idle";
    }

    private string GetChooseMotionStatus()
    {
        return selectedSlot == LobbyMotionCatalog.MotionSlot.Victory
            ? "Choose a victory motion."
            : "Choose an idle motion.";
    }

    private void UpdateCategoryButtons()
    {
        if (idleCategoryButton != null)
        {
            idleCategoryButton.interactable =
                selectedSlot != LobbyMotionCatalog.MotionSlot.Idle;
        }

        if (victoryCategoryButton != null)
        {
            victoryCategoryButton.interactable =
                selectedSlot != LobbyMotionCatalog.MotionSlot.Victory;
        }
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

    private void SetStatus(string value)
    {
        if (statusText != null)
        {
            statusText.text = value;
        }
    }

    private void EnsurePreviewScene()
    {
        if (previewViewport == null)
        {
            return;
        }

        if (previewTexture == null)
        {
            previewTexture = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32);
            previewTexture.name = "MotionShopPreviewTexture";
            previewTexture.Create();
        }

        previewViewport.texture = previewTexture;

        if (previewCamera == null)
        {
            GameObject cameraObject = new GameObject("MotionShopPreviewCamera");
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.12f, 0.10f, 0.13f, 1f);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 1.15f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20f;
            previewCamera.targetTexture = previewTexture;
            previewCamera.transform.position =
                PreviewWorldPosition + new Vector3(0f, 1.48f, -4f);
            previewCamera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
        }

        previewCamera.enabled = true;

        if (previewCharacter == null && previewCharacterPrefab != null)
        {
            previewCharacter = Instantiate(
                previewCharacterPrefab,
                PreviewWorldPosition,
                Quaternion.Euler(0f, 180f, 0f));
            previewCharacter.name = "MotionShopPreviewCharacter";
            DisablePreviewRuntimeComponents(previewCharacter);
            ConfigurePreviewRenderers(previewCharacter);
            previewApplicator =
                previewCharacter.GetComponent<CharacterAppearanceApplicator>();
            if (previewApplicator == null)
            {
                previewApplicator =
                    previewCharacter.AddComponent<CharacterAppearanceApplicator>();
            }

            previewAnimator = previewCharacter.GetComponent<Animator>();
            if (previewAnimator == null)
            {
                previewAnimator = previewCharacter.GetComponentInChildren<Animator>(true);
            }

            if (previewAnimator != null)
            {
                previewBaseController = previewAnimator.runtimeAnimatorController;
                previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        if (previewCharacter != null)
        {
            previewCharacter.SetActive(true);
        }
    }

    private void RefreshPreview(LobbyMotionCatalog.MotionEntry entry)
    {
        EnsurePreviewScene();

        if (previewApplicator != null)
        {
            previewApplicator.ApplyAppearance(CharacterAppearanceStore.EquippedCode);
            if (previewCharacter != null)
            {
                ConfigurePreviewRenderers(previewCharacter);
            }
        }

        ApplyPreviewMotion(entry);
    }

    private void ApplyPreviewMotion(LobbyMotionCatalog.MotionEntry entry)
    {
        if (previewAnimator == null || previewBaseController == null)
        {
            return;
        }

        if (catalog == null ||
            entry == null ||
            !entry.IsValid ||
            !catalog.TryGetClip(entry.Id, entry.Slot, out AnimationClip clip))
        {
            RestorePreviewBaseController();
            return;
        }

        if (previewOverrideController == null ||
            previewOverrideController.runtimeAnimatorController != previewBaseController)
        {
            previewOverrideController =
                new AnimatorOverrideController(previewBaseController)
                {
                    name = previewBaseController.name + "_MotionShopPreviewOverride"
                };
        }

        previewOverrides.Clear();
        previewOverrideController.GetOverrides(previewOverrides);

        bool replaced = false;
        for (int i = 0; i < previewOverrides.Count; i++)
        {
            AnimationClip originalClip = previewOverrides[i].Key;
            if (!IsPreviewIdleTarget(originalClip))
            {
                continue;
            }

            previewOverrides[i] =
                new KeyValuePair<AnimationClip, AnimationClip>(originalClip, clip);
            replaced = true;
        }

        if (!replaced)
        {
            RestorePreviewBaseController();
            return;
        }

        previewOverrideController.ApplyOverrides(previewOverrides);
        previewAnimator.runtimeAnimatorController = previewOverrideController;
        previewAnimator.speed = catalog.GetPlaybackSpeed(entry.Id, entry.Slot);
        RebindPreviewAnimator();
    }

    private void RestorePreviewBaseController()
    {
        if (previewAnimator == null || previewBaseController == null)
        {
            return;
        }

        previewAnimator.runtimeAnimatorController = previewBaseController;
        previewAnimator.speed = 1f;
        RebindPreviewAnimator();
    }

    private void RebindPreviewAnimator()
    {
        if (previewAnimator == null || !previewAnimator.gameObject.activeInHierarchy)
        {
            return;
        }

        previewAnimator.Rebind();
        int idleStateHash = Animator.StringToHash(PreviewIdleClipName);
        if (previewAnimator.HasState(0, idleStateHash))
        {
            previewAnimator.Play(idleStateHash, 0, PreviewStartNormalizedTime);
        }

        previewAnimator.Update(1f / 30f);
    }

    private static bool IsPreviewIdleTarget(AnimationClip clip)
    {
        return clip != null &&
               string.Equals(
                   clip.name,
                   PreviewIdleClipName,
                   System.StringComparison.Ordinal);
    }

    private static void DisablePreviewRuntimeComponents(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        CharacterController[] controllers =
            target.GetComponentsInChildren<CharacterController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            controllers[i].enabled = false;
        }

        Canvas[] canvases = target.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].enabled = false;
        }

        MonoBehaviour[] behaviours =
            target.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null ||
                behaviour is CharacterAppearanceApplicator ||
                behaviour is LobbyMotionShopUI)
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
