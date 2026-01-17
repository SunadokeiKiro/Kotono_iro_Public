using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// 設定シーンにおけるAPIキーやマイク関連の設定を管理し、ファイルに保存・読込します。
/// </summary>
public class SettingsManager : MonoBehaviour
{
    [Header("API設定")]
    [SerializeField] private TMP_InputField apiKeyInput;

    [Header("マイク設定")]
    [SerializeField] private TMP_Dropdown microphoneDropdown;
    [SerializeField] private Slider recordLengthSlider;
    [SerializeField] private Text recordLengthValueText;
    [SerializeField] private Slider gainSlider; // ★ ゲインスライダー
    [SerializeField] private Text gainValueText; // ★ ゲイン値表示
    [SerializeField] private Slider silenceTimeSlider;
    [SerializeField] private Text silenceTimeText;

    [Header("音声モニター")]
    [SerializeField] private Image voiceLevelBar;
    [SerializeField] private Image thresholdLineMarker; // ★ 固定位置に変更
    [SerializeField] private Image peakHoldMarker; // ★ ピークホールド表示用
    [SerializeField] private Text currentLevelText;

    [Header("UI要素")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button logoutButton; // Added
    [SerializeField] private TextMeshProUGUI statusText;  // ★ Changed to TMP
    [SerializeField] private TextMeshProUGUI currentPlanText; // ★ Changed to TMP

    private string micSettingsFilePath;

    private AudioClip microphoneClip;
    private string currentMonitoringDevice;
    private float[] samplesData;
    private float currentVoiceLevel = 0f;
    private bool isMonitoring = false;
    
    // ★ ピークホールド用
    private float peakLevel = 0f;
    private float peakHoldTimer = 0f;
    private const float PEAK_HOLD_DURATION = 1.0f;
    
    // ★ 固定閾値
    private const float FIXED_THRESHOLD = 0.05f;

    /// <summary>
    /// マイク設定をシリアライズするためのデータクラス。
    /// </summary>
    [Serializable]
    public class MicSettings
    {
        public string selectedMicrophone = "";
        public int recordLengthSec = 20;
        public float voiceDetectionThreshold = 0.05f; // ★ 固定値に変更
        public float silenceDetectionTime = 2.0f;
        public float inputGain = 5.0f; // ★ 新規追加 (1.0〜20.0)
    }

    private MicSettings micSettings = new MicSettings();

    void Start()
    {
        // ファイルパスを設定
        micSettingsFilePath = Path.Combine(Application.persistentDataPath, "micsettings.json");
        
        InitializeSliderRanges();

        // UIイベントのリスナーを設定
        saveButton.onClick.AddListener(SaveAllSettings);
        backButton.onClick.AddListener(GoBackToMainScene);
        if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClick);

        if (recordLengthSlider != null) 
        {
            recordLengthSlider.gameObject.SetActive(false); // ★ ユーザー要望: 廃止のため非表示
            if(recordLengthValueText) recordLengthValueText.gameObject.SetActive(false); 
        }

        // ★ ゲインスライダーのリスナー
        if (gainSlider != null) gainSlider.onValueChanged.AddListener(OnGainChanged);
        if (silenceTimeSlider != null) silenceTimeSlider.onValueChanged.AddListener(OnSilenceTimeChanged);


        // 保存されている設定を読み込んでUIに反映
        LoadApiKey();
        LoadMicSettings();

        // ★ プランに応じたUI更新
        UpdatePlanUI();

        // マイク関連の初期化
        InitializeMicrophoneDropdown();

        // サンプルデータ用の配列を初期化
        samplesData = new float[1024];
        
        // ★ 閾値マーカーを固定位置に設定
        UpdateThresholdMarkerPosition();

        // マイクモニタリングを開始
        StartMicrophoneMonitoring();

        ApplyStyles();
    }

