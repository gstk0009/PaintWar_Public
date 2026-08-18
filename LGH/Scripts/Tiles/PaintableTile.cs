using UnityEngine;

public class PaintableTile : MonoBehaviour
{
    private Renderer tileRenderer;
    private Material neutralMaterial;

    public PaintOwner Owner { get; private set; }
    public float LastClaimTime { get; private set; }

    public void Initialize()
    {
        if (tileRenderer == null)
        {
            tileRenderer = GetComponent<Renderer>();
        }

        if (tileRenderer != null && neutralMaterial == null)
        {
            neutralMaterial = tileRenderer.sharedMaterial;
        }
    }

    public void SetOwner(PaintOwner owner, Material ownerMaterial)
    {
        Initialize();

        Owner = owner;
        LastClaimTime = Time.time;

        if (tileRenderer == null)
        {
            return;
        }

        tileRenderer.sharedMaterial = owner == PaintOwner.Neutral
            ? ownerMaterial != null ? ownerMaterial : neutralMaterial
            : ownerMaterial;
    }
}
