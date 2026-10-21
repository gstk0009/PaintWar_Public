using System;
using System.Threading.Tasks;
using UnityEngine;

#if GOOGLE_SIGN_IN
using Firebase.Extensions;
using Google;
#endif

// Thin adapter for Google Sign-In.
// When the Google Sign-In Unity plugin is imported and GOOGLE_SIGN_IN is added
// to scripting define symbols, this class requests a Google ID token that can
// be exchanged for a Firebase credential.
public static class GoogleSignInBridge
{
    private const int SignInTimeoutMilliseconds = 20000;

    public static bool IsAvailable
    {
        get
        {
#if GOOGLE_SIGN_IN
            return true;
#else
            return false;
#endif
        }
    }

    public static void SignIn(
        string webClientId,
        Action<string> onIdToken,
        Action<string> onError)
    {
#if GOOGLE_SIGN_IN
        if (string.IsNullOrWhiteSpace(webClientId))
        {
            onError?.Invoke("Google web client id is not configured.");
            return;
        }

        GoogleSignIn.Configuration = new GoogleSignInConfiguration
        {
            WebClientId = webClientId,
            RequestEmail = true,
            RequestIdToken = true,
            UseGameSignIn = false
        };

        bool callbackResolved = false;
        Debug.Log("[FirebaseAuth] Starting Google sign-in request.");

        Task.Delay(SignInTimeoutMilliseconds).ContinueWithOnMainThread(_ =>
        {
            if (callbackResolved)
            {
                return;
            }

            callbackResolved = true;
            Debug.LogWarning("[FirebaseAuth] Google sign-in timed out waiting for plugin callback.");
            onError?.Invoke("Google sign-in timed out. Please try again.");
        });

        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(task =>
        {
            if (callbackResolved)
            {
                return;
            }

            callbackResolved = true;
            if (task.IsCanceled)
            {
                onError?.Invoke("Google sign-in was canceled.");
                return;
            }

            if (task.IsFaulted)
            {
                string message = BuildGoogleSignInFailureMessage(task.Exception);
                Debug.LogWarning("[FirebaseAuth] " + message);
                Debug.LogException(task.Exception);
                onError?.Invoke(message);
                return;
            }

            GoogleSignInUser user = task.Result;
            if (user == null || string.IsNullOrEmpty(user.IdToken))
            {
                onError?.Invoke("Google sign-in did not return an ID token.");
                return;
            }

            onIdToken?.Invoke(user.IdToken);
        });
#else
        onError?.Invoke(
            "Google Sign-In plugin is not installed or GOOGLE_SIGN_IN define is missing.");
#endif
    }

#if GOOGLE_SIGN_IN
    private static string BuildGoogleSignInFailureMessage(Exception exception)
    {
        GoogleSignIn.SignInException signInException =
            FindGoogleSignInException(exception);
        if (signInException == null)
        {
            return "Google sign-in failed. Check Console.";
        }

        switch (signInException.Status)
        {
            case GoogleSignInStatusCode.DeveloperError:
                return "Google sign-in failed: DeveloperError. Check SHA and client IDs.";
            case GoogleSignInStatusCode.NetworkError:
                return "Google sign-in failed: NetworkError.";
            case GoogleSignInStatusCode.Timeout:
                return "Google sign-in failed: Timeout.";
            default:
                return "Google sign-in failed: " + signInException.Status + ".";
        }
    }

    private static GoogleSignIn.SignInException FindGoogleSignInException(
        Exception exception)
    {
        if (exception == null)
        {
            return null;
        }

        GoogleSignIn.SignInException signInException =
            exception as GoogleSignIn.SignInException;
        if (signInException != null)
        {
            return signInException;
        }

        AggregateException aggregateException = exception as AggregateException;
        if (aggregateException != null)
        {
            foreach (Exception innerException in aggregateException.InnerExceptions)
            {
                signInException = FindGoogleSignInException(innerException);
                if (signInException != null)
                {
                    return signInException;
                }
            }
        }

        return FindGoogleSignInException(exception.InnerException);
    }
#endif

    public static void SignOut()
    {
#if GOOGLE_SIGN_IN
        GoogleSignIn.DefaultInstance.SignOut();
#endif
    }
}
