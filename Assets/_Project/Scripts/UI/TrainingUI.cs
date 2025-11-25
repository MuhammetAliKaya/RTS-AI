using UnityEngine;
using TMPro;
using UnityEngine.UI;

/*
 * TrainingUI.cs
 * * Displays real-time training statistics (Episodes, Epsilon, Success Rate) on screen.
 * Toggle visibility with the 'Tab' key (or a button).
 */
public class TrainingUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The main panel containing all training stats info.")]
    public GameObject statsPanel;

    [Header("Text Elements")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI episodeText;
    public TextMeshProUGUI epsilonText;
    public TextMeshProUGUI successRateText;
    public TextMeshProUGUI lastRewardText;
    public TextMeshProUGUI timeScaleText;

    [Header("Interaction")]
    public KeyCode toggleKey = KeyCode.Tab;

    private TrainingManager manager;

    void Start()
    {
        manager = TrainingManager.Instance;
        
        if (manager == null)
        {
            // Fallback search
            manager = FindFirstObjectByType<TrainingManager>();
        }

        if (manager == null)
        {
            Debug.LogError("[TrainingUI] TrainingManager not found in the scene!");
            this.enabled = false;
            return;
        }

        // Ensure panel starts visible or hidden as preferred
        if (statsPanel != null) statsPanel.SetActive(true);
    }

    void Update()
    {
        // Toggle Panel Logic
        if (Input.GetKeyDown(toggleKey))
        {
            if (statsPanel != null)
            {
                statsPanel.SetActive(!statsPanel.activeSelf);
            }
        }

        // Only update text if panel is visible to save performance
        if (statsPanel != null && statsPanel.activeSelf)
        {
            UpdateStats();
        }
    }

    private void UpdateStats()
    {
        if (manager == null) return;

        // 1. Status (Training / Idle)
        if (statusText != null)
        {
            statusText.text = manager.IsTraining ? "STATUS: <color=green>TRAINING</color>" : "STATUS: <color=red>IDLE</color>";
        }

        // 2. Episode Count
        if (episodeText != null)
        {
            episodeText.text = $"Episode: {manager.CurrentEpisode} / {manager.totalEpisodes}";
        }

        // 3. Epsilon (Exploration Rate)
        if (epsilonText != null && manager.agent != null)
        {
            epsilonText.text = $"Epsilon (Exploration): {manager.agent.epsilon:F4}";
        }

        // 4. Recent Success Rate
        if (successRateText != null)
        {
            float rate = manager.GetRecentSuccessRate();
            successRateText.text = $"Success Rate (Last {manager.successRateWindow}): {rate:P1}"; // P1 formats as percentage (e.g. 85.2%)
        }

        // 5. Last Episode Reward
        if (lastRewardText != null)
        {
            if (manager.AllMetrics.Count > 0)
            {
                var lastMetric = manager.AllMetrics[manager.AllMetrics.Count - 1];
                string color = lastMetric.success ? "green" : "red";
                lastRewardText.text = $"Last Reward: <color={color}>{lastMetric.totalReward:F1}</color>";
            }
            else
            {
                lastRewardText.text = "Last Reward: N/A";
            }
        }

        // 6. Time Scale Info
        if (timeScaleText != null)
        {
            timeScaleText.text = $"Speed: {Time.timeScale:F1}x";
        }
    }

    // --- Public Methods for Button OnClick Events ---

    public void TogglePanel()
    {
        if (statsPanel != null) statsPanel.SetActive(!statsPanel.activeSelf);
    }

    public void IncreaseSpeed()
    {
        if (manager != null)
        {
            manager.timeScaleDuringTraining = Mathf.Min(100f, manager.timeScaleDuringTraining + 10f);
        }
    }

    public void DecreaseSpeed()
    {
        if (manager != null)
        {
            manager.timeScaleDuringTraining = Mathf.Max(1f, manager.timeScaleDuringTraining - 10f);
        }
    }
}