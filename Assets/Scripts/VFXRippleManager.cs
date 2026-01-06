// Scripts/VFXRippleManager.cs (Pure C# ParticleSystem Version)
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(ParticleSystem))]
public class VFXRippleManager : MonoBehaviour
{
    [Header("Particle Settings")]
    [SerializeField] private ParticleSystem ps;
    [SerializeField] private int particleCount = 4000; // モバイル向けに調整 (3000-5000推奨)
    [SerializeField] private float sphereRadius = 6.28f;
    [SerializeField] private float particleSize = 0.05f;
    [SerializeField] private float sensitivity = 10.0f; // 感度調整用

    // 内部データ
    private ParticleSystem.Particle[] particles;
    private Vector3[] basePositions; // 各パーティクルの「基準位置」 (球体表面)
    private List<RippleData> activeRipples = new List<RippleData>();

    private void Awake()
    {
        if (ps == null) ps = GetComponent<ParticleSystem>();
    }

    private void Start()
    {
        InitializeParticles();
    }

    private void InitializeParticles()
    {
        if (ps == null) return;

        // パーティクルシステムの基本設定を上書き
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = particleCount;
        
        var emission = ps.emission;
        emission.enabled = false; // 生成はスクリプトで行う
        
        var shape = ps.shape;
        shape.enabled = false;

        // 配列確保
        particles = new ParticleSystem.Particle[particleCount];
        basePositions = new Vector3[particleCount];

        // 初期配置
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 randomPos = Random.onUnitSphere * sphereRadius;
            basePositions[i] = randomPos;
            
            particles[i].position = randomPos;
            particles[i].startSize = particleSize;
            particles[i].startColor = Color.white;
            particles[i].remainingLifetime = float.MaxValue; // 永続
        }

