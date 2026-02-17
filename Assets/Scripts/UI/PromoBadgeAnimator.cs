using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// プロモーションバッジに深海テーマに合う控えめなアニメーションを適用します。
/// 「深海の中で静かに光る」ような、瞑想的で穏やかな演出です。
///
/// 使用方法：apiKeyOfferBadge等のGameObjectにアタッチするだけで自動動作します。
/// </summary>
public class PromoBadgeAnimator : MonoBehaviour
{
    [Header("Breath Fade（呼吸フェード）")]
    [Tooltip("透明度の下限")]
    [SerializeField] private float alphaMin = 0.6f;
    [Tooltip("透明度の上限")]
    [SerializeField] private float alphaMax = 1.0f;
    [Tooltip("呼吸1サイクルの時間（秒）")]
    [SerializeField] private float breathDuration = 4.0f;

    [Header("Soft Glow（ソフトグロー）")]
    [Tooltip("グロー色（暗め）— シアンの深い色")]
    [SerializeField] private Color glowDark = new Color(0.2f, 0.6f, 0.7f, 1.0f);
    [Tooltip("グロー色（明るめ）— シアンの明るい色")]
    [SerializeField] private Color glowBright = new Color(0.4f, 0.9f, 0.95f, 1.0f);
    [Tooltip("色変化サイクルの時間（秒）")]
    [SerializeField] private float glowDuration = 5.0f;

    [Header("Fade In（フェードイン）")]
    [Tooltip("表示時のフェードイン時間（秒）")]
    [SerializeField] private float fadeInDuration = 0.8f;

    private CanvasGroup canvasGroup;
    private TextMeshProUGUI badgeText;
    private Image backgroundImage;

    void OnEnable()
    {
        badgeText = GetComponentInChildren<TextMeshProUGUI>();
        backgroundImage = GetComponent<Image>();

        // CanvasGroupがなければ自動追加（フェード制御用）
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        ApplyBadgeStyle();

        // フェードインで静かに現れる
        StartCoroutine(FadeIn());

        // 呼吸フェード（透明度の揺らぎ）
        StartCoroutine(BreathFadeLoop());

        // ソフトグロー（シアンの明暗）
        if (badgeText != null)
        {
            StartCoroutine(SoftGlowLoop());
        }
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
    }

    /// <summary>
    /// 深海テーマに合うスタイルを適用します。
    /// </summary>
    private void ApplyBadgeStyle()
    {
        if (backgroundImage != null)
        {
            backgroundImage.sprite = UIStyler.GetOrCreateRoundedSprite(16, 48);
            backgroundImage.type = Image.Type.Sliced;
            // 深海の暗い半透明背景
            backgroundImage.color = new Color(0.06f, 0.12f, 0.2f, 0.6f);
        }

        if (badgeText != null)
        {
            badgeText.color = glowDark;
            badgeText.fontStyle = TMPro.FontStyles.Bold;
        }
    }

    // ======================================================================
    // Animations — 深海テーマのアニメーション
    // ======================================================================

    /// <summary>
    /// CanvasGroupのalphaでフワッと現れるフェードイン。
    /// </summary>
    private IEnumerator FadeIn()
    {
        if (canvasGroup == null) yield break;

        canvasGroup.alpha = 0f;
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / fadeInDuration;
            // EaseOut — 最初に素早く、最後にゆっくり現れる
            t = 1f - (1f - t) * (1f - t);
            canvasGroup.alpha = t;
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// 透明度が静かに揺らぐ呼吸フェード（無限ループ）。
    /// </summary>
    private IEnumerator BreathFadeLoop()
    {
        if (canvasGroup == null) yield break;

        // フェードインが終わるまで待つ
        yield return new WaitForSecondsRealtime(fadeInDuration + 0.2f);

        while (true)
        {
            float elapsed = 0f;
            while (elapsed < breathDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / breathDuration;
                // Sin波で滑らかな呼吸
                float sineT = (Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f;
                canvasGroup.alpha = Mathf.Lerp(alphaMin, alphaMax, sineT);
                yield return null;
            }
        }
    }

    /// <summary>
    /// テキスト色をシアンの明暗で静かに揺らぐソフトグロー（無限ループ）。
    /// </summary>
    private IEnumerator SoftGlowLoop()
    {
        if (badgeText == null) yield break;

        while (true)
        {
            float elapsed = 0f;
            while (elapsed < glowDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / glowDuration;
                float sineT = (Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f;
                badgeText.color = Color.Lerp(glowDark, glowBright, sineT);
                yield return null;
            }
        }
    }
}
