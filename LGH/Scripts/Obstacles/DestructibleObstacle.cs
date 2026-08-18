using UnityEngine;

/// <summary>
/// Bomb implementations can call either overload of TryDestroyFromBomb on the server.
/// The field manager owns authority, replication, and contained-item spawning.
/// </summary>
[DisallowMultipleComponent]
public sealed class DestructibleObstacle : MonoBehaviour
{
    private ObstacleFieldManager fieldManager;
    private Vector2Int tileIndex;
    private bool containsItem;

    public Vector2Int TileIndex => tileIndex;
    public bool ContainsItem => containsItem;

    internal void Configure(
        ObstacleFieldManager manager,
        Vector2Int index,
        bool hasContainedItem)
    {
        fieldManager = manager;
        tileIndex = index;
        containsItem = hasContainedItem;
    }

    public bool TryDestroyFromBomb()
    {
        return fieldManager != null && fieldManager.ServerDestroyObstacle(this);
    }

    public bool TryDestroyFromBomb(Vector3 explosionPosition, float explosionRadius)
    {
        return fieldManager != null &&
               fieldManager.ServerDestroyInRadius(explosionPosition, explosionRadius) > 0;
    }
}
