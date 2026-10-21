using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

// 비어 있는 PaintOwner slot을 서버가 spawn한 네트워크 봇으로 채우는 helper입니다.
// 봇 prefab은 보통 player prefab을 재사용하고, 인간 이동을 끈 뒤 NetworkBotController를 붙입니다.
public sealed class NetworkRoundBotSpawner
{
    public NetworkObject BotPrefab { get; private set; }

    public void SetDefaultPrefab(NetworkObject prefab)
    {
        if (prefab != null && BotPrefab == null)
        {
            BotPrefab = prefab;
        }
    }

    public void TryAssignDefaultPrefabFromScene()
    {
        // HostPlayerSpawner가 이미 네트워크 player prefab을 알고 있으므로 fallback bot prefab으로 사용합니다.
        if (BotPrefab != null)
        {
            return;
        }

        HostPlayerSpawner[] spawners = Object.FindObjectsByType<HostPlayerSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < spawners.Length; i++)
        {
            if (spawners[i] != null && spawners[i].PlayerPrefab != null)
            {
                BotPrefab = spawners[i].PlayerPrefab;
                return;
            }
        }
    }

    public void SpawnBot(PaintOwner owner, NetworkManager networkManager, System.Action<GameObject, PaintOwner> applyOwner)
    {
        if (BotPrefab == null)
        {
            Debug.LogWarning("[RoundNet] botPrefab???놁뼱 " + owner + " ?щ’??遊뉗쑝濡?梨꾩슦吏 紐삵뻽?듬땲??");
            return;
        }

        NetworkObject botObject = Object.Instantiate(BotPrefab);
        botObject.name = "Network AI Bot " + (int)owner;
        string appearanceCode = GetAppearanceCode(owner);
        string motionCode = GetMotionCode(owner);
        applyOwner(botObject.gameObject, owner);
        ApplyAppearance(botObject.gameObject, appearanceCode, motionCode);

        // spawn된 player prefab을 AI가 제어하는 오브젝트로 변환합니다.
        NetworkBotController botController = botObject.GetComponent<NetworkBotController>();
        if (botController == null)
        {
            botController = botObject.gameObject.AddComponent<NetworkBotController>();
        }

        PlayerMovement playerMovement = botObject.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.enabled = false;
        }

        botController.Initialize(owner);
        networkManager.ServerManager.Spawn(botObject);
        networkManager.SceneManager.AddOwnerToDefaultScene(botObject);
        applyOwner(botObject.gameObject, owner);
        ApplyAppearance(botObject.gameObject, appearanceCode, motionCode);

        if (GameRoundManager.HasInstance)
        {
            GameRoundManager.Instance.RegisterNetworkBot(
                owner,
                GetDisplayName(owner),
                botObject.gameObject);
        }

        Debug.Log("[RoundNet] " + owner + " network bot spawned.");
    }

    private static void ApplyAppearance(
        GameObject botObject,
        string appearanceCode,
        string motionCode)
    {
        Player player = botObject.GetComponent<Player>();
        if (player == null)
        {
            return;
        }

        player.ServerSetAppearanceCode(appearanceCode);
        player.ServerSetMotionCode(motionCode);
    }

    private static string GetAppearanceCode(PaintOwner owner)
    {
        // AI slot에는 lobby roster 외형을 우선 사용하고, 없으면 deterministic해 보이는 fallback을 생성합니다.
        int slotIndex = (int)owner - 1;
        string rosterAppearance = slotIndex >= 0
            ? LoadingTransitionController.GetServerRosterAppearanceCode(slotIndex)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(rosterAppearance))
        {
            return rosterAppearance;
        }

        unchecked
        {
            int seed = (int)System.DateTime.UtcNow.Ticks;
            seed = (seed * 397) ^ (int)owner;
            seed = (seed * 397) ^ (int)(Time.realtimeSinceStartup * 1000f);
            return CharacterAppearanceStore.CreateRandomAiAppearanceCode(seed);
        }
    }

    private static string GetDisplayName(PaintOwner owner)
    {
        int slotIndex = (int)owner - 1;
        string rosterName = slotIndex >= 0
            ? LoadingTransitionController.GetServerRosterDisplayName(slotIndex)
            : string.Empty;
        return string.IsNullOrWhiteSpace(rosterName)
            ? AiNicknamePool.GetName(slotIndex)
            : rosterName;
    }

    private static string GetMotionCode(PaintOwner owner)
    {
        int slotIndex = (int)owner - 1;
        return slotIndex >= 0
            ? LoadingTransitionController.GetServerRosterMotionCode(slotIndex)
            : string.Empty;
    }
}
