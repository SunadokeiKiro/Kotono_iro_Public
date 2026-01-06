using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

// Note: Assumes Firebase Firestore SDK is available.

public class FirestoreManager : MonoBehaviour
{
    public static FirestoreManager Instance { get; private set; }

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
    /// Save subscription status to Firestore: users/{uid}/subscription_status
    /// </summary>
    public void SaveSubscriptionStatus(string yearMonth, float usedSeconds, string planName)
    {
        if (!FirebaseConfig.Instance.IsInitialized) return;
        
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) 
        {
            Debug.LogWarning("[FirestoreManager] Cannot save: User not logged in.");
            return;
        }

        Debug.Log($"[FirestoreManager] Saving Subscription Status: {yearMonth}, Used: {usedSeconds}, Plan: {planName}");

        var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(user.UserId).Collection("subscription").Document("status");

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "year_month", yearMonth },
            { "used_seconds", usedSeconds },
            { "plan", planName },
            { "last_updated", Firebase.Firestore.FieldValue.ServerTimestamp }
        };

        docRef.SetAsync(data).ContinueWith(task => {
            if (task.IsCompleted) Debug.Log("[Firestore] Subscription saved.");
            else Debug.LogError($"[Firestore] Failed to save subscription: {task.Exception}");
        });
    }

    /// <summary>
    /// Save ArtData to Firestore: users/{uid}/monthly_data/{monthKey}/art_data
    /// </summary>
    public void SaveArtData(string monthKey, ArtData data)
    {
         if (!FirebaseConfig.Instance.IsInitialized) return;

         var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
         if (user == null) return;

         Debug.Log($"[FirestoreManager] Saving ArtData for {monthKey}. Entries: {data.emotionHistory.Count}");

         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         
         // Use monthKey as document ID inside monthly_data collection
         var docRef = db.Collection("users").Document(user.UserId).Collection("monthly_data").Document(monthKey);
         
         // Serialize ArtData to JSON string (simple backup)
         string json = JsonUtility.ToJson(data); 
         
         Dictionary<string, object> map = new Dictionary<string, object>
         {
             { "json_data", json },
             { "entry_count", data.emotionHistory.Count },
             { "last_updated", Firebase.Firestore.FieldValue.ServerTimestamp }
         };

         docRef.SetAsync(map).ContinueWith(task => {
             if (task.IsCompleted) Debug.Log($"[Firestore] ArtData for {monthKey} saved.");
             else Debug.LogError($"[Firestore] Failed to save ArtData: {task.Exception}");
         });
    }

    /// <summary>
    /// Fetch subscription status from Firestore.
    /// Ensures we check the server to prevent local cache abuse.
    /// </summary>
    public void GetSubscriptionStatus(string yearMonth, Action<float> onSuccess, Action<string> onFailure)
    {
        if (!FirebaseConfig.Instance.IsInitialized) 
        {
            onFailure?.Invoke("Firebase not initialized.");
            return;
        }

        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) 
        {
            onFailure?.Invoke("User not logged in.");
            return;
        }

        var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(user.UserId).Collection("subscription").Document("status");

        // Force fetch from server to avoid stale/manipulated cache
        docRef.GetSnapshotAsync(Firebase.Firestore.Source.Server).ContinueWith(task => 
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"[Firestore] Get status failed: {task.Exception}");
                // Fallback to default/cache if network error? No, security first: fail.
                onFailure?.Invoke(task.Exception != null ? task.Exception.Message : "Unknown Error");
                return;
            }

            if (task.IsCompleted)
            {
                var snapshot = task.Result;
                if (snapshot.Exists)
                {
                    // Check if month matches
                    if (snapshot.TryGetValue("year_month", out string storedYm))
                    {
                        if (storedYm == yearMonth)
                        {
                            // Same month, return usage
                            if (snapshot.TryGetValue("used_seconds", out float used))
                            {
                                Debug.Log($"[Firestore] Server Usage for {yearMonth}: {used}");
                                onSuccess?.Invoke(used);
                                return;
                            }
                        }
                        else
                        {
                             // Month changed on server side, or old data. Implicitly 0 usage for new month.
                             Debug.Log($"[Firestore] Server has data for {storedYm}, but requesting {yearMonth}. Returned 0.");
                             onSuccess?.Invoke(0f);
                             return;
                        }
                    }
                }
                
                // No doc or mismatch -> 0 usage
                Debug.Log("[Firestore] No previous status found. Returned 0.");
                onSuccess?.Invoke(0f);
            }
        });
    }

    /// <summary>
    /// Load ArtData from Firestore: users/{uid}/monthly_data/{monthKey}
    /// Returns a new empty ArtData if not found.
    /// </summary>
    public void LoadArtData(string monthKey, Action<ArtData> onSuccess, Action<string> onFailure)
    {
        if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            onFailure?.Invoke("Not initialized or logged in.");
            return;
        }

        var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
        var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(userId).Collection("monthly_data").Document(monthKey);

        // キャッシュ優先ではなく、サーバー優先にするか？
        // 頻繁なロードではないため、整合性重視でServer。ただしオフライン時はエラーになるため、Default(自動判断)が良いか。
        // オフライン対応要件があるため、Source.Default (または指定なし) を使用。
        docRef.GetSnapshotAsync().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception != null ? task.Exception.Flatten().InnerExceptions[0].Message : "Load Failed"));
                return;
            }

            if (task.IsCompleted)
            {
                var snapshot = task.Result;
                if (snapshot.Exists && snapshot.TryGetValue("json_data", out string json))
                {
                    // JSONパース
                    try
                    {
                        ArtData data = JsonUtility.FromJson<ArtData>(json);
                        MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(data));
                    }
                    catch (Exception ex)
                    {
                        MainThreadDispatcher.Enqueue(() => onFailure?.Invoke($"Parse Error: {ex.Message}"));
                    }
                }
                else
                {
                    // データが存在しない場合は新規作成扱い (Empty Data)
                    MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(new ArtData()));
                }
            }
        });
    }

    /// <summary>
    /// Save TotalSentiments to Firestore: users/{uid}/monthly_data/{monthKey}_stats
    /// </summary>
    public void SaveTotalSentiments(string monthKey, TotalSentiments data)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null) return;

         var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         // 別ドキュメントとして保存 (例: "2023-11_stats")
         var docRef = db.Collection("users").Document(userId).Collection("monthly_data").Document($"{monthKey}_stats");

         string json = JsonUtility.ToJson(data); 
         
         Dictionary<string, object> map = new Dictionary<string, object>
         {
             { "json_data", json },
             { "last_updated", Firebase.Firestore.FieldValue.ServerTimestamp }
         };

         docRef.SetAsync(map).ContinueWith(tsk => {
             if(tsk.IsFaulted) Debug.LogError($"[Firestore] Failed to save stats: {tsk.Exception}");
         });
    }

    /// <summary>
    /// Load TotalSentiments from Firestore.
    /// </summary>
    public void LoadTotalSentiments(string monthKey, Action<TotalSentiments> onSuccess, Action<string> onFailure)
    {
        if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            onFailure?.Invoke("Not initialized");
            return;
        }

        var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
        var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(userId).Collection("monthly_data").Document($"{monthKey}_stats");

        docRef.GetSnapshotAsync().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Message));
                return;
            }

            if (task.IsCompleted)
            {
                var snapshot = task.Result;
                if (snapshot.Exists && snapshot.TryGetValue("json_data", out string json))
                {
                    try {
                        TotalSentiments data = JsonUtility.FromJson<TotalSentiments>(json);
                        MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(data));
                    } catch { 
                        MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(new TotalSentiments())); 
                    }
                }
                else
                {
                    MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(new TotalSentiments()));
                }
            }
        });
    }


    /// <summary>
    /// Save User API Key to Firestore: users/{uid}/private_data/settings
    /// This collection should be secured by rules.
    /// </summary>
    public void SaveUserApiKey(string key)
    {
         if (!FirebaseConfig.Instance.IsInitialized) return;
         var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
         if (user == null) return;

         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var docRef = db.Collection("users").Document(user.UserId).Collection("private_data").Document("settings");

         Dictionary<string, object> data = new Dictionary<string, object>
         {
             { "user_api_key", key },
             { "updated_at", Firebase.Firestore.FieldValue.ServerTimestamp }
         };

         docRef.SetAsync(data, Firebase.Firestore.SetOptions.MergeAll).ContinueWith(task => {
             if (task.IsCompleted) Debug.Log("[Firestore] User API Key saved.");
             else Debug.LogError($"[Firestore] Failed to save User API Key: {task.Exception}");
         });
    }

    /// <summary>
    /// Load User API Key from Firestore.
    /// </summary>
    public void GetUserApiKey(Action<string> onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var docRef = db.Collection("users").Document(user.UserId).Collection("private_data").Document("settings");

         docRef.GetSnapshotAsync().ContinueWith(task => {
             if (task.IsFaulted)
             {
                 MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Message));
                 return;
             }

             if (task.IsCompleted)
             {
                 var snapshot = task.Result;
                 if (snapshot.Exists && snapshot.TryGetValue("user_api_key", out string key))
                 {
                     MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(key));
                 }
                 else
                 {
                     MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke("")); // Empty if not found
                 }
             }
         });
    }

    /// <summary>
    /// Cloud FunctionでAPIキーの有効性を検証し、特典資格を確認します
    /// </summary>
    public void ValidateApiKey(string apiKey, Action<ValidateApiKeyResponse> onSuccess, Action<string> onFailure)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            onFailure?.Invoke("APIキーが空です");
            return;
        }
        
        StartCoroutine(ValidateApiKeyCoroutine(apiKey, onSuccess, onFailure));
    }

    [System.Serializable]
    public class ValidateApiKeyResponse
    {
        public bool valid;
        public bool offerEligible;
        public string offerId;
        public string message;
        public string error;
    }

    private IEnumerator ValidateApiKeyCoroutine(string apiKey, Action<ValidateApiKeyResponse> onSuccess, Action<string> onFailure)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            onFailure?.Invoke("ログインが必要です");
            yield break;
        }

        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            onFailure?.Invoke("認証トークンの取得に失敗しました");
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/validateApiKey";

        string jsonBody = $"{{\"apiKey\":\"{apiKey}\"}}";

        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ValidateApiKeyResponse>(request.downloadHandler.text);
                Debug.Log($"[Firestore] ValidateApiKey: valid={response.valid}, offerEligible={response.offerEligible}");
                onSuccess?.Invoke(response);
            }
            else
            {
                Debug.LogError($"[Firestore] ValidateApiKey Failed: {request.error}");
                onFailure?.Invoke(request.error);
            }
        }
    }

    /// <summary>
    /// APIキー特典の資格があるかどうかを取得します
    /// </summary>
    public void GetApiKeyOfferEligibility(Action<bool> onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var docRef = db.Collection("users").Document(userId).Collection("subscription").Document("status");

         docRef.GetSnapshotAsync(Firebase.Firestore.Source.Server).ContinueWith(task => {
             if (task.IsFaulted)
             {
                 MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Flatten().InnerExceptions[0].Message));
                 return;
             }

             if (task.IsCompleted)
             {
                 bool eligible = false;
                 if (task.Result.Exists && task.Result.ContainsField("apikey_offer_eligible"))
                 {
                     eligible = task.Result.GetValue<bool>("apikey_offer_eligible");
                 }
                 Debug.Log($"[Firestore] API Key Offer Eligible: {eligible}");
                 MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(eligible));
             }
         });
    }

    /// <summary>
    /// Save Analysis Log to Firestore: users/{uid}/history/{autoId}
    /// </summary>
    public void SaveAnalysisLog(string jsonResponse)
    {
         if (!FirebaseConfig.Instance.IsInitialized) return;
         var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
         if (user == null) return;

         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var colRef = db.Collection("users").Document(user.UserId).Collection("history");

         // Parse JSON to get SessionID if possible, usually passed in separate args but here we extract if needed or just store raw.
         // Storing raw JSON string is simple but less queryable. For logs, it's fine.
         
         Dictionary<string, object> data = new Dictionary<string, object>
         {
             { "json_response", jsonResponse },
             { "timestamp", Firebase.Firestore.FieldValue.ServerTimestamp }
         };

         colRef.AddAsync(data).ContinueWith(task => {
             if (task.IsCompleted) Debug.Log($"[Firestore] Analysis Log Saved. ID: {task.Result.Id}");
             else Debug.LogError($"[Firestore] Failed to save log: {task.Exception}");
         });
    }
    /// <summary>
    /// Fetch list of available months from Firestore: users/{uid}/monthly_data
    /// Returns a list of document IDs (e.g. "2023-11").
    /// </summary>
    public void GetMonthlyDataList(Action<List<string>> onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var colRef = db.Collection("users").Document(userId).Collection("monthly_data");

         colRef.GetSnapshotAsync().ContinueWith(task => {
             if (task.IsFaulted)
             {
                 MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Flatten().InnerExceptions[0].Message));
                 return;
             }

             if (task.IsCompleted)
             {
                 List<string> months = new List<string>();
                 foreach (var doc in task.Result.Documents)
                 {
                     // Filter out stats documents (suffix "_stats")
                     if (!doc.Id.EndsWith("_stats"))
                     {
                         months.Add(doc.Id);
                     }
                 }
                 MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(months));
             }
         });
    }

    // ---------------------------------------------------------
    // Subscription & Plan Plan Management
    // ---------------------------------------------------------

    /// <summary>
    /// Fetch the user's current plan from Firestore.
    /// </summary>
    public void GetUserPlan(Action<string> onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var docRef = db.Collection("users").Document(userId).Collection("subscription").Document("status");

         // ★ キャッシュではなくサーバーから強制取得（解約済みプランの誤表示防止）
         docRef.GetSnapshotAsync(Firebase.Firestore.Source.Server).ContinueWith(task => {
             if (task.IsFaulted)
             {
                 MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Flatten().InnerExceptions[0].Message));
                 return;
             }

             if (task.IsCompleted)
             {
                 string plan = "Free";
                 if (task.Result.Exists && task.Result.ContainsField("plan"))
                 {
                     plan = task.Result.GetValue<string>("plan");
                     
                     // ★ downgrade_reason が存在する場合は解約済みとみなしてFreeを返す
                     if (task.Result.ContainsField("downgrade_reason"))
                     {
                         string downgradeReason = task.Result.GetValue<string>("downgrade_reason");
                         if (!string.IsNullOrEmpty(downgradeReason))
                         {
                             Debug.Log($"[Firestore] downgrade_reason found: {downgradeReason}. Overriding plan to Free.");
                             plan = "Free";
                         }
                     }
                 }
                 Debug.Log($"[Firestore] Fetched Plan: {plan}");
                 MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(plan));
             }
         });
    }

    /// <summary>
    /// Freeユーザーの無料お試し使用回数を取得します。
    /// </summary>
    public void GetFreeTrialCount(Action<int> onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         var userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
         var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
         var docRef = db.Collection("users").Document(userId).Collection("subscription").Document("status");

         docRef.GetSnapshotAsync(Firebase.Firestore.Source.Server).ContinueWith(task => {
             if (task.IsFaulted)
             {
                 MainThreadDispatcher.Enqueue(() => onFailure?.Invoke(task.Exception.Flatten().InnerExceptions[0].Message));
                 return;
             }

             if (task.IsCompleted)
             {
                 int count = 0;
                 if (task.Result.Exists && task.Result.ContainsField("free_trial_count"))
                 {
                     count = (int)task.Result.GetValue<long>("free_trial_count");
                 }
                 Debug.Log($"[Firestore] Free Trial Count: {count}");
                 MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(count));
             }
         });
    }

    /// <summary>
    /// Update user's plan in Firestore via Cloud Function.
    /// Used when subscription is cancelled to downgrade to Free.
    /// (Direct Firestore write is blocked by rules for security)
    /// </summary>
    public void UpdateUserPlan(string newPlan, Action onSuccess, Action<string> onFailure)
    {
         if (!FirebaseConfig.Instance.IsInitialized || Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
         {
             onFailure?.Invoke("Not initialized or logged in");
             return;
         }

         // Cloud Function経由で更新（Firestore rulesで直接書き込み禁止のため）
         StartCoroutine(UpdateUserPlanViaCloudFunction(newPlan, onSuccess, onFailure));
    }

    private IEnumerator UpdateUserPlanViaCloudFunction(string newPlan, Action onSuccess, Action<string> onFailure)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            onFailure?.Invoke("Not logged in");
            yield break;
        }

        // Get Auth Token
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            onFailure?.Invoke("Token Error");
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/downgradePlan";

        string jsonBody = $"{{\"newPlan\":\"{newPlan}\"}}";

        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Firestore] Plan updated to: {newPlan} via Cloud Function");
                onSuccess?.Invoke();
            }
            else
            {
                Debug.LogError($"[Firestore] UpdateUserPlan Failed: {request.error}");
                onFailure?.Invoke(request.error);
            }
        }
    }

    /// <summary>
    /// Call Cloud Function 'verifyReceipt' to validate purchase and update plan.
    /// Uses UnityWebRequest to avoid dependency on Firebase Functions SDK (which is optional).
    /// </summary>
    public void VerifyPurchase(string receipt, string productId, Action onSuccess, Action<string> onFailure)
    {
        if (Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            onFailure?.Invoke("User not logged in");
            return;
        }

        StartCoroutine(VerifyPurchaseCoroutine(receipt, productId, onSuccess, onFailure));
    }

    private IEnumerator VerifyPurchaseCoroutine(string receipt, string productId, Action onSuccess, Action<string> onFailure)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        
        // 1. Get Auth Token
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            onFailure?.Invoke($"Token Error: {tokenTask.Exception.InnerException?.Message}");
            yield break;
        }

        string idToken = tokenTask.Result;

        // 2. Prepare Request
        // Note: Replace with your actual project location/ID if different.
        // Assuming 'us-central1' and project ID from existing config or hardcoded for now.
        // Ideally should be flexible, but matching ApiHandler's pattern.
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/verifyReceipt"; 

        // Create JSON body
        var requestData = new VerifyReceiptRequest
        {
            receipt = receipt,
            platform = Application.platform == RuntimePlatform.Android ? "GooglePlay" : "AppStore",
            productId = productId
        };
        string jsonBody = JsonUtility.ToJson(requestData);

        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            // UnityWebRequest.Post(string, string) sets Content-Type to application/x-www-form-urlencoded by default?
            // We need JSON. So we construct manually or use UploadHandler.
            
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Firestore] Receipt Verified: {request.downloadHandler.text}");
                onSuccess?.Invoke();
            }
            else
            {
                long code = request.responseCode;
                string errorMsg = request.error;
                Debug.LogError($"[Firestore] Verify Failed (Code: {code}): {errorMsg}");
                onFailure?.Invoke(errorMsg);
            }
        }
    }

    [System.Serializable]
    private class VerifyReceiptRequest
    {
        public string receipt;
        public string platform;
        public string productId;
    }

    // ---------------------------------------------------------
    // ★ CheckSubscriptionStatus (解約確認用 Cloud Function呼び出し)
    // ---------------------------------------------------------
    
    /// <summary>
    /// Checks subscription status directly with the server (Cloud Functions).
    /// Used during strict initialization. Handles timeouts safely (defaults to Free on error).
    /// </summary>
    public IEnumerator VerifySubscriptionStrict(Action<bool> onComplete)
    {
        if (FirebaseConfig.Instance == null || !FirebaseConfig.Instance.IsInitialized)
        {
            Debug.LogError("[Firestore] Cannot verify: Firebase not initialized.");
            onComplete?.Invoke(false);
            yield break;
        }

        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.Log("[Firestore] User not logged in. Treating as Free.");
            onComplete?.Invoke(true); // Verification finished (result is free)
            yield break;
        }

        bool completed = false;
        string receipt = ""; // TODO: Get latest receipt from IAP if available? 
                             // For strict check, if we don't have a receipt handy, we can send empty.
                             // The server will check Firestore. IF receipt is needed for "Refresh", 
                             // IAPManager should provide it. 
                             
                             // However, extracting receipt here is hard. 
                             // Let's rely on stored Firestore data OR pass receipt if possible.
                             // Ideally, IAPManager should have initialized Unity IAP by now.

        // Get Receipt from IAPManager if initialized
        string productId = "";
        if (IAPManager.Instance != null && IAPManager.Instance.IsInitialized())
        {
             // Try to find any active subscription receipt locally
             // This helps if the server doesn't have it yet (rare) or for re-validation.
             // But primarily we want the SERVER to tell US the truth.
        }

        Debug.Log("[Firestore] Starting Strict Server Verification...");

        // Call existing logic but wrap it
        CheckSubscriptionStatusOnServer(receipt, productId, (plan, expired) => {
             Debug.Log($"[Firestore] Strict Check Result: Plan={plan}, Expired={expired}");
             
             // Update SubscriptionManager immediately
             if (SubscriptionManager.Instance != null)
             {
                 SubscriptionManager.Instance.SetPlanFromString(plan);
             }
             
             completed = true;
        });

        // Wait with timeout
        float timeout = Time.time + 10.0f;
        while (!completed && Time.time < timeout)
        {
            yield return null;
        }

        if (!completed)
        {
            Debug.LogError("[Firestore] Subscription verification timed out! Defaulting to Free.");
            if (SubscriptionManager.Instance != null)
            {
                SubscriptionManager.Instance.SetPlanFromString("Free");
            }
        }

        onComplete?.Invoke(true);
    }

    /// <summary>
    /// サーバー側でサブスクリプション状態を確認し、期限切れならFreeに更新
    /// </summary>
    public void CheckSubscriptionStatusOnServer(string receipt, string productId, Action<string, bool> onComplete)
    {
        if (Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            onComplete?.Invoke("Free", true);
            return;
        }

        StartCoroutine(CheckSubscriptionStatusCoroutine(receipt, productId, onComplete));
    }

    private IEnumerator CheckSubscriptionStatusCoroutine(string receipt, string productId, Action<string, bool> onComplete)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            Debug.LogError($"[Firestore] Token Error: {tokenTask.Exception.InnerException?.Message}");
            onComplete?.Invoke("Free", true);
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/checkSubscriptionStatus";

        var requestData = new CheckSubscriptionRequest
        {
            uid = user.UserId,
            receipt = receipt,
            productId = productId
        };
        string jsonBody = JsonUtility.ToJson(requestData);

        using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Firestore] CheckSubscriptionStatus: {request.downloadHandler.text}");
                
                // Parse response
                var response = JsonUtility.FromJson<CheckSubscriptionResponse>(request.downloadHandler.text);
                
                if (response.success)
                {
                    onComplete?.Invoke(response.plan, response.expired);
                }
                else
                {
                    Debug.LogWarning($"[Firestore] CheckSubscriptionStatus returned success=false. Treating as Free.");
                    onComplete?.Invoke("Free", true);
                }
            }
            else
            {
                Debug.LogError($"[Firestore] CheckSubscriptionStatus Failed: {request.error}");
                onComplete?.Invoke("Free", true);
            }
        }
    }

    [System.Serializable]
    private class CheckSubscriptionRequest
    {
        public string uid;
        public string receipt;
        public string productId;
    }

    [System.Serializable]
    private class CheckSubscriptionResponse
    {
        public bool success;
        public string plan;
        public bool expired;
        public bool cancelled;
    }

    // ---------------------------------------------------------
    // ★ Quota Management (Security Enhancement)
    // Calls Cloud Functions to prevent race conditions
    // ---------------------------------------------------------

    [System.Serializable]
    private class QuotaRequest
    {
        public string yearMonth;
        public float requestedSeconds;
        public float actualSeconds;
        public float releasedSeconds;
    }

    [System.Serializable]
    public class QuotaReserveResponse
    {
        public bool success;
        public float reserved;
        public float remaining;
        public string message;
    }

    /// <summary>
    /// Reserve quota via Cloud Function before recording starts.
    /// </summary>
    public IEnumerator ReserveQuotaOnServer(string yearMonth, float requestedSeconds, Action<QuotaReserveResponse> onComplete)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            onComplete?.Invoke(new QuotaReserveResponse { success = false, message = "Not logged in" });
            yield break;
        }

        // Get Auth Token
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            onComplete?.Invoke(new QuotaReserveResponse { success = false, message = "Token Error" });
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/reserveQuota";

        var requestData = new QuotaRequest { yearMonth = yearMonth, requestedSeconds = requestedSeconds };
        string jsonBody = JsonUtility.ToJson(requestData);

        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<QuotaReserveResponse>(request.downloadHandler.text);
                Debug.Log($"[Firestore] Quota Reserved: {response.reserved}s, Remaining: {response.remaining}s");
                onComplete?.Invoke(response);
            }
            else
            {
                Debug.LogError($"[Firestore] ReserveQuota Failed: {request.error}");
                onComplete?.Invoke(new QuotaReserveResponse { success = false, message = request.error });
            }
        }
    }

    /// <summary>
    /// Consume quota via Cloud Function after recording completes.
    /// </summary>
    public IEnumerator ConsumeQuotaOnServer(string yearMonth, float actualSeconds, Action<bool> onComplete)
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        // Get Auth Token
        var tokenTask = user.TokenAsync(false);
        yield return new WaitUntil(() => tokenTask.IsCompleted);

        if (tokenTask.Exception != null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        string idToken = tokenTask.Result;
        string url = "https://us-central1-kotono-iro-project.cloudfunctions.net/consumeQuota";

        var requestData = new QuotaRequest { yearMonth = yearMonth, actualSeconds = actualSeconds, releasedSeconds = actualSeconds };
        string jsonBody = JsonUtility.ToJson(requestData);

        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + idToken);

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Firestore] Quota Consumed: {actualSeconds}s");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[Firestore] ConsumeQuota Failed: {request.error}");
                onComplete?.Invoke(false);
            }
        }
    }
}
