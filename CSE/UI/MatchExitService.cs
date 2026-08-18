using System;
using Cysharp.Threading.Tasks;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 최종 결과 화면과 관전 화면에서 로비로 안전하게 복귀시키는 공용 경로입니다.
/// 별도 패널이나 씬 오브젝트 없이 동작합니다.
/// </summary>
public static class MatchExitService
{
    private const string LobbySceneName = "LobbyScene";
    private const float ClientStopTimeoutSeconds = 5f;

    private static bool returning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        returning = false;
    }

    public static void ReturnToLobby()
    {
        if (returning)
        {
            return;
        }

        returning = true;
        ReturnToLobbyAsync().Forget();
    }

    private static async UniTaskVoid ReturnToLobbyAsync()
    {
        try
        {
            // 로비 UI가 이전 room으로 자동 재접속하지 않도록 먼저 복귀 상태를 준비합니다.
            LobbyButtonController.PrepareForFrontLobbyReturn();
            RoomAssignmentStore.Clear();

            // 일반 클라이언트와 Host의 client 연결을 먼저 종료합니다.
            if (InstanceFinder.ClientManager != null &&
                InstanceFinder.ClientManager.Started)
            {
                InstanceFinder.ClientManager.StopConnection();

                float deadline =
                    Time.realtimeSinceStartup + ClientStopTimeoutSeconds;

                while (InstanceFinder.ClientManager != null &&
                       InstanceFinder.ClientManager.Started &&
                       Time.realtimeSinceStartup < deadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                if (InstanceFinder.ClientManager != null &&
                    InstanceFinder.ClientManager.Started)
                {
                    Debug.LogWarning(
                        "[MatchExit] Client connection did not stop within " +
                        ClientStopTimeoutSeconds +
                        " seconds. Continuing lobby return with the cached " +
                        "room assignment already cleared.");
                }
            }

            // 로컬 Host 실행인 경우 server 연결도 정리합니다.
            // Dedicated Server에서는 이 경로를 사용하지 않습니다.
            if (InstanceFinder.ServerManager != null &&
                InstanceFinder.ServerManager.Started &&
                !Application.isBatchMode)
            {
                InstanceFinder.ServerManager.StopConnection(true);
            }

            // 다음 ENTER는 room port가 아니라 master port로 접속해야 합니다.
            ServerLaunchMode.ApplyClientTransport(
                ServerLaunchMode.DefaultPublicAddress,
                ServerLaunchMode.MasterPort);

            LoadingTransitionController.HideExternalOverlay();

            if (SceneManager.GetActiveScene().name == LobbySceneName)
            {
                return;
            }

            AsyncOperation loadOperation =
                SceneManager.LoadSceneAsync(
                    LobbySceneName,
                    LoadSceneMode.Single);

            if (loadOperation == null)
            {
                throw new InvalidOperationException(
                    "Failed to start loading " + LobbySceneName + ".");
            }

            while (!loadOperation.isDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            // 씬 활성화 직후 한 프레임을 넘겨 로비 오브젝트 초기화와
            // returning 해제가 같은 프레임에 겹치지 않도록 합니다.
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            // 성공/실패와 관계없이 반드시 해제해야 다음 매치의 EXIT가 동작합니다.
            returning = false;
        }
    }
}