using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// チュートリアルの進行管理を行うシングルトン。
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TutorialOverlayController overlayControllerPrefab;
    private TutorialOverlayController currentOverlay;

    [Header("Settings")]
    [SerializeField] private bool enableTutorial = true;

    // PlayerPrefs keys removed - manual trigger only, no completion tracking needed

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private TutorialOverlayController GetOverlay()
    {
        if (currentOverlay == null)
        {
            // シーン内に既存のものがないか探す
            currentOverlay = FindObjectOfType<TutorialOverlayController>();
            
            // なければPrefabから生成 (Canvasの子にする必要があるため、Canvasを探す)
            if (currentOverlay == null && overlayControllerPrefab != null)
            {
                Canvas canvas = FindObjectOfType<Canvas>();
                if (canvas != null)
                {
                    currentOverlay = Instantiate(overlayControllerPrefab, canvas.transform);
                    // 最前面へ
                    currentOverlay.transform.SetAsLastSibling();
                }
            }
        }
        return currentOverlay;
    }

    /// <summary>
    /// メイン画面のチュートリアルを開始します。
    /// </summary>
    public void StartMainTutorial(MainUIManager uiManager)
    {
        if (!enableTutorial) return;
        // Manual start ignores 'done' status
        StartCoroutine(RunMainTutorial(uiManager));
    }

    /// <summary>
    /// 設定画面のチュートリアルを開始します。
    /// </summary>
    public void StartSettingsTutorial(SettingsManager settingsManager)
    {
        if (!enableTutorial) return;
        // Manual start ignores 'done' status
        StartCoroutine(RunSettingsTutorial(settingsManager));
    }

    private IEnumerator RunMainTutorial(MainUIManager ui)
    {
        // ★ Reset flags at start to prevent skip bug on re-trigger
        stepNextTriggered = false;
        stepSkipTriggered = false;
        
        var overlay = GetOverlay();
        if (overlay == null || ui == null) yield break;

        // Step 1: Welcome
        yield return ShowStepRoutine(overlay, null, "コトノイロへようこそ。\nあなたの声を感情の色に変えてみましょう。");

        // Step 2: Rec Button
        yield return ShowStepRoutine(overlay, ui.RecButtonRect, "このボタンを押すと録音が始まります。\n（今回は押しません）");

        // Step 3: Gallery Button
        yield return ShowStepRoutine(overlay, ui.GalleryButtonRect, "毎月アートは保存され、\n過去の記録はここから確認できます。");

        // Step 4: Settings Button
        yield return ShowStepRoutine(overlay, ui.SettingsButtonRect, "マイク音量やプランの確認は\nここから行えます。");

        // Step 5: Result Area (Assuming central area)
        // MainUIManager doesn't have a specific rect for 'Result Area'. We can use a generic central position or pass null.
        // Or ui.PanelDetailsRect if accessible. Let's use null for center or add a rect later.
        yield return ShowStepRoutine(overlay, null, "分析結果はここに波紋として表示されます。\n感情が色と形で表現されます。");

        // Step 6: Finish
        yield return ShowStepRoutine(overlay, null, "さあ、あなたの今の気持ちを\n記録してみましょう。");

        // Complete
        overlay.Hide();
    }

    private IEnumerator RunSettingsTutorial(SettingsManager settings)
    {
        // ★ Reset flags at start to prevent skip bug on re-trigger
        stepNextTriggered = false;
        stepSkipTriggered = false;
        
        var overlay = GetOverlay();
        if (overlay == null || settings == null) yield break;

        // Step 1: Mic Settings
        // Use Gain Slider as a proxy for the mic settings area
        yield return ShowStepRoutine(overlay, settings.GainSliderRect, "使用するマイクと\n入力音量を調整できます。");

        // Step 2: Monitoring
        yield return ShowStepRoutine(overlay, settings.VoiceLevelBarRect, "声を出して、ゲージが動くことを\n確認してください。\n緑色になるのが目安です。");

        // Step 3: API Key
        yield return ShowStepRoutine(overlay, settings.ApiKeyInputRect, "必要に応じてAPIキーを設定できます。\n設定すると有料プランの特典も獲得できます。");

        // Step 4: Save
        yield return ShowStepRoutine(overlay, settings.SaveButtonRect, "変更した設定は\n必ず保存してください。");

        // Complete
        overlay.Hide();
    }

    private bool stepNextTriggered = false;
    private bool stepSkipTriggered = false;

    private IEnumerator ShowStepRoutine(TutorialOverlayController overlay, RectTransform target, string text)
    {
        if (stepSkipTriggered) yield break; // Already skipped

        stepNextTriggered = false;
        
        overlay.ShowStep(target, text, 
            () => { stepNextTriggered = true; }, 
            () => { stepSkipTriggered = true; stepNextTriggered = true; }
        );

        // Wait for input
        while (!stepNextTriggered)
        {
            yield return null;
        }
        
        // If skipped, hide overlay and let loop finish
        if (stepSkipTriggered)
        {
            overlay.Hide();
        }
    }
    
    // Debug method - no longer needed but kept for potential future use
    public void ResetTutorialStatus()
    {
        Debug.Log("Tutorial: Manual trigger only, no status to reset.");
    }
}
