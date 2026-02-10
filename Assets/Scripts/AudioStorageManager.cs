// Scripts/AudioStorageManager.cs
// 録音音声のローカル保存・読み込み・自動削除を管理するマネージャー
using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 録音音声ファイルの保存、読み込み、および期限切れファイルの自動削除を管理します。
/// 保存パス: persistentDataPath/recordings/{yyyy-MM}/{timestamp}.wav
/// </summary>
public class AudioStorageManager : MonoBehaviour
{
    public static AudioStorageManager Instance { get; private set; }

    // 猶予期間 (プラン別閲覧期間 + この値)
    private const int GRACE_PERIOD_MONTHS = 3;

    private string recordingsBasePath;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            recordingsBasePath = Path.Combine(Application.persistentDataPath, "recordings");
            EnsureDirectoryExists(recordingsBasePath);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 録音ファイルを永続ストレージに保存し、ファイル名を返します。
    /// </summary>
    /// <param name="sourceWavPath">元のWAVファイルパス (一時ファイル)</param>
    /// <param name="monthKey">yyyy-MM形式の月キー</param>
    /// <param name="timestamp">Unixタイムスタンプ</param>
    /// <returns>保存されたファイル名 (例: "1698765432.wav")</returns>
    public string SaveRecording(string sourceWavPath, string monthKey, long timestamp)
    {
        if (string.IsNullOrEmpty(sourceWavPath) || !File.Exists(sourceWavPath))
        {
            Debug.LogError($"[AudioStorageManager] Source file not found: {sourceWavPath}");
            return null;
        }

        try
        {
            // 月別ディレクトリを作成
            string monthDir = Path.Combine(recordingsBasePath, monthKey);
            EnsureDirectoryExists(monthDir);

            // ファイル名: タイムスタンプ.wav
            string fileName = $"{timestamp}.wav";
            string destPath = Path.Combine(monthDir, fileName);

            // ファイルをコピー
            File.Copy(sourceWavPath, destPath, overwrite: true);
            Debug.Log($"[AudioStorageManager] Recording saved: {destPath}");

            return fileName;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioStorageManager] Failed to save recording: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 録音ファイルを読み込み、AudioClipとして返します。
    /// </summary>
    /// <param name="monthKey">yyyy-MM形式の月キー</param>
    /// <param name="audioFileName">ファイル名 (例: "1698765432.wav")</param>
    /// <param name="callback">読み込み完了時のコールバック (AudioClipまたはnull)</param>
    public void LoadRecording(string monthKey, string audioFileName, Action<AudioClip> callback)
    {
        if (string.IsNullOrEmpty(audioFileName))
        {
            callback?.Invoke(null);
            return;
        }

        string filePath = GetRecordingPath(monthKey, audioFileName);
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[AudioStorageManager] Recording not found: {filePath}");
            callback?.Invoke(null);
            return;
        }

        StartCoroutine(LoadAudioClipCoroutine(filePath, callback));
    }

