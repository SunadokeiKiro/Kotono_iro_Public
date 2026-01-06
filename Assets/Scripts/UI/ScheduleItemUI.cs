using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ScheduleItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private Button enableButton;
    [SerializeField] private TextMeshProUGUI enableButtonText;
    [SerializeField] private Button deleteButton;

    private string scheduleId;
    private ScheduleManager manager;
    private bool isEnabled; // Local cache or fetch from item

    public void Initialize(ScheduleItem item, ScheduleManager manager)
    {
        this.scheduleId = item.id;
        this.manager = manager;
        this.isEnabled = item.isEnabled;

        UpdateDisplay(item);
        UpdateEnableVisuals();

        // Enable Button Setup
        enableButton.onClick.RemoveAllListeners();
        enableButton.onClick.AddListener(OnEnableClicked);
        
        // AutoUIStylerがアタッチされていると、Start時に色が上書きされてしまうため削除する
        var styler = enableButton.GetComponent<AutoUIStyler>();
        if (styler != null) Destroy(styler);

        // Delete Button Setup
        deleteButton.onClick.RemoveAllListeners();
        deleteButton.onClick.AddListener(OnDeleteClicked);
        
        // Delete Button Text
        var delText = deleteButton.GetComponentInChildren<TextMeshProUGUI>();
        if (delText != null)
        {
            delText.text = "削除"; // Or "✕"
            UIStyler.ApplyStyleToTMP(delText);
        }
        
        UIStyler.ApplyStyleToButton(deleteButton, isIconOnly: false);
        
        // ビジュアルの最終更新 (AutoUIStyler削除後に確実に適用)
        UpdateEnableVisuals();
    }

    private void UpdateDisplay(ScheduleItem item)
    {
        timeText.text = $"{item.startHour:00}:{item.startMinute:00}";
        durationText.text = $"{item.targetDurationSeconds / 60} min";
        UIStyler.ApplyStyleToTMP(timeText);
        UIStyler.ApplyStyleToTMP(durationText);
    }

    private void OnEnableClicked()
    {
        isEnabled = !isEnabled;
        
        // Managerへ更新通知
        var item = manager.GetSchedules().Find(x => x.id == scheduleId);
        if (item != null)
        {
             manager.UpdateSchedule(scheduleId, item.startHour, item.startMinute, item.targetDurationSeconds, isEnabled);
        }
        
        UpdateEnableVisuals();
    }
    
    private void UpdateEnableVisuals()
    {
        if (enableButtonText != null)
        {
            enableButtonText.text = isEnabled ? "有効" : "無効";
            UIStyler.ApplyStyleToTMP(enableButtonText);
        }

        Image bg = enableButton.GetComponent<Image>();
        if (bg != null)
        {
            // ONならアクセントカラー、OFFなら暗い色
            bg.color = isEnabled ? new Color(0.2f, 0.8f, 0.6f, 1.0f) : new Color(0.3f, 0.3f, 0.3f, 1.0f);
        }
    }

    private void OnDeleteClicked()
    {
        manager.RemoveSchedule(scheduleId);
        Destroy(gameObject);
    }
}
