// Scripts/MicrophoneController.cs
using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Linq;

public class MicrophoneController : MonoBehaviour
{
    [Header("Api Handler Reference")]
    [SerializeField] private ApiHandler apiHandler;
    [SerializeField] private MainUIManager mainUIManager;


    // --- 設定値 (SettingsManagerからロード) ---
    private int recordLengthSec = 20;
    private float voiceDetectionThreshold = 0.05f; // ★ 固定値に変更
    private float silenceDetectionTime = 2.0f;
    private float inputGain = 5.0f; // ★ 新規追加: ユーザー設定のゲイン
    private const int SAMPLE_RATE = 44100;

    // --- 内部状態 ---
    private string selectedMicrophoneDevice;
    private AudioClip microphoneInput;
    private string audioFilePath;
    private bool isRecording = false;
    private bool isListening = false;
    private float lastVoiceDetectedTime = 0f;
    private float[] samples = new float[128]; 

    public bool IsAutoRecordEnabled { get; private set; } = false;
    public bool IsRecordingOrAnalyzing => isRecording || (apiHandler != null && apiHandler.IsAnalyzing);
    public bool IsApiReady => apiHandler != null && apiHandler.IsReady; // ★ Proxy
    public float CurrentRmsValue { get; private set; } // ★ プロパティ定義を追加
    
    public float OverrideRecordLength { get; set; } = -1f; // ★追加: スケジュール録音用の強制制限時間 (-1で無効)
    public event Action<float, bool> OnRecordingFinished; // ★追加: 録音完了通知イベント (秒数, 自動かどうか)
    public event Action<bool> OnRecordingStateChanged; // ★追加: 録音状態変更イベント (True=開始, False=終了)
    public event Action<bool> OnBusyStateChanged; // ★追加: 録音〜分析完了までを含む「作業中」状態 (UI非表示用)

    private bool isCurrentRecordingAuto = false; // ★追加: 現在の録音が自動かどうか

    IEnumerator Start()
    {
        if (apiHandler == null)
        {
            Debug.LogError("MicrophoneController: ApiHandler is not set in the inspector.");
            enabled = false;
            yield break;
        }
        if (mainUIManager == null)
        {
            Debug.LogError("MicrophoneController: MainUIManager is not set in the inspector.");
            enabled = false;
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            // Wait for permission
            yield return new WaitForSeconds(0.5f);
            yield return new WaitUntil(() => UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone));
        }
#endif

        LoadSettings();
        
