// Scripts/Config/ApiConfig.cs
using UnityEngine;

/// <summary>
/// API接続に関する設定値を保持するScriptableObject。
/// これにより、コードを変更せずにAPIの接続先などを変更できます。
/// </summary>
[CreateAssetMenu(fileName = "ApiConfig", menuName = "KotoGame/API Configuration", order = 1)]
public class ApiConfig : ScriptableObject
{
    [Header("API Endpoint")]
    [Tooltip("音声認識APIのベースURL")]
    [SerializeField]
    private string apiUrlBase = "https://acp-api-async.amivoice.com/v1/recognitions";

    [Header("Request Parameters")]
    [Tooltip("リクエストの'd'パラメータに設定する値")]
    [SerializeField]
    private string dParameter = "grammarFileNames=-a-general loggingOptOut=True sentimentAnalysis=True";


    // プロパティを介して外部から値を取得できるようにする
    public string ApiUrlBase => apiUrlBase;
    public string DParameter => dParameter;
}