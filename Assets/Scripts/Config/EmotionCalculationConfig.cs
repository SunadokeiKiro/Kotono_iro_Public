// Scripts/Config/EmotionCalculationConfig.cs
using UnityEngine;

/// <summary>
/// 感情値から派生感情を計算するための係数や除数を保持するScriptableObject。
/// ゲームのバランス調整を容易にします。
/// </summary>
[CreateAssetMenu(fileName = "EmotionCalculationConfig", menuName = "KotoGame/Emotion Calculation Configuration", order = 2)]
public class EmotionCalculationConfig : ScriptableObject
{
    [Header("Joy (喜び) 計算パラメータ")]
    [Tooltip("喜びを計算する際の合計値の除数")]
    public float joyDivisor = 500.0f;

    [Header("Anger (怒り) 計算パラメータ")]
    [Tooltip("怒りを計算する際の合計値の除数")]
    public float angerDivisor = 400.0f;

    [Header("Sadness (悲しみ) 計算パラメータ")]
    [Tooltip("悲しみを計算する際の合計値の除数")]
    public float sadnessDivisor = 450.0f;
    
    // ★★★ エラー修正箇所: 0.5f -> 2.0f に修正 ★★★
    [Tooltip("Energyから悲しみを計算する際の除数。 (100 - energy) / この値")]
    public float sadnessFromEnergyDivisor = 2.0f; 

    [Header("Enjoyment (楽しみ) 計算パラメータ")]
    [Tooltip("楽しみを計算する際の合計値の除数")]
    public float enjoymentDivisor = 400.0f;

    [Header("Focus (集中) 計算パラメータ")]
    [Tooltip("集中を計算する際の合計値の除数")]
    public float focusDivisor = 400.0f;
    [Tooltip("EmoCogから論理スコアを計算する際の基準値")]
    public int emoCogBaseline = 100;

    [Header("Anxiety (不安) 計算パラメータ")]
    [Tooltip("不安を計算する際の合計値の除数")]
    public float anxietyDivisor = 400.0f;

    [Header("Confidence (自信) 計算パラメータ")]
    [Tooltip("自信を計算する際の合計値の除数")]
    public float confidenceDivisor = 400.0f;

    [Header("モデル拡大率の係数")]
    [Tooltip("派生感情値からモデルの拡大率を計算する際の係数")]
    public float joyEnlargeFactor = 1.0f;
    public float angerEnlargeFactor = 1.0f;
    public float sadnessEnlargeFactor = 1.0f;
    public float enjoymentEnlargeFactor = 1.0f;
    public float focusEnlargeFactor = 0.5f;
    public float anxietyEnlargeFactor = 1.0f;
    public float confidenceEnlargeFactor = 1.0f;
}