    private void ApplyStyles()
    {
        // Global styling
        UIStyler.ApplyStyleToInputField(apiKeyInput); // Overload exists for TMP_InputField
        UIStyler.ApplyStyleToSlider(recordLengthSlider);
        UIStyler.ApplyStyleToSlider(gainSlider); // ★ ゲインスライダーに変更
        UIStyler.ApplyStyleToSlider(silenceTimeSlider);
        UIStyler.ApplyStyleToButton(saveButton);
        UIStyler.ApplyStyleToButton(backButton, isIconOnly: true);
        if (logoutButton != null) UIStyler.ApplyStyleToButton(logoutButton, isIconOnly: false);
        
        // Settings page specific text colors
        UIStyler.ApplyStyleToText(recordLengthValueText);
        UIStyler.ApplyStyleToText(gainValueText); // ★ ゲイン値に変更
        UIStyler.ApplyStyleToText(silenceTimeText);
        UIStyler.ApplyStyleToTMP(statusText, isHeader: true);
        UIStyler.ApplyStyleToTMP(currentPlanText, isHeader: true);
        UIStyler.ApplyStyleToText(currentLevelText);
    }

    /// <summary>
    /// 現在のプランに基づいてUI（プラン名表示、APIキー入力欄のヒント）を更新します。
    /// </summary>
    void UpdatePlanUI()
    {
        if (SubscriptionManager.Instance == null) return;

        var plan = SubscriptionManager.Instance.CurrentPlan;
        
        // プラン名の表示
        if (currentPlanText != null)
        {
            currentPlanText.text = $"現在のプラン: {plan}";
        }

        // APIキー入力欄のプレースホルダー変更
        // TMP_InputField.placeholder is usually a Graphic (Text or TMP_Text)
        if (apiKeyInput != null && apiKeyInput.placeholder != null)
        {
            // Try to get TMP component
            var placeholderTMP = apiKeyInput.placeholder.GetComponent<TextMeshProUGUI>();
            
            bool canUseAppKey = SubscriptionManager.Instance.CanUseAppKey;
            string hintText = canUseAppKey ? "APIキー (任意 / 入力なしでApp Key使用)" : "APIキーを入力してください (必須)";

            if (placeholderTMP != null)
            {
                placeholderTMP.text = hintText;
            }
            else
            {
                 // Fallback to legacy Text if somehow used (unlikely for TMP_InputField but possible in mixed setup)
                 var placeholderText = apiKeyInput.placeholder.GetComponent<Text>();
                 if(placeholderText != null) placeholderText.text = hintText;
            }
        }
    }

    void Update()
    {
        if (isMonitoring && microphoneClip != null)
        {
            UpdateVoiceLevel();
        }
    }

    /// <summary>
    /// 各スライダーの最小値、最大値、刻み方を設定します。
    /// </summary>
    private void InitializeSliderRanges()
    {
        // ★ ゲインスライダーの範囲設定
        if (gainSlider != null)
        {
            gainSlider.minValue = 1.0f;
            gainSlider.maxValue = 20.0f;
            gainSlider.wholeNumbers = false;
        }
        if (silenceTimeSlider != null)
        {
            silenceTimeSlider.minValue = 0.5f;
            silenceTimeSlider.maxValue = 5.0f;
            silenceTimeSlider.wholeNumbers = false;
        }
    }

    /// <summary>
    /// 利用可能なマイクデバイスをドロップダウンに設定します。
    /// </summary>
    void InitializeMicrophoneDropdown()
    {
        if (microphoneDropdown == null) return;

        microphoneDropdown.ClearOptions();
        string[] devices = Microphone.devices;

        if (devices.Length > 0)
        {
            microphoneDropdown.AddOptions(devices.ToList());
            microphoneDropdown.onValueChanged.AddListener(OnMicrophoneSelectionChanged);

            int savedIndex = Array.IndexOf(devices, micSettings.selectedMicrophone);
            if (savedIndex >= 0)
            {
                microphoneDropdown.value = savedIndex;
            }
            else if(!string.IsNullOrEmpty(micSettings.selectedMicrophone))
            {
                Debug.LogWarning($"Saved microphone '{micSettings.selectedMicrophone}' not found.");
            }
        }
        else
        {
            microphoneDropdown.options.Add(new TMP_Dropdown.OptionData("マイクが見つかりません")); // Updated type
            microphoneDropdown.interactable = false;
            if (statusText != null) statusText.text = "警告: マイクデバイスが見つかりません";
        }
    }

