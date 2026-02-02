// Scripts/CosmicBackgroundController.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 抽象的・幻想的な宇宙背景を生成・制御します。
/// カメラの視野角を自動取得し、画面比率の変化にも対応します。
/// </summary>
public class CosmicBackgroundController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private Camera targetCamera; // 未設定時はCamera.mainを使用
    [SerializeField] private float fovPadding = 1.2f; // 視野角に余裕を持たせる倍率
    [SerializeField] private bool autoReinitializeOnAspectChange = true;
    
    [Header("Star Field Settings")]
    [SerializeField] private int starCount = 500;
    [SerializeField] private float starDistanceMin = 30f;
    [SerializeField] private float starDistanceMax = 100f;
    [SerializeField] private float starSizeMin = 0.02f;
    [SerializeField] private float starSizeMax = 0.08f;
    [SerializeField] private float starTwinkleSpeed = 1.5f;
    
    [Header("Bright Star Settings (明るい星)")]
    [SerializeField] private int brightStarCount = 20;
    [SerializeField] private float brightStarSizeMin = 0.1f;
    [SerializeField] private float brightStarSizeMax = 0.2f;
    
    [Header("Nebula Settings (控えめな星雲)")]
    [SerializeField] private bool enableNebula = true;
    [SerializeField] private int nebulaParticleCount = 15;
    [SerializeField] private float nebulaDistanceMin = 80f;
    [SerializeField] private float nebulaDistanceMax = 150f;
    [SerializeField] private float nebulaAlpha = 0.08f;
    [SerializeField] private float nebulaParticleSizeMin = 15f;
    [SerializeField] private float nebulaParticleSizeMax = 40f;
    
    [Header("Animation Settings")]
    [SerializeField] private float auroraSpeed = 0.05f;
    [SerializeField] private float auroraIntensity = 0.3f;
    
    [Header("Emotion Connection")]
    [SerializeField] private bool reactToEmotion = true;
    
    // 現在の視野角（カメラから自動取得）
    private float currentVerticalFOV;
    private float currentHorizontalFOV;
    private float lastAspectRatio;
    
    // 自動生成されるParticleSystem
    private ParticleSystem starfieldPS;
    private ParticleSystem brightStarPS;
    private ParticleSystem nebulaPS;
    
    // 内部データ
    private ParticleSystem.Particle[] stars;
    private ParticleSystem.Particle[] brightStars;
    private ParticleSystem.Particle[] nebulaParticles;
    private Vector3[] starBasePositions;
    private Vector3[] brightStarBasePositions;
    private float[] starTwinklePhases;
    private float[] brightStarTwinklePhases;
    
    // 感情データ
    private float currentValence = 0f;
    private float currentArousal = 0f;
    
    // 星雲カラーパレット（淡い色）
    private readonly Color[] nebulaColors = new Color[]
    {
        new Color(0.15f, 0.1f, 0.25f, 1f),   // 淡いパープル
        new Color(0.1f, 0.12f, 0.2f, 1f),   // ダークブルー
        new Color(0.08f, 0.15f, 0.18f, 1f), // ダークティール
    };
    
    // 生成されるテクスチャ
    private Texture2D starTexture;
    private Texture2D nebulaTexture;
    
    /// <summary>
    /// 丸い星用テクスチャを生成
    /// </summary>
    private Texture2D CreateStarTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float normalizedDist = dist / radius;
                
                // 中心から外側に向かって急速に減衰（星のような光）
                float alpha = Mathf.Clamp01(1f - Mathf.Pow(normalizedDist, 1.5f));
                alpha = Mathf.Pow(alpha, 2f); // より鋭いエッジ
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
    
    /// <summary>
    /// ソフトな星雲用テクスチャを生成
    /// </summary>
    private Texture2D CreateNebulaTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float normalizedDist = dist / radius;
                
                // ガウシアンブラーのような減衰（ソフトな霧）
                float alpha = Mathf.Exp(-normalizedDist * normalizedDist * 2f);
                alpha = Mathf.Clamp01(alpha);
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
    
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
        
        // テクスチャを生成
        starTexture = CreateStarTexture(64);
        nebulaTexture = CreateNebulaTexture(128);
        
        CreateParticleSystems();
    }
    
    private void Start()
    {
        UpdateFOVFromCamera();
        InitializeAllParticles();
    }
    
    /// <summary>
    /// カメラから視野角を計算
    /// </summary>
    private void UpdateFOVFromCamera()
    {
        if (targetCamera == null) return;
        
        // 垂直FOVはカメラから直接取得
        currentVerticalFOV = targetCamera.fieldOfView * fovPadding;
        
        // 水平FOVはアスペクト比から計算
        float aspectRatio = targetCamera.aspect;
        currentHorizontalFOV = 2f * Mathf.Atan(Mathf.Tan(currentVerticalFOV * 0.5f * Mathf.Deg2Rad) * aspectRatio) * Mathf.Rad2Deg;
        
        lastAspectRatio = aspectRatio;
        
        Debug.Log($"[CosmicBackground] FOV Updated - H:{currentHorizontalFOV:F1}° V:{currentVerticalFOV:F1}° Aspect:{aspectRatio:F2}");
    }
    
    /// <summary>
    /// 全パーティクルを初期化
    /// </summary>
    private void InitializeAllParticles()
    {
        InitializeStarfield();
        InitializeBrightStars();
        if (enableNebula)
            InitializeNebula();
    }
    
    private void Update()
    {
        // 画面比率の変化を検出して再初期化
        if (autoReinitializeOnAspectChange && targetCamera != null)
        {
            float currentAspect = targetCamera.aspect;
            if (Mathf.Abs(currentAspect - lastAspectRatio) > 0.01f)
            {
                Debug.Log($"[CosmicBackground] Aspect ratio changed: {lastAspectRatio:F2} -> {currentAspect:F2}");
                UpdateFOVFromCamera();
                InitializeAllParticles();
            }
        }
        
        UpdateStarTwinkle();
        UpdateBrightStarTwinkle();
        if (enableNebula)
            UpdateNebula();
    }
    
    private void CreateParticleSystems()
    {
        // 星雲（最背面）
        if (enableNebula)
        {
            GameObject nebulaObj = new GameObject("NebulaParticles");
            nebulaObj.transform.SetParent(transform);
            nebulaObj.transform.localPosition = Vector3.zero;
            nebulaPS = nebulaObj.AddComponent<ParticleSystem>();
            ConfigureParticleSystemForNebula(nebulaPS);
        }
        
        // 通常の星（中間）
        GameObject starObj = new GameObject("StarfieldParticles");
        starObj.transform.SetParent(transform);
        starObj.transform.localPosition = Vector3.zero;
        starfieldPS = starObj.AddComponent<ParticleSystem>();
        ConfigureParticleSystemForStars(starfieldPS, starCount);
        
        // 明るい星（最前面）
        GameObject brightObj = new GameObject("BrightStarParticles");
        brightObj.transform.SetParent(transform);
        brightObj.transform.localPosition = Vector3.zero;
        brightStarPS = brightObj.AddComponent<ParticleSystem>();
        ConfigureParticleSystemForStars(brightStarPS, brightStarCount);
    }
    
    private void ConfigureParticleSystemForStars(ParticleSystem ps, int count)
    {
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = count;
        main.startLifetime = float.MaxValue;
        main.startSpeed = 0;
        
        var emission = ps.emission;
        emission.enabled = false;
        
        var shape = ps.shape;
        shape.enabled = false;
        
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 10;
        
        // Additive ブレンドマテリアル（光る星）
        var mat = new Material(Shader.Find("Particles/Standard Unlit"));
        mat.SetFloat("_Mode", 0);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetColor("_Color", Color.white);
        mat.EnableKeyword("_ALPHABLEND_ON");
        // 丸いテクスチャを適用
        mat.mainTexture = starTexture;
        renderer.material = mat;
    }
    
    private void ConfigureParticleSystemForNebula(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = nebulaParticleCount;
        main.startLifetime = float.MaxValue;
        main.startSpeed = 0;
        
        var emission = ps.emission;
        emission.enabled = false;
        
        var shape = ps.shape;
        shape.enabled = false;
        
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = -10;
        
        // ソフトなアルファブレンド
        var mat = new Material(Shader.Find("Particles/Standard Unlit"));
        mat.SetFloat("_Mode", 0);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_ALPHABLEND_ON");
        // ソフトな星雲テクスチャを適用
        mat.mainTexture = nebulaTexture;
        renderer.material = mat;
    }
    
    private void InitializeStarfield()
    {
        if (starfieldPS == null) return;
        
        stars = new ParticleSystem.Particle[starCount];
        starBasePositions = new Vector3[starCount];
        starTwinklePhases = new float[starCount];
        
        // 視野角をラジアンに変換（カメラから自動取得済み）
        float halfHFov = currentHorizontalFOV * 0.5f * Mathf.Deg2Rad;
        float halfVFov = currentVerticalFOV * 0.5f * Mathf.Deg2Rad;
        
        for (int i = 0; i < starCount; i++)
        {
            // カメラの視野内にランダム配置
            float distance = Random.Range(starDistanceMin, starDistanceMax);
            
            // 視野角内のランダムな角度
            float angleH = Random.Range(-halfHFov, halfHFov);
            float angleV = Random.Range(-halfVFov, halfVFov);
            
            // 球面座標から直交座標に変換
            Vector3 randomPos = new Vector3(
                distance * Mathf.Tan(angleH),
                distance * Mathf.Tan(angleV),
                distance
            );
            
            starBasePositions[i] = randomPos;
            starTwinklePhases[i] = Random.Range(0f, Mathf.PI * 2f);
            
            stars[i].position = randomPos;
            stars[i].startSize = Random.Range(starSizeMin, starSizeMax);
            stars[i].remainingLifetime = float.MaxValue;
            
            // 星の色（主に白、少し青や黄色）
            float colorRoll = Random.value;
            Color starColor;
            if (colorRoll < 0.75f)
            {
                starColor = new Color(1f, 1f, 1f, Random.Range(0.4f, 0.9f));
            }
            else if (colorRoll < 0.9f)
            {
                starColor = new Color(0.85f, 0.9f, 1f, Random.Range(0.5f, 1f));
            }
            else
            {
                starColor = new Color(1f, 0.95f, 0.85f, Random.Range(0.5f, 1f));
            }
            stars[i].startColor = starColor;
        }
        
        starfieldPS.SetParticles(stars, starCount);
    }
    
    private void InitializeBrightStars()
    {
        if (brightStarPS == null) return;
        
        brightStars = new ParticleSystem.Particle[brightStarCount];
        brightStarBasePositions = new Vector3[brightStarCount];
        brightStarTwinklePhases = new float[brightStarCount];
        
        // 視野角をラジアンに変換（カメラから自動取得済み）
        float halfHFov = currentHorizontalFOV * 0.5f * Mathf.Deg2Rad;
        float halfVFov = currentVerticalFOV * 0.5f * Mathf.Deg2Rad;
        
        for (int i = 0; i < brightStarCount; i++)
        {
            float distance = Random.Range(starDistanceMin * 0.7f, starDistanceMax * 0.8f);
            
            // 視野角内のランダムな角度
            float angleH = Random.Range(-halfHFov, halfHFov);
            float angleV = Random.Range(-halfVFov, halfVFov);
            
            Vector3 randomPos = new Vector3(
                distance * Mathf.Tan(angleH),
                distance * Mathf.Tan(angleV),
                distance
            );
            
            brightStarBasePositions[i] = randomPos;
            brightStarTwinklePhases[i] = Random.Range(0f, Mathf.PI * 2f);
            
            brightStars[i].position = randomPos;
            brightStars[i].startSize = Random.Range(brightStarSizeMin, brightStarSizeMax);
            brightStars[i].remainingLifetime = float.MaxValue;
            
            // 明るい星の色（より多彩）
            float colorRoll = Random.value;
            Color starColor;
            if (colorRoll < 0.5f)
            {
                starColor = new Color(1f, 1f, 1f, 1f);
            }
            else if (colorRoll < 0.7f)
            {
                starColor = new Color(0.8f, 0.85f, 1f, 1f);
            }
            else if (colorRoll < 0.85f)
            {
                starColor = new Color(1f, 0.95f, 0.8f, 1f);
            }
            else
            {
                starColor = new Color(1f, 0.85f, 0.75f, 1f);
            }
            brightStars[i].startColor = starColor;
        }
        
        brightStarPS.SetParticles(brightStars, brightStarCount);
    }
    
    private void InitializeNebula()
    {
        if (nebulaPS == null) return;
        
        nebulaParticles = new ParticleSystem.Particle[nebulaParticleCount];
        
        // 視野角をラジアンに変換（カメラから自動取得済み）
        float halfHFov = currentHorizontalFOV * 0.5f * Mathf.Deg2Rad;
        float halfVFov = currentVerticalFOV * 0.5f * Mathf.Deg2Rad;
        
        for (int i = 0; i < nebulaParticleCount; i++)
        {
            float distance = Random.Range(nebulaDistanceMin, nebulaDistanceMax);
            
            // 視野角内のランダムな角度
            float angleH = Random.Range(-halfHFov, halfHFov);
            float angleV = Random.Range(-halfVFov, halfVFov);
            
            Vector3 randomPos = new Vector3(
                distance * Mathf.Tan(angleH),
                distance * Mathf.Tan(angleV),
                distance
            );
            
            nebulaParticles[i].position = randomPos;
            nebulaParticles[i].startSize = Random.Range(nebulaParticleSizeMin, nebulaParticleSizeMax);
            nebulaParticles[i].remainingLifetime = float.MaxValue;
            
            Color c = nebulaColors[Random.Range(0, nebulaColors.Length)];
            c.a = nebulaAlpha * Random.Range(0.3f, 1f);
            nebulaParticles[i].startColor = c;
        }
        
        nebulaPS.SetParticles(nebulaParticles, nebulaParticleCount);
    }
    private void UpdateStarTwinkle()
    {
        if (stars == null || starfieldPS == null) return;
        
        float time = Time.time;
        float twinkleSpeed = starTwinkleSpeed + (currentArousal * 1f);
        
        for (int i = 0; i < starCount; i++)
        {
            float twinkle = Mathf.Sin(time * twinkleSpeed + starTwinklePhases[i]);
            twinkle = (twinkle + 1f) * 0.5f;
            twinkle = 0.4f + (twinkle * 0.6f);
            
            Color c = stars[i].startColor;
            c.a = twinkle * (0.4f + Random.Range(0f, 0.1f));
            stars[i].startColor = c;
        }
        
        starfieldPS.SetParticles(stars, starCount);
    }
    
    private void UpdateBrightStarTwinkle()
    {
        if (brightStars == null || brightStarPS == null) return;
        
        float time = Time.time;
        float twinkleSpeed = starTwinkleSpeed * 0.7f + (currentArousal * 0.5f);
        
        for (int i = 0; i < brightStarCount; i++)
        {
            // 明るい星はゆっくり、はっきり瞬く
            float twinkle = Mathf.Sin(time * twinkleSpeed + brightStarTwinklePhases[i]);
            twinkle = (twinkle + 1f) * 0.5f;
            twinkle = 0.7f + (twinkle * 0.3f); // 最低輝度を高く
            
            Color c = brightStars[i].startColor;
            c.a = twinkle;
            brightStars[i].startColor = c;
        }
        
        brightStarPS.SetParticles(brightStars, brightStarCount);
    }
    
    private void UpdateNebula()
    {
        if (nebulaParticles == null || nebulaPS == null) return;
        
        float time = Time.time;
        
        for (int i = 0; i < nebulaParticleCount; i++)
        {
            int colorIndex = i % nebulaColors.Length;
            int nextColorIndex = (colorIndex + 1) % nebulaColors.Length;
            
            float colorPhase = (Mathf.Sin(time * auroraSpeed + i * 0.5f) + 1f) * 0.5f;
            Color baseColor = Color.Lerp(nebulaColors[colorIndex], nebulaColors[nextColorIndex], colorPhase);
            
            if (reactToEmotion)
            {
                baseColor = AdjustNebulaColorByEmotion(baseColor);
            }
            
            baseColor.a = nebulaAlpha * (0.3f + auroraIntensity * colorPhase * 0.7f);
            nebulaParticles[i].startColor = baseColor;
        }
        
        nebulaPS.SetParticles(nebulaParticles, nebulaParticleCount);
    }
    
    private Color AdjustNebulaColorByEmotion(Color baseColor)
    {
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);
        
        h += currentValence * 0.1f;
        if (h > 1f) h -= 1f;
        if (h < 0f) h += 1f;
        
        s = Mathf.Lerp(s * 0.7f, s * 1.2f, currentArousal);
        v = Mathf.Lerp(v * 0.8f, v * 1.1f, currentArousal);
        
        return Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
    }
    
    public void SetEmotionData(float valence, float arousal)
    {
        currentValence = Mathf.Clamp(valence, -1f, 1f);
        currentArousal = Mathf.Clamp01(arousal);
    }
    
    public void SetAverageEmotion(List<EmotionPoint> emotions)
    {
        if (emotions == null || emotions.Count == 0)
        {
            currentValence = 0f;
            currentArousal = 0f;
            return;
        }
        
        float sumV = 0f, sumA = 0f;
        foreach (var e in emotions)
        {
            sumV += e.valence;
            sumA += e.arousal;
        }
        
        currentValence = sumV / emotions.Count;
        currentArousal = sumA / emotions.Count;
    }
    
    private void OnDestroy()
    {
        if (starfieldPS != null) Destroy(starfieldPS.gameObject);
        if (brightStarPS != null) Destroy(brightStarPS.gameObject);
        if (nebulaPS != null) Destroy(nebulaPS.gameObject);
    }
}
