using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RTS.Simulation.Orchestrator;
using RTS.Simulation.Systems;

public class SimUIManager : MonoBehaviour
{
    [Header("Bağlantı")]
    public ExperimentManager Manager;

    [Header("Sol Panel (Kaynaklar)")]
    public TextMeshProUGUI ResourcesText; // Wood, Stone, Meat tek satırda

    [Header("Sağ Panel (Eğitim İstatistikleri)")]
    public TextMeshProUGUI EpisodeText;
    public TextMeshProUGUI WinRateText;
    public TextMeshProUGUI RewardText;
    public TextMeshProUGUI EpsilonText;

    [Header("Alt Panel (Kontroller)")]
    public Toggle FastModeToggle;
    public Slider SpeedSlider;
    public TextMeshProUGUI SpeedValueText;

    void Start()
    {
        // UI elemanlarını başlangıç ayarlarına çek
        if (Manager != null)
        {
            if (FastModeToggle)
            {
                FastModeToggle.isOn = Manager.RunFast;
                FastModeToggle.onValueChanged.AddListener(OnFastModeChanged);
            }

            if (SpeedSlider)
            {
                SpeedSlider.value = Manager.VisualSimulationSpeed;
                SpeedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
            }
        }
    }

    void Update()
    {
        if (Manager == null || Manager.World == null) return;

        UpdateResources();
        UpdateStats();
    }

    void UpdateResources()
    {
        var player = SimResourceSystem.GetPlayer(Manager.World, 1);
        if (player != null && ResourcesText != null)
        {
            ResourcesText.text = $"🌲 {player.Wood}  🪨 {player.Stone}  🍖 {player.Meat}  Pop: {player.CurrentPopulation}/{player.MaxPopulation}";
        }
    }

    void UpdateStats()
    {
        if (EpisodeText) EpisodeText.text = $"Episode: {Manager.CurrentEpisode}";

        // Kazanma Oranı (Renk kodu: Yeşil iyi, Kırmızı kötü)
        if (WinRateText)
        {
            float rate = Manager.WinRate * 100f;
            string color = rate > 50 ? "green" : "red";
            WinRateText.text = $"Win Rate: <color={color}>{rate:F1}%</color>";
        }

        if (RewardText) RewardText.text = $"Last Reward: {Manager.LastEpisodeReward:F1}";

        // Ajanın içinden Epsilon verisini çekiyoruz (String parse ederek veya Agent Interface'den)
        if (EpsilonText && Manager.Agent != null)
        {
            EpsilonText.text = Manager.Agent.GetStats();
        }
    }

    // --- EVENTS ---

    public void OnFastModeChanged(bool isFast)
    {
        if (Manager != null) Manager.RunFast = isFast;

        // Fast moddaysa Slider'ı kilitle ki kafa karışmasın
        if (SpeedSlider) SpeedSlider.interactable = !isFast;
    }

    public void OnSpeedSliderChanged(float val)
    {
        if (Manager != null) Manager.VisualSimulationSpeed = val;
        if (SpeedValueText) SpeedValueText.text = $"{val:F1}x";
    }
}