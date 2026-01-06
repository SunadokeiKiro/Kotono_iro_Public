using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class StyledText : MonoBehaviour
{
    public bool isHeader = false;

    void Start()
    {
        Text t = GetComponent<Text>();
        UIStyler.ApplyStyleToText(t, isHeader);
    }
}
