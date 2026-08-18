using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ?쇱슫??李멸???紐⑸줉, 濡쒖뺄 ?뚮젅?댁뼱 ?깅줉, 鍮??먮━ AI ?앹꽦留??대떦?⑸땲??
/// GameRoundManager?먯꽌 ??濡쒖쭅??遺꾨━?댁꽌 ?쇱슫???쒓컙 ?먮쫫怨?李멸????앹꽦 肄붾뱶媛 ?욎씠吏 ?딄쾶 ?⑸땲??
/// </summary>
/// <summary>
/// ?쇱슫??李멸???紐⑸줉??愿由ы빀?덈떎.
/// ?쒕쾭/濡쒖뺄 ?뚯뒪?몄뿉?쒕뒗 ?ㅼ젣 GameObject源뚯? ?깅줉?섍퀬, ?먭꺽 ?대씪?댁뼵?몃뒗 broadcast mask濡??쒖떆??李멸????뺣낫瑜?蹂듭썝?⑸땲??
/// </summary>
/// <summary>
/// GameRoundManager의 참가자 목록을 관리합니다.
/// 서버/로컬 모드는 실제 GameObject 참조를 들고, 원격 클라이언트는 mask로 가벼운 참가자 정보를 복원합니다.
/// </summary>
public sealed class RoundParticipantRegistry
{
    private static readonly bool EnableLocalTestBots = false;

    // 이 list는 GameRoundManager가 소유하며 participant state 복사를 피하기 위해 여기서 공유받습니다.
    private readonly List<GameParticipant> participants;
    private readonly PaintOwner[] ownerOrder;
    private GameObject playerTemplate;

    /// <summary>
    /// GameRoundManager媛 ?뚯쑀??李멸???由ъ뒪?몄? owner ?쒖꽌瑜?怨듭쑀諛쏆븘 ?덉??ㅽ듃由щ? 珥덇린?뷀빀?덈떎.
    /// </summary>
    public RoundParticipantRegistry(List<GameParticipant> participants, PaintOwner[] ownerOrder, GameObject playerTemplate)
    {
        this.participants = participants;
        this.ownerOrder = ownerOrder;
        this.playerTemplate = playerTemplate;
    }

    /// <summary>
    /// 遊?蹂듭젣???ъ슜???뚮젅?댁뼱 ?쒗뵆由??ㅻ툕?앺듃?낅땲??
    /// </summary>
    public GameObject PlayerTemplate { get { return playerTemplate; } set { playerTemplate = value; } }

    /// <summary>
    /// owner媛 ?쇱튂?섎뒗 李멸??먮? ?꾩옱 李멸???紐⑸줉?먯꽌 李얠뒿?덈떎.
    /// </summary>
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

    /// <summary>
    /// ?ъ뿉 ?대? ?볦뿬 ?덈뒗 議곗옉 媛?ν븳 ?뚮젅?댁뼱瑜?P1?쇰줈 ?깅줉?⑸땲??
    /// 濡쒖뺄 ?뚯뒪?몄뿉?쒕뒗 ???뚮젅?댁뼱瑜?湲곗??쇰줈 AI 遊뉗쓣 蹂듭젣?⑸땲??
    /// </summary>
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

    /// <summary>
    /// 4??留ㅼ튂 湲곗??쇰줈 鍮꾩뼱 ?덈뒗 ?뚮젅?댁뼱 ?щ’??AI濡?梨꾩썎?덈떎.
    /// ?ㅼ젣 硫?고뵆?덉씠媛 遺숈쑝硫????⑥닔 ????쒕쾭 留ㅼ묶 寃곌낵瑜?李멸??먮줈 ?ｌ쑝硫??⑸땲??
    /// </summary>
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

    /// <summary>
    /// ?뚯쑀??蹂寃쎌씠??遊??앹꽦 ?댄썑 PlayerLevelStats??owner 罹먯떆瑜??ㅼ떆 留욎땅?덈떎.
    /// 寃쏀뿕移?吏湲됱? PaintOwner瑜??ㅻ줈 李얘린 ?뚮Ц?????숆린?붽? ?꾩슂?⑸땲??
    /// </summary>
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
        // ?먭꺽 ?대씪?댁뼵?몃뒗 ?쒕쾭???ㅼ젣 GameObject 李몄“瑜?紐⑤몢 媛뽮퀬 ?덉? ?딆쓣 ???덉뒿?덈떎.
        // 洹몃옒??owner蹂?bit mask留뚯쑝濡?HUD/踰?怨꾩궛???꾩슂???쒖떆??李멸????곹깭瑜?留욎땅?덈떎.
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

    /// <summary>
    /// 濡쒖뺄 ?뚮젅?댁뼱 ?쒗뵆由우쓣 蹂듭젣?섍굅??罹≪뒓 ?泥??ㅻ툕?앺듃瑜?留뚮뱾??AI 遊뉗쑝濡?珥덇린?뷀빀?덈떎.
    /// </summary>
    private GameObject CreateBotObject(PaintOwner owner)
    {
        // 로컬 시뮬레이션은 player template을 복제하고, 네트워크 봇은 NetworkRoundBotSpawner가 spawn합니다.
        // 濡쒖뺄 ?뚯뒪???꾩슜 遊??앹꽦 寃쎈줈?낅땲?? FishNet ?ㅽ듃?뚰겕 遊뉗? NetworkRoundController.SpawnBot???ъ슜?⑸땲??
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

    /// <summary>
    /// 遊뉗뿉 WeaponRuntimeController媛 ?덉쑝硫?蹂댁쑀 臾닿린 以??섎굹瑜?臾댁옉?꾨줈 ?μ갑?⑸땲??
    /// </summary>
    private void EquipRandomBotWeapon(GameObject botObject)
    {
        WeaponRuntimeController weaponController = botObject.GetComponent<WeaponRuntimeController>();
        if (weaponController == null || weaponController.Weapons == null || weaponController.Weapons.Count == 0)
        {
            return;
        }

        weaponController.Equip(Random.Range(0, weaponController.Weapons.Count));
    }

    /// <summary>
    /// 紐낆떆???쒗뵆由우씠 ?놁쓣 ?????덉쓽 濡쒖뺄 ?뚮젅?댁뼱 ?꾨낫瑜?李얠븘 遊?蹂듭젣 ?먮낯?쇰줈 ?ъ슜?⑸땲??
    /// </summary>
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

    /// <summary>
    /// PlayerMovement, PlayerPaintIdentity, Player ?쒖꽌濡????덉쓽 議곗옉 媛?ν븳 濡쒖뺄 ?뚮젅?댁뼱瑜?李얠뒿?덈떎.
    /// </summary>
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

    /// <summary>
    /// 李멸????ㅻ툕?앺듃???꾩슂??identity/stats 而댄룷?뚰듃瑜?蹂댁옣????李멸???紐⑸줉??異붽??⑸땲??
    /// </summary>
    private void AddParticipant(PaintOwner owner, string displayName, bool isBot, GameObject playerObject)
    {
        // 점수 보상이 시작되기 전에 identity/stats 컴포넌트가 participant owner와 일치하도록 보장합니다.
        // ?ㅼ젣 ?ㅻ툕?앺듃媛 ?덈뒗 李멸??먮뒗 identity/stats瑜?蹂댁옣?댁꽌 ???蹂댁긽怨??쇱슫???먯젙??媛숈? owner瑜?蹂닿쾶 ?⑸땲??
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
