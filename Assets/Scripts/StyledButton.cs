using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class StyledButton : MonoBehaviour
{
    [Tooltip("Check this if the button is an icon (no text/background frame).")]
    public bool isIconOnly = false;

    void Start()
    {
        Button btn = GetComponent<Button>();
        UIStyler.ApplyStyleToButton(btn, isIconOnly);
    }

    void OnValidate()
    {
        // Editor-time preview (optional, might need ExecuteInEditMode)
    }
}
