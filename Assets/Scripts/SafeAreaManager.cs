// Scripts/SafeAreaManager.cs
using UnityEngine;
using System.Collections;

/// <summary>
/// UI要素をScreen.safeArea（ノッチなどを避けた安全な表示領域）に適応させるためのクラス。
/// アタッチされたRectTransformのアンカーとオフセットを自動調整します。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaManager : MonoBehaviour
{
    private RectTransform panel;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private ScreenOrientation lastScreenOrientation;

    void Awake()
    {
        panel = GetComponent<RectTransform>();
        lastScreenOrientation = Screen.orientation;
        
        // 初回適用
        ApplySafeArea();
    }

    void Update()
    {
        // セーフエリアまたは画面の向きが変わった場合にのみ適用処理を呼び出す
        if (Screen.safeArea != lastSafeArea || Screen.orientation != lastScreenOrientation)
        {
            ApplySafeArea();
        }
    }

    /// <summary>
    /// 現在のScreen.safeAreaに基づいてRectTransformを調整します。
    /// </summary>
    void ApplySafeArea()
    {
        Rect safeArea = Screen.safeArea;

        // 更新情報を記録
        lastSafeArea = safeArea;
        lastScreenOrientation = Screen.orientation;
        
        // アンカーを画面の四隅に設定（ストレッチ）
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;

        // safeAreaに基づいてオフセットを計算
        // offsetMin: 左端と下端がsafeAreaの左下までどれだけずれているか
        Vector2 offsetMin = safeArea.position;
        
        // offsetMax: 右端と上端がsafeAreaの右上までどれだけずれているか（負の値になる）
        Vector2 offsetMax = -(new Vector2(Screen.width, Screen.height) - (safeArea.position + safeArea.size));
        
        panel.offsetMin = offsetMin;
        panel.offsetMax = offsetMax;
    }
}