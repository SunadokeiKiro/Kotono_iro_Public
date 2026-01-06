using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// アタッチするだけで、そのオブジェクトについているUIコンポーネント(Button, Text, Inputなど)を判別し、
/// 自動的に共通スタイル(UIStyler)を適用する便利スクリプト。
/// </summary>
public class AutoUIStyler : MonoBehaviour
{
    [Header("Optional Settings")]
    [Tooltip("テキストの場合: 見出し(Header)として扱うか")]
    public bool isHeader = false;
    
    [Tooltip("ボタンの場合: アイコンのみ(枠線なし)として扱うか")]
    public bool isIconOnly = false;

    [Tooltip("実行時(Start)だけでなく、インスペクタ変更時にも適用するか")]
    public bool applyInEditMode = true;

    void Start()
    {
        ApplyStyle();
    }

    void OnValidate()
    {
        if (applyInEditMode)
        {
            // OnValidateはエディタ上で値を変えたときに呼ばれる。
            // 頻繁に呼ばれるため、重い処理は避けるべきだが、色変え程度ならOK。
            // ただし、Start()と異なり、実行中以外でも動作する。
            ApplyStyle();
        }
    }

    public void ApplyStyle()
    {
        // 1. Button
        Button btn = GetComponent<Button>();
        if (btn != null) UIStyler.ApplyStyleToButton(btn, isIconOnly);

        // 2. Toggle
        Toggle toggle = GetComponent<Toggle>();
        if (toggle != null) UIStyler.ApplyStyleToToggle(toggle);

        // 3. Slider
        Slider slider = GetComponent<Slider>();
        if (slider != null) UIStyler.ApplyStyleToSlider(slider);

        // 4. InputField (TMP)
        TMP_InputField tmpInput = GetComponent<TMP_InputField>();
        if (tmpInput != null) UIStyler.ApplyStyleToTMPInputField(tmpInput);

        // 5. InputField (Legacy)
        InputField legInput = GetComponent<InputField>();
        if (legInput != null) UIStyler.ApplyStyleToInputField(legInput);

        // 6. Dropdown (TMP)
        TMP_Dropdown tmpDropdown = GetComponent<TMP_Dropdown>();
        if (tmpDropdown != null) UIStyler.ApplyStyleToDropdown(tmpDropdown);

        // 7. TextMeshPro
        TextMeshProUGUI tmpText = GetComponent<TextMeshProUGUI>();
        if (tmpText != null) UIStyler.ApplyStyleToTMP(tmpText, isHeader);

        // 8. Text (Legacy)
        Text legText = GetComponent<Text>();
        if (legText != null) UIStyler.ApplyStyleToText(legText, isHeader);
        
        // 9. ScrollRect
        ScrollRect scroll = GetComponent<ScrollRect>();
        if (scroll != null) UIStyler.ApplyStyleToScrollView(scroll);
    }
}
