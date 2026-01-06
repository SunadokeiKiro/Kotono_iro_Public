using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

/// <summary>
/// ネットワーク経由で正確な時刻を取得するマネージャ。
/// 日本時間 (JST) を基準とします。
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    private DateTime? networkTime = null;
    private float realtimeSinceStartupAtNetworkTime;
    
    // 日本標準時 (JST) のタイムゾーン情報
    // private static readonly TimeZoneInfo JstZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    // ↑ This causes crash on Android because ID differs ("Asia/Tokyo"). 
    // Since we just need JST, fixed offset is safer and faster.

    public bool IsTimeSynced => networkTime.HasValue;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 現在の日本時間 (JST) を取得します。
    /// 同期できていない場合は、警告を出しつつローカル時刻をJSTに変換して返します（不正リスクあり）。
    /// </summary>
    public DateTime GetCurrentJstTime()
    {
        DateTime utcNow;

        if (networkTime.HasValue)
        {
            // 取得したネットワーク時刻 + 経過時間
            float elapsed = Time.realtimeSinceStartup - realtimeSinceStartupAtNetworkTime;
            utcNow = networkTime.Value.AddSeconds(elapsed);
        }
        else
        {
            Debug.LogWarning("[TimeManager] Time not synced yet. Using System Time (Unsafe for strictly managed apps).");
            utcNow = DateTime.UtcNow;
        }

        // UTC -> JST 変換 (UTC + 9 hours)
        return utcNow.AddHours(9);
    }

    /// <summary>
    /// サーバー(Google)から時刻を取得して同期します。
    /// </summary>
    public IEnumerator SyncTimeWithServer(Action<bool> onComplete)
    {
        Debug.Log("[TimeManager] Syncing time with server...");
        
        // GoogleへのHEADリクエストでDateヘッダーを取得するのが軽量で高速
        using (UnityWebRequest request = UnityWebRequest.Head("https://www.google.com"))
        {
            request.timeout = 5; // 5秒タイムアウト
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string dateStr = request.GetResponseHeader("Date");
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out DateTime dt))
                {
                    networkTime = dt.ToUniversalTime();
                    realtimeSinceStartupAtNetworkTime = Time.realtimeSinceStartup;
                    Debug.Log($"[TimeManager] Time Synced! UTC: {networkTime}, JST: {GetCurrentJstTime()}");
                    onComplete?.Invoke(true);
                }
                else
                {
                    Debug.LogError("[TimeManager] Failed to parse Date header.");
                    onComplete?.Invoke(false);
                }
            }
            else
            {
                Debug.LogError($"[TimeManager] Network Error: {request.error}");
                onComplete?.Invoke(false);
            }
        }
    }
}
