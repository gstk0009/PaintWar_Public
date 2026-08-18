using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CharacterAppearanceApplicator : MonoBehaviour
{
    private const string RuntimeCosmeticRootName = "RuntimeCosmetics";
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int MainColorProperty = Shader.PropertyToID("_MainColor");
    private static readonly int TintColorProperty = Shader.PropertyToID("_TintColor");
    private static readonly int GridColorProperty = Shader.PropertyToID("_GridColor");
    private static readonly int PlayerColorProperty = Shader.PropertyToID("_PlayerColor");

    [SerializeField] private Material sharedClothingMaterial;
    private Transform runtimeRoot;
    private readonly Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
    private readonly List<Renderer> tintableCosmeticRenderers = new List<Renderer>();
    private bool hasCosmeticTint;
    private Color cosmeticTint = Color.white;
    private MaterialPropertyBlock cosmeticProperties;

    public void SetCosmeticTint(Color tint)
    {
        SetCosmeticTint(tint, null);
    }

    public void SetCosmeticTint(Color tint, Material tintMaterial)
    {
        hasCosmeticTint = true;
        cosmeticTint = tint;
        ApplyTintToCosmetics();
    }

    public void ApplyAppearance(string appearanceCode)
    {
        CharacterCosmeticCatalog catalog = CharacterCosmeticCatalog.LoadDefault();
        if (catalog == null)
        {
            return;
        }

        EnsureRuntimeRoot();
        ClearRuntimeCosmetics();
        BuildBoneMap();

        Dictionary<CharacterCosmeticSlot, string> selections =
            CharacterAppearanceStore.Decode(appearanceCode);

        foreach (KeyValuePair<CharacterCosmeticSlot, string> selection in selections)
        {
            // Legacy Body selections are ignored because the player prefab already
            // owns its base skinned mesh.
            if (selection.Key == CharacterCosmeticSlot.Body)
            {
                continue;
            }

            CharacterCosmeticCatalog.CosmeticItem item =
                catalog.FindItem(selection.Key, selection.Value);
            if (item == null)
            {
                continue;
            }

            GameObject cosmeticPrefab = item.LoadPrefab();
            if (cosmeticPrefab == null)
            {
                Debug.LogWarning("[Shop] Could not load cosmetic prefab: " + item.DisplayName);
                continue;
            }

            if (!AttachCosmetic(cosmeticPrefab, selection.Key))
            {
                Debug.LogWarning("[Shop] Could not attach cosmetic prefab: " + item.DisplayName);
            }
        }
    }

    private void EnsureRuntimeRoot()
    {
        if (runtimeRoot != null)
        {
            return;
        }

        Transform found = transform.Find(RuntimeCosmeticRootName);
        if (found != null)
        {
            runtimeRoot = found;
            return;
        }

        GameObject rootObject = new GameObject(RuntimeCosmeticRootName);
        runtimeRoot = rootObject.transform;
        runtimeRoot.SetParent(transform, false);
    }

    private void ClearRuntimeCosmetics()
    {
        tintableCosmeticRenderers.Clear();

        for (int i = runtimeRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(runtimeRoot.GetChild(i).gameObject);
        }
    }

    private void BuildBoneMap()
    {
        boneMap.Clear();
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform target = transforms[i];
            if (target == null)
            {
                continue;
            }

            AddBoneAlias(target.name, target);
            AddBoneAlias(NormalizeBoneName(target.name), target);
        }
    }

    private bool AttachCosmetic(GameObject prefab, CharacterCosmeticSlot slot)
    {
        Object clone = Instantiate((Object)prefab, runtimeRoot, false);
        GameObject instance = clone as GameObject;
        if (instance == null && clone is Component component)
        {
            instance = component.gameObject;
        }

        if (instance == null)
        {
            if (clone != null)
            {
                Destroy(clone);
            }

            return false;
        }

        instance.name = prefab.name;
        SetActiveRecursively(instance, true);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        DisableRuntimeBehaviours(instance);
        RemapSkinnedMeshes(instance);
        EnableRenderers(instance);
        RegisterTintableRenderers(instance, slot);
        SetLayerRecursively(instance, gameObject.layer);
        return true;
    }

    private void RegisterTintableRenderers(GameObject instance, CharacterCosmeticSlot slot)
    {
        if (!IsTintableSlot(slot))
        {
            return;
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            tintableCosmeticRenderers.Add(renderer);

            if (hasCosmeticTint)
            {
                ApplyTintToRenderer(renderer, cosmeticTint);
            }
        }
    }

    private void ApplyTintToCosmetics()
    {
        for (int i = tintableCosmeticRenderers.Count - 1; i >= 0; i--)
        {
            Renderer renderer = tintableCosmeticRenderers[i];
            if (renderer == null)
            {
                tintableCosmeticRenderers.RemoveAt(i);
                continue;
            }

            ApplyTintToRenderer(renderer, cosmeticTint);
        }
    }

    private void ApplyTintToRenderer(Renderer renderer, Color tint)
    {
        if (sharedClothingMaterial != null)
        {
            Material[] replacementMaterials = renderer.sharedMaterials;
            for (int i = 0; i < replacementMaterials.Length; i++)
            {
                replacementMaterials[i] = sharedClothingMaterial;
            }

            renderer.sharedMaterials = replacementMaterials;
        }

        Color clothingTint = GetClothingTint(tint);
        if (cosmeticProperties == null)
        {
            cosmeticProperties = new MaterialPropertyBlock();
        }

        renderer.GetPropertyBlock(cosmeticProperties);
        cosmeticProperties.SetColor(PlayerColorProperty, clothingTint);
        cosmeticProperties.SetColor(BaseColorProperty, clothingTint);
        cosmeticProperties.SetColor(ColorProperty, clothingTint);
        cosmeticProperties.SetColor(MainColorProperty, clothingTint);
        cosmeticProperties.SetColor(TintColorProperty, clothingTint);
        cosmeticProperties.SetColor(GridColorProperty, clothingTint);
        renderer.SetPropertyBlock(cosmeticProperties);
    }

    private static Color GetClothingTint(Color tint)
    {
        return tint;
    }

    private static bool IsTintableSlot(CharacterCosmeticSlot slot)
    {
        switch (slot)
        {
            case CharacterCosmeticSlot.Hat:
            case CharacterCosmeticSlot.Accessories:
            case CharacterCosmeticSlot.Pants:
            case CharacterCosmeticSlot.Outerwear:
            case CharacterCosmeticSlot.Shoes:
            case CharacterCosmeticSlot.FullBody:
            case CharacterCosmeticSlot.Gloves:
                return true;
            default:
                return false;
        }
    }

    private void RemapSkinnedMeshes(GameObject instance)
    {
        SkinnedMeshRenderer[] renderers =
            instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = true;
            renderer.updateWhenOffscreen = true;

            Transform[] sourceBones = renderer.bones;
            Transform[] remappedBones = new Transform[sourceBones.Length];
            bool hasRemappedBone = false;

            for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
            {
                Transform sourceBone = sourceBones[boneIndex];
                if (sourceBone != null && TryGetBone(sourceBone.name, out Transform targetBone))
                {
                    remappedBones[boneIndex] = targetBone;
                    hasRemappedBone = true;
                }
                else
                {
                    remappedBones[boneIndex] = sourceBone;
                }
            }

            if (hasRemappedBone)
            {
                renderer.bones = remappedBones;
            }

            if (renderer.rootBone != null && TryGetBone(renderer.rootBone.name, out Transform rootBone))
            {
                renderer.rootBone = rootBone;
            }

            renderer.localBounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(4f, 4f, 4f));
        }
    }

    private void AddBoneAlias(string key, Transform target)
    {
        if (string.IsNullOrWhiteSpace(key) || boneMap.ContainsKey(key))
        {
            return;
        }

        boneMap.Add(key, target);
    }

    private bool TryGetBone(string sourceName, out Transform target)
    {
        if (boneMap.TryGetValue(sourceName, out target))
        {
            return true;
        }

        return boneMap.TryGetValue(NormalizeBoneName(sourceName), out target);
    }

    private static string NormalizeBoneName(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
        {
            return string.Empty;
        }

        int separatorIndex = boneName.LastIndexOf(':');
        string normalized = separatorIndex >= 0 && separatorIndex < boneName.Length - 1
            ? boneName.Substring(separatorIndex + 1)
            : boneName;

        return normalized.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    }

    private static void EnableRenderers(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = true;
        }
    }

    private static void DisableRuntimeBehaviours(GameObject instance)
    {
        Animator[] animators = instance.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].enabled = false;
        }

        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        Transform targetTransform = target.transform;
        for (int i = 0; i < targetTransform.childCount; i++)
        {
            SetLayerRecursively(targetTransform.GetChild(i).gameObject, layer);
        }
    }

    private static void SetActiveRecursively(GameObject target, bool active)
    {
        target.SetActive(active);
        Transform targetTransform = target.transform;
        for (int i = 0; i < targetTransform.childCount; i++)
        {
            SetActiveRecursively(targetTransform.GetChild(i).gameObject, active);
        }
    }
}
