using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LGH 계열 씬에서 라운드 관련 런타임 시스템이 누락되었을 때 자동으로 붙여주는 부트스트랩입니다.
/// </summary>
public class RoundSystemBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAfterSceneLoad()
    {
        EnsureForActiveScene();
    }

    public static void EnsureForActiveScene()
    {
        if (!ShouldBootstrapForActiveScene())
        {
            return;
        }

        ActivateHostPlayerSpawners();

        if (GameRoundManager.HasInstance
            && TileClaimService.HasInstance
            && NetworkTilePaintAuthority.HasInstance
            && NetworkWeaponPlacementVisualSync.HasInstance
            && NetworkWeaponStatusEffectSync.HasInstance
            && FindFirstObjectByType<RoundHudView>() != null
            && FindFirstObjectByType<NetworkRoundController>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<RoundSystemBootstrap>() != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("Round System Bootstrap");
        bootstrapObject.AddComponent<RoundSystemBootstrap>();
    }

    private static void ActivateHostPlayerSpawners()
    {
        HostPlayerSpawner[] spawners =
            FindObjectsByType<HostPlayerSpawner>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < spawners.Length; i++)
        {
            HostPlayerSpawner spawner = spawners[i];
            if (spawner != null && !spawner.gameObject.activeSelf)
            {
                spawner.gameObject.SetActive(true);
                Debug.Log("[RoundBootstrap] Activated inactive HostPlayerSpawner.");
            }
        }
    }

    private async void Start()
    {
        try
        {
            CancellationToken token = this.GetCancellationTokenOnDestroy();
            while (!TilePaintManager.HasInstance)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            EnsureRoundObjects();
            Destroy(gameObject);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnsureRoundObjects()
    {
        if (!TileClaimService.HasInstance)
        {
            TilePaintManager.Instance.gameObject.AddComponent<TileClaimService>();
        }

        if (!NetworkTilePaintAuthority.HasInstance)
        {
            TilePaintManager.Instance.gameObject.AddComponent<NetworkTilePaintAuthority>();
        }

        GameObject systemsObject = GameObject.Find("Round Game Systems");
        if (systemsObject == null)
        {
            systemsObject = new GameObject("Round Game Systems");
        }

        if (!GameRoundManager.HasInstance)
        {
            systemsObject.AddComponent<GameRoundManager>();
        }

        if (FindFirstObjectByType<NetworkRoundController>() == null)
        {
            systemsObject.AddComponent<NetworkRoundController>();
        }

        if (!NetworkWeaponPlacementVisualSync.HasInstance)
        {
            systemsObject.AddComponent<NetworkWeaponPlacementVisualSync>();
        }

        if (!NetworkWeaponStatusEffectSync.HasInstance)
        {
            systemsObject.AddComponent<NetworkWeaponStatusEffectSync>();
        }

        if (FindFirstObjectByType<RoundHudView>() == null)
        {
            systemsObject.AddComponent<RoundHudView>();
        }
    }

    private static bool ShouldBootstrapForActiveScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "LGH" ||
            sceneName == "LGH_Bot" ||
            sceneName == "CSE" ||
            sceneName == "CSE_Weapon" ||
            sceneName == "GameScene")
        {
            return true;
        }

        return TilePaintManager.HasInstance && InstanceFinder.NetworkManager != null;
    }
}
