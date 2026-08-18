using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

public struct WeaponPlacementVisualRequestBroadcast : IBroadcast
{
    public string WeaponId;
    public int Owner;
    public int PlayerLevel;
    public float OriginX;
    public float OriginY;
    public float OriginZ;
    public float ForwardX;
    public float ForwardY;
    public float ForwardZ;
}

public struct WeaponPlacementVisualBroadcast : IBroadcast
{
    public string WeaponId;
    public int Owner;
    public int PlayerLevel;
    public int SourceClientId;
    public float OriginX;
    public float OriginY;
    public float OriginZ;
    public float ForwardX;
    public float ForwardY;
    public float ForwardZ;
}

public sealed class NetworkWeaponPlacementVisualSync : MonoBehaviour
{
    public static NetworkWeaponPlacementVisualSync Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    private NetworkManager networkManager;
    private bool broadcastsRegistered;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        CacheNetworkManager();
        RegisterBroadcasts();
    }

    private void OnDestroy()
    {
        UnregisterBroadcasts();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool RequestVisual(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel)
    {
        if (weapon == null)
        {
            return false;
        }

        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsClientStarted)
        {
            return false;
        }

        networkManager.ClientManager.Broadcast(new WeaponPlacementVisualRequestBroadcast
        {
            WeaponId = weapon.WeaponId,
            Owner = (int)owner,
            PlayerLevel = Mathf.Max(1, playerLevel),
            OriginX = origin.x,
            OriginY = origin.y,
            OriginZ = origin.z,
            ForwardX = forward.x,
            ForwardY = forward.y,
            ForwardZ = forward.z
        });

        return true;
    }

    private void OnVisualRequest(
        NetworkConnection connection,
        WeaponPlacementVisualRequestBroadcast message,
        Channel channel)
    {
        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            return;
        }

        PaintOwner owner = (PaintOwner)message.Owner;
        if (NetworkRoundController.HasInstance &&
            !NetworkRoundController.Instance.IsOwnerAllowed(connection, owner))
        {
            Debug.LogWarning(
                "[WeaponVisualSync] Rejected placement visual request. client=" +
                (connection != null ? connection.ClientId.ToString() : "none") +
                " owner=" + owner + ".");
            return;
        }

        WeaponData weapon = FindWeapon(message.WeaponId);
        if (weapon == null)
        {
            Debug.LogWarning(
                "[WeaponVisualSync] Rejected unknown weapon request. weaponId=" +
                message.WeaponId + ".");
            return;
        }

        Vector3 origin = new Vector3(message.OriginX, message.OriginY, message.OriginZ);
        Vector3 forward = new Vector3(message.ForwardX, message.ForwardY, message.ForwardZ);
        int playerLevel = Mathf.Max(1, message.PlayerLevel);
        ApplyServerPatternSideEffectsAfterDelayAsync(
            weapon,
            origin,
            forward,
            owner,
            playerLevel,
            this.GetCancellationTokenOnDestroy()).Forget();

        networkManager.ServerManager.Broadcast(new WeaponPlacementVisualBroadcast
        {
            WeaponId = message.WeaponId,
            Owner = message.Owner,
            PlayerLevel = playerLevel,
            SourceClientId = connection != null ? connection.ClientId : -1,
            OriginX = message.OriginX,
            OriginY = message.OriginY,
            OriginZ = message.OriginZ,
            ForwardX = message.ForwardX,
            ForwardY = message.ForwardY,
            ForwardZ = message.ForwardZ
        });
    }

    private async UniTaskVoid ApplyServerPatternSideEffectsAfterDelayAsync(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel,
        CancellationToken cancellationToken)
    {
        WeaponVisualSettings settings = WeaponVisualSettings.GetOrLoadDefault();
        float impactDelay = WeaponRuntimeController.GetImpactEffectDelaySeconds(weapon, settings);
        float gameplayDelay = settings.PlacementDelaySeconds + settings.GameplayApplyDelayAfterImpact;
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, impactDelay)),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        DetonateMinesInPattern(weapon, origin, forward, playerLevel);

        float remainingGameplayDelay = Mathf.Max(0f, gameplayDelay - impactDelay);
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(remainingGameplayDelay),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!TilePaintManager.HasInstance ||
            !TilePaintManager.Instance.IsGridReady)
        {
            return;
        }

        List<Vector2Int> affectedTiles = new List<Vector2Int>(25);
        WeaponPatternImpactResolver.ApplyAuthoritative(
            weapon, origin, forward, owner, playerLevel, affectedTiles);
    }

    private void OnVisualBroadcast(WeaponPlacementVisualBroadcast message, Channel channel)
    {
        CacheNetworkManager();
        if (networkManager != null &&
            networkManager.ClientManager != null &&
            networkManager.ClientManager.Connection != null &&
            networkManager.ClientManager.Connection.ClientId == message.SourceClientId)
        {
            return;
        }

        WeaponData weapon = FindWeapon(message.WeaponId);
        if (weapon == null)
        {
            Debug.LogWarning("[WeaponVisualSync] Missing weapon data for visual. weaponId=" + message.WeaponId + ".");
            return;
        }

        Vector3 origin = new Vector3(message.OriginX, message.OriginY, message.OriginZ);
        Vector3 forward = new Vector3(message.ForwardX, message.ForwardY, message.ForwardZ);
        int playerLevel = Mathf.Max(1, message.PlayerLevel);
        WeaponPlacementVisual.Create(
            weapon,
            origin,
            forward,
            (PaintOwner)message.Owner,
            playerLevel,
            WeaponVisualSettings.GetOrLoadDefault());

        DetonateLocalMinesAfterDelayAsync(
            weapon,
            origin,
            forward,
            playerLevel,
            this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid DetonateLocalMinesAfterDelayAsync(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        int playerLevel,
        CancellationToken cancellationToken)
    {
        WeaponVisualSettings settings = WeaponVisualSettings.GetOrLoadDefault();
        float delay = WeaponRuntimeController.GetImpactEffectDelaySeconds(weapon, settings);
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, delay)),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        DetonateMinesInPattern(weapon, origin, forward, playerLevel);
    }

    private static int DetonateMinesInPattern(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        int playerLevel)
    {
        List<Vector2Int> affectedTiles = new List<Vector2Int>(25);
        return WeaponPatternImpactResolver.DetonateMinesInPattern(
            weapon,
            origin,
            forward,
            playerLevel,
            affectedTiles);
    }

    private static WeaponData FindWeapon(string weaponId)
    {
        return WeaponRuntimeController.TryFindWeaponData(weaponId, out WeaponData weapon) ? weapon : null;
    }

    private void CacheNetworkManager()
    {
        if (networkManager == null)
        {
            networkManager = InstanceFinder.NetworkManager;
        }
    }

    private void RegisterBroadcasts()
    {
        if (broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.RegisterBroadcast<WeaponPlacementVisualRequestBroadcast>(
            OnVisualRequest,
            false);
        networkManager.ClientManager.RegisterBroadcast<WeaponPlacementVisualBroadcast>(OnVisualBroadcast);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<WeaponPlacementVisualRequestBroadcast>(OnVisualRequest);
        networkManager.ClientManager.UnregisterBroadcast<WeaponPlacementVisualBroadcast>(OnVisualBroadcast);
        broadcastsRegistered = false;
    }
}
