// Scripts/ApiHandler.cs
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;

public class ApiHandler : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private ApiConfig apiConfig;

    [Header("Controller References")]
    [SerializeField] private GameController gameController;
    // 変更: APIのレスポンスに合わせてフィールドを追加
[System.Serializable]
public class SentimentAnalysisResponse
{
    public string status;
    public string message; 
    public string error_message; 
    public string code;
    public string session_id;
    public string service_id;
    public string text; // ★ Restored
    public List<Segment> segments; // ★ Restored
    public SentimentAnalysisParams parameters;
    public SentimentAnalysisResults results;
}

[System.Serializable]
public class SentimentAnalysisParams
{
    public string service_id;
    public string domain_id;
}

[System.Serializable]
public class SentimentAnalysisResults
{
    public SentimentResult[] results;
}

[System.Serializable]
public class Segment
{
    public string text;
    public List<SentimentResult> results;
}

[System.Serializable]
public class SentimentResult
{
    public string text;
    // "sentiment" (array of 3 floats: [negative, positive, neutral]) logic is handled elsewhere or assumed.
}
    [SerializeField] private MainUIManager mainUIManager;

    private string userApiKey;
    private string activeApiKey;
    private string activeDParams;
    private bool isUsingAppKey = false;
    private string sessionID;

    // ★★★ 追加: APIが解析中かどうかの状態を持つプロパティ ★★★
    public bool IsAnalyzing { get; private set; } = false;

    // ★★★ 追加: 初期化完了フラグ ★★★
    private bool isApiKeyLoaded = false;
    private bool isSubscriptionSynced = false;
    public bool IsInitialized => isApiKeyLoaded && isSubscriptionSynced;

    // 解析完了イベント
    public event Action OnAnalysisCompleted;

    // ★ Check if API Key (User or App) is available
    public bool IsReady 
    {
        get 
        {
            if (!string.IsNullOrEmpty(userApiKey)) return true;
            if (SubscriptionManager.Instance != null && SubscriptionManager.Instance.CanUseAppKey) return true;
            return false;
        }
    }

    void Start()
    {
        if (apiConfig == null) { Debug.LogError("ApiConfig missing"); enabled = false; return; }
        if (gameController == null) { Debug.LogError("GameController missing"); enabled = false; return; }
        if (mainUIManager == null) { Debug.LogError("MainUIManager missing"); enabled = false; return; }

        StartCoroutine(InitializeApiHandler());
    }

    private IEnumerator InitializeApiHandler()
    {
        // 1. Wait for Firebase initialization
        float timeout = Time.time + 10f;
        while ((FirebaseConfig.Instance == null || !FirebaseConfig.Instance.IsInitialized) && Time.time < timeout)
        {
            yield return null;
        }

        // 2. Load User API Key from Firestore
        if (FirestoreManager.Instance != null)
        {
            bool keyLoadComplete = false;
            FirestoreManager.Instance.GetUserApiKey(
                (key) => {
                    this.userApiKey = key;
                    Debug.Log($"[ApiHandler] User Key loaded from Firestore: {(string.IsNullOrEmpty(key) ? "None" : "Found")}");
                    keyLoadComplete = true;
                }, 
                (error) => {
                    Debug.LogWarning($"[ApiHandler] Failed to load User Key: {error}");
                    keyLoadComplete = true;
                }
            );
            
            // Wait for key load with timeout
            float keyTimeout = Time.time + 5f;
            while (!keyLoadComplete && Time.time < keyTimeout)
            {
                yield return null;
            }
        }
        isApiKeyLoaded = true;
        Debug.Log("[ApiHandler] API Key loading phase complete.");

        // 3. Wait for SubscriptionManager sync (プラン情報が必要)
        if (SubscriptionManager.Instance != null)
        {
            float syncTimeout = Time.time + 15f;
            while (SubscriptionManager.Instance.IsSyncing && Time.time < syncTimeout)
            {
                yield return null;
            }
        }
        isSubscriptionSynced = true;
        Debug.Log($"[ApiHandler] Initialization complete. IsReady={IsReady}");

        // --- Session Recovery Check ---
        CheckPendingSessions();
    }

    private void CheckPendingSessions()
    {
        var pendingIds = SessionRecoveryManager.GetPendingSessions();
        if (pendingIds.Count > 0)
        {
            Debug.Log($"[ApiHandler] Found {pendingIds.Count} pending sessions. Attempting recovery...");
            // APIキーの準備が必要です。リカバリー時はとりあえずAppKey設定などを試みますが、
            // 確実に行うため PrepareConnectionSettings を呼び出します。
            // (リカバリーはバックグラウンドで行いたいが、MainThreadでCoroutine起動)
            
            // Note: 複数のセッションがある場合、順番に処理するか並列にするか。
            // ここでは簡易的に、最初に見つかった1つだけ、あるいはすべてをリカバリーします。
            // ただし、PrepareConnectionSettings は stateful なので注意。
            
            if (PrepareConnectionSettings(false)) 
            {
                foreach(var id in pendingIds)
                {
                    Debug.Log($"[ApiHandler] Recovering session: {id}");
                    StartCoroutine(PollJobStatus(id));
                }
            }
        }
    }



    public void StartAnalysis(string audioFilePath, bool isAutoRecord = false)
    {
        StartCoroutine(StartAnalysisAsync(audioFilePath, isAutoRecord));
    }

    private IEnumerator StartAnalysisAsync(string audioFilePath, bool isAutoRecord = false)
    {
        // ★ 初期化完了を待機 (最大5秒)
        if (!IsInitialized)
        {
            Debug.Log("[ApiHandler] Waiting for initialization to complete...");
            mainUIManager.ShowProcessingMessage("初期化中...");
            
            float initTimeout = Time.time + 5f;
            while (!IsInitialized && Time.time < initTimeout)
            {
                yield return null;
            }
            
            if (!IsInitialized)
            {
                Debug.LogWarning("[ApiHandler] Initialization timed out. Proceeding anyway.");
            }
        }

        // 1. flag set immediately
        IsAnalyzing = true;
        mainUIManager.ShowProcessingMessage(isUsingAppKey ? "解析中(App)..." : "解析中...");

        // 2. 音声ファイルの存在チェック
        if (!File.Exists(audioFilePath))
        {
            Debug.LogError($"Audio file not found: {audioFilePath}");
            mainUIManager.HideProcessingMessage();
            IsAnalyzing = false;
            OnAnalysisCompleted?.Invoke();
            yield break;
        }

        // 3. クォータの事前チェック (Double Check)
        float duration = 0f;
        try 
        {
            FileInfo fi = new FileInfo(audioFilePath);
            if (fi.Exists)
            {
                // WAV Header ~44bytes. 16bit(2byte), 1ch, 44100Hz
                duration = (float)(Math.Max(0, fi.Length - 44)) / (44100f * 2f * 1f);
            }
        }
        catch (Exception e) 
        { 
            Debug.LogWarning($"Failed to calculate duration: {e.Message}"); 
            duration = 0f; 
        }

        if (duration <= 0) 
        {
            // Just a safeguard, if calculation fails, maybe let it pass or block?
            // Let's treat 0 as "Unknown" but safe to check quota if we had a remaining. 
            // Actually if 0, we can't check quota properly. Let's assume it requires at least some quota.
            // But for now, if 0, let's skip check or assume small.
        }

        if (SubscriptionManager.Instance != null)
        {
            float remaining = SubscriptionManager.Instance.GetRemainingQuotaSeconds();
            // Allow a small tolerance (e.g. 2.0s) for discrepancy between estimated file duration and actual remaining quota
            if (remaining < (duration - 2.0f)) 
            {
                Debug.LogError($"[ApiHandler] Insufficient quota. Remaining: {remaining:F1}s, Required: {duration:F1}s");
                mainUIManager.ShowBlockingMessage("月間制限により送信できません", true);
                IsAnalyzing = false;
                OnAnalysisCompleted?.Invoke();
                yield break;
            }
        }

        // 4. キーとパラメータの初期設定
        bool ready = PrepareConnectionSettings(forceAppKey: false);
        if (!ready)
        {
             Debug.LogError("No valid API Key found.");
             mainUIManager.ShowBlockingMessage("APIキーエラー", true);
             IsAnalyzing = false;
             OnAnalysisCompleted?.Invoke();
             yield break;
        }

        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError("[ApiHandler] Cannot start analysis: GameObject is inactive.");
            mainUIManager.HideProcessingMessage(); 
            IsAnalyzing = false;
            OnAnalysisCompleted?.Invoke();
            yield break;
        }

        StartCoroutine(PostRequest(audioFilePath, isAutoRecord));
    }
    


    /// <summary>
    /// APIキーとDパラメータを決定します。
    /// </summary>
    /// <param name="forceAppKey">trueの場合、強制的にAppKeyを使用します(フォールバック用)</param>
    /// <returns>準備完了ならtrue</returns>
    private bool PrepareConnectionSettings(bool forceAppKey)
    {
        // ユーザーキー優先
        if (!forceAppKey && !string.IsNullOrEmpty(userApiKey))
        {
            activeApiKey = userApiKey;
            
            // Ultimateチェック (プランに基づくDパラメータ設定)
            bool isUltimate = false;
            if (SubscriptionManager.Instance != null) isUltimate = (SubscriptionManager.Instance.CurrentPlan == PlanType.Ultimate);
            
            activeDParams = SetLoggingOptOut(apiConfig.DParameter, isUltimate);
            isUsingAppKey = false;
            Debug.Log($"Using User API Key. (Plan: {SubscriptionManager.Instance?.CurrentPlan})");
            return true;
        }

        // App Key判定 (有料プランなら使用可)
        if (SubscriptionManager.Instance != null && SubscriptionManager.Instance.CanUseAppKey)
        {
            isUsingAppKey = true;
            activeApiKey = "CLOUD_FUNCTION_WILL_INJECT_KEY"; // Cloud Function側でキーを付与するため、Unity側はダミーでOK
            
            bool isUltimate = (SubscriptionManager.Instance.CurrentPlan == PlanType.Ultimate);
            activeDParams = SetLoggingOptOut(apiConfig.DParameter, isUltimate);
            
            Debug.Log($"Using App Key Mode (via Cloud Function).");
            return true;
        }

        return false;
    }

    private string SetLoggingOptOut(string originalParam, bool optOut)
    {
        // 文字列置換でパラメータ書き換え (簡易実装)
        // loggingOptOut=True または False を探して置換、なければ追加
        string target = optOut ? "loggingOptOut=True" : "loggingOptOut=False";
        
        if (originalParam.Contains("loggingOptOut=True"))
            return originalParam.Replace("loggingOptOut=True", target);
        if (originalParam.Contains("loggingOptOut=False"))
            return originalParam.Replace("loggingOptOut=False", target);
            
        return originalParam + " " + target;
    }

    private IEnumerator PostRequest(string audioFilePath, bool isAutoRecord = false)
    {
        Debug.Log($"Sending audio recognition request... (isAutoRecord={isAutoRecord})");
        // IsAnalyzing = true; // Moved to StartAnalysis
        // mainUIManager.ShowLoading(...); // Moved to StartAnalysis or updated there

        byte[] audioData;
        try { audioData = File.ReadAllBytes(audioFilePath); }
        catch (Exception) { IsAnalyzing = false; mainUIManager.HideProcessingMessage(); yield break; }
        
        // リトライループ (最大1回: UserKey -> AppKey)
        int maxRetries = 1;
        bool fallbackTriggered = false;

        for (int i = 0; i <= maxRetries; i++)
        {
            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("u", activeApiKey),
                new MultipartFormDataSection("d", activeDParams),
                new MultipartFormDataSection("isAutoRecord", isAutoRecord ? "true" : "false"), // ★ 自動録音フラグ追加
                new MultipartFormFileSection("a", audioData, Path.GetFileName(audioFilePath), "audio/wav")
            };

            string url = isUsingAppKey ? "https://us-central1-kotono-iro-project.cloudfunctions.net/proxyAmiVoice" : apiConfig.ApiUrlBase;

            // Get Token if using App Key (Cloud Function)
            string authToken = "";
            if (isUsingAppKey)
            {
                 var user = FirebaseAuth.DefaultInstance.CurrentUser;
                 if (user != null)
                 {
                     var task = user.TokenAsync(false);
                     yield return new WaitUntil(() => task.IsCompleted);
                     if (task.Exception == null) authToken = task.Result;
                     else Debug.LogError($"[ApiHandler] Failed to get Auth Token: {task.Exception}");
                 }
            }

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                if (isUsingAppKey && !string.IsNullOrEmpty(authToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + authToken);
                }

                // ★ 非同期でリクエスト開始（進捗表示のため）
                var asyncOp = request.SendWebRequest();
                
                // ★ アップロード進捗をリアルタイム表示
                float fileSizeMB = audioData.Length / (1024f * 1024f);
                while (!asyncOp.isDone)
                {
                    float progress = request.uploadProgress;
                    int percent = Mathf.RoundToInt(progress * 100f);
                    
                    string progressText = isUsingAppKey 
                        ? $"解析中(App)... アップロード {percent}%"
                        : $"解析中... アップロード {percent}%";
                    
                    // ファイルサイズも表示（大きい場合のみ）
                    if (fileSizeMB >= 1f)
                    {
                        progressText += $"\n({fileSizeMB:F1}MB)";
                    }
                    
                    mainUIManager.ShowProcessingMessage(progressText);
                    
                    yield return new WaitForSeconds(0.2f); // 0.2秒ごとに更新
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    HandlePostResponse(request.downloadHandler.text);
                    yield break; // 成功により終了
                }
                else
                {
                    long code = request.responseCode;
                    Debug.LogWarning($"Request failed. Code: {code}, KeyType: {(isUsingAppKey ? "App" : "User")}");

                    // 401 Unauthorized で、かつUserKeyを使っていた場合 -> AppKeyにフォールバック
                    if ((code == 401 || code == 403) && !isUsingAppKey && !fallbackTriggered)
                    {
                        Debug.Log("User Key unauthorized. Attempting fallback to App Key...");
                        if (PrepareConnectionSettings(forceAppKey: true))
                        {
                            fallbackTriggered = true;
                            // ループ継続してリトライ
                            continue; 
                        }
                    }

                    // 復帰不可のエラー
                    Debug.LogError($"Final API Error: {request.error}");
                    
                    // ★ 「解析中...」パネルを非表示にしてからエラーを表示
                    mainUIManager.HideProcessingMessage();
                    
                    // ★ ユーザーにわかりやすいエラーメッセージを表示
                    string userMessage = BuildApiErrorMessage(code, request.error);
                    mainUIManager.ShowBlockingMessage(userMessage, true);
                    
                    IsAnalyzing = false;
                    OnAnalysisCompleted?.Invoke();
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// APIエラーコードに基づいてユーザー向けのエラーメッセージを構築します。
    /// </summary>
    private string BuildApiErrorMessage(long httpCode, string rawError)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("APIエラーが発生しました");
        sb.AppendLine("");
        
        switch (httpCode)
        {
            case 413:
                sb.AppendLine("録音ファイルが大きすぎます");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・録音時間を短くしてください");
                sb.AppendLine("・20分以内を推奨します");
                break;
            case 401:
            case 403:
                sb.AppendLine("認証エラー");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・APIキーを確認してください");
                sb.AppendLine("・再ログインしてください");
                break;
            case 429:
                sb.AppendLine("リクエスト制限に達しました");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・しばらく待ってから再試行");
                break;
            case 500:
            case 502:
            case 503:
                sb.AppendLine("サーバーエラー");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・しばらく待ってから再試行");
                break;
            default:
                sb.AppendLine($"エラーコード: {httpCode}");
                if (!string.IsNullOrEmpty(rawError))
                {
                    sb.AppendLine($"詳細: {rawError}");
                }
                break;
        }
        
        return sb.ToString().Trim();
    }

    /// <summary>
    /// AmiVoice APIからのエラーメッセージを日本語に変換します。
    /// </summary>
    private string TranslateApiErrorMessage(string rawMessage, string code)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        
        // 既知のエラーパターンをチェック
        if (!string.IsNullOrEmpty(rawMessage))
        {
            // 信頼度が低すぎるエラー
            if (rawMessage.Contains("confidence is below the threshold"))
            {
                sb.AppendLine("音声が不明瞭で認識できませんでした");
                sb.AppendLine("");
                sb.AppendLine("考えられる原因:");
                sb.AppendLine("・音声が小さすぎる");
                sb.AppendLine("・背景ノイズが多い");
                sb.AppendLine("・マイクとの距離が遠い");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・マイクに近づいて話す");
                sb.AppendLine("・静かな環境で録音する");
                return sb.ToString().Trim();
            }
            
            // 音声データなしエラー
            if (rawMessage.Contains("no speech") || rawMessage.Contains("no audio"))
            {
                sb.AppendLine("音声が検出されませんでした");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・マイクが正しく接続されているか確認");
                sb.AppendLine("・音声入力があるか確認");
                return sb.ToString().Trim();
            }
            
            // トランスクリプト失敗
            if (rawMessage.Contains("Failed to transcribe"))
            {
                sb.AppendLine("音声の文字起こしに失敗しました");
                sb.AppendLine("");
                sb.AppendLine("考えられる原因:");
                sb.AppendLine("・音声が不明瞭");
                sb.AppendLine("・音量が小さい");
                sb.AppendLine("");
                sb.AppendLine("対処法:");
                sb.AppendLine("・はっきりと話す");
                sb.AppendLine("・マイクに近づく");
                return sb.ToString().Trim();
            }
        }
        
        // 未知のエラー: 元のメッセージをそのまま表示
        sb.AppendLine("分析エラーが発生しました");
        if (!string.IsNullOrEmpty(rawMessage))
        {
            sb.AppendLine("");
            sb.AppendLine($"詳細: {rawMessage}");
        }
        if (!string.IsNullOrEmpty(code))
        {
            sb.AppendLine($"(Code: {code})");
        }
        
        return sb.ToString().Trim();
    }
    private void HandlePostResponse(string jsonResponse)
    {
        // (省略なし, sessionID取得)
        try
        {
            RecognitionResponse recogResponse = JsonUtility.FromJson<RecognitionResponse>(jsonResponse);
            if (recogResponse != null && !string.IsNullOrEmpty(recogResponse.sessionid))
            {
                sessionID = recogResponse.sessionid;
                
                // ★★★ Session Recovery Add ★★★
                SessionRecoveryManager.AddSession(sessionID);
                
                StartCoroutine(PollJobStatus(sessionID));
            }
            else
            {
                mainUIManager.HideProcessingMessage();
                IsAnalyzing = false;
                OnAnalysisCompleted?.Invoke();
            }
        }
        catch { mainUIManager.HideProcessingMessage(); IsAnalyzing = false; OnAnalysisCompleted?.Invoke(); }
    }

    private IEnumerator PollJobStatus(string targetSessionId)
    {
        if (string.IsNullOrEmpty(targetSessionId)) { 
            mainUIManager.HideProcessingMessage(); 
            IsAnalyzing = false; 
            OnAnalysisCompleted?.Invoke(); 
            yield break; 
        }
        
        string pollUrl;
        string authToken = "";

        if (isUsingAppKey)
        {
            // Cloud Function Proxy for Polling
            pollUrl = $"https://us-central1-kotono-iro-project.cloudfunctions.net/proxyAmiVoice?sessionid={targetSessionId}";
            
             var user = FirebaseAuth.DefaultInstance.CurrentUser;
             if (user != null)
             {
                 var task = user.TokenAsync(false);
                 yield return new WaitUntil(() => task.IsCompleted);
                 if (task.Exception == null) authToken = task.Result;
             }
        }
        else
        {
            // Direct AmiVoice Polling
            pollUrl = $"{apiConfig.ApiUrlBase}/{targetSessionId}";
        }

        const int maxPolls = 100;
        const float pollInterval = 4f;

        for (int i = 0; i < maxPolls; i++)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(pollUrl))
            {
                // activeApiKeyを使用 (AppKeyモードならToken, UserKeyモードならそのキー)
                if (isUsingAppKey)
                {
                    if (!string.IsNullOrEmpty(authToken))
                    {
                        request.SetRequestHeader("Authorization", "Bearer " + authToken);
                    }
                }
                else
                {
                     request.SetRequestHeader("Authorization", "Bearer " + activeApiKey);
                }

                yield return request.SendWebRequest();
                // (以下変更なし)
                if (request.result == UnityWebRequest.Result.Success)
                {
                    // 成功時、HandlePollResponse内で RemoveSession されるようにする
                    if (HandlePollResponse(request.downloadHandler.text, targetSessionId)) 
                    { 
                        mainUIManager.HideProcessingMessage(); 
                        IsAnalyzing = false; 
                        OnAnalysisCompleted?.Invoke(); 
                        yield break; 
                    }
                }
                else
                {
                     // 通信エラーでも、まだ諦めるべきではないかもしれないが、
                     // 404など致命的なら削除すべき。ここでは4xx系ならRecoveryから削除して諦めるなどの判断も必要だが、
                     // 安全側に倒して「削除しない」でおく（次回起動時に再トライ）
                     if(request.responseCode == 404) 
                     {
                         SessionRecoveryManager.RemoveSession(targetSessionId);
                         mainUIManager.HideProcessingMessage(); IsAnalyzing = false; OnAnalysisCompleted?.Invoke(); yield break;
                     }
                     // 他のエラーはリトライ
                }
            }
            mainUIManager.ShowProcessingMessage($"分析中... ({(i + 1) * pollInterval}s)");
            yield return new WaitForSeconds(pollInterval);
        }
        mainUIManager.HideProcessingMessage(); IsAnalyzing = false; OnAnalysisCompleted?.Invoke();
    }

    // (HandlePollResponse, PrintResultsSummary, SaveLogは修正なし)
    private bool HandlePollResponse(string jsonResponse, string processedSessionId)
    {
        try
        {
            SentimentAnalysisResponse sentimentResponse = JsonUtility.FromJson<SentimentAnalysisResponse>(jsonResponse);

            if (sentimentResponse == null)
            {
                Debug.LogError("Polling response is null. Parsing may have failed.");
                return true; 
            }

            switch (sentimentResponse.status)
            {
                case "completed":
                    Debug.Log("Analysis completed successfully!");
                    
                    // ★★★ Session Recovery Remove ★★★
                    SessionRecoveryManager.RemoveSession(processedSessionId);

                    SaveLog(jsonResponse);
                    gameController.SetParametersFromJson(jsonResponse);
                    Debug.Log("Sentiment parameters sent to GameController.");
                    PrintResultsSummary(sentimentResponse);
                    return true;

                case "error":
                    // メッセージ優先度: error_message > message > "Unknown Error"
                    string rawMsg = !string.IsNullOrEmpty(sentimentResponse.error_message) ? sentimentResponse.error_message : sentimentResponse.message;
                    Debug.LogError($"Job error from API: {rawMsg} (Code: {sentimentResponse.code})");
                    
                    // ★ エラーメッセージを日本語に変換してユーザーに表示
                    string userFriendlyMessage = TranslateApiErrorMessage(rawMsg, sentimentResponse.code);
                    mainUIManager.ShowBlockingMessage(userFriendlyMessage, true);

                    // エラー完了の場合もリストから外す
                    SessionRecoveryManager.RemoveSession(processedSessionId);
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse polling response JSON: {e.Message}\nResponse: {jsonResponse}");
            return true;
        }
    }
    
    private void PrintResultsSummary(SentimentAnalysisResponse response)
    {
        StringBuilder sb = new StringBuilder("Recognized Text:\n");
        if (response.segments != null && response.segments.Count > 0)
        {
            foreach (var segment in response.segments)
            {
                if (segment.results != null && segment.results.Count > 0)
                {
                    sb.AppendLine(segment.results[0].text);
                }
                else
                {
                    sb.AppendLine(segment.text);
                }
            }
        }
        else if (!string.IsNullOrEmpty(response.text))
        {
            sb.AppendLine(response.text);
        }
        Debug.Log(sb.ToString().Trim());
    }

    private void SaveLog(string data)
    {
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.SaveAnalysisLog(data);
        }
        // Remove local file saving for security
        // try { ... } catch ...
    }
}