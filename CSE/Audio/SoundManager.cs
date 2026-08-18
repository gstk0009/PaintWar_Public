using UnityEngine;
using UnityEngine.SceneManagement;

namespace CSE
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        private const string BgmVolumeKey = "BgmVolume";
        [Header("BGM Clips")]
        [SerializeField] private AudioClip loginAndLobbyBgm;
        [SerializeField] private AudioClip gameBgm;
        [Tooltip("BGM used when the match enters its final countdown. Falls back to Next Round BGM when empty.")]
        [SerializeField] private AudioClip finalCountdownBgm;
        [SerializeField] private AudioClip nextRoundBgm;
        [SerializeField] private AudioClip winnerBgm;

        [Header("Authored Audio Sources")]
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource movementSfxSource;
        [SerializeField] private AudioSource[] spatialSfxSources;

        [Header("Gameplay SFX")]
        [SerializeField] private AudioClip movementStepClip;
        [SerializeField] private AudioClip teleportClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Min(0.1f)] private float spatialSfxMinDistance = 3.5f;
        [SerializeField, Min(0.5f)] private float spatialSfxMaxDistance = 12f;

        [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float maxBgmOutputVolume = 0.64f;
        [SerializeField] private bool playOnSceneLoaded = true;

        private string currentSceneName;
        private int nextSpatialSourceIndex;

        public float BgmVolume => bgmVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (bgmSource == null)
            {
                bgmSource = GetComponent<AudioSource>();
            }

            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumeKey, bgmVolume));
            ApplyBgmVolume();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (playOnSceneLoaded)
            {
                PlayForScene(SceneManager.GetActiveScene().name);
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void SetBgmVolume(float volume)
        {
            bgmVolume = Mathf.Clamp01(volume);
            ApplyBgmVolume();

            PlayerPrefs.SetFloat(BgmVolumeKey, bgmVolume);
            PlayerPrefs.Save();
        }

        private void ApplyBgmVolume()
        {
            if (bgmSource != null)
            {
                bgmSource.volume = bgmVolume * Mathf.Clamp01(maxBgmOutputVolume);
            }
        }

        private void OnValidate()
        {
            bgmVolume = Mathf.Clamp01(bgmVolume);
            maxBgmOutputVolume = Mathf.Clamp01(maxBgmOutputVolume);
            if (Application.isPlaying)
            {
                ApplyBgmVolume();
            }
        }

        public void PlayForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || bgmSource == null)
            {
                return;
            }

            AudioClip nextClip = GetBgmForScene(sceneName);
            currentSceneName = sceneName;

            if (nextClip == null)
            {
                bgmSource.Stop();
                bgmSource.clip = null;
                return;
            }

            if (bgmSource.clip == nextClip && bgmSource.isPlaying)
            {
                return;
            }

            PlayBgm(nextClip, true);
        }

        public void PlayRoundResultBgm()
        {
            PlayBgm(nextRoundBgm, true);
        }

        public void PlayFinalCountdownBgm()
        {
            PlayBgm(finalCountdownBgm != null ? finalCountdownBgm : nextRoundBgm, true);
        }

        public void PlayWinnerBgm()
        {
            PlayBgm(winnerBgm, false);
        }

        public void RestoreGameBgm()
        {
            PlayBgm(gameBgm, true);
        }

        public void PlayMovementStep()
        {
            if (movementStepClip == null || movementSfxSource == null || movementSfxSource.isPlaying)
            {
                return;
            }

            float pitchVariation = UnityEngine.Random.Range(0.97f, 1.03f);
            float volumeDbVariation = UnityEngine.Random.Range(-1.5f, 1.5f);
            movementSfxSource.clip = movementStepClip;
            movementSfxSource.pitch = pitchVariation;
            movementSfxSource.volume = 0.86f * Mathf.Pow(10f, volumeDbVariation / 20f) * sfxVolume;
            movementSfxSource.Play();
        }

        public void PlayTeleportAt(Vector3 worldPosition)
        {
            PlaySfxAt(teleportClip, worldPosition, 0.96f);
        }

        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || sfxSource == null)
            {
                return;
            }

            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * sfxVolume);
        }

        public void PlaySfxAt(AudioClip clip, Vector3 worldPosition, float volumeScale = 1f)
        {
            if (clip == null || spatialSfxSources == null || spatialSfxSources.Length == 0)
            {
                return;
            }

            AudioSource selected = null;
            for (int i = 0; i < spatialSfxSources.Length; i++)
            {
                int index = (nextSpatialSourceIndex + i) % spatialSfxSources.Length;
                AudioSource candidate = spatialSfxSources[index];
                if (candidate != null && !candidate.isPlaying)
                {
                    selected = candidate;
                    nextSpatialSourceIndex = (index + 1) % spatialSfxSources.Length;
                    break;
                }
            }

            if (selected == null)
            {
                int index = nextSpatialSourceIndex % spatialSfxSources.Length;
                selected = spatialSfxSources[index];
                nextSpatialSourceIndex = (index + 1) % spatialSfxSources.Length;
            }

            if (selected == null)
            {
                return;
            }

            selected.transform.position = worldPosition;
            selected.minDistance = Mathf.Max(0.1f, spatialSfxMinDistance);
            selected.maxDistance = Mathf.Max(selected.minDistance + 0.1f, spatialSfxMaxDistance);
            selected.volume = Mathf.Clamp01(volumeScale) * sfxVolume;
            selected.clip = clip;
            selected.Play();
        }

        private void PlayBgm(AudioClip clip, bool loop)
        {
            if (bgmSource == null || clip == null)
            {
                return;
            }

            if (bgmSource.clip == clip && bgmSource.isPlaying)
            {
                bgmSource.loop = loop;
                return;
            }

            bgmSource.Stop();
            bgmSource.clip = clip;
            bgmSource.loop = loop;
            bgmSource.Play();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (playOnSceneLoaded && scene.name != currentSceneName)
            {
                PlayForScene(scene.name);
            }
        }

private AudioClip GetBgmForScene(string sceneName)
        {
            return sceneName == "GameScene" ? gameBgm : loginAndLobbyBgm;
        }
    }
}
