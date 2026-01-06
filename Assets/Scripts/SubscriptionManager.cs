// Scripts/SubscriptionManager.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum PlanType
{
    Free,
    Standard,
    Premium,
    Ultimate
}

/// <summary>
/// ユーザーの契約プランと権限を管理するマネージャー。
/// Cloud Firestoreとの連携を追加。
/// </summary>
public class SubscriptionManager : MonoBehaviour
{
    public static SubscriptionManager Instance { get; private set; }

    [Header("Debug Settings")]
    [Tooltip("現在のプラン (デバッグ用)")]
    [SerializeField] private PlanType currentPlan = PlanType.Free;

    // --- Quota System ---
    
    [System.Serializable]
    private class SubscriptionStatus
    {
        public int year;
        public int month;
        public float usedSeconds;
    }

    private SubscriptionStatus currentStatus;
    private string statusFilePath;

    // プランごとの月間制限時間 (秒)
    // Free: 3分, Standard: 60分, Premium/Enterprise: 3時間
    private readonly Dictionary<PlanType, float> planQuotas = new Dictionary<PlanType, float>
    {
        { PlanType.Free,       180f   },   // 3分
        { PlanType.Standard,   3600f  },   // 60分
        { PlanType.Premium,    10800f },   // 180分
        { PlanType.Ultimate,   28800f }    // 480分
    };

    // Events for UI Blocking
    public event System.Action OnSyncStart;
    public event System.Action OnSyncEnd;
    public bool IsSyncing { get; private set; } = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            statusFilePath = System.IO.Path.Combine(Application.persistentDataPath, "subscription_status.json");
            LoadSubscriptionStatus();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        Debug.Log($"[SubscriptionManager] Initialized. Plan: {currentPlan}, Quota: {GetMonthlyQuotaSeconds()}s, Used: {currentStatus.usedSeconds:F1}s");
        
