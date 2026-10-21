// 타일 점령 규칙을 평가한 결과입니다.
// 네트워크 서버 검증과 로컬 테스트가 같은 결과 구조를 사용할 수 있게 분리했습니다.
public readonly struct TileClaimResult
{
    public readonly bool Changed;
    public readonly PaintOwner BeforeOwner;
    public readonly PaintOwner AfterOwner;

    public TileClaimResult(bool changed, PaintOwner beforeOwner, PaintOwner afterOwner)
    {
        Changed = changed;
        BeforeOwner = beforeOwner;
        AfterOwner = afterOwner;
    }
}
