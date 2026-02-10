// Scripts/GameController.cs (Ver 2.1 - With Cloud Sync)
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

// Note: EmotionPoint, ArtData, TotalSentiments are defined in RippleDataStructs.cs

public class GameController : MonoBehaviour
{
    [Header("Configuration Assets")]
    [SerializeField] private EmotionCalculationConfig emotionConfig;

    [Header("Manager References")]
    [SerializeField] private MainUIManager mainUIManager;
    [SerializeField] private VFXRippleManager vfxRippleManager; 
    [SerializeField] private SimpleCameraController cameraController; 
    [SerializeField] private BackgroundGraphController graphController; // Optional: Streak Feature Heatmap
    [SerializeField] private PostProcessingController postProcessingController; // Art Enhancement
    [SerializeField] private CosmicBackgroundController cosmicBackgroundController; // Cosmic Background (replaces heatmap)
    [SerializeField] private MicrophoneController microphoneController; // ★追加: 録音ファイル名取得用


    private ArtData currentArtData = new ArtData();
    // private string currentArtDataPath; // Removed local path
    
    // private string totalSentimentsPath; // Removed local path

    // 現在閲覧中の月Key (例: "2023-11")
    private string currentMonthKey;

    // 現在フォーカス中の波紋 (nullならフォーカスなし)
    private RippleData? activeFocusedRipple = null;

    // ★追加: 最後の録音ファイル名を一時保持 (分析完了まで)
    private string pendingAudioFileName = null;

    [Header("Input Settings")]
    [SerializeField] private float rippleClickThreshold = 3.0f; // 1.0 -> 3.0 (Sensitivity Up)

    void Start()
    {
        if (emotionConfig == null || mainUIManager == null || vfxRippleManager == null) 
        {
            Debug.LogError("GameController: Essential components are missing in the Inspector!");
            enabled = false;
            return;
        }
        
        // ★★★ Camera Pivot Safety Setup ★★★
        if (cameraController != null && vfxRippleManager != null)
        {
            cameraController.SetPivot(vfxRippleManager.transform);
        }

        // 初期ロード: ログイン状態の確認と待機
        Debug.Log("[GameController] Start: Waiting for Firebase Auth State Change...");
        
        // ★ Debug Feedback
        if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Initializing...\nWaiting for Auth", false);

        // Fallback
        if (FirebaseConfig.Instance == null)
        {
             Debug.LogWarning("[GameController] FirebaseConfig instance is missing in Start.");
             if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Error: Firebase Config Missing", true);
        }
        
        // ★修正: シーン遷移後に認証イベントが発火しない問題への対応
        // Start()の時点で既にログイン済みの場合、イベントを待たずにデータをロード
        StartCoroutine(CheckAndLoadOnStart());
    }
    
