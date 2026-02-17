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
        public Image buttonBackground;
    }

    [SerializeField] private List<TabPair> tabs;

    // ★ 深海テーマのカラーをUIStyler基準で設定
    private Color activeColor;
    private Color inactiveColor;

    IEnumerator Start()
    {
        yield return new WaitForEndOfFrame();

        // ★ UIStylerから深海テーマの色を取得
        activeColor = UIStyler.Accent;
        inactiveColor = new Color(0.08f, 0.12f, 0.18f, 0.5f); // 深海ダーク半透明

        foreach (var tab in tabs)
        {
            tab.tabButton.onClick.AddListener(() => OnTabClicked(tab));
        }

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
            
            // ★ UIFadeHelperでフェード切替
            if (tab.contentPanel != null)
            {
                if (isActive)
                    UIFadeHelper.FadeIn(this, tab.contentPanel, 0.2f);
                else
                    UIFadeHelper.FadeOut(this, tab.contentPanel, 0.15f);
            }

            if (tab.buttonBackground != null)
            {
                tab.buttonBackground.color = isActive ? activeColor : inactiveColor;
            }
            
            var tmp = tab.tabButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.color = isActive ? Color.white : new Color(0.55f, 0.6f, 0.7f, 0.8f);
                tmp.ForceMeshUpdate();
            }
        }
    }
}
