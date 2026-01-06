// Scripts/RippleDataStructs.cs (最終データ定義版)
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// =========================================================================
// 1. VFX GRAPH用 (Compute Bufferに送るための厳密な構造体)
// =========================================================================
/// <summary>
/// VFX Graphに送る単一の波紋の情報を定義する構造体。
/// Compute Bufferに格納できるよう、LayoutKind.Sequentialを設定します。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RippleData
{
    // 感情パラメータ (0〜1に正規化された値)
    public float valence;
    public float arousal;
    public float thought;
    public float confidence;

    // 波紋の位置と時間情報
    public Vector3 centerPosition; // 波紋の中心座標 (X, Y, Z)
    public float creationTime;     // 波紋が作成された時間 (Time.time)
    public long unixTimestamp;    // ★追加: 詳細表示用 (Unix Time)
    public long startTimestamp;   // ★追加: 範囲表示開始
    public long endTimestamp;     // ★追加: 範囲表示終了

    // 合計サイズ: 40 + 16 = 56 bytes
    public static int Size => 56;
}

// =========================================================================
// 2. JSON保存用 & UI用 (GameControllerが使うデータ)
// =========================================================================
/// <summary>
/// 感情分析された時点のデータを保持する構造体。ArtDataにリスト化され保存されます。
/// </summary>
[System.Serializable]
public struct EmotionPoint
{
    public float valence;    // 感情価 (-1 to 1)
    public float arousal;    // 覚醒度 (0 to 1)
    public float thought;    // 思考 (0 to 1)
    public float confidence; // 自信 (0 to 1)
    public long timestamp;   // 記録時のタイムスタンプ
    public Vector3 position; // ★追加: 波紋の発生位置（球体上の座標）
    
    // 生データ保存用 (将来の拡張のため)
    public List<SentimentSegment> rawSegments;
}

/// <summary>
/// 感情ポイントの履歴全体をラップするクラス (JSONのルートとして必要)。
/// </summary>
[System.Serializable]
public class ArtData
{
    public List<EmotionPoint> emotionHistory = new List<EmotionPoint>();
}

/// <summary>
/// UI表示用の感情の合計値（月次）を保持するクラス。
/// </summary>
[System.Serializable]
public class TotalSentiments
{
    public int totalEnergy, totalContent, totalUpset, totalAggression, totalStress, totalUncertainty,
               totalExcitement, totalConcentration, totalEmoCog, totalHesitation, totalBrainPower,
               totalEmbarrassment, totalIntensiveThinking, totalImaginationActivity, totalExtremeEmotion,
               totalPassionate, totalAtmosphere, totalAnticipation, totalDissatisfaction, totalConfidence;
}