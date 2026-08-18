using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CSE
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LobbyBackgroundLayer : MonoBehaviour
    {
        [SerializeField] private bool keepBehindOtherUi = true;
        [SerializeField] private bool stretchToParent = true;
        [SerializeField] private Graphic[] nonInteractiveGraphics;
        [SerializeField] private bool renderAsWorldBackdropInPlay = true;
        [SerializeField] private float worldBackdropDistance = 18f;

        private Graphic[] runtimeGraphics;
        private GameObject worldBackdropRoot;
        private MeshRenderer solidBackdropRenderer;
        private MeshRenderer patternBackdropRenderer;
        private Material solidBackdropMaterial;
        private Material patternBackdropMaterial;
        private RawImage sourcePatternImage;
        private Image sourceSolidImage;
        private Vector2 patternOffset;

        private void Reset()
        {
            nonInteractiveGraphics = GetComponentsInChildren<Graphic>(true);
        }

        private void Awake()
        {
            ApplyLayerSettings();
            ApplyWorldBackdropState();
        }

        private void OnEnable()
        {
            ApplyLayerSettings();
            ApplyWorldBackdropState();
        }

private void OnValidate()
        {
            ApplyLayerSettings();
        }

        private void Update()
        {
            ApplyLayerSettings();
            ApplyWorldBackdropState();
            UpdateWorldBackdrop();
        }

        private void OnDisable()
        {
            RestoreUiGraphics();
            DestroyWorldBackdrop();
        }

        private void OnDestroy()
        {
            RestoreUiGraphics();
            DestroyWorldBackdrop();
        }


        private void ApplyLayerSettings()
        {
            if (keepBehindOtherUi)
            {
                transform.SetAsFirstSibling();
            }

            if (stretchToParent)
            {
                RectTransform rectTransform = transform as RectTransform;
                if (rectTransform != null)
                {
                    rectTransform.anchorMin = Vector2.zero;
                    rectTransform.anchorMax = Vector2.one;
                    rectTransform.offsetMin = Vector2.zero;
                    rectTransform.offsetMax = Vector2.zero;
                    rectTransform.anchoredPosition = Vector2.zero;
                    rectTransform.localScale = Vector3.one;
                }
            }

            runtimeGraphics = GetRuntimeGraphics();
            if (runtimeGraphics == null)
            {
                return;
            }

            for (int i = 0; i < runtimeGraphics.Length; i++)
            {
                if (runtimeGraphics[i] != null)
                {
                    runtimeGraphics[i].raycastTarget = false;
                }
            }
        }

        private void ApplyWorldBackdropState()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (!Application.isPlaying || !renderAsWorldBackdropInPlay)
            {
                RestoreUiGraphics();
                DestroyWorldBackdrop();
                return;
            }

            CacheSourceGraphics();
            EnsureWorldBackdrop();
            SetUiGraphicsEnabled(false);
        }

        private void UpdateWorldBackdrop()
        {
            if (Application.isBatchMode ||
                !Application.isPlaying ||
                !renderAsWorldBackdropInPlay ||
                worldBackdropRoot == null)
            {
                return;
            }

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                return;
            }

            float distance = Mathf.Max(1f, worldBackdropDistance);
            Transform cameraTransform = targetCamera.transform;
            worldBackdropRoot.transform.SetPositionAndRotation(
                cameraTransform.position + cameraTransform.forward * distance,
                cameraTransform.rotation);

            float height;
            if (targetCamera.orthographic)
            {
                height = targetCamera.orthographicSize * 2f;
            }
            else
            {
                height = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            float width = height * targetCamera.aspect;
            Vector3 scale = new Vector3(width, height, 1f);
            if (solidBackdropRenderer != null)
            {
                solidBackdropRenderer.transform.localScale = scale;
            }

            if (patternBackdropRenderer != null)
            {
                patternBackdropRenderer.transform.localScale = scale;
            }

            UpdateWorldBackdropMaterials();
        }

        private void EnsureWorldBackdrop()
        {
            if (worldBackdropRoot != null)
            {
                return;
            }

            worldBackdropRoot = new GameObject("LobbyWorldBackdrop");
            worldBackdropRoot.hideFlags = HideFlags.DontSave;

            solidBackdropRenderer = CreateBackdropQuad("Solid", 0f);
            patternBackdropRenderer = CreateBackdropQuad("Pattern", -0.02f);

            UpdateWorldBackdropMaterials();
        }

        private MeshRenderer CreateBackdropQuad(string objectName, float localForwardOffset)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = objectName;
            quad.hideFlags = HideFlags.DontSave;
            quad.transform.SetParent(worldBackdropRoot.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, localForwardOffset);

            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private void UpdateWorldBackdropMaterials()
        {
            if (solidBackdropRenderer != null)
            {
                if (solidBackdropMaterial == null)
                {
                    solidBackdropMaterial = CreateBackdropMaterial();
                    solidBackdropRenderer.sharedMaterial = solidBackdropMaterial;
                }

                Color color = sourceSolidImage != null
                    ? sourceSolidImage.color
                    : new Color(0.55f, 0.71f, 0.65f, 1f);
                SetMaterialColor(solidBackdropMaterial, color);
            }

            if (patternBackdropRenderer == null)
            {
                return;
            }

            if (patternBackdropMaterial == null)
            {
                patternBackdropMaterial = CreateBackdropMaterial();
                patternBackdropRenderer.sharedMaterial = patternBackdropMaterial;
            }

            if (sourcePatternImage != null && sourcePatternImage.texture != null)
            {
                sourcePatternImage.texture.wrapMode = TextureWrapMode.Repeat;
                SetMaterialTexture(patternBackdropMaterial, sourcePatternImage.texture);
                SetMaterialScale(patternBackdropMaterial, sourcePatternImage.uvRect.size);
                patternOffset += sourcePatternImage.uvRect.size.normalized * Time.unscaledDeltaTime * 0.18f;
                SetMaterialOffset(patternBackdropMaterial, patternOffset);
                Color patternColor = sourcePatternImage.color;
                patternColor.a = Mathf.Max(patternColor.a, 0.45f);
                SetMaterialColor(patternBackdropMaterial, patternColor);
            }
        }

        private Material CreateBackdropMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader);
            material.hideFlags = HideFlags.DontSave;
            ConfigureTransparentMaterial(material);
            return material;
        }

        private void CacheSourceGraphics()
        {
            if (sourcePatternImage == null)
            {
                sourcePatternImage = GetComponentInChildren<RawImage>(true);
            }

            if (sourceSolidImage == null)
            {
                sourceSolidImage = GetComponentInChildren<Image>(true);
            }
        }

        private Graphic[] GetRuntimeGraphics()
        {
            if (nonInteractiveGraphics != null && nonInteractiveGraphics.Length > 0)
            {
                return nonInteractiveGraphics;
            }

            return GetComponentsInChildren<Graphic>(true);
        }

        private void SetUiGraphicsEnabled(bool enabled)
        {
            runtimeGraphics = GetRuntimeGraphics();
            if (runtimeGraphics == null)
            {
                return;
            }

            for (int i = 0; i < runtimeGraphics.Length; i++)
            {
                if (runtimeGraphics[i] != null)
                {
                    runtimeGraphics[i].enabled = enabled;
                }
            }
        }

        private void RestoreUiGraphics()
        {
            SetUiGraphicsEnabled(true);
        }

        private void DestroyWorldBackdrop()
        {
            if (solidBackdropMaterial != null)
            {
                Destroy(solidBackdropMaterial);
                solidBackdropMaterial = null;
            }

            if (patternBackdropMaterial != null)
            {
                Destroy(patternBackdropMaterial);
                patternBackdropMaterial = null;
            }

            if (worldBackdropRoot != null)
            {
                Destroy(worldBackdropRoot);
                worldBackdropRoot = null;
            }

            solidBackdropRenderer = null;
            patternBackdropRenderer = null;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void SetMaterialTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        private static void SetMaterialScale(Material material, Vector2 scale)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", scale);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", scale);
            }
        }

        private static void SetMaterialOffset(Material material, Vector2 offset)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureOffset("_BaseMap", offset);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureOffset("_MainTex", offset);
            }
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }
        }
    }
}
