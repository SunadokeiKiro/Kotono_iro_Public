// Scripts/UIOneLine.cs
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// uGUI上で2点間に直線を描画するためのコンポーネント。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
[RequireComponent(typeof(RectTransform))]
public class UIOneLine : Graphic
{
    [Tooltip("直線の始点座標")]
    [SerializeField]
    private Vector2 _position1;

    [Tooltip("直線の終点座標")]
    [SerializeField]
    private Vector2 _position2;
    
    [Tooltip("直線の太さ")]
    [SerializeField]
    private float _weight = 1.0f;

    /// <summary>
    /// メッシュを生成・更新する際にUnityから呼び出されるメソッド。
    /// </summary>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        // 以前の頂点をクリア
        vh.Clear();

        // 線の太さを考慮して4つの頂点を計算
        Vector2 p1 = new Vector2(_position1.x, _position1.y - _weight / 2);
        Vector2 p2 = new Vector2(_position1.x, _position1.y + _weight / 2);
        Vector2 p3 = new Vector2(_position2.x, _position2.y - _weight / 2);
        Vector2 p4 = new Vector2(_position2.x, _position2.y + _weight / 2);

        // 頂点を追加
        AddVert(vh, p1);
        AddVert(vh, p2);
        AddVert(vh, p3);
        AddVert(vh, p4);

        // 2つの三角形で四角形（線）を描画
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 1, 3);
    }

    /// <summary>
    /// 指定された座標に頂点を追加するヘルパーメソッド。
    /// </summary>
    private void AddVert(VertexHelper vh, Vector2 pos)
    {
        var vert = UIVertex.simpleVert;
        vert.position = pos;
        vert.color = color; // Graphicコンポーネントのcolorを使用
        vh.AddVert(vert);
    }
}