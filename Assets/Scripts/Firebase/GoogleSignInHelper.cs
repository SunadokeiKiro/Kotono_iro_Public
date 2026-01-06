using UnityEngine;
using System.Threading.Tasks;

// NOTE: Requires 'Google Sign-In Unity Plugin' to be imported.
// https://github.com/googlesamples/google-signin-unity

public class GoogleSignInHelper : MonoBehaviour
{
    /*
    public static GoogleSignInHelper Instance { get; private set; }

    public string webClientId = "YOUR_WEB_CLIENT_ID_FROM_GOOGLE_CLOUD_CONSOLE"; // Get this from Google Cloud Console

    private void Awake()
    {
        Instance = this;
        
        // Initialize Google Sign-In Configuration
        GoogleSignIn.Configuration = new GoogleSignInConfiguration
        {
            WebClientId = webClientId,
            RequestIdToken = true,
            RequestEmail = true
        };
    }

    public void SignIn(System.Action<string, string> onSuccess, System.Action<string> onError)
    {
        GoogleSignIn.DefaultInstance.SignIn().ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                onError?.Invoke("Canceled");
            }
            else if (task.IsFaulted)
            {
                onError?.Invoke("Error: " + task.Exception);
            }
            else
            {
                // Success
                string idToken = task.Result.IdToken;
                string accessToken = task.Result.AccessToken; // Sometimes needed depending on setup
                onSuccess?.Invoke(idToken, accessToken);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void SignOut()
    {
        GoogleSignIn.DefaultInstance.SignOut();
    }
    */
    
    // Placeholder to prevent cheat errors before import
    public void Start()
    {
        Debug.LogWarning("GoogleSignInHelper is present but commented out. Import Google Sign-In Plugin to enable.");
    }
}
