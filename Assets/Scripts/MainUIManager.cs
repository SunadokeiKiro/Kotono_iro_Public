// Scripts/MainUIManager.cs
using UnityEngine;
using UnityEngine.UI; // Layout Elementを使うために必要
using TMPro;
using System.Collections;
using System.Collections.Generic; // Listを使うために必要

/// <summary>
/// メインシーンのUIイベントと表示更新をすべて管理します。
/// ユーザーからの入力を受け付け、各専門コントローラーに処理を依頼します。
/// </summary>
public class MainUIManager : MonoBehaviour
{
    [Header("Controller References")]
    [SerializeField] private GameController gameController;
    [SerializeField] private MicrophoneController microphoneController;

    [Header("UI Elements")]
    [SerializeField] private Button recButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button galleryButton; 
    [SerializeField] private Button closeArchiveButton; // 追加: アーカイブモード終了ボタン
    [SerializeField] private Button helpButton; // 追加: チュートリアル開始ボタン
    [SerializeField] private Button autoRecordOnButton;
    [SerializeField] private Button autoRecordOffButton;
    [SerializeField] private TextMeshProUGUI autoRecordStatusText;
    
    [Header("Panels")]
    [SerializeField] private GalleryPanelController galleryPanel; 
    [SerializeField] private CanvasGroup panelDetails; // 感情詳細パネル
    [SerializeField] private Button toggleDetailsButton; // Restored


    [Header("Status Indicators")]
    [SerializeField] private GameObject blockingPanel; // Opaque/Blocking (Warning, Init) - Old "loadingPanel"
    [SerializeField] private Button blockingCloseButton; // Close button for warnings
    [SerializeField] private GameObject recordingPanel; // Transparent (Recording & Processing)
    
    [SerializeField] private TextMeshProUGUI blockingText;
    [SerializeField] private TextMeshProUGUI recordingText;

    [SerializeField] private TextMeshProUGUI modeIndicatorText; // 追加: 過去ログ閲覧等のモード表示用
    [SerializeField] private TextMeshProUGUI userIdText; // ★ New: Global User ID Display

    [Header("4-Axis Emotion UI")]
    [SerializeField] private TextMeshProUGUI valenceText;
    [SerializeField] private TextMeshProUGUI arousalText;
    [SerializeField] private TextMeshProUGUI thoughtText;
    [SerializeField] private TextMeshProUGUI confidenceText;
    [SerializeField] private TextMeshProUGUI displayModeText; // "Average" or "Latest"
    [SerializeField] private float displayToggleInterval = 5f;

    private ArtData currentArtDataForDisplay;
    private bool isShowingAverage = true;

    // Old 20-sentiment fields removed.

    private bool isArchiveMode = false;

    void Awake()
    {
        Debug.Log($"[MainUIManager] Awake() called. GameObject={gameObject.name}, enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}");
        
        // ★修正: 重複インスタンスチェック
        // DontDestroyOnLoadされたマネージャーが古いMainUIManagerへの参照を持つことで
        // 新しいインスタンスが正しく初期化されない問題を回避
        var existingInstances = FindObjectsByType<MainUIManager>(FindObjectsSortMode.None);
        Debug.Log($"[MainUIManager] Found {existingInstances.Length} MainUIManager instances");
        
        foreach (var instance in existingInstances)
        {
            if (instance != this && instance.gameObject.scene.name != gameObject.scene.name)
            {
                // 別シーンからの古いインスタンスがあれば破棄
                Debug.Log($"[MainUIManager] Destroying old instance from scene: {instance.gameObject.scene.name}");
                Destroy(instance.gameObject);
            }
        }
    }

    void OnEnable()
    {
        Debug.Log($"[MainUIManager] OnEnable() called. enabled={enabled}");
    }

    void OnDisable()
    {
        Debug.Log($"[MainUIManager] OnDisable() called. reason: enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}");
    }

    void Start()
    {
        Debug.Log($"[MainUIManager] Start() called. gameController={(gameController != null ? "OK" : "NULL")}, microphoneController={(microphoneController != null ? "OK" : "NULL")}");
        
        if (gameController == null || microphoneController == null)
        {
            Debug.LogError("MainUIManager: Controller references are not set in the inspector.");
            enabled = false;
            return;
        }

        InitializeButtons();

        // ★ Event Subscription
        if (microphoneController != null)
        {
            microphoneController.OnRecordingStateChanged += UpdateRecButtonState;
            microphoneController.OnBusyStateChanged += UpdateBusyState;
        }

        // ★ Firebase Auth Subscription
        Firebase.Auth.FirebaseAuth.DefaultInstance.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null); 

        if (blockingPanel != null) blockingPanel.SetActive(false);
        if (recordingPanel != null) 
        {
            recordingPanel.SetActive(false);
            // ★ 起動時にテキストをクリア（"New Text"問題の修正）
            if (recordingText != null) recordingText.text = "";
        }

