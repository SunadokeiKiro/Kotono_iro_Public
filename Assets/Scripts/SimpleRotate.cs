using UnityEngine;

/// <summary>
/// UIオブジェクトをZ軸で回転させ続けるシンプルなスクリプト。
/// ローディングアイコンなどに使用します。
/// </summary>
public class SimpleRotate : MonoBehaviour
{
    [Tooltip("1秒あたりの回転速度（度）")]
    [SerializeField]
    private float rotationSpeed = 200f; // マイナス値で時計回りに

    void Update()
    {
        // Z軸を中心にオブジェクトを回転させる
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
    }
}