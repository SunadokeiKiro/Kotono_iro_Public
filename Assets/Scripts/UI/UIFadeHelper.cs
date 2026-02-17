using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// パネルの表示/非表示をCanvasGroupフェードで行う汎用ユーティリティ。
/// 深海テーマに合うゆったりとしたトランジションを提供します。
/// </summary>
public static class UIFadeHelper
{
    private const float DefaultDuration = 0.3f;

    /// <summary>
    /// パネルをフェードインで表示します。
    /// CanvasGroupがなければ自動追加します。
    /// </summary>
    public static Coroutine FadeIn(MonoBehaviour host, GameObject panel, float duration = DefaultDuration)
    {
        if (panel == null || host == null) return null;

        panel.SetActive(true);
        CanvasGroup cg = GetOrAddCanvasGroup(panel);
        cg.alpha = 0f;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        return host.StartCoroutine(FadeCoroutine(cg, 0f, 1f, duration, null));
    }

    /// <summary>
    /// パネルをフェードアウトで非表示にします。
    /// 完了後にSetActive(false)を呼びます。
    /// </summary>
    public static Coroutine FadeOut(MonoBehaviour host, GameObject panel, float duration = DefaultDuration)
    {
        if (panel == null || host == null) return null;
        if (!panel.activeSelf) return null; // 既に非表示

        CanvasGroup cg = GetOrAddCanvasGroup(panel);
        cg.blocksRaycasts = false;
        cg.interactable = false;

        return host.StartCoroutine(FadeCoroutine(cg, cg.alpha, 0f, duration, () =>
        {
            panel.SetActive(false);
        }));
    }

    /// <summary>
    /// 即座に表示します（フェードなし）。
    /// </summary>
    public static void ShowImmediate(GameObject panel)
    {
        if (panel == null) return;
        panel.SetActive(true);
        CanvasGroup cg = GetOrAddCanvasGroup(panel);
        cg.alpha = 1f;
        cg.blocksRaycasts = true;
        cg.interactable = true;
    }

    /// <summary>
    /// 即座に非表示にします（フェードなし）。
    /// </summary>
    public static void HideImmediate(GameObject panel)
    {
        if (panel == null) return;
        CanvasGroup cg = GetOrAddCanvasGroup(panel);
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        panel.SetActive(false);
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = go.AddComponent<CanvasGroup>();
        }
        return cg;
    }

    private static IEnumerator FadeCoroutine(CanvasGroup cg, float from, float to, float duration, System.Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // SmoothStep イージング
            t = t * t * (3f - 2f * t);
            cg.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        cg.alpha = to;
        onComplete?.Invoke();
    }
}
