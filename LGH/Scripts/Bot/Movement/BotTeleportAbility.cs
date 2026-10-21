using FishNet.Component.Transforming;
using UnityEngine;

public sealed class BotTeleportAbility
{
    private readonly Transform transform;
    private readonly CharacterController characterController;
    private readonly PlayerLevelStats levelStats;
    private readonly NetworkTransform networkTransform;
    private readonly PlayerMovement playerMovement;
    private readonly PlayerStatusEffectController statusEffects;
    private readonly PaintOwner owner;
    private readonly int baseTileSteps;
    private readonly float fallbackTileDistance;
    private readonly float cooldownSeconds;

    private float cooldownEndsAt = -1f;
    private bool hasFollowThroughTarget;
    private Vector2Int followThroughTargetIndex;

    public float CooldownRemaining
    {
        get { return Mathf.Max(0f, cooldownEndsAt - Time.time); }
    }
    public int TileSteps { get { return GetTileSteps(); } }

    public BotTeleportAbility(
        Transform transform,
        CharacterController characterController,
        PlayerLevelStats levelStats,
        NetworkTransform networkTransform,
        PlayerMovement playerMovement,
        PlayerStatusEffectController statusEffects,
        PaintOwner owner,
        int baseTileSteps,
        float fallbackTileDistance,
        float cooldownSeconds)
    {
        this.transform = transform;
        this.characterController = characterController;
        this.levelStats = levelStats;
        this.networkTransform = networkTransform;
        this.playerMovement = playerMovement;
        this.statusEffects = statusEffects;
        this.owner = owner;
        this.baseTileSteps = Mathf.Max(1, baseTileSteps);
        this.fallbackTileDistance = Mathf.Max(0.01f, fallbackTileDistance);
        this.cooldownSeconds = Mathf.Max(0.1f, cooldownSeconds);
    }

    public bool TryTeleportToward(Vector3 worldTarget)
    {
        if (transform == null ||
            CooldownRemaining > 0f ||
            (statusEffects != null && statusEffects.IsStunned))
        {
            return false;
        }

        Vector3 direction = worldTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.01f)
        {
            return false;
        }

        return TryTeleport(direction.normalized);
    }

    public bool TryTeleport(Vector3 direction)
    {
        hasFollowThroughTarget = false;
        direction.y = 0f;
        if (transform == null ||
            direction.sqrMagnitude <= 0.0001f ||
            CooldownRemaining > 0f ||
            (statusEffects != null && statusEffects.IsStunned))
        {
            return false;
        }

        direction.Normalize();
        Vector3 targetPosition;
        if (!TryGetGridTarget(direction, out targetPosition))
        {
            if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
            {
                return false;
            }

            targetPosition = transform.position + direction * GetWorldDistance();
        }

        Vector3 startPosition = transform.position;
        ApplyPosition(targetPosition, direction);
        if (playerMovement != null)
        {
            playerMovement.ShowBotTeleportEffects(startPosition, transform.position);
        }
        else
        {
            BombMine.ConvertMinesAlongTeleportPath(startPosition, transform.position, owner);
        }

        cooldownEndsAt = Time.time + cooldownSeconds;
        return true;
    }

    public bool TryConsumeFollowThroughTarget(out Vector2Int targetIndex)
    {
        targetIndex = followThroughTargetIndex;
        bool result = hasFollowThroughTarget;
        hasFollowThroughTarget = false;
        return result;
    }

    private bool TryGetGridTarget(Vector3 direction, out Vector3 targetPosition)
    {
        targetPosition = transform.position;
        if (!TilePaintManager.HasInstance || !TilePaintManager.Instance.IsGridReady)
        {
            return false;
        }

        Vector2Int gridDirection = GetGridDirection(direction);
        if (gridDirection == Vector2Int.zero)
        {
            return false;
        }

        TilePaintManager tileManager = TilePaintManager.Instance;
        Vector2Int currentIndex = tileManager.GetGridIndex(transform.position);
        for (int step = GetTileSteps(); step >= 1; step--)
        {
            Vector2Int targetIndex = currentIndex + gridDirection * step;
            Vector2Int followIndex = targetIndex + gridDirection;
            if (!CanUseGridIndex(tileManager, targetIndex) ||
                !CanUseGridIndex(tileManager, followIndex))
            {
                continue;
            }

            Vector3 tileCenter = tileManager.GetTileCenterWorldPosition(targetIndex);
            targetPosition = new Vector3(
                tileCenter.x,
                tileManager.GetTileSurfaceY(targetIndex),
                tileCenter.z);
            followThroughTargetIndex = followIndex;
            hasFollowThroughTarget = true;
            return true;
        }

        return false;
    }

    private bool CanUseGridIndex(TilePaintManager tileManager, Vector2Int index)
    {
        if (!tileManager.IsTileWalkable(index) ||
            tileManager.IsHomeProtected(index) ||
            BombMine.IsBlockingTile(index))
        {
            return false;
        }

        return !GameRoundManager.HasInstance ||
               GameRoundManager.Instance.CanOwnerClaimTile(owner, index);
    }

    private void ApplyPosition(Vector3 targetPosition, Vector3 direction)
    {
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
        {
            characterController.enabled = false;
        }

        transform.position = targetPosition;

        if (controllerWasEnabled)
        {
            characterController.enabled = true;
        }

        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        if (networkTransform != null)
        {
            networkTransform.Teleport();
            networkTransform.ForceSend();
        }
    }

    private int GetTileSteps()
    {
        int level = levelStats != null ? levelStats.Level : 1;
        int steps = baseTileSteps;
        if (level >= 3)
        {
            steps++;
        }

        if (level >= 5)
        {
            steps++;
        }

        return steps;
    }

    private float GetWorldDistance()
    {
        if (TilePaintManager.HasInstance && TilePaintManager.Instance.IsGridReady)
        {
            return GetTileSteps() * Mathf.Max(0.0001f, TilePaintManager.Instance.TileSize);
        }

        return GetTileSteps() * fallbackTileDistance;
    }

    private static Vector2Int GetGridDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector2Int.zero;
        }

        direction.Normalize();
        return new Vector2Int(
            Mathf.Abs(direction.x) > 0.25f ? (direction.x > 0f ? 1 : -1) : 0,
            Mathf.Abs(direction.z) > 0.25f ? (direction.z > 0f ? 1 : -1) : 0);
    }
}
