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

        InitializeDropdowns();

        addButton.onClick.AddListener(OnAddClicked);
        UIStyler.ApplyStyleToButton(addButton, isIconOnly: false);
        // Dropdown styling support (optional) - assuming standard TMP Dropdowns
        
        RefreshList();
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
