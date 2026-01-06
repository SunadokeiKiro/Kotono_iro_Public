using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using UnityEngine.SceneManagement;

public class ButtonScript : MonoBehaviour
{
    [Header("GameController Reference")]
    public GameController gameController;

    [Header("UI Elements (Assign in Inspector)")]
    public Button recButton;
    public Button settingsButton;
    
    [Header("Auto Recording Settings")]
    public Button autoRecordOnButton;
    public Button autoRecordOffButton;
    public Text autoRecordStatusText;
    
    private string apiUrl = "https://acp-api-async.amivoice.com/v1/recognitions";
    private string audioFilePath;
    private string sessionID;
    private string microphone;
    private AudioClip microphoneInput;
    private int RECORD_LENGTH_SEC = 20; 
    private const int SAMPLE_RATE = 41100; 
    
    private bool isAutoRecordEnabled = false;
    private bool isListeningForVoice = false;
    private bool isRecording = false;
    private float[] samples = new float[128]; 
    private float lastVoiceDetectedTime = 0f;
    
    private float voiceDetectionThreshold = 0.02f;
    private float silenceDetectionTime = 2.0f;
    private string selectedMicrophone = "";

    void Start()
    {
        if (recButton == null) 
        {
            Debug.LogError("ButtonScript: InspectorにUI要素が正しく割り当てられていません。");
            return;
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OpenSettingsScene);
        }

