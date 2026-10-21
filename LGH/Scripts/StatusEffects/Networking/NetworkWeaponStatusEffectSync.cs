using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

public struct WeaponStatusEffectRequestBroadcast : IBroadcast
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

public struct WeaponStatusEffectBroadcast : IBroadcast
{
    public int TargetOwner;
    public int EffectType;
    public float Magnitude;
    public float Duration;
    public int StackMode;
}

public sealed class NetworkWeaponStatusEffectSync : MonoBehaviour
{
    public static NetworkWeaponStatusEffectSync Instance { get; private set; }
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

    public bool RequestEffect(
        WeaponData weapon,
        Vector3 origin,
        Vector3 forward,
        PaintOwner owner,
        int playerLevel)
    {
        if (weapon == null || !weapon.HasStatusEffect)
        {
            return false;
        }

        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsClientStarted)
        {
            return false;
        }

        networkManager.ClientManager.Broadcast(new WeaponStatusEffectRequestBroadcast
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

    public void BroadcastEffectToOwner(PaintOwner targetOwner, StatusEffectSpec effect)
    {
        if (targetOwner == PaintOwner.Neutral || !effect.IsValid)
        {
            return;
        }

        CacheNetworkManager();
        if (networkManager == null || !networkManager.IsServerStarted)
        {
            return;
        }

        networkManager.ServerManager.Broadcast(new WeaponStatusEffectBroadcast
        {
            TargetOwner = (int)targetOwner,
            EffectType = (int)effect.Type,
            Magnitude = effect.Magnitude,
            Duration = effect.Duration,
            StackMode = (int)effect.StackMode
        });
    }

    private void OnEffectRequest(
        NetworkConnection connection,
        WeaponStatusEffectRequestBroadcast message,
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
                "[WeaponStatusSync] Rejected status effect request. client=" +
                (connection != null ? connection.ClientId.ToString() : "none") +
                " owner=" + owner + ".");
            return;
        }

        WeaponData weapon = FindWeapon(message.WeaponId);
        if (weapon == null || !weapon.HasStatusEffect)
        {
            return;
        }

        int level = Mathf.Max(1, message.PlayerLevel);
        Vector3 origin = new Vector3(message.OriginX, message.OriginY, message.OriginZ);
        Vector3 forward = new Vector3(message.ForwardX, message.ForwardY, message.ForwardZ);
        List<PlayerPaintIdentity> targets = WeaponStatusEffectService.Apply(weapon, owner, origin, forward, level);
        BroadcastAppliedEffects(targets, owner, weapon.StatusEffect);
    }

    private void OnEffectBroadcast(WeaponStatusEffectBroadcast message, Channel channel)
    {
        WeaponStatusEffectService.ApplyToOwner(
            (PaintOwner)message.TargetOwner,
            new StatusEffectSpec(
                (StatusEffectType)message.EffectType,
                message.Magnitude,
                message.Duration,
                (StatusEffectStackMode)message.StackMode));
    }

    private void BroadcastAppliedEffects(
        List<PlayerPaintIdentity> targets,
        PaintOwner sourceOwner,
        StatusEffectSpec effect)
    {
        if (targets == null || !effect.IsValid)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            PlayerPaintIdentity target = targets[i];
            if (target == null || target.Owner == sourceOwner)
            {
                continue;
            }

            networkManager.ServerManager.Broadcast(new WeaponStatusEffectBroadcast
            {
                TargetOwner = (int)target.Owner,
                EffectType = (int)effect.Type,
                Magnitude = effect.Magnitude,
                Duration = effect.Duration,
                StackMode = (int)effect.StackMode
            });
        }
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

        networkManager.ServerManager.RegisterBroadcast<WeaponStatusEffectRequestBroadcast>(
            OnEffectRequest,
            false);
        networkManager.ClientManager.RegisterBroadcast<WeaponStatusEffectBroadcast>(OnEffectBroadcast);
        broadcastsRegistered = true;
    }

    private void UnregisterBroadcasts()
    {
        if (!broadcastsRegistered || networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.UnregisterBroadcast<WeaponStatusEffectRequestBroadcast>(OnEffectRequest);
        networkManager.ClientManager.UnregisterBroadcast<WeaponStatusEffectBroadcast>(OnEffectBroadcast);
        broadcastsRegistered = false;
    }
}