        // パーティクルをセット
        ps.SetParticles(particles, particleCount);
    }

    // パフォーマンス考慮: 最新の10個の波紋のみ計算対象とする
    private const int MAX_ACTIVE_RIPPLES = 10;

    // 表示用 (計算済みの10個の波紋)
    private List<RippleData> displayRipples = new List<RippleData>();

    private void Update()
    {
        if (ps == null || particles == null) return;

        // 計算には displayRipples (最大10個) を使用
        int count = displayRipples.Count;
        float currentTime = Time.time;

        // ... Debug Log (略) ...

 

        float time = Time.time;
        float driftSpeed = 0.5f;
        float driftAmount = 0.3f; 

        for (int i = 0; i < particleCount; i++)
        {
            Vector3 basePos = basePositions[i];
            
            // 1. アイドル状態の「漂い」計算 (三角関数でゆらぎを作る)
            // 座標ごとに位相をずらしてランダムな動きに見せる
            float dx = Mathf.Sin(time * driftSpeed + basePos.y * 2.0f);
            float dy = Mathf.Cos(time * driftSpeed + basePos.z * 2.0f);
            float dz = Mathf.Sin(time * driftSpeed + basePos.x * 2.0f);
            Vector3 driftOffset = new Vector3(dx, dy, dz) * driftAmount;

            // 常に漂い位置を使用
            Vector3 currentPos = (basePos + driftOffset).normalized * sphereRadius;

            float totalHeight = 0f;
            Color finalColor = Color.white; 
            
            for (int r = 0; r < count; r++)
            {
                var ripple = displayRipples[r];
                
                // --- Looping Logic (廃止: 常に動き続ける「ネズミ」にするためループリセットは不要) ---
                // 時間経過はずっと続き、それによって移動し続ける
                float age = currentTime - ripple.creationTime;

                // --- Epicenter Movement ("Mouse" Movement) ---
                // Confidenceが高いほど、震源地が球面上を早く移動する
                // 変更: Confidenceが0でも少しは動くようにする (Base 2.0 + Scaled 18.0)
                float moveSpeed = 2.0f + (ripple.confidence * 18.0f); // 度/秒
                
                // 1日経つと非常に大きな値になるが、Quaternion.AngleAxisは循環するので問題ない
                float angle = moveSpeed * age; 
                
                Vector3 moveAxis = Vector3.Cross(ripple.centerPosition, Vector3.up + Vector3.right * 0.5f).normalized;
                if (moveAxis == Vector3.zero) moveAxis = Vector3.right;

                Quaternion moveRot = Quaternion.AngleAxis(angle, moveAxis);
                Vector3 movingCenter = moveRot * ripple.centerPosition; 

                // --- "Mouse under carpet" Logic ---
 
                float dist = Vector3.Distance(currentPos, movingCenter);

                // 波紋パラメータ
                float amplifiedArousal = ripple.arousal * sensitivity;
                float amplifiedValence = ripple.valence * sensitivity;
                
                // 幅 (ネズミの大きさ) - ユーザー要望により半径を小さく調整
                // 旧 (Step 454まで): 0.3f + (amplifiedArousal * 0.6f) -> 最大6.3になり巨大すぎる
                // 新: 0.3f + (amplifiedArousal * 0.15f) -> 最大1.8 (球体半径6.28に対して程よいサイズ)
                // さらに万が一のために Clamp で上限を 2.0 に制限する
                // 幅 (ネズミの大きさ) - ユーザー要望「山の裾を広く、緩やかに」に対応
                // 幅 (ネズミの大きさ) - ユーザー要望「0.5くらいに」に対応
                // 変更: 0.8 -> 0.5。最大値も少し抑えて(5.0->3.0)全体を飲み込まないようにする
                float widthContrib = amplifiedArousal * 0.25f;
                float baseWidth = Mathf.Clamp(0.5f + widthContrib, 0.5f, 3.0f); 
                
                // --- Thoughtによる「尾 (Trail)」の計算 ---
                // 進行方向(Velocity)を計算: 回転軸(moveAxis)と現在の位置(movingCenter)の外積
                Vector3 velocityDir = Vector3.Cross(moveAxis, movingCenter).normalized;
                Vector3 dirToParticle = (currentPos - movingCenter).normalized;
                float alignment = Vector3.Dot(velocityDir, dirToParticle); // 1.0=前方, -1.0=後方

                float effectiveWidth = baseWidth;

                // 後方(alignment < 0)にいる場合、Thoughtの値に応じて幅(尾)を伸ばす
                // Thoughtが1.0なら、後ろに長く(widthが広がる)なる
                if (alignment < 0)
                {
                    // alignmentは -1 ～ 0。Absをとって「真後ろ」ほど伸ばす
                    float backwardFactor = Mathf.Abs(alignment);
                    // 尾の長さ係数: Thought * 5.0倍 (調整可)
                    float tailExtension = 1.0f + (ripple.thought * 5.0f * backwardFactor);
                    effectiveWidth *= tailExtension;
                }
                
                // diff は単に震源地からの距離
                float diff = dist;
                
                if (diff < effectiveWidth)
                {
                    float t = diff / effectiveWidth;
                    
                    // 高さ (Cosine Bell)
                    // 以前の 1 - t^2(3-2t) よりも裾野(hem)が自然に広がる釣り鐘型に変更
                    // t=0で1, t=1で0
                    float h = 0.5f * (1.0f + Mathf.Cos(t * Mathf.PI));
                    
                    // 高さの係数調整
                    float heightScale = 0.8f + (amplifiedArousal * 0.6f);
                    
                    float heightContribution = h * heightScale;
                    totalHeight += heightContribution;

                    // 色の決定 (3色グラデーション: 不快(青) -> 中立(白) -> 快(オレンジ))
                    Color waveColor = GetColorFromValence(ripple.valence); // ripple.valenceは -1~1 (amplifiedを使用しないのが自然)
                    
                    // 色の合成強度を上げる (波が小さくても色が乗りやすくする)
                    float blendFactor = Mathf.Clamp01(heightContribution * 1.5f);
                    finalColor = Color.Lerp(finalColor, waveColor, blendFactor);
                }
            }

            // ... (3. 最終位置決定) ...

            // 3. 最終位置決定
            Vector3 normal = currentPos.normalized;
            
            // 隆起を加算
            particles[i].position = currentPos + (normal * totalHeight * 0.5f); 
            
            // 密度低下対策: 高い場所ほどパーティクルを大きくして隙間を埋める
            // また、輝度も上げて視認性を高める
            float sizeScale = 1.0f + (totalHeight * 0.5f); 
            particles[i].startSize = particleSize * sizeScale;
            
            particles[i].startColor = finalColor;
        }

        ps.SetParticles(particles, particleCount);
    }

    public void AddNewRipple(float valence, float arousal, float thought, float confidence, long timestamp = 0, Vector3? position = null)
    {
        Debug.Log($"[VFXRippleManager] AddNewRipple Called! Val:{valence}, Aro:{arousal}");
        
        Vector3 spawnPos = position.HasValue ? position.Value : Random.onUnitSphere * sphereRadius;


        long finalTimestamp = (timestamp == 0) ? System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() : timestamp;

        var newRipple = new RippleData
        {
            valence = valence,
            arousal = arousal,
            thought = thought,
            confidence = confidence,
            centerPosition = spawnPos,
            creationTime = Time.time,
            unixTimestamp = finalTimestamp,
            startTimestamp = finalTimestamp,
            endTimestamp = finalTimestamp
        };
        activeRipples.Add(newRipple);

        // 追加のたびに表示用波紋を再計算
        RecalculateDisplayRipples();
    }

    // 10個のバケットに圧縮するアルゴリズム
    private void RecalculateDisplayRipples()
    {
        displayRipples.Clear();
        int totalData = activeRipples.Count;
        int targetCount = 10;

        // データが10個以下ならそのまま使う
        if (totalData <= targetCount)
        {
            displayRipples.AddRange(activeRipples);
            return;
        }

        // バケット計算
        float bucketSize = (float)totalData / targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            float startIdx = i * bucketSize;
            float endIdx = (i + 1) * bucketSize;

            int firstIdx = Mathf.FloorToInt(startIdx);
            int lastIdx = Mathf.FloorToInt(endIdx - 0.001f); // 境界の取り扱い

            float totalWeight = 0f;
            float sumValence = 0f;
            float sumArousal = 0f;
            float sumThought = 0f;
            float sumConfidence = 0f;
            float sumTime = 0f;
            double sumUnixTime = 0d; // Use double for large number averaging
            Vector3 sumPos = Vector3.zero;

            long minTs = long.MaxValue;
            long maxTs = long.MinValue;

            // バケットに含まれるインデックスを走査
            for (int idx = firstIdx; idx <= lastIdx; idx++)
            {
                if (idx >= totalData) break;

                // そのインデックスがこのバケット内で占める範囲 [overlapStart, overlapEnd]
                float rangeStart = Mathf.Max(startIdx, idx);
                float rangeEnd = Mathf.Min(endIdx, idx + 1.0f);
                float weight = Mathf.Max(0f, rangeEnd - rangeStart); // 0.833... or 0.166...

                var data = activeRipples[idx];
                
                sumValence += data.valence * weight;
                sumArousal += data.arousal * weight;
                sumThought += data.thought * weight;
                sumConfidence += data.confidence * weight;
                sumTime += data.creationTime * weight;
                sumUnixTime += (double)data.unixTimestamp * weight;
                sumPos += data.centerPosition * weight;

                if (data.unixTimestamp < minTs) minTs = data.unixTimestamp;
                if (data.unixTimestamp > maxTs) maxTs = data.unixTimestamp;

                totalWeight += weight;
            }

            if (totalWeight > 0)
            {
                var avgRipple = new RippleData
                {
                    valence = sumValence / totalWeight,
                    arousal = sumArousal / totalWeight,
                    thought = sumThought / totalWeight,
                    confidence = sumConfidence / totalWeight,
                    creationTime = sumTime / totalWeight,
                    unixTimestamp = (long)(sumUnixTime / totalWeight),
                    startTimestamp = minTs, // Range Start
                    endTimestamp = maxTs,   // Range End
                    centerPosition = (sumPos / totalWeight).normalized * sphereRadius // 球面上に戻す
                };
                displayRipples.Add(avgRipple);
            }
        }
    }

    /// <summary>
    /// Valence (快/不快) に基づいて色を決定します。
    /// -1.0(不快): 青
    ///  0.0(中立): 白 (少し青みがかった白)
    /// +1.0(快): オレンジ
    /// </summary>
    private Color GetColorFromValence(float valence)
    {
        // カラーパレット定義
        Color colorNegative = new Color(0.1f, 0.2f, 0.9f); // 深い青
        Color colorNeutral  = new Color(0.2f, 0.8f, 0.2f); // 緑 (ユーザー要望により変更)
        Color colorPositive = new Color(1.0f, 0.5f, 0.1f); // 鮮やかなオレンジ

        if (valence < 0)
        {
            // 不快(-1) ～ 中立(0)
            float t = valence + 1.0f; // 0.0 ~ 1.0 に正規化
            return Color.Lerp(colorNegative, colorNeutral, t);
        }
        else
        {
            // 中立(0) ～ 快(+1)
            float t = valence; // 0.0 ~ 1.0
            return Color.Lerp(colorNeutral, colorPositive, t);
        }
    }

    // ... (Existing code) ...

    public void ResetRipples()
    {
        activeRipples.Clear();
        displayRipples.Clear(); // ★ Fix: アーカイブモード切り替え時に古い表示用データが残らないようにクリア

        // パーティクル位置を元に戻す
        if (particles != null && ps != null)
        {
            for(int i=0; i<particleCount; i++)
            {
                particles[i].position = basePositions[i];
                particles[i].startColor = Color.white;
                particles[i].startSize = particleSize; // ★ Reset size too for consistency
            }
            ps.SetParticles(particles, particleCount);
        }
    }

    /// <summary>
    /// 指定された座標（球体表面）に最も近い波紋データを返します。
    /// タップ判定用です。
    /// </summary>
    /// <param name="hitPoint">Raycastがヒットしたワールド座標</param>
    /// <param name="thresholdDistance">ヒットとみなす最大距離</param>
    /// <param name="currentCenter">計算された現在の波紋中心座標（出力用）</param>
    /// <returns>見つかったRippleData。なければnull</returns>
    public RippleData? GetClosestRipple(Vector3 hitPoint, float thresholdDistance, out Vector3 currentCenter)
    {
        currentCenter = Vector3.zero;
        if (displayRipples.Count == 0) return null;

        // hitPointはワールド座標なので、ローカル（球体中心基準）に変換が必要だが、
        // このスクリプトが球体オブジェクト自体にアタッチされているなら transform.InverseTransformPoint(hitPoint) を使う。
        // ただしParticleSystemはSimulationSpace=Local設定済み。
        Vector3 localHitPos = transform.InverseTransformPoint(hitPoint);

        RippleData? closest = null; // Nullable
        float minDist = float.MaxValue;
        float currentTime = Time.time;

        foreach (var ripple in displayRipples)
        {
            // Updateループと同じロジックで「現在の中心位置」を計算
            Vector3 movingCenter = CalculateCurrentRippleCenter(ripple, currentTime);
            
            float dist = Vector3.Distance(localHitPos, movingCenter);
            if (dist < minDist)
            {
                minDist = dist;
                closest = ripple; // Value copy
                currentCenter = transform.TransformPoint(movingCenter); // ワールド座標に戻して返す
            }
        }

        if (minDist <= thresholdDistance)
        {
            return closest;
        }
        return null;
    }

    // Update内の移動ロジックを再利用できるように切り出し
    public Vector3 CalculateCurrentRippleCenter(RippleData ripple, float currentTime)
    {
        float age = currentTime - ripple.creationTime;
        float moveSpeed = 2.0f + (ripple.confidence * 18.0f); // 同様にBase速度を追加 
        float angle = moveSpeed * age; 
        
        Vector3 moveAxis = Vector3.Cross(ripple.centerPosition, Vector3.up + Vector3.right * 0.5f).normalized;
        if (moveAxis == Vector3.zero) moveAxis = Vector3.right;

        Quaternion moveRot = Quaternion.AngleAxis(angle, moveAxis);
        return moveRot * ripple.centerPosition; 
    }

    public void ClearRipples()
    {
        activeRipples.Clear();
        displayRipples.Clear();
        
        // Reset particle positions to base sphere
        if (particles != null && basePositions != null)
        {
            for (int i = 0; i < particleCount; i++)
            {
                particles[i].position = basePositions[i];
                particles[i].startColor = Color.white;
            }
            if (ps != null) ps.SetParticles(particles, particleCount);
        }
    }
}