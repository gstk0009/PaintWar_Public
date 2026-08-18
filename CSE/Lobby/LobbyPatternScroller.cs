using UnityEngine;
using UnityEngine.UI;

namespace CSE
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LobbyPatternScroller : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private RawImage rawImage;
        [SerializeField] private Renderer targetRenderer;

        [Header("Pattern")]
        [SerializeField] private Texture patternTexture;
        [SerializeField] private Vector2 tileCount = new Vector2(3f, 3f);
        [SerializeField] private Vector2 scrollSpeed = new Vector2(0.05f, -0.05f);
        [SerializeField] private Color patternTint = new Color(0.48f, 0.18f, 0.36f, 0.45f);

        [Header("Pixel Pattern Tiling")]
        [SerializeField] private bool autoTileFromRectSize = true;
        [SerializeField] private Vector2 tileSizePixels = new Vector2(32f, 32f);

        [Header("Options")]
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool assignTextureOnAwake = true;
        [SerializeField] private bool stretchToParent = true;

        private Material runtimeMaterial;
        private RectTransform rectTransform;
        private Vector2 offset;
        private Vector2 lastRectSize;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

private void Reset()
        {
            rawImage = GetComponent<RawImage>();
            targetRenderer = GetComponent<Renderer>();
            rectTransform = GetComponent<RectTransform>();
        }

private void Awake()
        {
            CacheTargets();

            if (patternTexture != null)
            {
                patternTexture.wrapMode = TextureWrapMode.Repeat;
            }

            if (assignTextureOnAwake)
            {
                ApplyTexture();
            }

            ApplyTiling();
        }

private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ApplyTiling();
        }

private void OnValidate()
        {
            CacheTargets();
            ApplyLayoutAndTint();

            if (patternTexture != null)
            {
                patternTexture.wrapMode = TextureWrapMode.Repeat;
            }

            ApplyTexture();
            ApplyTiling();
        }



        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            offset += scrollSpeed * deltaTime;
            offset.x = Mathf.Repeat(offset.x, 1f);
            offset.y = Mathf.Repeat(offset.y, 1f);

            Vector2 currentTileCount = GetCurrentTileCount();

            if (rawImage != null)
            {
                rawImage.uvRect = new Rect(offset, currentTileCount);
            }

            if (targetRenderer != null)
            {
                Material material = GetRuntimeMaterial();
                if (material != null && material.HasProperty(MainTexId))
                {
                    material.SetTextureOffset(MainTexId, offset);
                    material.SetTextureScale(MainTexId, currentTileCount);
                }
            }
        }

        public void SetPattern(Texture texture)
        {
            patternTexture = texture;
            if (patternTexture != null)
            {
                patternTexture.wrapMode = TextureWrapMode.Repeat;
            }

            ApplyTexture();
            ApplyTiling();
        }

        private void ApplyTexture()
        {
            if (patternTexture == null)
            {
                return;
            }

            if (rawImage != null)
            {
                rawImage.texture = patternTexture;
            }

            Material material = GetRuntimeMaterial();
            if (material != null && material.HasProperty(MainTexId))
            {
                material.SetTexture(MainTexId, patternTexture);
            }
        }

private void ApplyTiling()
        {
            Vector2 currentTileCount = GetCurrentTileCount();

            if (rawImage != null)
            {
                rawImage.uvRect = new Rect(offset, currentTileCount);
            }

            Material material = GetRuntimeMaterial();
            if (material != null && material.HasProperty(MainTexId))
            {
                material.SetTextureScale(MainTexId, currentTileCount);
            }
        }

private Vector2 GetCurrentTileCount()
        {
            if (!autoTileFromRectSize || rectTransform == null)
            {
                return tileCount;
            }

            Vector2 rectSize = rectTransform.rect.size;
            lastRectSize = rectSize;

            float tileWidth = Mathf.Max(1f, tileSizePixels.x);
            float tileHeight = Mathf.Max(1f, tileSizePixels.y);
            tileCount = new Vector2(
                Mathf.Max(1f, rectSize.x / tileWidth),
                Mathf.Max(1f, rectSize.y / tileHeight));

            return tileCount;
        }


        private Material GetRuntimeMaterial()
        {
            if (targetRenderer == null)
            {
                return null;
            }

            if (runtimeMaterial == null)
            {
                runtimeMaterial = targetRenderer.material;
            }

            return runtimeMaterial;
        }
    

private void CacheTargets()
        {
            if (rawImage == null)
            {
                rawImage = GetComponent<RawImage>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }
        }

private void ApplyLayoutAndTint()
        {
            if (rawImage != null)
            {
                rawImage.color = patternTint;
                rawImage.raycastTarget = false;
            }

            if (!stretchToParent || rectTransform == null)
            {
                return;
            }

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

}
}