    private IEnumerator LoadAudioClipCoroutine(string filePath, Action<AudioClip> callback)
    {
        // file:// プレフィックスを付与
        string fileUrl = "file:///" + filePath.Replace("\\", "/");
        
        using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.WAV))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                callback?.Invoke(clip);
            }
            else
            {
                Debug.LogError($"[AudioStorageManager] Failed to load audio: {www.error}");
                callback?.Invoke(null);
            }
        }
    }

    /// <summary>
    /// 録音ファイルが存在するか確認します。
    /// </summary>
    public bool RecordingExists(string monthKey, string audioFileName)
    {
        if (string.IsNullOrEmpty(audioFileName)) return false;
        string filePath = GetRecordingPath(monthKey, audioFileName);
        return File.Exists(filePath);
    }

    /// <summary>
    /// 録音の再生可否を判定します。
    /// </summary>
    /// <param name="monthKey">yyyy-MM形式の月キー</param>
    /// <returns>再生可能な場合true、期限切れの場合false</returns>
    public bool CanPlayRecording(string monthKey)
    {
        if (SubscriptionManager.Instance == null) return false;
        return SubscriptionManager.Instance.CanAccessMonth(monthKey);
    }

    /// <summary>
    /// 録音ファイルのフルパスを取得します。
    /// </summary>
    public string GetRecordingPath(string monthKey, string audioFileName)
    {
        return Path.Combine(recordingsBasePath, monthKey, audioFileName);
    }

    /// <summary>
    /// 期限切れの録音ファイルを削除します。
    /// プラン別閲覧期間 + 猶予期間 を超過したファイルを対象とします。
    /// </summary>
    public void CleanupExpiredRecordings()
    {
        if (SubscriptionManager.Instance == null)
        {
            Debug.LogWarning("[AudioStorageManager] SubscriptionManager not available. Skipping cleanup.");
            return;
        }

        try
        {
            int allowedMonths = SubscriptionManager.Instance.GetAllowedHistoryMonths();
            
            // 無制限プランは削除対象なし
            if (allowedMonths == int.MaxValue)
            {
                Debug.Log("[AudioStorageManager] Unlimited plan - no cleanup needed.");
                return;
            }

            // 削除対象期間: 閲覧可能期間 + 猶予期間
            int totalRetentionMonths = allowedMonths + GRACE_PERIOD_MONTHS;
            
            DateTime now = TimeManager.Instance != null 
                ? TimeManager.Instance.GetCurrentJstTime() 
                : DateTime.Now;

            // 削除閾値を計算
            DateTime threshold = now.AddMonths(-totalRetentionMonths);
            string thresholdKey = threshold.ToString("yyyy-MM");

            Debug.Log($"[AudioStorageManager] Cleanup: Plan allows {allowedMonths} months + {GRACE_PERIOD_MONTHS} grace = {totalRetentionMonths} total. Threshold: {thresholdKey}");

            // 月別ディレクトリを列挙
            if (!Directory.Exists(recordingsBasePath)) return;

            var monthDirs = Directory.GetDirectories(recordingsBasePath);
            int deletedCount = 0;

            foreach (var dir in monthDirs)
            {
                string monthKey = Path.GetFileName(dir);
                
                // yyyy-MM形式かチェック
                if (!IsValidMonthKey(monthKey)) continue;

                // 閾値より古ければ削除
                if (string.Compare(monthKey, thresholdKey) < 0)
                {
                    Debug.Log($"[AudioStorageManager] Deleting expired recordings: {monthKey}");
                    Directory.Delete(dir, recursive: true);
                    deletedCount++;
                }
            }

            Debug.Log($"[AudioStorageManager] Cleanup completed. Deleted {deletedCount} month(s) of recordings.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioStorageManager] Cleanup failed: {e.Message}");
        }
    }

    /// <summary>
    /// 文字列が有効なyyyy-MM形式かチェックします。
    /// </summary>
    private bool IsValidMonthKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length != 7) return false;
        return DateTime.TryParseExact(key, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out _);
    }

    /// <summary>
    /// ディレクトリが存在しない場合は作成します。
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <summary>
    /// 指定月の録音ファイル一覧を取得します。
    /// </summary>
    public List<string> GetRecordingsForMonth(string monthKey)
    {
        string monthDir = Path.Combine(recordingsBasePath, monthKey);
        if (!Directory.Exists(monthDir)) return new List<string>();

        return Directory.GetFiles(monthDir, "*.wav")
            .Select(Path.GetFileName)
            .ToList();
    }

    /// <summary>
    /// 使用中のストレージ容量を取得します (バイト単位)。
    /// </summary>
    public long GetTotalStorageUsed()
    {
        if (!Directory.Exists(recordingsBasePath)) return 0;

        return Directory.GetFiles(recordingsBasePath, "*.wav", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }
}
