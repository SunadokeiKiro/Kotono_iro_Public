using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// サブスクリプション購入画面のUIを管理するクラス。
/// 購入ボタンのイベントハンドリングと、プラン状態による表示切替を行います。
/// </summary>
public class SubscriptionUIManager : MonoBehaviour
{
    [Header("Purchase Buttons")]
    [SerializeField] private Button buyStandardButton;
    [SerializeField] private Button buyPremiumButton;
    [SerializeField] private Button buyUltimateButton;
    [SerializeField] private Button restoreButton; // iOS用 (今回はAndroidメインでも一応用意)

    [Header("Price Labels")]
    [SerializeField] private TextMeshProUGUI standardPriceText;
    [SerializeField] private TextMeshProUGUI premiumPriceText;
    [SerializeField] private TextMeshProUGUI ultimatePriceText;

    [Header("Status UI")]
    [SerializeField] private TextMeshProUGUI currentPlanText;
    [SerializeField] private TextMeshProUGUI statusText;  // ★ エラー/ステータス表示用
    [SerializeField] private GameObject premiumBadge; // プレミアム以上のユーザー向けの装飾
    [SerializeField] private GameObject apiKeyOfferBadge; // ★ APIキー特典バッジ
    [SerializeField] private TextMeshProUGUI apiKeyOfferBadgeText; // ★ 特典バッジテキスト

    private bool isApiKeyOfferEligible = false;

    void Start()
    {
        // ボタンのリスナー設定
        if (buyStandardButton != null)
            buyStandardButton.onClick.AddListener(() => OnBuyClicked(IAPManager.PRODUCT_STANDARD_MONTHLY));
        
        if (buyPremiumButton != null)
            buyPremiumButton.onClick.AddListener(() => OnBuyClicked(IAPManager.PRODUCT_PREMIUM_MONTHLY));

        if (buyUltimateButton != null)
            buyUltimateButton.onClick.AddListener(() => OnBuyClicked(IAPManager.PRODUCT_ULTIMATE_MONTHLY));

        if (restoreButton != null)
            restoreButton.onClick.AddListener(OnRestoreClicked);

        // IAPManagerのイベント購読
        if (IAPManager.Instance != null)
        {
            IAPManager.Instance.OnPurchaseSuccess += OnPurchaseSuccess;
            IAPManager.Instance.OnIAPPurchaseFailed += ShowErrorMessage;
        }

        // APIキー特典資格をチェック
        CheckApiKeyOfferEligibility();
        RefreshUI();
    }

    void OnDestroy()
    {
        if (IAPManager.Instance != null)
        {
            IAPManager.Instance.OnPurchaseSuccess -= OnPurchaseSuccess;
            IAPManager.Instance.OnIAPPurchaseFailed -= ShowErrorMessage;
        }
    }

    /// <summary>
    /// 購入成功時の処理
    /// </summary>
    private void OnPurchaseSuccess()
    {
        ClearStatusMessage();
        isApiKeyOfferEligible = false; // 使用済みにする
        RefreshUI();
    }

    /// <summary>
    /// APIキー特典の資格をチェック
    /// </summary>
    private void CheckApiKeyOfferEligibility()
    {
        if (FirestoreManager.Instance == null) return;
        
        FirestoreManager.Instance.GetApiKeyOfferEligibility(
            (eligible) => {
                isApiKeyOfferEligible = eligible;
                UpdateApiKeyOfferBadge();
            },
            (error) => {
                Debug.LogWarning($"[SubscriptionUIManager] Failed to check API key offer: {error}");
                isApiKeyOfferEligible = false;
            }
        );
    }

    private void UpdateApiKeyOfferBadge()
    {
        if (apiKeyOfferBadge != null)
        {
            apiKeyOfferBadge.SetActive(isApiKeyOfferEligible);
        }
        if (apiKeyOfferBadgeText != null && isApiKeyOfferEligible)
        {
            apiKeyOfferBadgeText.text = "1カ月無料";
        }
    }

