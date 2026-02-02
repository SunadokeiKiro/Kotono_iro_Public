// Scripts/DeepSeaBackgroundController.cs
using UnityEngine;

/// <summary>
/// 深海オーロラ背景を制御します。
/// カメラを主軸に位置・サイズを自動調整し、画面比率の変化にも対応します。
/// </summary>
public class DeepSeaBackgroundController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("背景をカメラの子オブジェクトとして配置するか")]
    [SerializeField] private bool attachToCamera = true;
    [Tooltip("カメラからの距離（far clipに近いほど背面）")]
    [SerializeField, Range(0.5f, 0.99f)] private float distanceRatio = 0.95f;
    [Tooltip("サイズに余裕を持たせる倍率")]
    [SerializeField, Range(1f, 1.5f)] private float sizePadding = 1.1f;
    
    [Header("Light Settings")]
    [SerializeField] private Color topLightColor = new Color(0.2f, 0.4f, 0.6f, 1f);
    [SerializeField] private Color bottomLightColor = new Color(0.1f, 0.3f, 0.5f, 1f);
    [SerializeField] private Color deepSeaColor = new Color(0.02f, 0.05f, 0.1f, 1f);
    [SerializeField, Range(0f, 2f)] private float lightIntensity = 0.8f;
    [SerializeField, Range(0.1f, 5f)] private float lightFalloff = 2.0f;
    
    [Header("Center Masking (球体を邪魔しない)")]
    [SerializeField, Range(0f, 1f)] private float centerDarkness = 0.7f;
    [SerializeField, Range(0f, 0.5f)] private float centerRadius = 0.3f;
    
    [Header("Noise Animation")]
    [SerializeField, Range(0.1f, 10f)] private float noiseScale = 2.0f;
    [SerializeField, Range(0f, 0.5f)] private float noiseSpeed = 0.05f;
    [SerializeField, Range(0f, 1f)] private float noiseIntensity = 0.3f;
    
    [Header("Emotion Settings")]
    [SerializeField, Range(0f, 0.2f)] private float colorVariationRange = 0.1f;
    [Tooltip("感情値がこの値で最大の色相シフトになる")]
    [SerializeField, Range(0f, 1f)] private float maxHueShift = 0.15f;
    
    [Header("References")]
    [SerializeField] private Material backgroundMaterial;
    
    // キャッシュされたプロパティID
    private static readonly int TopColorID = Shader.PropertyToID("_TopColor");
    private static readonly int BottomColorID = Shader.PropertyToID("_BottomColor");
    private static readonly int DeepColorID = Shader.PropertyToID("_DeepColor");
    private static readonly int LightIntensityID = Shader.PropertyToID("_LightIntensity");
    private static readonly int LightFalloffID = Shader.PropertyToID("_LightFalloff");
    private static readonly int CenterDarknessID = Shader.PropertyToID("_CenterDarkness");
    private static readonly int CenterRadiusID = Shader.PropertyToID("_CenterRadius");
    private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedID = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int NoiseIntensityID = Shader.PropertyToID("_NoiseIntensity");
    private static readonly int EmotionHueID = Shader.PropertyToID("_EmotionHue");
    private static readonly int EmotionIntensityID = Shader.PropertyToID("_EmotionIntensity");
    private static readonly int ColorVariationID = Shader.PropertyToID("_ColorVariation");
    
    // 感情データ
    private float currentValence = 0f;
    private float currentArousal = 0f;
    private float targetValence = 0f;
    private float targetArousal = 0f;
    
    // 背景Quad
    private GameObject backgroundQuad;
    private float lastAspectRatio;
    
    private void Awake()
    {
        // カメラを自動検出
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = GetComponentInParent<Camera>();
            }
        }
    }
    
    private void Start()
    {
        if (backgroundMaterial == null)
        {
            // マテリアルを自動作成
            Shader shader = Shader.Find("Custom/DeepSeaBackground");
            if (shader != null)
            {
                backgroundMaterial = new Material(shader);
            }
            else
            {
                Debug.LogError("[DeepSeaBackground] Shader 'Custom/DeepSeaBackground' not found!");
                return;
            }
        }
        
        CreateFullscreenQuad();
        UpdateMaterialProperties();
        
        if (targetCamera != null)
        {
            lastAspectRatio = targetCamera.aspect;
        }
    }
    
    private void CreateFullscreenQuad()
    {
        // 既存のQuadがあれば削除
        if (backgroundQuad != null)
        {
            Destroy(backgroundQuad);
        }
        
        // フルスクリーンQuadを作成
        backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundQuad.name = "DeepSeaBackgroundQuad";
        
        // Collider削除
        Destroy(backgroundQuad.GetComponent<Collider>());
        
        // マテリアル適用
        var meshRenderer = backgroundQuad.GetComponent<MeshRenderer>();
        meshRenderer.material = backgroundMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        
        // カメラベースの配置
        UpdateQuadTransform();
    }
    
    private void UpdateQuadTransform()
    {
        if (backgroundQuad == null || targetCamera == null) return;
        
        float distance = targetCamera.farClipPlane * distanceRatio;
        
        if (attachToCamera)
        {
            // カメラの子オブジェクトにする
            backgroundQuad.transform.SetParent(targetCamera.transform);
            backgroundQuad.transform.localPosition = new Vector3(0, 0, distance);
            backgroundQuad.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // ワールド座標で配置
            backgroundQuad.transform.SetParent(transform);
            backgroundQuad.transform.position = targetCamera.transform.position + targetCamera.transform.forward * distance;
            backgroundQuad.transform.rotation = targetCamera.transform.rotation;
        }
        
        // 画面を覆うサイズに調整
        float frustumHeight = 2.0f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = frustumHeight * targetCamera.aspect;
        backgroundQuad.transform.localScale = new Vector3(frustumWidth * sizePadding, frustumHeight * sizePadding, 1f);
        
        Debug.Log($"[DeepSeaBackground] Quad updated - Size: {frustumWidth:F1}x{frustumHeight:F1}, Distance: {distance:F1}, Aspect: {targetCamera.aspect:F2}");
    }
    
    private void Update()
    {
        // 感情値を滑らかに補間
        currentValence = Mathf.Lerp(currentValence, targetValence, Time.deltaTime * 0.5f);
        currentArousal = Mathf.Lerp(currentArousal, targetArousal, Time.deltaTime * 0.5f);
        
        UpdateEmotionProperties();
        
        // 画面比率の変化を検出
        if (targetCamera != null)
        {
            float currentAspect = targetCamera.aspect;
            if (Mathf.Abs(currentAspect - lastAspectRatio) > 0.01f)
            {
                Debug.Log($"[DeepSeaBackground] Aspect ratio changed: {lastAspectRatio:F2} -> {currentAspect:F2}");
                lastAspectRatio = currentAspect;
                UpdateQuadTransform();
            }
        }
    }
    
    private void UpdateMaterialProperties()
    {
        if (backgroundMaterial == null) return;
        
        backgroundMaterial.SetColor(TopColorID, topLightColor);
        backgroundMaterial.SetColor(BottomColorID, bottomLightColor);
        backgroundMaterial.SetColor(DeepColorID, deepSeaColor);
        backgroundMaterial.SetFloat(LightIntensityID, lightIntensity);
        backgroundMaterial.SetFloat(LightFalloffID, lightFalloff);
        backgroundMaterial.SetFloat(CenterDarknessID, centerDarkness);
        backgroundMaterial.SetFloat(CenterRadiusID, centerRadius);
        backgroundMaterial.SetFloat(NoiseScaleID, noiseScale);
        backgroundMaterial.SetFloat(NoiseSpeedID, noiseSpeed);
        backgroundMaterial.SetFloat(NoiseIntensityID, noiseIntensity);
        backgroundMaterial.SetFloat(ColorVariationID, colorVariationRange);
    }
    
    private void UpdateEmotionProperties()
    {
        if (backgroundMaterial == null) return;
        
        // Valence: -1 ~ 1 を色相シフトに変換
        float hueShift = currentValence * maxHueShift;
        backgroundMaterial.SetFloat(EmotionHueID, hueShift);
        
        // Arousal: 光の強度に影響
        float intensity = Mathf.Abs(currentArousal);
        backgroundMaterial.SetFloat(EmotionIntensityID, intensity);
    }
    
    /// <summary>
    /// 感情データを受け取る（VFXRippleManagerから呼ばれる想定）
    /// </summary>
    public void SetEmotionData(float valence, float arousal)
    {
        targetValence = Mathf.Clamp(valence, -1f, 1f);
        targetArousal = Mathf.Clamp(arousal, -1f, 1f);
    }
    
    /// <summary>
    /// 感情分析結果を受け取る（0~1の値）
    /// </summary>
    public void SetAnalysisResult(float valence, float arousal)
    {
        // 0~1 を -1~1 に変換
        targetValence = (valence - 0.5f) * 2f;
        targetArousal = (arousal - 0.5f) * 2f;
    }
    
    /// <summary>
    /// アイドル状態に戻す
    /// </summary>
    public void ResetToIdle()
    {
        targetValence = 0f;
        targetArousal = 0f;
    }
    
    /// <summary>
    /// Quadの位置・サイズを再計算（手動呼び出し用）
    /// </summary>
    public void RefreshQuadTransform()
    {
        UpdateQuadTransform();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UpdateMaterialProperties();
    }
#endif
}
