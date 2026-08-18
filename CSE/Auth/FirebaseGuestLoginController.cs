using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FirebaseGuestLoginController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button guestLoginButton;
    [SerializeField] private Button googleLoginButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Nickname")]
    [SerializeField] private GameObject nicknamePanel;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button nicknameConfirmButton;
    [SerializeField] private Button nicknameCancelButton;
    [SerializeField] private bool requireNicknameConfirmationAfterGoogle = true;

    [Header("Google Sign-In")]
    [SerializeField] private string googleWebClientId;

    [Header("Scene")]
    [SerializeField] private string nextSceneName = "LobbyScene";

#if UNITY_EDITOR
    [Header("Editor Testing")]
    [SerializeField] private bool resetLoginOnEditorPlay;
#endif

    private FirebaseAuth auth;
    private bool isFirebaseReady;
    private bool isSigningIn;
    private FirebaseUser pendingUser;

    private void Awake()
    {
        SetLoginButtonsInteractable(false);
        SetNicknamePanelVisible(false);
        SetStatus("Firebase initializing...");
        InitializeFirebase();
    }

    private void OnDestroy()
    {
        auth = null;
    }

    public void OnClick_GuestLogin()
    {
        if (!isFirebaseReady || auth == null)
        {
            SetStatus("Firebase is not ready yet.");
            return;
        }

        if (isSigningIn)
        {
            return;
        }

        isSigningIn = true;
        SetLoginButtonsInteractable(false);
        SetNicknamePanelVisible(false);
        SetStatus("Signing in as guest...");

        if (auth.CurrentUser != null)
        {
            ReuseCurrentGuestOrCreateNew();
            return;
        }

        SignInNewGuest();
    }

    public void OnClick_GoogleLogin()
    {
        if (!isFirebaseReady || auth == null)
        {
            SetStatus("Firebase is not ready yet.");
            return;
        }

        if (isSigningIn)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(googleWebClientId))
        {
            SetStatus("Google web client id is not configured.");
            return;
        }

        if (!GoogleSignInBridge.IsAvailable)
        {
            SetStatus("Google Sign-In plugin setup is required.");
            Debug.LogWarning(
                "[FirebaseAuth] Google login is wired, but Google Sign-In Unity plugin " +
                "and GOOGLE_SIGN_IN scripting define are not configured yet.");
            return;
        }

        isSigningIn = true;
        SetLoginButtonsInteractable(false);
        SetNicknamePanelVisible(false);
        SetStatus("Signing in with Google...");

        GoogleSignInBridge.SignIn(
            googleWebClientId,
            SignInFirebaseWithGoogleIdToken,
            message =>
            {
                isSigningIn = false;
                SetStatus(message);
                SetLoginButtonsInteractable(true);
            });
    }

    public async void OnClick_ConfirmNickname()
    {
        string nickname = PlayerProfile.SanitizeNickname(
            nicknameInput != null ? nicknameInput.text : string.Empty);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            SetStatus("Please enter a nickname.");
            return;
        }

        SetNicknameButtonsInteractable(false);
        SetStatus("Checking nickname...");
        bool isAvailable;
        try
        {
            isAvailable = await FirebasePlayerProfileStore.IsNicknameAvailableAsync(nickname);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            SetStatus("Nickname check failed. Please try again.");
            SetNicknameButtonsInteractable(true);
            return;
        }

        if (!isAvailable)
        {
            SetStatus(FirebasePlayerProfileStore.LastNicknameAvailabilityCheckFailed
                ? "Nickname check failed. Please try again."
                : "That nickname is already in use.");
            SetNicknameButtonsInteractable(true);
            return;
        }

        if (!PlayerProfile.TryConfirmNickname(nickname))
        {
            SetStatus("Please enter a nickname.");
            SetNicknameButtonsInteractable(true);
            return;
        }

        SetStatus("Nickname saved.");
        TryUpdateFirebaseDisplayName(PlayerProfile.Nickname);
        LoadNextScene();
    }

    public void OnClick_CancelNickname()
    {
        pendingUser = null;
        SetNicknamePanelVisible(false);
        SetLoginButtonsInteractable(true);
        SetStatus("Login canceled before nickname setup.");
    }

    private void SignInNewGuest()
    {
        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(task =>
        {
            isSigningIn = false;

            if (task.IsCanceled)
            {
                SetStatus("Guest sign-in was canceled.");
                SetLoginButtonsInteractable(true);
                return;
            }

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                SetStatus("Guest sign-in failed. Check Console.");
                SetLoginButtonsInteractable(true);
                return;
            }

            CompleteLogin(task.Result.User, "Guest sign-in success", false);
        });
    }

    private void ReuseCurrentGuestOrCreateNew()
    {
        FirebaseUser currentUser = auth.CurrentUser;
        if (currentUser == null)
        {
            SignInNewGuest();
            return;
        }

        CompleteLogin(currentUser, "Guest sign-in reused", false);
    }

    private void SignInFirebaseWithGoogleIdToken(string idToken)
    {
        Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
        FirebaseUser currentUser = auth.CurrentUser;

        if (currentUser != null && currentUser.IsAnonymous)
        {
            currentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogWarning(
                        "[FirebaseAuth] Anonymous account link failed; falling back to Google sign-in.");
                    if (task.Exception != null)
                    {
                        Debug.LogException(task.Exception);
                    }

                    SignInFirebaseWithGoogleCredential(credential);
                    return;
                }

                CompleteLogin(currentUser, "Google sign-in linked to guest account", true);
            });
            return;
        }

        SignInFirebaseWithGoogleCredential(credential);
    }

    private void SignInFirebaseWithGoogleCredential(Credential credential)
    {
        auth.SignInAndRetrieveDataWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
        {
            isSigningIn = false;

            if (task.IsCanceled)
            {
                SetStatus("Google sign-in was canceled.");
                SetLoginButtonsInteractable(true);
                return;
            }

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                SetStatus("Google sign-in failed. Check Console.");
                SetLoginButtonsInteractable(true);
                return;
            }

            CompleteLogin(task.Result.User, "Google sign-in success", true);
        });
    }


    private void InitializeFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                SetStatus("Firebase dependency check failed.");
                return;
            }

            DependencyStatus dependencyStatus = task.Result;
            if (dependencyStatus != DependencyStatus.Available)
            {
                SetStatus("Firebase unavailable: " + dependencyStatus);
                Debug.LogError("[FirebaseAuth] Could not resolve Firebase dependencies: " + dependencyStatus);
                return;
            }

            auth = FirebaseAuth.DefaultInstance;
            isFirebaseReady = true;

