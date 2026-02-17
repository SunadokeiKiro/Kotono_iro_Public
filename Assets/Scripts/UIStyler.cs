using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI要素に共通のスタイル（深海テーマ、グラスモーフィズム）を適用するユーティリティクラス。
/// ランタイムで角丸Spriteを生成し、全UIコンポーネントに統一的なビジュアルを提供します。
/// </summary>
public static class UIStyler
{
    // ======================================================================
    // Color Palette — 深海テーマ（Deep Sea Theme）
    // ======================================================================
    private static readonly Color ColorTextMain = new Color(0.95f, 0.97f, 1.0f, 1.0f);   // クールホワイト
    private static readonly Color ColorTextSub  = new Color(0.7f, 0.78f, 0.85f, 1.0f);    // ソフトブルーグレー
    private static readonly Color ColorControlBg = new Color(0.08f, 0.12f, 0.18f, 0.6f);  // 深海ダーク半透明
    private static readonly Color ColorButtonBg  = new Color(0.1f, 0.16f, 0.24f, 0.55f);  // 深海ボタン半透明
    private static readonly Color ColorAccent    = new Color(0.3f, 0.85f, 0.9f, 1.0f);    // シアンアクセント
    private static readonly Color ColorGlass     = new Color(0.06f, 0.1f, 0.16f, 0.45f);  // グラスモーフィズム用
    private static readonly Color ColorGlassBorder = new Color(0.3f, 0.5f, 0.7f, 0.15f);  // ガラス境界線

    // ======================================================================
    // Rounded Sprite Generation — 角丸Sprite生成 & キャッシュ
    // ======================================================================
    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    // デフォルトの角丸半径
    private const int DefaultCornerRadius = 24;
    private const int DefaultSpriteSize = 64;  // 9-Slice用に小さいテクスチャで十分

    /// <summary>
    /// 角丸矩形のSpriteをランタイム生成します（アンチエイリアス＋9-Slice対応）。
    /// 同一パラメータはキャッシュされ再利用されます。
    /// </summary>
    public static Sprite GetOrCreateRoundedSprite(int radius = DefaultCornerRadius,
                                                   int size = DefaultSpriteSize,
                                                   Color? fillColor = null)
    {
        Color fill = fillColor ?? Color.white; // 色はImageコンポーネント側で乗算するので白で生成
        string key = $"rounded_{size}_{radius}_{fill.r:F2}_{fill.g:F2}_{fill.b:F2}_{fill.a:F2}";

        if (spriteCache.TryGetValue(key, out Sprite cached))
        {
            return cached;
        }

        Texture2D tex = CreateRoundedRectTexture(size, size, radius, fill);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        // 9-Slice用のborder（左・下・右・上）を角丸半径に合わせる
        float border = Mathf.Min(radius + 2, size / 2 - 1);
        Vector4 borders = new Vector4(border, border, border, border);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            borders
        );
        sprite.name = key;
        spriteCache[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// ピクセル単位で角丸矩形テクスチャを描画します（SDF風アンチエイリアス付き）。
    /// </summary>
    private static Texture2D CreateRoundedRectTexture(int width, int height, int radius, Color fillColor)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];
        Color clear = new Color(0, 0, 0, 0);

