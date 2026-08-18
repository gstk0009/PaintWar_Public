using UnityEngine;

public sealed class TeleportLockIndicator : MonoBehaviour
{
    private static GameObject sharedIndicatorPrefab;

    [SerializeField] private GameObject indicatorPrefab;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.25f, 0f);
    [SerializeField] private Vector3 localScale = Vector3.one;
    [SerializeField] private float targetVisualBoundsSize = 0.65f;
    [SerializeField] private Color fallbackColor = new Color(1f, 0.85f, 0.08f, 1f);
    [SerializeField] private float bobHeight = 0.07f;
    [SerializeField] private float bobSpeed = 4.5f;
    [SerializeField] private float rotationSpeed = 140f;

    private GameObject indicatorObject;
    private Renderer[] indicatorRenderers;
    private ParticleSystem[] indicatorParticles;
    private Animator[] indicatorAnimators;
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
        RestartVisualPlayback();
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

        KeepVisualAlive();

        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        indicatorObject.transform.localPosition = localOffset + Vector3.up * bob;
        indicatorObject.transform.Rotate(
            Vector3.up,
            rotationSpeed * Time.deltaTime,
            Space.Self);
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
        indicatorObject.name = "Teleport Lock Indicator";
        indicatorObject.transform.SetParent(transform, false);
        indicatorObject.transform.localPosition = localOffset;
        indicatorObject.transform.localRotation = Quaternion.identity;
        indicatorObject.transform.localScale = localScale;

        RemoveRuntimeBehaviours(indicatorObject);
        RemovePhysics(indicatorObject);
        ScaleVisualToTargetBounds(indicatorObject);
        CacheVisualComponents();

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

        indicatorRenderers = null;
        indicatorParticles = null;
        indicatorAnimators = null;

        if (shouldShow)
        {
            EnsureIndicator();
            indicatorObject.SetActive(true);
            RestartVisualPlayback();
        }
    }

    private void CacheVisualComponents()
    {
        indicatorRenderers = indicatorObject.GetComponentsInChildren<Renderer>(true);
        indicatorParticles = indicatorObject.GetComponentsInChildren<ParticleSystem>(true);
        indicatorAnimators = indicatorObject.GetComponentsInChildren<Animator>(true);
    }

    private void RestartVisualPlayback()
    {
        SetRenderersEnabled(true);

        if (indicatorParticles != null)
        {
            for (int i = 0; i < indicatorParticles.Length; i++)
            {
                ParticleSystem particle = indicatorParticles[i];
                if (particle == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = particle.main;
                main.loop = true;
                particle.Clear(true);
                particle.Play(true);
            }
        }

        if (indicatorAnimators != null)
        {
            for (int i = 0; i < indicatorAnimators.Length; i++)
            {
                Animator animator = indicatorAnimators[i];
                if (animator == null)
                {
                    continue;
                }

                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);
            }
        }
    }

    private void KeepVisualAlive()
    {
        if (!indicatorObject.activeSelf)
        {
            indicatorObject.SetActive(true);
        }

        SetRenderersEnabled(true);

        if (indicatorParticles == null)
        {
            return;
        }

        for (int i = 0; i < indicatorParticles.Length; i++)
        {
            ParticleSystem particle = indicatorParticles[i];
            if (particle != null && !particle.isPlaying)
            {
                particle.Play(true);
            }
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (indicatorRenderers == null)
        {
            return;
        }

        for (int i = 0; i < indicatorRenderers.Length; i++)
        {
            if (indicatorRenderers[i] != null)
            {
                indicatorRenderers[i].enabled = enabled;
            }
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
        indicatorMaterial.color = fallbackColor;
        indicatorRenderer.sharedMaterial = indicatorMaterial;
    }

    private void OnDisable()
    {
        Hide();
    }
}
