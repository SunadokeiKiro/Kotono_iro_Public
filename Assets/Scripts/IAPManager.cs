using UnityEngine;
using UnityEngine.Purchasing;
using System;
using System.Collections;
using UnityEngine.Purchasing.Extension;

/// <summary>
/// Unity IAPを管理するクラス。
/// 購入処理の開始、ストアとの連携、レシートの取得を行います。
/// </summary>
public class IAPManager : MonoBehaviour, IDetailedStoreListener
{
    public static IAPManager Instance { get; private set; }

    private IStoreController controller;
    private IExtensionProvider extensions;

    // Product IDs (Must match Google Play / App Store)
    public const string PRODUCT_STANDARD_MONTHLY = "com.kotono_iro.standard_monthly";
    public const string PRODUCT_PREMIUM_MONTHLY = "com.kotono_iro.premium_monthly";
    public const string PRODUCT_ULTIMATE_MONTHLY = "com.kotono_iro.ultimate_monthly";

    // Callbacks
    public event Action OnPurchaseSuccess;
    public event Action<string> OnIAPPurchaseFailed;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializePurchasing();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void InitializePurchasing()
    {
        if (IsInitialized()) return;

        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

        // Add Products (Subscription)
        builder.AddProduct(PRODUCT_STANDARD_MONTHLY, ProductType.Subscription);
        builder.AddProduct(PRODUCT_PREMIUM_MONTHLY, ProductType.Subscription);
        builder.AddProduct(PRODUCT_ULTIMATE_MONTHLY, ProductType.Subscription);

        UnityPurchasing.Initialize(this, builder);
    }

    public bool IsInitialized()
    {
        return controller != null && extensions != null;
    }

    /// <summary>
    /// 購入処理を開始します。
    /// </summary>
    /// <param name="productId">商品ID</param>
    public void BuyProduct(string productId)
    {
        if (IsInitialized())
        {
            Product product = controller.products.WithID(productId);

            if (product != null && product.availableToPurchase)
            {
                Debug.Log($"[IAPManager] Purchasing: {product.definition.id}");
                controller.InitiatePurchase(product);
            }
            else
            {
                Debug.LogError("[IAPManager] Product not found or not available.");
                OnIAPPurchaseFailed?.Invoke("Product not available");
            }
        }
        else
        {
            Debug.LogError("[IAPManager] Not initialized.");
            OnIAPPurchaseFailed?.Invoke("IAP not initialized");
        }
    }

    /// <summary>
    /// 特典付きで購入処理を開始します（Google Playデベロッパー指定特典）
    /// Google Play Consoleで「デベロッパー指定」特典を設定すると、
    /// 購入フロー内でGoogle Playが自動的に特典を適用します。
    /// </summary>
    /// <param name="productId">商品ID</param>
    /// <param name="offerId">特典ID（ログ用、実際の適用はGoogle Play側で行われる）</param>
    public void BuyProductWithOffer(string productId, string offerId)
    {
        if (!IsInitialized())
        {
            Debug.LogError("[IAPManager] Not initialized.");
            OnIAPPurchaseFailed?.Invoke("IAP not initialized");
            return;
        }

        Product product = controller.products.WithID(productId);
        if (product == null || !product.availableToPurchase)
        {
            Debug.LogError("[IAPManager] Product not found or not available.");
            OnIAPPurchaseFailed?.Invoke("Product not available");
            return;
        }

        // ★ Google Playの「デベロッパー指定」特典は、購入フロー内で
        // Google Playが条件を確認し、自動的に特典を適用します。
        // クライアント側では通常の購入処理を行うだけでOK。
        Debug.Log($"[IAPManager] Purchasing with offer eligibility: {offerId}");
        Debug.Log($"[IAPManager] Note: Google Play will apply the offer if eligible.");
        controller.InitiatePurchase(product);
    }

