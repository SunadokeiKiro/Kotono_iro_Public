// Scripts/SessionManager.cs
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

/// <summary>
/// デバイスセッション管理マネージャー。
/// アプリ起動時やフォアグラウンド復帰時にサーバーにセッションを登録し、
/// 同時ログインデバイス数を制限するシステムを担います。
/// </summary>
public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance { get; private set; }

    [SerializeField] private ApiConfig apiConfig;
    
    private const string DEVICE_ID_KEY = "kotono_device_id";
    
    /// <summary>
    /// このデバイスの一意なID（PlayerPrefsに永続化）
    /// </summary>
    public string DeviceId { get; private set; }
    
    /// <summary>
    /// セッションが有効かどうか（サーバーに登録済みか）
    /// </summary>
    public bool IsSessionValid { get; private set; } = false;
    
    /// <summary>
    /// セッション登録に失敗した場合のイベント（他デバイスにキックされた等）
    /// </summary>
    public event Action<string> OnSessionExpired;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeDeviceId();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeDeviceId()
    {
        // PlayerPrefsからデバイスIDを取得、なければ新規生成
        if (PlayerPrefs.HasKey(DEVICE_ID_KEY))
        {
            DeviceId = PlayerPrefs.GetString(DEVICE_ID_KEY);
        }
        else
        {
            // GUIDを生成して永続化
            DeviceId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(DEVICE_ID_KEY, DeviceId);
            PlayerPrefs.Save();
            Debug.Log($"[SessionManager] New Device ID generated: {DeviceId}");
        }
        Debug.Log($"[SessionManager] Device ID: {DeviceId}");
    }

    /// <summary>
    /// サーバーにデバイスセッションを登録します。
    /// アプリ起動時、ログイン後、フォアグラウンド復帰時などに呼び出してください。
    /// </summary>
    public IEnumerator RegisterSession(Action<bool> onComplete)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("[SessionManager] Cannot register session: User not logged in.");
            IsSessionValid = false;
            onComplete?.Invoke(false);
            yield break;
        }

        // Get Firebase Auth Token
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            Debug.LogError($"[SessionManager] Token Error: {tokenTask.Exception}");
            IsSessionValid = false;
            onComplete?.Invoke(false);
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = $"{apiConfig.CloudFunctionsBaseUrl}/registerSession";

        // Prepare Request Body
        var requestData = new RegisterSessionRequest
        {
            deviceId = DeviceId,
            deviceInfo = $"{SystemInfo.deviceModel} ({SystemInfo.deviceName})",
            platform = Application.platform.ToString()
        };
        string jsonBody = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<RegisterSessionResponse>(request.downloadHandler.text);
                IsSessionValid = response.success;
                Debug.Log($"[SessionManager] Session {response.action}. Plan: {response.plan}, Limit: {response.limit}");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[SessionManager] RegisterSession Failed: {request.error} ({request.responseCode})");
                IsSessionValid = false;
                onComplete?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// セッション切れエラーを処理します（proxyAmiVoiceから401が返ってきた場合等）。
    /// </summary>
    public void HandleSessionExpired(string message)
    {
        IsSessionValid = false;
        Debug.LogWarning($"[SessionManager] Session Expired: {message}");
        OnSessionExpired?.Invoke(message);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        // フォアグラウンド復帰時にセッションを再登録（ハートビート相当）
        if (!pauseStatus && IsSessionValid)
        {
            StartCoroutine(RegisterSession(null));
        }
    }

    [Serializable]
    private class RegisterSessionRequest
    {
        public string deviceId;
        public string deviceInfo;
        public string platform;
    }

    [Serializable]
    private class RegisterSessionResponse
    {
        public bool success;
        public string action;
        public string plan;
        public int limit;
    }
}