#if UNITY_EDITOR
            if (resetLoginOnEditorPlay)
            {
                auth.SignOut();
                PlayerProfile.ClearLocalGuestProfile();
                SetStatus("Ready for guest sign-in.");
                SetLoginButtonsInteractable(true);
                Debug.Log("[FirebaseAuth] Editor play reset cleared cached guest login.");
                return;
            }
#endif

            if (auth.CurrentUser != null)
            {
                PlayerProfile.SetFirebaseUser(auth.CurrentUser.UserId, auth.CurrentUser.IsAnonymous);
                Debug.Log("[FirebaseAuth] Saved guest session found. uid=" + auth.CurrentUser.UserId);
                SetStatus("Account ready. Choose a login option to continue.");
                SetLoginButtonsInteractable(true);
                return;
            }

            SetStatus("Ready for sign-in.");
            SetLoginButtonsInteractable(true);
        });
    }

    private void CompleteLogin(FirebaseUser user, string logMessage, bool requireNicknameSetup)
    {
        isSigningIn = false;

        if (user == null)
        {
            SetStatus("Sign-in failed. User data is missing.");
            SetLoginButtonsInteractable(true);
            return;
        }

        PlayerProfile.SetFirebaseUser(user.UserId, user.IsAnonymous && !requireNicknameSetup);
        Debug.Log("[FirebaseAuth] " + logMessage + ". uid=" + user.UserId);
        pendingUser = user;
        LoadCloudProfileThenContinue(requireNicknameSetup);
    }

    private async void LoadCloudProfileThenContinue(bool requireNicknameSetup)
    {
        SetStatus("Loading player profile...");

        try
        {
            bool loaded = await FirebasePlayerProfileStore.LoadCurrentUserAsync();
            if (!loaded && !FirebasePlayerProfileStore.LastLoadFailedTransiently)
            {
                CharacterAppearanceStore.EnsureNewProfileDefaultAppearance();

                if (!requireNicknameSetup || PlayerProfile.HasConfirmedNickname())
                {
                    FirebasePlayerProfileStore.SaveCurrentSnapshot();
                }
            }
            else
            {
                FirebasePlayerProfileStore.ApplyCurrentLocalDataToRuntime();
            }
        }
        catch (TaskCanceledException)
        {
            Debug.LogWarning("[FirestoreProfile] Profile load was canceled; using local cache.");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[FirestoreProfile] Profile load failed; using local cache.");
            Debug.LogException(exception);
        }

        ContinueAfterProfileLoad(requireNicknameSetup);
    }

    private void ContinueAfterProfileLoad(bool requireNicknameSetup)
    {
        if (requireNicknameConfirmationAfterGoogle &&
            requireNicknameSetup &&
            !PlayerProfile.HasConfirmedNickname())
        {
            ShowNicknameSetup();
            return;
        }

        SetStatus("Signed in as " + PlayerProfile.Nickname + ".");
        LoadNextScene();
    }

    private void ShowNicknameSetup()
    {
        if (nicknameInput != null)
        {
            nicknameInput.text = PlayerProfile.HasConfirmedNickname()
                ? PlayerProfile.Nickname
                : string.Empty;
            nicknameInput.Select();
        }

        SetNicknamePanelVisible(true);
        SetStatus("Choose your nickname.");
    }

    private void TryUpdateFirebaseDisplayName(string nickname)
    {
        FirebaseUser user = pendingUser != null ? pendingUser : auth?.CurrentUser;
        if (user == null || string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        UserProfile profile = new UserProfile { DisplayName = nickname };
        user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogWarning("[FirebaseAuth] Firebase display name update failed.");
                if (task.Exception != null)
                {
                    Debug.LogException(task.Exception);
                }
            }
        });

        FirebasePlayerProfileStore.SaveCurrentSnapshot();
    }

    private void LoadNextScene()
    {
        pendingUser = null;
        SetNicknamePanelVisible(false);
        SceneManager.LoadScene(nextSceneName);
    }

    private void SetLoginButtonsInteractable(bool value)
    {
        if (guestLoginButton != null)
        {
            guestLoginButton.interactable = value;
        }

        if (googleLoginButton != null)
        {
            googleLoginButton.interactable = value;
        }
    }

    private void SetNicknamePanelVisible(bool visible)
    {
        if (nicknamePanel != null)
        {
            nicknamePanel.SetActive(visible);
        }

        if (nicknameConfirmButton != null)
        {
            nicknameConfirmButton.interactable = visible;
        }

        if (nicknameCancelButton != null)
        {
            nicknameCancelButton.interactable = visible;
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

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[FirebaseAuth] " + message);
    }
}