    // --- IStoreListener Callbacks ---

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        Debug.Log("[IAPManager] IAP Initialized successfully.");
        this.controller = controller;
        this.extensions = extensions;
        
        // ★ サーバー権威型: Firestoreからプランを読み取るのみ
        // RTDNがFirestoreを自動更新するため、クライアントはFirestoreを信頼する
        SyncPlanFromFirestore();
    }
    
    /// <summary>
    /// Firestoreから現在のプランを読み取り、ローカル状態を更新します。
    /// サーバー権威型のため、クライアントは確認・検証を行わず、Firestoreの値を信頼します。
    /// </summary>
    private void SyncPlanFromFirestore()
    {
        if (FirestoreManager.Instance == null)
        {
            Debug.LogWarning("[IAPManager] FirestoreManager missing. Using local plan.");
            return;
        }

        Debug.Log("[IAPManager] Syncing plan from Firestore (Server-Authoritative)...");

        FirestoreManager.Instance.GetUserPlan(
            (plan) => {
                Debug.Log($"[IAPManager] Firestore Plan: {plan}");
                SetPlan(plan);
            },
            (error) => {
                Debug.LogWarning($"[IAPManager] Failed to get plan from Firestore: {error}. Using local plan.");
            }
        );
    }

    private void SetPlan(string plan)
    {
        if (SubscriptionManager.Instance != null)
        {
            SubscriptionManager.Instance.SetPlanFromString(plan);
            var subUI = UnityEngine.Object.FindFirstObjectByType<SubscriptionUIManager>();
            if (subUI != null) subUI.RefreshUI();
        }
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.LogError($"[IAPManager] IAP Initialization Failed: {error}");
    }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        Debug.LogError($"[IAPManager] IAP Initialization Failed: {error}, Message: {message}");
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        Debug.Log($"[IAPManager] Purchase Success: {args.purchasedProduct.definition.id}");

        // 1. Get Receipt
        string receipt = args.purchasedProduct.receipt;
        string productId = args.purchasedProduct.definition.id;

        // 2. Validate with Cloud Function
        if (FirestoreManager.Instance != null)
        {
            FirestoreManager.Instance.VerifyPurchase(receipt, productId, () =>
            {
                Debug.Log("[IAPManager] Receipt verification success. Plan updated in DB. Syncing local...");
                
                // Immediately sync plan from server
                if (SubscriptionManager.Instance != null)
                {
                    StartCoroutine(SubscriptionManager.Instance.RefreshPlanFromServer((success) => {
                         Debug.Log($"[IAPManager] Plan Sync Completed. Success: {success}");
                         // Notify UI *after* plan is synced
                         OnPurchaseSuccess?.Invoke();
                         
                         // Then sync quota (optional order, but good to do)
                         StartCoroutine(SubscriptionManager.Instance.SyncQuotaWithServer(null));
                    }));
                }
                else
                {
                    // Fallback if SM is missing (unlikely)
                    OnPurchaseSuccess?.Invoke();
                }

            }, (error) =>
            {
                Debug.LogError($"[IAPManager] Receipt verification failed: {error}");
                OnIAPPurchaseFailed?.Invoke("Verification failed: " + error);
            });
        }
        else
        {
             Debug.LogError("[IAPManager] FirestoreManager missing. Cannot verify purchase.");
        }

        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.LogError($"[IAPManager] Purchase Failed: {product.definition.id}, Reason: {failureReason}");
        OnIAPPurchaseFailed?.Invoke(failureReason.ToString());
    }
    public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
    {
         Debug.LogError($"[IAPManager] Purchase Failed: {product.definition.id}, Reason: {failureDescription.message}");
         OnIAPPurchaseFailed?.Invoke(failureDescription.message);
    }

    // --- Utility Methods ---
    
    public string GetProductPrice(string productId)
    {
        if (!IsInitialized()) return "";
        var product = controller.products.WithID(productId);
        return product != null ? product.metadata.localizedPriceString : "";
    }
}
