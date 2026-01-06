// Scripts/DateTimeDisplay.cs
using UnityEngine;
using TMPro; // TextMeshProを使用するために必要
using System; // DateTimeを使用するために必要

/// <summary>
/// TextMeshProのUIに現在の日付や時刻をリアルタイムで表示します。
/// インスペクターから表示モードを選択できます。
/// </summary>
public class DateTimeDisplay : MonoBehaviour
{
    /// <summary>
    /// 何を表示するかを選択するためのモード定義。
    /// </summary>
    public enum DisplayMode
    {
        DateOnly,   // 日付のみ (例: 2024 / 5 / 26)
        TimeOnly,   // 時刻のみ (例: 15:30:45)
        DateTime    // 日付と時刻の両方
    }

    [Tooltip("日付や時刻を表示するTextMeshProコンポーネント")]
    [SerializeField]
    private TextMeshProUGUI dateTimeText;

    [Tooltip("表示する内容のモードを選択")]
    [SerializeField]
    private DisplayMode displayMode = DisplayMode.DateOnly;

    [Tooltip("日付と時刻の両方を表示する際のフォーマット")]
    [SerializeField]
    private string dateTimeFormat = "yyyy/MM/dd HH:mm:ss";

    void Start()
    {
        // 参照が設定されていない場合のエラーチェック
        if (dateTimeText == null)
        {
            Debug.LogError("DateTimeText is not assigned in the inspector.", this.gameObject);
            this.enabled = false;
        }
    }

    void Update()
    {
        DateTime now = TimeManager.Instance != null ? TimeManager.Instance.GetCurrentJstTime() : DateTime.Now;
        string textToShow = "";

        // 選択されたモードに応じて表示するテキストを決定
        switch (displayMode)
        {
            case DisplayMode.DateOnly:
                // 年 / 月 / 日 の形式で表示
                textToShow = $"{now.Year} / {now.Month} / {now.Day}";
                break;

            case DisplayMode.TimeOnly:
                // 長い時刻形式 (例: "15:30:45") で表示
                textToShow = now.ToLongTimeString();
                break;

            case DisplayMode.DateTime:
                // カスタムフォーマットで日付と時刻を表示
                textToShow = now.ToString(dateTimeFormat);
                break;
        }

        // テキストUIに反映
        dateTimeText.text = textToShow;
    }
}