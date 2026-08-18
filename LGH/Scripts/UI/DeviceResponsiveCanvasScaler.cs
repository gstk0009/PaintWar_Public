using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public class DeviceResponsiveCanvasScaler : MonoBehaviour
{
    private enum ReferenceOrientation
    {
        Auto,
        Landscape,
        Portrait
    }

    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private ReferenceOrientation referenceOrientation = ReferenceOrientation.Auto;
    [SerializeField] private Vector2 landscapeReferenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 portraitReferenceResolution = new Vector2(1080f, 1920f);
    [SerializeField, Range(0f, 1f)] private float matchWidthOrHeight = 0.5f;
    [SerializeField] private CanvasScaler.ScreenMatchMode screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    [SerializeField] private RectTransform safeAreaRoot;
    [SerializeField] private bool applySafeAreaToRoot;

    private int lastWidth = -1;
    private int lastHeight = -1;
    private Rect lastSafeArea;

    private void Reset()
    {
        canvasScaler = GetComponent<CanvasScaler>();
    }

    private void Awake()
    {
        CacheCanvasScaler();
        ApplyIfNeeded(true);
    }

    private void OnEnable()
    {
        CacheCanvasScaler();
        ApplyIfNeeded(true);
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyIfNeeded(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheCanvasScaler();

        if (!Application.isPlaying)
        {
            lastWidth = -1;
            ApplyIfNeeded(true);
        }
    }
#endif

    private void CacheCanvasScaler()
    {
        if (canvasScaler == null)
        {
            canvasScaler = GetComponent<CanvasScaler>();
        }
    }

    private void ApplyIfNeeded(bool force)
    {
        if (canvasScaler == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        int screenWidth = Mathf.Max(1, Screen.width);
        int screenHeight = Mathf.Max(1, Screen.height);

        if (!force &&
            screenWidth == lastWidth &&
            screenHeight == lastHeight &&
            safeArea == lastSafeArea)
        {
            return;
        }

        lastWidth = screenWidth;
        lastHeight = screenHeight;
        lastSafeArea = safeArea;

        Vector2 screenSize = new Vector2(screenWidth, screenHeight);
        Vector2 referenceResolution = GetReferenceResolution(screenSize);

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = referenceResolution;
        canvasScaler.screenMatchMode = screenMatchMode;
        canvasScaler.matchWidthOrHeight = matchWidthOrHeight;

        ApplySafeAreaIfAssigned(screenWidth, screenHeight, safeArea);
    }

    private Vector2 GetReferenceResolution(Vector2 screenSize)
    {
        bool useLandscape = referenceOrientation == ReferenceOrientation.Landscape ||
                            (referenceOrientation == ReferenceOrientation.Auto && screenSize.x >= screenSize.y);

        Vector2 referenceResolution = useLandscape ? landscapeReferenceResolution : portraitReferenceResolution;
        referenceResolution.x = Mathf.Max(1f, referenceResolution.x);
        referenceResolution.y = Mathf.Max(1f, referenceResolution.y);
        return referenceResolution;
    }

    private void ApplySafeAreaIfAssigned(int screenWidth, int screenHeight, Rect safeArea)
    {
        if (!applySafeAreaToRoot || safeAreaRoot == null)
        {
            return;
        }

        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;

        anchorMin.x /= screenWidth;
        anchorMin.y /= screenHeight;
        anchorMax.x /= screenWidth;
        anchorMax.y /= screenHeight;

        safeAreaRoot.anchorMin = anchorMin;
        safeAreaRoot.anchorMax = anchorMax;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
    }
}
