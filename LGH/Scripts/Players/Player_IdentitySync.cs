/// <summary>
/// Player의 PaintOwner SyncVar 변경과 owner 기반 캐시 갱신을 담당합니다.
/// </summary>
public partial class Player
{
    /// <summary>
    /// 서버에서 플레이어 owner를 직접 설정합니다.
    /// </summary>
    public void ServerSetPaintOwner(PaintOwner newOwner)
    {
        myPaintOwner.Value = newOwner;
    }

    /// <summary>
    /// owner SyncVar 변경 시 stats registry와 캐릭터 색상을 갱신합니다.
    /// </summary>
    private void OnPaintOwnerChanged(PaintOwner prev, PaintOwner next, bool asServer)
    {
        if (levelStats == null)
        {
            levelStats = GetComponent<PlayerLevelStats>();
        }

        if (levelStats != null)
        {
            levelStats.RefreshOwnerRegistration();
        }

        PlayerPaintIdentity identity = GetComponent<PlayerPaintIdentity>();
        if (identity != null)
        {
            identity.RefreshOwnerRegistration();
        }

        paintMaterialDirty = true;
        if (TryApplyPaintOwnerMaterial())
        {
            ApplyCosmeticTint();
        }

        ApplySkillAimEffectColor(next);
    }
}
