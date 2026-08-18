using FishNet.Broadcast;

public struct RoundStartRequestBroadcast : IBroadcast
{
    // 값 자체보다 "클라이언트가 시작 요청을 보냈다"는 신호가 중요합니다.
    public byte Requested;
}

/// <summary>
/// 서버가 현재 라운드 표시 상태를 클라이언트에 알려주는 broadcast 데이터입니다.
/// 참가자/봇/탈락 여부는 owner별 bit mask로 압축합니다. bit 0은 Player1, bit 1은 Player2입니다.
/// </summary>
public struct RoundStateBroadcast : IBroadcast
{
    public int Phase;
    public int RoundIndex;
    public float PhaseRemainingSeconds;
    public float MatchWaitRemainingSeconds;
    public int WinnerOwner;
    public int RoundResultEliminatedOwner;
    public int SecondPlaceOwner;
    public int ThirdPlaceOwner;
    public int FourthPlaceOwner;
    public int ParticipantMask;
    public int BotMask;
    public int EliminatedMask;
    public int ItemSpawnSeed;
    public byte FinalCountdownActive;
    public int Player1TileCount;
    public int Player2TileCount;
    public int Player3TileCount;
    public int Player4TileCount;
}

public struct RoundRosterBroadcast : IBroadcast
{
    public int ParticipantMask;
    public int BotMask;
    public string Player1DisplayName;
    public string Player2DisplayName;
    public string Player3DisplayName;
    public string Player4DisplayName;
}
