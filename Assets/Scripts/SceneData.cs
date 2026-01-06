// Scripts/SceneData.cs
using System.Collections.Generic;

/// <summary>
/// UI表示やデータ保存のために、感情の合計値などをまとめて扱うためのデータ構造。
/// 以前はGameController内にあった定義を、独立したファイルに分離しました。
/// </summary>
[System.Serializable]
public class SceneData
{
    // 以前の鉱石モデル関連のデータは不要になったため削除し、
    // UI表示に必要な感情の合計値のみを残しています。
    public int totalEnergy, totalContent, totalUpset, totalAggression, totalStress, totalUncertainty,
            totalExcitement, totalConcentration, totalEmoCog, totalHesitation, totalBrainPower,
            totalEmbarrassment, totalIntensiveThinking, totalImaginationActivity, totalExtremeEmotion,
            totalPassionate, totalAtmosphere, totalAnticipation, totalDissatisfaction, totalConfidence;
}

// 注意：このファイルはMonoBehaviourを継承しないため、
// Unityのコンポーネントとしてゲームオブジェクトにアタッチする必要はありません。
// これは純粋なデータの「設計図」として機能します。