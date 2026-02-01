using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

/// <summary>
/// Controls the Background Heatmap (Calendar View).
/// Generates a grid of squares representing days of the month.
/// </summary>
public class BackgroundGraphController : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private Transform gridContainer; // The parent object with GridLayoutGroup
    [SerializeField] private GameObject cellPrefab;   // A simple Image prefab
    [SerializeField] private Text monthLabel; // Optional: To show "2023.11" etc.

    [Header("Appearance")]
    [SerializeField] private Color colorLevel0 = new Color(1f, 1f, 1f, 0.1f); // Transparent Grey
    [SerializeField] private Color colorLevel1 = new Color(0.6f, 0.9f, 0.6f, 0.5f); // Light Green
    [SerializeField] private Color colorLevel2 = new Color(0.3f, 0.7f, 0.3f, 0.8f); // Medium Green
    [SerializeField] private Color colorLevel3 = new Color(0.1f, 0.5f, 0.1f, 1.0f); // Dark Green

    private List<Image> cellList = new List<Image>();
    private int currentYear;
    private int currentMonth;

    private void Awake()
    {
        // If no prefab set, try to generate one or warn
        if (cellPrefab == null)
        {
            Debug.LogWarning("[BackgroundGraphController] Cell Prefab is null. Will create runtime.");
        }
    }

    /// <summary>
    /// Re-generates the grid for a specific month.
    /// monthKey format: "yyyy-MM"
    /// </summary>
    public void GenerateGrid(string monthKey)
    {
        // Parse "yyyy-MM"
        string[] parts = monthKey.Split('-');
        if (parts.Length < 2) return;

        if (int.TryParse(parts[0], out int year) && int.TryParse(parts[1], out int month))
        {
            currentYear = year;
            currentMonth = month;
            GenerateGridInternal(year, month);
        }
    }

    private void GenerateGridInternal(int year, int month)
    {
        // Clear existing
        foreach (Transform child in gridContainer)
        {
            Destroy(child.gameObject);
        }
        cellList.Clear();

        if (monthLabel) monthLabel.text = $"{year}.{month}";

        // Calculate days in month
        int daysInMonth = DateTime.DaysInMonth(year, month);
        DateTime firstDay = new DateTime(year, month, 1);
        
        // Calculate offset (DayOfWeek: Sun=0, Mon=1...)
        // We want 7 columns (Sun-Sat)
        int startOffset = (int)firstDay.DayOfWeek; 

        // Total cells needed = startOffset + daysInMonth
        // Just fill the grid sequentially. GridLayoutGroup handles the rows (constraint count = 7).
        
        // 1. Empty cells (pad before 1st)
        for (int i = 0; i < startOffset; i++)
        {
            CreateCell(null); // Empty/Invisible cell
        }

        // 2. Day cells
        for (int day = 1; day <= daysInMonth; day++)
        {
            var cell = CreateCell(day);
            cellList.Add(cell);
        }
        
        // 3. (Optional) Pad end? Not strictly necessary for this look.
        
        // Request initial update
        if (StreakManager.Instance != null)
        {
            // Trigger an update from manager's cache for this month
             UpdateHeatmap(StreakManager.Instance.GetMonthData(currentYear, currentMonth));
        }
    }

    private Image CreateCell(int? dayNumber)
    {
        GameObject obj;
        
        if (cellPrefab != null)
        {
            obj = Instantiate(cellPrefab, gridContainer);
        }
        else
        {
            // Runtime generation (Fallback)
            obj = new GameObject("Cell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(gridContainer, false);
        }

        Image img = obj.GetComponent<Image>();
        if (dayNumber == null)
        {
            // Spacer
            img.color = Color.clear;
        }
        else
        {
            // Default
            img.color = colorLevel0;
            // Tag for debug if needed
            obj.name = $"Day_{dayNumber}";
        }
        
        return img;
    }

    /// <summary>
    /// Update colors based on data.
    /// data: Dictionary<Day(int), Count(int)>
    /// </summary>
    public void UpdateHeatmap(Dictionary<int, int> activityData)
    {
        // cellList corresponds to day 1 to daysInMonth (index 0 = day 1)
        for (int i = 0; i < cellList.Count; i++)
        {
            int day = i + 1;
            int count = 0;
            if (activityData.ContainsKey(day))
            {
                count = activityData[day];
            }

            cellList[i].color = GetColorForLevel(count);
        }
    }
    
    // Also support full cache update
    public void UpdateHeatmap(Dictionary<string, int> fullCache)
    {
        var localData = new Dictionary<int, int>();
        string prefix = $"{currentYear:D4}-{currentMonth:D2}";

        foreach (var kvp in fullCache)
        {
            if (kvp.Key.StartsWith(prefix))
            {
                if (DateTime.TryParse(kvp.Key, out DateTime date))
                {
                    localData[date.Day] = kvp.Value;
                }
            }
        }
        UpdateHeatmap(localData);
    }

    private Color GetColorForLevel(int count)
    {
        if (count <= 0) return colorLevel0;
        if (count == 1) return colorLevel1;
        if (count <= 3) return colorLevel2;
        return colorLevel3;
    }
}