        float r = Mathf.Min(radius, Mathf.Min(width, height) / 2f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dist = RoundedRectSDF(x, y, width, height, r);

                if (dist <= -1f)
                {
                    // 完全に内側
                    pixels[y * width + x] = fillColor;
                }
                else if (dist >= 1f)
                {
                    // 完全に外側
                    pixels[y * width + x] = clear;
                }
                else
                {
                    // エッジ（アンチエイリアス）
                    float alpha = Mathf.Clamp01(0.5f - dist * 0.5f);
                    pixels[y * width + x] = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * alpha);
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 角丸矩形のSigned Distance Field。
    /// 負の値 = 内側、正の値 = 外側、0 = エッジ上。
    /// </summary>
    private static float RoundedRectSDF(int px, int py, int w, int h, float r)
    {
        // ピクセル中心からの距離を計算
        float cx = px + 0.5f;
        float cy = py + 0.5f;

        // 矩形の各コーナー円へのSDF
        float dx = Mathf.Max(Mathf.Abs(cx - w * 0.5f) - (w * 0.5f - r), 0f);
        float dy = Mathf.Max(Mathf.Abs(cy - h * 0.5f) - (h * 0.5f - r), 0f);

        return Mathf.Sqrt(dx * dx + dy * dy) - r;
    }

    // ======================================================================
    // Style Application — スタイル適用メソッド
    // ======================================================================

    public static void ApplyStyleToText(Text text, bool isHeader = false)
    {
        if (text == null) return;
        text.color = isHeader ? ColorTextMain : ColorTextSub;
    }

    public static void ApplyStyleToButton(Button button, bool isIconOnly = false)
    {
        if (button == null) return;

        Image bg = button.GetComponent<Image>();
        if (bg != null)
        {
            if (isIconOnly)
            {
                // アイコンボタン: 背景は透明、アイコンにソフトな白
                bg.color = Color.clear;
                foreach (Transform child in button.transform)
                {
                    Image iconImg = child.GetComponent<Image>();
                    if (iconImg != null && iconImg != bg)
                    {
                        iconImg.color = new Color(0.85f, 0.9f, 0.95f, 0.7f);
                    }
                }
            }
            else
            {
                // 通常ボタン: 角丸Sprite + 深海カラー
                bg.sprite = GetOrCreateRoundedSprite();
                bg.type = Image.Type.Sliced;
                bg.color = ColorButtonBg;
            }
        }

        // テキスト色
        Text t = button.GetComponentInChildren<Text>();
        if (t != null) t.color = ColorTextMain;

        TextMeshProUGUI tmp = button.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.color = ColorTextMain;
    }

    /// <summary>
    /// パネルにグラスモーフィズム風のスタイルを適用します。
    /// 半透明の深海色 + 角丸Sprite + ソフトな境界感。
    /// </summary>
    public static void ApplyGlassStyle(Image panel, float alpha = -1f)
    {
        if (panel == null) return;

        panel.sprite = GetOrCreateRoundedSprite(16, 48);
        panel.type = Image.Type.Sliced;
        panel.color = alpha >= 0f
            ? new Color(ColorGlass.r, ColorGlass.g, ColorGlass.b, alpha)
            : ColorGlass;
    }

    public static void ApplyStyleToTMPInputField(TMP_InputField input)
    {
        if (input == null) return;

        Image bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GetOrCreateRoundedSprite(12, 48);
            bg.type = Image.Type.Sliced;
            bg.color = ColorControlBg;
        }

        if (input.textComponent != null)
            input.textComponent.color = ColorTextMain;

        if (input.placeholder != null)
        {
            Graphic g = input.placeholder.GetComponent<Graphic>();
            if (g != null) g.color = new Color(0.45f, 0.55f, 0.65f, 0.5f);
        }
    }

    public static void ApplyStyleToInputField(InputField input)
    {
        if (input == null) return;

        Image bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GetOrCreateRoundedSprite(12, 48);
            bg.type = Image.Type.Sliced;
            bg.color = ColorControlBg;
        }

        if (input.textComponent != null)
            input.textComponent.color = ColorTextMain;

