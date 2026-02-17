using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 録音中にボタンがシアンで静かに脈打つグロー演出を提供します。
/// 深海の中で呼吸するような穏やかなアニメーションです。
/// </summary>
public class RecordButtonGlow : MonoBehaviour
{
    [Header("Glow Settings")]
    [Tooltip("グロー色（シアン系）")]
    [SerializeField] private Color glowColor = new Color(0.3f, 0.85f, 0.95f, 0.7f);
    [Tooltip("呼吸サイクルの時間（秒）")]
    [SerializeField] private float breathDuration = 5.0f;
    [Tooltip("グロー時の最小透明度")]
    [SerializeField] private float alphaMin = 0.3f;
    [Tooltip("グロー時の最大透明度")]
    [SerializeField] private float alphaMax = 0.8f;

    private Image buttonImage;
    private Color normalColor;
    private bool isRecording = false;
    private Coroutine glowCoroutine;

    void Awake()
    {
        buttonImage = GetComponent<Image>();
        if (buttonImage != null)
        {
            normalColor = buttonImage.color;
        }
    }

    /// <summary>
    /// 録音状態を設定します。trueでグロー開始、falseで停止。
    /// </summary>
    public void SetRecording(bool recording)
    {
        if (isRecording == recording) return;
        isRecording = recording;

        if (isRecording)
        {
            StartGlow();
        }
        else
        {
            StopGlow();
        }
    }

    private void StartGlow()
    {
        if (glowCoroutine != null)
        {
            StopCoroutine(glowCoroutine);
        }
        glowCoroutine = StartCoroutine(GlowBreathLoop());
    }

    private void StopGlow()
    {
        if (glowCoroutine != null)
        {
            StopCoroutine(glowCoroutine);
            glowCoroutine = null;
        }

        // 通常色にスムーズに戻す
        StartCoroutine(FadeToNormal());
    }

    /// <summary>
    /// シアンで静かに呼吸するグローアニメーション（無限ループ）。
    /// </summary>
    private IEnumerator GlowBreathLoop()
    {
        if (buttonImage == null) yield break;

        while (true)
        {
            float elapsed = 0f;
            while (elapsed < breathDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / breathDuration;
                float sineT = (Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f;
                float alpha = Mathf.Lerp(alphaMin, alphaMax, sineT);
                buttonImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
                yield return null;
            }
        }
    }

    /// <summary>
    /// 通常色にスムーズに戻すフェード。
    /// </summary>
    private IEnumerator FadeToNormal()
    {
        if (buttonImage == null) yield break;

        Color current = buttonImage.color;
        float duration = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); // SmoothStep
            buttonImage.color = Color.Lerp(current, normalColor, t);
            yield return null;
        }

        buttonImage.color = normalColor;
    }

    void OnDisable()
    {
        if (glowCoroutine != null)
        {
            StopCoroutine(glowCoroutine);
            glowCoroutine = null;
        }
        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }
    }
}
