using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using TMPro;

/// <summary>
/// メインシーン内でポップアップ表示されるギャラリーパネルを制御します。
/// シーン遷移を行わず、GameControllerに直接データロードを指示します。
/// </summary>
public class GalleryPanelController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameController gameController;

    [Header("UI References")]
    [SerializeField] private GameObject panelRoot; // パネルのルートオブジェクト (表示/非表示用)
    [SerializeField] private Transform contentParent; // ScrollView Content
    [SerializeField] private GameObject buttonTemplate; // リスト項目Prefab
    [SerializeField] private Button closeButton; // 閉じるボタン
    [SerializeField] private TextMeshProUGUI titleText;
    
    // ★ New Diagnostic UI
    [SerializeField] private TextMeshProUGUI userIdText; 
    [SerializeField] private TextMeshProUGUI messageText;

    private List<GameObject> activeButtons = new List<GameObject>();

    void Start()
    {
        // ルートが未設定なら自分自身をルートとする
        if (panelRoot == null) panelRoot = this.gameObject;
        
        // 初期化時は非表示
        panelRoot.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }
        
        // テンプレートは非表示に
        if(buttonTemplate != null) buttonTemplate.SetActive(false);
    }

    /// <summary>
    /// パネルを表示し、リストを更新します。
    /// </summary>
    public void OpenPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshGalleryList();
    }

    /// <summary>
    /// パネルを閉じます。
    /// </summary>
    public void ClosePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void RefreshGalleryList()
    {
        // 既存ボタン削除
        foreach (var btn in activeButtons) Destroy(btn);
        activeButtons.Clear();

        if (contentParent == null || buttonTemplate == null) return;

        // 1. User ID / Status Display
        string uidStr = "Guest";
        if (Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser != null)
        {
             string fullId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;
             uidStr = fullId.Length > 6 ? fullId.Substring(0, 6) + "..." : fullId;
        }
        if (userIdText != null) userIdText.text = $"ID: {uidStr}";
        if (messageText != null) messageText.text = "データを取得中...";

        // 2. Fetch from Firestore (Previously Local File Only)
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.GetMonthlyDataList((months) => {
                // Success
                GenerateButtons(months);
                
                string msg = (months.Count > 0) ? "履歴から月を選択してください" : "データが見つかりませんでした (0件)";
                if (messageText != null) messageText.text = msg;
                
            }, (error) => {
                // Failure
                string err = $"取得エラー: {error}";
                if (messageText != null) messageText.text = err;
                Debug.LogError($"[GalleryPanel] Firestore Error: {error}");
            });
        }
        else
        {
            if (messageText != null) messageText.text = "Error: FirestoreManager Missing";
            Debug.LogError("[GalleryPanel] FirestoreManager Not Found");
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

            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = FormatMonthLabel(monthKey);

            Button btnComp = newBtnObj.GetComponent<Button>();
            Image btnImg = newBtnObj.GetComponent<Image>();

            // サブスクリプション判定
            bool isAllowed = true;
            if (SubscriptionManager.Instance != null)
            {
                isAllowed = SubscriptionManager.Instance.CanAccessMonth(monthKey);
            }

            if (isAllowed)
            {
                if (btnComp != null)
                {
                    string key = monthKey; // Capture
                    btnComp.onClick.AddListener(() => OnMonthClicked(key));
                }
            }
            else
            {
                if (btnComp != null) btnComp.interactable = false;
                if (btnImg != null) btnImg.color = Color.gray;
                if (btnText != null) btnText.text += " (Locked)";
            }
        }
    }

    private void OnMonthClicked(string monthKey)
    {
        // データをロードしてパネルを閉じる
        if (gameController != null)
        {
            gameController.LoadMonthData(monthKey);
        }
        ClosePanel();
    }

    private string FormatMonthLabel(string monthKey)
    {
        string[] parts = monthKey.Split('-');
        return (parts.Length == 2) ? $"{parts[0]}年 {parts[1]}月" : monthKey;
    }

    private class GalleryFileItem
    {
        public string monthKey;
    }
}
