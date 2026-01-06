using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ギャラリーシーンで選択された月のアートをプレビュー表示するためのマネージャ。
/// VFXRippleManagerの簡易版として動作します。
/// </summary>
public class PreviewRippleManager : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private ParticleSystem targetParticleSystem;
    [SerializeField] private float sphereRadius = 2.0f;
    [SerializeField] private float particleSize = 0.1f;
    [SerializeField] private float heightScale = 0.8f;
    [SerializeField] private Gradient colorGradient; // Valenceに応じた色

    private ParticleSystem.Particle[] particles;

    void Start()
    {
        if (targetParticleSystem == null)
            targetParticleSystem = GetComponent<ParticleSystem>();
            
         // Initialize arrays
         if(targetParticleSystem != null)
         {
             particles = new ParticleSystem.Particle[targetParticleSystem.main.maxParticles];
         }
    }

    void Update()
    {
        // ゆっくり回転させてプレビューっぽくする
        transform.Rotate(Vector3.up * 10f * Time.deltaTime);
    }

    public void ShowPreview(ArtData data)
    {
        if (targetParticleSystem == null) return;
        
        targetParticleSystem.Clear();
        
        if (data.emotionHistory == null || data.emotionHistory.Count == 0)
        {
            return;
        }

        // Ensure system is playing
        if (!targetParticleSystem.isPlaying) targetParticleSystem.Play();

        var emitParams = new ParticleSystem.EmitParams();
        emitParams.startLifetime = 1000f; // 長生きさせる

        foreach(var emotion in data.emotionHistory)
        {
            // 暫定: ランダムな球面上配置
            Vector3 centerPos = Random.onUnitSphere * sphereRadius;
            
            // 色 (Valence)
            float t = (emotion.valence + 1f) * 0.5f; 
            Color c = colorGradient.Evaluate(t);
            c.a = 1.0f; // はっきり見せる

            // サイズ (Arousal)
            float size = particleSize * (1.0f + emotion.arousal);

            // 1つの感情につきクラスターを生成
            int clusterCount = 15;
            for(int i=0; i<clusterCount; i++)
            {
                // 中心から少し散らす
                Vector3 offset = Random.insideUnitSphere * 0.3f;
                Vector3 pos = (centerPos + offset).normalized * sphereRadius;
                
                // 高さ (Arousal)
                float h = emotion.arousal * heightScale;
                // 中心に近いほど高く
                float dist = Vector3.Distance(pos, centerPos);
                float heightFactor = Mathf.Max(0, 1.0f - dist * 3.0f);
                pos *= (1.0f + h * heightFactor);

                emitParams.position = pos;
                emitParams.startColor = c;
                emitParams.startSize = size * Random.Range(0.8f, 1.2f);
                
                targetParticleSystem.Emit(emitParams, 1);
            }
        }
    }
}
