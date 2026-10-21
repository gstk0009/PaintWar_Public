using System.Collections.Generic;
using UnityEngine;

// 씬 오브젝트를 타일 점령, 라운드 규칙, 스탯, 머티리얼에서 쓰는 PaintOwner와 연결합니다.
// 네트워크 플레이어는 Player.myPaintOwner SyncVar를 우선하고, 오프라인/봇 오브젝트는 serialized fallback owner를 사용합니다.
public class PlayerPaintIdentity : MonoBehaviour
{
    private static readonly List<PlayerPaintIdentity> ActiveIdentities = new List<PlayerPaintIdentity>(8);
    private static readonly Dictionary<PaintOwner, PlayerPaintIdentity> IdentityByOwner =
        new Dictionary<PaintOwner, PlayerPaintIdentity>();

    // 네트워크 SyncVar 데이터가 오기 전이나 로컬 시뮬레이션 오브젝트에서 사용하는 fallback owner입니다.
    [SerializeField] private PaintOwner owner = PaintOwner.Player1;

    private PlayerLevelStats levelStats;
    private Player player;
    private PaintOwner registeredOwner = PaintOwner.Neutral;

    public PaintOwner Owner
    {
        get { return player != null ? player.myPaintOwner.Value : owner; }
        set { SetOwner(value); }
    }

    private void Awake()
    {
        levelStats = GetComponent<PlayerLevelStats>();
        player = GetComponent<Player>();
    }

    private void OnEnable()
    {
        if (!ActiveIdentities.Contains(this))
        {
            ActiveIdentities.Add(this);
        }

        RefreshOwnerRegistration();
    }

    private void Start()
    {
        ApplyOwnerState();
    }

    private void OnDisable()
    {
        ActiveIdentities.Remove(this);

        if (registeredOwner != PaintOwner.Neutral &&
            IdentityByOwner.TryGetValue(registeredOwner, out PlayerPaintIdentity identity) &&
            identity == this)
        {
            IdentityByOwner.Remove(registeredOwner);
        }

        registeredOwner = PaintOwner.Neutral;
    }

    public void SetOwner(PaintOwner newOwner)
    {
        // 서버는 SyncVar를 직접 쓰고, 클라이언트는 Player의 ServerRpc를 통해 변경을 요청합니다.
        owner = newOwner;

        if (player != null)
        {
            if (player.IsServerInitialized)
                player.myPaintOwner.Value = newOwner;
            else if (player.IsClientInitialized)
                player.ServerCmdChangePaintOwner(newOwner);
        }

        ApplyOwnerState();
    }

    private void ApplyOwnerState()
    {
        // 스탯 등록은 현재 owner에 의존하므로 ownership이 바뀔 때마다 갱신합니다.
        if (levelStats == null) levelStats = GetComponent<PlayerLevelStats>();
        if (player == null) player = GetComponent<Player>();

        if (levelStats != null)
        {
            levelStats.RefreshOwnerRegistration();
        }

        RefreshOwnerRegistration();
    }

    public void RefreshOwnerRegistration()
    {
        if (registeredOwner != PaintOwner.Neutral &&
            IdentityByOwner.TryGetValue(registeredOwner, out PlayerPaintIdentity identity) &&
            identity == this)
        {
            IdentityByOwner.Remove(registeredOwner);
        }

        registeredOwner = Owner;
        if (registeredOwner != PaintOwner.Neutral)
        {
            IdentityByOwner[registeredOwner] = this;
        }
    }

    public static IReadOnlyList<PlayerPaintIdentity> ActivePlayers
    {
        get { return ActiveIdentities; }
    }

    public static bool TryGetByOwner(PaintOwner owner, out PlayerPaintIdentity identity)
    {
        return IdentityByOwner.TryGetValue(owner, out identity) && identity != null && identity.isActiveAndEnabled;
    }
}
