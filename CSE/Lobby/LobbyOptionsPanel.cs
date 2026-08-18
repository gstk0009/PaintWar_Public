using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CSE
{
    [DisallowMultipleComponent]
    public sealed class LobbyOptionsPanel : MonoBehaviour
    {
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private TMP_Text bgmVolumeText;
        [SerializeField] private Button linkGoogleButton;
        [SerializeField] private Button resetNicknameButton;
        [SerializeField] private Button logoutButton;
        [SerializeField] private Button deleteAccountButton;
        [SerializeField] private TMP_Text accountStatusText;
        [SerializeField] private GameObject nicknamePanel;
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button nicknameConfirmButton;
        [SerializeField] private Button nicknameCancelButton;
        [SerializeField] private string googleWebClientId;
        [SerializeField] private string loginSceneName = "LoginScene";
        [SerializeField] private float deleteConfirmSeconds = 5f;

        private bool isAccountActionInProgress;
        private float deleteConfirmExpiresAt = -1f;
        private FirebaseUser pendingLinkedUser;
        private bool isNicknameResetMode;

        private void Awake()
        {
            if (openButton != null)
            {
                openButton.onClick.AddListener(OpenPanel);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(ClosePanel);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.minValue = 0f;
                bgmVolumeSlider.maxValue = 1f;
                bgmVolumeSlider.wholeNumbers = false;
                bgmVolumeSlider.onValueChanged.AddListener(SetBgmVolume);
            }

            if (linkGoogleButton != null)
            {
                linkGoogleButton.onClick.AddListener(LinkGoogleAccount);
            }

            if (resetNicknameButton != null)
            {
                resetNicknameButton.onClick.AddListener(OpenNicknameReset);
            }

            if (logoutButton != null)
            {
                logoutButton.onClick.AddListener(Logout);
            }

            if (deleteAccountButton != null)
            {
                deleteAccountButton.onClick.AddListener(RequestOrConfirmDeleteAccount);
            }

            if (nicknameConfirmButton != null)
            {
                nicknameConfirmButton.onClick.AddListener(ConfirmLinkedNickname);
            }

            if (nicknameCancelButton != null)
            {
                nicknameCancelButton.onClick.AddListener(CancelLinkedNickname);
            }
        }

        private void Start()
        {
            float volume = SoundManager.Instance != null ? SoundManager.Instance.BgmVolume : PlayerPrefs.GetFloat("BgmVolume", 0.7f);
            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.SetValueWithoutNotify(volume);
            }

            RefreshVolumeText(volume);
            ResetDeleteConfirmation();
            SetNicknamePanelVisible(false);
            SetAccountStatus(string.Empty);
            RefreshGoogleLinkButton();
            ClosePanel();
        }

        private void Update()
        {
            if (!isAccountActionInProgress &&
                deleteConfirmExpiresAt > 0f &&
                Time.unscaledTime > deleteConfirmExpiresAt)
            {
                ResetDeleteConfirmation();
            }
        }

        private void OnDestroy()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveListener(OpenPanel);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(ClosePanel);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.onValueChanged.RemoveListener(SetBgmVolume);
            }

            if (linkGoogleButton != null)
            {
                linkGoogleButton.onClick.RemoveListener(LinkGoogleAccount);
            }

            if (resetNicknameButton != null)
            {
                resetNicknameButton.onClick.RemoveListener(OpenNicknameReset);
            }

            if (logoutButton != null)
            {
                logoutButton.onClick.RemoveListener(Logout);
            }

            if (deleteAccountButton != null)
            {
                deleteAccountButton.onClick.RemoveListener(RequestOrConfirmDeleteAccount);
            }

            if (nicknameConfirmButton != null)
            {
                nicknameConfirmButton.onClick.RemoveListener(ConfirmLinkedNickname);
            }

            if (nicknameCancelButton != null)
            {
                nicknameCancelButton.onClick.RemoveListener(CancelLinkedNickname);
            }
        }

        public void OpenPanel()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            RefreshGoogleLinkButton();
            ShowNicknameSetupIfNeeded();
        }

        public void ClosePanel()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void SetBgmVolume(float volume)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.SetBgmVolume(volume);
            }
            else
            {
                PlayerPrefs.SetFloat("BgmVolume", Mathf.Clamp01(volume));
                PlayerPrefs.Save();
            }

            RefreshVolumeText(volume);
        }

        private void LinkGoogleAccount()
        {
            if (isAccountActionInProgress)
            {
                return;
            }

            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser currentUser = auth != null ? auth.CurrentUser : null;
            if (currentUser == null)
            {
                SetAccountStatus("Please log in again before linking Google.");
                return;
            }

            if (!currentUser.IsAnonymous)
            {
                SetAccountStatus("This account is already linked.");
                RefreshGoogleLinkButton();
                return;
            }

            if (string.IsNullOrWhiteSpace(googleWebClientId))
            {
                SetAccountStatus("Google web client id is not configured.");
                return;
            }

            if (!GoogleSignInBridge.IsAvailable)
            {
                SetAccountStatus("Google Sign-In setup is required.");
                return;
            }

            isAccountActionInProgress = true;
            SetAccountButtonsInteractable(false);
            SetNicknamePanelVisible(false);
            SetAccountStatus("Linking Google account...");

            GoogleSignInBridge.SignIn(
                googleWebClientId,
                LinkCurrentUserWithGoogleIdToken,
                message =>
                {
                    isAccountActionInProgress = false;
                    SetAccountButtonsInteractable(true);
                    RefreshGoogleLinkButton();
                    SetAccountStatus(message);
                });
        }

        private void OpenNicknameReset()
        {
            if (isAccountActionInProgress)
            {
                return;
            }

            pendingLinkedUser = FirebaseAuth.DefaultInstance?.CurrentUser;
            isNicknameResetMode = true;
            isAccountActionInProgress = true;
            SetAccountButtonsInteractable(false);

            if (nicknameInput != null)
            {
                nicknameInput.text = PlayerProfile.HasConfirmedNickname()
                    ? PlayerProfile.Nickname
                    : string.Empty;
                nicknameInput.Select();
            }

            SetNicknamePanelVisible(true);
            SetNicknameButtonsInteractable(true);
            SetAccountStatus("Choose your new nickname.");
        }

        private void LinkCurrentUserWithGoogleIdToken(string idToken)
        {
            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser currentUser = auth != null ? auth.CurrentUser : null;
            if (currentUser == null)
            {
                isAccountActionInProgress = false;
                SetAccountButtonsInteractable(true);
                SetAccountStatus("Please log in again before linking Google.");
                return;
            }

            if (!currentUser.IsAnonymous)
            {
                isAccountActionInProgress = false;
                SetAccountButtonsInteractable(true);
                RefreshGoogleLinkButton();
                SetAccountStatus("This account is already linked.");
                return;
            }

            Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
            currentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    isAccountActionInProgress = false;
                    SetAccountButtonsInteractable(true);
                    RefreshGoogleLinkButton();
                    SetAccountStatus("Google linking was canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    isAccountActionInProgress = false;
                    SetAccountButtonsInteractable(true);
                    RefreshGoogleLinkButton();
                    SetAccountStatus("Google linking failed. The account may already be in use.");
                    return;
                }

                pendingLinkedUser = currentUser;
                isNicknameResetMode = false;
                PlayerProfile.SetFirebaseUser(currentUser.UserId);
                ShowLinkedNicknameSetup();
            });
        }

        private void ShowLinkedNicknameSetup()
        {
            isAccountActionInProgress = true;
            SetAccountButtonsInteractable(false);

            if (nicknameInput != null)
            {
                nicknameInput.text = PlayerProfile.HasConfirmedNickname()
                    ? PlayerProfile.Nickname
                    : string.Empty;
                nicknameInput.Select();
            }

            SetNicknamePanelVisible(true);
            SetNicknameButtonsInteractable(true);
            SetAccountStatus("Google linked. Choose your nickname.");
        }

        private async void ConfirmLinkedNickname()
        {
            string nickname = PlayerProfile.SanitizeNickname(
                nicknameInput != null ? nicknameInput.text : string.Empty);
            if (string.IsNullOrWhiteSpace(nickname))
            {
                SetAccountStatus("Please enter a nickname.");
                return;
            }

            SetNicknameButtonsInteractable(false);
            SetAccountStatus("Checking nickname...");
            bool isAvailable;
            try
            {
                isAvailable = await FirebasePlayerProfileStore.IsNicknameAvailableAsync(nickname);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                SetAccountStatus("Nickname check failed. Please try again.");
                SetNicknameButtonsInteractable(true);
                return;
            }

            if (!isAvailable)
            {
                SetAccountStatus(FirebasePlayerProfileStore.LastNicknameAvailabilityCheckFailed
                    ? "Nickname check failed. Please try again."
                    : "That nickname is already in use.");
                SetNicknameButtonsInteractable(true);
                return;
            }

            if (!PlayerProfile.TryConfirmNickname(nickname))
            {
                SetAccountStatus("Please enter a nickname.");
                SetNicknameButtonsInteractable(true);
                return;
            }

            FirebaseUser user = pendingLinkedUser != null
                ? pendingLinkedUser
                : FirebaseAuth.DefaultInstance?.CurrentUser;
            if (user != null)
            {
                UserProfile profile = new UserProfile { DisplayName = PlayerProfile.Nickname };
                user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Debug.LogException(task.Exception);
                    }
                });
            }

            LobbyButtonController.SendCurrentPlayerProfile();
            FirebasePlayerProfileStore.SaveCurrentProfile();
            pendingLinkedUser = null;
            isAccountActionInProgress = false;
            SetNicknamePanelVisible(false);
            SetAccountButtonsInteractable(true);
            RefreshGoogleLinkButton();
            SetAccountStatus(isNicknameResetMode
                ? "Nickname changed to " + PlayerProfile.Nickname + "."
                : "Google account linked as " + PlayerProfile.Nickname + ".");
            isNicknameResetMode = false;
        }

        private void CancelLinkedNickname()
        {
            pendingLinkedUser = null;
            isAccountActionInProgress = false;
            SetNicknamePanelVisible(false);
            SetAccountButtonsInteractable(true);
            RefreshGoogleLinkButton();
            SetAccountStatus(isNicknameResetMode
                ? "Nickname change canceled."
                : "Google linked. You can set nickname later from this panel.");
            isNicknameResetMode = false;
        }

        private void Logout()
        {
            if (isAccountActionInProgress)
            {
                return;
            }

            isAccountActionInProgress = true;
            SetAccountButtonsInteractable(false);
            SetAccountStatus("Logging out...");

            SignOutAndReturnToLogin("[Account] Logged out.", false);
        }

        private void RequestOrConfirmDeleteAccount()
        {
            if (isAccountActionInProgress)
            {
                return;
            }

            if (Time.unscaledTime > deleteConfirmExpiresAt)
            {
                deleteConfirmExpiresAt = Time.unscaledTime + Mathf.Max(1f, deleteConfirmSeconds);
                SetAccountStatus("Tap Delete Account again to permanently remove this account.");
                return;
            }

            DeleteAccount();
        }

        private void DeleteAccount()
        {
            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser user = auth != null ? auth.CurrentUser : null;

            if (user == null)
            {
                SignOutAndReturnToLogin("[Account] No Firebase user found while deleting account.", true);
                return;
            }

            isAccountActionInProgress = true;
            SetAccountButtonsInteractable(false);
            SetAccountStatus("Deleting account...");

            user.DeleteAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    isAccountActionInProgress = false;
                    ResetDeleteConfirmation();
                    SetAccountButtonsInteractable(true);
                    SetAccountStatus("Account deletion was canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    isAccountActionInProgress = false;
                    ResetDeleteConfirmation();
                    SetAccountButtonsInteractable(true);
                    SetAccountStatus("Account deletion failed. Please log in again and retry.");
                    return;
                }

                SignOutAndReturnToLogin("[Account] Account deleted.", true);
            });
        }

        private void SignOutAndReturnToLogin(string logMessage, bool clearLocalGuestProfile)
        {
            DisconnectNetworkIfNeeded();
            RoomAssignmentStore.Clear();

            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            if (auth != null)
            {
                auth.SignOut();
            }

            GoogleSignInBridge.SignOut();
            if (clearLocalGuestProfile)
            {
                PlayerProfile.ClearLocalGuestProfile();
            }
            else
            {
                PlayerProfile.ClearRuntimeGuestProfile();
            }

            Debug.Log(logMessage);
            SceneManager.LoadScene(loginSceneName);
        }

        private void DisconnectNetworkIfNeeded()
        {
            if (InstanceFinder.ClientManager != null &&
                InstanceFinder.ClientManager.Started)
            {
                InstanceFinder.ClientManager.StopConnection();
            }

            if (!Application.isBatchMode &&
                InstanceFinder.ServerManager != null &&
                InstanceFinder.ServerManager.Started)
            {
                InstanceFinder.ServerManager.StopConnection(true);
            }
        }

        private void SetAccountButtonsInteractable(bool value)
        {
            if (linkGoogleButton != null)
            {
                linkGoogleButton.interactable = value && ShouldAllowGoogleLink();
            }

            if (resetNicknameButton != null)
            {
                resetNicknameButton.interactable = value;
            }

            if (logoutButton != null)
            {
                logoutButton.interactable = value;
            }

            if (deleteAccountButton != null)
            {
                deleteAccountButton.interactable = value;
            }

            if (nicknameConfirmButton != null)
            {
                nicknameConfirmButton.interactable = value;
            }

            if (nicknameCancelButton != null)
            {
                nicknameCancelButton.interactable = value;
            }
        }

        private void ResetDeleteConfirmation()
        {
            deleteConfirmExpiresAt = -1f;
        }

        private void SetAccountStatus(string message)
        {
            if (accountStatusText != null)
            {
                accountStatusText.text = message;
            }
        }

        private void SetNicknamePanelVisible(bool visible)
        {
            if (nicknamePanel != null)
            {
                nicknamePanel.SetActive(visible);
            }
        }

        private void SetNicknameButtonsInteractable(bool value)
        {
            if (nicknameConfirmButton != null)
            {
                nicknameConfirmButton.interactable = value;
            }

            if (nicknameCancelButton != null)
            {
                nicknameCancelButton.interactable = value;
            }
        }

        private void RefreshGoogleLinkButton()
        {
            if (linkGoogleButton != null)
            {
                linkGoogleButton.interactable = !isAccountActionInProgress && ShouldAllowGoogleLink();
            }
        }

        private void ShowNicknameSetupIfNeeded()
        {
            if (isAccountActionInProgress || !ShouldPromptForGoogleNickname())
            {
                return;
            }

            pendingLinkedUser = FirebaseAuth.DefaultInstance?.CurrentUser;
            ShowLinkedNicknameSetup();
        }

        private static bool ShouldAllowGoogleLink()
        {
            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser currentUser = auth != null ? auth.CurrentUser : null;
            return currentUser != null && currentUser.IsAnonymous;
        }

        private static bool ShouldPromptForGoogleNickname()
        {
            FirebaseAuth auth = FirebaseAuth.DefaultInstance;
            FirebaseUser currentUser = auth != null ? auth.CurrentUser : null;
            return currentUser != null &&
                !currentUser.IsAnonymous &&
                !PlayerProfile.HasConfirmedNickname();
        }

        private void RefreshVolumeText(float volume)
        {
            if (bgmVolumeText != null)
            {
                bgmVolumeText.text = Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f) + "%";
            }
        }
    }
}
