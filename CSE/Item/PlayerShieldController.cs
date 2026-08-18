using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerShieldController : MonoBehaviour
{
    public const float DefaultShieldSeconds = 5f;

    [SerializeField] private ShieldStatusVisual shieldVisual;

    private PlayerPaintIdentity identity;
    private float shieldEndsAt = -1f;

    public bool IsShielded
    {
        get { return Time.time < shieldEndsAt; }
    }

    public float RemainingSeconds
    {
        get { return Mathf.Max(0f, shieldEndsAt - Time.time); }
    }

    private void Awake()
    {
        identity = GetComponent<PlayerPaintIdentity>();
        EnsureVisualReference();
        if (shieldVisual != null)
        {
            shieldVisual.Hide();
        }
    }

    public void ApplyShield(float duration)
    {
        float effectDuration = Mathf.Max(0.1f, duration);
        shieldEndsAt = Mathf.Max(shieldEndsAt, Time.time + effectDuration);
        EnsureVisualReference();

        if (shieldVisual != null)
        {
            shieldVisual.Show(GetOwner(), RemainingSeconds);
        }

        Debug.Log(
            "[Item] Shield applied. owner=" + GetOwner() +
            " duration=" + RemainingSeconds.ToString("0.00") + ".");
    }

    public void ClearShield()
    {
        shieldEndsAt = -1f;
        if (shieldVisual != null)
        {
            shieldVisual.Hide();
        }
    }

    public static bool IsProtected(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        PlayerShieldController shield =
            target.GetComponentInParent<PlayerShieldController>();
        return shield != null && shield.IsShielded;
    }

    public static bool IsProtected(Component target)
    {
        return target != null && IsProtected(target.gameObject);
    }

    public static bool IsProtected(PlayerPaintIdentity target)
    {
        return target != null && IsProtected(target.gameObject);
    }

    public static bool ApplyShieldToOwner(PaintOwner owner, float duration)
    {
        if (!TryFindTargetByOwner(owner, out PlayerPaintIdentity target))
        {
            return false;
        }

        PlayerShieldController shield =
            target.GetComponent<PlayerShieldController>();
        if (shield == null)
        {
            Debug.LogWarning(
                "[Item] Shield target has no PlayerShieldController. owner=" +
                owner + ".");
            return false;
        }

        shield.ApplyShield(duration);
        return true;
    }

    private static bool TryFindTargetByOwner(
        PaintOwner owner,
        out PlayerPaintIdentity target)
    {
        if (PlayerPaintIdentity.TryGetByOwner(owner, out target))
        {
            return true;
        }

        if (GameRoundManager.HasInstance)
        {
            GameParticipant participant = GameRoundManager.Instance.FindParticipant(owner);
            if (participant != null && !participant.IsEliminated)
            {
                target = participant.Identity;
                if (target == null && participant.Transform != null)
                {
                    participant.Transform.TryGetComponent(out target);
                }

                if (target != null && target.isActiveAndEnabled)
                {
                    return true;
                }
            }
        }

        System.Collections.Generic.IReadOnlyList<PlayerPaintIdentity> identities =
            PlayerPaintIdentity.ActivePlayers;
        for (int i = 0; i < identities.Count; i++)
        {
            PlayerPaintIdentity identity = identities[i];
            if (identity != null &&
                identity.isActiveAndEnabled &&
                identity.Owner == owner)
            {
                target = identity;
                return true;
            }
        }

        target = null;
        return false;
    }

    private PaintOwner GetOwner()
    {
        if (identity == null)
        {
            identity = GetComponent<PlayerPaintIdentity>();
        }

        return identity != null ? identity.Owner : PaintOwner.Neutral;
    }

    private void EnsureVisualReference()
    {
        if (shieldVisual != null)
        {
            return;
        }

        shieldVisual = GetComponentInChildren<ShieldStatusVisual>(true);
    }

    private void OnDisable()
    {
        ClearShield();
    }
}
