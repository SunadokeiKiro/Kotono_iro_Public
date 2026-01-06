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
    private float initialHeight;   // 初期高さ（Return用）
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
            initialHeight = transform.position.y - rotationPivot.position.y; // 高さ(Y差分)
        }
        else
        {
            initialDistance = 15.0f; // Fallback
            initialHeight = 0f;
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
            
            // Return Home Logic: アイドル中は初期位置（距離＆高さ）に戻ろうとする
            // 手動ドラッグ中は行わない
            if (!isDragging)
            {
                // 現在の水平方向（XZ平面）のベクトルを取得
                Vector3 currentDir = (transform.position - rotationPivot.position);
                currentDir.y = 0; // 高さを無視
                currentDir.Normalize();

                if (currentDir == Vector3.zero) currentDir = Vector3.back;

                // 目標位置: ピボット + 水平方向 * 距離 + 高さ
                // これにより、経度(回転角)は維持しつつ、距離と高さだけ初期状態に戻す
                Vector3 homePos = rotationPivot.position + (currentDir * initialDistance);
                homePos.y = rotationPivot.position.y + initialHeight;

                transform.position = Vector3.SmoothDamp(transform.position, homePos, ref currentVelocity, 1.0f); // ゆっくり戻る
                
                // 回転もピボットを見るように戻す (Pitchをリセット)
                var lookRot = Quaternion.LookRotation(rotationPivot.position - transform.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 2.0f);
            }
        }
    }

    private void HandleManualRotation()
    {
        // ... (省略: 修正なし)
        float rotationInput = 0f;
        
        // Touch Input (Mobile / Simulator)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Moved)
            {
                isDragging = true;
                lastManualInteractionTime = Time.time;
                // Touch delta is in pixels. Adjust sensitivity accordingly.
                // Assuming standard density, 0.1f sensitivity relative to mouse might be needed, 
                // but let's use a dedicated multiplier or reuse manualRotationSpeed with factor.
                // Mouse X returns -1 to 1 usually (or delta). Touch delta is pixels.
                // Let's normalize by Screen.width or use a small factor. 
                // A factor of 0.1f * manualRotationSpeed usually works well for pixel deltas.
                rotationInput = touch.deltaPosition.x * 0.1f * manualRotationSpeed;
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
            float mouseX = Input.GetAxis("Mouse X");
            rotationInput = mouseX * manualRotationSpeed;
        }
        else
        {
            isDragging = false;
        }

        if (isDragging && Mathf.Abs(rotationInput) > 0.001f)
        {
            transform.RotateAround(rotationPivot.position, Vector3.up, rotationInput * Time.deltaTime);
        }

        // HandleManualRotation内でのLookAtは、Focus中以外常に呼ばれるべきだが、
        // Return Home中はSmoothDampした位置からLookAtしたい。
        // ここでは「ドラッグ中」のみ即時LookAtし、それ以外はUpdateのReturn Logicに任せるか？
        // 既存コードでは HandleManualRotation 下で LookAt している。
        // Return Logic で rotation を Slerp しているので競合する。
        // -> HandleManualRotation の LookAt を条件付きにするか、Return Logic の Slerp を優先する。
        
        // 修正: ManualRotation内では LookAt を削除し、Updateの最後に共通で行う設計にするのが筋だが、
        // ここでは「ドラッグ中」だけ強制LookAtし、戻り中はSlerpに任せる。
        if (isDragging) 
        {
             transform.LookAt(rotationPivot);
        }
        
        if (!isDragging && (Time.time - lastManualInteractionTime > resumeIdleDelay))
        {
            transform.RotateAround(rotationPivot.position, idleAxis, idleRotationSpeed * Time.deltaTime);
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
