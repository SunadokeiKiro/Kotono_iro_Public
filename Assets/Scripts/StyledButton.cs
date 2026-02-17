using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// ボタンにスタイルとマイクロアニメーションを適用するコンポーネント。
/// タップ時のスプリングバウンスと、ポインターホバー時のスケールアップを実現します。
/// 「言のイロ」の瞑想的な世界観に合わせた、ゆったりとしたイージングを使用します。
/// </summary>
[RequireComponent(typeof(Button))]
public class StyledButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                            IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("アイコンのみのボタン（背景フレームなし）の場合チェック")]
    public bool isIconOnly = false;

    [Header("Animation Settings")]
    [Tooltip("タップ時のスケールバウンスを有効にする")]
    [SerializeField] private bool enableBounce = true;

    [Tooltip("ホバー時のスケールアップを有効にする")]
    [SerializeField] private bool enableHoverScale = true;

    // アニメーション定数
    private const float PressScale = 0.92f;       // タップ時の縮小
    private const float BounceScale = 1.05f;      // バウンス時のオーバーシュート
    private const float HoverScale = 1.05f;       // ホバー時のスケール
    private const float NormalScale = 1.0f;       // 通常スケール
    private const float PressDuration = 0.1f;     // 押下アニメーション時間
    private const float BounceDuration = 0.2f;    // バウンスアニメーション時間
    private const float SettleDuration = 0.15f;   // 落ち着きアニメーション時間
    private const float HoverDuration = 0.2f;     // ホバーアニメーション時間

    private RectTransform rectTransform;
    private Coroutine currentAnimation;
    private bool isPointerInside = false;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        Button btn = GetComponent<Button>();
        UIStyler.ApplyStyleToButton(btn, isIconOnly);
    }

    // ======================================================================
    // Pointer Event Handlers
    // ======================================================================

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enableBounce || !IsInteractable()) return;
        StopCurrentAnimation();
        currentAnimation = StartCoroutine(AnimateScale(PressScale, PressDuration));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!enableBounce || !IsInteractable()) return;
        StopCurrentAnimation();
        // スプリングバウンス: 押下 → オーバーシュート → 通常 or ホバースケール
        float targetScale = (isPointerInside && enableHoverScale) ? HoverScale : NormalScale;
        currentAnimation = StartCoroutine(SpringBounce(targetScale));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        if (!enableHoverScale || !IsInteractable()) return;
        StopCurrentAnimation();
        currentAnimation = StartCoroutine(AnimateScale(HoverScale, HoverDuration));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        if (!enableHoverScale || !IsInteractable()) return;
        StopCurrentAnimation();
        currentAnimation = StartCoroutine(AnimateScale(NormalScale, HoverDuration));
    }

    // ======================================================================
    // Animation Coroutines
    // ======================================================================

    /// <summary>
    /// スムーズなイージングでスケールを変更します。
    /// </summary>
    private IEnumerator AnimateScale(float targetScale, float duration)
    {
        if (rectTransform == null) yield break;

        float startScale = rectTransform.localScale.x;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            // SmoothStep イージング（ゆったりとした加減速）
            t = t * t * (3f - 2f * t);

            float scale = Mathf.Lerp(startScale, targetScale, t);
            rectTransform.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        rectTransform.localScale = new Vector3(targetScale, targetScale, 1f);
        currentAnimation = null;
    }

    /// <summary>
    /// スプリングバウンスアニメーション: オーバーシュート → ターゲットスケール
    /// </summary>
    private IEnumerator SpringBounce(float finalScale)
    {
        // Phase 1: オーバーシュート
        yield return AnimateScale(BounceScale, BounceDuration);
        // Phase 2: ターゲットに落ち着く
        yield return AnimateScale(finalScale, SettleDuration);
    }

    // ======================================================================
    // Utility
    // ======================================================================

    private void StopCurrentAnimation()
    {
        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            currentAnimation = null;
        }
    }

    private bool IsInteractable()
    {
        Button btn = GetComponent<Button>();
        return btn != null && btn.interactable;
    }

    void OnDisable()
    {
        // 無効化時にスケールをリセット
        StopCurrentAnimation();
        if (rectTransform != null)
        {
            rectTransform.localScale = Vector3.one;
        }
    }

    void OnValidate()
    {
        // Editor-time preview (optional)
    }
}
