using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Manage Activity Log and Streak logic.
/// Acts as a bridge between FirestoreManager and UI (BackgroundGraphController).
/// </summary>
public class StreakManager : MonoBehaviour
{
    public static StreakManager Instance { get; private set; }

    [SerializeField] private BackgroundGraphController graphController;

    private Dictionary<string, int> localActivityCache = new Dictionary<string, int>();
    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // Optional: depends on scene structure. If GameController persists, this should too.
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Try to load initial data
        LoadActivityLog();
    }

    /// <summary>
    /// Loads the full activity log from Firestore.
    /// </summary>
    public void LoadActivityLog()
    {
        if (FirestoreManager.Instance == null) return;

        FirestoreManager.Instance.GetActivityLog(
            (data) => {
                localActivityCache = data;
                isInitialized = true;
                Debug.Log($"[StreakManager] Loaded {data.Count} activity entries.");
                
                // Refresh UI if controller is linked
                if (graphController != null)
                {
                    graphController.UpdateHeatmap(localActivityCache);
                }
            },
            (error) => {
                Debug.LogWarning($"[StreakManager] Failed to load activity log: {error}");
            }
        );
    }

    /// <summary>
    /// Call this when an analysis is completed (Activity).
    /// </summary>
    public void LogActivity()
    {
        if (FirestoreManager.Instance == null) return;

        // Determine "Today" based on Local Time (JST) implied by system settings
        string todayKey = DateTime.Now.ToString("yyyy-MM-dd");

        // 1. Optimistic Update (Local)
        if (localActivityCache.ContainsKey(todayKey))
            localActivityCache[todayKey]++;
        else
            localActivityCache[todayKey] = 1;

        // Update UI immediately (Optimistic)
        if (graphController != null)
        {
            graphController.UpdateHeatmap(localActivityCache);
        }

        // 2. Server Update
        FirestoreManager.Instance.IncrementActivity(todayKey, 
            () => {
                Debug.Log("[StreakManager] Activity logged on server.");
            },
            (error) => {
                 Debug.LogError($"[StreakManager] Server update failed: {error}");
                 // Revert local cache if needed? Usually fine to just keep optimistic or reload.
            }
        );
    }

    /// <summary>
    /// Returns the activity count for a specific month.
    /// Output is a dictionary of Day(1-31) -> Count.
    /// </summary>
    public Dictionary<int, int> GetMonthData(int year, int month)
    {
        Dictionary<int, int> result = new Dictionary<int, int>();
        
        // Filter cache for keys starting with "yyyy-MM"
        string prefix = $"{year:D4}-{month:D2}";

        foreach (var kvp in localActivityCache)
        {
            if (kvp.Key.StartsWith(prefix))
            {
                // Parse day part
                if (DateTime.TryParse(kvp.Key, out DateTime date))
                {
                    result[date.Day] = kvp.Value;
                }
            }
        }

        return result;
    }
}
