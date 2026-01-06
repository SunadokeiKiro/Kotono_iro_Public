// Scripts/LoadingSceneController.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// LoadingSceneのUIを制御し、非同期でのシーンロードを実行します。
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;

    void Start()
    {
        if (progressBar == null || progressText == null)
        {
            Debug.LogError("UI references are not set in the inspector!");
            return;
        }

        // 非同期ロードとフェードのコルーチンを開始
        StartCoroutine(LoadAndFade());
    }
    
    private IEnumerator LoadAndFade()
    {
        // 1. まずはロード画面にフェードイン
        yield return StartCoroutine(SceneTransitionManager.Instance.Fade(0f)); // 画面を明るくする

        // 2. 非同期で次のシーンのロードを開始
        string sceneToLoad = SceneTransitionManager.GetSceneToLoad();
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneToLoad);
        operation.allowSceneActivation = false; // ロードが完了しても自動でシーンを切り替えない

        // 3. ロードの進捗をUIに反映
        while (!operation.isDone)
        {
            // isDoneになるのはprogressが0.9の時なので、それで進捗を計算
            float progress = Mathf.Clamp01(operation.progress / 0.9f);
            UpdateProgress(progress);
            
            // ロードがほぼ完了したらループを抜ける
            if (operation.progress >= 0.9f)
            {
                break;
            }

            yield return null;
        }

        UpdateStatus("Ready!");

        // 進捗を100%にする
        UpdateProgress(1f);
        yield return new WaitForSeconds(0.3f); // 少しだけ待ってからフェードアウト

        // 4. 次のシーンに切り替える準備としてフェードアウト
        yield return StartCoroutine(SceneTransitionManager.Instance.Fade(1f)); // 画面を暗くする

        // 5. シーンのアクティベート（切り替え）を許可
        operation.allowSceneActivation = true;
    }


    /// <summary>
    /// ロードの進捗状況をUIに反映させるためのコールバックメソッド。
    /// </summary>
    /// <param name="progress">0.0から1.0の進捗</param>
    private void UpdateProgress(float progress)
    {
        progressBar.value = progress;
        progressText.text = $"Loading... {(progress * 100f):F0}%";
    }

    private void UpdateStatus(string message)
    {
        if (progressText != null) progressText.text = message;
    }
}