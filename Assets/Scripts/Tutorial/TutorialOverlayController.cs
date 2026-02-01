using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// チュートリアルのオーバーレイ表示（ハイライト枠、説明文、ボタン）を制御します。
/// </summary>
public class TutorialOverlayController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform highlightFrame;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private RectTransform overlayCanvasRect; // このオーバーレイ自体のRectTransform

    private System.Action onNextCallback;
    private System.Action onSkipCallback;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (overlayCanvasRect == null) overlayCanvasRect = GetComponent<RectTransform>();

        if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
        if (skipButton != null) skipButton.onClick.AddListener(OnSkipClicked);

        // 初期状態は非表示
        Hide();
        
        // ハイライト枠がなければ作成する（簡易的な黄色い枠）
        if (highlightFrame == null)
        {
            // CreateDefaultHighlightFrame(); // 実装簡略化のため、Inspectorでの設定を推奨
            Debug.LogWarning("TutorialOverlayController: Highlight Frame is missing.");
        }
    }

    /// <summary>
    /// 指定されたターゲットをハイライトし、説明文を表示します。
    /// </summary>
    /// <param name="target">ハイライトするUI要素（nullの場合は画面中央などをデフォルトとする）</param>
    /// <param name="text">説明文</param>
    /// <param name="onNext">「次へ」ボタンが押された時のコールバック</param>
    /// <param name="onSkip">「スキップ」ボタンが押された時のコールバック</param>
    public void ShowStep(RectTransform target, string text, System.Action onNext, System.Action onSkip)
    {
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        instructionText.text = text;
        onNextCallback = onNext;
        onSkipCallback = onSkip;

        if (target != null)
        {
            // ターゲットの位置とサイズに合わせてハイライト枠を移動・リサイズ
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            Camera cam = null;
            if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = rootCanvas.worldCamera;
            }

            Vector3 worldPos = target.position;
            Vector2 localPos;
            
            // ターゲットのスクリーン座標を取得
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPos);

            // スクリーン座標をオーバーレイ内のローカル座標に変換
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlayCanvasRect, 
                screenPoint, 
                cam, 
                out localPos
            );

            highlightFrame.anchoredPosition = localPos;
            
            // サイズ調整（ターゲットのサイズ + 余白）
            // ターゲットのサイズもスケールを考慮して取得する必要がある場合があるが、通常はrect.width/heightで十分
            highlightFrame.sizeDelta = new Vector2(target.rect.width + 20, target.rect.height + 20); 
            
            highlightFrame.gameObject.SetActive(true);
        }
        else
        {
            // ターゲットがない場合（Welcomeメッセージなど）は枠を隠す
            highlightFrame.gameObject.SetActive(false);
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    private void OnNextClicked()
    {
        onNextCallback?.Invoke();
    }

    private void OnSkipClicked()
    {
        onSkipCallback?.Invoke();
    }
}
