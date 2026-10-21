using UnityEngine;

// Player의 외형 코드, owner 색상, renderer/material 적용을 담당하는 partial입니다.
public partial class Player
{
    // 서버 기준 외형 코드를 설정하고 서버 오브젝트에 즉시 적용합니다.
    // 클라이언트는 appearanceCode SyncVar 변경으로 같은 외형을 적용합니다.
    public void ServerSetAppearanceCode(string newAppearanceCode)
    {
        string normalizedAppearanceCode =
            CharacterAppearanceStore.NormalizeCode(newAppearanceCode);
        appearanceCode.Value = normalizedAppearanceCode;
        ApplyAppearance(normalizedAppearanceCode);
    }

    public void ServerSetMotionCode(string newMotionCode)
    {
        string normalizedMotionCode =
            CharacterMotionStore.NormalizeProfileMotionCode(newMotionCode);
        motionCode.Value = normalizedMotionCode;
        ApplyMotion(normalizedMotionCode);
    }

    // appearanceCode SyncVar 변경 시 클라이언트 외형을 갱신합니다.
    private void OnAppearanceCodeChanged(string prev, string next, bool asServer)
    {
        ApplyAppearance(next);
    }

    private void OnMotionCodeChanged(string prev, string next, bool asServer)
    {
        ApplyMotion(next);
    }

    // 캐릭터 파츠 외형과 owner tint를 적용합니다.
    private void ApplyAppearance(string code)
    {
        if (appearanceApplicator == null)
        {
            appearanceApplicator = gameObject.AddComponent<CharacterAppearanceApplicator>();
        }

        appearanceApplicator.ApplyAppearance(code);
        ApplyCosmeticTint(appearanceApplicator);
        EnablePlayerRenderers();
    }

    private void ApplyMotion(string code)
    {
        if (motionApplicator == null)
        {
            motionApplicator = GetComponent<CharacterMotionApplicator>();
        }

        if (motionApplicator == null)
        {
            motionApplicator = gameObject.AddComponent<CharacterMotionApplicator>();
        }

        motionApplicator.ApplyProfileMotion(code);
    }

    // 현재 owner 색상을 코스메틱 파츠에 입힙니다.
    private void ApplyCosmeticTint(CharacterAppearanceApplicator applicator = null)
    {
        if (applicator == null)
        {
            applicator = appearanceApplicator;
        }

        if (applicator == null)
        {
            return;
        }

        applicator.SetCosmeticTint(
            GetPaintOwnerTintColor(myPaintOwner.Value),
            GetPaintOwnerTintMaterial(myPaintOwner.Value));
    }

    private Material GetPaintOwnerTintMaterial(PaintOwner owner)
    {
        if (!TilePaintManager.HasInstance)
        {
            return null;
        }

        return TilePaintManager.Instance.GetOwnerMaterial(owner);
    }

    // TilePaintManager owner material 색상을 우선 사용하고, 없으면 고정 fallback 색상을 반환합니다.
    private Color GetPaintOwnerTintColor(PaintOwner owner)
    {
        switch (owner)
        {
            case PaintOwner.Player1:
                return new Color32(0xC9, 0x2D, 0x48, 0xFF);
            case PaintOwner.Player2:
                return new Color32(0x27, 0x5B, 0xCB, 0xFF);
            case PaintOwner.Player3:
                return new Color32(0xD6, 0xAC, 0x00, 0xFF);
            case PaintOwner.Player4:
                return new Color32(0x11, 0x86, 0x5D, 0xFF);
            default:
                return new Color32(0x59, 0x66, 0x7A, 0xFF);
        }
    }

    // 모든 자식 renderer를 켜고 SkinnedMeshRenderer bounds를 안정화합니다.
    private void EnablePlayerRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = true;
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
                skinnedRenderer.localBounds = CharacterRenderBounds;
            }
        }
    }

    // Animator가 카메라 culling 때문에 멈추지 않도록 자식 Animator 설정을 맞춥니다.
    private void ConfigureAnimatorCulling()
    {
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
            {
                animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }
    }

    public Material GetPlayerMaterialColor()
    {
        if (TilePaintManager.HasInstance)
        {
            return TilePaintManager.Instance.GetOwnerMaterial(myPaintOwner.Value);
        }

        return null;
    }

    // owner material을 캐릭터용 runtime material로 복제해서 적용합니다.
    public bool TryApplyPaintOwnerMaterial()
    {
        if (playerRenderer == null)
        {
            playerRenderer = GetComponentInChildren<Renderer>();
        }

        if (playerRenderer == null || playerMaterial == null)
        {
            paintMaterialDirty = true;
            return false;
        }

        if (playerRenderer.sharedMaterial != playerMaterial)
        {
            playerRenderer.sharedMaterial = playerMaterial;
        }

        if (playerAppearanceProperties == null)
        {
            playerAppearanceProperties = new MaterialPropertyBlock();
        }

        playerRenderer.GetPropertyBlock(playerAppearanceProperties);
        playerAppearanceProperties.SetColor(
            PlayerColorProperty,
            GetPaintOwnerTintColor(myPaintOwner.Value));
        playerRenderer.SetPropertyBlock(playerAppearanceProperties);
        paintMaterialDirty = false;
        return true;
    }
}