        // Wait for Firebase to initialize before syncing
        StartCoroutine(InitializeAndSync());
    }

    private IEnumerator InitializeAndSync()
    {
        // Wait up to 10 seconds for Firebase Config
        float timeout = Time.time + 10f;
        while ((FirebaseConfig.Instance == null || !FirebaseConfig.Instance.IsInitialized) && Time.time < timeout)
        {
            yield return null;
        }

        if (FirebaseConfig.Instance != null && FirebaseConfig.Instance.IsInitialized)
        {
            // 1. Sync Plan First
            yield return RefreshPlanFromServer(null);

            // 2. Sync Quota
            yield return SyncQuotaWithServer(null);
        }
        else
        {
            Debug.LogError("[SubscriptionManager] Firebase initialization timed out. Skipping initial sync.");
        }
    }

    /// <summary>
    /// Firestoreから最新のプラン情報を再取得し、ローカル状態を更新します。
    /// 購入直後など、プラン変更が予想されるタイミングで呼び出してください。
    /// </summary>
    public IEnumerator RefreshPlanFromServer(System.Action<bool> onComplete)
    {
        bool planSynced = false;
        bool success = false;

        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.GetUserPlan((planStr) => {
                SetPlanFromString(planStr);
                planSynced = true;
                success = true;
            }, (error) => {
                Debug.LogWarning($"[SubscriptionManager] Failed to fetch plan: {error}. Keeping {currentPlan}.");
                planSynced = true;
                success = false;
            });
        }
        else
        {
            planSynced = true;
            success = false;
        }

        // Wait for plan sync
        float planTimeout = Time.time + 5f;
        while (!planSynced && Time.time < planTimeout) yield return null;
        
        onComplete?.Invoke(success);
    }

    public void SetPlanFromString(string planStr)
    {
        if (System.Enum.TryParse(planStr, true, out PlanType parsedPlan))
        {
            currentPlan = parsedPlan;
            Debug.Log($"[SubscriptionManager] Plan updated to: {currentPlan}");
        }
        else
        {
            Debug.LogWarning($"[SubscriptionManager] Unknown plan string: {planStr}. Fallback to Free (or maintaining previous).");
        }
    }

    private void OnValidate()
    {
        // インスペクターで値を変更した時にログを出す（プレイモード中）
        if (Application.isPlaying)
        {
            Debug.Log($"[SubscriptionManager] Plan changed to: {currentPlan}");
        }
    }

    /// <summary>
    /// 現在のプランを取得します。
    /// </summary>
    public PlanType CurrentPlan => currentPlan;

    /// <summary>
    /// App Key (無料枠外のアプリ提供キー) を使用できるかどうか。
    /// ★ Free月1回10秒対応: 実際のアクセス制御はサーバー側で実施するため、
    /// クライアント側ではFreeでもAPI呼び出しを許可する。
    /// </summary>
    public bool CanUseAppKey => true;

    /// <summary>
    /// 自動録音を使用できるかどうか。Premium/Ultimate限定。
    /// </summary>
    public bool CanUseAutoRecord => currentPlan == PlanType.Premium || currentPlan == PlanType.Ultimate;

    /// <summary>
    /// ギャラリーで閲覧可能な過去の月数を返します。
    /// </summary>
    public int GetAllowedHistoryMonths()
    {
        switch (currentPlan)
        {
            case PlanType.Free:
                return 0;  // 今月のみ
            case PlanType.Standard:
                return 6;  // 過去6カ月
            case PlanType.Premium:
            case PlanType.Ultimate:
                return int.MaxValue; // 無制限
            default:
                return 0;
        }
    }

    /// <summary>
    /// 指定された月が閲覧可能かどうかを判定します。
    /// </summary>
    /// <param name="monthKey">yyyy-MM 形式のキー</param>
    public bool CanAccessMonth(string monthKey)
    {
        // 1. 今月は常にアクセス可能
        string thisMonth = TimeManager.Instance != null 
            ? TimeManager.Instance.GetCurrentJstTime().ToString("yyyy-MM") 
            : System.DateTime.Now.ToString("yyyy-MM"); // Fallback
            
        if (monthKey == thisMonth) return true;

        // 2. 無料プランなら過去ログはNG
        if (currentPlan == PlanType.Free) return false;

        // 3. 有料プランなら期間制限チェック
        int monthsDiff = CalculateMonthsDifference(monthKey, thisMonth);
        int allowed = GetAllowedHistoryMonths();

        return monthsDiff <= allowed;
    }

    private int CalculateMonthsDifference(string oldMonth, string newMonth)
    {
        try
        {
            var oldD = System.DateTime.Parse(oldMonth + "-01");
            var newD = System.DateTime.Parse(newMonth + "-01");

            // 年の差 * 12 + 月の差
            return ((newD.Year - oldD.Year) * 12) + newD.Month - oldD.Month;
        }
        catch
        {
            Debug.LogError($"Date parse error: {oldMonth} or {newMonth}");
            return int.MaxValue; 
        }
    }

    // --- Quota Management Methods ---

    public float GetMonthlyQuotaSeconds()
    {
        if (planQuotas.TryGetValue(currentPlan, out float quota))
        {
            return quota;
        }
        return 180f; // Default fallback (Free)
    }

    public float GetUsedQuotaSeconds()
    {
        CheckAndResetMonthlyStatus();
        return currentStatus.usedSeconds;
    }

    public float GetRemainingQuotaSeconds()
    {
        float quota = GetMonthlyQuotaSeconds();
        float used = GetUsedQuotaSeconds();
        return Mathf.Max(0f, quota - used);
    }

    public void ConsumeQuota(float seconds)
    {
        CheckAndResetMonthlyStatus();
        currentStatus.usedSeconds += seconds;
        Debug.Log($"[SubscriptionManager] Quota Consumed: {seconds:F1}s. Total Used: {currentStatus.usedSeconds:F1}s / {GetMonthlyQuotaSeconds()}s");
        SaveSubscriptionStatus();
        
        // ★★★ Cloud Functions経由でサーバーに消費を記録（セキュリティ強化）★★★
        if (FirestoreManager.Instance != null)
        {
            string ym = $"{currentStatus.year}-{currentStatus.month:D2}";
            // Cloud Functionsを呼び出し（非同期、失敗しても録音は継続）
            StartCoroutine(FirestoreManager.Instance.ConsumeQuotaOnServer(ym, seconds, (success) => {
                if (!success)
                {
                    Debug.LogWarning("[SubscriptionManager] Failed to sync quota consumption to server. Will retry on next sync.");
                }
            }));
        }
    }

    /// <summary>
    /// ★ サーバー側でクォータを予約（レース条件防止）
    /// 録音開始前に呼び出す
    /// </summary>
    public IEnumerator ReserveQuotaOnServer(float requestedSeconds, System.Action<bool, float> onComplete)
    {
        if (FirestoreManager.Instance == null)
        {
            onComplete?.Invoke(false, 0f);
            yield break;
        }

        CheckAndResetMonthlyStatus();
        string ym = $"{currentStatus.year}-{currentStatus.month:D2}";

        FirestoreManager.QuotaReserveResponse response = null;
        yield return FirestoreManager.Instance.ReserveQuotaOnServer(ym, requestedSeconds, (r) => response = r);

        if (response != null && response.success)
        {
            Debug.Log($"[SubscriptionManager] Quota Reserved: {response.reserved}s, Remaining: {response.remaining}s");
            onComplete?.Invoke(true, response.remaining);
        }
        else
        {
            string msg = response?.message ?? "Unknown error";
            Debug.LogWarning($"[SubscriptionManager] Quota Reservation Failed: {msg}");
            onComplete?.Invoke(false, 0f);
        }
    }

    /// <summary>
    /// サーバー時刻・データと同期を行います。
    /// UIブロック -> NTP取得 -> Firestore取得 -> マージ -> 解除
    /// </summary>
    public IEnumerator SyncQuotaWithServer(System.Action<bool> onComplete)
    {
        if (IsSyncing) yield break;
        IsSyncing = true;
        OnSyncStart?.Invoke();
        Debug.Log("[SubscriptionManager] Starting Server Sync...");

        try
        {
            // 1. Time Sync (Timeout handled internally in TimeManager, roughly 5s)
            bool timeSuccess = false;
            if (TimeManager.Instance != null)
            {
                yield return TimeManager.Instance.SyncTimeWithServer((success) => timeSuccess = success);
            }
            else
            {
                Debug.LogError("[SubscriptionManager] TimeManager.Instance is null! Skipping Time Sync.");
            }
            CheckAndResetMonthlyStatus(); // Update local month based on potentially new time

            bool firestoreSuccess = false;
            bool firestoreCompleted = false;

            // 2. Firestore Sync (with safety timeout)
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user != null)
            {
                string ym = $"{currentStatus.year}-{currentStatus.month:D2}";
                FirestoreManager.Instance.GetSubscriptionStatus(ym, 
                    (serverUsed) => 
                    {
                        if (serverUsed > currentStatus.usedSeconds)
                        {
                            Debug.LogWarning($"[SubscriptionManager] Local usage ({currentStatus.usedSeconds:F3}) < Server ({serverUsed:F3}). Updating local.");
                            currentStatus.usedSeconds = serverUsed;
                            SaveSubscriptionStatus();
                        }
                        else
                        {
                            Debug.Log($"[SubscriptionManager] Local usage ({currentStatus.usedSeconds:F3}) >= Server ({serverUsed:F3}). No change.");
                        }
                        firestoreSuccess = true;
                        firestoreCompleted = true;
                    },
                    (error) => 
                    {
                        Debug.LogError($"[SubscriptionManager] Firestore sync failed: {error}");
                        firestoreSuccess = false;
                        firestoreCompleted = true;
                    }
                );

                // Wait for Firestore callback with 10s timeout
                float timeout = Time.time + 10.0f;
                while (!firestoreCompleted && Time.time < timeout)
                {
                    yield return null;
                }

                if (!firestoreCompleted)
                {
                    Debug.LogError("[SubscriptionManager] Firestore sync timed out!");
                }
            }
            else
            {
                Debug.LogWarning("[SubscriptionManager] No user logged in. Skipping Firestore sync.");
                firestoreCompleted = true;
            }

            onComplete?.Invoke(timeSuccess && firestoreSuccess);
        }
        finally
        {
            IsSyncing = false;
            OnSyncEnd?.Invoke();
        }
    }

    private void CheckAndResetMonthlyStatus()
    {
        System.DateTime now = TimeManager.Instance != null ? TimeManager.Instance.GetCurrentJstTime() : System.DateTime.Now;

        if (currentStatus.year != now.Year || currentStatus.month != now.Month)
        {
            Debug.Log($"[SubscriptionManager] Month changed from {currentStatus.year}-{currentStatus.month} to {now.Year}-{now.Month}. Resetting quota.");
            currentStatus.year = now.Year;
            currentStatus.month = now.Month;
            currentStatus.usedSeconds = 0f;
            SaveSubscriptionStatus();
        }
    }

    private void LoadSubscriptionStatus()
    {
        currentStatus = new SubscriptionStatus();
        
        // Default to current date
        // Default to current date (JST if possible)
        System.DateTime now = TimeManager.Instance != null ? TimeManager.Instance.GetCurrentJstTime() : System.DateTime.Now;
        currentStatus.year = now.Year;
        currentStatus.month = now.Month;
        currentStatus.usedSeconds = 0f;

        if (System.IO.File.Exists(statusFilePath))
        {
            try
            {
                string json = System.IO.File.ReadAllText(statusFilePath);
                var loaded = JsonUtility.FromJson<SubscriptionStatus>(json);
                if (loaded != null)
                {
                    currentStatus = loaded;
                    CheckAndResetMonthlyStatus(); // Load後にも月またぎチェック
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load subscription status: {e.Message}");
            }
        }
    }

    private void SaveSubscriptionStatus()
    {
        try
        {
            string json = JsonUtility.ToJson(currentStatus, true);
            System.IO.File.WriteAllText(statusFilePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save subscription status: {e.Message}");
        }
    }
}
