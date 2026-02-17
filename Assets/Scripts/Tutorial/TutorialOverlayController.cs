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
    private RectTransform textBackgroundRect; // テキスト背景用

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (overlayCanvasRect == null) overlayCanvasRect = GetComponent<RectTransform>();

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextClicked);
            UIStyler.ApplyStyleToButton(nextButton);
        }
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(OnSkipClicked);
            UIStyler.ApplyStyleToButton(skipButton);
        }

        // 初期状態: GOは有効なまま、CanvasGroupで非表示にする
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        
        if (highlightFrame == null)
        {
            Debug.LogWarning("TutorialOverlayController: Highlight Frame is missing.");
        }
        else
        {
            highlightFrame.gameObject.SetActive(false);
        }

        // テキスト背景を動的生成
        CreateTextBackground();
    }

    /// <summary>
    /// instructionTextの背後にグラスモーフィズム風背景パネルを生成
    /// </summary>
    private void CreateTextBackground()
    {
        if (instructionText == null) return;

        GameObject bgObj = new GameObject("TextBackground", typeof(RectTransform), typeof(Image));
        bgObj.transform.SetParent(instructionText.transform.parent, false);

        int textIndex = instructionText.transform.GetSiblingIndex();
        bgObj.transform.SetSiblingIndex(textIndex);

        textBackgroundRect = bgObj.GetComponent<RectTransform>();
        Image bgImage = bgObj.GetComponent<Image>();
        // ★ グラスモーフィズム風に変更
        UIStyler.ApplyGlassStyle(bgImage);
        bgImage.raycastTarget = false;
    }

    /// <summary>
    /// 指定されたターゲットをハイライトし、説明文を表示します。
    /// </summary>
    /// <param name="target">ハイライトするUI要素（nullの場合は画面中央などをデフォルトとする）</param>
    /// <param name="text">説明文</param>
    /// <param name="onNext">「次へ」ボタンが押された時のコールバック</param>
    /// <param name="onSkip">「スキップ」ボタンが押された時のコールバック</param>
    /// <param name="fullWidth">trueの場合、ターゲットのY座標で画面幅いっぱいに横一列ハイライト</param>
    public void ShowStep(RectTransform target, string text, System.Action onNext, System.Action onSkip, bool fullWidth = false)
    {
        gameObject.SetActive(true);
        // ★ フェードインでフワッと表示
        if (canvasGroup != null)
        {
            StopAllCoroutines();
            StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 1f, 0.3f, () =>
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }));
        }

        if (instructionText != null) instructionText.text = text;
        onNextCallback = onNext;
        onSkipCallback = onSkip;

        if (target != null && highlightFrame != null)
        {
            // ターゲットのワールド座標をオーバーレイのローカル座標に変換
            Vector3 localPos = overlayCanvasRect.InverseTransformPoint(target.position);
            
            // サイズ計算（ターゲットのワールドスケールを考慮）
            float scaleRatioX = (overlayCanvasRect.lossyScale.x > 0) ? target.lossyScale.x / overlayCanvasRect.lossyScale.x : 1f;
            float scaleRatioY = (overlayCanvasRect.lossyScale.y > 0) ? target.lossyScale.y / overlayCanvasRect.lossyScale.y : 1f;
            float h = target.rect.height * scaleRatioY + 20;

            if (fullWidth)
            {
                // 横一列モード: Y座標はターゲットに合わせ、X座標は中央、幅は画面いっぱい
                highlightFrame.anchoredPosition = new Vector2(0, localPos.y);
                highlightFrame.sizeDelta = new Vector2(overlayCanvasRect.rect.width, h);
            }
            else
            {
                // 通常モード: ターゲットの位置とサイズに合わせる
                highlightFrame.anchoredPosition = new Vector2(localPos.x, localPos.y);
                float w = target.rect.width * scaleRatioX + 20;
                highlightFrame.sizeDelta = new Vector2(w, h);
            }
            
            highlightFrame.gameObject.SetActive(true);

            // テキスト・ボタン位置調整: ハイライトと重ならないように配置
            AdjustUIPosition(localPos.y, h);
        }
        else if (highlightFrame != null)
        {
            // ターゲットがない場合（Welcomeメッセージなど）は枠を隠す
            highlightFrame.gameObject.SetActive(false);
            // テキスト・ボタンを画面中央に配置
            ResetUIPosition();
        }
    }

    /// <summary>
    /// ハイライト位置に応じてテキスト・ボタンを上下に配置（重なり防止＋画面外防止）
    /// </summary>
    private void AdjustUIPosition(float highlightY, float highlightHeight)
    {
        RectTransform textRect = (instructionText != null) ? instructionText.GetComponent<RectTransform>() : null;
        RectTransform nextRect = (nextButton != null) ? nextButton.GetComponent<RectTransform>() : null;
        RectTransform skipRect = (skipButton != null) ? skipButton.GetComponent<RectTransform>() : null;

        float margin = 30f;
        float buttonSpacing = 10f;
        float canvasHalfH = overlayCanvasRect.rect.height / 2f;

        // テキストの高さ
        float textH = (textRect != null) ? textRect.rect.height : 60f;
        // ボタンの高さ
        float btnH = (nextRect != null) ? nextRect.rect.height : 40f;
        // テキスト + ボタン全体の高さ
        float totalContentH = textH + buttonSpacing + btnH;

        // ハイライトの上端・下端（ローカル座標）
        float hlTop = highlightY + highlightHeight / 2f;
        float hlBottom = highlightY - highlightHeight / 2f;

        // テキスト群を置くY座標の中心を決定
        float contentCenterY;

        // 下側に置けるか判定
        float candidateBottom = hlBottom - margin - totalContentH;
        // 上側に置けるか判定
        float candidateTop = hlTop + margin + totalContentH;

        if (highlightY > 0)
        {
            // ハイライトが上側 → まず下に置く
            contentCenterY = hlBottom - margin - totalContentH / 2f;
            // 下端が画面外なら上に移動
            if (contentCenterY - totalContentH / 2f < -canvasHalfH + 20f)
            {
                contentCenterY = hlTop + margin + totalContentH / 2f;
            }
        }
        else
        {
            // ハイライトが下側 → まず上に置く
            contentCenterY = hlTop + margin + totalContentH / 2f;
            // 上端が画面外なら下に移動
            if (contentCenterY + totalContentH / 2f > canvasHalfH - 20f)
            {
                contentCenterY = hlBottom - margin - totalContentH / 2f;
            }
        }

        // 最終Clamp（画面端に収まるように）
        float minY = -canvasHalfH + totalContentH / 2f + 20f;
        float maxY = canvasHalfH - totalContentH / 2f - 20f;
        contentCenterY = Mathf.Clamp(contentCenterY, minY, maxY);

        // テキストを上、ボタンをその下に配置
        float textY = contentCenterY + totalContentH / 2f - textH / 2f;
        float btnY = contentCenterY - totalContentH / 2f + btnH / 2f;

        if (textRect != null)
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, textY);

        if (nextRect != null)
            nextRect.anchoredPosition = new Vector2(nextRect.anchoredPosition.x, btnY);

        if (skipRect != null)
            skipRect.anchoredPosition = new Vector2(skipRect.anchoredPosition.x, btnY);

        UpdateTextBackground();
    }

    /// <summary>
    /// テキスト・ボタン位置をデフォルト（中央付近）にリセット
    /// </summary>
    private void ResetUIPosition()
    {
        RectTransform textRect = (instructionText != null) ? instructionText.GetComponent<RectTransform>() : null;
        RectTransform nextRect = (nextButton != null) ? nextButton.GetComponent<RectTransform>() : null;
        RectTransform skipRect = (skipButton != null) ? skipButton.GetComponent<RectTransform>() : null;

        float textH = (textRect != null) ? textRect.rect.height : 60f;
        float btnH = (nextRect != null) ? nextRect.rect.height : 40f;
        float spacing = 15f;

        // テキスト + ボタンをセットで中央に配置
        float totalH = textH + spacing + btnH;
        float textY = totalH / 2f - textH / 2f;
        float btnY = -totalH / 2f + btnH / 2f;

        if (textRect != null)
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, textY);
        if (nextRect != null)
            nextRect.anchoredPosition = new Vector2(nextRect.anchoredPosition.x, btnY);
        if (skipRect != null)
            skipRect.anchoredPosition = new Vector2(skipRect.anchoredPosition.x, btnY);

        UpdateTextBackground();
    }

    /// <summary>
    /// テキスト背景パネルをテキストの位置・サイズに同期
    /// </summary>
    private void UpdateTextBackground()
    {
        if (textBackgroundRect == null || instructionText == null) return;
        RectTransform textRect = instructionText.GetComponent<RectTransform>();
        if (textRect == null) return;

        float padding = 20f;
        textBackgroundRect.anchoredPosition = textRect.anchoredPosition;
        textBackgroundRect.sizeDelta = new Vector2(
            textRect.rect.width + padding * 2,
            textRect.rect.height + padding
        );
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            // ★ フェードアウトでフワッと消える
            StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 0f, 0.3f, () =>
            {
                gameObject.SetActive(false);
            }));
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// CanvasGroupのalphaをフェードするコルーチン。
    /// </summary>
    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration, System.Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t); // SmoothStep
            cg.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }
        cg.alpha = to;
        onComplete?.Invoke();
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