        if (input.placeholder != null)
        {
            Graphic g = input.placeholder.GetComponent<Graphic>();
            if (g != null) g.color = new Color(0.45f, 0.55f, 0.65f, 0.5f);
        }
    }

    public static void ApplyStyleToSlider(Slider slider)
    {
        if (slider == null) return;

        // Background
        Transform bgTrans = slider.transform.Find("Background");
        if (bgTrans != null)
        {
            Image bg = bgTrans.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0.3f, 0.5f, 0.7f, 0.12f); // ソフトブルー
        }

        // Fill Area
        Transform fillTrans = slider.fillRect;
        if (fillTrans != null)
        {
            Image fill = fillTrans.GetComponent<Image>();
            if (fill != null) fill.color = ColorAccent;
        }

        // Handle
        Transform handleTrans = slider.handleRect;
        if (handleTrans != null)
        {
            Image handle = handleTrans.GetComponent<Image>();
            if (handle != null) handle.color = ColorTextMain;
        }
    }

    public static void ApplyStyleToPanel(Image panel)
    {
        if (panel == null) return;
        // グラスモーフィズム風パネル
        panel.sprite = GetOrCreateRoundedSprite(16, 48);
        panel.type = Image.Type.Sliced;
        panel.color = new Color(0.04f, 0.07f, 0.12f, 0.8f);
    }

    public static void ApplyStyleToTMP(TextMeshProUGUI text, bool isHeader = false)
    {
        if (text == null) return;
        text.color = isHeader ? ColorTextMain : ColorTextMain;
    }

    public static void ApplyStyleToScrollView(ScrollRect scroll)
    {
        if (scroll == null) return;

        Image bg = scroll.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GetOrCreateRoundedSprite(16, 48);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.04f, 0.07f, 0.12f, 0.85f);
        }

        if (scroll.verticalScrollbar != null) ApplyStyleToScrollbar(scroll.verticalScrollbar);
        if (scroll.horizontalScrollbar != null) ApplyStyleToScrollbar(scroll.horizontalScrollbar);
    }

    private static void ApplyStyleToScrollbar(Scrollbar sb)
    {
        if (sb == null) return;

        if (sb.handleRect != null)
        {
            Image handle = sb.handleRect.GetComponent<Image>();
            if (handle != null) handle.color = ColorAccent;
        }

        Image bg = sb.GetComponent<Image>();
        if (bg != null) bg.color = new Color(0.2f, 0.3f, 0.5f, 0.08f);
    }

    public static void ApplyStyleToToggle(Toggle toggle)
    {
        if (toggle == null) return;

        Graphic checkmark = toggle.graphic;
        if (checkmark != null)
            checkmark.color = ColorAccent;

        Text label = toggle.GetComponentInChildren<Text>();
        if (label != null) ApplyStyleToText(label);

        TextMeshProUGUI tmpLabel = toggle.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpLabel != null) ApplyStyleToTMP(tmpLabel);
    }

    // Overload for TMP_InputField
    public static void ApplyStyleToInputField(TMP_InputField input)
    {
        ApplyStyleToTMPInputField(input);
    }

    public static void ApplyStyleToDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null) return;

        Image bg = dropdown.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GetOrCreateRoundedSprite(12, 48);
            bg.type = Image.Type.Sliced;
            bg.color = ColorControlBg;
        }

        if (dropdown.captionText != null) dropdown.captionText.color = ColorTextMain;

        Transform arrow = dropdown.transform.Find("Arrow");
        if (arrow != null)
        {
            Image arrowImg = arrow.GetComponent<Image>();
            if (arrowImg != null) arrowImg.color = ColorTextSub;
        }

        if (dropdown.template != null)
        {
            Image templateBg = dropdown.template.GetComponent<Image>();
            if (templateBg != null)
            {
                templateBg.sprite = GetOrCreateRoundedSprite(12, 48);
                templateBg.type = Image.Type.Sliced;
                templateBg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);
            }
        }
    }

    // ======================================================================
    // Utility — ユーティリティ
    // ======================================================================

    /// <summary>
    /// アクセントカラーを外部から参照するためのプロパティ。
    /// </summary>
    public static Color Accent => ColorAccent;

    /// <summary>
    /// Spriteキャッシュをクリアします（シーン遷移時など）。
    /// </summary>
    public static void ClearSpriteCache()
    {
        foreach (var kvp in spriteCache)
        {
            if (kvp.Value != null && kvp.Value.texture != null)
            {
                Object.Destroy(kvp.Value.texture);
                Object.Destroy(kvp.Value);
            }
        }
        spriteCache.Clear();
    }
}
