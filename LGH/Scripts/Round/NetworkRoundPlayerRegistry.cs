using System.Collections.Generic;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// FishNet connection怨?PaintOwner ?щ’ 留ㅽ븨??愿由ы빀?덈떎.
/// NetworkRoundController媛 湲몄뼱吏吏 ?딅룄濡??뚮젅?댁뼱 ?깅줉 ?곹깭留?遺꾨━?덉뒿?덈떎.
/// </summary>
/// <summary>
/// FishNet client id와 PaintOwner를 연결하는 서버 측 map입니다.
/// slot 사용 가능 여부는 이 클래스가 맡고 NetworkRoundController는 흐름에 집중하게 합니다.
/// </summary>
public sealed class NetworkRoundPlayerRegistry
{
    // Key는 FishNet ClientId입니다. 재접속 시 바뀌므로 재접속 로직이 새 id를 다시 등록합니다.
    private readonly Dictionary<int, RegisteredPlayer> registeredPlayers = new Dictionary<int, RegisteredPlayer>();
    private readonly PaintOwner[] ownerOrder;

    private struct RegisteredPlayer
    {
        public NetworkConnection Connection;
        public GameObject PlayerObject;
        public PaintOwner Owner;
    }

    public NetworkRoundPlayerRegistry(PaintOwner[] ownerOrder)
    {
        this.ownerOrder = ownerOrder;
    }

    public int RegisteredCount => registeredPlayers.Count;

    public bool TryGetOwner(NetworkConnection conn, out PaintOwner owner)
    {
        owner = PaintOwner.Neutral;
        if (conn == null)
        {
            return false;
        }

        if (!registeredPlayers.TryGetValue(conn.ClientId, out RegisteredPlayer player) ||
            !object.ReferenceEquals(player.Connection, conn))
        {
            return false;
        }

        owner = player.Owner;
        return owner != PaintOwner.Neutral;
    }

    public bool IsOwnerAllowed(NetworkConnection conn, PaintOwner owner)
    {
        // client가 다른 PaintOwner로 행동하지 못하게 tile/item RPC에서 사용합니다.
        return conn != null && conn.IsActive && conn.IsAuthenticated &&
               registeredPlayers.TryGetValue(conn.ClientId, out RegisteredPlayer player) &&
               object.ReferenceEquals(player.Connection, conn) && player.PlayerObject != null &&
               owner >= PaintOwner.Player1 && owner <= PaintOwner.Player4 && player.Owner == owner;
    }

    public bool TryGetRegisteredOwner(NetworkConnection conn, out PaintOwner owner)
    {
        return TryGetOwner(conn, out owner);
    }

    public void Register(NetworkConnection conn, GameObject playerObject, PaintOwner owner)
    {
        registeredPlayers[conn.ClientId] = new RegisteredPlayer
        {
            Connection = conn,
            PlayerObject = playerObject,
            Owner = owner
        };
    }

    public void Unregister(NetworkConnection conn)
    {
        if (conn != null)
        {
            registeredPlayers.Remove(conn.ClientId);
        }
    }

    public void UnregisterOwner(PaintOwner owner)
    {
        if (owner == PaintOwner.Neutral)
        {
            return;
        }

        List<int> removeClientIds = null;
        foreach (KeyValuePair<int, RegisteredPlayer> pair in registeredPlayers)
        {
            if (pair.Value.Owner != owner)
            {
                continue;
            }

            if (removeClientIds == null)
            {
                removeClientIds = new List<int>();
            }

            removeClientIds.Add(pair.Key);
        }

        if (removeClientIds == null)
        {
            return;
        }

        for (int i = 0; i < removeClientIds.Count; i++)
        {
            registeredPlayers.Remove(removeClientIds[i]);
        }
    }

    public PaintOwner GetFirstFreeOwner()
    {
        // 현재 connection mapping과 이미 생성된 GameRoundManager participant를 모두 피합니다.
        for (int i = 0; i < ownerOrder.Length; i++)
        {
            PaintOwner owner = ownerOrder[i];
            if (IsUsedByConnection(owner))
            {
                continue;
            }

            if (GameRoundManager.HasInstance && GameRoundManager.Instance.FindParticipant(owner) != null)
            {
                continue;
            }

            return owner;
        }

        return PaintOwner.Neutral;
    }

    public bool IsOwnerAvailable(PaintOwner owner)
    {
        if (owner == PaintOwner.Neutral || IsUsedByConnection(owner))
        {
            return false;
        }

        return !GameRoundManager.HasInstance ||
               GameRoundManager.Instance.FindParticipant(owner) == null;
    }

    private bool IsUsedByConnection(PaintOwner owner)
    {
        return IsOwnerUsedByConnection(owner);
    }

    public bool IsOwnerUsedByConnection(PaintOwner owner)
    {
        foreach (RegisteredPlayer registered in registeredPlayers.Values)
        {
            if (registered.Owner == owner)
            {
                return true;
            }
        }

        return false;
    }
}
