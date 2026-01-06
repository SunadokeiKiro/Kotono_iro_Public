using UnityEngine;
using UnityEngine.UI;
using System.Collections; // ★ Added for IEnumerator
using System.Collections.Generic;

public class SettingsTabController : MonoBehaviour
{
    [System.Serializable]
    public class TabPair
    {
        public Button tabButton;
        public GameObject contentPanel;
        public Image buttonBackground; // Optional: to highlight active tab
    }

    [SerializeField] private List<TabPair> tabs;
    [SerializeField] private Color activeColor = new Color(0.2f, 0.8f, 0.6f, 1f); // Accent
    [SerializeField] private Color inactiveColor = new Color(0.25f, 0.25f, 0.25f, 0.7f); // Dark Gray

    IEnumerator Start()
    {
        // Wait for other scripts (SettingsManager/UIStyler) to initialize
        // SettingsManager performs UIStyler.Apply... in Start().
        // We wait for EndOfFrame to ensure we override any default styles if necessary.
        yield return new WaitForEndOfFrame();

        // Setup buttons
        foreach (var tab in tabs)
        {
            tab.tabButton.onClick.AddListener(() => OnTabClicked(tab));
        }

        // Activate first tab by default
        if (tabs.Count > 0)
        {
            OnTabClicked(tabs[0]);
        }
    }

    private void OnTabClicked(TabPair selectedTab)
    {
        foreach (var tab in tabs)
        {
            bool isActive = (tab == selectedTab);
            
            // Show/Hide Panel
            if (tab.contentPanel != null)
                tab.contentPanel.SetActive(isActive);

            // Update Button Visuals
            if (tab.buttonBackground != null)
            {
                tab.buttonBackground.color = isActive ? activeColor : inactiveColor;
            }
            
            // Optional: Change Text Color? 
            // Currently assuming UIStyler handles basic text, 
            // but highlighting active text is also good practice.
            var tmp = tab.tabButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.color = isActive ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);
                tmp.ForceMeshUpdate(); // ★ Force update to ensure font/style is applied
            }
        }
    }
}
