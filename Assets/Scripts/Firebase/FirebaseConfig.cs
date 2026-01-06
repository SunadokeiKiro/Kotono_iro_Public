using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Note: This script assumes the Firebase Unity SDK is imported.
// Since we cannot import packages here, this is a skeleton implementation to guide the user.

public class FirebaseConfig : MonoBehaviour
{
    // Make sure to add this script to an initialization object in the scene.
    public static FirebaseConfig Instance { get; private set; }

    // Firebase dependency status
    // public Firebase.DependencyStatus dependencyStatus = Firebase.DependencyStatus.UnavailableOther;
    public bool IsInitialized { get; private set; } = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeFirebase();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeFirebase()
    {
        Debug.Log("[FirebaseConfig] Initializing Firebase...");
        
        Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == Firebase.DependencyStatus.Available)
            {
                // Init needs to happen on main thread to be safe for events
                MainThreadDispatcher.Enqueue(() => {
                    InitializeFirebaseAuthentication();
                    IsInitialized = true;
                    Debug.Log("[FirebaseConfig] Firebase Initialized Successfully.");
                });
            }
            else
            {
                Debug.LogError(System.String.Format(
                  "[FirebaseConfig] Could not resolve all Firebase dependencies: {0}", dependencyStatus));
            }
        });
    }

    // Event for Auth State Change
    public event System.Action<Firebase.Auth.FirebaseUser> OnAuthStateChanged;

    private void InitializeFirebaseAuthentication()
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;

        // Note: Auto-sign out removed to allow session persistence.
        
        // Subscribe to state changes
        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    private void AuthStateChanged(object sender, System.EventArgs eventArgs)
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        // Logging can happen on background thread safely usually, but Enqueue ensures order
        MainThreadDispatcher.Enqueue(() => {
            if (auth.CurrentUser != null)
            {
                Debug.LogFormat("[FirebaseConfig] User Signed In: {0} ({1})", auth.CurrentUser.DisplayName, auth.CurrentUser.UserId);
            }
            else
            {
                Debug.Log("[FirebaseConfig] User Signed Out");
            }
            OnAuthStateChanged?.Invoke(auth.CurrentUser);
        });
    }

    // --- Email / Password Auth ---

    public void RegisterWithEmail(string email, string password, System.Action<string> onSuccess, System.Action<string> onError)
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWith(task => {
            if (task.IsCanceled)
            {
                MainThreadDispatcher.Enqueue(() => onError("Registration canceled."));
                return;
            }
            if (task.IsFaulted)
            {
                MainThreadDispatcher.Enqueue(() => onError("Registration failed: " + task.Exception.Flatten().InnerExceptions[0].Message));
                return;
            }

            // Success
            Firebase.Auth.AuthResult result = task.Result;
            Firebase.Auth.FirebaseUser newUser = result.User;
            MainThreadDispatcher.Enqueue(() => onSuccess(newUser.UserId));
        });
    }

    public void LoginWithEmail(string email, string password, System.Action<string> onSuccess, System.Action<string> onError)
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWith(task => {
            if (task.IsCanceled)
            {
                MainThreadDispatcher.Enqueue(() => onError("Login canceled."));
                return;
            }
            if (task.IsFaulted)
            {
                MainThreadDispatcher.Enqueue(() => onError("Login failed: " + task.Exception.Flatten().InnerExceptions[0].Message));
                return;
            }

            // Success
            Firebase.Auth.AuthResult result = task.Result;
            Firebase.Auth.FirebaseUser newUser = result.User;
            MainThreadDispatcher.Enqueue(() => onSuccess(newUser.UserId));
        });
    }

    public void SignOut()
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        auth.SignOut();
        // Google SignOut logic will be added here later
    }

    // --- Google Sign-In (Placeholder) ---
    // Note: Requires Google Sign-In Plugin.
    /*
    public void SignInWithGoogle(string idToken, string accessToken, System.Action<string> onSuccess, System.Action<string> onError)
    {
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        Firebase.Auth.Credential credential = Firebase.Auth.GoogleAuthProvider.GetCredential(idToken, accessToken);
        
        auth.SignInWithCredentialAsync(credential).ContinueWith(task => {
            if (task.IsCanceled) { MainThreadDispatcher.Enqueue(() => onError("Google Sign-in canceled.")); return; }
            if (task.IsFaulted) { MainThreadDispatcher.Enqueue(() => onError("Google Sign-in error: " + task.Exception.Flatten().InnerExceptions[0].Message)); return; }

            Firebase.Auth.AuthResult result = task.Result;
            MainThreadDispatcher.Enqueue(() => onSuccess(result.User.UserId));
        });
    }
    */
}
