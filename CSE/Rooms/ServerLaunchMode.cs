using FishNet;
using FishNet.Transporting;
using System;
using UnityEngine;

public enum PaintWarServerRole
{
    // 에디터 또는 로컬 단일 process 실행입니다.
    SingleRoom,
    // matchmaking client를 받고 room process를 실행하는 dedicated process입니다.
    Master,
    // 하나의 match room을 담당하는 dedicated child process입니다.
    Room
}

/// <summary>
/// PaintWar 전용 서버 command-line argument를 파싱하고 FishNet transport port를 적용합니다.
/// master와 room process는 같은 실행 파일을 사용하며, 이 클래스가 runtime role을 결정합니다.
/// </summary>
public static class ServerLaunchMode
{
    public const string DefaultPublicAddress = "158.180.89.56";
    public const ushort MasterPort = 7770;
    public const ushort FirstRoomPort = 7781;
    public const ushort LastRoomPort = 7796;

    private const string RoleArg = "-paintwarServerRole";
    private const string RoomIdArg = "-paintwarRoomId";
    private const string RoomPortArg = "-paintwarRoomPort";
    private const string PublicAddressArg = "-paintwarPublicAddress";
    private const string RoomTimeoutArg = "-paintwarRoomMatchTimeout";

    /// <summary>
    /// 현재 process role입니다. Batch mode는 기본 master라 배포된 headless build가 matchmaking mode로 시작합니다.
    /// </summary>
    public static PaintWarServerRole Role
    {
        get
        {
            string value = GetArg(RoleArg);
            if (string.Equals(value, "master", StringComparison.OrdinalIgnoreCase))
            {
                return PaintWarServerRole.Master;
            }

            if (string.Equals(value, "room", StringComparison.OrdinalIgnoreCase))
            {
                return PaintWarServerRole.Room;
            }

            return Application.isBatchMode
                ? PaintWarServerRole.Master
                : PaintWarServerRole.SingleRoom;
        }
    }

    public static bool IsMasterServer { get { return Role == PaintWarServerRole.Master; } }
    public static bool IsRoomServer { get { return Role == PaintWarServerRole.Room; } }

    public static string RoomId
    {
        get
        {
            string value = GetArg(RoomIdArg);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public static ushort RoomPort
    {
        get
        {
            if (TryGetUShortArg(RoomPortArg, out ushort port))
            {
                return port;
            }

            return MasterPort;
        }
    }

    public static string PublicAddress
    {
        get
        {
            string value = GetArg(PublicAddressArg);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public static float RoomMatchTimeout
    {
        get
        {
            string value = GetArg(RoomTimeoutArg);
            return float.TryParse(value, out float seconds) ? Mathf.Max(1f, seconds) : 5f;
        }
    }

    public static void ApplyServerTransportOverride()
    {
        Transport transport = GetTransport();
        if (transport == null)
        {
            return;
        }

        if (IsRoomServer)
        {
            transport.SetPort(RoomPort);
            Debug.Log("[Rooms] Room server transport port set to " + RoomPort + ".");
        }
        else if (IsMasterServer)
        {
            transport.SetPort(MasterPort);
            Debug.Log("[Rooms] Master server transport port set to " + MasterPort + ".");
        }
    }

    public static bool ApplyClientTransport(string address, ushort port)
    {
        // 클라이언트는 master matchmaking에서 room server로 이동할 때 기존 FishNet transport를 재사용합니다.
        Transport transport = GetTransport();
        if (transport == null ||
            string.IsNullOrWhiteSpace(address) ||
            port == 0)
        {
            return false;
        }

        transport.SetClientAddress(address.Trim());
        transport.SetPort(port);
        Debug.Log("[Rooms] Client transport target set to " + address + ":" + port + ".");
        return true;
    }

    public static ushort GetConfiguredPort()
    {
        Transport transport = GetTransport();
        return transport != null ? transport.GetPort() : MasterPort;
    }

    private static Transport GetTransport()
    {
        return InstanceFinder.NetworkManager != null
            ? InstanceFinder.NetworkManager.TransportManager.Transport
            : null;
    }

    private static bool TryGetUShortArg(string key, out ushort value)
    {
        value = 0;
        string raw = GetArg(key);
        return ushort.TryParse(raw, out value);
    }

    private static string GetArg(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return string.Empty;
    }
}
