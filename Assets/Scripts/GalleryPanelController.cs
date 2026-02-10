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
    private bool isRefreshing = false;

    void Awake()
    {
        InitPanel();
    }

    /// <summary>
    /// panelRootの参照設定と初期状態を確立します。
    /// Awake()から呼ばれますが、GOが最初から無効だった場合はOpenPanel()で遅延初期化されます。
    /// </summary>
    private bool isPanelInitialized = false;
    private void InitPanel()
    {
        if (isPanelInitialized) return;
        isPanelInitialized = true;

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
        // GOが初回無効だった場合のAwake()未実行対策（遅延初期化）
        if (!isPanelInitialized) InitPanel();

        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshGalleryList();
    }

    /// <summary>
    /// パネルを閉じます。
    /// </summary>
    public void ClosePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        isRefreshing = false; // 閉じた時はリフレッシュフラグもリセット
    }

    private void RefreshGalleryList()
    {
        if (isRefreshing) return; // 非同期リフレッシュ中の重複呼び出しを防止
        isRefreshing = true;

        // 既存ボタン削除
        ClearExistingButtons();

        if (contentParent == null || buttonTemplate == null)
        {
            isRefreshing = false;
            return;
        }

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
                // Success: コールバック到着時に既存ボタンを再クリア（重複防止）
                ClearExistingButtons();
                GenerateButtons(months);
                
                string msg = (months.Count > 0) ? "履歴から月を選択してください" : "データが見つかりませんでした (0件)";
                if (messageText != null) messageText.text = msg;
                isRefreshing = false;
                
            }, (error) => {
                // Failure
                string err = $"取得エラー: {error}";
                if (messageText != null) messageText.text = err;
                Debug.LogError($"[GalleryPanel] Firestore Error: {error}");
                isRefreshing = false;
            });
        }
        else
        {
            if (messageText != null) messageText.text = "Error: FirestoreManager Missing";
            Debug.LogError("[GalleryPanel] FirestoreManager Not Found");
            isRefreshing = false;
        }
    }

    /// <summary>
    /// 既存の動的生成ボタンをすべて削除します。
    /// </summary>
    private void ClearExistingButtons()
    {
        foreach (var btn in activeButtons)
        {
            if (btn != null) Destroy(btn);
        }
        activeButtons.Clear();

        // contentParentの子要素も直接走査（テンプレート以外を削除）
        if (contentParent != null)
        {
            for (int i = contentParent.childCount - 1; i >= 0; i--)
            {
                var child = contentParent.GetChild(i).gameObject;
                if (child != buttonTemplate)
                {
                    Destroy(child);
                }
            }
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
