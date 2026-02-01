using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ScheduleSettingsUIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScheduleManager scheduleManager;
    [SerializeField] private GameObject scheduleItemPrefab;
    [SerializeField] private Transform contentParent;

    [Header("Input UI")]
    [SerializeField] private TMP_Dropdown hourDropdown;
    [SerializeField] private TMP_Dropdown minuteDropdown;
    [SerializeField] private TMP_Dropdown durationDropdown;
    [SerializeField] private Button addButton;
    [SerializeField] private Text statusText;

    void Start()
    {
        if (scheduleManager == null)
        {
            scheduleManager = FindFirstObjectByType<ScheduleManager>();
        }

        // ★ プラン制限チェック - UIを先に初期化してから制限を適用
        InitializeDropdowns();
        
        if (addButton != null)
        {
            addButton.onClick.AddListener(OnAddClicked);
            UIStyler.ApplyStyleToButton(addButton, isIconOnly: false);
        }

        // プランに応じてUIを制限
        if (!CheckPlanAccess())
        {
            ShowPlanRestrictionUI();
        }
        else
        {
            RefreshList();
        }
    }

    /// <summary>
    /// スケジュール機能へのアクセス権限をチェック
    /// </summary>
    private bool CheckPlanAccess()
    {
        if (SubscriptionManager.Instance == null) return false;
        return SubscriptionManager.Instance.CanUseSchedule;
    }

    /// <summary>
    /// プラン制限時のUI表示
    /// </summary>
    private void ShowPlanRestrictionUI()
    {
        // 入力UIを無効化
        if (hourDropdown != null) hourDropdown.interactable = false;
        if (minuteDropdown != null) minuteDropdown.interactable = false;
        if (durationDropdown != null) durationDropdown.interactable = false;
        if (addButton != null) addButton.interactable = false;
        
        // 制限メッセージを表示
        string currentPlan = SubscriptionManager.Instance?.CurrentPlan.ToString() ?? "Free";
        SetStatus($"⚠️ スケジュール機能は Premium / Ultimate プランで利用可能です。\n\n" +
                  $"現在のプラン: {currentPlan}\n" +
                  $"プランをアップグレードすると使用できます。");
        
        // 既存のスケジュールがある場合でも、実行はされないことを明示
        if (scheduleManager != null && scheduleManager.GetSchedules().Count > 0)
        {
            SetStatus($"⚠️ スケジュール機能は Premium / Ultimate プランで利用可能です。\n\n" +
                      $"現在のプラン: {currentPlan}\n" +
                      $"※ 登録済みスケジュールは現在のプランでは実行されません。");
        }
    }

    private void InitializeDropdowns()
    {
        // Hours (00-23)
        hourDropdown.ClearOptions();
        List<string> hours = new List<string>();
        for (int i = 0; i < 24; i++) hours.Add(i.ToString("00"));
        hourDropdown.AddOptions(hours);

        // Minutes (00-59) - 5分刻みの方が使いやすいかもしれないが、まずは1分刻みで
        minuteDropdown.ClearOptions();
        List<string> minutes = new List<string>();
        for (int i = 0; i < 60; i++) minutes.Add(i.ToString("00"));
        minuteDropdown.AddOptions(minutes);

        // Durations (Presets: 1min, 3min, 5min, 10min, 30min, 60min)
        durationDropdown.ClearOptions();
        List<string> durations = new List<string> { "1", "3", "5", "10", "15", "30", "60" };
        durationDropdown.AddOptions(durations);
    }

    public void RefreshList()
    {
        Debug.Log("RefreshList called.");
        // Clear current list
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        if (scheduleManager == null) 
        {
             Debug.LogWarning("RefreshList: scheduleManager is null.");
             return;
        }

        var list = scheduleManager.GetSchedules();
        foreach (var item in list)
        {
            var obj = Instantiate(scheduleItemPrefab, contentParent);
            obj.SetActive(true); // プレハブが非アクティブな場合に対応
            
            var ui = obj.GetComponent<ScheduleItemUI>();
            if (ui != null)
            {
                ui.Initialize(item, scheduleManager);
            }
        }
    }

    private void OnAddClicked()
    {
        if (hourDropdown.options.Count == 0 || minuteDropdown.options.Count == 0 || durationDropdown.options.Count == 0) return;

        // Dropdownの選択されているテキストを取得してパースする
        string hText = hourDropdown.options[hourDropdown.value].text;
        string mText = minuteDropdown.options[minuteDropdown.value].text;
        string dText = durationDropdown.options[durationDropdown.value].text;
        
        if (int.TryParse(hText, out int h) && 
            int.TryParse(mText, out int m) && 
            int.TryParse(dText, out int d))
        {
            // 分 -> 秒変換
            if (scheduleManager != null)
            {
                scheduleManager.AddSchedule(h, m, d * 60);
                RefreshList();
                SetStatus("Schedule Added");
            }
            else
            {
                SetStatus("Error: No ScheduleManager");
            }
        }
        else
        {
            SetStatus("Invalid Input");
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
