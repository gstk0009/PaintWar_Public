using FishNet.Object;
using TMPro;
using UnityEngine;

public sealed class LobbyCurrentCharacterPreview : MonoBehaviour
{
    private const string PreviewCharacterName = "LobbyCurrentCharacterPreview";
    private const int PreviewLayer = 8;
    private const int PreviewLayerMask = 1 << PreviewLayer;

    private static readonly Bounds CharacterRenderBounds =
        new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(5f, 5f, 5f));

    [SerializeField] private GameObject previewCharacter;
    [SerializeField] private Vector3 previewWorldPosition = new Vector3(0f, 0f, -6.5f);
    [SerializeField] private Vector3 previewEulerAngles = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 previewScale = new Vector3(1.15f, 1.15f, 1.15f);
    [SerializeField] private bool hideWhenOwnedLobbyPlayerExists = true;
    [SerializeField] private float refreshInterval = 0.25f;

    private static LobbyCurrentCharacterPreview activeInstance;
    private static bool matchmakingHideRequested;

    private CharacterAppearanceApplicator previewApplicator;
    private CharacterMotionApplicator previewMotionApplicator;
    private string lastAppearanceCode;
    private string lastMotionCode;
    private float nextRefreshTime;
    private bool isConfigured;

    private void Awake()
    {
        ConfigurePreviewCharacter();
        RefreshPreview(true);
    }

    private void OnEnable()
    {
        activeInstance = this;
        CharacterAppearanceStore.Saved += OnAppearanceSaved;
        CharacterAppearanceStore.EquippedChanged += OnAppearanceSaved;
        CharacterMotionStore.EquippedChanged += OnMotionChanged;
        LobbyButtonController.MatchmakingViewStateChanged += OnMatchmakingViewStateChanged;
        RefreshPreview(true);
    }

    private void OnDisable()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }

        CharacterAppearanceStore.Saved -= OnAppearanceSaved;
        CharacterAppearanceStore.EquippedChanged -= OnAppearanceSaved;
        CharacterMotionStore.EquippedChanged -= OnMotionChanged;
        LobbyButtonController.MatchmakingViewStateChanged -= OnMatchmakingViewStateChanged;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        RefreshPreview(false);
    }

    private void OnAppearanceSaved(string appearanceCode)
    {
        RefreshPreview(true);
    }

    private void OnMotionChanged(string motionCode)
    {
        RefreshPreview(true);
    }

    private void OnMatchmakingViewStateChanged(bool active)
    {
        RefreshPreview(true);
    }

    public static void HideImmediatelyForMatchmaking()
    {
        matchmakingHideRequested = true;
        SetMainCameraPreviewLayerVisible(false);

        if (activeInstance != null)
        {
            activeInstance.SetPreviewCharacterActive(false);
            return;
        }

        GameObject preview = FindPreviewCharacterObject();
        if (preview != null)
        {
            preview.SetActive(false);
        }
    }

    public static void RefreshImmediatelyForFrontScreen()
    {
        matchmakingHideRequested = false;
        SetMainCameraPreviewLayerVisible(true);

        if (activeInstance != null)
        {
            activeInstance.RefreshPreview(true);
        }
    }

    private void ConfigurePreviewCharacter()
    {
        if (isConfigured)
        {
            return;
        }

        ResolvePreviewCharacter();
        if (previewCharacter == null)
        {
            return;
        }

        previewCharacter.name = PreviewCharacterName;
        SetLayerRecursively(previewCharacter, PreviewLayer);

        DisableRuntimeComponents(previewCharacter);
        ConfigureRenderers(previewCharacter);

        previewApplicator = previewCharacter.GetComponent<CharacterAppearanceApplicator>();
        if (previewApplicator == null)
        {
            previewApplicator = previewCharacter.AddComponent<CharacterAppearanceApplicator>();
        }

        previewMotionApplicator = previewCharacter.GetComponent<CharacterMotionApplicator>();
        if (previewMotionApplicator == null)
        {
            previewMotionApplicator = previewCharacter.AddComponent<CharacterMotionApplicator>();
        }

        isConfigured = true;
    }

    private void RefreshPreview(bool force)
    {
        ConfigurePreviewCharacter();
        ResolvePreviewCharacter();
        if (previewCharacter == null)
        {
            return;
        }

        previewCharacter.transform.SetPositionAndRotation(
            previewWorldPosition,
            Quaternion.Euler(previewEulerAngles));
        previewCharacter.transform.localScale = previewScale;

        bool hasOwnedLobbyPlayer = HasOwnedLobbyPlayerInScene(previewCharacter);
        bool shouldShow =
            !matchmakingHideRequested &&
            !LobbyButtonController.IsMatchmakingViewActive &&
            (!hideWhenOwnedLobbyPlayerExists || !hasOwnedLobbyPlayer);
        string currentAppearanceCode =
            CharacterAppearanceStore.NormalizeCode(CharacterAppearanceStore.EquippedCode);
        string currentMotionCode =
            CharacterMotionStore.NormalizeIdleMotionId(CharacterMotionStore.CurrentIdleMotionId);
        if (!force &&
            currentAppearanceCode == lastAppearanceCode &&
            currentMotionCode == lastMotionCode)
        {
            SetPreviewCharacterActive(shouldShow);
            return;
        }

        lastAppearanceCode = currentAppearanceCode;
        lastMotionCode = currentMotionCode;
        if (previewApplicator != null)
        {
            previewApplicator.ApplyAppearance(currentAppearanceCode);
        }

        if (previewMotionApplicator != null)
        {
            previewMotionApplicator.ApplyIdleMotion(currentMotionCode);
        }

        SetPreviewCharacterActive(shouldShow);
    }

    private void ResolvePreviewCharacter()
    {
        if (previewCharacter != null)
        {
            return;
        }

        GameObject found = FindPreviewCharacterObject();
        if (found != null && found != gameObject)
        {
            previewCharacter = found;
        }
    }

    private void SetPreviewCharacterActive(bool active)
    {
        SetMainCameraPreviewLayerVisible(active);
        ResolvePreviewCharacter();
        if (previewCharacter != null && previewCharacter.activeSelf != active)
        {
            previewCharacter.SetActive(active);
        }

        if (active && previewCharacter != null)
        {
            SetLayerRecursively(previewCharacter, PreviewLayer);
            DisableRuntimeComponents(previewCharacter);
            ConfigureRenderers(previewCharacter);
        }
    }

    private static void SetMainCameraPreviewLayerVisible(bool visible)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        if (visible)
        {
            mainCamera.cullingMask |= PreviewLayerMask;
        }
        else
        {
            mainCamera.cullingMask &= ~PreviewLayerMask;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
        {
            return;
        }

        target.layer = layer;
        Transform targetTransform = target.transform;
        for (int i = 0; i < targetTransform.childCount; i++)
        {
            SetLayerRecursively(targetTransform.GetChild(i).gameObject, layer);
        }
    }

    private static GameObject FindPreviewCharacterObject()
    {
        GameObject found = GameObject.Find(PreviewCharacterName);
        if (found != null)
        {
            return found;
        }

        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null && candidate.name == PreviewCharacterName)
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    private static bool HasOwnedLobbyPlayerInScene(GameObject previewRoot)
    {
        LobbyPlayer[] lobbyPlayers = FindObjectsByType<LobbyPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < lobbyPlayers.Length; i++)
        {
            LobbyPlayer lobbyPlayer = lobbyPlayers[i];
            if (lobbyPlayer == null ||
                (previewRoot != null &&
                 lobbyPlayer.transform.IsChildOf(previewRoot.transform)))
            {
                continue;
            }

            if (lobbyPlayer.IsOwner)
            {
                return true;
            }
        }

        return false;
    }

    private static void DisableRuntimeComponents(GameObject target)
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

        TextMeshProUGUI[] labels =
            target.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i].enabled = false;
        }

        MonoBehaviour[] behaviours =
            target.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null ||
                behaviour is CharacterAppearanceApplicator ||
                behaviour is CharacterMotionApplicator ||
                behaviour is LobbyCurrentCharacterPreview)
            {
                continue;
            }

            behaviour.enabled = false;
        }

        NetworkObject[] networkObjects =
            target.GetComponentsInChildren<NetworkObject>(true);
        for (int i = 0; i < networkObjects.Length; i++)
        {
            networkObjects[i].enabled = false;
        }
    }

    private static void ConfigureRenderers(GameObject target)
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
                skinnedRenderer.localBounds = CharacterRenderBounds;
            }
        }
    }
}
