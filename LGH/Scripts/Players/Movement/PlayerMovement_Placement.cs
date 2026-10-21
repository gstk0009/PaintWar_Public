using UnityEngine;

// PlayerMovement의 라운드 배치/위치 보정 로컬 적용을 담당합니다.
// RPC 선언은 PlayerMovement.cs에 모아둡니다.
public partial class PlayerMovement
{
    private void ApplyRoundPlacement(Vector3 position, Quaternion rotation)
    {
        CacheComponents();
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        // CharacterController가 켜져 있으면 transform teleport를 막을 수 있어 배치 순간에만 잠시 끕니다.
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
        {
            characterController.enabled = false;
        }

        // A delayed client may still be presenting this real player on the result
        // stage. Update the pose that presentation will restore before moving it.
        RoundResultDisplayAvatar.UpdatePendingGameplayPose(transform, position, rotation);
        transform.SetPositionAndRotation(position, rotation);
        tileClaimDriver.Reset();
        isStepMoving = false;
        CancelSoftGridSnap();
        isTeleporting = false;

        if (controllerWasEnabled)
        {
            characterController.enabled = true;
        }
    }

    private void ForceRoundPlacementNetworkSync()
    {
        if (networkTransform == null)
        {
            networkTransform =
                GetComponent<FishNet.Component.Transforming.NetworkTransform>();
        }

        if (networkTransform == null)
        {
            return;
        }

        networkTransform.Teleport();
        networkTransform.ForceSend();
    }

    public void ApplyExternalTileDisplacement(Vector3 position)
    {
        CacheComponents();
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
        {
            characterController.enabled = false;
        }

        transform.position = position;
        if (tileClaimDriver != null)
        {
            tileClaimDriver.Reset();
        }

        isStepMoving = false;
        CancelSoftGridSnap();
        isTeleporting = false;

        if (controllerWasEnabled)
        {
            characterController.enabled = true;
        }

        ForceRoundPlacementNetworkSync();
    }
}
