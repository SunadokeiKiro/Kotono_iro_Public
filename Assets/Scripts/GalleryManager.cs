// Scripts/GalleryManager.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System.Linq; // For sorting
using TMPro;

/// <summary>
/// ギャラリーシーンのUIと機能を管理します。
/// 永続化された月次データ (art_data_yyyy-MM.json) を検索し、動的にリスト表示します。
/// </summary>
public class GalleryManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform contentParent; // ScrollViewのContent
    [SerializeField] private GameObject buttonTemplate; // リスト項目のテンプレート (Prefab)
    [SerializeField] private Button loadSelectedButton;
    [SerializeField] private Button loadCurrentButton; // 追加: 現在のアートに戻るボタン
    [SerializeField] private Button backToMainButton;
    [SerializeField] private TextMeshProUGUI userIdText; // ★ New: 専用ID表示
    [SerializeField] private TextMeshProUGUI messageText; // ★ New: ステータス/件数表示
    [SerializeField] private TextMeshProUGUI statusText; // Legacy fallback

    [Header("Preview System")]
    [SerializeField] private PreviewRippleManager previewManager;

    private string selectedMonthKey; // 例: "2023-11"
    private List<GameObject> activeButtons = new List<GameObject>();

    void Start()
    {
        // ボタンのリスナー設定
        if (backToMainButton != null)
        {
             backToMainButton.onClick.AddListener(OnBackToMain); 
        }

        // 初期表示リセット
        if (userIdText != null) userIdText.text = "ID: ...";
        if (messageText != null) messageText.text = "Ready";
        if (statusText != null) statusText.text = ""; // Clear legacy

        // テンプレートを非表示にしておく
        if(buttonTemplate != null && buttonTemplate.activeInHierarchy)
        {
            buttonTemplate.SetActive(false);
        }

        RefreshGalleryList();
    }

    /// <summary>
    /// Firestoreから保存されているデータのリストを取得し、一覧を更新します。
    /// </summary>
    public void RefreshGalleryList()
    {
        // 既存のボタンをクリア (テンプレート以外)
        foreach (var btn in activeButtons)
        {
            Destroy(btn);
        }
        activeButtons.Clear();

        if (contentParent == null || buttonTemplate == null)
        {
            Debug.LogError("GalleryManager: Content Parent or Button Template is not assigned.");
            if (messageText != null) messageText.text = "Error: UI Not Assigned";
            return;
        }

        // 1. User ID Display
        string uidStr = "Guest";
        if (Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser != null)
        {
             string fullId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
             uidStr = fullId.Length > 6 ? fullId.Substring(0, 6) + "..." : fullId;
        }
        if (userIdText != null) userIdText.text = $"ID: {uidStr}";

        // 2. Status Update
        if (messageText != null) messageText.text = "データを取得中...";

        // Firestoreからリスト取得
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.GetMonthlyDataList((months) => {
                // Success
                GenerateButtons(months);
                
                string msg = (months.Count > 0) ? "履歴から月を選択してください" : "データが見つかりませんでした (0件)";
                if (messageText != null) messageText.text = msg;
                if (statusText != null) statusText.text = msg; // Fallback
                
            }, (error) => {
                // Failure
                string err = $"取得エラー: {error}";
                if (messageText != null) messageText.text = err;
                if (statusText != null) statusText.text = err; // Fallback
                
                Debug.LogError($"[GalleryManager] Failed to fetch list: {error}");
            });
        }
        else
        {
             if (messageText != null) messageText.text = "Error: FirestoreManager Missing";
        }
    }

    private void GenerateButtons(List<string> monthKeys)
    {
        // 新しい順にソート (文字列ソートでyyyy-MMなら問題ない)
        var sortedList = monthKeys.OrderByDescending(x => x).ToList();

        foreach (var monthKey in sortedList)
        {
            GameObject newBtnObj = Instantiate(buttonTemplate, contentParent);
            newBtnObj.SetActive(true);
            activeButtons.Add(newBtnObj);

            // ボタンのラベル設定
            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                btnText.text = FormatMonthLabel(monthKey);
            }

            // クリックイベント設定 & サブスクリプション判定
            Button btnComp = newBtnObj.GetComponent<Button>();
            Image btnImg = newBtnObj.GetComponent<Image>();
            
            bool isAllowed = true;
            if (SubscriptionManager.Instance != null)
            {
                isAllowed = SubscriptionManager.Instance.CanAccessMonth(monthKey);
            }

            if (isAllowed)
            {
                if (btnComp != null)
                {
                    string key = monthKey; // クロージャ用
                    btnComp.onClick.AddListener(() => OnMonthSelected(key, newBtnObj));
                }
            }
            else
            {
                // 制限かかっている場合
                if (btnComp != null) btnComp.interactable = false;
                if (btnImg != null) btnImg.color = Color.gray;
                
                if (btnText != null)
                {
                    btnText.text += " (Locked)";
                    btnText.color = Color.gray;
                }
            }
        }
    }

    private string FormatMonthLabel(string monthKey)
    {
        // "2023-11" -> "2023年 11月"
        string[] parts = monthKey.Split('-');
        if (parts.Length == 2)
        {
            return $"{parts[0]}年 {parts[1]}月";
        }
        return monthKey;
    }

    private void OnBackToMain()
    {
        SceneTransitionManager.Instance.LoadScene("ArtScene");
    }


    private void OnMonthSelected(string monthKey, GameObject selectedObj)
    {
        Debug.Log($"[GalleryManager] Month selected: {monthKey}. Loading directly...");

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.MonthToLoad = monthKey;
            
            // Play a click sound if available (optional)
            // ...

            // Transition to ArtScene
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.LoadScene("ArtScene");
            }
            else
            {
                SceneManager.LoadScene("ArtScene");
            }
        }
        else
        {
            Debug.LogError("GameDataManager instance not found!");
            if (statusText != null) statusText.text = "エラー: データマネージャーが見つかりません";
        }
    }

    // Removed unused methods: OnLoadSelected, OnLoadCurrent
    // Removed unused fields in Inspector (to be cleaned up in Editor manually or ignored)

    private class GalleryFileItem
    {
        public string monthKey;
    }
}