        // ★ 起動時にuserIdTextを初期化（表示遅延問題の修正）
        if (userIdText != null)
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user != null)
            {
                string fullId = user.UserId;
                userIdText.text = $"ID: {(fullId.Length > 6 ? fullId.Substring(0, 6) + "..." : fullId)}";
            }
            else
            {
                userIdText.text = "ID: ...";
            }
        }

        // ★ Session Expiry Event Subscription
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnSessionExpired += OnSessionExpiredHandler;
        }

        // Ensure UI starts in a clean state
        SetArchiveMode(false);

        ApplyStyles();
        
        if (microphoneController != null)
        {
            UpdateAutoRecordButtons(microphoneController.IsAutoRecordEnabled);
            UpdateAutoRecordStatusText(microphoneController.IsAutoRecordEnabled);
        }
        
        StartCoroutine(ToggleEmotionDisplayCoroutine());
        StartCoroutine(ForceRebuildUI());
        StartCoroutine(CheckApiReadyLoop()); // ★ API Ready Check
        
        // ★追加: 再生イベント購読
        if (audioPlaybackController != null)
        {
            audioPlaybackController.OnPlaybackStarted += OnPlaybackStartedHandler;
            audioPlaybackController.OnPlaybackFinished += OnPlaybackFinishedHandler;
        }
    }

    private IEnumerator CheckApiReadyLoop()
    {
        // Initially disable
        if(recButton) recButton.interactable = false;
        if(autoRecordOnButton) autoRecordOnButton.interactable = false;

        // Poll for readiness
        while (microphoneController != null && !microphoneController.IsApiReady)
        {
            yield return new WaitForSeconds(0.5f);
        }

        // Enable
        if(recButton) recButton.interactable = true;
        if(autoRecordOnButton) autoRecordOnButton.interactable = true;
    }

    // ★ Auth State Handler
    private void AuthStateChanged(object sender, System.EventArgs eventArgs)
    {
        if (userIdText == null) return;

        MainThreadDispatcher.Enqueue(() =>
        {
            if (userIdText == null) return; // Safety check on main thread
            
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user != null)
            {
                string fullId = user.UserId;
                userIdText.text = $"ID: {(fullId.Length > 6 ? fullId.Substring(0, 6) + "..." : fullId)}";
                
                // ★ セッション登録（デバイス制限機能）
                if (SessionManager.Instance != null)
                {
                    StartCoroutine(SessionManager.Instance.RegisterSession((success) => {
                        if (success) Debug.Log("[MainUIManager] Device session registered successfully.");
                        else Debug.LogWarning("[MainUIManager] Failed to register device session.");
                    }));
                }
            }
            else
            {
                userIdText.text = "ID: ..."; // Or "Guest" / "Loading..."
            }
        });
    }

    private IEnumerator ForceRebuildUI()
    {
        // 起動直後のセーフエリア適用やレイアウト計算待ち
        yield return new WaitForSeconds(0.5f);

        // ルートCanvasを探す
        Canvas rootCanvas = null;
        if (recButton != null) rootCanvas = recButton.GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = GetComponent<Canvas>(); // 自分についている場合
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>(); // 親にある場合

        if (rootCanvas != null)
        {
            // Canvasを一度無効化→有効化
            rootCanvas.enabled = false;
            yield return null; 
            rootCanvas.enabled = true;
            
            // ★Jiggle Fix: サイズをわずかに変更して強制的にダーティフラグを立てる
            // Unityシミュレーター等で発生する「変更イベントが来ないと更新されない」バグへの対策
            var rect = rootCanvas.GetComponent<RectTransform>();
            if (rect != null)
            {
                Vector2 originalSize = rect.sizeDelta;
                rect.sizeDelta = new Vector2(originalSize.x + 1, originalSize.y); // +1px
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                
                rect.sizeDelta = originalSize; // 元に戻す
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }
        
        // ★Fix: Ensure Status Text Color is applied AFTER all initialization (StyledText overwrites)
        if (microphoneController != null)
        {
            UpdateAutoRecordButtons(microphoneController.IsAutoRecordEnabled);
            UpdateAutoRecordStatusText(microphoneController.IsAutoRecordEnabled);
        }
    }

    private void ApplyStyles()
    {
        // Global styles
        UIStyler.ApplyStyleToButton(recButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(settingsButton, isIconOnly: true); // Icon
        UIStyler.ApplyStyleToButton(galleryButton, isIconOnly: true); // Icon
        UIStyler.ApplyStyleToButton(closeArchiveButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(autoRecordOnButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(autoRecordOffButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(autoRecordOffButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(autoRecordOffButton, isIconOnly: false);
        UIStyler.ApplyStyleToButton(toggleDetailsButton, isIconOnly: true); // Icon or small button
        UIStyler.ApplyStyleToButton(helpButton, isIconOnly: true); // Help Icon

        if (autoRecordStatusText != null) UIStyler.ApplyStyleToTMP(autoRecordStatusText); // Base style

        // Apply styles to New 4-Axis UI
        UIStyler.ApplyStyleToTMP(valenceText);
        UIStyler.ApplyStyleToTMP(arousalText);
        UIStyler.ApplyStyleToTMP(thoughtText);
        UIStyler.ApplyStyleToTMP(confidenceText);
        UIStyler.ApplyStyleToTMP(displayModeText);

        // Apply styles to Status Indicators
        UIStyler.ApplyStyleToTMP(blockingText);
        UIStyler.ApplyStyleToTMP(recordingText);
        UIStyler.ApplyStyleToTMP(modeIndicatorText);
    }

    private void InitializeButtons()
    {
        if(recButton) recButton.onClick.AddListener(OnRecButtonClick);
        if(autoRecordOnButton) autoRecordOnButton.onClick.AddListener(OnAutoRecordOnButtonClick);
        if(autoRecordOffButton) autoRecordOffButton.onClick.AddListener(OnAutoRecordOffButtonClick);
        
        if(settingsButton) 
            settingsButton.onClick.AddListener(() => {
                if(microphoneController != null && microphoneController.IsRecordingOrAnalyzing) return;
                SceneTransitionManager.Instance.LoadScene("SettingsScene");
            });

        if(helpButton)
            helpButton.onClick.AddListener(() => {
                if (TutorialManager.Instance != null) TutorialManager.Instance.StartMainTutorial(this);
            });
        
        // Gallery Panel Control
        if(galleryButton && galleryPanel) 
            galleryButton.onClick.AddListener(() => {
                if(microphoneController != null && microphoneController.IsRecordingOrAnalyzing) return;
                galleryPanel.OpenPanel();
            });
            
        // Close Archive Control
        if(closeArchiveButton && gameController)
            closeArchiveButton.onClick.AddListener(() => {
                // 1. Exit Archive Mode (Reset UI)
                SetArchiveMode(false);
                if (modeIndicatorText) modeIndicatorText.text = ""; // Clear "Viewing Past Log"

                // 2. Load Current Month Data
                string currentKey = TimeManager.Instance != null 
                    ? TimeManager.Instance.GetCurrentJstTime().ToString("yyyy-MM") 
                    : System.DateTime.Now.ToString("yyyy-MM");
                gameController.LoadMonthData(currentKey);
            });

        // Toggle Details (Currently does nothing or could toggle the panel containing the new texts)
        if (toggleDetailsButton)
            toggleDetailsButton.onClick.AddListener(OnToggleDetailsClick);

        // Blocking Close Button
        if (blockingCloseButton)
            blockingCloseButton.onClick.AddListener(HideBlockingMessage);
        
        // ★追加: 録音再生ボタン
        if (playbackButton)
            playbackButton.onClick.AddListener(OnPlaybackButtonClick);
    }
    
    private void OnToggleDetailsClick()
    {
       // If keeping panelDetails for background or container of new text
       if (panelDetails != null)
       {
           bool isShowing = (panelDetails.alpha > 0.5f);
           StartCoroutine(FadeCanvasGroup(panelDetails, isShowing ? 1f : 0f, isShowing ? 0f : 1f, 0.3f));
           panelDetails.blocksRaycasts = !isShowing;
       }
    }
    
    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float startAlpha, float endAlpha, float duration)
    {
        if (cg == null) yield break;
        float currentTime = 0f;
        cg.alpha = startAlpha;
        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            cg.alpha = Mathf.Lerp(startAlpha, endAlpha, currentTime / duration);
            yield return null;
        }
        cg.alpha = endAlpha;
    }

    #region UIイベントハンドラ
    private void OnRecButtonClick() 
    { 
        if (microphoneController.IsRecording)
        {
            microphoneController.StopManualRecording();
        }
        else
        {
            StartCoroutine(microphoneController.StartManualRecording()); 
        }
    }
    private void OnAutoRecordOnButtonClick()
    {
        // ★ プラン分岐: FreeはFreeTrialボタン、Premium以上は自動録音
        if (SubscriptionManager.Instance != null)
        {
            var plan = SubscriptionManager.Instance.CurrentPlan;
            
            if (plan == PlanType.Free)
            {
                // Freeユーザー: 無料お試し録音を開始（手動録音と同じ）
                Debug.Log("[MainUIManager] Free user: Starting free trial recording.");
                StartCoroutine(microphoneController.StartManualRecording());
                return;
            }
            else if (plan == PlanType.Standard)
            {
                // Standardユーザー: 自動録音は利用不可
                if (autoRecordStatusText != null)
                {
                    autoRecordStatusText.text = "自動録音: Premium以上必要";
                    autoRecordStatusText.color = Color.yellow;
                }
                Debug.Log("[MainUIManager] Standard user: AutoRecord requires Premium or higher.");
                return;
            }
            // Premium/Ultimate: 自動録音を開始
        }
        
        microphoneController.StartAutoRecording();
        UpdateAutoRecordButtons(true);
        UpdateAutoRecordStatusText(true);
    }
    private void OnAutoRecordOffButtonClick()
    {
        microphoneController.StopAutoRecording();
        UpdateAutoRecordButtons(false);
        UpdateAutoRecordStatusText(false);
    }
    #endregion

    #region Status Indicator
    public void ShowBlockingMessage(string message, bool showCloseButton = false) // Renamed from ShowLoading
    {
        if (blockingPanel != null) 
        { 
            if (blockingText != null) blockingText.text = message; 
            if (blockingCloseButton != null) blockingCloseButton.gameObject.SetActive(showCloseButton);
            blockingPanel.SetActive(true); 
        }
    }
    public void HideBlockingMessage() { if (blockingPanel != null) blockingPanel.SetActive(false); }

    public void ShowProcessingMessage(string message) // Now uses RecordingPanel (Overlay)
    {
        if (recordingPanel != null) { if (recordingText != null) recordingText.text = message; recordingPanel.SetActive(true); }
    }
    public void HideProcessingMessage() { if (recordingPanel != null) recordingPanel.SetActive(false); }

    public void ShowRecordingPanel(string message)
    {
         if (recordingPanel != null)
         {
             if (recordingText != null) recordingText.text = message;
             recordingPanel.SetActive(true);
         }
    }
    public void HideRecordingPanel()
    {
         if (recordingPanel != null) recordingPanel.SetActive(false);
    }
    #endregion

    #region UI表示更新
    public void SetArchiveMode(bool enabled, string label = "")
    {
        isArchiveMode = enabled;

        // アーカイブ終了ボタンの表示切替
        if (closeArchiveButton != null) closeArchiveButton.gameObject.SetActive(enabled);

        if (enabled)
        {
            // アーカイブモードに入る際は、安全のため自動録音を停止する
            if (microphoneController != null) microphoneController.StopAutoRecording();

            // アーカイブモード: 各種ボタン非表示、テキストはアーカイブ名
            if (recButton) recButton.gameObject.SetActive(false);
            if (autoRecordOnButton) autoRecordOnButton.gameObject.SetActive(false);
            if (autoRecordOffButton) autoRecordOffButton.gameObject.SetActive(false);
            if (settingsButton) settingsButton.gameObject.SetActive(false);
            if (galleryButton) galleryButton.gameObject.SetActive(false);
            
            if (autoRecordStatusText)
            {
                // Force Update Style first to reset unwanted overrides
                UIStyler.ApplyStyleToTMP(autoRecordStatusText);
                autoRecordStatusText.text = label;
                autoRecordStatusText.color = Color.yellow;
            }
        }
        else
        {
            // 通常モード: ボタン復帰
            UpdateRecButtonVisibility(); // Use helper to respect Busy state
            if (settingsButton) settingsButton.gameObject.SetActive(true);
            if (galleryButton) galleryButton.gameObject.SetActive(true);
            
            // AutoRecordボタンの表示更新は別途行われるため、ここではフラグ解除のみが重要
            if (microphoneController != null)
            {
                UpdateAutoRecordButtons(microphoneController.IsAutoRecordEnabled);
                UpdateAutoRecordStatusText(microphoneController.IsAutoRecordEnabled);
            }
        }
    }

    private void UpdateAutoRecordButtons(bool isAuto)
    {
        if (isArchiveMode) return; // アーカイブ中は更新しない

        // ★ プラン別UI表示
        if (SubscriptionManager.Instance != null)
        {
            var plan = SubscriptionManager.Instance.CurrentPlan;
            
            if (plan == PlanType.Free)
            {
                // Freeユーザー: 無料お試しボタンとして表示（Offボタンは非表示）
                if (autoRecordOnButton != null) autoRecordOnButton.gameObject.SetActive(true);
                if (autoRecordOffButton != null) autoRecordOffButton.gameObject.SetActive(false);
                
                // Free Trial残回数を取得してボタンテキスト更新
                UpdateFreeTrialButtonUI();
                return;
            }
            else if (plan == PlanType.Standard)
            {
                // Standardユーザー: 自動録音ボタンは非表示
                if (autoRecordOnButton != null) autoRecordOnButton.gameObject.SetActive(false);
                if (autoRecordOffButton != null) autoRecordOffButton.gameObject.SetActive(false);
                return;
            }
        }
        
        // Premium/Ultimate: 通常の自動録音ボタン表示
        if (autoRecordOnButton != null) autoRecordOnButton.gameObject.SetActive(!isAuto);
        if (autoRecordOffButton != null) autoRecordOffButton.gameObject.SetActive(isAuto);
    }

    /// <summary>
    /// Freeユーザー向けの無料お試しボタンUIを更新
    /// </summary>
    private void UpdateFreeTrialButtonUI()
    {
        if (FirestoreManager.Instance == null) return;
        
        FirestoreManager.Instance.GetFreeTrialCount(
            (count) => {
                int remaining = 3 - count;
                var btnText = autoRecordOnButton?.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                
                if (remaining > 0)
                {
                    // 残回数あり: ボタン有効、テキスト更新
                    if (autoRecordOnButton != null) autoRecordOnButton.interactable = true;
                    if (btnText != null) btnText.text = $"無料録音\n残り{remaining}回";
                    if (autoRecordStatusText != null)
                    {
                        autoRecordStatusText.text = $"無料お試し: 残り{remaining}回（各10秒）";
                        autoRecordStatusText.color = Color.cyan;
                    }
                }
                else
                {
                    // 使い切り: ボタン無効化、プラン変更を促す
                    if (autoRecordOnButton != null) autoRecordOnButton.interactable = false;
                    if (btnText != null) btnText.text = "無料枠終了";
                    if (autoRecordStatusText != null)
                    {
                        autoRecordStatusText.text = "有料プランにアップグレード";
                        autoRecordStatusText.color = Color.yellow;
                    }
                }
            },
            (error) => {
                Debug.LogWarning($"[MainUIManager] Failed to get free trial count: {error}");
                // エラー時はデフォルト表示
                if (autoRecordStatusText != null)
                {
                    autoRecordStatusText.text = "無料お試し（最大3回）";
                    autoRecordStatusText.color = Color.cyan;
                }
            }
        );
    }

    private void UpdateAutoRecordStatusText(bool isAuto)
    {
        if (isArchiveMode) return; // アーカイブ中は更新しない

        // ★ プラン別UI表示（Freeはボタン更新時にまとめて処理済み）
        if (SubscriptionManager.Instance != null)
        {
            var plan = SubscriptionManager.Instance.CurrentPlan;
            if (plan == PlanType.Free || plan == PlanType.Standard)
            {
                return; // Freeはボタン更新時に処理済み、Standardはボタン非表示
            }
        }

        // Premium/Ultimate: 通常の自動録音ステータス表示
        if (autoRecordStatusText != null) 
        { 
            autoRecordStatusText.text = isAuto ? "自動録音: オン" : "自動録音: オフ"; 
            autoRecordStatusText.color = isAuto ? Color.green : Color.red; 
        }
    }
    #endregion
    
    // ★ Renamed/Refactored: Update Emotion Display
    public void UpdateEmotionDisplay(ArtData artData)
    {
        this.currentArtDataForDisplay = artData;
        UpdateEmotionValues(); // Update immediately upon data change
    }

    private IEnumerator ToggleEmotionDisplayCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(displayToggleInterval);
            
            // Fade Out
            yield return CrossFadeEmotionTexts(0f, 0.5f);

            isShowingAverage = !isShowingAverage;
            UpdateEmotionValues();

            // Fade In
            yield return CrossFadeEmotionTexts(1f, 0.5f);
        }
    }

    private IEnumerator CrossFadeEmotionTexts(float targetAlpha, float duration)
    {
        if(valenceText) valenceText.CrossFadeAlpha(targetAlpha, duration, false);
        if(arousalText) arousalText.CrossFadeAlpha(targetAlpha, duration, false);
        if(thoughtText) thoughtText.CrossFadeAlpha(targetAlpha, duration, false);
        if(confidenceText) confidenceText.CrossFadeAlpha(targetAlpha, duration, false);
        if(displayModeText) displayModeText.CrossFadeAlpha(targetAlpha, duration, false);
        
        yield return new WaitForSeconds(duration);
    }

    private void UpdateEmotionValues()
    {
        if (currentArtDataForDisplay == null || currentArtDataForDisplay.emotionHistory == null || currentArtDataForDisplay.emotionHistory.Count == 0)
        {
            // No Data
            SetEmotionTexts(0, 0, 0, 0, "No Data");
            return;
        }

        if (isShowingAverage)
        {
            // Calculate Average
            float sumV = 0, sumA = 0, sumT = 0, sumC = 0;
            int count = currentArtDataForDisplay.emotionHistory.Count;
            
            foreach (var point in currentArtDataForDisplay.emotionHistory)
            {
                sumV += point.valence;
                sumA += point.arousal;
                sumT += point.thought;
                sumC += point.confidence;
            }
            
            // Avoid division by zero (checked by count==0 above but safe)
            if (count > 0)
                SetEmotionTexts(sumV/count, sumA/count, sumT/count, sumC/count, "Monthly Average");
        }
        else
        {
            // Show Latest
            var latest = currentArtDataForDisplay.emotionHistory[currentArtDataForDisplay.emotionHistory.Count - 1];
            SetEmotionTexts(latest.valence, latest.arousal, latest.thought, latest.confidence, "Latest Analysis");
        }
    }

    private void SetEmotionTexts(float v, float a, float t, float c, string mode)
    {
         if(valenceText) valenceText.text = $"Valence: {v:F2}";
         if(arousalText) arousalText.text = $"Arousal: {a:F2}";
         if(thoughtText) thoughtText.text = $"Thought: {t:F2}";
         if(confidenceText) confidenceText.text = $"Confidence: {c:F2}";
         if(displayModeText) displayModeText.text = mode;
    }

    [Header("Ripple Info UI")]
    [SerializeField] private TMPro.TextMeshProUGUI rippleInfoText;
    [SerializeField] private GameObject rippleInfoPanel;
    [SerializeField] private Button playbackButton; // ★追加: 再生ボタン
    [SerializeField] private TextMeshProUGUI playbackButtonText; // ★追加: 再生ボタンテキスト
    [SerializeField] private AudioPlaybackController audioPlaybackController; // ★追加: 再生コントローラー
    
    [Header("Recording List UI")]
    [SerializeField] private GameObject recordingListPanel; // ★追加: 録音リストパネル（ScrollView等）
    [SerializeField] private Transform recordingListContent; // ★追加: リスト項目の親Transform
    [SerializeField] private GameObject recordingItemPrefab; // ★追加: リスト項目のプレハブ
    
    private string currentFocusedMonthKey; // ★追加: フォーカス中の月キー
    private string currentFocusedAudioFileName; // ★追加: フォーカス中の音声ファイル名
    private List<(long timestamp, string audioFileName)> currentRecordingList = new List<(long, string)>(); // ★追加: 録音リスト
    private int currentSelectedIndex = 0; // ★追加: 選択中のインデックス

    public void ShowRippleInfo(RippleData data)
    {
        if (rippleInfoPanel != null) rippleInfoPanel.SetActive(true);
        if (rippleInfoText != null)
        {
            // Unix Time -> DateTime conversion
            System.DateTime startDate = System.DateTimeOffset.FromUnixTimeSeconds(data.startTimestamp).ToLocalTime().DateTime;
            System.DateTime endDate = System.DateTimeOffset.FromUnixTimeSeconds(data.endTimestamp).ToLocalTime().DateTime;
            
            string timeStr;
            // 差が1分未満なら単一表示、それ以上なら範囲表示
            if ((endDate - startDate).TotalMinutes < 1.0)
            {
                timeStr = startDate.ToString("yyyy.MM.dd HH:mm");
            }
            else
            {
                // 日またぎ判定
                if (startDate.Date == endDate.Date)
                {
                    timeStr = $"{startDate.ToString("yyyy.MM.dd HH:mm")} - {endDate.ToString("HH:mm")}";
                }
                else
                {
                    timeStr = $"{startDate.ToString("yyyy.MM.dd HH:mm")} - {endDate.ToString("MM.dd HH:mm")}";
                }
            }

            string info = $"{timeStr}\n" +
                          $"<size=70%>Valence: {data.valence:F2}  Arousal: {data.arousal:F2}\n" +
                          $"Thought: {data.thought:F2}  Confidence: {data.confidence:F2}</size>"; 
            rippleInfoText.text = info;
        }
    }

    public void HideRippleInfo()
    {
        if (rippleInfoPanel != null) rippleInfoPanel.SetActive(false);
        
        // ★追加: 録音リストパネルも非表示
        if (recordingListPanel != null) recordingListPanel.SetActive(false);
        
        // ★追加: 再生停止
        if (audioPlaybackController != null)
        {
            audioPlaybackController.StopPlayback();
        }
        currentFocusedMonthKey = null;
        currentFocusedAudioFileName = null;
        currentRecordingList.Clear();
    }

    /// <summary>
    /// フォーカス中の波紋の音声再生可否を設定します。
    /// </summary>
    public void SetPlaybackAvailable(string monthKey, string audioFileName, bool canPlay, bool isExpired = false)
    {
        Debug.Log($"[MainUIManager] SetPlaybackAvailable called - monthKey: {monthKey}, audioFileName: {audioFileName}, canPlay: {canPlay}, isExpired: {isExpired}");
        Debug.Log($"[MainUIManager] playbackButton is null: {playbackButton == null}, playbackButtonText is null: {playbackButtonText == null}");
        
        currentFocusedMonthKey = monthKey;
        currentFocusedAudioFileName = audioFileName;
        
        if (playbackButton != null)
        {
            bool hasAudio = !string.IsNullOrEmpty(audioFileName);
            Debug.Log($"[MainUIManager] hasAudio: {hasAudio}, setting button active");
            playbackButton.gameObject.SetActive(hasAudio);
            
            if (hasAudio)
            {
                if (isExpired)
                {
                    // 期限切れ（アップグレード促進）
                    playbackButton.interactable = false;
                    if (playbackButtonText != null)
                    {
                        playbackButtonText.text = "🔒 アップグレードで再生";
                    }
                }
                else if (canPlay)
                {
                    // 再生可能
                    playbackButton.interactable = true;
                    UpdatePlaybackButtonState(false);
                }
                else
                {
                    // ファイルが存在しない
                    playbackButton.interactable = false;
                    if (playbackButtonText != null)
                    {
                        playbackButtonText.text = "音声なし";
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// 複数録音対応: フォーカス中の波紋に関連する録音リストを設定します。
    /// </summary>
    public void SetPlaybackList(string monthKey, List<(long timestamp, string audioFileName)> recordings, bool canPlay, bool isExpired)
    {
        currentFocusedMonthKey = monthKey;
        currentRecordingList = recordings ?? new List<(long, string)>();
        currentSelectedIndex = 0;
        
        int count = currentRecordingList.Count;
        Debug.Log($"[MainUIManager] SetPlaybackList: count={count}, canPlay={canPlay}, isExpired={isExpired}");
        
        // 録音なし
        if (count == 0)
        {
            if (recordingListPanel != null) recordingListPanel.SetActive(false);
            currentFocusedAudioFileName = null;
            return;
        }
        
        // 1つ以上: 常にリスト表示
        currentFocusedAudioFileName = currentRecordingList[0].audioFileName;
        
        // リストパネルを表示
        if (recordingListPanel != null)
        {
            recordingListPanel.SetActive(true);
            PopulateRecordingList(canPlay, isExpired);
        }
        else
        {
            Debug.LogWarning("[MainUIManager] recordingListPanel is null! Please assign it in the Inspector.");
        }
    }
    
    /// <summary>
    /// 録音リストUIを生成します。
    /// </summary>
    private void PopulateRecordingList(bool canPlay, bool isExpired)
    {
        if (recordingListContent == null) return;
        
        // 既存のリスト項目をクリア
        foreach (Transform child in recordingListContent)
        {
            Destroy(child.gameObject);
        }
        
        // プレハブがなければテキストベースで簡易生成
        for (int i = 0; i < currentRecordingList.Count; i++)
        {
            var (timestamp, audioFileName) = currentRecordingList[i];
            int index = i; // クロージャ用
            
            GameObject item;
            if (recordingItemPrefab != null)
            {
                item = Instantiate(recordingItemPrefab, recordingListContent);
            }
            else
            {
                // フォールバック: 簡易ボタン生成
                item = new GameObject($"RecordingItem_{i}");
                item.transform.SetParent(recordingListContent, false);
                var btn = item.AddComponent<Button>();
                var txt = item.AddComponent<TextMeshProUGUI>();
                txt.fontSize = 14;
                txt.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            }
            
            // テキスト設定（GetTimeLabelを使用）
            var text = item.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = GetTimeLabel(i);
                // 初期色設定（まだ再生していないので全て白）
                text.color = Color.white;
            }
            
            // ボタンクリック設定
            var button = item.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = canPlay && !isExpired;
                button.onClick.AddListener(() => OnRecordingItemSelected(index));
            }
        }
    }
    
    /// <summary>
    /// 録音リストの項目が選択された時のハンドラー。
    /// </summary>
    private void OnRecordingItemSelected(int index)
    {
        if (index < 0 || index >= currentRecordingList.Count) return;
        
        currentSelectedIndex = index;
        currentFocusedAudioFileName = currentRecordingList[index].audioFileName;
        
        // 再生開始
        if (audioPlaybackController != null)
        {
            audioPlaybackController.PlayRecording(currentFocusedMonthKey, currentFocusedAudioFileName);
            UpdatePlaybackButtonState(true);
        }
        
        // リスト表示更新（選択状態の強調）
        int i = 0;
        foreach (Transform child in recordingListContent)
        {
            var text = child.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.color = (i == currentSelectedIndex) ? Color.cyan : Color.white;
            }
            i++;
        }
    }
    
    /// <summary>
    /// 再生ボタンの表示状態を更新します。
    /// </summary>
    private void UpdatePlaybackButtonState(bool isPlaying)
    {
        if (playbackButtonText != null)
        {
            playbackButtonText.text = isPlaying ? "■ 停止" : "▶ 再生";
        }
    }
    
    /// <summary>
    /// 再生ボタンがクリックされた時のハンドラー。
    /// </summary>
    public void OnPlaybackButtonClick()
    {
        if (audioPlaybackController == null || string.IsNullOrEmpty(currentFocusedAudioFileName))
        {
            return;
        }
        
        if (audioPlaybackController.IsPlaying)
        {
            audioPlaybackController.StopPlayback();
            UpdatePlaybackButtonState(false);
        }
        else
        {
            audioPlaybackController.PlayRecording(currentFocusedMonthKey, currentFocusedAudioFileName);
            UpdatePlaybackButtonState(true);
        }
    }
    
    /// <summary>
    /// 再生開始イベントハンドラー
    /// </summary>
    private void OnPlaybackStartedHandler()
    {
        UpdateRecordingListVisuals(true);
    }
    
    /// <summary>
    /// 再生終了イベントハンドラー
    /// </summary>
    private void OnPlaybackFinishedHandler()
    {
        UpdateRecordingListVisuals(false);
    }
    
    /// <summary>
    /// 録音リストの視覚状態を更新（再生中は緑、停止時は白、選択中はシアン）
    /// </summary>
    private void UpdateRecordingListVisuals(bool isPlaying)
    {
        if (recordingListContent == null) return;
        
        int i = 0;
        foreach (Transform child in recordingListContent)
        {
            var text = child.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                if (i == currentSelectedIndex)
                {
                    // 選択中の項目
                    if (isPlaying)
                    {
                        // 再生中: 緑で点滅感を出すためにライム色
                        text.color = new Color(0.4f, 1f, 0.4f); // ライトグリーン
                        text.text = $"[再生中] {GetTimeLabel(i)}";
                    }
                    else
                    {
                        // 停止中: シアン
                        text.color = Color.cyan;
                        text.text = GetTimeLabel(i);
                    }
                }
                else
                {
                    // 非選択項目: 白
                    text.color = Color.white;
                    text.text = GetTimeLabel(i);
                }
            }
            i++;
        }
    }
    
    /// <summary>
    /// リスト項目のラベルを取得
    /// </summary>
    private string GetTimeLabel(int index)
    {
        if (index < 0 || index >= currentRecordingList.Count) return "";
        var (timestamp, _) = currentRecordingList[index];
        var dt = System.DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
        return $"録音 {dt:HH:mm:ss}";
    }

    [Header("Focus Mode Settings")]
    [SerializeField] private List<GameObject> panelsToHideOnFocus; // インスペクターで設定
    [SerializeField] private List<GameObject> panelsToHideOnRecording; // ★追加: 録音中に隠すUIリスト

    /// <summary>
    /// フォーカスモード（波紋詳細表示）のON/OFFに応じてUIの表示を切り替えます。
    /// </summary>
    public void SetFocusMode(bool isFocused)
    {
        if (panelsToHideOnFocus != null)
        {
            foreach (var panel in panelsToHideOnFocus)
            {
                if (panel != null) panel.SetActive(!isFocused);
            }
        }
    }

    /// <summary>
    /// 録音モードのON/OFFに応じてUIの表示を切り替えます。
    /// </summary>
    public void SetRecordingMode(bool isRecording)
    {
        if (panelsToHideOnRecording != null)
        {
            foreach (var panel in panelsToHideOnRecording)
            {
                if (panel != null) panel.SetActive(!isRecording);
            }
        }
    }

    // --- UI Update Helper ---
    
    private bool isSystemBusy = false; // Tracks Recording OR Analyzing state

    private void UpdateBusyState(bool isBusy)
    {
        isSystemBusy = isBusy;
        SetRecordingMode(isBusy);
        UpdateRecButtonVisibility();
    }

    private void UpdateRecButtonState(bool isRecording)
    {
        if (recButton != null)
        {
            var text = recButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (text != null)
            {
                text.text = isRecording ? "STOP" : "REC";
                text.color = isRecording ? Color.red : Color.white;
            }
            UpdateRecButtonVisibility();
        }
    }

    private void UpdateRecButtonVisibility()
    {
        if (recButton == null) return;

        // Hide RecButton ONLY if Busy (Analyzing) AND Not Recording
        // (If Recording, we need it visible to show "STOP")
        bool shouldShow = !isSystemBusy || (isSystemBusy && (microphoneController != null && microphoneController.IsRecording));
        
        // Archive Mode overrides everything (already handled in SetArchiveMode, but let's be safe)
        if (isArchiveMode) shouldShow = false;

        recButton.gameObject.SetActive(shouldShow);
    }

    // --- Tutorial References ---
    public RectTransform RecButtonRect => recButton != null ? recButton.GetComponent<RectTransform>() : null;
    public RectTransform GalleryButtonRect => galleryButton != null ? galleryButton.GetComponent<RectTransform>() : null;
    public RectTransform SettingsButtonRect => settingsButton != null ? settingsButton.GetComponent<RectTransform>() : null;

    void OnDestroy()
    {
        if (microphoneController != null)
        {
            microphoneController.OnRecordingStateChanged -= UpdateRecButtonState;
            microphoneController.OnBusyStateChanged -= UpdateBusyState;
        }
        
        // Unsubscribe from Auth State Changes
        Firebase.Auth.FirebaseAuth.DefaultInstance.StateChanged -= AuthStateChanged;

        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnSessionExpired -= OnSessionExpiredHandler;
        }
        
        // ★追加: 再生イベント購読解除
        if (audioPlaybackController != null)
        {
            audioPlaybackController.OnPlaybackStarted -= OnPlaybackStartedHandler;
            audioPlaybackController.OnPlaybackFinished -= OnPlaybackFinishedHandler;
        }
    }

    private void OnSessionExpiredHandler(string message)
    {
        MainThreadDispatcher.Enqueue(() => {
            ShowBlockingMessage(message, true);
        });
    }
}