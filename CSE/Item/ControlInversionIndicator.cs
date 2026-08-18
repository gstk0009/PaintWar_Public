using UnityEngine;

public class ControlInversionIndicator : MonoBehaviour
{
    private static GameObject sharedIndicatorPrefab;

    [SerializeField] private GameObject indicatorPrefab;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.25f, 0f);
    [SerializeField] private Vector3 localScale = Vector3.one;
    [SerializeField] private float targetVisualBoundsSize = 0.65f;
    [SerializeField] private Color indicatorColor = new Color(1f, 0.2f, 1f, 1f);
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobSpeed = 4f;
    [SerializeField] private float rotationSpeed = 180f;

    private GameObject indicatorObject;
    private float hideAtTime;
    private bool isVisible;

    public static void SetSharedIndicatorPrefab(GameObject prefab)
    {
        if (prefab != null)
        {
            sharedIndicatorPrefab = prefab;
        }
    }

    public void SetIndicatorPrefab(GameObject prefab)
    {
        if (prefab == null || indicatorPrefab == prefab)
        {
            return;
        }

        indicatorPrefab = prefab;
        RebuildIndicator();
    }

    public void Show(float duration)
    {
        EnsureIndicator();

        hideAtTime = Time.time + Mathf.Max(0.1f, duration);
        isVisible = true;
        indicatorObject.SetActive(true);
    }

    public void Hide()
    {
        isVisible = false;

        if (indicatorObject != null)
        {
            indicatorObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!isVisible || indicatorObject == null)
        {
            return;
        }

        if (Time.time >= hideAtTime)
        {
            Hide();
            return;
        }

        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        indicatorObject.transform.localPosition = localOffset + Vector3.up * bob;
        indicatorObject.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
    }

    private void EnsureIndicator()
    {
        if (indicatorObject != null)
        {
            return;
        }

        GameObject prefab = indicatorPrefab != null
            ? indicatorPrefab
            : sharedIndicatorPrefab;
        indicatorObject = prefab != null
            ? Instantiate(prefab, transform)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        indicatorObject.name = "Control Inversion Indicator";
        indicatorObject.transform.SetParent(transform, false);
        indicatorObject.transform.localPosition = localOffset;
        indicatorObject.transform.localRotation = Quaternion.identity;
        indicatorObject.transform.localScale = localScale;

        RemoveRuntimeBehaviours(indicatorObject);
        RemovePhysics(indicatorObject);
        ScaleVisualToTargetBounds(indicatorObject);

        if (prefab == null)
        {
            TintFallbackIndicator();
        }

        indicatorObject.SetActive(false);
    }

    private void RebuildIndicator()
    {
        bool shouldShow = isVisible;
        if (indicatorObject != null)
        {
            Destroy(indicatorObject);
            indicatorObject = null;
        }

        if (shouldShow)
        {
            EnsureIndicator();
            indicatorObject.SetActive(true);
        }
    }

    private static void RemovePhysics(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Destroy(colliders[i]);
        }

        Rigidbody[] rigidbodies = target.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Destroy(rigidbodies[i]);
        }
    }

    private static void RemoveRuntimeBehaviours(GameObject target)
    {
        MonoBehaviour[] behaviours = target.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                Destroy(behaviours[i]);
            }
        }
    }

    private void ScaleVisualToTargetBounds(GameObject target)
    {
        if (!TryGetRendererBounds(target, out Bounds bounds))
        {
            target.transform.localScale = localScale * 0.65f;
            return;
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize <= 0.001f)
        {
            return;
        }

        float scale = Mathf.Max(0.01f, targetVisualBoundsSize / maxSize);
        target.transform.localScale = localScale * scale;
    }

    private static bool TryGetRendererBounds(GameObject target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds(target.transform.position, Vector3.one);
        bool hasRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null)
            {
                continue;
            }

            if (!hasRenderer)
            {
                bounds = itemRenderer.bounds;
                hasRenderer = true;
            }
            else
            {
                bounds.Encapsulate(itemRenderer.bounds);
            }
        }

        return hasRenderer;
    }

    private void TintFallbackIndicator()
    {
        Renderer indicatorRenderer = indicatorObject.GetComponent<Renderer>();
        if (indicatorRenderer == null)
        {
            return;
        }

        Material indicatorMaterial = new Material(indicatorRenderer.sharedMaterial);
        indicatorMaterial.color = indicatorColor;
        indicatorRenderer.sharedMaterial = indicatorMaterial;
    }

    private void OnDisable()
    {
        Hide();
    }
}
