// Scripts/PostProcessingController.cs
// URPのVolume Profileを動的に制御し、感情に応じて映像効果を変化させます。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-Processingエフェクトを感情データに基づいて動的に制御するコントローラー。
/// Bloom, Color Adjustments, Vignette, Chromatic Aberrationを管理します。
/// </summary>
public class PostProcessingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private VFXRippleManager rippleManager;

    [Header("Bloom Settings")]
    [SerializeField] private float baseBloomIntensity = 0.5f;
    [SerializeField] private float maxBloomIntensity = 3.0f;
    [SerializeField] private float bloomThreshold = 0.9f;

    [Header("Vignette Settings")]
    [SerializeField] private float baseVignetteIntensity = 0.2f;
    [SerializeField] private float focusVignetteIntensity = 0.4f;

    [Header("Chromatic Aberration Settings")]
    [SerializeField] private float baseChromaticIntensity = 0.0f;
    [SerializeField] private float maxChromaticIntensity = 0.3f;

    [Header("Color Settings")]
    [SerializeField] private float baseSaturation = 0f;
    [SerializeField] private float maxSaturation = 30f;
    [SerializeField] private float baseContrast = 0f;
    [SerializeField] private float maxContrast = 20f;

    [Header("Animation")]
    [SerializeField] private float transitionSpeed = 2.0f;

    // Volume Profile Components
    private Bloom bloom;
    private Vignette vignette;
    private ChromaticAberration chromaticAberration;
    private ColorAdjustments colorAdjustments;
    private FilmGrain filmGrain;

    // Target Values (for smooth transitions)
    private float targetBloom;
    private float targetVignette;
    private float targetChromatic;
    private float targetSaturation;
    private float targetContrast;

    // State
    private bool isInitialized = false;
    private bool isFocusMode = false;

    // Quality Settings (Device Local)
    private const string PREF_KEY_QUALITY = "PostProcessingQuality";
    private int currentQuality = 2; // 0=Off, 1=Low, 2=Medium, 3=High

    void Start()
    {
        InitializeComponents();
        LoadQualitySettings();
    }

    private void InitializeComponents()
    {
        if (globalVolume == null)
        {
            globalVolume = FindFirstObjectByType<Volume>();
        }

        if (globalVolume == null || globalVolume.profile == null)
        {
            Debug.LogWarning("[PostProcessingController] Volume or Profile not found. Creating new profile.");
            CreateVolumeProfile();
        }

        // Get or add components from profile
        VolumeProfile profile = globalVolume.profile;

        if (!profile.TryGet(out bloom))
        {
            bloom = profile.Add<Bloom>();
        }
        if (!profile.TryGet(out vignette))
        {
            vignette = profile.Add<Vignette>();
        }
        if (!profile.TryGet(out chromaticAberration))
        {
            chromaticAberration = profile.Add<ChromaticAberration>();
        }
        if (!profile.TryGet(out colorAdjustments))
        {
            colorAdjustments = profile.Add<ColorAdjustments>();
        }
        if (!profile.TryGet(out filmGrain))
        {
            filmGrain = profile.Add<FilmGrain>();
        }

        // Initialize overrides
        bloom.active = true;
        bloom.intensity.overrideState = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = bloomThreshold;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.7f;

        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.5f;

        chromaticAberration.active = true;
        chromaticAberration.intensity.overrideState = true;

        colorAdjustments.active = true;
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.contrast.overrideState = true;
        colorAdjustments.postExposure.overrideState = true;

        filmGrain.active = true;
        filmGrain.intensity.overrideState = true;
        filmGrain.intensity.value = 0.1f; // Subtle grain
        filmGrain.type.overrideState = true;
        filmGrain.type.value = FilmGrainLookup.Medium1;

        // Set initial values
        targetBloom = baseBloomIntensity;
        targetVignette = baseVignetteIntensity;
        targetChromatic = baseChromaticIntensity;
        targetSaturation = baseSaturation;
        targetContrast = baseContrast;

        isInitialized = true;
        Debug.Log("[PostProcessingController] Initialized successfully.");
    }

    private void CreateVolumeProfile()
    {
        // Create Volume GameObject if needed
        if (globalVolume == null)
        {
            GameObject volumeObj = new GameObject("GlobalVolume_Auto");
            globalVolume = volumeObj.AddComponent<Volume>();
            globalVolume.isGlobal = true;
        }

        // Create a new profile
        globalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
    }

    void Update()
    {
        if (!isInitialized || currentQuality == 0) return;

        ApplyQualityModifiers();
        SmoothTransition();
    }

    private void ApplyQualityModifiers()
    {
        // Quality affects effect intensity
        float qualityMultiplier = currentQuality switch
        {
            1 => 0.3f,  // Low
            2 => 0.7f,  // Medium
            3 => 1.0f,  // High
            _ => 0f     // Off
        };

        bloom.intensity.value = Mathf.Lerp(bloom.intensity.value, targetBloom * qualityMultiplier, Time.deltaTime * transitionSpeed);
        vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, targetVignette, Time.deltaTime * transitionSpeed);
        chromaticAberration.intensity.value = Mathf.Lerp(chromaticAberration.intensity.value, targetChromatic * qualityMultiplier, Time.deltaTime * transitionSpeed);
        colorAdjustments.saturation.value = Mathf.Lerp(colorAdjustments.saturation.value, targetSaturation * qualityMultiplier, Time.deltaTime * transitionSpeed);
        colorAdjustments.contrast.value = Mathf.Lerp(colorAdjustments.contrast.value, targetContrast * qualityMultiplier, Time.deltaTime * transitionSpeed);

        // Film grain only on High
        filmGrain.active = currentQuality >= 3;
    }

    private void SmoothTransition()
    {
        // Placeholder for future per-frame updates
    }

    /// <summary>
    /// 感情データに基づいてエフェクトを更新します。
    /// GameControllerやMainUIManagerから呼び出されます。
    /// </summary>
    /// <param name="valence">感情価 (-1 to 1)</param>
    /// <param name="arousal">覚醒度 (0 to 1)</param>
    public void UpdateEffectsFromEmotion(float valence, float arousal)
    {
        if (!isInitialized) return;

        // Bloom: Arousalが高いほど輝きが増す
        targetBloom = Mathf.Lerp(baseBloomIntensity, maxBloomIntensity, arousal);

        // Chromatic Aberration: 高Arousalで色収差が増す
        targetChromatic = Mathf.Lerp(baseChromaticIntensity, maxChromaticIntensity, arousal * arousal); // Quadratic for emphasis

        // Saturation: Valenceの絶対値が高いほど彩度アップ
        float emotionalIntensity = Mathf.Abs(valence);
        targetSaturation = Mathf.Lerp(baseSaturation, maxSaturation, emotionalIntensity);

        // Contrast: Arousalに連動
        targetContrast = Mathf.Lerp(baseContrast, maxContrast, arousal);
    }

    /// <summary>
    /// フォーカスモードの切り替え。ビネット効果を強調します。
    /// </summary>
    public void SetFocusMode(bool focus)
    {
        isFocusMode = focus;
        targetVignette = focus ? focusVignetteIntensity : baseVignetteIntensity;
    }

    /// <summary>
    /// アイドル状態（感情データなし）に戻す
    /// </summary>
    public void ResetToIdle()
    {
        targetBloom = baseBloomIntensity;
        targetVignette = baseVignetteIntensity;
        targetChromatic = baseChromaticIntensity;
        targetSaturation = baseSaturation;
        targetContrast = baseContrast;
    }

    // =====================
    // Quality Settings (Device Local)
    // =====================

    public void SetQuality(int quality)
    {
        currentQuality = Mathf.Clamp(quality, 0, 3);
        SaveQualitySettings();

        // If turning off, immediately disable effects
        if (currentQuality == 0 && isInitialized)
        {
            bloom.active = false;
            vignette.active = false;
            chromaticAberration.active = false;
            colorAdjustments.active = false;
            filmGrain.active = false;
        }
        else if (isInitialized)
        {
            bloom.active = true;
            vignette.active = true;
            chromaticAberration.active = true;
            colorAdjustments.active = true;
        }

        Debug.Log($"[PostProcessingController] Quality set to {currentQuality}");
    }

    public int GetQuality() => currentQuality;

    public string GetQualityName()
    {
        return currentQuality switch
        {
            0 => "オフ",
            1 => "低",
            2 => "中",
            3 => "高",
            _ => "不明"
        };
    }

    private void SaveQualitySettings()
    {
        PlayerPrefs.SetInt(PREF_KEY_QUALITY, currentQuality);
        PlayerPrefs.Save();
    }

    private void LoadQualitySettings()
    {
        currentQuality = PlayerPrefs.GetInt(PREF_KEY_QUALITY, 2); // Default: Medium
    }

    // =====================
    // Pulse Effect (for idle animation later)
    // =====================

    private float pulseTimer = 0f;
    [Header("Idle Pulse")]
    [SerializeField] private float pulseSpeed = 0.5f;
    [SerializeField] private float pulseBloomRange = 0.3f;

    /// <summary>
    /// アイドル時のパルスエフェクト（呼吸のような効果）
    /// VFXRippleManagerのアイドル演出と連動させます。
    /// </summary>
    public void UpdateIdlePulse()
    {
        if (!isInitialized || currentQuality == 0) return;

        pulseTimer += Time.deltaTime * pulseSpeed;
        float pulse = (Mathf.Sin(pulseTimer * Mathf.PI * 2f) + 1f) * 0.5f; // 0 to 1

        // Subtle bloom pulsation
        targetBloom = baseBloomIntensity + (pulse * pulseBloomRange);
    }
}
