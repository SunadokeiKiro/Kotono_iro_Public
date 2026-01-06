using UnityEngine;
using TMPro;

[RequireComponent(typeof(TMP_InputField))]
public class StyledTMPInputField : MonoBehaviour
{
    void Start()
    {
        TMP_InputField input = GetComponent<TMP_InputField>();
        UIStyler.ApplyStyleToTMPInputField(input);
    }

    void OnValidate()
    {
        // Optional: Could add ExecuteInEditMode logic if instant preview is desired,
        // but sticking to Start is safer to avoid serialization dirtying issues.
    }
}
