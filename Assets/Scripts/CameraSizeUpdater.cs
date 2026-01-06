// Scripts/CameraSizeUpdater.cs
// Original Source: http://kan-kikuchi.hatenablog.com/entry/CameraSizeUpdater
// Created by kan.kikuchi on 2019.07.02.
// Modified for this project.

using UnityEngine;

/// <summary>
/// カメラのOrthographicSizeを、異なる画面アスペクト比に応じて自動で更新するクラス。
/// これにより、どのデバイスでも意図した画角を維持します。
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteInEditMode] // エディタ上での画面サイズ変更にも追従するため
public class CameraSizeUpdater : MonoBehaviour 
{
    private Camera _camera;
  
    // どの辺を基準にサイズを合わせるかの定義
    private enum BaseType 
    {
        Both,  // 縦と横、両方を考慮してはみ出さないように調整
        Width, // 横幅を基準に合わせる
        Height // 高さを基準に合わせる
    }

    [Tooltip("どの辺を基準にカメラサイズを調整するか")]
    [SerializeField]
    private BaseType _baseType = BaseType.Both;
  
    [Tooltip("開発時の基準となる画面解像度（幅）")]
    [SerializeField]
    private float _baseWidth = 1080;
    
    [Tooltip("開発時の基準となる画面解像度（高さ）")]
    [SerializeField]
    private float _baseHeight = 1920;

    [Tooltip("基準解像度における画像のPixel Per Unit")]
    [SerializeField]
    private float _pixelPerUnit = 100f;

    [Tooltip("実行中に常に更新を続けるか（通常は不要）")]
    [SerializeField]
    private bool _isAlwaysUpdate = false;
  
    private float _currentAspect;

    private void Awake() 
    {
        _camera = GetComponent<Camera>();
        UpdateOrthographicSize();
    }

    // インスペクターの値が変更された時にエディタ上で即時反映させるため
    private void OnValidate() 
    {
        _camera = GetComponent<Camera>();
        _currentAspect = 0; // アスペクト比をリセットして強制的に更新
        UpdateOrthographicSize();
    }

    private void Update() 
    {
        // 実行中はパフォーマンスのため、isAlwaysUpdateがtrueの場合のみ更新
        if (!_isAlwaysUpdate && Application.isPlaying) 
        {
            return;
        }
        UpdateOrthographicSize();
    }

    /// <summary>
    /// カメラのOrthographicSizeを現在のアスペクト比に応じて更新します。
    /// </summary>
    private void UpdateOrthographicSize() 
    {
        if (_camera == null || !_camera.orthographic) return;

        // 現在のアスペクト比を取得
        float currentAspect = (float)Screen.height / (float)Screen.width;

        // アスペクト比に変化がなければ更新しない
        if (Mathf.Approximately(_currentAspect, currentAspect)) 
        {
            return;
        }
        _currentAspect = currentAspect;
    
        // 基準のアスペクト比と、その時のOrthographicSizeを計算
        float baseAspect = _baseHeight / _baseWidth; 
        float baseOrthographicSize = _baseHeight / _pixelPerUnit / 2f;
    
        // 現在のアスペクト比に応じてカメラのorthographicSizeを再設定
        if (_baseType == BaseType.Height)
        {
            // 高さを基準にする場合
            _camera.orthographicSize = baseOrthographicSize;
        }
        else if (_baseType == BaseType.Width)
        {
            // 幅を基準にする場合
             _camera.orthographicSize = baseOrthographicSize * (baseAspect / _currentAspect);
        }
        else // Bothの場合
        {
            // 基準より縦長（スマホを縦に持った状態）なら、横に合わせる（上下が切れる）
            // 基準より横長なら、縦に合わせる（左右が切れる）
            if (baseAspect > _currentAspect) 
            {
                _camera.orthographicSize = baseOrthographicSize * (baseAspect / _currentAspect);
            } 
            else 
            {
                _camera.orthographicSize = baseOrthographicSize;
            }
        }
    }
}