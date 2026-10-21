using System.Collections.Generic;
using UnityEngine;

public static class SolidColorMaterialUtility
{
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly Dictionary<MaterialCacheKey, Material> Cache =
        new Dictionary<MaterialCacheKey, Material>();
    private static Shader litShader;

    public static Material Create(Material source, string suffix = "_Solid")
    {
        return GetShared(source, GetSourceColor(source), suffix);
    }

    public static Material GetShared(Material source, Color color, string suffix = "_Solid")
    {
        if (source == null)
        {
            return null;
        }

        MaterialCacheKey key = new MaterialCacheKey(source.GetInstanceID(), (Color32)color);
        if (Cache.TryGetValue(key, out Material cached) && cached != null)
        {
            return cached;
        }

        Material material = new Material(source)
        {
            name = source.name + suffix
        };

        if (litShader == null)
        {
            litShader = Shader.Find(LitShaderName);
        }

        if (litShader != null)
        {
            material.shader = litShader;
        }

        if (material.HasProperty(BaseColorProperty))
        {
            material.SetColor(BaseColorProperty, color);
        }

        if (material.HasProperty(ColorProperty))
        {
            material.SetColor(ColorProperty, color);
        }

        Cache[key] = material;
        return material;
    }

    private static Color GetSourceColor(Material source)
    {
        if (source == null)
        {
            return Color.white;
        }

        if (source.HasProperty(BaseColorProperty))
        {
            return source.GetColor(BaseColorProperty);
        }

        return source.HasProperty(ColorProperty)
            ? source.GetColor(ColorProperty)
            : Color.white;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        foreach (Material material in Cache.Values)
        {
            if (material == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(material);
            }
            else
            {
                Object.DestroyImmediate(material);
            }
        }

        Cache.Clear();
        litShader = null;
    }

    private readonly struct MaterialCacheKey
    {
        private readonly int sourceId;
        private readonly Color32 color;

        public MaterialCacheKey(int sourceId, Color32 color)
        {
            this.sourceId = sourceId;
            this.color = color;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialCacheKey other &&
                   sourceId == other.sourceId &&
                   color.Equals(other.color);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (sourceId * 397) ^ color.GetHashCode();
            }
        }
    }
}
