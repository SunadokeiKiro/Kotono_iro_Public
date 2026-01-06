using UnityEngine;
using UnityEngine.UI;
using TMPro; // Assuming TextMeshPro is used based on previous file views
using System.Collections;

public class LoginUIManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject mainAppContent; // The parent object of the main game UI (to hide/show)

    [Header("Input Fields")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;

    [Header("Buttons")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button googleLoginButton;
    [SerializeField] private Button toggleModeButton; // Switch between "Login" and "Register" mode

    [Header("Status Text")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI toggleButtonText;

    private bool isRegisterMode = false;
    
    // Event to notify when login is fully processed and Main App is shown
    public static event System.Action OnLoginSuccessEvent;

    // Google Sign-In Helper Reference (To be implemented)
    // [SerializeField] private GoogleSignInHelper googleHelper;

    private void OnEnable()
    {
        // Ensure UI state (text, visibility) is correct whenever this object/panel is enabled
        UpdateUIState();
    }

    void Start()
    {
        // Initial State: Hide everything to prevent flashing
        if (loginPanel != null) loginPanel.SetActive(false);
        if (mainAppContent != null) mainAppContent.SetActive(false);
        
        // Start Initialization Routine
        StartCoroutine(InitializeLoginState());

        // Button Listeners - Separate actions for each button
        if (loginButton) loginButton.onClick.AddListener(OnLoginClick);
        if (registerButton) registerButton.onClick.AddListener(OnRegisterClick);
        
        // Optional: Toggle Button (If used for other UI changes, otherwise optional)
        if (toggleModeButton != null)
            toggleModeButton.onClick.AddListener(ToggleMode);
            
        if (googleLoginButton != null)
            googleLoginButton.onClick.AddListener(OnGoogleLoginClick);

        // Initialize UI State (Text, etc.)
        UpdateUIState();
    }

    private void OnDestroy()
    {
        // Critical: Unsubscribe to prevent memory leaks and zombie event calls on scene change
        if (FirebaseConfig.Instance != null)
        {
            FirebaseConfig.Instance.OnAuthStateChanged -= HandleAuthStateChanged;
        }
    }

    private IEnumerator InitializeLoginState()
    {
        // 1. Wait for FirebaseConfig Instance
        while (FirebaseConfig.Instance == null)
        {
            yield return null;
        }

        // 2. Wait for Firebase Initialization to complete
        while (!FirebaseConfig.Instance.IsInitialized)
        {
            yield return null;
        }

        // 3. Check current Auth State
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        
        // 4. Update UI based on state
        if (user != null)
        {
            OnLoginSuccess(user.UserId);
        }
        else
        {
            ShowLoginScreen();
        }

        // 5. Subscribe to future changes
        FirebaseConfig.Instance.OnAuthStateChanged += HandleAuthStateChanged;
    }

    private void HandleAuthStateChanged(Firebase.Auth.FirebaseUser user)
    {
        Debug.Log($"[LoginUIManager] Auth State Changed: {(user != null ? user.UserId : "null")}");
        
        // Only react if the state actually changes from what we are currently showing
        // But for safety, we just enforce the correct state.
        if (user != null) 
        {
            // Note: If we are already in the main app, this might be redundant but harmless.
            if (loginPanel.activeSelf) OnLoginSuccess(user.UserId);
        }
        else 
        {
            // If user logged out, show login screen
            if (!loginPanel.activeSelf) ShowLoginScreen();
        }
    }

    // Toggle Mode assumes we might still want to swap visibility, 
    // but if the user wants both visible, we shouldn't force hide them unless requested.
    // For now, let's keep both visible by removing the hiding logic in Start/UpdateUIState.
    private void ToggleMode()
    {
        isRegisterMode = !isRegisterMode;
        UpdateUIState();
    }

    private void UpdateUIState()
    {
        if (statusText != null)
        {
            statusText.text = isRegisterMode 
                ? "メールアドレスとパスワードを入力して、新規登録を行ってください。" 
                : "メールアドレスとパスワードを入力して、ログインしてください。";
            statusText.color = Color.white;
        }
        
        // If the user has a Toggle Button, they might expect the "Other" button to hide.
        // But if they don't have one assigned, we shouldn't hide anything.
        if (toggleModeButton != null)
        {
             if (isRegisterMode)
             {
                 if (toggleButtonText) toggleButtonText.text = "ログインへ切替";
                 if (loginButton) loginButton.gameObject.SetActive(false);
                 if (registerButton) registerButton.gameObject.SetActive(true);
             }
             else
             {
                 if (toggleButtonText) toggleButtonText.text = "新規登録へ切替";
                 if (loginButton) loginButton.gameObject.SetActive(true);
                 if (registerButton) registerButton.gameObject.SetActive(false);
             }
        }
    }

    public void OnLoginClick()
    {
        string email = emailInput.text;
        string password = passwordInput.text;

        Debug.Log($"[LoginUIManager] Login Clicked. Email: {email}");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("メールアドレスとパスワードを入力してください。", true);
            return;
        }

        SetStatus("ログイン中...", false);
        FirebaseConfig.Instance.LoginWithEmail(email, password, 
            (uid) => {
                Debug.Log("[LoginUIManager] Login Success");
                SetStatus("ログインに成功しました！", false);
                OnLoginSuccess(uid); // Ensure UI updates immediately
            }, 
            (error) => {
                Debug.LogError($"[LoginUIManager] Login Error: {error}");
                SetStatus("エラー: " + error, true);
            }
        );
    }

    public void OnRegisterClick()
    {
        string email = emailInput.text;
        string password = passwordInput.text;

        Debug.Log($"[LoginUIManager] Register Clicked. Email: {email}");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("メールアドレスとパスワードを入力してください。", true);
            return;
        }

        SetStatus("登録中...", false);
        FirebaseConfig.Instance.RegisterWithEmail(email, password, 
            (uid) => {
                Debug.Log("[LoginUIManager] Registration Success");
                SetStatus("登録に成功しました！", false);
                OnLoginSuccess(uid); // Ensure UI updates immediately
            }, 
            (error) => {
                Debug.LogError($"[LoginUIManager] Registration Error: {error}");
                SetStatus("エラー: " + error, true);
            }
        );
    }

    public void OnGoogleLoginClick()
    {
        SetStatus("Googleログインプラグインが未導入のため、まだ利用できません。", true);
        /* 
        // Example integration
        if (GoogleSignInHelper.Instance != null) {
             GoogleSignInHelper.Instance.SignIn((idToken, accessToken) => {
                 FirebaseConfig.Instance.SignInWithGoogle(idToken, accessToken, ...);
             });
        }
        */
    }

    private void OnLoginSuccess(string uid)
    {
        Debug.Log("Login UI: Login Success. Hiding Panel.");
        if (loginPanel != null) loginPanel.SetActive(false);
        if (mainAppContent != null) mainAppContent.SetActive(true);
        
        // Notify GameController or others that login is fully complete and App UI is ready
        OnLoginSuccessEvent?.Invoke();
    }

    private void ShowLoginScreen()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (mainAppContent != null) mainAppContent.SetActive(false);
        
        // ★ 修正: GameControllerが表示したblockingPanelを非表示にする
        // mainAppContentが非アクティブになるとMainUIManagerも非アクティブになる可能性があるため、
        // 直接探して非表示にする
        var loadingPanel = GameObject.Find("LoadingPanel");
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? Color.red : Color.white;
        }
    }
}
