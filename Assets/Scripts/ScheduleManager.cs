using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class ScheduleItem
{
    public string id; // Unique ID
    public int startHour;
    public int startMinute;
    public int targetDurationSeconds; // Default 60
    public bool isEnabled;

    // 実行時の一時ステータス (JSONには保存しなくともよいが、Serializableなので保存される。再起動時の復帰ロジックに使えるかも)
    public float activeAccumulatedTime;
    public bool isRunning; // 現在このスケジュールがアクティブかどうか

    public ScheduleItem()
    {
        id = Guid.NewGuid().ToString();
        isEnabled = true;
        targetDurationSeconds = 60;
        activeAccumulatedTime = 0;
        isRunning = false;
    }
}

[System.Serializable]
public class ScheduleConfig
{
    public List<ScheduleItem> items = new List<ScheduleItem>();
}

public class ScheduleManager : MonoBehaviour
{
    [SerializeField] private MicrophoneController microphoneController;

    private ScheduleConfig config = new ScheduleConfig();
    private string configPath;
    


    // 現在実行中のスケジュールID
    private string currentRunningScheduleId = null;

    void Awake()
    {
        configPath = Path.Combine(Application.persistentDataPath, "schedule_config.json");
        LoadConfig();
    }

    void Start()
    {
        if (microphoneController == null)
        {
            // Try to find it (Main Scene case)
            microphoneController = FindFirstObjectByType<MicrophoneController>();
        }

        if (microphoneController == null)
        {
            // Settings Scene case: Just managing data
            Debug.Log("ScheduleManager: Running in Config Mode (No MicrophoneController found).");
            // Do NOT disable the script, so UI can still add/remove schedules.
        }
        else
        {
            // Main Scene case: Execution
            // イベント購読
            microphoneController.OnRecordingFinished += OnRecordingFinishedHandler;
            
            // 1秒ごとにチェック
            InvokeRepeating(nameof(CheckSchedule), 1f, 1f);
        }
    }

    void OnDestroy()
    {
        if (microphoneController != null)
        {
            microphoneController.OnRecordingFinished -= OnRecordingFinishedHandler;
        }
    }

    private void CheckSchedule()
    {
        DateTime now = TimeManager.Instance != null ? TimeManager.Instance.GetCurrentJstTime() : DateTime.Now;
        
        // 1. 新規開始チェック (毎分0秒のタイミング、またはアプリ起動時など)
        // ここでは毎秒チェックし、Hour/Minuteが一致し、かつまだ実行中でないものを探す
        foreach (var item in config.items)
        {
            if (!item.isEnabled) continue;

            // 開始時刻になったか？
            // 注意: 「7:00」に設定してあり、現在「7:00:05」なら開始すべき。
            // ただし「7:00」の間にアプリを再起動した場合なども考慮が必要。
            // シンプルに: Hour/Minuteが一致 && !isRunning なら開始。
            if (now.Hour == item.startHour && now.Minute == item.startMinute)
            {
                if (!item.isRunning && currentRunningScheduleId == null)
                {
                    StartSchedule(item);
                }
            }
        }
    }

    private void StartSchedule(ScheduleItem item)
    {
        // ★ プラン制御: Premium/Ultimateのみスケジュールによる自動録音が可能
        if (SubscriptionManager.Instance != null && !SubscriptionManager.Instance.CanUseAutoRecord)
        {
            Debug.LogWarning($"[ScheduleManager] Schedule skipped - AutoRecord requires Premium or higher. Current plan: {SubscriptionManager.Instance.CurrentPlan}");
            // スケジュールはスキップするが、isRunningは立てない（次の分で再トリガーされないように）
            // 代わりに、その日のスケジュールとしては「実行済み」扱いにする
            item.isRunning = true;
            item.activeAccumulatedTime = item.targetDurationSeconds; // 完了扱い
            currentRunningScheduleId = item.id;
            EndSchedule(item); // 即座に終了
            return;
        }
        
        Debug.Log($"[ScheduleManager] Starting schedule: {item.startHour}:{item.startMinute:00}, Target: {item.targetDurationSeconds}s");
        
        item.isRunning = true;
        item.activeAccumulatedTime = 0; // リセット
        currentRunningScheduleId = item.id;
        
        // マイク設定更新
        UpdateMicrophoneOverride(item);

        // 自動録音開始
        microphoneController.StartAutoRecording();
    }

