/// <summary>
/// 타일 하나의 소유권 변경 규칙만 담당합니다.
/// 중립 타일은 공격자 소유가 되고, 적 타일은 먼저 중립화되며, 내 타일은 변경되지 않습니다.
/// </summary>
public sealed class TileClaimRules
{
    public TileClaimResult Evaluate(PaintOwner beforeOwner, PaintOwner attacker)
    {
        if (attacker == PaintOwner.Neutral || beforeOwner == attacker)
        {
            return new TileClaimResult(false, beforeOwner, beforeOwner);
        }

        PaintOwner afterOwner = beforeOwner == PaintOwner.Neutral
            ? attacker
            : PaintOwner.Neutral;

        return new TileClaimResult(true, beforeOwner, afterOwner);
    }
}
