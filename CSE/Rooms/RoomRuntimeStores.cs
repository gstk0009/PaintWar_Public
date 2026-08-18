using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public readonly struct RoomRuntimeStatus
{
    // heartbeat timestamp??UTC??master媛 process ?ъ떆???꾩뿉??child ?곹깭瑜?鍮꾧탳?????덉뒿?덈떎.
    public readonly DateTime HeartbeatUtc;
    public readonly int ClientCount;
    public readonly string State;

    public RoomRuntimeStatus(DateTime heartbeatUtc, int clientCount, string state)
    {
        HeartbeatUtc = heartbeatUtc;
        ClientCount = clientCount;
        State = state ?? string.Empty;
    }
}

/// <summary>
/// room server媛 ?곌퀬 master launcher媛 ?쎈뒗 heartbeat ?뚯씪 ??μ냼?낅땲??
/// </summary>
public static class RoomRuntimeStatusStore
{
    public static void Write(string roomId, int clientCount, string state)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(GetDirectory());
            File.WriteAllText(
                GetPath(roomId),
                DateTime.UtcNow.Ticks + "|" + Mathf.Max(0, clientCount) + "|" +
                SanitizeState(state));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Rooms] Failed to write room heartbeat: " + exception.Message);
        }
    }

    public static bool TryRead(string roomId, out RoomRuntimeStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return false;
        }

        try
        {
            string path = GetPath(roomId);
            if (!File.Exists(path))
            {
                return false;
            }

            string[] parts = File.ReadAllText(path).Split('|');
            if (parts.Length < 3 ||
                !long.TryParse(parts[0], out long ticks) ||
                !int.TryParse(parts[1], out int clientCount))
            {
                return false;
            }

            status = new RoomRuntimeStatus(
                new DateTime(ticks, DateTimeKind.Utc),
                Mathf.Max(0, clientCount),
                parts[2]);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Delete(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        try
        {
            string path = GetPath(roomId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Rooms] Failed to delete room heartbeat: " + exception.Message);
        }
    }

    private static string GetPath(string roomId)
    {
        string safeRoomId = roomId.Trim()
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        return Path.Combine(GetDirectory(), safeRoomId + ".runtime");
    }

    private static string GetDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "PaintWarRooms");
    }

    private static string SanitizeState(string state)
    {
        return string.IsNullOrWhiteSpace(state)
            ? "unknown"
            : state.Trim().Replace('|', '_');
    }
}

public static class RoomConnectionProfileCache
{
    // FishNet client id?먯꽌 ?덉젙?곸씤 profile user id濡??댁뼱吏???쒕쾭 痢??꾩떆 留ㅽ븨?낅땲??
    private static readonly Dictionary<int, string> userIdsByClientId =
        new Dictionary<int, string>();

    public static void Set(NetworkConnection connection, string userId)
    {
        if (connection == null)
        {
            return;
        }

        userId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        userIdsByClientId[connection.ClientId] = userId;
    }

    public static bool TryGetUserId(NetworkConnection connection, out string userId)
    {
        if (connection != null &&
            userIdsByClientId.TryGetValue(connection.ClientId, out userId) &&
            !string.IsNullOrWhiteSpace(userId))
        {
            return true;
        }

        userId = string.Empty;
        return false;
    }

    public static void Remove(NetworkConnection connection)
    {
        if (connection != null)
        {
            userIdsByClientId.Remove(connection.ClientId);
        }
    }

    public static void Clear()
    {
        userIdsByClientId.Clear();
    }
}

