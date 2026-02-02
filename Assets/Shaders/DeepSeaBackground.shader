Shader "Custom/DeepSeaBackground"
{
    Properties
    {
        _TopColor ("Top Light Color", Color) = (0.2, 0.4, 0.6, 1)
        _BottomColor ("Bottom Light Color", Color) = (0.1, 0.3, 0.5, 1)
        _DeepColor ("Deep Sea Color", Color) = (0.02, 0.05, 0.1, 1)
        _LightIntensity ("Light Intensity", Range(0, 2)) = 0.8
        _LightFalloff ("Light Falloff", Range(0.1, 5)) = 2.0
        _CenterDarkness ("Center Darkness", Range(0, 1)) = 0.7
        _CenterRadius ("Center Dark Radius", Range(0, 0.5)) = 0.3
        
        _NoiseScale ("Noise Scale", Range(0.1, 10)) = 2.0
        _NoiseSpeed ("Noise Speed", Range(0, 0.5)) = 0.05
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.3
        
        _Time2 ("Time", Float) = 0
        
        // 感情連動
        _EmotionHue ("Emotion Hue Shift", Range(-0.5, 0.5)) = 0
        _EmotionIntensity ("Emotion Intensity", Range(0, 1)) = 0
        _ColorVariation ("Color Variation Range", Range(0, 0.2)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        LOD 100
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            float4 _TopColor;
            float4 _BottomColor;
            float4 _DeepColor;
            float _LightIntensity;
            float _LightFalloff;
            float _CenterDarkness;
            float _CenterRadius;
            
            float _NoiseScale;
            float _NoiseSpeed;
            float _NoiseIntensity;
            
            float _EmotionHue;
            float _EmotionIntensity;
            float _ColorVariation;
            
            // Simple noise function
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                
                float a = hash(i);
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));
                
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            
            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * noise(p);
                    p *= 2.0;
                    amplitude *= 0.5;
                }
                return value;
            }
            
            // RGB to HSV
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            
            // HSV to RGB
            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float time = _Time.y;
                
                // ノイズによる揺らぎ
                float2 noiseUV = uv * _NoiseScale + time * _NoiseSpeed;
                float n = fbm(noiseUV);
                float n2 = fbm(noiseUV * 1.5 + float2(100, 100));
                
                // 上からの光（y=1が上）
                float topLight = pow(uv.y, _LightFalloff);
                topLight += n * _NoiseIntensity * topLight;
                
                // 下からの光（y=0が下）
                float bottomLight = pow(1.0 - uv.y, _LightFalloff);
                bottomLight += n2 * _NoiseIntensity * bottomLight;
                
                // 中央を暗くする（球体の邪魔をしない）
                float2 center = uv - 0.5;
                float distFromCenter = length(center);
                float centerMask = smoothstep(_CenterRadius, _CenterRadius + 0.3, distFromCenter);
                centerMask = lerp(1.0 - _CenterDarkness, 1.0, centerMask);
                
                // ライトにマスク適用
                topLight *= centerMask;
                bottomLight *= centerMask;
                
                // 色の揺らぎ（感情値を中心に±ColorVariationで変化）
                float colorWave = sin(time * 0.3 + n * 3.14159) * _ColorVariation;
                float hueShift = _EmotionHue + colorWave;
                
                // 上の光の色を調整
                float3 topHSV = rgb2hsv(_TopColor.rgb);
                topHSV.x = frac(topHSV.x + hueShift);
                topHSV.z *= (1.0 + _EmotionIntensity * 0.3);
                float3 topRGB = hsv2rgb(topHSV);
                
                // 下の光の色を調整
                float3 bottomHSV = rgb2hsv(_BottomColor.rgb);
                bottomHSV.x = frac(bottomHSV.x + hueShift * 0.7);
                bottomHSV.z *= (1.0 + _EmotionIntensity * 0.2);
                float3 bottomRGB = hsv2rgb(bottomHSV);
                
                // 最終色の合成
                float3 deepColor = _DeepColor.rgb;
                float3 finalColor = deepColor;
                finalColor = lerp(finalColor, topRGB, topLight * _LightIntensity);
                finalColor = lerp(finalColor, bottomRGB, bottomLight * _LightIntensity * 0.7);
                
                // 微細なノイズを追加（深海の粒子感）
                float fineNoise = noise(uv * 50.0 + time * 0.1) * 0.02;
                finalColor += fineNoise;
                
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}
