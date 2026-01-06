using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class StyledTMP : MonoBehaviour
{
    public bool isHeader = false;

    void Start()
    {
        TextMeshProUGUI t = GetComponent<TextMeshProUGUI>();
        UIStyler.ApplyStyleToTMP(t, isHeader);
    }
}
