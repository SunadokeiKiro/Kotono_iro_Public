// Scripts/KukuScripts/ApiResponseDefinishons.cs
using System;
using System.Collections.Generic;

// このファイルは、AmiVoice APIからのJSONレスポンスをC#のクラスにマッピングするためのデータ構造を定義します。
// UnityのJsonUtilityでデシリアライズするために、各クラスには[Serializable]属性が付与されています。

/// <summary>
/// 音声認識リクエスト直後のレスポンス。非同期処理のセッションIDを含みます。
/// </summary>
[Serializable]
public class RecognitionResponse
{
    public string sessionid;
    public string text; // 同期認識の場合のテキスト
}

/// <summary>
/// ポーリング時に受け取る、感情分析を含む解析結果の完全なレスポンス。
/// </summary>
[Serializable]
public class SentimentAnalysisResponse 
{
    public string status; // "completed", "processing", "error"など
    public string session_id;
    public string service_id;
    public int audio_size;
    public string audio_md5;
    public List<Segment> segments;
    public string utteranceid;
    public string text; // 全体の認識結果テキスト
    public string code; // エラーコード
    public string message; // エラーメッセージ
    public SentimentAnalysis sentiment_analysis;
}

/// <summary>
/// 発話の区間ごとの認識結果。
/// </summary>
[Serializable]
public class Segment 
{
    public List<Result> results;
    public string text; // このセグメントの認識結果テキスト
}

/// <summary>
/// 認識結果の詳細。信頼度や単語リストなどを含みます。
/// </summary>
[Serializable]
public class Result 
{
    public List<Token> tokens;
    public float confidence;
    public int starttime;
    public int endtime;
    public List<string> tags;
    public string rulename;
    public string text;
}

/// <summary>
/// 個々の単語（トークン）ごとの情報。
/// </summary>
[Serializable]
public class Token 
{
    public string written; // 書字形 (例: "今日")
    public float confidence;
    public int starttime;
    public int endtime;
    public string spoken; // 話し言葉形 (例: "きょう")
}

/// <summary>
/// 感情分析結果のルートオブジェクト。
/// </summary>
[Serializable]
public class SentimentAnalysis 
{
    public List<SentimentSegment> segments;
}

/// <summary>
/// 区間ごとの詳細な感情分析結果。
/// </summary>
[Serializable]
public class SentimentSegment 
{
    public int starttime;
    public int endtime;
    public int energy;
    public int content;
    public int upset;
    public int aggression;
    public int stress;
    public int uncertainty;
    public int excitement;
    public int concentration;
    public int emo_cog; // 感情優位か思考優位かのバランス
    public int hesitation;
    public int brain_power;
    public int embarrassment;
    public int intensive_thinking;
    public int imagination_activity;
    public int extreme_emotion;
    public int passionate;
    public int atmosphere; // 明るい/暗い
    public int anticipation;
    public int dissatisfaction;
    public int confidence;
}