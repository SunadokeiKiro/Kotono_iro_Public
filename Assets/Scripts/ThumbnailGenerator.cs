// Scripts/ThumbnailGenerator.cs
using UnityEngine;
using System.IO;

/// <summary>
/// 指定されたGameObjectのサムネイル画像を生成し、PNGファイルとして保存する静的ユーティリティクラス。
/// </summary>
public static class ThumbnailGenerator
{
    private static Camera thumbnailCamera;
    private static RenderTexture renderTexture;

    /// <summary>
    /// サムネイル撮影用のカメラとRenderTextureを準備または再設定します。
    /// </summary>
    private static void SetupThumbnailCamera(int width, int height)
    {
        if (thumbnailCamera == null)
        {
            GameObject camObj = new GameObject("ThumbnailCamera");
            // シーンを切り替えても破棄されないようにし、ヒエラルキーに表示されないようにする
            Object.DontDestroyOnLoad(camObj);
            camObj.hideFlags = HideFlags.HideAndDontSave;

            thumbnailCamera = camObj.AddComponent<Camera>();
            thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;
            thumbnailCamera.backgroundColor = Color.clear; // 背景を透過
            thumbnailCamera.cullingMask = LayerMask.GetMask("Default"); // "Default"レイヤーのみを撮影
            thumbnailCamera.orthographic = true;
            thumbnailCamera.enabled = false; // 普段は無効にしておく
        }

        // RenderTextureのサイズが異なる場合、または未作成の場合に再生成
        if (renderTexture == null || renderTexture.width != width || renderTexture.height != height)
        {
            if (renderTexture != null) renderTexture.Release();
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
        }
        thumbnailCamera.targetTexture = renderTexture;
    }

    /// <summary>
    /// 指定されたGameObjectのサムネイルを撮影し、指定パスにPNGで保存します。
    /// </summary>
    /// <returns>保存に成功した場合はtrue、失敗した場合はfalse</returns>
    public static bool CaptureAndSaveThumbnail(GameObject targetObject, string filePath, int width = 256, int height = 256)
    {
        if (targetObject == null)
        {
            Debug.LogError("ThumbnailGenerator: Target object is null.");
            return false;
        }

        SetupThumbnailCamera(width, height);

        // 対象オブジェクトとその子オブジェクトのレイヤーを一時的に撮影用レイヤーに変更
        int originalLayer = targetObject.layer;
        RecursiveSetLayer(targetObject.transform, LayerMask.NameToLayer("Default"));

        // カメラの位置とサイズを調整してオブジェクト全体を捉える
        Bounds bounds = CalculateBounds(targetObject);
        // 少し後ろに引いてオブジェクト全体が映るように調整
        thumbnailCamera.transform.position = bounds.center + new Vector3(0, 0, -Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1.5f);
        thumbnailCamera.transform.LookAt(bounds.center);
        // オブジェクトの最も大きい辺が収まるようにカメラサイズを調整
        thumbnailCamera.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 0.6f;

        // 撮影実行
        thumbnailCamera.Render();

        // RenderTextureからTexture2Dにピクセルを読み込み
        RenderTexture.active = renderTexture;
        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();
        RenderTexture.active = null;

        // 対象オブジェクトのレイヤーを元に戻す
        RecursiveSetLayer(targetObject.transform, originalLayer);

        // PNGとしてファイルに保存
        try
        {
            byte[] bytes = texture2D.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            Debug.Log($"Thumbnail saved to: {filePath}");
            Object.Destroy(texture2D); // メモリリークを防ぐためにTexture2Dを破棄
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save thumbnail: {e.Message}");
            Object.Destroy(texture2D);
            return false;
        }
    }

    /// <summary>
    /// オブジェクトとその子のレイヤーを再帰的に設定します。
    /// </summary>
    private static void RecursiveSetLayer(Transform trans, int layer)
    {
        trans.gameObject.layer = layer;
        foreach (Transform child in trans)
        {
            RecursiveSetLayer(child, layer);
        }
    }
    
    /// <summary>
    /// GameObjectとその子のRendererをすべて含んだ包括的なバウンディングボックスを計算します。
    /// </summary>
    private static Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for(int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    /// <summary>
    /// アプリケーション終了時などにリソースを解放します。
    /// </summary>
    public static void Cleanup()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Object.Destroy(renderTexture);
            renderTexture = null;
        }
        if (thumbnailCamera != null)
        {
            Object.Destroy(thumbnailCamera.gameObject);
            thumbnailCamera = null;
        }
    }
}