using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI要素に共通のスタイル（ダークテーマ、ミニマリズム）を適用するユーティリティクラス。
/// </summary>
public static class UIStyler
{
    // Color Palette
    // Color Palette - Adjusted for better visibility
    private static readonly Color ColorTextMain = new Color(1.0f, 1.0f, 1.0f, 1.0f); // Pure White
    private static readonly Color ColorTextSub = new Color(0.9f, 0.9f, 0.9f, 1.0f); // Bright Gray
    private static readonly Color ColorControlBg = new Color(0.1f, 0.1f, 0.1f, 0.8f); // Dark Gray Translucent
    private static readonly Color ColorButtonBg = new Color(0.25f, 0.25f, 0.25f, 0.7f); // Brighter & More Opaque Button Background
    private static readonly Color ColorAccent = new Color(0.2f, 0.8f, 0.6f, 1.0f); // Green-Cyan Accent

    public static void ApplyStyleToText(Text text, bool isHeader = false)
    {
        if (text == null) return;
        text.color = isHeader ? ColorTextMain : ColorTextSub;
        // Font size could be adjusted here if needed, but layout might break
    }

    public static void ApplyStyleToButton(Button button, bool isIconOnly = false)
    {
        if (button == null) return;

        // Background Image
        Image bg = button.GetComponent<Image>();
        if (bg != null)
        {
            if (isIconOnly)
            {
                // 背景(Square Frame)は消す
                bg.color = Color.clear;

                // 子オブジェクトにあるアイコン画像を探して色をつける
                // (bgと同じImageでないものを探す)
                foreach(Transform child in button.transform)
                {
                    Image iconImg = child.GetComponent<Image>();
                    if (iconImg != null && iconImg != bg)
                    {
                        iconImg.color = new Color(1f, 1f, 1f, 0.6f);
                    }
                }
            }
            else
            {
                bg.color = ColorButtonBg;
            }
        }

        // Text (Legacy)
        Text t = button.GetComponentInChildren<Text>();
        if (t != null)
        {
            t.color = ColorTextMain;
        }

        // Text (TMP)
        TextMeshProUGUI tmp = button.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.color = ColorTextMain;
        }
    }

    public static void ApplyStyleToTMPInputField(TMP_InputField input)
    {
        if (input == null) return;

        // Background
        Image bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = ColorControlBg;
        }

        // Text Component (Input)
        if (input.textComponent != null)
        {
            input.textComponent.color = ColorTextMain;
        }

        // Placeholder
        if (input.placeholder != null)
        {
            Graphic g = input.placeholder.GetComponent<Graphic>();
            if (g != null) g.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        }
    }

    public static void ApplyStyleToInputField(InputField input)
    {
        if (input == null) return;

        // Background
        Image bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = ColorControlBg;
        }

        // Text Component (Input)
        if (input.textComponent != null)
        {
            input.textComponent.color = ColorTextMain;
        }

        // Placeholder
        if (input.placeholder != null)
        {
            Graphic g = input.placeholder.GetComponent<Graphic>();
            if (g != null) g.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
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
            if (bg != null) bg.color = new Color(1f, 1f, 1f, 0.1f); // Thin white
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
            if (handle != null) handle.color = Color.white;
        }
    }

    public static void ApplyStyleToPanel(Image panel)
    {
        if (panel == null) return;
        panel.color = new Color(0f, 0f, 0f, 0.85f); // Deep dark background
    }

    public static void ApplyStyleToTMP(TextMeshProUGUI text, bool isHeader = false)
    {
        if (text == null) return;
        text.color = isHeader ? ColorTextMain : ColorTextMain; // すべて白文字に統一 (User request "Apply design")
        // サブテキストが必要な場合は ColorTextSub を使用
    }

    public static void ApplyStyleToScrollView(ScrollRect scroll)
    {
        if (scroll == null) return;

        // Background
        Image bg = scroll.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0f, 0f, 0f, 0.85f); // Deep dark background for the panel
        }

        // Scrollbars
        if (scroll.verticalScrollbar != null) ApplyStyleToScrollbar(scroll.verticalScrollbar);
        if (scroll.horizontalScrollbar != null) ApplyStyleToScrollbar(scroll.horizontalScrollbar);
    }

    private static void ApplyStyleToScrollbar(Scrollbar sb)
    {
        if (sb == null) return;

        // Handle (Knob)
        if (sb.handleRect != null)
        {
             Image handle = sb.handleRect.GetComponent<Image>();
             if (handle != null) handle.color = ColorAccent;
        }

        // Output (Background)
        Image bg = sb.GetComponent<Image>();
        if (bg != null) bg.color = new Color(1f, 1f, 1f, 0.05f); // Very faint track
    }

    public static void ApplyStyleToToggle(Toggle toggle)
    {
        if (toggle == null) return;

        // Background
        Image bg = toggle.targetGraphic as Image;
        if (bg != null)
        {
            // 標準的なToggleの場合、Backgroundはチェックボックスの枠
            // ここではシンプルに
            // bg.color = ColorControlBg;
        }

        // Checkmark
        // CheckmarkはGraphicとして取得できる
        Graphic checkmark = toggle.graphic;
        if (checkmark != null)
        {
            checkmark.color = ColorAccent;
        }

        // Label
        Text label = toggle.GetComponentInChildren<Text>();
        if (label != null) ApplyStyleToText(label);
        
        TextMeshProUGUI tmpLabel = toggle.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpLabel != null) ApplyStyleToTMP(tmpLabel);
    }
    
    // Overload for TMP_InputField to match ScheduleSettingsUIManager usage
    public static void ApplyStyleToInputField(TMP_InputField input)
    {
        ApplyStyleToTMPInputField(input);
    }

    public static void ApplyStyleToDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null) return;

        // Main Background (Label Area)
        Image bg = dropdown.GetComponent<Image>();
        if (bg != null) bg.color = ColorControlBg;

        // Label Text
        if (dropdown.captionText != null) dropdown.captionText.color = ColorTextMain;

        // Arrow
        Transform arrow = dropdown.transform.Find("Arrow");
        if (arrow != null)
        {
            Image arrowImg = arrow.GetComponent<Image>();
            if (arrowImg != null) arrowImg.color = ColorTextSub;
        }
        
        // Template (Popup List)
        // Note: Template might be inactive, so we use the reference in dropdown
        if (dropdown.template != null)
        {
            Image templateBg = dropdown.template.GetComponent<Image>();
            if (templateBg != null) templateBg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f); // Darker Opaque
        }
        
        // Items (This usually requires runtime instantiation handling, 
        // but we can try to style the prototypes if accessible, or relying on TMP settings)
        // For now, simpler is better.
    }
}