        // Wait a frame to ensure devices are detected after permission grant
        yield return null;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found. Recording features will be disabled.");
            if(mainUIManager != null) mainUIManager.ShowBlockingMessage("マイクが見つかりません", true);
            yield break;
        }
        
        if (!string.IsNullOrEmpty(selectedMicrophoneDevice) && Microphone.devices.Contains(selectedMicrophoneDevice))
        {
        }
        else
        {
            selectedMicrophoneDevice = Microphone.devices[0];
            Debug.LogWarning($"Specified microphone not found. Using default microphone: '{selectedMicrophoneDevice}'");
        }
        Debug.Log("Using Microphone: " + selectedMicrophoneDevice);
        
        audioFilePath = Path.Combine(Application.persistentDataPath, "recorded_audio.wav");

        // Subscribe to analysis completion event
        if (apiHandler != null)
        {
            apiHandler.OnAnalysisCompleted += OnAnalysisCompleted;
        }
    }

    private void OnAnalysisCompleted()
    {
        // Analysis finished = Busy state ends
        OnBusyStateChanged?.Invoke(false);

        // Resume auto-recording if enabled
        if (IsAutoRecordEnabled && !isRecording && !isListening)
        {
            Debug.Log("[MicrophoneController] Analysis finished. Resuming auto-recording listener...");
            StartListening();
        }
    }
    


    // (LoadSettings, 自動録音関連のメソッドは修正なし)
    private void LoadSettings()
    {
        try
        {
            string settingsPath = Path.Combine(Application.persistentDataPath, "micsettings.json");
            if (File.Exists(settingsPath))
            {
                string jsonData = File.ReadAllText(settingsPath);
                SettingsManager.MicSettings settings = JsonUtility.FromJson<SettingsManager.MicSettings>(jsonData);

                voiceDetectionThreshold = settings.voiceDetectionThreshold;
                silenceDetectionTime = settings.silenceDetectionTime;
                recordLengthSec = settings.recordLengthSec;
                selectedMicrophoneDevice = settings.selectedMicrophone;
                inputGain = settings.inputGain; // ★ ゲイン読み込み追加

                Debug.Log($"Microphone settings loaded: Threshold={voiceDetectionThreshold}, SilenceTime={silenceDetectionTime}s, RecordLength={recordLengthSec}s, Gain={inputGain}x, MicDevice='{selectedMicrophoneDevice}'");
            }
            else
            {
                Debug.Log("Microphone settings file not found. Using default values.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load microphone settings: " + e.Message);
        }
    }

    #region 自動録音
    public void StartAutoRecording()
    {
        StartCoroutine(StartAutoRecordingSequenceWithSync());
    }

    private IEnumerator StartAutoRecordingSequenceWithSync()
    {
        if (string.IsNullOrEmpty(selectedMicrophoneDevice)) yield break;

        // 1. Sync with Server (Blocking)
        if (SubscriptionManager.Instance != null)
        {
            bool syncSuccess = false;
            yield return SubscriptionManager.Instance.SyncQuotaWithServer((success) => syncSuccess = success);
            
            // ★ セキュリティ強化: 同期失敗時は録音を拒否（オフライン不正使用防止）
            if (!syncSuccess)
            {
                Debug.LogWarning("[MicrophoneController] Sync failed. Blocking auto-recording for security.");
                mainUIManager.ShowBlockingMessage("サーバーに接続できません\nネットワークを確認してください", true);
                yield break;
            }

            // 2. Re-Check Quota after sync
            float remaining = SubscriptionManager.Instance.GetRemainingQuotaSeconds();
            if (remaining <= 0)
            {
                Debug.LogWarning("[Quota] Monthly limit reached (checked after sync). Cannot start auto-recording.");
                mainUIManager.ShowBlockingMessage("月間制限に達しました", true);
                yield break;
            }
        }

        IsAutoRecordEnabled = true;
        Debug.Log("Auto-recording enabled.");
        StartListening();
    }
    
    public void StopAutoRecording()
    {
        IsAutoRecordEnabled = false;
        StopListening();
        
        // ★ Fix: Stop recording ONLY if it was started automatically
        if (isRecording && isCurrentRecordingAuto)
        {
            StopRecordingAndProcess();
        }
        else if (isRecording && !isCurrentRecordingAuto)
        {
             Debug.Log("Auto-Record disabled, but continuing Manual Recording.");
        }
        else
        {
             // Not recording, just disabled flag
        }
        
        Debug.Log("Auto-recording disabled.");
    }

    private int GetCorrectSampleRate(string deviceName)
    {
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(deviceName, out minFreq, out maxFreq);

        int finalSampleRate = SAMPLE_RATE; // Default 44100
        
        // If both are 0, any frequency is supported (keep default)
        if (minFreq == 0 && maxFreq == 0)
        {
            return finalSampleRate;
        }
        
        if (minFreq > 0 && maxFreq > 0)
        {
            finalSampleRate = Mathf.Clamp(SAMPLE_RATE, minFreq, maxFreq);
        }
        else if (maxFreq > 0)
        {
             finalSampleRate = maxFreq; // Use max capability if only max is reported
        }
        
        return finalSampleRate;
    }

    private void StartListening()
    {
        // ★★★ 修正: apiHandler.IsAnalyzing のチェックを追加 ★★★
        if (string.IsNullOrEmpty(selectedMicrophoneDevice) || !IsAutoRecordEnabled || isListening || isRecording || apiHandler.IsAnalyzing) return;

        isListening = true;
        Debug.Log("Starting to listen for voice activity...");

        // ★ Frequency Selection Logic
        int finalSampleRate = GetCorrectSampleRate(selectedMicrophoneDevice);
        
        // Log capabilities for debugging
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(selectedMicrophoneDevice, out minFreq, out maxFreq);
        Debug.Log($"Microphone Init: Device={selectedMicrophoneDevice}, Rate={finalSampleRate}Hz (Caps: {minFreq}-{maxFreq})");

        microphoneInput = Microphone.Start(selectedMicrophoneDevice, true, 1, finalSampleRate);
        
        if (microphoneInput == null)
        {
            Debug.LogError("Microphone.Start failed (for listening).");
            isListening = false;
            return;
        }

        // Increase buffer for better detection (1024 samples @ 44k = ~23ms)
        if (samples == null || samples.Length != 1024)
        {
            samples = new float[1024];
        }

        StartCoroutine(MonitorAudioLevel());
    }

    private void StopListening()
    {
        isListening = false;
        StopAllCoroutines(); 
        if (Microphone.IsRecording(selectedMicrophoneDevice))
        {
            Microphone.End(selectedMicrophoneDevice);
        }
    }
    
    private IEnumerator MonitorAudioLevel()
    {
        yield return new WaitForSeconds(0.1f); 

        while (isListening && IsAutoRecordEnabled && !isRecording)
        {
            float rmsValue = GetCurrentRmsValue(microphoneInput);
            
            // Gain removed
            // rmsValue *= (inputGain > 1.0f ? inputGain : 1.0f);
            
            CurrentRmsValue = rmsValue; 
            if (rmsValue > voiceDetectionThreshold)
            {
                Debug.Log($"Voice detected (Level: {rmsValue:F4}). Starting automatic recording.");
                StopListening(); 
                yield return StartCoroutine(StartAutomaticRecordingSequence()); 
                yield break; 
            }
            yield return null;
        }
        isListening = false;
        CurrentRmsValue = 0f; 
    }
    
    private IEnumerator StartAutomaticRecordingSequence()
    {
        // ★★★ 修正: apiHandler.IsAnalyzing のチェックを追加 ★★★
        if (isRecording || !IsAutoRecordEnabled || string.IsNullOrEmpty(selectedMicrophoneDevice) || apiHandler.IsAnalyzing) yield break;

        // Quota Cap Calculation (Base Limit)
        float currentMaxDuration = recordLengthSec;

        // 1. Override Check (Schedule Feature)
        if (OverrideRecordLength > 0 && OverrideRecordLength < currentMaxDuration)
        {
            currentMaxDuration = OverrideRecordLength;
            Debug.Log($"[MicrophoneController] Override limit applied: {currentMaxDuration:F1}s");
        }

        // 2. Subscription Quota Check (Client-side preliminary check)
        if (SubscriptionManager.Instance != null)
        {
            float remaining = SubscriptionManager.Instance.GetRemainingQuotaSeconds();
            if (remaining < currentMaxDuration)
            {
                currentMaxDuration = remaining;
                Debug.LogWarning($"[Quota] Approaching limit. Capping recording to {currentMaxDuration:F1}s.");
            }
        }

        // ★★★ 3. サーバー側でクォータを予約（セキュリティ強化） ★★★
        bool quotaReserved = false;
        float serverRemaining = 0f;
        
        if (SubscriptionManager.Instance != null)
        {
            yield return SubscriptionManager.Instance.ReserveQuotaOnServer(
                currentMaxDuration, 
                (success, remaining) => {
                    quotaReserved = success;
                    serverRemaining = remaining;
                }
            );
            
            if (!quotaReserved)
            {
                Debug.LogWarning("[MicrophoneController] Server quota reservation failed. Cannot start auto-recording.");
                mainUIManager.ShowBlockingMessage("月間制限に達しました", true);
                
                // 自動録音を継続するためリスニングを再開
                if (IsAutoRecordEnabled) StartListening();
                yield break;
            }
            
            Debug.Log($"[MicrophoneController] Quota reserved on server. Remaining: {serverRemaining:F1}s");
        }

        isRecording = true;
        isCurrentRecordingAuto = true; // ★ 自動フラグON
        OnRecordingStateChanged?.Invoke(true); // ★ Event
        OnBusyStateChanged?.Invoke(true); // ★ Busy Mode (Hide UI)

        Debug.Log($"Automatic recording in progress... (Max {currentMaxDuration} seconds)");
        
        mainUIManager.ShowRecordingPanel("録音中...");

        microphoneInput = Microphone.Start(selectedMicrophoneDevice, false, Mathf.CeilToInt(currentMaxDuration), GetCorrectSampleRate(selectedMicrophoneDevice));
        if (microphoneInput == null)
        {
            Debug.LogError("Microphone.Start failed (for automatic recording).");
            isRecording = false;
            mainUIManager.HideRecordingPanel();
            if (IsAutoRecordEnabled) StartListening(); 
            yield break;
        }

        lastVoiceDetectedTime = Time.time;
        float recordingStartTime = Time.time;
        bool hasVoiceBeenDetected = false;

        while (isRecording && IsAutoRecordEnabled)
        {
            if (Time.time - recordingStartTime >= currentMaxDuration) // ★ 修正: recordLengthSec -> currentMaxDuration
            {
                Debug.Log("Maximum recording time reached.");
                StopRecordingAndProcess();
                yield break;
            }

            float rmsValue = GetCurrentRmsValue(microphoneInput);
            CurrentRmsValue = rmsValue; // ★ 音量更新
            if (rmsValue > voiceDetectionThreshold)
            {
                lastVoiceDetectedTime = Time.time;
                if (!hasVoiceBeenDetected)
                {
                    hasVoiceBeenDetected = true;
                    Debug.Log("Initial voice detected during recording.");
                }
            }
            else if (hasVoiceBeenDetected && (Time.time - lastVoiceDetectedTime > silenceDetectionTime))
            {
                Debug.Log("Silence detected for a period. Stopping recording.");
                StopRecordingAndProcess();
                yield break;
            }
            yield return null;
        }
    }
    #endregion

    #region 手動録音
    public IEnumerator StartManualRecording()
    {
        // ★★★ 修正: apiHandler.IsAnalyzing のチェックを追加 ★★★
        if (string.IsNullOrEmpty(selectedMicrophoneDevice) || isRecording || apiHandler.IsAnalyzing)
        {
            if(apiHandler.IsAnalyzing) Debug.LogWarning("Cannot start manual recording: Analysis is already in progress.");
            yield break;
        }

        // ★ 安全対策: 自動録音の待機中(リスニング中)であれば、一旦リスニングを停止する
        if (isListening)
        {
            Debug.Log("[MicrophoneController] Pausing auto-listening for manual recording.");
            StopListening();
        }

        // 1. Sync with Server (Blocking)
        if (SubscriptionManager.Instance != null)
        {
            // Note: SubscriptionManager handles blocking UI via events
            bool syncSuccess = false;
            yield return SubscriptionManager.Instance.SyncQuotaWithServer((success) => syncSuccess = success);
            
            // ★ セキュリティ強化: 同期失敗時は録音を拒否（オフライン不正使用防止）
            if (!syncSuccess)
            {
                Debug.LogWarning("[MicrophoneController] Sync failed. Blocking manual recording for security.");
                mainUIManager.ShowBlockingMessage("サーバーに接続できません\\nネットワークを確認してください", true);
                yield break;
            }
            
            // 2. Re-Check Quota after sync
            float remaining = SubscriptionManager.Instance.GetRemainingQuotaSeconds();
            if (remaining <= 0)
            {
                 Debug.LogWarning("[Quota] Monthly limit reached. Cannot start manual recording.");
                 mainUIManager.ShowBlockingMessage("月間制限に達しました", true);
                 yield break;
            }
        }

        // Quota Cap Calculation
        // ★ ユーザー要望: 手動録音は最大10分 (600秒) 固定
        float currentMaxDuration = 600f; 
        
        if (SubscriptionManager.Instance != null)
        {
            float remaining = SubscriptionManager.Instance.GetRemainingQuotaSeconds();
            if (remaining < currentMaxDuration)
            {
                currentMaxDuration = remaining;
                Debug.LogWarning($"[Quota] Approaching limit. Capping recording to {currentMaxDuration:F1}s.");
            }
        }
        
        // ★★★ 3. サーバー側でクォータを予約（セキュリティ強化） ★★★
        bool quotaReserved = false;
        float serverRemaining = 0f;
        
        if (SubscriptionManager.Instance != null)
        {
            yield return SubscriptionManager.Instance.ReserveQuotaOnServer(
                currentMaxDuration, 
                (success, remaining) => {
                    quotaReserved = success;
                    serverRemaining = remaining;
                }
            );
            
            if (!quotaReserved)
            {
                Debug.LogWarning("[MicrophoneController] Server quota reservation failed. Cannot start manual recording.");
                mainUIManager.ShowBlockingMessage("月間制限に達しました", true);
                yield break;
            }
            
            Debug.Log($"[MicrophoneController] Quota reserved on server. Remaining: {serverRemaining:F1}s");
        }

        isRecording = true;
        isCurrentRecordingAuto = false; // ★ 自動フラグOFF
        OnRecordingStateChanged?.Invoke(true); // ★ Event
        OnBusyStateChanged?.Invoke(true); // ★ Busy Mode (Hide UI)
        
        Debug.Log($"Manual recording started for {currentMaxDuration} seconds (10min Limit).");
        
        mainUIManager.ShowRecordingPanel("録音中...");
        
        try 
        {
            // Re-verify device existence just in case
            if (!Microphone.devices.Contains(selectedMicrophoneDevice))
            {
                 if(Microphone.devices.Length > 0) 
                 {
                     selectedMicrophoneDevice = Microphone.devices[0];
                     Debug.LogWarning($"Mic device lost, switched to default: {selectedMicrophoneDevice}");
                 }
                 else
                 {
                     throw new Exception("No Microphone devices found at runtime.");
                 }
            }

            microphoneInput = Microphone.Start(selectedMicrophoneDevice, false, Mathf.CeilToInt(currentMaxDuration), GetCorrectSampleRate(selectedMicrophoneDevice));
        }
        catch (Exception e)
        {
            Debug.LogError($"CRITICAL: Microphone.Start failed. Cause: {e.Message}");
            mainUIManager.HideRecordingPanel();
            mainUIManager.ShowBlockingMessage($"マイク起動エラー:\\n{e.Message}", true);
            isRecording = false;
            yield break;
        }

        if (microphoneInput == null)
        {
            Debug.LogError("Microphone.Start returned null.");
            isRecording = false;
            mainUIManager.HideRecordingPanel();
            mainUIManager.ShowBlockingMessage("マイク初期化失敗\\n(Null Return)", true);
            yield break;
        }

        // ★ 音量監視ループに変更
        float startTime = Time.time;
        while (isRecording && (Time.time - startTime < currentMaxDuration))
        {
            // 手動録音でも音量(RMS)を更新してVisualizerに伝える
            CurrentRmsValue = GetCurrentRmsValue(microphoneInput);
            yield return null;
        }

        Debug.Log("Manual recording time finished.");
        StopRecordingAndProcess();
    }
    
    public void StopManualRecording()
    {
        Debug.Log("Stopping manual recording by user request.");
        StopRecordingAndProcess();
    }

    public bool IsRecording => isRecording;
    
    #endregion

    // (StopRecordingAndProcess, GetCurrentRmsValue, SaveWavFile, OnDestroyは修正なし)
    private void StopRecordingAndProcess()
    {
        if (!isRecording && !Microphone.IsRecording(selectedMicrophoneDevice)) return;
        
        float duration = 0f;
        int recordedSamples = 0;
        if (Microphone.IsRecording(selectedMicrophoneDevice))
        {
            recordedSamples = Microphone.GetPosition(selectedMicrophoneDevice);
            Microphone.End(selectedMicrophoneDevice);
            if (recordedSamples > 0) duration = (float)recordedSamples / SAMPLE_RATE;
        }

        if (isRecording)
        {
            isRecording = false;
            OnRecordingStateChanged?.Invoke(false); // ★ Event
            // 録音終了時に一時フラグを退避（イベント通知用）
            bool wasAuto = isCurrentRecordingAuto;

            if (microphoneInput != null)
            {
                Debug.Log($"Stopping recording. Duration: {duration:F2}s, Samples: {recordedSamples}");
                
                if (duration <= 0.01f && microphoneInput != null)
                {
                     duration = microphoneInput.length;
                     recordedSamples = microphoneInput.samples;
                }

                // ★ 実際の録音長さにトリミングしてからWAVファイルを保存
                AudioClip trimmedClip = TrimAudioClip(microphoneInput, recordedSamples);
                AudioClip processedClip = NormalizeAudioClip(trimmedClip);
                SaveWavFile(audioFilePath, processedClip);
                
                mainUIManager.HideRecordingPanel(); 
                apiHandler.StartAnalysis(audioFilePath, wasAuto); // ★ 自動録音フラグを渡す
                
                // ★ イベント発火 (自動かどうかを通知)
                OnRecordingFinished?.Invoke(duration, wasAuto);
            }
            else
            {
                Debug.LogError("microphoneInput is null. Skipping save and analysis.");
                mainUIManager.HideRecordingPanel();
                OnBusyStateChanged?.Invoke(false); // ★ Analysis won't start, so clear busy
            }
        }
        else
        {
             mainUIManager.HideRecordingPanel();
        }
        
        CurrentRmsValue = 0f; // ★ 録音終了時にリセット

        // 自動録音が有効ならリスニング再開
        // (手動録音が割り込んでいた場合も、ここで自動リスニングに戻る)
        if (IsAutoRecordEnabled)
        {
            StartListening();
        }
    }
    
    private float GetCurrentRmsValue(AudioClip clip)
    {
        if (clip == null || !Microphone.IsRecording(selectedMicrophoneDevice))
        {
            if (isListening)
            {
                Debug.LogWarning("Microphone stopped while listening. Attempting to restart.");
                StopListening();
                if (IsAutoRecordEnabled) StartListening();
            }
            return 0f;
        }

        int micPosition = Microphone.GetPosition(selectedMicrophoneDevice);
        if (micPosition < samples.Length)
        {
            return 0f;
        }
        
        clip.GetData(samples, micPosition - samples.Length);
        
        float sum = 0f;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }
        float rawRms = Mathf.Sqrt(sum / samples.Length);
        
        // ★ ユーザー設定のゲインを適用
        return rawRms * inputGain;
    }

    /// <summary>
    /// AudioClipを実際の録音長さにトリミングします。
    /// Microphone.Startで確保した大きなバッファから、実際に録音された部分だけを抽出。
    /// </summary>
    /// <param name="clip">元のAudioClip</param>
    /// <param name="recordedSamples">実際に録音されたサンプル数</param>
    /// <returns>トリミングされたAudioClip</returns>
    private AudioClip TrimAudioClip(AudioClip clip, int recordedSamples)
    {
        if (clip == null) return null;
        
        // 全サンプルを使用する場合はそのまま返す
        if (recordedSamples <= 0 || recordedSamples >= clip.samples)
        {
            return clip;
        }
        
        // 実際に録音された部分だけを抽出
        int totalSamples = recordedSamples * clip.channels;
        float[] originalData = new float[clip.samples * clip.channels];
        clip.GetData(originalData, 0);
        
        float[] trimmedData = new float[totalSamples];
        System.Array.Copy(originalData, 0, trimmedData, 0, totalSamples);
        
        // 新しいAudioClipを作成
        AudioClip trimmedClip = AudioClip.Create(
            clip.name + "_trimmed",
            recordedSamples,
            clip.channels,
            clip.frequency,
            false
        );
        trimmedClip.SetData(trimmedData, 0);
        
        float originalDuration = clip.length;
        float trimmedDuration = (float)recordedSamples / clip.frequency;
        Debug.Log($"[Audio Trim] Original: {originalDuration:F2}s -> Trimmed: {trimmedDuration:F2}s (Saved {(originalDuration - trimmedDuration):F2}s)");
        
        return trimmedClip;
    }

    /// <summary>
    /// 音声データを正規化して音量を最適化します。
    /// ユーザー設定のゲインを基準に、必要に応じて追加の増幅を行います。
    /// </summary>
    /// <param name="clip">入力オーディオクリップ</param>
    /// <param name="targetPeak">目標ピークレベル（0.0〜1.0）</param>
    /// <returns>正規化されたオーディオクリップ</returns>
    private AudioClip NormalizeAudioClip(AudioClip clip, float targetPeak = 0.8f)
    {
        if (clip == null) return null;
        
        int totalSamples = clip.samples * clip.channels;
        float[] audioSamples = new float[totalSamples];
        clip.GetData(audioSamples, 0);
        
        // 1. 最大振幅を検出
        float maxAbs = 0f;
        for (int i = 0; i < audioSamples.Length; i++)
        {
            float abs = Mathf.Abs(audioSamples[i]);
            if (abs > maxAbs) maxAbs = abs;
        }
        
        // 2. 無音チェック（ほぼ無音ならスキップ）
        if (maxAbs < 0.001f)
        {
            Debug.Log("[Audio Normalize] Almost silent recording. Skipping normalization.");
            return clip;
        }
        
        // ★ 3. ユーザー設定のゲインを基準に計算
        float effectiveGain = inputGain;
        
        // 目標ピークに達していない場合のみ追加ゲイン
        float peakAfterUserGain = maxAbs * inputGain;
        if (peakAfterUserGain < targetPeak)
        {
            // ユーザーゲインの2倍までの追加増幅を許可
            effectiveGain = Mathf.Min(targetPeak / maxAbs, inputGain * 2f);
            Debug.Log($"[Audio Normalize] Peak={maxAbs:F4}, UserGain={inputGain:F1}x -> EffectiveGain={effectiveGain:F2}x");
        }
        else
        {
            Debug.Log($"[Audio Normalize] Peak={maxAbs:F4} with UserGain={inputGain:F1}x - Volume sufficient.");
            effectiveGain = inputGain;
        }
        
        // 4. ゲインを適用
        if (effectiveGain > 1.01f)
        {
            for (int i = 0; i < audioSamples.Length; i++)
            {
                // クリッピング防止
                audioSamples[i] = Mathf.Clamp(audioSamples[i] * effectiveGain, -1f, 1f);
            }
            
            // 新しいAudioClipを作成
            AudioClip normalizedClip = AudioClip.Create(
                clip.name + "_normalized",
                clip.samples,
                clip.channels,
                clip.frequency,
                false
            );
            normalizedClip.SetData(audioSamples, 0);
            return normalizedClip;
        }
        else
        {
            return clip; // 増幅不要
        }
    }
    
    private void SaveWavFile(string filepath, AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("SaveWavFile failed: AudioClip is null.");
            return;
        }
        try
        {
            WavUtility.FromAudioClip(clip, filepath, true);
            Debug.Log($"WAV file saved successfully: {filepath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"An exception occurred in WavUtility.FromAudioClip: {e.Message}\n{e.StackTrace}");
        }
    }
    


    void OnDestroy()
    {
        if (apiHandler != null)
        {
            apiHandler.OnAnalysisCompleted -= OnAnalysisCompleted;
        }
        StopAutoRecording();
    }
}