    /// <summary>
    /// マイクの選択が変更されたときにモニタリングを再開します。
    /// </summary>
    void OnMicrophoneSelectionChanged(int index)
    {
        StopMicrophoneMonitoring();
        StartMicrophoneMonitoring();
    }

    #region 設定の保存と読込
    /// <summary>
    /// APIキーとマイク設定の両方を保存します。
    /// </summary>
    void SaveAllSettings()
    {
        SaveApiKey();
        SaveMicSettings();
        if (statusText != null)
        {
            statusText.text = "すべての設定を保存しました";
            statusText.color = Color.green;
        }
    }
    
    void LoadApiKey()
    {
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.GetUserApiKey(
                (key) => {
                    if (apiKeyInput != null) 
                    {
                        apiKeyInput.text = key;
                        if (!string.IsNullOrEmpty(key) && statusText != null) 
                            statusText.text = "FirestoreからAPIキーを読み込みました";
                    }
                },
                (error) => {
                    Debug.LogError("Failed to load API key from Firestore: " + error);
                    if (statusText != null) statusText.text = "APIキー読込失敗: " + error;
                }
            );
        }
    }

    void LoadMicSettings()
    {
        try
        {
            if (File.Exists(micSettingsFilePath))
            {
                string json = File.ReadAllText(micSettingsFilePath);
                micSettings = JsonUtility.FromJson<MicSettings>(json);
                Debug.Log("Microphone settings loaded.");
            }
            
            // ★ ゲイン値をスライダーに反映
            if (gainSlider != null)
                gainSlider.value = Mathf.Clamp(micSettings.inputGain, gainSlider.minValue, gainSlider.maxValue);
            if (silenceTimeSlider != null)
                silenceTimeSlider.value = Mathf.Clamp(micSettings.silenceDetectionTime, silenceTimeSlider.minValue, silenceTimeSlider.maxValue);
            
            UpdateRecordLengthText();
            UpdateGainValueText();
            UpdateSilenceTimeText();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load microphone settings: " + e.Message);
        }
    }
    
    void SaveApiKey()
    {
        if (string.IsNullOrEmpty(apiKeyInput.text))
        {
            Debug.LogWarning("API key input is empty.");
            return;
        }

        string apiKey = apiKeyInput.text.Trim();

        if (FirestoreManager.Instance != null)
        {
            // 1. まずFirestoreに保存
            FirestoreManager.Instance.SaveUserApiKey(apiKey);
            Debug.Log("API key saving to Firestore...");

            // 2. Cloud FunctionでAPIキーの有効性を検証
            if (statusText != null)
            {
                statusText.text = "APIキーを検証中...";
                statusText.color = Color.white;
            }
            
            FirestoreManager.Instance.ValidateApiKey(apiKey,
                (response) => {
                    if (response.valid)
                    {
                        if (statusText != null)
                        {
                            statusText.text = response.message;
                            statusText.color = Color.green;
                        }
                        
                        // 特典メッセージを追加表示
                        if (response.offerEligible)
                        {
                            ShowOfferMessage("Standardプラン1カ月無料特典あり");
                        }
                    }
                    else
                    {
                        if (statusText != null)
                        {
                            statusText.text = response.error ?? "APIキーが無効です";
                            statusText.color = Color.red;
                        }
                    }
                },
                (error) => {
                    if (statusText != null)
                    {
                        statusText.text = "検証エラー: " + error;
                        statusText.color = Color.red;
                    }
                }
            );
        }
    }

    /// <summary>
    /// statusTextに特典メッセージを追加表示（2行以内）
    /// </summary>
    void ShowOfferMessage(string message)
    {
        if (statusText != null)
        {
            statusText.text += "\n" + message;
        }
    }

    void SaveMicSettings()
    {
        try
        {
            if (microphoneDropdown.options.Count > 0 && microphoneDropdown.interactable)
            {
                micSettings.selectedMicrophone = microphoneDropdown.options[microphoneDropdown.value].text;
            }

            // ★ ゲイン値を保存（閾値は固定）
            if (gainSlider != null) micSettings.inputGain = gainSlider.value;
            if (silenceTimeSlider != null) micSettings.silenceDetectionTime = silenceTimeSlider.value;

            string json = JsonUtility.ToJson(micSettings, true);
            File.WriteAllText(micSettingsFilePath, json);
            Debug.Log("Microphone settings saved to: " + micSettingsFilePath);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to save microphone settings: " + e.Message);
        }
    }
    #endregion

    #region UIスライダーのイベントハンドラ
    void OnRecordLengthChanged(float value)
    {
        UpdateRecordLengthText();
    }

    void UpdateRecordLengthText()
    {
        if (recordLengthValueText != null && recordLengthSlider != null)
        {
            recordLengthValueText.text = recordLengthSlider.value.ToString("F0") + "秒";
        }
    }

    // ★ ゲインスライダーのイベントハンドラ
    void OnGainChanged(float value)
    {
        UpdateGainValueText();
    }

    void UpdateGainValueText()
    {
        if (gainValueText != null && gainSlider != null)
        {
            gainValueText.text = $"{gainSlider.value:F1}x";
        }
    }
    
    void OnSilenceTimeChanged(float value)
    {
        UpdateSilenceTimeText();
    }

    void UpdateSilenceTimeText()
    {
        if (silenceTimeText != null)
        {
            silenceTimeText.text = silenceTimeSlider.value.ToString("F1") + "秒";
        }
    }
    #endregion
    
    #region マイクモニタリング
    void StartMicrophoneMonitoring()
    {
        if (microphoneDropdown.options.Count == 0 || !microphoneDropdown.interactable)
        {
            Debug.LogWarning("Cannot start monitoring: No microphone available.");
            return;
        }

        currentMonitoringDevice = microphoneDropdown.options[microphoneDropdown.value].text;
        microphoneClip = Microphone.Start(currentMonitoringDevice, true, 1, 44100);

        if (microphoneClip == null)
        {
            Debug.LogError("Microphone.Start failed for monitoring.");
            return;
        }

        isMonitoring = true;
        if (statusText != null) statusText.text = "マイクモニタリング中...";
    }

    void StopMicrophoneMonitoring()
    {
        if (isMonitoring && !string.IsNullOrEmpty(currentMonitoringDevice))
        {
            Microphone.End(currentMonitoringDevice);
            isMonitoring = false;
            currentMonitoringDevice = null;
            microphoneClip = null;

            if (voiceLevelBar != null) voiceLevelBar.fillAmount = 0f;
            if (currentLevelText != null) currentLevelText.text = "0.000";
        }
    }

    void UpdateVoiceLevel()
    {
        if (microphoneClip == null || !Microphone.IsRecording(currentMonitoringDevice))
        {
            isMonitoring = false;
            return;
        }

        int micPosition = Microphone.GetPosition(currentMonitoringDevice);
        if (micPosition < samplesData.Length) return;

        microphoneClip.GetData(samplesData, micPosition - samplesData.Length);

        // ★ RMS計算（MicrophoneControllerと同じ方式）
        float sum = 0f;
        foreach (float sample in samplesData)
        {
            sum += sample * sample;
        }
        float rawRms = Mathf.Sqrt(sum / samplesData.Length);
        
        // ★ ユーザー設定のゲインを適用
        float gain = (gainSlider != null) ? gainSlider.value : 5f;
        currentVoiceLevel = rawRms * gain;
        
        // ★ ピークホールド処理
        if (currentVoiceLevel > peakLevel)
        {
            peakLevel = currentVoiceLevel;
            peakHoldTimer = PEAK_HOLD_DURATION;
        }
        else
        {
            peakHoldTimer -= Time.deltaTime;
            if (peakHoldTimer <= 0)
            {
                peakLevel = Mathf.Lerp(peakLevel, currentVoiceLevel, 0.1f);
            }
        }

        UpdateVoiceLevelUI();
    }

    void UpdateVoiceLevelUI()
    {
        if (voiceLevelBar != null)
        {
            // ★ 固定閾値を基準に表示
            float maxDisplay = Mathf.Max(FIXED_THRESHOLD * 3f, 0.2f);
            voiceLevelBar.fillAmount = Mathf.Clamp01(currentVoiceLevel / maxDisplay);
            
            // ★ 色の遷移ロジック（グレー → 黄 → 緑）
            float ratio = currentVoiceLevel / FIXED_THRESHOLD;
            Color barColor;
            if (ratio >= 1.0f)
            {
                barColor = Color.green; // 閾値超え
            }
            else if (ratio >= 0.7f)
            {
                barColor = Color.Lerp(Color.yellow, Color.green, (ratio - 0.7f) / 0.3f);
            }
            else
            {
                barColor = Color.Lerp(Color.gray, Color.yellow, ratio / 0.7f);
            }
            voiceLevelBar.color = barColor;
        }
        
        // ★ ピークホールドマーカーの更新
        if (peakHoldMarker != null && voiceLevelBar != null)
        {
            float maxDisplay = Mathf.Max(FIXED_THRESHOLD * 3f, 0.2f);
            RectTransform barRect = voiceLevelBar.rectTransform;
            RectTransform markerRect = peakHoldMarker.rectTransform;
            float peakX = Mathf.Clamp01(peakLevel / maxDisplay) * barRect.rect.width;
            Vector2 pos = markerRect.anchoredPosition;
            pos.x = peakX;
            markerRect.anchoredPosition = pos;
        }

        if (currentLevelText != null)
        {
            currentLevelText.text = currentVoiceLevel.ToString("F3");
        }
    }
    
    /// <summary>
    /// 閾値マーカーを固定位置に設定します。
    /// </summary>
    void UpdateThresholdMarkerPosition()
    {
        if (thresholdLineMarker != null && voiceLevelBar != null)
        {
            RectTransform barRect = voiceLevelBar.rectTransform;
            RectTransform markerRect = thresholdLineMarker.rectTransform;

            float barWidth = barRect.rect.width;
            // ★ 固定閾値の位置を計算
            float maxDisplay = Mathf.Max(FIXED_THRESHOLD * 3f, 0.2f);
            float normalizedThreshold = FIXED_THRESHOLD / maxDisplay;
            
            Vector2 anchoredPosition = markerRect.anchoredPosition;
            anchoredPosition.x = normalizedThreshold * barWidth;
            markerRect.anchoredPosition = anchoredPosition;
        }
    }
    #endregion

    void GoBackToMainScene()
    {
        Debug.Log("Returning to main scene...");
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadScene("ArtScene");
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager not found. Loading scene directly.");
            SceneManager.LoadScene("ArtScene");
        }
    }

    void OnLogoutClick()
    {
        Debug.Log("Logging out...");
        if (FirebaseConfig.Instance != null)
        {
            FirebaseConfig.Instance.SignOut();
        }

        // Return to Main Scene (Login Logic in LoginUIManager will handle the rest)
        GoBackToMainScene();
    }

    void OnDestroy()
    {
        StopMicrophoneMonitoring();
    }
}
