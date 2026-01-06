using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(ScrollRect))]
public class StyledScrollView : MonoBehaviour
{
    void Start()
    {
        ScrollRect scroll = GetComponent<ScrollRect>();
        UIStyler.ApplyStyleToScrollView(scroll);
    }
}
