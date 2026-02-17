using UnityEngine;


/// <summary>
/// カメラを制御するスクリプト。
/// アイドル時の自動回転機能と、特定のターゲットにフォーカスする機能を持ちます。
/// 「言のイロ」の世界観に合わせ、ゆっくりとしたスムーズな動きを実現します。
/// </summary>
public class SimpleCameraController : MonoBehaviour
{
    [Header("Idle Rotation Settings")]
    [SerializeField] private Transform rotationPivot; // 回転の中心（通常は球体の中心）
    [SerializeField] private float idleRotationSpeed = 3.0f; // 既存のSimpleRotateは早すぎるので調整
    [SerializeField] private Vector3 idleAxis = Vector3.up;

    public void SetPivot(Transform pivot) { rotationPivot = pivot; }

    [Header("Manual Rotation Settings")]
    [SerializeField] private float manualRotationSpeed = 300.0f; // マウス感度
    [SerializeField] private float resumeIdleDelay = 3.0f; // 手動操作後、自動回転に戻るまでの時間

    [Header("Focus Settings")]
    [SerializeField] private float focusDistance = 16.0f; // 12.0 -> 16.0 (さらに引いた画角)
    [SerializeField] private float moveSmoothTime = 0.5f;

    // State
    private bool isFocusing = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    
    private Vector3 currentVelocity;

    // Manual Drag State
    private bool isDragging = false;
    // State
    private float initialDistance; // 初期距離（Return用）
    private float lastManualInteractionTime;

    void Start()
    {
        lastManualInteractionTime = -resumeIdleDelay; // 最初から自動回転するように

        if (rotationPivot == null)
        {
            var sphere = GameObject.Find("Sphere");
            if (sphere != null) rotationPivot = sphere.transform;
            else
            {
                var pivotObj = new GameObject("AutoCreatedPivot");
                pivotObj.transform.position = Vector3.zero;
                rotationPivot = pivotObj.transform;
            }
        }
        
        // 初期状態を保存
        if (rotationPivot != null)
        {
            initialDistance = Vector3.Distance(transform.position, rotationPivot.position);
        }
        else
        {
            initialDistance = 15.0f; // Fallback
        }
    }

    void Update()
    {
        if (rotationPivot == null) return;

        if (isFocusing)
        {
            // フォーカスモード: ターゲット位置へスムーズに移動
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, moveSmoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5.0f);
        }
        else
        {
            HandleManualRotation();
            
            // Return Home Logic: 距離のみ初期値に戻す（方向・仰角は維持）
            // moveSmoothTime で設定した時間で元の距離に戻る
            // 手動ドラッグ中は行わない
            if (!isDragging)
            {
                Vector3 offsetFromPivot = transform.position - rotationPivot.position;
                float currentDistance = offsetFromPivot.magnitude;

                // 距離だけ初期値に補正（方向はそのまま維持）
                if (currentDistance > 0.01f)
                {
                    Vector3 homePos = rotationPivot.position + offsetFromPivot.normalized * initialDistance;
                    transform.position = Vector3.SmoothDamp(transform.position, homePos, ref currentVelocity, moveSmoothTime);
                }

                // ピボットを見るように回転を補正
                var lookRot = Quaternion.LookRotation(rotationPivot.position - transform.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5.0f);
            }
        }
    }

    private void HandleManualRotation()
    {
        float rotationInputX = 0f;
        float rotationInputY = 0f;
        
        // Touch Input (Mobile / Simulator)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Moved)
            {
                isDragging = true;
                lastManualInteractionTime = Time.time;
                rotationInputX = touch.deltaPosition.x * 0.1f * manualRotationSpeed;
                rotationInputY = touch.deltaPosition.y * 0.1f * manualRotationSpeed;
            }
            else if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Stationary)
            {
                isDragging = true;
            }
            else
            {
                isDragging = false;
            }
        }
        // Mouse Input (Fallback / Editor)
        else if (Input.GetMouseButton(0))
        {
            isDragging = true;
            lastManualInteractionTime = Time.time;
            rotationInputX = Input.GetAxis("Mouse X") * manualRotationSpeed;
            rotationInputY = Input.GetAxis("Mouse Y") * manualRotationSpeed;
        }
        else
        {
            isDragging = false;
        }

        if (isDragging && (Mathf.Abs(rotationInputX) > 0.001f || Mathf.Abs(rotationInputY) > 0.001f))
        {
            // 横回転: ワールドY軸周り
            transform.RotateAround(rotationPivot.position, Vector3.up, rotationInputX * Time.deltaTime);

            // 縦回転: カメラのローカル右方向軸周り（制約なし・360度自由回転）
            float verticalDelta = -rotationInputY * Time.deltaTime;
            if (Mathf.Abs(verticalDelta) > 0.001f)
            {
                transform.RotateAround(rotationPivot.position, transform.right, verticalDelta);
            }

            // RotateAround後、ピボットを向くように補正
            transform.LookAt(rotationPivot);
        }
        
        // アイドル回転: 現在の画角（仰角）を維持したままY軸周りに水平回転
        if (!isDragging && (Time.time - lastManualInteractionTime > resumeIdleDelay))
        {
            transform.RotateAround(rotationPivot.position, Vector3.up, idleRotationSpeed * Time.deltaTime);
            // 回転後、常にピボットを向くように補正
            transform.LookAt(rotationPivot);
        }
    }

    public void FocusOnPoint(Vector3 surfacePoint)
    {
        isFocusing = true;
        
        // --- Horizon View Calculation (真横に近い視点) ---
        // 球体中心からポイントへの法線
        Vector3 surfaceNormal = (surfacePoint - rotationPivot.position).normalized;
        
        // 1. "Right" Vector (横方向) を求める (北極=Y軸 と仮定)
        Vector3 sideAxis = Vector3.Cross(surfaceNormal, Vector3.up).normalized;
        if (sideAxis == Vector3.zero) sideAxis = Vector3.right; // 特異点対策
        
        // 2. 法線を基準に、視点を倒す (例: 55度)
        // 60度くらい倒すと、球体のホライズンが見えるような「斜め横」になる
        Vector3 viewDirection = Quaternion.AngleAxis(55.0f, sideAxis) * surfaceNormal;

        // 3. 距離設定
        // サイドビューの場合は、少し引いた方が背景との対比が見やすいが、
        // ユーザー要望「半分波、半分背景」なら、表面すれすれから狙う必要がある。
        // focusDistanceはそのまま使い、角度で調整する。
        
        targetPosition = rotationPivot.position + (viewDirection * focusDistance);
        
        // 回転目標: アップベクトルを法線方向に合わせると「地面」感が出る
        targetRotation = Quaternion.LookRotation(surfacePoint - targetPosition, surfaceNormal);
    }

    public void ClearFocus()
    {
        isFocusing = false;
        // 距離を戻す処理はUpdate内の"Return Home Logic"が行う
    }
}