    private IEnumerator CheckAndLoadOnStart()
    {
        // Firebase初期化を待つ（最大3秒）
        float timeout = 3f;
        while (FirebaseConfig.Instance == null || !FirebaseConfig.Instance.IsInitialized)
        {
            timeout -= Time.deltaTime;
            if (timeout <= 0)
            {
                Debug.LogWarning("[GameController] Firebase initialization timeout.");
                yield break;
            }
            yield return null;
        }
        
        // ★既にログイン済みならデータをロード
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user != null)
        {
            Debug.Log($"[GameController] User already logged in on Start: {user.UserId.Substring(0, 5)}... Loading data.");
            HandleAuthStateChanged(user);
        }
    }
    
    // Flag to ensure we don't load data before Login UI gives the go-ahead


    private void OnEnable()
    {
        if (SubscriptionManager.Instance != null)
        {
            SubscriptionManager.Instance.OnSyncStart += HandleSyncStart;
            SubscriptionManager.Instance.OnSyncEnd += HandleSyncEnd;
        }

        if (FirebaseConfig.Instance != null)
        {
            FirebaseConfig.Instance.OnAuthStateChanged += HandleAuthStateChanged;
        }
        
        LoginUIManager.OnLoginSuccessEvent += HandleLoginSuccess;
        
        // ★追加: MicrophoneControllerの録音完了イベント購読
        if (microphoneController != null)
        {
            microphoneController.OnRecordingFinished += HandleRecordingFinished;
        }
    }

    private void OnDisable()
    {
        if (SubscriptionManager.Instance != null)
        {
            SubscriptionManager.Instance.OnSyncStart -= HandleSyncStart;
            SubscriptionManager.Instance.OnSyncEnd -= HandleSyncEnd;
        }

        if (FirebaseConfig.Instance != null)
        {
            FirebaseConfig.Instance.OnAuthStateChanged -= HandleAuthStateChanged;
        }
        
        LoginUIManager.OnLoginSuccessEvent -= HandleLoginSuccess;
        
        // ★追加: MicrophoneControllerの録音完了イベント解除
        if (microphoneController != null)
        {
            microphoneController.OnRecordingFinished -= HandleRecordingFinished;
        }
    }

    /// <summary>
    /// 録音完了時に呼び出され、ファイル名を一時保持します。
    /// </summary>
    private void HandleRecordingFinished(float duration, bool wasAuto, string audioFileName)
    {
        pendingAudioFileName = audioFileName;
        Debug.Log($"[GameController] Recording finished. Duration: {duration:F2}s, Auto: {wasAuto}, File: {audioFileName}");
    }

    private void HandleLoginSuccess()
    {
        Debug.Log("[GameController] Received Login Success Signal. Loading Data.");
        if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Login Signal Received.\nLoading Data...", false);
        

        LoadWithCurrentSettings();
        
        // ★追加: 期限切れ録音の自動削除
        if (AudioStorageManager.Instance != null)
        {
            AudioStorageManager.Instance.CleanupExpiredRecordings();
        }
        
        // ★ Start Main Tutorial
        if (TutorialManager.Instance != null && mainUIManager != null)
        {
             // データのロード開始後、少し待ってからチュートリアル確認
             // (ローディング表示が終わった後くらいが望ましいが、ここでも可)
             StartCoroutine(TriggerTutorialAfterDelay());
        }
    }

    private IEnumerator TriggerTutorialAfterDelay()
    {
        // Auto-start tutorial logic removed as per user request (manual trigger only)
        yield break; 
        /*
        yield return new WaitForSeconds(1.5f); // Wait for initial loading UI to settle
        if (TutorialManager.Instance != null)
        {
            TutorialManager.Instance.StartMainTutorial(mainUIManager);
        }
        */
    }

    private void HandleAuthStateChanged(Firebase.Auth.FirebaseUser user)
    {
        Debug.Log($"[GameController] HandleAuthStateChanged: User is {(user != null ? "Logged In" : "Null")}");
        
        if (user != null)
        {
            // If we are already logged in (auto-login), we might not get HandleLoginSuccess if the UI is skipped.
            // Check if LoginUIManager is showing? Or just trust this if it's the first run.
            // Let's force load if it's a persistent session to ensure data appears.
            
            // ★ Critical Fix: If user is found on startup, just load! Waiting for "Login Signal" might freeze if Login UI thinks "Already logged in" and does nothing.
            if (mainUIManager != null) mainUIManager.ShowBlockingMessage($"Auth OK: {user.UserId.Substring(0,5)}...\nFetching Data...", false);
            
            LoadWithCurrentSettings(); 
        }
        else
        {
            ClearData();
            // 未ログイン時は操作可能にするためブロッキング解除
            if (mainUIManager != null) mainUIManager.HideBlockingMessage();
        }
    }

    // Unified helper for Current Month Key
    private string GetCurrentMonthKey()
    {
        // TimeManagerがあればJSTを取得、なければローカル時間（起動直後のフォールバック）
         return TimeManager.Instance != null 
            ? TimeManager.Instance.GetCurrentJstTime().ToString("yyyy-MM") 
            : DateTime.Now.ToString("yyyy-MM");
    }

    // Helper to load based on GameDataManager or Current Month
    private void LoadWithCurrentSettings()
    {
        StartCoroutine(LoadWithCurrentSettingsCoroutine());
    }

    private IEnumerator LoadWithCurrentSettingsCoroutine()
    {
        // ★ Strict Verification (Moved from LoadingScene)
        if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Verifying Subscription...", false);
        
        bool verificationDone = false;
        if (FirestoreManager.Instance != null && FirebaseConfig.Instance != null && FirebaseConfig.Instance.IsInitialized)
        {
            yield return FirestoreManager.Instance.VerifySubscriptionStrict((success) => {
                verificationDone = true;
            });
        }
        else
        {
            verificationDone = true;
        }

        // Wait just in case (though yield return above handles coroutine wait)
        // If VerifySubscriptionStrict is changed to not return Generic IEnumerator, we need to correct this.
        // FirestoreManager.VerifySubscriptionStrict IS an IEnumerator. So yield return works.

        try
        {
            string targetMonth = null;
            if (GameDataManager.Instance != null)
            {
                targetMonth = GameDataManager.Instance.MonthToLoad;
            }

            string currentKey = GetCurrentMonthKey();

            if (string.IsNullOrEmpty(targetMonth))
            {
                targetMonth = currentKey;
            }

            // Check if sync is already running
            if (SubscriptionManager.Instance != null && SubscriptionManager.Instance.IsSyncing)
            {
                // Close button hidden for syncing
                if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Syncing...", false);
            }

            if (targetMonth == currentKey)
            {
                 if (GameDataManager.Instance != null && GameDataManager.Instance.MonthToLoad != null)
                 {
                     Debug.Log("[GameController] Clearing GameDataManager.MonthToLoad as it matches current month.");
                     GameDataManager.Instance.MonthToLoad = null;
                 }
            }

            // ★ Async Call - Message will be updated inside LoadMonthData
            LoadMonthData(targetMonth);
            
            // ★ REMOVED premature HideBlockingMessage. 
            // LoadMonthData is async; hiding it here would hide the "Loading..." spinner immediately!
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameController] Critical Error in LoadWithCurrentSettings: {e.Message}\n{e.StackTrace}");
            if (mainUIManager != null) mainUIManager.ShowBlockingMessage($"ロード処理でエラー発生:\n{e.Message}", true);
        }
    }

    public void ClearData()
    {
        Debug.Log("[GameController] ClearData called. Resetting to neutral state.");

        currentArtData = new ArtData();
        currentArtData.emotionHistory = new List<EmotionPoint>();
        
        // 2. Clear Visuals
        if (vfxRippleManager != null)
        {
            vfxRippleManager.ClearRipples();
        }
        
        // 3. Clear UI Details and Mode
        if (mainUIManager != null)
        {
            // Explicitly exit archive mode to ensure startup UI is clean
            mainUIManager.SetArchiveMode(false);
            
            // Note: Details panel updates automatically on Focus, so clearing ripples handles it.
        }
    }

    private void HandleSyncStart()
    {
        if (mainUIManager != null) mainUIManager.ShowBlockingMessage("Syncing...", false);
    }

    private void HandleSyncEnd()
    {
        if (mainUIManager != null) mainUIManager.HideBlockingMessage();
    }

    void Update()
    {
        HandleInput();
        
        // ★ Continuous Tracking ★
        // フォーカス中の波紋があれば、その現在位置（動いている）を毎フレーム計算してカメラに伝える
        if (activeFocusedRipple.HasValue && vfxRippleManager != null && cameraController != null)
        {
            Vector3 currentPos = vfxRippleManager.CalculateCurrentRippleCenter(activeFocusedRipple.Value, Time.time);
            cameraController.FocusOnPoint(currentPos);
        }
    }

    // ... (LoadMonthData, etc.) ...

    private void HandleInput()
    {
        // UI操作中は除外（マウスとタッチの両方に対応）
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            // マウス入力の場合
            if (Input.GetMouseButtonDown(0) && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            
            // タッチ入力の場合
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                {
                    return;
                }
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            // メインカメラ判定
            if (Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            
            // 数学的球体判定 (半径6.28)
            Vector3 hitPoint = Vector3.zero;
            bool hitSphere = false;
            float radius = 6.28f;
           
            // 簡易ヒット判定 (Colliderがない場合を想定)
            Vector3 o = ray.origin;
            Vector3 d = ray.direction;
            
            // カメラが球の外(=原点からの距離 > radius)にある前提
            float a = Vector3.Dot(d, d);
            float b = 2 * Vector3.Dot(o, d);
            float c = Vector3.Dot(o, o) - (radius * radius);
            float discriminat = b * b - 4 * a * c;
            
            if (discriminat >= 0)
            {
                float t = (-b - Mathf.Sqrt(discriminat)) / (2 * a);
                // tは距離。0以上なら前方。
                if (t >= 0)
                {
                    hitPoint = o + d * t;
                    hitSphere = true;
                }
            }

            if (hitSphere)
            {
                // 波紋マネージャに問い合わせ
                Vector3 rippleCenter;
                RippleData? clickedRipple = vfxRippleManager.GetClosestRipple(hitPoint, rippleClickThreshold, out rippleCenter);

                if (clickedRipple.HasValue)
                {
                    // 同じ波紋を再度クリックした場合 -> フォーカス解除 (トグル動作)
                    // (activeFocusedRippleと比較。nullかどうかと、TimestampなどのユニークIDで比較)
                    if (activeFocusedRipple.HasValue && clickedRipple.Value.unixTimestamp == activeFocusedRipple.Value.unixTimestamp)
                    {
                        ClearFocus();
                    }
                    else
                    {
                        // 新しい波紋ヒット！ -> フォーカス移動 & 詳細表示 & 追尾開始
                        activeFocusedRipple = clickedRipple; 
                        RippleData data = clickedRipple.Value;
                        Debug.Log($"Ripple Clicked! Timestamp: {data.unixTimestamp}");
                        
                        if (cameraController != null)
                            cameraController.FocusOnPoint(rippleCenter);
                            
                        if (mainUIManager != null)
                        {
                            mainUIManager.ShowRippleInfo(data);
                            mainUIManager.SetFocusMode(true); // UIを隠す
                        }
                        
                        // Post-Processing: フォーカスモードのビネット強調
                        if (postProcessingController != null)
                        {
                            postProcessingController.SetFocusMode(true);
                            postProcessingController.UpdateEffectsFromEmotion(data.valence, data.arousal);
                        }
                        
                        // ★変更: 時間範囲内の全EmotionPointを検索して録音リストを構築
                        if (mainUIManager != null && currentArtData != null && currentMonthKey != null)
                        {
                            // startTimestamp〜endTimestamp範囲内の録音付きEmotionPointを取得
                            var recordingsInRange = new List<(long timestamp, string audioFileName)>();
                            
                            Debug.Log($"[GameController] Searching recordings in range: {data.startTimestamp} - {data.endTimestamp}, MonthKey: {currentMonthKey}");
                            Debug.Log($"[GameController] Total EmotionPoints: {currentArtData.emotionHistory?.Count ?? 0}");
                            
                            foreach (var ep in currentArtData.emotionHistory)
                            {
                                bool inRange = ep.timestamp >= data.startTimestamp && ep.timestamp <= data.endTimestamp;
                                Debug.Log($"[GameController] EP timestamp: {ep.timestamp}, audioFileName: '{ep.audioFileName}', inRange: {inRange}");
                                
                                if (inRange)
                                {
                                    if (!string.IsNullOrEmpty(ep.audioFileName))
                                    {
                                        // ファイル存在チェック
                                        bool exists = AudioStorageManager.Instance != null && 
                                            AudioStorageManager.Instance.RecordingExists(currentMonthKey, ep.audioFileName);
                                        Debug.Log($"[GameController] File exists check: {exists}");
                                        
                                        if (exists)
                                        {
                                            recordingsInRange.Add((ep.timestamp, ep.audioFileName));
                                        }
                                    }
                                }
                            }
                            
                            Debug.Log($"[GameController] Found {recordingsInRange.Count} recordings in range");
                            
                            // 再生可否を判定
                            bool canPlay = AudioStorageManager.Instance != null 
                                && AudioStorageManager.Instance.CanPlayRecording(currentMonthKey);
                            
                            // 期限切れかどうか
                            bool isExpired = recordingsInRange.Count > 0 && !canPlay;
                            
                            // UIに録音リストを渡す
                            mainUIManager.SetPlaybackList(currentMonthKey, recordingsInRange, canPlay, isExpired);
                        }
                    }
                }
                else
                {
                    // 球体には触れたが、波紋ではない -> フォーカス解除
                    ClearFocus();
                }
            }
            else
            {
                // 背景クリック -> フォーカス解除
                ClearFocus();
            }
        }
    }

    private void ClearFocus()
    {
        activeFocusedRipple = null; // 追尾終了
        if (cameraController != null) cameraController.ClearFocus();
        if (mainUIManager != null) 
        {
            mainUIManager.HideRippleInfo();
            mainUIManager.SetFocusMode(false); // UIを表示に戻す
        }
        
        // Post-Processing: フォーカス解除
        if (postProcessingController != null)
        {
            postProcessingController.SetFocusMode(false);
            postProcessingController.ResetToIdle();
        }
    }

    /// <summary>
    /// 指定された月のデータをロードし、VFXとUIを更新します。
    /// 外部（GalleryPanel）から呼び出せるようにpublicに変更しました。
    /// </summary>
    /// <summary>
    /// 指定された月のデータをロードし、VFXとUIを更新します。
    /// 外部（GalleryPanel）から呼び出せるようにpublicに変更しました。
    /// </summary>
    public void LoadMonthData(string monthKey)
    {
        currentMonthKey = monthKey;
        Debug.Log($"Loading data for month: {monthKey}");

        // ★ Update Graph
        if (graphController != null)
        {
            graphController.GenerateGrid(monthKey);
        }
        
        // アイドル回転を再開（フォーカス中なら解除）
        ClearFocus();

        string thisMonthKey = GetCurrentMonthKey();
        bool isReviewMode = (monthKey != thisMonthKey);
        
        Debug.Log($"[GameController] LoadMonthData: Target='{monthKey}', Current='{thisMonthKey}', IsReviewMode={isReviewMode}");
        
        if (monthKey != thisMonthKey)
        {
            mainUIManager.SetArchiveMode(true, $"Viewing Archive: {monthKey}");
        }
        else
        {
            mainUIManager.SetArchiveMode(false);
            
            // ★ Reset the load target so subsequent scene loads default to "Current"
            if (GameDataManager.Instance != null)
            {
                GameDataManager.Instance.MonthToLoad = null;
            }
        }

        // Show loading while fetching from Firestore
        mainUIManager.ShowBlockingMessage("Loading Data...", false);

        // ★ Visual Reset: Clear ripples immediately to show transition
        if (vfxRippleManager != null)
        {
            vfxRippleManager.ResetRipples();
        }

        // 1. Art Dataのロード (Async from Firestore)
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.LoadArtData(monthKey, (loadedData) => {
                // Success Callback
                currentArtData = loadedData;
                Debug.Log($"[GameController] Loaded {currentArtData.emotionHistory.Count} ripples for {monthKey}.");
                
                // VFX更新
            UpdateVFXWithCurrentData();


            // Update UI (4-Axis)
            if (mainUIManager != null) mainUIManager.UpdateEmotionDisplay(currentArtData);

            // Loading Done (Hidden here since ArtData was the last thing)
            mainUIManager.HideBlockingMessage();

            }, (error) => {
                // Error Callback
                Debug.LogError($"[GameController] Failed to load ArtData: {error}");
                
                // ★ User Feedback for debugging on device
                if (mainUIManager != null) 
                    mainUIManager.ShowBlockingMessage($"データ読込エラー:\n{error}", true);

                // Fallback to empty
                currentArtData = new ArtData();
                vfxRippleManager.ResetRipples();
                
                mainUIManager.HideBlockingMessage();
            });
        }
        else
        {
             // FirestoreManagerがない場合のフォールバック（通常ありえないが）
             Debug.LogError("[GameController] FirestoreManager missing!");
             mainUIManager.HideBlockingMessage();
        }
    }

    private void UpdateVFXWithCurrentData()
    {
        if (vfxRippleManager == null) return;
        
        vfxRippleManager.ResetRipples();
        if (currentArtData.emotionHistory != null)
        {
            bool dataModified = false;
            float sumValence = 0f, sumArousal = 0f;
            
            for (int i = 0; i < currentArtData.emotionHistory.Count; i++)
            {
                var emotion = currentArtData.emotionHistory[i];
                if (emotion.position.sqrMagnitude < 0.01f)
                {
                    emotion.position = UnityEngine.Random.onUnitSphere * 6.28f;
                    currentArtData.emotionHistory[i] = emotion;
                    dataModified = true;
                }
                vfxRippleManager.AddNewRipple(emotion.valence, emotion.arousal, emotion.thought, emotion.confidence, emotion.timestamp, emotion.position);
                
                sumValence += emotion.valence;
                sumArousal += emotion.arousal;
            }
            
            if (dataModified && FirestoreManager.Instance != null)
            {
                // 位置修正などあれば保存し直す
                FirestoreManager.Instance.SaveArtData(currentMonthKey, currentArtData);
            }
            
            // Post-Processing: 月間平均感情に基づいてエフェクトを調整
            if (postProcessingController != null && currentArtData.emotionHistory.Count > 0)
            {
                float avgValence = sumValence / currentArtData.emotionHistory.Count;
                float avgArousal = sumArousal / currentArtData.emotionHistory.Count;
                postProcessingController.UpdateEffectsFromEmotion(avgValence, avgArousal);
            }
            else if (postProcessingController != null)
            {
                // データがない場合はアイドル状態に
                postProcessingController.ResetToIdle();
            }
            
            // Cosmic Background: 感情データを渡す
            if (cosmicBackgroundController != null)
            {
                cosmicBackgroundController.SetAverageEmotion(currentArtData.emotionHistory);
            }
        }
    }

    /// <summary>
    /// APIからの解析結果を受け取り、新しい感情ポイントを追加します。
    /// もし過去の月を閲覧中だった場合、自動的に現在の月に切り替えてから保存します。
    /// </summary>
    public void SetParametersFromJson(string jsonData)
    {
        // Guard: Prevent saving data if no user is logged in (risk of overwriting empty data)
        if (FirebaseConfig.Instance == null || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            Debug.LogWarning("[GameController] Received data but no user logged in. Ignoring to prevent data loss.");
            return;
        }

        try
        {
            // 現在の月を取得
            string thisMonthKey = DateTime.Now.ToString("yyyy-MM");

            // もし閲覧中の月が「今月」でなければ、強制的に今月に切り替える
            if (currentMonthKey != thisMonthKey)
            {
                Debug.LogWarning("Viewing past data. Switching to current month for new recording...");
                LoadMonthData(thisMonthKey);
                
                // GameDataManagerの状態もリセットしておく (次にシーンロードした時のため)
                if (GameDataManager.Instance != null)
                {
                    GameDataManager.Instance.MonthToLoad = null; 
                }
            }

            SentimentAnalysisResponse response = JsonUtility.FromJson<SentimentAnalysisResponse>(jsonData);
            if (response?.sentiment_analysis?.segments == null || response.sentiment_analysis.segments.Count == 0)
            {
                // 感情分析データが空の場合、ユーザーに理由を表示
                string errorReason = BuildNoSentimentMessage(response);
                Debug.LogWarning($"No valid sentiment segments in API response. {errorReason}");
                
                if (mainUIManager != null)
                {
                    mainUIManager.ShowBlockingMessage(errorReason, true);
                }
                return;
            }

            Debug.Log($"[GameController] Valid sentiment segments found: {response.sentiment_analysis.segments.Count}");

            // 感情ポイントの計算と保存
            EmotionPoint newEmotion = CalculateAndSaveEmotionPoint(response);
            
            // VFXに追加 (新規作成時もタイムスタンプと位置を渡す)
            vfxRippleManager.AddNewRipple(newEmotion.valence, newEmotion.arousal, newEmotion.thought, newEmotion.confidence, newEmotion.timestamp, newEmotion.position);
            
            // 累計データ更新処理は削除されました (UpdateTotalSentimentUI)

            // --- Quota Consumption (実音声時間) ---
            if (SubscriptionManager.Instance != null)
            {
                long totalMilliseconds = 0;
                foreach(var seg in response.sentiment_analysis.segments)
                {
                    totalMilliseconds += (seg.endtime - seg.starttime);
                }
                // starttime/endtime はミリ秒単位と想定 (AmiVoice仕様)
                float totalSeconds = totalMilliseconds / 1000.0f;
                if (totalSeconds < 0) totalSeconds = 0;
                
                SubscriptionManager.Instance.ConsumeQuota(totalSeconds);
            }

            // ★ Streak / Activity Log
            if (StreakManager.Instance != null)
            {
                StreakManager.Instance.LogActivity();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing JSON response: {e.Message}");
        }
    }

    /// <summary>
    /// 感情分析データが空の場合に、ユーザーに表示するエラーメッセージを構築します。
    /// </summary>
    private string BuildNoSentimentMessage(SentimentAnalysisResponse response)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("感情分析データを取得できませんでした");
        sb.AppendLine("");
        
        // APIからのエラーコード/メッセージがあれば表示
        if (!string.IsNullOrEmpty(response?.code))
        {
            sb.AppendLine($"エラーコード: {response.code}");
        }
        if (!string.IsNullOrEmpty(response?.message))
        {
            sb.AppendLine($"メッセージ: {response.message}");
        }
        
        // 音声認識テキストの情報を追加
        bool hasRecognizedText = !string.IsNullOrEmpty(response?.text);
        int segmentCount = response?.segments?.Count ?? 0;
        
        if (hasRecognizedText && segmentCount > 0)
        {
            // 音声認識は成功している
            sb.AppendLine("");
            sb.AppendLine("音声認識テキスト:");
            
            // テキストを短縮表示（長すぎる場合）
            string displayText = response.text;
            if (displayText.Length > 50)
            {
                displayText = displayText.Substring(0, 47) + "...";
            }
            sb.AppendLine($"「{displayText}」");
            sb.AppendLine("");
            sb.AppendLine("考えられる原因:");
            sb.AppendLine("・音声が小さすぎる");
            sb.AppendLine("・背景ノイズが多い");
            sb.AppendLine("・発話時間が短すぎる");
            sb.AppendLine("・API契約の確認が必要");
        }
        else
        {
            // 音声認識自体も失敗
            sb.AppendLine("");
            sb.AppendLine("音声認識できませんでした");
            sb.AppendLine("");
            sb.AppendLine("考えられる原因:");
            sb.AppendLine("・音声が入力されていない");
            sb.AppendLine("・マイクの音量が小さすぎる");
        }
        
        return sb.ToString().Trim();
    }

    private EmotionPoint CalculateAndSaveEmotionPoint(SentimentAnalysisResponse response)
    {
        float totalValence = 0, totalArousal = 0, totalThought = 0, totalConfidenceDiff = 0;
        
        // 非ゼロ平均用のカウンタ
        int countValence = 0;
        int countArousal = 0;
        int countThought = 0;
        int countConfidence = 0;

        // 係数定義
        float scale30 = 3.3f; 
        float scaleNeg = 1.5f; // 自信のマイナス要素用（係数を下げる）

        foreach (var seg in response.sentiment_analysis.segments)
        {
            // === 1. Valence ===
            float pos = (seg.content * scale30) 
                      + (seg.atmosphere) 
                      + (seg.excitement * scale30)
                      + (seg.passionate * scale30)
                      + (seg.anticipation * 0.5f);

            float neg = (seg.stress * 1.0f) 
                      + (seg.dissatisfaction * scale30) 
                      + (seg.aggression * scale30) 
                      + (seg.upset * scale30) 
                      + (seg.embarrassment * scale30);

            float vVal = pos - neg;
            totalValence += vVal;
            // 値が有意にある場合のみカウント
            if (Mathf.Abs(vVal) > 0.1f) countValence++;
            
            // === 2. Arousal ===
            float arousalSum = (seg.energy * 1.0f) 
                             + (seg.extreme_emotion * scale30) 
                             + (seg.excitement * scale30) 
                             + (seg.aggression * scale30);
            totalArousal += arousalSum;
            if (arousalSum > 0.1f) countArousal++;
            
            // === 3. Thought ===
            float thoughtSum = (seg.intensive_thinking * 1.0f) 
                             + (seg.concentration * 1.0f) 
                             + (seg.brain_power * 1.0f)
                             + (seg.imagination_activity * scale30);
            totalThought += thoughtSum;
            if (thoughtSum > 0.1f) countThought++;
            
            // === 4. Confidence ===
            // ユーザー要望: マイナス要素の係数を下げ、結果が0付近(均衡)なら「中間の速度」にしたい
            // -> ここでは「Pos - Neg」の差分だけを集計し、最後に0.5を足す方式にする
            
            float confPos = (seg.confidence * scale30);
            float confNeg = (seg.hesitation * scaleNeg) + (seg.uncertainty * scaleNeg);
            
            float cDiff = confPos - confNeg;
            totalConfidenceDiff += cDiff;
            // どちらかの要素があればカウント
            if (confPos > 0.1f || confNeg > 0.1f) countConfidence++;
        }
        
        // カウントが0の場合は1にしてゼロ除算防止
        if (countValence == 0) countValence = 1;
        if (countArousal == 0) countArousal = 1;
        if (countThought == 0) countThought = 1;
        if (countConfidence == 0) countConfidence = 1;

        // 正規化係数 (Divisor) - 「1セグメントあたりのMAX」を基準にする
        float normValence = 250f; 
        float normArousal = 120f; 
        float normThought = 150f;
        float normConfidence = 100f; // 差分なのでこのくらいで

        EmotionPoint newEmotion = new EmotionPoint
        {
            // 非ゼロ平均: 合計 / 有効セグメント数 / 正規化係数
            valence = Mathf.Clamp(totalValence / countValence / normValence, -1f, 1f),
            
            arousal = Mathf.Clamp01(totalArousal / countArousal / normArousal),
            
            thought = Mathf.Clamp01(totalThought / countThought / normThought),
            
            // Confidence: 基準値0.5 + (平均差分 / 正規化)
            // これにより、差分0なら0.5(中間)、プラスなら増え、マイナスなら減る
            // ★ユーザー要望: 最低値でも0にはせず、少し動くようにする (Min 0.1)
            confidence = Mathf.Clamp(0.5f + (totalConfidenceDiff / countConfidence / normConfidence), 0.1f, 1.0f), 
            
            timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            position = UnityEngine.Random.onUnitSphere * 6.28f,
            
            audioFileName = pendingAudioFileName, // ★追加: 録音ファイル名を設定
            
            rawSegments = new List<SentimentSegment>(response.sentiment_analysis.segments)
        };
        
        // ★追加: ファイル名使用済みなのでクリア
        pendingAudioFileName = null;
        
        // リストに追加
        currentArtData.emotionHistory.Add(newEmotion);
        
        // ★★★ Cloud Sync Only (No Local File) ★★★
        if (FirestoreManager.Instance != null && currentMonthKey != null)
        {
             FirestoreManager.Instance.SaveArtData(currentMonthKey, currentArtData);
        }

        Debug.Log($"Saved new emotion point (Cloud). Total: {currentArtData.emotionHistory.Count}");
        return newEmotion;
    }
}



