using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// GameRoundManager의 참가자 목록을 관리합니다.
// 서버/로컬 모드는 실제 GameObject 참조를 들고, 원격 클라이언트는 mask로 가벼운 참가자 정보를 복원합니다.
public sealed class RoundParticipantRegistry
{
    private static readonly bool EnableLocalTestBots = false;

    // 이 list는 GameRoundManager가 소유하며 participant state 복사를 피하기 위해 여기서 공유받습니다.
    private readonly List<GameParticipant> participants;
    private readonly PaintOwner[] ownerOrder;
    private GameObject playerTemplate;

    public RoundParticipantRegistry(List<GameParticipant> participants, PaintOwner[] ownerOrder, GameObject playerTemplate)
    {
        this.participants = participants;
        this.ownerOrder = ownerOrder;
        this.playerTemplate = playerTemplate;
    }

    public GameObject PlayerTemplate { get { return playerTemplate; } set { playerTemplate = value; } }

    public GameParticipant FindParticipant(PaintOwner owner)
    {
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i].Owner == owner)
            {
                return participants[i];
            }
        }

        return null;
    }

    public void RegisterLocalHumanPlayer()
    {
        // 로컬 시뮬레이션은 조작 가능한 인간 플레이어 한 명을 가정하고 나머지 owner를 봇으로 채웁니다.
        if (participants.Count > 0)
        {
            return;
        }

        GameObject localPlayer = FindLocalPlayerObject();
        if (localPlayer == null)
        {
            Debug.LogWarning("[Round] LGH scene?먯꽌 濡쒖뺄 ?뚮젅?댁뼱瑜?李얠? 紐삵뻽?듬땲??");
            return;
        }

        PlayerPaintIdentity localIdentity = localPlayer.GetComponent<PlayerPaintIdentity>();
        if (localIdentity == null)
        {
            localIdentity = localPlayer.AddComponent<PlayerPaintIdentity>();
        }

        if (localPlayer.GetComponent<PlayerLevelStats>() == null)
        {
            localPlayer.AddComponent<PlayerLevelStats>();
        }

        BotPlayerController botController = localPlayer.GetComponent<BotPlayerController>();
        if (botController != null)
        {
            botController.enabled = false;
        }

        playerTemplate = localPlayer;
        localIdentity.SetOwner(PaintOwner.Player1);
        AddParticipant(PaintOwner.Player1, "P1", false, localPlayer);
    }

    public void FillEmptySlotsWithBots()
    {
        RegisterLocalHumanPlayer();
        if (!EnableLocalTestBots)
        {
            Debug.Log("[Round] Local test bot fill is disabled. Network matches use NetworkRoundBotSpawner.");
            return;
        }

        for (int i = 0; i < ownerOrder.Length; i++)
        {
            PaintOwner owner = ownerOrder[i];
            if (FindParticipant(owner) != null)
            {
                continue;
            }

            GameObject botObject = CreateBotObject(owner);
            AddParticipant(
                owner,
                AiNicknamePool.GetName((int)owner - 1),
                true,
                botObject);
            Debug.Log("[Round] " + owner + " 濡쒖뺄 AI 遊뉗쓣 ?앹꽦?덉뒿?덈떎.");
        }
    }

    public void RefreshParticipantStatRegistrations()
    {
        for (int i = 0; i < participants.Count; i++)
        {
            GameParticipant participant = participants[i];
            if (participant == null)
            {
                continue;
            }

            if (participant.Identity != null)
            {
                participant.Identity.SetOwner(participant.Owner);
            }

            if (participant.Stats != null)
            {
                participant.Stats.RefreshOwnerRegistration();
            }
        }
    }

    public void RegisterExistingParticipant(PaintOwner owner, string displayName, bool isBot, GameObject playerObject)
    {
        AddParticipant(owner, displayName, isBot, playerObject);
    }

    public void ApplyNetworkParticipantMask(RoundStateBroadcast state)
    {
        // 원격 클라이언트는 모든 player GameObject가 없을 수 있어 mask로 HUD/result에 필요한 상태를 복원합니다.
        for (int i = 0; i < ownerOrder.Length; i++)
        {
            PaintOwner owner = ownerOrder[i];
            int bit = 1 << ((int)owner - 1);
            bool isParticipant = (state.ParticipantMask & bit) != 0;
            GameParticipant participant = FindParticipant(owner);

            if (!isParticipant)
            {
                continue;
            }

            bool isBot = (state.BotMask & bit) != 0;
            bool isEliminated = (state.EliminatedMask & bit) != 0;
            string displayName = participant != null && !string.IsNullOrWhiteSpace(participant.DisplayName)
                ? participant.DisplayName
                : (isBot
                    ? AiNicknamePool.GetName((int)owner - 1)
                    : "P" + (int)owner);

            if (participant != null)
            {
                participant.IsBot = isBot;
                participant.IsEliminated = isEliminated;
                participant.DisplayName = displayName;
                AttachLiveParticipant(participant);
                ApplyParticipantVisibility(participant);
                continue;
            }

            participant = new GameParticipant
            {
                Owner = owner,
                DisplayName = displayName,
                IsBot = isBot,
                IsEliminated = isEliminated
            };
            AttachLiveParticipant(participant);
            participants.Add(participant);
        }
    }

    private static void AttachLiveParticipant(GameParticipant participant)
    {
        if (participant == null || participant.Transform != null)
        {
            return;
        }

        if (!PlayerPaintIdentity.TryGetByOwner(
                participant.Owner,
                out PlayerPaintIdentity identity))
        {
            return;
        }

        participant.Transform = identity.transform;
        participant.Identity = identity;
        participant.Stats = identity.GetComponent<PlayerLevelStats>();
    }

    private static void ApplyParticipantVisibility(GameParticipant participant)
    {
        if (participant == null || participant.Transform == null)
        {
            return;
        }

        Player player = participant.Transform.GetComponent<Player>();
        if (player != null)
        {
            player.ApplyEliminatedSnapshot(participant.IsEliminated);
            return;
        }

        participant.Transform.gameObject.SetActive(!participant.IsEliminated);
    }

    public void ApplyNetworkRoster(RoundRosterBroadcast roster)
    {
        // roster update는 round-state보다 드물게 오며 이름과 bot flag만 전달합니다.
        for (int i = 0; i < ownerOrder.Length; i++)
        {
            PaintOwner owner = ownerOrder[i];
            int bit = 1 << ((int)owner - 1);
            if ((roster.ParticipantMask & bit) == 0) continue;

            bool isBot = (roster.BotMask & bit) != 0;
            GameParticipant participant = FindParticipant(owner);
            if (participant == null)
            {
                participant = new GameParticipant { Owner = owner };
                participants.Add(participant);
            }

            participant.IsBot = isBot;
            participant.DisplayName = GetRosterDisplayName(roster, owner, isBot);
        }
    }

    private static string GetRosterDisplayName(
        RoundRosterBroadcast roster,
        PaintOwner owner,
        bool isBot)
    {
        string displayName = owner == PaintOwner.Player1 ? roster.Player1DisplayName
            : owner == PaintOwner.Player2 ? roster.Player2DisplayName
            : owner == PaintOwner.Player3 ? roster.Player3DisplayName
            : owner == PaintOwner.Player4 ? roster.Player4DisplayName
            : string.Empty;
        return string.IsNullOrWhiteSpace(displayName)
            ? (isBot
                ? AiNicknamePool.GetName((int)owner - 1)
                : "P" + (int)owner)
            : displayName;
    }

    private GameObject CreateBotObject(PaintOwner owner)
    {
        // 로컬 시뮬레이션은 player template을 복제하고, 네트워크 봇은 NetworkRoundBotSpawner가 spawn합니다.
        GameObject source = playerTemplate != null ? playerTemplate : FindPlayerTemplate();
        GameObject botObject;

        if (source != null)
        {
            botObject = Object.Instantiate(source);
            botObject.name = "AI Bot " + ((int)owner);
        }
        else
        {
            botObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            botObject.name = "AI Bot " + ((int)owner);
            botObject.AddComponent<CharacterController>();
            botObject.AddComponent<Player>();
            botObject.AddComponent<PlayerPaintIdentity>();
            botObject.AddComponent<PlayerLevelStats>();
        }

        PlayerMovement movement = botObject.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        BotPlayerController botController = botObject.GetComponent<BotPlayerController>();
        if (botController == null)
        {
            botController = botObject.AddComponent<BotPlayerController>();
        }

        PlayerPaintIdentity identity = botObject.GetComponent<PlayerPaintIdentity>();
        if (identity == null)
        {
            identity = botObject.AddComponent<PlayerPaintIdentity>();
        }

        identity.SetOwner(owner);
        botController.Initialize(owner);
        EquipRandomBotWeapon(botObject);
        return botObject;
    }

    private void EquipRandomBotWeapon(GameObject botObject)
    {
        WeaponRuntimeController weaponController = botObject.GetComponent<WeaponRuntimeController>();
        if (weaponController == null || weaponController.Weapons == null || weaponController.Weapons.Count == 0)
        {
            return;
        }

        weaponController.Equip(Random.Range(0, weaponController.Weapons.Count));
    }

    private GameObject FindPlayerTemplate()
    {
        GameObject localPlayer = FindLocalPlayerObject();
        if (localPlayer != null)
        {
            return localPlayer;
        }

        PlayerPaintIdentity identity = Object.FindFirstObjectByType<PlayerPaintIdentity>();
        return identity != null ? identity.gameObject : null;
    }

    private GameObject FindLocalPlayerObject()
    {
        PlayerMovement movement = Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.enabled && candidate.GetComponent<BotPlayerController>() == null);

        if (movement != null)
        {
            return movement.gameObject;
        }

        PlayerPaintIdentity identity = Object.FindObjectsByType<PlayerPaintIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.Owner == PaintOwner.Player1 && candidate.GetComponent<BotPlayerController>() == null)
            ?? Object.FindObjectsByType<PlayerPaintIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.GetComponent<BotPlayerController>() == null);

        if (identity != null)
        {
            return identity.gameObject;
        }

        Player player = Object.FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.GetComponent<BotPlayerController>() == null);
        return player != null ? player.gameObject : null;
    }

    private void AddParticipant(PaintOwner owner, string displayName, bool isBot, GameObject playerObject)
    {
        // 점수 보상이 시작되기 전에 identity/stats 컴포넌트가 participant owner와 일치하도록 보장합니다.
        if (playerObject == null)
        {
            return;
        }

        PlayerPaintIdentity identity = playerObject.GetComponent<PlayerPaintIdentity>();
        if (identity == null)
        {
            identity = playerObject.AddComponent<PlayerPaintIdentity>();
        }

        identity.SetOwner(owner);

        PlayerLevelStats stats = playerObject.GetComponent<PlayerLevelStats>();
        if (stats == null)
        {
            stats = playerObject.AddComponent<PlayerLevelStats>();
        }

        stats.RefreshOwnerRegistration();

        GameParticipant existing = FindParticipant(owner);
        if (existing != null)
        {
            existing.DisplayName = displayName;
            existing.IsBot = isBot;
            existing.Transform = playerObject.transform;
            existing.Identity = identity;
            existing.Stats = stats;
            return;
        }

        participants.Add(new GameParticipant
        {
            Owner = owner,
            DisplayName = displayName,
            IsBot = isBot,
            IsEliminated = false,
            Transform = playerObject.transform,
            Identity = identity,
            Stats = stats
        });
    }
}
