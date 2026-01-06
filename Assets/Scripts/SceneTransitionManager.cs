// Scripts/SceneTransitionManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// フェードイン・アウトと非同期シーンロードを管理するシングルトン。
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("フェード設定")]
    [SerializeField] private GameObject fadeCanvasPrefab; // フェード用UIのPrefab
    [SerializeField] private float fadeDuration = 0.5f;   // フェードにかかる時間

    private CanvasGroup fadeCanvasGroup;
    private static string sceneToLoad;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // フェード用CanvasをPrefabから生成し、永続化する
            if (fadeCanvasPrefab != null)
            {
                GameObject canvasObj = Instantiate(fadeCanvasPrefab);
                fadeCanvasGroup = canvasObj.GetComponent<CanvasGroup>();
                DontDestroyOnLoad(canvasObj);
            }
            else
            {
                Debug.LogError("Fade Canvas Prefabが設定されていません。");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 指定されたシーンにフェード付きで遷移します。
    /// </summary>
    /// <param name="sceneName">ロードしたいシーン名</param>
    public void LoadScene(string sceneName)
    {
        sceneToLoad = sceneName;
        StartCoroutine(Transition());
    }

    /// <summary>
    /// シーン遷移のメイン処理を行うコルーチン。
    /// </summary>
    private IEnumerator Transition()
    {
        // 1. フェードアウト
        yield return StartCoroutine(Fade(1f)); // 画面を暗くする

        // 2. ローディングシーンをロード
        SceneManager.LoadScene("LoadingScene");

        // ローディングシーン側でフェードイン処理が行われる
    }
    
    /// <summary>
    /// ターゲットのアルファ値に向けてフェード処理を行う。
    /// </summary>
    /// <param name="targetAlpha">目標のアルファ値 (0=透明, 1=不透明)</param>
    public IEnumerator Fade(float targetAlpha)
    {
        if (fadeCanvasGroup == null) yield break;

        fadeCanvasGroup.blocksRaycasts = true; // フェード中はUI操作をブロック
        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0;

        while (time < fadeDuration)
        {
            time += Time.unscaledDeltaTime; // Time.timeScaleに影響されない時間
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
        fadeCanvasGroup.blocksRaycasts = (targetAlpha == 1f); // 完全に不透明な時だけ操作をブロック
    }

    /// <summary>
    /// 現在ロード対象のシーン名を取得します。LoadingSceneControllerから使います。
    /// </summary>
    public static string GetSceneToLoad()
    {
        return sceneToLoad;
    }
}