        LoadSettings(); 

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("ButtonScript: マイクが見つかりません。");
            microphone = null; 
            if (recButton != null) recButton.interactable = false;
            if (autoRecordOnButton != null) autoRecordOnButton.interactable = false;
            return;
        }
        
        if (!string.IsNullOrEmpty(selectedMicrophone) && Array.IndexOf(Microphone.devices, selectedMicrophone) >= 0)
        {
            microphone = selectedMicrophone;
        }
        else
        {
            microphone = Microphone.devices[0]; 
        }
        Debug.Log("ButtonScript: 使用マイク: " + microphone);

        #if UNITY_ANDROID && !UNITY_EDITOR
            audioFilePath = Path.Combine(Application.persistentDataPath, "test.wav");
        #elif UNITY_IOS && !UNITY_EDITOR
            audioFilePath = Path.Combine(Application.persistentDataPath, "test.wav");
        #else
            audioFilePath = Path.Combine(Application.persistentDataPath, "test.wav");
        #endif
        Debug.Log("ButtonScript: オーディオファイルパス: " + audioFilePath);

        recButton.onClick.AddListener(() => StartCoroutine(StartRec()));
        
        if (autoRecordOnButton != null && autoRecordOffButton != null)
        {
            autoRecordOnButton.onClick.AddListener(StartAutoRecording);
            autoRecordOffButton.onClick.AddListener(StopAutoRecording);
            autoRecordOffButton.gameObject.SetActive(false);
            autoRecordOnButton.gameObject.SetActive(true);
            UpdateAutoRecordStatus();
        }
        else
        {
            Debug.LogWarning("ButtonScript: 自動録音ボタンがInspectorにアタッチされていません。");
        }
    }
    
    void LoadSettings()
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
                RECORD_LENGTH_SEC = settings.recordLengthSec; 
                selectedMicrophone = settings.selectedMicrophone;
                
                Debug.Log($"ButtonScript: マイク設定読み込み: 閾値={voiceDetectionThreshold}, 無音時間={silenceDetectionTime}, 録音長={RECORD_LENGTH_SEC}s, マイク={selectedMicrophone}");
            }
            else
            {
                Debug.Log("ButtonScript: 設定ファイルが見つからないためデフォルト値を使用します。RECORD_LENGTH_SECは初期値の " + RECORD_LENGTH_SEC + " のままです。");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("ButtonScript: 設定の読み込みに失敗: " + e.Message);
        }
    }
    
    public void StartAutoRecording()
    {
        if (microphone == null) {
            Debug.LogError("ButtonScript: マイクが利用できないため自動録音を開始できません。");
            return;
        }
        isAutoRecordEnabled = true;
        if(autoRecordOnButton != null) autoRecordOnButton.gameObject.SetActive(false);
        if(autoRecordOffButton != null) autoRecordOffButton.gameObject.SetActive(true);
        UpdateAutoRecordStatus();
        StartListeningForVoice();
    }
    
    public void StopAutoRecording()
    {
        isAutoRecordEnabled = false;
        if(autoRecordOffButton != null) autoRecordOffButton.gameObject.SetActive(false);
        if(autoRecordOnButton != null) autoRecordOnButton.gameObject.SetActive(true);
        UpdateAutoRecordStatus();
        StopListeningForVoice();
        if (isRecording) StopRecording();
    }
    
    void UpdateAutoRecordStatus()
    {
        if (autoRecordStatusText != null)
        {
            autoRecordStatusText.text = isAutoRecordEnabled ? "自動録音: オン" : "自動録音: オフ";
            autoRecordStatusText.color = isAutoRecordEnabled ? Color.green : Color.red;
        }
    }
    
    void StartListeningForVoice()
    {
        if (microphone == null || !isAutoRecordEnabled || isListeningForVoice || isRecording) return;

        isListeningForVoice = true;
        Debug.Log("ButtonScript: 音声検出リスニングを開始します。");
        AudioClip listeningClip = Microphone.Start(microphone, true, 1, SAMPLE_RATE); 
        if (listeningClip == null) {
            Debug.LogError("ButtonScript: マイクの起動に失敗しました (音声検出用)。");
            isListeningForVoice = false;
            return;
        }
        StartCoroutine(MonitorAudioLevel(listeningClip));
    }
    
    void StopListeningForVoice()
    {
        if (microphone != null && Microphone.IsRecording(microphone) && isListeningForVoice) 
        {
            Microphone.End(microphone);
            Debug.Log("ButtonScript: 音声検出リスニングを停止しました。");
        }
        isListeningForVoice = false; 
    }
    
    IEnumerator MonitorAudioLevel(AudioClip clip)
    {
        if (clip == null) {
             Debug.LogError("ButtonScript: MonitorAudioLevelに渡されたAudioClipがnullです。");
             isListeningForVoice = false; 
             yield break;
        }

        yield return new WaitForSeconds(0.1f); 

        while (isListeningForVoice && isAutoRecordEnabled && !isRecording)
        {
            if (!Microphone.IsRecording(microphone)) {
                Debug.LogWarning("ButtonScript: モニタリング中にマイクが停止しました。リスニングを再試行します。");
                isListeningForVoice = false; 
                StartListeningForVoice(); 
                yield break; 
            }

            int micPosition = Microphone.GetPosition(microphone);
            if (micPosition < samples.Length) { 
                 yield return null;
                 continue;
            }

            clip.GetData(samples, micPosition - samples.Length); 
            
            float rmsValue = 0f;
            foreach (float sample in samples) rmsValue += sample * sample;
            rmsValue = Mathf.Sqrt(rmsValue / samples.Length);
            
            if (rmsValue > voiceDetectionThreshold)
            {
                Debug.Log("ButtonScript: 音声検出 (レベル: " + rmsValue + ")。自動録音を開始します。");
                StopListeningForVoice(); 
                StartCoroutine(StartAutomaticRecording());
                yield break; 
            }
            yield return null;
        }
        isListeningForVoice = false; 
    }
    
    IEnumerator StartAutomaticRecording()
    {
        if (isRecording || !isAutoRecordEnabled || microphone == null) yield break;

        isRecording = true;
        Debug.Log("ButtonScript: 自動録音を開始します。(" + RECORD_LENGTH_SEC + "秒間)");
        
        microphoneInput = Microphone.Start(microphone, false, RECORD_LENGTH_SEC, SAMPLE_RATE); 
        if (microphoneInput == null) {
            Debug.LogError("ButtonScript: マイクの起動に失敗しました (自動録音用)。");
            isRecording = false;
            if (isAutoRecordEnabled) StartListeningForVoice(); 
            yield break;
        }
        
        lastVoiceDetectedTime = Time.time;
        StartCoroutine(MonitorSilenceDuringRecording()); 
    }
    
    IEnumerator MonitorSilenceDuringRecording() 
    {
        if (microphoneInput == null) {
            Debug.LogError("ButtonScript: MonitorSilenceDuringRecordingに渡されたmicrophoneInputがnullです。");
            if (isRecording) StopRecording(); 
            yield break;
        }
        bool initialVoiceDetected = false; 
        float recordingStartTime = Time.time;

        while (isRecording && isAutoRecordEnabled)
        {
            if (!Microphone.IsRecording(microphone)) { 
                Debug.LogWarning("ButtonScript: 自動録音中にマイクが停止しました。");
                StopRecording(); 
                yield break;
            }

            if (Time.time - recordingStartTime >= RECORD_LENGTH_SEC) {
                Debug.Log("ButtonScript: 最大録音時間に達しました。録音を終了します。");
                StopRecording();
                yield break;
            }

            int micPosition = Microphone.GetPosition(microphone);
            if (micPosition < samples.Length) {
                 yield return null;
                 continue;
            }
            microphoneInput.GetData(samples, micPosition - samples.Length);
            
            float rmsValue = 0f;
            foreach (float sample in samples) rmsValue += sample * sample;
            rmsValue = Mathf.Sqrt(rmsValue / samples.Length);
            
            if (rmsValue > voiceDetectionThreshold)
            {
                lastVoiceDetectedTime = Time.time;
                initialVoiceDetected = true; 
            }
            else if (initialVoiceDetected && (Time.time - lastVoiceDetectedTime > silenceDetectionTime))
            {
                Debug.Log("ButtonScript: 一定時間無音を検出。録音を停止します。");
                StopRecording();
                yield break;
            }
            yield return null;
        }
    }
    
    void StopRecording()
    {
        if (!isRecording && !Microphone.IsRecording(microphone)) return; 

        if (Microphone.IsRecording(microphone)) 
        {
            Microphone.End(microphone);
            Debug.Log("ButtonScript: 録音を停止しました。");
        }

        if (isRecording) 
        {
             isRecording = false; 
            if (microphoneInput != null) 
            {
                SaveWavFile(audioFilePath, microphoneInput);
                Debug.Log("ButtonScript: 自動録音完了 - 解析開始");
                StartCoroutine(ReadApiKeyAndPostRequest());
            } else {
                Debug.LogError("ButtonScript: microphoneInputがnullのため、WAV保存とAPIリクエストをスキップします。");
            }
        }
        
        if (isAutoRecordEnabled)
        {
            StartListeningForVoice(); 
        }
    }

    void OpenSettingsScene()
    {
        SceneManager.LoadScene("SettingsScene");
    }

    private string LoadApiKey()
    {
        string apiKey = "";
        string keyPath = Path.Combine(Application.persistentDataPath, "apikey.txt");
        try
        {
            if (File.Exists(keyPath))
            {
                apiKey = File.ReadAllText(keyPath).Trim();
                Debug.Log("ButtonScript: 保存されたAPIキーを読み込みました。");
            }
            else
            {
                Debug.LogError("ButtonScript: APIキーファイルが見つかりません: " + keyPath);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("ButtonScript: APIキーの読み込み失敗: " + e.Message);
        }
        return apiKey;
    }

    IEnumerator ReadApiKeyAndPostRequest()
    {
        Debug.Log("ButtonScript: APIリクエスト準備中...");
        string apiKey = LoadApiKey();
        
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("ButtonScript: APIキーが空のためリクエスト中止。");
            yield break;
        }
        
        if (!File.Exists(audioFilePath)) {
            Debug.LogError($"ButtonScript: 音声ファイルが見つかりません: {audioFilePath}");
            yield break;
        }

        yield return StartCoroutine(PostRequest(apiKey));
    }

    IEnumerator PostRequest(string apiKey)
    {
        Debug.Log("ButtonScript: APIへリクエスト送信中...");
        List<IMultipartFormSection> form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("u", apiKey),
            new MultipartFormDataSection("d", "grammarFileNames=-a-general loggingOptOut=True sentimentAnalysis=True"),
            new MultipartFormDataSection("c", "LSB44K") 
        };

        byte[] audioData;
        try {
            audioData = File.ReadAllBytes(audioFilePath);
        } catch (Exception e) {
            Debug.LogError($"ButtonScript: 音声ファイルの読み込みに失敗: {audioFilePath}, Error: {e.Message}");
            yield break;
        }
        
        form.Add(new MultipartFormFileSection("a", audioData, Path.GetFileName(audioFilePath), "audio/wav"));

        using (UnityWebRequest request = UnityWebRequest.Post(apiUrl, form))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("ButtonScript: APIリクエスト成功: " + request.downloadHandler.text);
                Save(request.downloadHandler.text); 

                RecognitionResponse recogResponse = null;
                try {
                    recogResponse = JsonUtility.FromJson<RecognitionResponse>(request.downloadHandler.text);
                } catch (Exception e) {
                    Debug.LogError("ButtonScript: RecognitionResponseのJSONパース失敗: " + e.Message);
                    yield break;
                }

                if (recogResponse != null && !string.IsNullOrEmpty(recogResponse.sessionid)) {
                    sessionID = recogResponse.sessionid;
                    Debug.Log("ButtonScript: sessionID: " + sessionID);
                    StartCoroutine(PollJobStatus(apiKey));
                } else {
                     Debug.LogError("ButtonScript: sessionIDの取得に失敗しました。レスポンス: " + request.downloadHandler.text);
                }
            }
            else
            {
                Debug.LogError($"ButtonScript: APIリクエスト失敗: {request.error}, Code: {request.responseCode}, Body: {request.downloadHandler?.text}");
            }
        }
    }

    IEnumerator PollJobStatus(string apiKey)
    {
        if (string.IsNullOrEmpty(sessionID)) {
            Debug.LogError("ButtonScript: PollJobStatus - sessionIDが空です。");
            yield break;
        }

        Debug.Log("ButtonScript: 解析結果待機中...");
        string pollUrl = $"https://acp-api-async.amivoice.com/v1/recognitions/{sessionID}";
        int pollCount = 0; // ポーリング回数カウンターを再導入
        int maxPolls = 100; // ポーリング回数の上限を100回に設定

        while (pollCount < maxPolls) // ループ条件を修正
        {
            using (UnityWebRequest request = UnityWebRequest.Get(pollUrl))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    SentimentAnalysisResponse sentimentResponse = null;
                    try {
                        sentimentResponse = JsonUtility.FromJson<SentimentAnalysisResponse>(request.downloadHandler.text);
                    } catch (Exception e) {
                        Debug.LogError("ButtonScript: SentimentAnalysisResponseのJSONパース失敗: " + e.Message + "\nResponse: " + request.downloadHandler.text);
                        yield break;
                    }
                    
                    if (sentimentResponse == null) {
                        Debug.LogError("ButtonScript: SentimentAnalysisResponseがnullです。パース失敗の可能性。");
                        yield break;
                    }

                    Debug.Log("ButtonScript: ジョブ状態: " + sentimentResponse.status);
                    if (sentimentResponse.status == "completed")
                    {
                        Debug.Log("ButtonScript: 音声認識・感情分析完了！");
                        Save(request.downloadHandler.text); 

                        if (gameController != null)
                        {
                            gameController.SetParametersFromJson(request.downloadHandler.text);
                            Debug.Log("ButtonScript: 感情パラメータをGameControllerに送信しました。");
                        }
                        else Debug.LogError("ButtonScript: GameControllerが見つかりません！");

                        StringBuilder sb = new StringBuilder();
                        if (sentimentResponse.segments != null) 
                        {
                            foreach (Segment segText in sentimentResponse.segments)
                            {
                                if(segText.results != null) {
                                    foreach (Result res in segText.results)
                                    {
                                        if(res.tokens != null) {
                                            foreach (Token tok in res.tokens)
                                            {
                                                sb.Append(tok.written);
                                                if (!tok.written.Equals("。") && !tok.written.Equals("、")) { 
                                                    sb.Append(" ");
                                                }
                                            }
                                        }
                                        sb.Append("\n"); 
                                    }
                                } else if (!string.IsNullOrEmpty(segText.text)){ 
                                     sb.Append(segText.text).Append("\n");
                                }
                            }
                        } else if (!string.IsNullOrEmpty(sentimentResponse.text)) { 
                            sb.Append(sentimentResponse.text);
                        }
                        
                        Debug.Log($"ButtonScript: 認識結果テキスト:\n{sb.ToString().Trim()}");

                        if (sentimentResponse.sentiment_analysis != null && sentimentResponse.sentiment_analysis.segments != null)
                        {
                            SentimentSegment sumSegments = new SentimentSegment(); 
                            int cnt = 0;
                            foreach (SentimentSegment segSA in sentimentResponse.sentiment_analysis.segments) 
                            {
                                sumSegments.energy += segSA.energy;
                                sumSegments.content += segSA.content;
                                sumSegments.upset += segSA.upset;
                                sumSegments.aggression += segSA.aggression;
                                sumSegments.stress += segSA.stress;
                                sumSegments.uncertainty += segSA.uncertainty;
                                sumSegments.excitement += segSA.excitement;
                                sumSegments.concentration += segSA.concentration;
                                sumSegments.emo_cog += segSA.emo_cog;
                                sumSegments.hesitation += segSA.hesitation;
                                sumSegments.brain_power += segSA.brain_power;
                                sumSegments.embarrassment += segSA.embarrassment;
                                sumSegments.intensive_thinking += segSA.intensive_thinking;
                                sumSegments.imagination_activity += segSA.imagination_activity;
                                sumSegments.extreme_emotion += segSA.extreme_emotion;
                                sumSegments.passionate += segSA.passionate;
                                sumSegments.atmosphere += segSA.atmosphere;
                                sumSegments.anticipation += segSA.anticipation;
                                sumSegments.dissatisfaction += segSA.dissatisfaction;
                                sumSegments.confidence += segSA.confidence;
                                cnt++;
                            }
                            if (cnt > 0) Ave(sumSegments, cnt); 
                            else Debug.LogWarning("ButtonScript: 感情分析セグメント数が0でした。");
                        } else {
                             Debug.LogWarning("ButtonScript: sentiment_analysis またはそのsegmentsがnullです。");
                        }
                        yield break; 
                    }
                    else if (sentimentResponse.status == "error")
                    {
                        Debug.LogError($"ButtonScript: ジョブエラー: {sentimentResponse.message} (Code: {sentimentResponse.code})");
                        yield break;
                    }
                    else
                    {
                        Debug.Log($"ButtonScript: 解析中... (ステータス: {sentimentResponse.status}, ポーリング: {pollCount + 1}/{maxPolls})");
                    }
                }
                else
                {
                    Debug.LogError($"ButtonScript: ポーリング失敗: {request.error}, Code: {request.responseCode}, Body: {request.downloadHandler?.text}");
                    yield break;
                }
            }
            pollCount++; // カウンターをインクリメント
            yield return new WaitForSeconds(4f); 
        }

        // ループ終了後（maxPollsに達した場合）のタイムアウト処理
        if (pollCount >= maxPolls) {
            Debug.LogWarning("ButtonScript: 最大ポーリング回数(100回)に達しました。タイムアウトと見なします。");
        }
    }

    IEnumerator StartRec()
    {
        if (microphone == null) {
            Debug.LogError("ButtonScript: マイクが利用できないため録音を開始できません。");
            yield break;
        }

        if (RECORD_LENGTH_SEC <= 0)
        {
            Debug.LogError("ButtonScript: 録音時間が0以下のため録音を開始できません。設定を確認してください。 RECORD_LENGTH_SEC: " + RECORD_LENGTH_SEC);
            yield break;
        }

        microphoneInput = Microphone.Start(microphone, false, RECORD_LENGTH_SEC, SAMPLE_RATE);
        if (microphoneInput == null) {
            Debug.LogError("ButtonScript: マイクの起動に失敗しました (手動録音用)。");
            yield break;
        }

        Debug.Log("ButtonScript: 手動録音を開始します (" + RECORD_LENGTH_SEC + "秒間)");
        StartCoroutine(WaitAndExecuteRecording()); 
    }

    IEnumerator WaitAndExecuteRecording()
    {
        yield return new WaitForSeconds(RECORD_LENGTH_SEC);
        
        if (Microphone.IsRecording(microphone)) 
        {
            Microphone.End(microphone);
        }
        Debug.Log("ButtonScript: 手動録音を終了し、WAVファイルに保存します。");

        if (microphoneInput != null) {
            SaveWavFile(audioFilePath, microphoneInput);
            if (File.Exists(audioFilePath)) { 
                Debug.Log("ButtonScript: 録音完了 - 解析開始");
                StartCoroutine(ReadApiKeyAndPostRequest()); 
            } else {
                Debug.LogError($"ButtonScript: 保存されたWAVファイルが見つかりません: {audioFilePath}。APIリクエストを中止します。");
            }
        } else {
            Debug.LogError("ButtonScript: microphoneInputがnullのため、手動録音のWAV保存とAPIリクエストをスキップします。");
        }
    }

    private void SaveWavFile(string filepath, AudioClip clip)
    {
        if (clip == null) {
            Debug.LogError("ButtonScript: SaveWavFile - AudioClipがnullです。");
            return;
        }
        try
        {
            Debug.Log($"ButtonScript: WAVファイルを保存中: {filepath}");
            byte[] wavBytes = WavUtility.FromAudioClip(clip, filepath, true);
            if (wavBytes == null || wavBytes.Length == 0) {
                Debug.LogError("ButtonScript: WAVファイルの生成に失敗しました。");
            } else {
                 if (!File.Exists(filepath))
                {
                     Debug.LogError($"ButtonScript: WAVファイルの保存に失敗したようです。ファイルが存在しません: {filepath}");
                } else {
                     Debug.Log($"ButtonScript: WAVファイル保存完了 ({wavBytes.Length} bytes) at {filepath}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ButtonScript: SaveWavFileで例外発生: {e.Message}\n{e.StackTrace}");
        }
    }

    void Ave(SentimentSegment sumRes, int count) 
    {
        if (count == 0)
        {
            Debug.LogWarning("ButtonScript: Ave - countが0のため平均計算をスキップします。");
            return;
        }

        Debug.Log("ButtonScript: 平均感情値:");
        Debug.Log($"エネルギー: {Calc(sumRes.energy, count):F2}");
        Debug.Log($"よろこび: {Calc(sumRes.content, count):F2}");
        Debug.Log($"動揺: {Calc(sumRes.upset, count):F2}");
        Debug.Log($"攻撃性: {Calc(sumRes.aggression, count):F2}");
        Debug.Log($"ストレス: {Calc(sumRes.stress, count):F2}");
        Debug.Log($"不確実性: {Calc(sumRes.uncertainty, count):F2}");
        Debug.Log($"興奮: {Calc(sumRes.excitement, count):F2}");
        Debug.Log($"集中: {Calc(sumRes.concentration, count):F2}");
        Debug.Log($"感情バランス: {Calc(sumRes.emo_cog, count):F2}");
        Debug.Log($"ためらい: {Calc(sumRes.hesitation, count):F2}");
        Debug.Log($"脳活動: {Calc(sumRes.brain_power, count):F2}");
        Debug.Log($"困惑: {Calc(sumRes.embarrassment, count):F2}");
        Debug.Log($"思考: {Calc(sumRes.intensive_thinking, count):F2}");
        Debug.Log($"想像力: {Calc(sumRes.imagination_activity, count):F2}");
        Debug.Log($"極端な起伏: {Calc(sumRes.extreme_emotion, count):F2}");
        Debug.Log($"情熱: {Calc(sumRes.passionate, count):F2}");
        Debug.Log($"雰囲気: {Calc(sumRes.atmosphere, count):F2}");
        Debug.Log($"期待: {Calc(sumRes.anticipation, count):F2}");
        Debug.Log($"不満: {Calc(sumRes.dissatisfaction, count):F2}");
        Debug.Log($"自信: {Calc(sumRes.confidence, count):F2}");
    }

    void Save(string data)
    {
        try
        {
            string filePath = Path.Combine(Application.persistentDataPath, "log.txt");
            File.WriteAllText(filePath, data); 
            Debug.Log("ButtonScript: ログデータを保存しました: " + filePath);
        }
        catch (Exception e)
        {
            Debug.LogError("ButtonScript: ログデータの保存に失敗: " + e.Message);
        }
    }

    float Calc(int num, int div) 
    {
        if (div == 0) return 0f;
        return (float)num / div;
    }
    
    void OnDestroy()
    {
        StopAllCoroutines();
        if (microphone != null) { 
            if (isListeningForVoice && Microphone.IsRecording(microphone))
            {
                Microphone.End(microphone);
            }
            if (isRecording && Microphone.IsRecording(microphone))
            {
                Microphone.End(microphone);
            }
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            if (microphone != null && Microphone.IsRecording(microphone))
            {
                Debug.Log("ButtonScript: アプリケーション一時停止。マイクの状態を確認してください。");
            }
        }
        else
        {
            Debug.Log("ButtonScript: アプリケーション再開。");
        }
    }
}