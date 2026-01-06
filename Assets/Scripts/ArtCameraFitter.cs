// Scripts/ArtCameraFitter.cs
using UnityEngine;

/// <summary>
/// カメラのビューポート矩形とアートの半径に基づき、
/// アートが常に画面にぴったり収まるようカメラの距離を自動調整します。
/// 縦横の向きが変わっても自動で対応します。
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteInEditMode] // エディタ上でも動作確認できるようにする
public class ArtCameraFitter : MonoBehaviour
{
    [Tooltip("アートの半径（GameControllerのartRadiusと合わせる）")]
    [SerializeField] private float artRadius = 10f;

    [Tooltip("アートと画面の端との間に空ける余白（%）")]
    [SerializeField] [Range(0f, 1f)] private float padding = 0.1f; // 10%の余白

    private Camera cam;

    // Conflict Fix: This script was overriding SimpleCameraController every frame.
    // Changed to manual call only.
    public void FitCamera()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }

        // 1. ビューポート（実際に描画される矩形）のピクセル単位でのアスペクト比を計算
        float viewWidthPixels = Screen.width * cam.rect.width;
        float viewHeightPixels = Screen.height * cam.rect.height;
        float viewportAspect = viewWidthPixels / viewHeightPixels;

        // 2. カメラの垂直・水平FOV（視野角）をラジアンで計算
        float vFovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float hFovRad = 2 * Mathf.Atan(Mathf.Tan(vFovRad / 2) * viewportAspect);

        // 3. アートを収めるために必要な距離を、垂直方向と水平方向それぞれで計算
        float distanceForVerticalFit = artRadius / Mathf.Tan(vFovRad / 2);
        float distanceForHorizontalFit = artRadius / Mathf.Tan(hFovRad / 2);

        // 4. 縦横両方が収まるよう、より「遠い」方を採用
        float optimalDistance = Mathf.Max(distanceForVerticalFit, distanceForHorizontalFit);

        // 5. 余白(padding)を考慮して、さらに少しだけカメラを引く
        optimalDistance /= (1f - padding);

        // 6. カメラの位置を更新（アートは(0,0,0)にある前提）
        transform.position = new Vector3(0, 0, -optimalDistance);
    }
}