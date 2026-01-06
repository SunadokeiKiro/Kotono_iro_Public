using UnityEngine;

/// <summary>
/// マイクの音量に応じてパーティクルを制御し、「声を吸い込む」演出を行うスクリプト。
/// パーティクルシステムが割り当てられていない場合、自動的に生成・設定します。
/// </summary>
public class VoiceVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MicrophoneController microphoneController;
    [SerializeField] private ParticleSystem voiceParticles;

    [Header("Behavior Settings")]
    [SerializeField] private float sensitivity = 50f; 
    [SerializeField] private float minEmission = 0f;
    [SerializeField] private float maxEmission = 100f; // パーティクル数を増やす
    [SerializeField] private float smoothSpeed = 10f; 

    private float targetEmission = 0f;
    private float currentEmission = 0f;

    void Start()
    {
        // マイクコントローラーが未設定なら自動検索
        if (microphoneController == null)
        {
            microphoneController = FindFirstObjectByType<MicrophoneController>();
        }

        // パーティクルが未設定なら自動生成
        if (voiceParticles == null)
        {
            SetupParticleSystem();
        }
    }

    private float debugLogTimer = 0f;
    
    void Update()
    {
        // デバッグ: 参照チェック (1秒ごと、録音中のみ)
        debugLogTimer += Time.deltaTime;
        if (debugLogTimer >= 1f)
        {
            debugLogTimer = 0f;
            if (microphoneController == null)
            {
                Debug.LogWarning("[VoiceVisualizer] microphoneController is NULL!");
            }
            else if (voiceParticles == null)
            {
                Debug.LogWarning("[VoiceVisualizer] voiceParticles is NULL (should auto-generate)!");
            }
            else if (microphoneController.IsRecording) // ★ 録音中のみログを出力
            {
                Debug.Log($"[VoiceVisualizer] RMS={microphoneController.CurrentRmsValue:F4}, Emission={currentEmission:F1}");
            }
        }
        
        if (microphoneController == null || voiceParticles == null) return;

        // 音量取得 (プロパティ追加済み前提)
        float volume = microphoneController.CurrentRmsValue;
        
        // 感度調整
        float adjustedVolume = Mathf.Clamp01(volume * sensitivity);
        
        targetEmission = Mathf.Lerp(minEmission, maxEmission, adjustedVolume);
        currentEmission = Mathf.Lerp(currentEmission, targetEmission, Time.deltaTime * smoothSpeed);

        var emission = voiceParticles.emission;
        emission.rateOverTime = currentEmission;
    }

    private void SetupParticleSystem()
    {
        GameObject pObj = new GameObject("VoiceParticles_Auto");
        pObj.transform.SetParent(this.transform, false);
        pObj.transform.localPosition = Vector3.zero; // ★ 修正: 中心を親に合わせる (0,0,0)
        pObj.transform.localRotation = Quaternion.Euler(-90, 0, 0); // 上向き

        voiceParticles = pObj.AddComponent<ParticleSystem>();
        var renderer = pObj.GetComponent<ParticleSystemRenderer>();

        // ★ 修正: マテリアルをシェーダーから生成 (ピンク回避)
        Shader shader = Shader.Find("Mobile/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit"); // URP Fallback
        
        if (shader != null)
        {
            renderer.material = new Material(shader);
        }
        else
        {
            Debug.LogWarning("VoiceVisualizer: Could not find suitable particle shader.");
        }

        // Stop the system before modification to allow setting duration/maxParticles
        voiceParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // --- Main Module ---
        var main = voiceParticles.main;
        main.duration = 5.0f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 4.0f);
        main.startSpeed = 0f; // 速度はVelocityOverLifetimeで制御
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startColor = new Color(0.4f, 0.8f, 1.0f, 0.5f); // 青白く
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1000;

        // --- Emission Module ---
        var emission = voiceParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f; // 初期値0

        // --- Shape Module ---
        var shape = voiceParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 12.0f; // 広範囲から
        shape.radiusThickness = 1.0f;

        // --- Velocity Over Lifetime (吸い込みの要) ---
        var velocity = voiceParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.radial = new ParticleSystem.MinMaxCurve(-5.0f, -8.0f); // 中心へ強く吸い込む
        
        // --- Size Over Lifetime ---
        var sizeOverLifetime = voiceParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0.0f, 0.0f);  // 出現時小
        sizeCurve.AddKey(0.2f, 1.0f);  // すぐ大きくなる
        sizeCurve.AddKey(1.0f, 0.0f);  // 消滅時小
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

        // --- Color Over Lifetime ---
        var colorOverLifetime = voiceParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.cyan, 0.0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(Color.blue, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);
        
        // --- Noise Module (ゆらぎ) ---
        var noise = voiceParticles.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.5f;

        Debug.Log("VoiceVisualizer: Auto-generated particle system settings.");
        
        // ★ 重要: 設定完了後にパーティクルシステムを再生開始
        voiceParticles.Play();
    }
}
