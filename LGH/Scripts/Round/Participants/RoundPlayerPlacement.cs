using System.Collections.Generic;
using UnityEngine;

// 라운드 시작 시 생존 플레이어를 각 파밍 구역의 스폰 위치로 이동시키는 기능만 담당합니다.
// 위치 배치 로직을 분리하면 GameRoundManager는 "라운드를 시작한다"는 흐름만 유지할 수 있습니다.
// 새 라운드가 시작될 때 생존 참가자를 각 FarmingZone의 안전한 시작 위치로 옮깁니다.
// 네트워크 플레이어는 PlayerMovement.ServerPlaceForRound를 통해 서버/owner client 위치를 같이 보정합니다.
public static class RoundPlayerPlacement
{
    // 탈락하지 않은 참가자만 활성화하고, 각자의 FarmingZone 중심 근처 안전한 타일에 배치합니다.
    // 배치 후에는 모든 플레이어가 맵 중앙을 바라보게 회전시켜 시작 구도가 읽히도록 합니다.
    public static void PlaceAlivePlayersAtZoneCenters(IEnumerable<GameParticipant> participants, TilePaintManager tileManager)
    {
        if (participants == null || tileManager == null)
        {
            return;
        }

        foreach (GameParticipant participant in participants)
        {
            if (participant == null || participant.Transform == null)
            {
                continue;
            }

            Player player = participant.Transform.GetComponent<Player>();
            if (player != null)
            {
                // SetActive만으로는 원격 클라이언트에 탈락 상태가 동기화되지 않으므로 Player RPC를 우선 사용합니다.
                player.ServerSetEliminated(participant.IsEliminated);
            }
            else
            {
                participant.Transform.gameObject.SetActive(!participant.IsEliminated);
            }

            if (participant.IsEliminated || participant.Zone == null)
            {
                continue;
            }

            Vector2Int centerIndex = participant.Zone.GetSpawnIndex(tileManager.GridSize);
            Vector2Int mapCenter = new Vector2Int(tileManager.GridSize / 2, tileManager.GridSize / 2);
            Vector2Int inward = new Vector2Int(System.Math.Sign(mapCenter.x - centerIndex.x),
                System.Math.Sign(mapCenter.y - centerIndex.y));
            centerIndex = tileManager.ClampIndex(centerIndex + inward * 2);
            Vector3 position = tileManager.GetTileCenterWorldPosition(tileManager.ClampIndex(centerIndex));
            position.y = participant.Transform.position.y;
            Quaternion rotation = GetRotationTowardMapCenter(position, tileManager);

            PlayerMovement movement = participant.Transform.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                // CharacterController와 NetworkTransform이 엇갈리지 않도록 PlayerMovement가 배치를 전담합니다.
                movement.ServerPlaceForRound(position, rotation);
                ResetBotAfterPlacement(participant.Transform);
                continue;
            }

            participant.Transform.SetPositionAndRotation(position, rotation);
            FishNet.Component.Transforming.NetworkTransform networkTransform =
                participant.Transform.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
            if (networkTransform != null)
            {
                networkTransform.Teleport();
                networkTransform.ForceSend();
            }

            ResetBotAfterPlacement(participant.Transform);
        }
    }

    private static void ResetBotAfterPlacement(Transform participantTransform)
    {
        NetworkBotController botController = participantTransform.GetComponent<NetworkBotController>();
        if (botController != null)
        {
            botController.ServerResetForRoundPlacement();
        }
    }

    // 배치된 참가자가 라운드 시작 시 맵 중앙을 바라보도록 회전시킵니다.
    private static Quaternion GetRotationTowardMapCenter(Vector3 position, TilePaintManager tileManager)
    {
        Vector3 center = tileManager.GetTileCenterWorldPosition(new Vector2Int(tileManager.GridSize / 2, tileManager.GridSize / 2));
        Vector3 direction = center - position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            return Quaternion.LookRotation(direction);
        }

        return Quaternion.identity;
    }
}
