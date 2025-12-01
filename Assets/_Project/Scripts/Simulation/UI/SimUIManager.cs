using UnityEngine;
using TMPro; // TextMeshPro kullanıyorsan
using UnityEngine.UI; // Normal Text kullanıyorsan (Aşağıda ikisini de destekleyen yapı var)
using RTS.Simulation.Orchestrator;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // Context

public class SimUIManager : MonoBehaviour
{
    [Header("Bağlantılar")]
    public ExperimentManager Manager; // RL Modu için (Opsiyonel)

    [Header("Kaynak Göstergesi")]
    // Eğer TextMeshPro kullanıyorsan bunu kullan:
    public TextMeshProUGUI ResourcesTextTMP;


    [Header("İstatistikler (Sadece RL)")]
    public TextMeshProUGUI EpisodeText;
    public TextMeshProUGUI WinRateText;
    public TextMeshProUGUI RewardText;

    [Header("Kontroller")]
    public Toggle FastModeToggle;
    public Slider SpeedSlider;
    public TextMeshProUGUI SpeedValueText;

    void Start()
    {
        // Manager yoksa (Skirmish Modu), RL ile ilgili butonları kapat
        if (Manager == null)
        {
            if (FastModeToggle) FastModeToggle.gameObject.SetActive(false);
            // SpeedSlider kalabilir, oyunu hızlandırmak isteyebilirsin
        }
        else
        {
            // RL Modu ayarları
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
        // 1. DÜNYA VAR MI KONTROL ET
        if (SimGameContext.ActiveWorld == null) return;

        // 2. KAYNAKLARI GÜNCELLE (Her modda çalışır)
        UpdateResources();

        // 3. İSTATİSTİKLERİ GÜNCELLE (Sadece Manager varsa)
        if (Manager != null) UpdateStats();
    }

    void UpdateResources()
    {
        // Player 1'in verisini çek
        var player = SimResourceSystem.GetPlayer(SimGameContext.ActiveWorld, 1);

        string displayText = "";
        if (player != null)
        {
            displayText = $"WOOD: {player.Wood}\n" +
                          $"STONE: {player.Stone}\n" +
                          $"MEAT: {player.Meat}\n" +
                          $"POP: {player.CurrentPopulation}/{player.MaxPopulation}";
        }
        else
        {
            displayText = "WOOD: 0\nSTONE: 0\nMEAT: 0";
        }

        // Hangi text kutusu doluysa ona yaz
        if (ResourcesTextTMP != null) ResourcesTextTMP.text = displayText;

    }

    void UpdateStats()
    {
        if (EpisodeText) EpisodeText.text = $"Episode: {Manager.CurrentEpisode}";
        if (RewardText) RewardText.text = $"Reward: {Manager.LastEpisodeReward:F1}";
        if (WinRateText) WinRateText.text = $"Win Rate: {Manager.WinRate * 100f:F1}%";
    }

    public void OnFastModeChanged(bool isFast)
    {
        if (Manager != null) Manager.RunFast = isFast;
    }

    public void OnSpeedSliderChanged(float val)
    {
        if (Manager != null) Manager.VisualSimulationSpeed = val;
        if (SpeedValueText) SpeedValueText.text = $"{val:F1}x";
    }
}