    private void EndSchedule(ScheduleItem item)
    {
        Debug.Log($"[ScheduleManager] Ending schedule: {item.startHour}:{item.startMinute:00}. Total: {item.activeAccumulatedTime:F1}s");
        
        item.isRunning = false;
        currentRunningScheduleId = null;

        // マイク設定解除
        microphoneController.OverrideRecordLength = -1f;

        // 自動録音停止
        microphoneController.StopAutoRecording();
    }

    private void OnRecordingFinishedHandler(float duration, bool isAuto)
    {
        // 手動録音の場合はスケジュール進行に影響させない
        if (!isAuto) return;

        // 実行中のスケジュールがある場合のみ処理
        if (string.IsNullOrEmpty(currentRunningScheduleId)) return;

        var item = config.items.Find(x => x.id == currentRunningScheduleId);
        if (item == null || !item.isRunning)
        {
            currentRunningScheduleId = null;
            return;
        }

        item.activeAccumulatedTime += duration;
        Debug.Log($"[ScheduleManager] Recording finished ({duration:F1}s). Accumulated: {item.activeAccumulatedTime:F1}/{item.targetDurationSeconds}");

        // ノルマ達成チェック
        if (item.activeAccumulatedTime >= item.targetDurationSeconds)
        {
            // 達成！終了
            EndSchedule(item);
        }
        else
        {
            // まだ続く -> マイクの制限時間を更新して再開
            UpdateMicrophoneOverride(item);
            StartCoroutine(ResumeRecordingDelay());
        }
    }

    private System.Collections.IEnumerator ResumeRecordingDelay()
    {
        Debug.Log("[ScheduleManager] Resuming schedule in 3 seconds...");
        yield return new WaitForSeconds(3.0f); // 解析・保存の処理待ち（少し間隔を空ける）
        
        if (microphoneController != null)
        {
            microphoneController.StartAutoRecording();
        }
    }

    private void UpdateMicrophoneOverride(ScheduleItem item)
    {
        float remaining = item.targetDurationSeconds - item.activeAccumulatedTime;
        if (remaining < 0) remaining = 0;
        
        // 残り時間をセット
        microphoneController.OverrideRecordLength = remaining;
        Debug.Log($"[ScheduleManager] Updated override length check to: {remaining:F1}s");
    }

    // --- Public API for UI ---

    public List<ScheduleItem> GetSchedules()
    {
        return config.items;
    }

    public void AddSchedule(int hour, int minute, int durationSec)
    {
        var newItem = new ScheduleItem
        {
            startHour = hour,
            startMinute = minute,
            targetDurationSeconds = durationSec
        };
        config.items.Add(newItem);
        SaveConfig();
    }

    public void RemoveSchedule(string id)
    {
        var item = config.items.Find(x => x.id == id);
        if (item != null)
        {
            // もし実行中なら止める
            if (item.id == currentRunningScheduleId)
            {
                EndSchedule(item);
            }
            config.items.Remove(item);
            SaveConfig();
        }
    }

    public void UpdateSchedule(string id, int hour, int minute, int durationSec, bool isEnabled)
    {
        var item = config.items.Find(x => x.id == id);
        if (item != null)
        {
            item.startHour = hour;
            item.startMinute = minute;
            item.targetDurationSeconds = durationSec;
            item.isEnabled = isEnabled;
            SaveConfig();
        }
    }

    private void SaveConfig()
    {
        string json = JsonUtility.ToJson(config, true);
        File.WriteAllText(configPath, json);
    }

    private void LoadConfig()
    {
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                config = JsonUtility.FromJson<ScheduleConfig>(json);
            }
            catch
            {
                config = new ScheduleConfig();
            }
        }
    }
}