    /// <summary>
    /// UIの表示を更新します。
    /// </summary>
    public void RefreshUI()
    {
        // 1. 価格の更新 (ストアから取得できていれば)
        if (IAPManager.Instance != null && IAPManager.Instance.IsInitialized())
        {
            if (standardPriceText != null) 
                standardPriceText.text = IAPManager.Instance.GetProductPrice(IAPManager.PRODUCT_STANDARD_MONTHLY);
            
            if (premiumPriceText != null) 
                premiumPriceText.text = IAPManager.Instance.GetProductPrice(IAPManager.PRODUCT_PREMIUM_MONTHLY);

            if (ultimatePriceText != null) 
                ultimatePriceText.text = IAPManager.Instance.GetProductPrice(IAPManager.PRODUCT_ULTIMATE_MONTHLY);
        }

        // 2. 現在のプランによる表示切替
        if (SubscriptionManager.Instance != null)
        {
            var plan = SubscriptionManager.Instance.CurrentPlan;
            
            if (currentPlanText != null)
                currentPlanText.text = $"現在のプラン: {plan}";

            bool isFree = plan == PlanType.Free;
            bool isStandard = plan == PlanType.Standard;
            bool isPremium = plan == PlanType.Premium;
            bool isUltimate = plan == PlanType.Ultimate;
            bool isPremiumOrHigher = isPremium || isUltimate;

            // Standard Button Logic
            if (buyStandardButton != null)
            {
                var txt = buyStandardButton.GetComponentInChildren<TextMeshProUGUI>();
                if (isFree)
                {
                    buyStandardButton.interactable = true;
                    if(txt) txt.text = "プランに加入";
                }
                else if (isStandard)
                {
                    buyStandardButton.interactable = false;
                    if(txt) txt.text = "加入済み";
                }
                else if (isPremiumOrHigher)
                {
                    // Downgrade not supported
                    buyStandardButton.interactable = false;
                    if(txt) txt.text = "-";
                }
            }

            // Premium Button Logic
            if (buyPremiumButton != null)
            {
                var txt = buyPremiumButton.GetComponentInChildren<TextMeshProUGUI>();
                if (isFree || isStandard)
                {
                    buyPremiumButton.interactable = true;
                    if(txt) txt.text = isStandard ? "アップグレード" : "プランに加入";
                }
                else if (isPremium)
                {
                    buyPremiumButton.interactable = false;
                    if(txt) txt.text = "加入済み";
                }
                else if (isUltimate)
                {
                    // Downgrade not supported
                    buyPremiumButton.interactable = false;
                    if(txt) txt.text = "-";
                }
            }

            // Ultimate Button Logic
            if (buyUltimateButton != null)
            {
                var txt = buyUltimateButton.GetComponentInChildren<TextMeshProUGUI>();
                if (isUltimate)
                {
                    buyUltimateButton.interactable = false;
                    if(txt) txt.text = "加入済み";
                }
                else
                {
                    buyUltimateButton.interactable = true;
                    if(txt) txt.text = (isPremium || isStandard) ? "アップグレード" : "プランに加入";
                }
            }
            
            if (premiumBadge != null) premiumBadge.SetActive(isPremiumOrHigher);
        }
    }

    private void OnBuyClicked(string productId)
    {
        ClearStatusMessage();  // 購入開始時にエラーメッセージをクリア
        
        if (IAPManager.Instance == null)
        {
            ShowErrorMessage("IAPManager not found.");
            return;
        }

        // ★ StandardプランでAPIキー特典が利用可能な場合はOffer付き購入
        if (productId == IAPManager.PRODUCT_STANDARD_MONTHLY && isApiKeyOfferEligible)
        {
            Debug.Log("[SubscriptionUIManager] Using API key offer for Standard plan.");
            IAPManager.Instance.BuyProductWithOffer(productId, "apikey-registration-trial");
            
            // 特典使用済みフラグをFirestoreに保存
            MarkApiKeyOfferUsed();
        }
        else
        {
            IAPManager.Instance.BuyProduct(productId);
        }
    }

    private void MarkApiKeyOfferUsed()
    {
        if (FirestoreManager.Instance == null) return;
        
        // Firestoreに使用済みフラグを保存（Cloud Functionsで検証時に確認される）
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;
        
        var db = Firebase.Firestore.FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(user.UserId).Collection("subscription").Document("status");
        
        docRef.SetAsync(new System.Collections.Generic.Dictionary<string, object> {
            { "apikey_offer_used", true },
            { "apikey_offer_eligible", false },
            { "apikey_offer_used_at", Firebase.Firestore.FieldValue.ServerTimestamp }
        }, Firebase.Firestore.SetOptions.MergeAll);
    }

    private void OnRestoreClicked()
    {
        // Restore logic (mainly for iOS)
        // Unity IAP handles this typically via extension, omitted for brevity but button exists.
        Debug.Log("Restore Transactions clicked.");
    }

    /// <summary>
    /// エラーメッセージをステータステキストに表示します（現在のプラン表示は維持）
    /// </summary>
    private void ShowErrorMessage(string error)
    {
        Debug.LogError($"Purchase Error: {error}");
        
        // ★ エラーはstatusTextに表示（currentPlanTextは変更しない）
        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.text = $"エラー: {error}";
            statusText.color = Color.red;
        }
        else
        {
            // statusTextがない場合はcurrentPlanTextにフォールバック
            if (currentPlanText != null)
            {
                currentPlanText.text = $"エラー: {error}";
            }
        }
    }

    /// <summary>
    /// ステータスメッセージをクリアします
    /// </summary>
    private void ClearStatusMessage()
    {
        if (statusText != null)
        {
            statusText.text = "";
            statusText.gameObject.SetActive(false);
        }
    }
}
