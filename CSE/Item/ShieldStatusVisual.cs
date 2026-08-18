using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ShieldStatusVisual : MonoBehaviour
{
    [SerializeField] private LineRenderer[] shieldRings;
    [SerializeField] private Renderer[] tintRenderers;
    [SerializeField] private float rotationSpeed = 28f;
    [SerializeField] private float pulseSpeed = 4.8f;
    [SerializeField] private float pulseScale = 0.035f;
    [SerializeField] private float continuousEmissionRate = 0.55f;
    [SerializeField] private float trailContinuousEmissionRate = 2.2f;

    private Renderer[] cachedRenderers;
    private ParticleSystem[] particles;
    private ParticleSystem.MinMaxGradient[] originalStartColors;
    private ParticleSystem.MinMaxGradient[] originalLifetimeColors;
    private bool[] originalLifetimeEnabled;
    private float hideAtTime;
    private bool visible;

    public void Show(PaintOwner owner, float duration)
    {
        EnsureVisualComponents();
        hideAtTime = Time.time + Mathf.Max(0.1f, duration);
        visible = true;
        gameObject.SetActive(true);
        RestorePrefabColors();
        SetVisible(true);
    }

    public void Hide()
    {
        visible = false;
        SetVisible(false);
    }

    private void Awake()
    {
        EnsureVisualComponents();
        SetVisible(false);
    }

    private void Update()
    {
        if (!visible)
        {
            return;
        }

        if (Time.time >= hideAtTime)
        {
            Hide();
            return;
        }

        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
        transform.localScale = Vector3.one * pulse;
        KeepParticlesAlive();
    }

    private void EnsureVisualComponents()
    {
        DisableLegacyRings();

        if (cachedRenderers == null || cachedRenderers.Length == 0)
        {
            cachedRenderers = tintRenderers != null && tintRenderers.Length > 0
                ? tintRenderers
                : GetComponentsInChildren<Renderer>(true);
        }

        if (particles == null)
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
            CachePrefabParticleColors();
            ConfigureParticles();
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer renderer = cachedRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private void CachePrefabParticleColors()
    {
        if (particles == null)
        {
            originalStartColors = null;
            originalLifetimeColors = null;
            originalLifetimeEnabled = null;
            return;
        }

        originalStartColors = new ParticleSystem.MinMaxGradient[particles.Length];
        originalLifetimeColors = new ParticleSystem.MinMaxGradient[particles.Length];
        originalLifetimeEnabled = new bool[particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            originalStartColors[i] = main.startColor;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                particle.colorOverLifetime;
            originalLifetimeEnabled[i] = colorOverLifetime.enabled;
            originalLifetimeColors[i] = colorOverLifetime.color;
        }
    }

    private void RestorePrefabColors()
    {
        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer renderer = cachedRenderers[i];
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(null);
                }
            }
        }

        if (particles == null || originalStartColors == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null || i >= originalStartColors.Length)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.startColor = originalStartColors[i];

            if (originalLifetimeEnabled != null &&
                originalLifetimeColors != null &&
                i < originalLifetimeEnabled.Length &&
                i < originalLifetimeColors.Length)
            {
                ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                    particle.colorOverLifetime;
                colorOverLifetime.enabled = originalLifetimeEnabled[i];
                colorOverLifetime.color = originalLifetimeColors[i];
            }
        }
    }

    private void ConfigureParticles()
    {
        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.enabled = true;
            emission.rateOverTimeMultiplier = Mathf.Max(
                emission.rateOverTimeMultiplier,
                GetContinuousEmissionRate(particle));
        }
    }

    private void KeepParticlesAlive()
    {
        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            if (!particle.isPlaying || !particle.IsAlive(true))
            {
                particle.Play(true);
            }
        }
    }

    private float GetContinuousEmissionRate(ParticleSystem particle)
    {
        if (particle != null &&
            particle.name.IndexOf(
                "Trail",
                System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Mathf.Max(0f, trailContinuousEmissionRate);
        }

        return Mathf.Max(0f, continuousEmissionRate);
    }

    private void SetVisible(bool isVisible)
    {
        DisableLegacyRings();

        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] != null)
                {
                    cachedRenderers[i].enabled = isVisible;
                }
            }
        }

        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            if (isVisible)
            {
                particle.Play(true);
            }
            else
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private void DisableLegacyRings()
    {
        if (shieldRings == null)
        {
            return;
        }

        for (int i = 0; i < shieldRings.Length; i++)
        {
            if (shieldRings[i] != null)
            {
                shieldRings[i].enabled = false;
            }
        }
    }

    private void OnDisable()
    {
        Hide();
        transform.localScale = Vector3.one;
    }
}
