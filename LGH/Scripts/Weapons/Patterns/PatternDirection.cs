using UnityEngine;

public static class PatternDirection
{
    public static Vector2Int Forward(Vector3 forward)
    {
        Vector3 flat = new Vector3(forward.x, 0f, forward.z);
        if (flat.sqrMagnitude < 0.001f)
        {
            return Vector2Int.up;
        }

        flat.Normalize();
        if (Mathf.Abs(flat.x) > Mathf.Abs(flat.z))
        {
            return flat.x >= 0f ? Vector2Int.right : Vector2Int.left;
        }

        return flat.z >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    public static Vector2Int Right(Vector2Int forward)
    {
        return new Vector2Int(forward.y, -forward.x);
    }
}
