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

        if (panelRoot == null) panelRoot = this.gameObject;
        
        // 初期化時は非表示
        UIFadeHelper.HideImmediate(panelRoot);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
            UIStyler.ApplyStyleToButton(closeButton);
        }
        
        if(buttonTemplate != null) buttonTemplate.SetActive(false);

        // ★ グラスモーフィズム適用（ギャラリーは背景が透けると見づらいため高alpha）
        Image panelImage = panelRoot.GetComponent<Image>();
        if (panelImage != null) UIStyler.ApplyGlassStyle(panelImage, alpha: 0.95f);

        // ★ テキストスタイル適用
        if (titleText != null) UIStyler.ApplyStyleToTMP(titleText, isHeader: true);
        if (userIdText != null) UIStyler.ApplyStyleToTMP(userIdText);
        if (messageText != null) UIStyler.ApplyStyleToTMP(messageText);
    }

    /// <summary>
    /// パネルを表示し、リストを更新します。
    /// </summary>
    public void OpenPanel()
    {
        if (!isPanelInitialized) InitPanel();

        if (panelRoot != null) UIFadeHelper.FadeIn(this, panelRoot);
        RefreshGalleryList();
    }

    /// <summary>
    /// パネルを閉じます。
    /// </summary>
    public void ClosePanel()
    {
        if (panelRoot != null) UIFadeHelper.FadeOut(this, panelRoot);
        isRefreshing = false;
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

            // ★ UIStyler適用
            if (btnComp != null) UIStyler.ApplyStyleToButton(btnComp);

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
                    string key = monthKey;
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
