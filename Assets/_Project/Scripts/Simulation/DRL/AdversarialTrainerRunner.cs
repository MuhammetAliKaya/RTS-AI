using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;
using System.Globalization;
using System.Threading; // Buraya eklendi
using System.IO; // En üste ekleyin
using UnityEngine.SceneManagement; // EKLENDİ: Sahne değişimi için


public enum AIDifficulty
{
    Passive,
    Defensive,
    Aggressive
}

public enum AIOpponentType
{
    Balanced,       // SimpleMacroAI
    Rusher,         // RusherAI
    Turtle,         // TurtleAI
    EcoBoom,        // EcoBoomAI (YENİ)
    WorkerRush,     // WorkerRushAI (YENİ)
    Harasser,       // HarasserAI (YENİ)
    EliteCommander, // King of Bots (Eğitimde seçme!)
    Random          // Rastgele birini seç
}

public enum PlayerControllerType
{
    Scripted, // Kural Tabanlı Bot
    AI,       // Derin Öğrenme (RL Agent)
    Human     // İnsan Oyuncu
}
public class AdversarialTrainerRunner : MonoBehaviour
{

    [Header("⚔️ EŞLEŞME AYARLARI")]
    public PlayerControllerType Player1Controller = PlayerControllerType.AI;
    public PlayerControllerType Player2Controller = PlayerControllerType.Scripted;

    [Header("🤖 PLAYER 1 AYARLARI (AI / Scripted)")]
    public RTSOrchestrator AgentP1; // Eğer AI ise burası
    public AIOpponentType ScriptedBotP1 = AIOpponentType.Balanced; // Eğer Scripted ise

    [Header("🤖 PLAYER 2 AYARLARI (AI / Scripted)")]
    public RTSOrchestrator AgentP2;
    public AIOpponentType ScriptedBotP2 = AIOpponentType.Balanced;
    public AIDifficulty EnemyDifficultyP2 = AIDifficulty.Passive;

    // --- İÇ DEĞİŞKENLER ---
    private IMacroAI _p1ScriptedBot; // P1 Scripted mantığı
    private IMacroAI _p2ScriptedBot; // P2 Scripted mantığı (Eski _enemyAI)


    [Header("Ayarlar")]
    public RTSOrchestrator Orchestrator;

    public int MapSize = 20;
    public int MaxSteps = 5000;

    public string AllowedAgentName = "AdvTrainerRunner";

    [Header("Inference Analizi")]
    public bool RecordInferenceToCSV = true;
    private string _inferenceFilePath;
    private List<string> _inferenceBuffer = new List<string>();

    [Header("💀 Ölümcül Oyun Kuralları (Sudden Death)")]
    [Tooltip("Kaç adım boyunca askeri olmazsa yenik sayılsın?")]
    public int NoSoldierDefeatSteps = 100;

    [Tooltip("Bu kural oyunun kaçıncı adımından sonra devreye girsin? (Erken oyunda kaybetmemek için)")]
    public int NoSoldierRuleStartStep = 800; // Örn: İlk 800 adım muafiyet

    [Tooltip("Harita rastgeleliğini kontrol etmek için kullanılır.")]
    public int mapSeed = 12345;
    public bool useRandomSeed = true;

    [Header("Zaman Ayarları")]
    public bool IsTrainingMode = false;

    public float _simStepSize = 0.0025f;
    [Range(1f, 10000f)]
    public float _simStepCountPerFrame = 1f;

    private float _timer = 0f;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Rakip Ayarları")]
    public bool UseMacroAI = true;

    [Header("PARALEL EĞİTİM AYARI")]
    public AIOpponentType SelectedBotType = AIOpponentType.Balanced;
    public AIDifficulty EnemyDifficulty = AIDifficulty.Passive;

    // --- GUI AYARLARI ---
    [Header("Gelişmiş GUI Ayarları")]
    public bool ShowGUI = true;
    public KeyCode ToggleKey = KeyCode.G;
    [Range(1f, 3f)] public float GUIScale = 1.3f; // Arayüz büyüklüğü

    // İSTATİSTİKLER
    private int _statsTotalEpisodes = 0;
    private int _statsWins = 0;
    private int _statsLosses = 0;
    private float _statsCurrentReward = 0f;
    private float _statsLastEpisodeReward = 0f;

    // Detaylı Sayaçlar
    private int _cumulativeKills = 0; // Öldürülen düşman askeri
    private int _cumulativeRazes = 0; // Yıkılan düşman binası
    private int _myBuildingCount = 0; // Kendi bina sayım

    private int _lastFarmCount = 0;
    private int _lastWoodCutterCount = 0;
    private int _lastStonePitCount = 0;
    private bool _barracksRewardGiven = false; // Sadece ilk kışla için

    // Grafik Verisi
    private List<float> _rewardGraphHistory = new List<float>();
    private const int GRAPH_HISTORY_SIZE = 60; // Grafikte kaç adım gösterilecek
    private Texture2D _graphTexture; // Çizim için beyaz piksel

    // SİSTEMLER
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    // TAKİP DEĞİŞKENLERİ
    private int _lastSoldiers = 0;
    private float _lastMyBaseHealth = 1000f;

    private int _lastEnemyUnitCount = 0;
    private int _lastEnemyBuildingCount = 0;
    private float _lastEnemyBaseHealth = 1000f;
    private int _lastWood = 0;
    private int _lastMeat = 0;
    private int _lastStone = 0;
    private int _lastWorkerCount = 0;

    private IMacroAI _enemyAI;
    private int _currentStep = 0;
    private bool _gameEnded = false;

    private int _agentDecisionCounter = 0;
    private int _enemyDecisionCounter = 0;
    private int _lastBarracksCount = 0;
    private const int AGENT_DECISION_INTERVAL = 4;
    private const int ENEMY_DECISION_INTERVAL = 16;

    private float _agentDecisionTimer = 0f;
    public float AgentDecisionTimeStep = 5f;

    private MatchAnalytics _currentStats;

    private bool _needsFullReset = false;

    private bool _farmRewardGiven = false; // Sadece ilk farm için

    private Dictionary<int, HashSet<int>> _frameAttackLog = new Dictionary<int, HashSet<int>>();
    private int _lastTowerCount = 0;

    private bool _fullEcoMilestoneGiven = false;
    private int dcCountAI = 0;

    private float _agentP2DecisionTimer = 0f; // P2 için karar zamanlayıcısı



    private int _lastCurrentPop = 0;
    private int _lastMaxPop = 0;

    [Header("Sahne Akışı")]
    public string MenuSceneName = "menu_0"; // Menü sahnesinin adı

    private int _p1NoSoldierCounter = 0;
    private int _p2NoSoldierCounter = 0;

    private float _cumulativeCriticalGatherReward = 0f; // Sömürü kontrolü için sayaç
    private const float MAX_CRITICAL_GATHER_REWARD = 3.0f; // Maç başına max 3.0 puan


    void Awake()
    {
        // Uygulamanın tüm çalışma sürecinde ondalık ayırıcıyı NOKTA yapar.
        // Böylece 279,00 yerine 279.00 çıktısı alırsın ve CSV sütunları kaymaz.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        // Bazı sistemlerde thread bazlı ayar da gerekebilir
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        Debug.Log("Global Kültür Ayarı: InvariantCulture (Nokta Ayırıcı) aktif.");

        if (GameSessionSettings.IsLoadedFromMenu)
        {
            Debug.Log("📥 Menü Ayarları Yükleniyor...");

            this.Player1Controller = GameSessionSettings.P1Controller;
            this.ScriptedBotP1 = GameSessionSettings.P1BotType;

            this.Player2Controller = GameSessionSettings.P2Controller;
            this.ScriptedBotP2 = GameSessionSettings.P2BotType;
            this.EnemyDifficultyP2 = GameSessionSettings.P2Difficulty;

            // Eğer P1 Human ise AgentP1'i null yapabiliriz veya olduğu gibi bırakabiliriz,
            // sistem zaten Player1Controller enum'una bakıyor.
        }
    }

    void Start()
    {
        // Grafik çizimi için basit 1x1 beyaz texture oluştur
        _graphTexture = new Texture2D(1, 1);
        _graphTexture.SetPixel(0, 0, Color.white);
        _graphTexture.Apply();

        if (Orchestrator == null) Orchestrator = GetComponentInChildren<RTSOrchestrator>();

        if (Orchestrator != null)
        {
            Orchestrator.Setup(_world, _gridSys, _unitSys, _buildSys, this);
        }

        if (RecordInferenceToCSV)
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _inferenceFilePath = Path.Combine(Application.dataPath, $"InferenceTimes_{timestamp}.csv");
            File.WriteAllText(_inferenceFilePath, "Step,ElapsedMs,BotType,Difficulty\n");
        }

        ResetSimulation();
    }

    void Update()
    {
        // Toggle GUI
        if (Input.GetKeyDown(ToggleKey))
        {
            ShowGUI = !ShowGUI;
        }

        if (_gameEnded) return;

        if (IsTrainingMode && !Orchestrator.IsWaitingForDecision)
        {
            for (int i = 0; i < _simStepCountPerFrame; i++) SimulationStep(_simStepSize);
        }
        else
        {
            SimulationStep(_simStepSize);
        }
    }

    // --- GELİŞMİŞ GUI ÇİZİMİ ---
    void OnGUI()
    {
        if (!ShowGUI) return;

        // 1. Ölçeklendirmeyi Ayarla
        Matrix4x4 originalMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * GUIScale);

        float width = 320f;  // Genişlik biraz artırıldı
        float height = 550f; // YÜKSEKLİK CİDDİ ORANDA ARTIRILDI (320 -> 550)
        float padding = 10f;

        Rect boxRect = new Rect(padding, padding, width, height);

        // Arka plan kutusu (Yarı saydam siyah)
        GUI.backgroundColor = new Color(0, 0, 0, 0.85f);
        GUI.Box(boxRect, GUIContent.none);
        GUI.backgroundColor = Color.white;

        GUILayout.BeginArea(new Rect(padding + 10, padding + 10, width - 20, height - 20));

        // BAŞLIK
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.alignment = TextAnchor.MiddleCenter;
        headerStyle.fontSize = 14;
        headerStyle.normal.textColor = Color.yellow;
        GUILayout.Label($"TRAINING DASHBOARD ({_currentStep}/{MaxSteps})", headerStyle);

        GUIStyle textStyle = new GUIStyle(GUI.skin.label);
        textStyle.fontSize = 13; // Yazı boyutu biraz büyütüldü
        textStyle.normal.textColor = Color.white;
        textStyle.richText = true;

        GUILayout.Space(10);

        // EPISODE & WIN RATE
        float winRate = _statsTotalEpisodes > 0 ? ((float)_statsWins / _statsTotalEpisodes) * 100f : 0f;
        string wrColor = winRate > 50 ? "green" : (winRate > 25 ? "yellow" : "red");
        GUILayout.Label($"<b>Episode:</b> {_statsTotalEpisodes} | <b>WinRate:</b> <color={wrColor}>%{winRate:F1}</color>", textStyle);
        GUILayout.Label($"Score: {_statsWins}W - {_statsLosses}L", textStyle);

        GUILayout.Space(5);
        GUILayout.Box("", GUILayout.Height(2)); // Ayırıcı Çizgi
        GUILayout.Space(5);

        // KAYNAKLAR (DETAYLI)
        GUILayout.Label("<b>KAYNAKLAR (Resources)</b>", textStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"🌲 {_lastWood}", textStyle);
        GUILayout.Label($"🏔️ {_lastStone}", textStyle);
        GUILayout.Label($"🍖 {_lastMeat}", textStyle);
        GUILayout.EndHorizontal();

        // --- EKLENEN KISIM: NÜFUS GÖSTERGESİ ---
        GUILayout.Label($"📈 Nüfus: <b>{_lastCurrentPop}/{_lastMaxPop}</b>", textStyle);
        // ---------------------------------------

        GUILayout.Space(5);
        GUILayout.Box("", GUILayout.Height(2));
        GUILayout.Space(5);

        // DETAYLI İSTATİSTİKLER
        GUILayout.Label("<b>SAVAŞ & GELİŞİM</b>", textStyle);
        GUILayout.Label($"🏠 Binalarım: <b>{_myBuildingCount}</b>", textStyle);
        GUILayout.Label($"⚔️ Öldürülen Düşman: <color=red><b>{_cumulativeKills}</b></color>", textStyle);
        GUILayout.Label($"🔥 Yıkılan Bina: <color=orange><b>{_cumulativeRazes}</b></color>", textStyle);

        GUILayout.Space(5);
        GUILayout.Box("", GUILayout.Height(2));
        GUILayout.Space(5);

        // ÖDÜL BİLGİSİ
        GUILayout.Label($"Current Reward: <color=cyan>{_statsCurrentReward:F2}</color>", textStyle);
        GUILayout.Label($"Last Ep Reward: {_statsLastEpisodeReward:F2}", textStyle);

        // GRAFİK ALANI
        GUILayout.Space(15);
        GUILayout.Label("<b>Reward Değişimi (Son 60 Adım)</b>", textStyle);
        DrawRewardGraph(width - 20, 80f); // Grafik yüksekliği artırıldı

        GUILayout.EndArea();

        // Matrix'i eski haline getir (Diğer Unity GUI'lerini bozmamak için)
        GUI.matrix = originalMatrix;
    }

    private void DrawRewardGraph(float w, float h)
    {
        // Çerçeve
        Rect graphRect = GUILayoutUtility.GetRect(w, h);
        GUI.DrawTexture(graphRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.2f, 0.2f, 0.2f, 0.5f), 0, 0);

        if (_rewardGraphHistory.Count < 2) return;

        float maxVal = _rewardGraphHistory.Max();
        float minVal = _rewardGraphHistory.Min();
        float range = Mathf.Max(Mathf.Abs(maxVal - minVal), 1f); // Sıfıra bölünmeyi önle

        // Barlar çiz
        float barWidth = w / (float)GRAPH_HISTORY_SIZE;

        for (int i = 0; i < _rewardGraphHistory.Count; i++)
        {
            float val = _rewardGraphHistory[i];

            // Normalize et (Grafiğin içine sığdır)
            // Min ve Max değerlere göre normalize edelim ki grafik hep dolu görünsün
            float normalizedH = (val - minVal) / range;

            // Min yükseklik garantisi (görünürlük için)
            float barH = Mathf.Max(normalizedH * h, 2f);

            float x = graphRect.x + (i * barWidth);
            float y = graphRect.y + h - barH; // Aşağıdan yukarı

            Color barColor = val >= 0 ? Color.green : new Color(1f, 0.3f, 0.3f); // Negatifler kırmızı

            GUI.color = barColor;
            GUI.DrawTexture(new Rect(x, y, barWidth - 1, barH), _graphTexture);
            GUI.color = Color.white;
        }
    }

    // Yardımcı: Ödül eklerken GUI için de kaydet
    private void TrackReward(float amount)
    {
        _statsCurrentReward += amount;
    }

    // Her simülasyon adımında grafik verisini güncelle
    private void UpdateGraphHistory()
    {
        _rewardGraphHistory.Add(_statsCurrentReward);
        if (_rewardGraphHistory.Count > GRAPH_HISTORY_SIZE)
        {
            _rewardGraphHistory.RemoveAt(0);
        }
    }

    public void SimulationStep(float dt)
    {
        if (_gameEnded) return;

        // --- 1. SCRIPTED BOTLARI ÇALIŞTIR ---
        if (Player1Controller == PlayerControllerType.Scripted && _p1ScriptedBot != null) _p1ScriptedBot.Update(dt);
        if (Player2Controller == PlayerControllerType.Scripted && _p2ScriptedBot != null) _p2ScriptedBot.Update(dt);

        // --- 2. AI KARARLARI (P1) ---
        // Orchestrator kullanan ana ajan
        if (Player1Controller == PlayerControllerType.AI && AgentP1 != null)
        {
            _agentDecisionTimer += dt;

            // Senin mantığına göre dinamik süreyi al
            float dynamicInterval = GetDynamicDecisionInterval();

            if (_agentDecisionTimer >= dynamicInterval)
            {
                _agentDecisionTimer = 0f;

                // Eğer Orchestrator boşsa kararı yapıştır
                if (AgentP1.CurrentState == RTSOrchestrator.OrchestratorState.Idle)
                {
                    AgentP1.RequestFullDecision();

                    // Debug satırı (İstersen açıp hızın nasıl değiştiğini izleyebilirsin)
                    // Debug.Log($"Karar Verildi. Varlık: {1.0f/dynamicInterval} adet -> Hız: {dynamicInterval} sn");
                }
            }
        }

        // --- 3. AI KARARLARI (P2) ---
        // İkinci bir ajan varsa (Örn: Self-Play veya AI vs AI)
        if (Player2Controller == PlayerControllerType.AI && AgentP2 != null)
        {
            _agentP2DecisionTimer += dt;
            if (_agentP2DecisionTimer >= AgentDecisionTimeStep)
            {
                _agentP2DecisionTimer = 0f;
                // Eğer P2 Orchestrator boşta ise karar iste
                if (AgentP2.CurrentState == RTSOrchestrator.OrchestratorState.Idle)
                {
                    AgentP2.RequestFullDecision();
                }
            }
        }

        // --- MEVCUT SİMÜLASYON KODLARI (AYNEN KALSIN) ---
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);
        if (_unitSys != null) _unitSys.UpdateAllUnits(dt);

        // 4. İstatistikleri ve Ödülleri Güncelle
        UpdateStatisticsVariables(); // YENİ: İstatistikleri topla
        CheckSurvivalMilestones();
        CalculateCombatRewards();
        CalculateEconomyRewards();
        ApplyIdlePenalty();
        CheckGameResult();
        if (_currentStep > 0 && _currentStep % 100 == 0)
        {
            // CheckWorkerSurvivalBonus();
        }

        // 5. Grafik verisini güncelle (Her 10 adımda bir güncelle ki grafik çok hızlı akmasın)
        if (_currentStep % 10 == 0) UpdateGraphHistory();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            EndGame(-35);
        }
    }
    private float GetDynamicDecisionInterval()
    {
        if (_world == null) return 3.0f; // Güvenlik önlemi

        // 1. Aktif Birimleri Say (İşçi + Asker)
        // Askerler de emir alıp hareket ettiği için onları da saymalıyız.
        int activeUnitCount = _world.Units.Values.Count(u =>
            u.PlayerID == 1 &&
            (u.UnitType == SimUnitType.Worker || u.UnitType == SimUnitType.Soldier) && // Hem Worker hem Soldier
            u.State != SimTaskType.Dead
        );

        // 2. Sadece Üretim Binalarını Say (Base ve Barracks)
        // House, Tower, WoodCutter vb. pasif olduğu için sayılmıyor.
        int productionBuildingCount = _world.Buildings.Values.Count(b =>
            b.PlayerID == 1 &&
            b.IsConstructed &&
            (b.Type == SimBuildingType.Base || b.Type == SimBuildingType.Barracks)
        );

        // 3. Toplam Aksiyon Alabilir Varlık Sayısı
        // Artık ordu büyüdükçe AI daha hızlı düşünmeye çalışacak.
        int totalActionableEntities = activeUnitCount + productionBuildingCount;

        // 4. Frekansı Belirle (Saniyedeki Karar Sayısı)
        // En az 1 varlık varmış gibi davran (0'a bölünmeyi önlemek için)
        // Üst limit (Cap) olarak 40 yapalım. Ordu savaşlarında (Micro) hız lazım olabilir.
        // Bilgisayarın çok güçlüyse 40'ı 50-60 yapabilirsin ama 40 genelde yeterlidir.
        int targetDecisionsPerSecond = Mathf.Clamp(totalActionableEntities, 1, 40);

        // 5. Aralığı Döndür (Örn: 20 birim -> 0.05sn, 1 birim -> 1.0sn)
        return 3.0f / (float)(targetDecisionsPerSecond * 3);
    }

    private void UpdateStatisticsVariables()
    {
        if (_world == null) return;

        // Kendi bina sayımı güncelle
        _myBuildingCount = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.IsConstructed);

        // Kaynakları güncelle
        if (_world.Players.ContainsKey(1))
        {
            var p = _world.Players[1];
            _lastWood = p.Wood;
            _lastStone = p.Stone;
            _lastMeat = p.Meat;
            _lastCurrentPop = p.CurrentPopulation;
            _lastMaxPop = p.MaxPopulation;
        }
    }

    private void CalculateEconomyRewards()
    {
        if (Orchestrator == null || _world == null || !_world.Players.ContainsKey(1)) return;

        var player = _world.Players[1];

        // --- 1. KAYNAK TOPLAMA BONUSU (LİMİTLİ versiyon) ---
        int deltaWood = player.Wood - _lastWood;
        int deltaStone = player.Stone - _lastStone;
        int deltaMeat = player.Meat - _lastMeat;

        float criticalBonus = 0f;

        // Odun Kritiği
        if (player.Wood < 250 && deltaWood > 0)
        {
            criticalBonus += deltaWood * 0.004f; // Normalin yaklaşık 40 katı (0.00005 vs 0.002)
        }
        // Taş Kritiği
        if (player.Stone < 250 && deltaStone > 0)
        {
            criticalBonus += deltaStone * 0.004f;
        }
        // Et Kritiği
        if (player.Meat < 250 && deltaMeat > 0)
        {
            criticalBonus += deltaMeat * 0.006f; // Et daha değerli
        }

        // Sınır Kontrolü
        if (criticalBonus > 0 && _cumulativeCriticalGatherReward < MAX_CRITICAL_GATHER_REWARD)
        {
            // ActionRewardOnly kullanıyoruz çünkü sadece o işçiyi ilgilendiriyor
            Orchestrator.AddActionRewardOnly(criticalBonus);

            _cumulativeCriticalGatherReward += criticalBonus;
            // Debug.Log($"Critical Resource Gathered! Bonus: {criticalBonus:F3}");
        }

        // --- NORMAL KAYNAK ÖDÜLLERİ (Çok düşük tutmaya devam) ---
        if (deltaWood > 0 && _currentStats.TotalWoodGathered <= 10000)
            Orchestrator.AddGroupReward(deltaWood * 0.00005f);

        if (deltaStone > 0 && _currentStats.TotalStoneGathered <= 10000)
            Orchestrator.AddGroupReward(deltaStone * 0.00005f);

        if (deltaMeat > 0 && _currentStats.TotalMeatGathered <= 100000)
            Orchestrator.AddGroupReward(deltaMeat * 0.0001f);

        // Değerleri güncelle
        _lastWood = player.Wood;
        _lastStone = player.Stone;
        _lastMeat = player.Meat;

        // --- 2. KIŞLA (BARRACKS) ÜRETİM BONUSU (YENİ) ---
        // Sadece "Inşaatı Bitmiş" kışlaları sayıyoruz.
        int currentBarracks = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Barracks && b.IsConstructed);

        if (currentBarracks > _lastBarracksCount)
        {
            // Kaç tane yeni bitti? (Genelde 1 olur ama aynı anda biterse diye döngüye alıyoruz)
            int newFinishedCount = currentBarracks - _lastBarracksCount;

            for (int i = 0; i < newFinishedCount; i++)
            {
                if (!_barracksRewardGiven)
                {
                    // --- İLK KIŞLA: BÜYÜK ÖDÜL (3.0) ---
                    _barracksRewardGiven = true;
                    Orchestrator.AddGroupReward(0.5f);
                    Debug.Log(">>> FIRST BARRACKS REWARD GIVEN! (+3.0) <<<");
                }
                else if (currentBarracks <= 3) // LİMİT EKLENDİ (Maks 6 Kışla)
                {
                    Orchestrator.AddGroupReward(0.1f);
                }
                else if (currentBarracks <= 5) // LİMİT EKLENDİ (Maks 6 Kışla)
                {
                    Orchestrator.AddGroupReward(0.05f);
                }
                else if (currentBarracks <= 10) // LİMİT EKLENDİ (Maks 6 Kışla)
                {
                    Orchestrator.AddGroupReward(0.01f);
                }
            }
        }
        _lastBarracksCount = currentBarracks;

        // --- 3. İŞÇİ ÜRETİMİ (ESKİSİ) ---
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        if (currentWorkers > _lastWorkerCount)
        {
            float rewardAmount = 0f;

            // İlk 15 işçi ekonomiyi kurmak için değerlidir
            if (currentWorkers <= 10) rewardAmount = 0.1f;
            // Sonrası sadece nüfus kalabalığıdır, ödülü düşür.
            else if (currentWorkers <= 30) rewardAmount = 0.01f;

            if (rewardAmount > 0)
            {
                Orchestrator.AddActionRewardOnly(rewardAmount);
                TrackReward(rewardAmount);
            }
        }

        // --- KULE (SAVUNMA) BONUSU ---
        // Mevcut (bitmiş) kule sayısını bul
        int currentTowers = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Tower && b.IsConstructed);

        // Eğer kule sayısı artmışsa VE abartmamışsa (Max 5 kule)
        if (currentTowers > _lastTowerCount && currentTowers <= 5)
        {
            if (Orchestrator != null)
            {
                Orchestrator.AddGroupReward(0.1f); // Kule stratejik yatırımdır
                Debug.Log($"[Defense] Strategic Tower Built! ({currentTowers}/5)");
            }
        }
        // Sayacı güncelle

        // --- 2. EKONOMİ BİNALARI İNŞASI ---
        int currentFarms = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Farm && b.IsConstructed);
        int currentCutters = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.WoodCutter && b.IsConstructed);
        int currentPits = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.StonePit && b.IsConstructed);

        // Her yeni ekonomi binası için ödül (abartmadan)
        if (currentFarms > _lastFarmCount)
        {
            int count = currentFarms - _lastFarmCount;
            for (int i = 0; i < count; i++)
            {
                // İlk çiftlik bonusu
                if (!_farmRewardGiven)
                {
                    _farmRewardGiven = true;
                    Debug.Log("FarmReward");
                    Orchestrator.AddGroupReward(2f);
                    Orchestrator.AddActionRewardOnly(1.0f);

                }
                // Sonraki çiftlikler (Sadece ilk 8 tanesi ödül verir)
                else if (currentFarms <= 5)
                {
                    Orchestrator.AddGroupReward(0.05f);
                }
                // 8'den fazlası gereksiz harcamadır, ödül yok.
            }
        }

        // 2. ODUNCU (CUTTER) - Limit: 5 Adet
        if (currentCutters > _lastWoodCutterCount)
        {
            if (currentCutters <= 5) Orchestrator.AddGroupReward(0.05f);
            // Sadece mantıklı sayıda yaparsa ödül ver
            if (currentCutters <= 1) Orchestrator.AddGroupReward(1.95f);
            if (currentCutters <= 1) Orchestrator.AddActionRewardOnly(1.0f);

        }

        // 3. TAŞ OCAĞI (PIT) - Limit: 5 Adet
        if (currentPits > _lastStonePitCount)
        {
            if (currentPits <= 5) Orchestrator.AddGroupReward(0.05f);
            if (currentPits <= 1) Orchestrator.AddGroupReward(1.95f);
            if (currentPits <= 1) Orchestrator.AddActionRewardOnly(1.0f);


        }

        if (!_fullEcoMilestoneGiven && currentFarms > 0 && currentCutters > 0 && currentPits > 0)
        {
            _fullEcoMilestoneGiven = true;
            float milestoneReward = 1.5f; // İlk kez üçüne de sahip olduğu için büyük ödül

            if (Orchestrator != null)
            {
                Orchestrator.AddGroupReward(milestoneReward);
                Orchestrator.AddActionRewardOnly(milestoneReward / 2);

            }
        }

        // Sayaçları güncelle (Bu kısım aynı kalmalı)
        _lastFarmCount = currentFarms;
        _lastWoodCutterCount = currentCutters;
        _lastStonePitCount = currentPits;

        _lastFarmCount = currentFarms;
        _lastWoodCutterCount = currentCutters;
        _lastStonePitCount = currentPits;
        _lastTowerCount = currentTowers;
        _lastWorkerCount = currentWorkers;
    }

    private void CalculateCombatRewards()
    {
        if (Orchestrator == null) return;

        int currentEnemyUnits = 0;
        int currentEnemyBuildings = 0;
        float currentEnemyBaseHealth = 0;
        int currentSoldiers = 0;

        foreach (var u in _world.Units.Values)
            if (u.PlayerID == 2 && u.State != SimTaskType.Dead) currentEnemyUnits++;

        foreach (var u in _world.Units.Values)
            if (u.UnitType == SimUnitType.Soldier && u.PlayerID == 1 && u.State != SimTaskType.Dead) currentSoldiers++;

        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 2)
            {
                currentEnemyBuildings++;
                if (b.Type == SimBuildingType.Base) currentEnemyBaseHealth = b.Health;
            }
        }

        // --- 1. ASKER ÜRETİMİ (Mevcut güvenli hali) ---
        if (currentSoldiers > _lastSoldiers && currentSoldiers <= 10)
        {
            float r = 0.15f;
            Orchestrator.AddActionRewardOnly(r); // Sadece ActionReward!
            TrackReward(r);
        }
        else if (currentSoldiers > _lastSoldiers && currentSoldiers <= 30)
        {
            float r = 0.02f;
            Orchestrator.AddActionRewardOnly(r);
            TrackReward(r);
        }

        // --- 2. DÜŞMAN ÖLDÜRME (KADEMELİ SİSTEM) ---
        if (currentEnemyUnits < _lastEnemyUnitCount)
        {
            int killCount = _lastEnemyUnitCount - currentEnemyUnits;

            for (int i = 0; i < killCount; i++)
            {
                _cumulativeKills++; // Toplam öldürülen sayısını artır

                float killReward = 0f;

                // İlk 20 Düşman: Yüksek Ödül (Savaşı domine et)
                if (_cumulativeKills <= 20)
                {
                    killReward = 0.15f;
                }
                // 20-50 Arası: Orta Ödül (Temizliğe devam et)
                else if (_cumulativeKills <= 50)
                {
                    killReward = 0.05f;
                }
                // 50+ Sonrası: Çok Düşük (Artık oyunu bitir, farm yapma)
                else
                {
                    killReward = 0.005f;
                }

                // Grup ödülü veriyoruz ki takım motive olsun
                Orchestrator.AddGroupReward(killReward);
                TrackReward(killReward);
            }
        }

        // --- 3. BİNA YIKMA (KADEMELİ SİSTEM) ---
        if (currentEnemyBuildings < _lastEnemyBuildingCount)
        {
            int destroyCount = _lastEnemyBuildingCount - currentEnemyBuildings;

            for (int i = 0; i < destroyCount; i++)
            {
                _cumulativeRazes++;
                float razeReward = 0f;

                // İlk 5 bina kritik (Kışla, Base vb.)
                if (_cumulativeRazes <= 5) razeReward = 0.2f;
                // Sonraki binalar (Evler vb.)
                else razeReward = 0.05f;

                Orchestrator.AddGroupReward(razeReward);
                TrackReward(razeReward);
            }
        }

        // 4. Kendi Üssümüz Hasar Alırsa
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        if (myBase != null)
        {
            if (myBase.Health < _lastMyBaseHealth)
            {
                float damageTaken = _lastMyBaseHealth - myBase.Health;
                float penalty = -damageTaken * 0.0005f;
                Orchestrator.AddGroupReward(penalty);
                TrackReward(penalty);
            }
            _lastMyBaseHealth = myBase.Health;
        }

        if (currentEnemyBaseHealth < _lastEnemyBaseHealth)
        {
            float damageDealt = _lastEnemyBaseHealth - currentEnemyBaseHealth;
            // Hasar başına puan (Örn: 100 hasar = 0.1 puan)
            Orchestrator.AddGroupReward(damageDealt * 0.0001f);
        }

        // DEĞERLERİ GÜNCELLEME (Burası fonksiyonun en sonunda olmalı)
        _lastSoldiers = currentSoldiers;
        _lastEnemyUnitCount = currentEnemyUnits;
        _lastEnemyBuildingCount = currentEnemyBuildings;
        _lastEnemyBaseHealth = currentEnemyBaseHealth;
    }

    private void ApplyIdlePenalty()
    {
        if (Orchestrator == null) return;

        int idleCount = _world.Units.Values.Count(u =>
            u.PlayerID == 1 &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        );

        if (idleCount > 0)
        {
            float penalty = idleCount * -0.001f;
            // Orchestrator.AddUnitRewardOnly(penalty);
            // TrackReward(penalty);
        }
    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (_currentStep > NoSoldierRuleStartStep)
        {
            // P1 Aktif Birim Sayısı (Asker VEYA İşçi)
            int p1Units = _world.Units.Values.Count(u =>
                u.PlayerID == 1 &&
                (u.UnitType == SimUnitType.Soldier || u.UnitType == SimUnitType.Worker) && // İkisini de kapsar
                u.State != SimTaskType.Dead
            );

            // P2 Aktif Birim Sayısı (Asker VEYA İşçi)
            int p2Units = _world.Units.Values.Count(u =>
                u.PlayerID == 2 &&
                (u.UnitType == SimUnitType.Soldier || u.UnitType == SimUnitType.Worker) &&
                u.State != SimTaskType.Dead
            );

            // P1 Kontrolü (Hiçbir birimi kalmadıysa sayaç artar)
            if (p1Units == 0) _p1NoSoldierCounter++;
            else _p1NoSoldierCounter = 0; // Herhangi bir birim varsa sayaç sıfırlanır

            // P2 Kontrolü
            if (p2Units == 0) _p2NoSoldierCounter++;
            else _p2NoSoldierCounter = 0;

            // CEZA UYGULAMA (P1 İçin)
            if (_p1NoSoldierCounter >= NoSoldierDefeatSteps)
            {
                Debug.Log($"<color=red>P1 DEFEAT: No Units (Worker/Soldier) for {NoSoldierDefeatSteps} steps!</color>");
                EndGame(-35.0f); // P1 Kaybetti
                return;
            }

            // P2 Kontrolü (Senin yorum satırına aldığın kısım, istersen açabilirsin)
            /*
            if (_p2NoSoldierCounter >= NoSoldierDefeatSteps)
            {
               Debug.Log($"<color=green>P2 DEFEAT: No Units for {NoSoldierDefeatSteps} steps! (P1 WIN)</color>");
               EndGame(50.0f); // P1 Kazandı
               return;
            }
            */
        }

        if (myBase == null) // Kaybettik
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 10.0f;
            EndGame(-35.0f);
            Debug.Log("Game Lost");
        }
        else if (enemyBase == null) // Kazandık
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 10.0f;
            Debug.Log("Game Won");
            EndGame(35.0f);
        }
    }

    private void EndGame(float reward)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        // ANALİTİK KAYDETME:
        // 1. Analitik Verilerini TensorBoard'a Gönder
        if (_currentStats != null)
        {
            _currentStats.IsWin = reward > 0;
            _currentStats.EpisodeID = _statsTotalEpisodes; // Kaçıncı maç olduğu

            _currentStats.MatchDuration = _currentStep * _simStepSize;
            Academy.Instance.StatsRecorder.Add($"Match/Duration/{_currentStats.Opponent}", _currentStats.MatchDuration);

            // --- TENSORBOARD: BOT BAZLI GRUPLAMA ---
            // Bu sayede "Economy/Rusher/Wood" gibi ayrı grafikler görürsün
            string botName = _currentStats.Opponent.ToString();
            var tb = Academy.Instance.StatsRecorder;

            tb.Add($"WinRate/{botName}", _currentStats.IsWin ? 1f : 0f);
            tb.Add($"Economy/{botName}/TotalWood", _currentStats.TotalWoodGathered);
            tb.Add($"Economy/{botName}/TotalStone", _currentStats.TotalStoneGathered);
            tb.Add($"Economy/{botName}/TotalMeat", _currentStats.TotalMeatGathered);
            tb.Add($"Military/{botName}/Soldiers", _currentStats.TotalSoldiersCreated);

            // --- MEKANSAL VERİLERİ DOSYAYA YAZDIR ---
            SaveSpatialDataAsJSON(_currentStats);

            // Klasik CSV kaydı
            SaveMatchToCSV(_currentStats);
        }
        UnsubscribeAnalytics();

        // --- İSTATİSTİKLERİ GÜNCELLE ---
        _statsTotalEpisodes++;
        TrackReward(reward);
        _statsLastEpisodeReward = _statsCurrentReward;

        // Grafiğe son durumu ekle
        _rewardGraphHistory.Add(_statsCurrentReward);

        if (reward > 0) _statsWins++;
        else _statsLosses++;
        // -------------------------------

        if (Orchestrator != null)
        {
            if (reward == 0 && EnemyDifficulty == AIDifficulty.Passive) reward = -1.0f;

            Orchestrator.AddGroupReward(reward);
            Orchestrator.EndGroupEpisode();
        }
        Orchestrator.IsWaitingForDecision = false;

        if (Player2Controller == PlayerControllerType.AI && AgentP2 != null)
        {
            float p2Reward = -reward; // P1 kazandıysa P2 kaybetmiştir

            // YENİ: Orchestrator üzerinden grup ödülü veriyoruz
            AgentP2.AddGroupReward(p2Reward);
            AgentP2.EndGroupEpisode();
        }

        if (GameSessionSettings.IsLoadedFromMenu)
        {
            Debug.Log("🔙 Oyun Bitti. Menüye dönülüyor...");

            // Eğer statik eventleri temizlemezsek yeni sahnede hata verebilir
            UnsubscribeAnalytics();

            SceneManager.LoadScene(MenuSceneName);
        }
        else
        {
            ResetSimulation();
        }

    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;
        _timer = 0;
        _statsCurrentReward = 0f;
        _farmRewardGiven = false;
        _fullEcoMilestoneGiven = false;
        _p1NoSoldierCounter = 0;
        _p2NoSoldierCounter = 0;

        SimResourceSystem.OnResourceSpent += HandleAnalyticsSpend;

        // Yeni Episode Sıfırlamaları
        _cumulativeKills = 0;
        _cumulativeRazes = 0;
        _myBuildingCount = 1; // Base ile başlıyoruz

        float difficultyLevel = 0.0f;
        if (Academy.IsInitialized)
            difficultyLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("enemy_difficulty_level", 0.0f);

        if (EnemyDifficulty == AIDifficulty.Passive) difficultyLevel = 0;
        else if (EnemyDifficulty == AIDifficulty.Defensive) difficultyLevel = 0.5f;
        else difficultyLevel = 2;

        _world = new SimWorldState(MapSize, MapSize);

        int finalSeed = mapSeed;
        if (useRandomSeed || mapSeed <= 0)
        {
            finalSeed = System.DateTime.Now.Millisecond + System.DateTime.Now.Second * 1000;
        }
        GenerateMap(finalSeed);

        if (Visualizer != null)
        {
            // Visualizer'a "Artık bu dünyaya bakacaksın" diyoruz.
            Visualizer.Initialize(_world);
        }

        if (gameObject.name == AllowedAgentName)
        {
            SimGameContext.ActiveWorld = _world;
        }

        // 1. SİSTEMLERİ BAŞLAT
        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        _unitSys.OnUnitAttackedUnit -= HandleAdversarialAttackUnit;
        _unitSys.OnUnitAttackedBuilding -= HandleAdversarialAttackBuilding;
        SimBuildingSystem.OnTowerAttacked -= HandleTowerDamage;

        // Yeni eventlere abone ol
        _unitSys.OnUnitAttackedUnit += HandleAdversarialAttackUnit;
        _unitSys.OnUnitAttackedBuilding += HandleAdversarialAttackBuilding;
        SimBuildingSystem.OnTowerAttacked += HandleTowerDamage;

        // ---------------------------------
        // 2. OYUNCU KAYNAKLARINI ATAN
        _world.Players.Clear();
        _world.Players.Add(1, new SimPlayerData
        {
            PlayerID = 1,
            Wood = 500,
            Stone = 500,
            Meat = 500,
            MaxPopulation = 20,
            CurrentPopulation = 0
        });
        _world.Players.Add(2, new SimPlayerData
        {
            PlayerID = 2,
            Wood = 500,
            Stone = 500,
            Meat = 500,
            MaxPopulation = 20,
            CurrentPopulation = 0
        });

        // İstatistikler için sıfırla
        _lastWood = 500;
        _lastStone = 500;
        _lastMeat = 500;
        _lastWorkerCount = 0;

        // 3. BASE'LERİ KUR
        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        // -------------------------------------------------------------------------
        // PLAYER 1 KURULUMU & ORCHESTRATOR YÖNETİMİ
        // -------------------------------------------------------------------------
        if (AgentP1 != null)
        {
            // Eğer P1 AI ise Orchestrator'ı AÇ, değilse KAPAT
            bool isP1AI = (Player1Controller == PlayerControllerType.AI);
            AgentP1.gameObject.SetActive(isP1AI);

            if (isP1AI)
            {
                // AI Kurulumunu Yap
                AgentP1.Setup(_world, _gridSys, _unitSys, _buildSys, this);
                AgentP1.IsHumanDemoMode = false;
            }
        }

        // Scripted Bot (Eğer AI değilse ve Scripted seçildiyse)
        if (Player1Controller == PlayerControllerType.Scripted)
        {
            _p1ScriptedBot = CreateBot(ScriptedBotP1, 1);
        }


        // -------------------------------------------------------------------------
        // PLAYER 2 KURULUMU & ORCHESTRATOR YÖNETİMİ
        // -------------------------------------------------------------------------
        if (AgentP2 != null)
        {
            // Eğer P2 AI ise Orchestrator'ı AÇ, değilse KAPAT
            bool isP2AI = (Player2Controller == PlayerControllerType.AI);
            AgentP2.gameObject.SetActive(isP2AI);

            if (isP2AI)
            {
                // AI Kurulumunu Yap
                AgentP2.MyPlayerID = 2; // Kimliği belirle
                AgentP2.Setup(_world, _gridSys, _unitSys, _buildSys, this);
            }
        }

        // Scripted Bot (Eğer AI değilse ve Scripted seçildiyse)
        if (Player2Controller == PlayerControllerType.Scripted)
        {
            _p2ScriptedBot = CreateBot(ScriptedBotP2, 2);
        }

        // İnsan Oyuncu Yetkisi (SimInputManager)
        if (SimInputManager.Instance != null)
        {
            if (Player1Controller == PlayerControllerType.Human) SimInputManager.Instance.LocalPlayerID = 1;
            else if (Player2Controller == PlayerControllerType.Human) SimInputManager.Instance.LocalPlayerID = 2;
            else SimInputManager.Instance.LocalPlayerID = 0; // İzleyici
        }

        _lastSoldiers = 0;
        _lastEnemyUnitCount = 0;
        _lastEnemyBuildingCount = 1;
        _lastEnemyBaseHealth = 1000f;
        _lastMyBaseHealth = 1000f;
        _lastFarmCount = 0;
        _lastWoodCutterCount = 0;
        _lastStonePitCount = 0;
        _barracksRewardGiven = false;
        _cumulativeCriticalGatherReward = 0f; // SIFIRLA

        // ANALİTİK BAŞLATMA:
        _currentStats = new MatchAnalytics(MapSize);
        _currentStats.Opponent = SelectedBotType;
        if (Orchestrator != null) Orchestrator.CurrentMatchStats = _currentStats;
        if (AgentP2 != null) Orchestrator.CurrentMatchStats = _currentStats;

        // Event Abone Olma (Statik eventler olduğu için temizlik önemli)
        UnsubscribeAnalytics();
        SimResourceSystem.OnResourceGathered += HandleAnalyticsGather;
        SimBuildingSystem.OnUnitCreated += HandleAnalyticsUnit;
        SimBuildingSystem.OnBuildingFinished += HandleAnalyticsBuilding;
    }

    private void GenerateMap(int seed)
    {
        int MapSize = _world.Map.Grid.GetLength(0);
        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 0; y < MapSize; y++)
            {
                _world.Map.Grid[x, y] = new SimMapNode { x = x, y = y, Type = SimTileType.Grass, IsWalkable = true, OccupantID = -1 };
            }
        }

        UnityEngine.Random.InitState(seed);
        int resourceCount = 45;
        for (int i = 0; i < resourceCount; i++)
        {
            int x = UnityEngine.Random.Range(0, MapSize);
            int y = UnityEngine.Random.Range(0, MapSize);
            if ((x < 5 && y < 5) || (x > MapSize - 5 && y > MapSize - 5)) continue;

            if (_world.Map.Grid[x, y].IsWalkable)
            {
                var res = new SimResourceData { ID = _world.NextID(), GridPosition = new int2(x, y), AmountRemaining = 500 };
                float r = UnityEngine.Random.value;
                if (r < 0.33f) { res.Type = SimResourceType.Wood; _world.Map.Grid[x, y].Type = SimTileType.Forest; }
                else if (r < 0.66f) { res.Type = SimResourceType.Stone; _world.Map.Grid[x, y].Type = SimTileType.Stone; }
                else { res.Type = SimResourceType.Meat; _world.Map.Grid[x, y].Type = SimTileType.MeatBush; }

                _world.Resources.Add(res.ID, res);
                _world.Map.Grid[x, y].OccupantID = res.ID;
                _world.Map.Grid[x, y].IsWalkable = false;
            }
        }
    }

    private void SetupBase(int pid, int2 pos)
    {
        var building = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = pid,
            Type = SimBuildingType.Base,
            GridPosition = pos,
            Health = 1000,
            MaxHealth = 1000,
            IsConstructed = true
        };
        SimBuildingSystem.InitializeBuildingStats(building, true);
        _world.Buildings.Add(building.ID, building);
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        _world.Map.Grid[pos.x, pos.y].OccupantID = building.ID;

        int start_workercount = 1;
        for (int i = 0; i < start_workercount; i++)
        {
            SimBuildingSystem.SpawnUnit(_world, pos, SimUnitType.Worker, pid);
        }
    }


    private void HandleAnalyticsGather(int playerID, int amount, SimResourceType type)
    {
        if (playerID != 1 || _currentStats == null) return;
        if (type == SimResourceType.Wood) _currentStats.TotalWoodGathered += amount;
        else if (type == SimResourceType.Stone) _currentStats.TotalStoneGathered += amount;
        else if (type == SimResourceType.Meat) _currentStats.TotalMeatGathered += amount;
    }

    private void HandleAnalyticsUnit(SimUnitData unit)
    {
        // İstatistikler (Eski kodun)
        if (unit.PlayerID != 1 || _currentStats == null) return;

        if (unit.UnitType == SimUnitType.Worker)
        {
            _currentStats.TotalWorkersCreated++;

            // --- EKONOMİ DERSİ 101: FARM YOKSA İŞÇİ BASMA ---

            // Aktif (inşa edilmiş veya edilmekte olan) Farm var mı kontrol et
            int farmCount = _world.Buildings.Values.Count(b =>
                b.PlayerID == 1 &&
                b.Type == SimBuildingType.Farm);
            // Not: b.IsConstructed kontrolünü kaldırdım. 
            // Temelini atsa bile "tamam farm kuruyor" kabul edelim ki ceza yemesin.

            if (farmCount == 0)
            {
                // Farm yokken işçi bastı -> BÜYÜK CEZA
                // Bu ceza, o işçiden alacağı tüm potansiyel ödülleri silmeli.
                // Orchestrator.AddGroupReward(-3.0f);

                // Opsiyonel: Agent'ı log'da ifşa et
                // Debug.Log($"<color=red>CEZA: Farm yokken işçi basıldı! (-3.0)</color>");
            }
        }
        else
        {
            _currentStats.TotalSoldiersCreated++;
        }
    }

    private void HandleAnalyticsBuilding(SimBuildingData b)
    {
        if (b.PlayerID != 1 || _currentStats == null) return;
        if (b.Type == SimBuildingType.Tower) _currentStats.TotalTowersBuilt++;
    }

    private void UnsubscribeAnalytics()
    {
        SimResourceSystem.OnResourceGathered -= HandleAnalyticsGather;
        SimBuildingSystem.OnUnitCreated -= HandleAnalyticsUnit;
        SimBuildingSystem.OnBuildingFinished -= HandleAnalyticsBuilding;
        SimResourceSystem.OnResourceSpent -= HandleAnalyticsSpend;
    }

    private void SaveMatchToCSV(MatchAnalytics s)
    {
        string path = Application.dataPath + "/Match_Analytics.csv";
        bool exists = System.IO.File.Exists(path);
        using (System.IO.StreamWriter writer = new System.IO.StreamWriter(path, true))
        {
            if (!exists) writer.WriteLine("Opponent,Win,Duration,Workers,Soldiers,Towers,Wood,Stone,Meat");
            writer.WriteLine($"{s.Opponent},{s.IsWin},{s.MatchDuration:F2},{s.TotalWorkersCreated},{s.TotalSoldiersCreated},{s.TotalTowersBuilt},{s.TotalWoodGathered},{s.TotalStoneGathered},{s.TotalMeatGathered}");
        }
    }

    private void SaveSpatialDataAsJSON(MatchAnalytics s)
    {
        // Veri klasörünü oluştur
        string folderPath = Application.dataPath + "/SpatialLogs";
        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);

        // Dosya adı: Ep_123_Rusher_Spatial.json
        string fileName = $"Ep_{s.EpisodeID}_{s.Opponent}_Spatial.json";
        string fullPath = System.IO.Path.Combine(folderPath, fileName);

        // Veriyi JSON formatına çevir (Heatmap ve AttackTargets dahil)
        // Not: MatchAnalytics sınıfın [Serializable] olmalıdır.
        string json = JsonUtility.ToJson(s);
        System.IO.File.WriteAllText(fullPath, json);

        // Debug.Log($"[Analytics] Mekansal veriler kaydedildi: {fileName}");
    }

    private void HandleAnalyticsSpend(int playerID, int amount, SimResourceType type)
    {
        if (playerID != 1 || _currentStats == null) return;

        if (type == SimResourceType.Wood) _currentStats.TotalWoodSpent += amount;
        else if (type == SimResourceType.Stone) _currentStats.TotalStoneSpent += amount;
        else if (type == SimResourceType.Meat) _currentStats.TotalMeatSpent += amount;
    }

    public void RecordInferenceTime(double ms)
    {
        if (!RecordInferenceToCSV) return;

        string line = $"{_currentStep},{ms.ToString("F4", CultureInfo.InvariantCulture)},{SelectedBotType},{EnemyDifficulty}";
        _inferenceBuffer.Add(line);

        // Performans için her 10 kayıtta bir dosyaya yazalım
        if (_inferenceBuffer.Count >= 10)
        {
            File.AppendAllLines(_inferenceFilePath, _inferenceBuffer);
            _inferenceBuffer.Clear();
        }
    }


    // 1. FOCUS FIRE (İKİ BİRİMLE TEK HEDEFE SALDIRMA)
    private void HandleAdversarialAttackUnit(SimUnitData attacker, SimUnitData victim, float damage)
    {
        // Sadece BENİM (Player 1) saldırılarımı takip et
        if (attacker.PlayerID != 1 || victim.PlayerID == 1) return;

        if (!_frameAttackLog.ContainsKey(victim.ID))
        {
            _frameAttackLog[victim.ID] = new HashSet<int>();
        }

        // Saldıranın ID'sini kaydet
        _frameAttackLog[victim.ID].Add(attacker.ID);

        // Eğer bu adımda aynı kurbana vuran BENİM ünite sayım 2 ise (Tam işbirliği anı)
        if (_frameAttackLog[victim.ID].Count >= 2)
        {
            // Ödülü Orchestrator üzerinden veriyoruz
            if (Orchestrator != null)
            {
                // Orchestrator.AddGroupReward(0.1f); // Güzel bir taktik ödülü
                // Debug.Log($"[Tactic] Focus Fire! Target: {victim.ID}");
            }
        }
    }

    // 2. RAKİP BASE'İNE SALDIRMA (ÖZEL ÖDÜL)
    private void HandleAdversarialAttackBuilding(SimUnitData attacker, SimBuildingData building, float damage)
    {
        // Sadece BENİM (Player 1) saldırılarım
        if (attacker.PlayerID != 1) return;

        // Eğer hedef ANA ÜS (Base) ise
        if (building.Type == SimBuildingType.Base)
        {
            if (_lastEnemyUnitCount <= 0)
            {
                if (Orchestrator != null)
                {
                    // Temizlik Ödülü: Hasar * 0.02 (Normalden biraz daha yüksek veriyoruz ki bitirsin)
                    // Örn: 100 hasar = 0.2 puan
                    Orchestrator.AddGroupReward(damage * 0.002f);
                }
            }
        }
    }

    // 3. İŞÇİLERİ SAĞ TUTMA (PERİYODİK KONTROL)
    // private void CheckWorkerSurvivalBonus()
    // {
    //     if (_world == null) return;

    //     int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

    //     // Oyunun süresine göre beklentimiz artıyor
    //     int expected = 0;
    //     if (_currentStep < 1500) expected = 5;       // Erken oyun
    //     else if (_currentStep < 3000) expected = 10; // Orta oyun
    //     else expected = 15;                          // Geç oyun

    //     if (workerCount >= expected && Orchestrator != null)
    //     {
    //         // "Aferin, ekonomini koruyorsun" ödülü
    //         Orchestrator.AddGroupReward(1.0f);
    //     }
    // }

    private void HandleTowerDamage(SimBuildingData tower, SimUnitData victim, float damage)
    {
        // Sadece BENİM kulem düşmana vuruyorsa
        if (tower.PlayerID == 1 && victim.PlayerID != 1)
        {
            if (Orchestrator != null)
            {
                // Hasar başına ufak ödül (Savunmayı teşvik eder)
                //1000 0.5
                Orchestrator.AddGroupReward(damage * 0.0005f);
            }
        }
    }
    void OnDestroy()
    {
        if (_unitSys != null)
        {
            _unitSys.OnUnitAttackedUnit -= HandleAdversarialAttackUnit;
            _unitSys.OnUnitAttackedBuilding -= HandleAdversarialAttackBuilding;
        }
        SimBuildingSystem.OnTowerAttacked -= HandleTowerDamage;
        UnsubscribeAnalytics();
    }

    // --- YENİ: HAYATTA KALMA VE OYUNU UZATMA ÖDÜLÜ ---
    private void CheckSurvivalMilestones()
    {
        if (Orchestrator == null) return;

        // Hedef: 3500. adım
        int targetStep = 3500;

        // Sadece hedef adıma kadar ödül ver, sonrası için verme (Amacımız sonsuza kadar uzatmak değil, late-game'e kalmak)
        if (_currentStep > targetStep) return;

        // 1. BÜYÜK ÖDÜL (Tam 3500. Adım)
        if (_currentStep == targetStep)
        {
            Orchestrator.AddGroupReward(1.0f); // Çok büyük bir hayatta kalma bonusu!
            Debug.Log($"<color=green><b>🛡️ SURVIVAL TARGET REACHED! ({targetStep} Steps) -> +1.0 Reward</b></color>");

            // İstersen burada grafiğe de işaret koyabilirsin
            TrackReward(1.0f);
        }

        // 2. ARA ÖDÜLLER (Motivasyonu korumak için her 500 adımda bir)
        else if (_currentStep > 0 && _currentStep % 500 == 0)
        {
            float milestoneReward = 0.1f;
            Orchestrator.AddGroupReward(milestoneReward);
            // Debug.Log($"⏱️ Survival Milestone: {_currentStep} Steps (+{milestoneReward})");
        }
    }


    // Bu metodu AdversarialTrainerRunner sınıfının içine uygun bir yere ekleyin.
    // Orchestrator'da aksiyonlar işlenirken bu metod çağrılmalıdır.
    private const int ACT_ATTACK = 10;
    private const int ACT_GATHER = 12;
    // Bu metodu AdversarialTrainerRunner sınıfının içinde mevcut olanla değiştirin.
    public void NotifyAgentAction(int actionType, int targetIndex)
    {
        if (Orchestrator == null || _world == null || !_world.Players.ContainsKey(1)) return;

        // Sabitler (Kodun başka yerinde tanımlı değilse buraya hardcode veya const olarak ekleyin)
        const int ACT_GATHER = 12;

        // --- SENARYO C: KRİTİK KAYNAK TOPLAMA BONUSU (CRITICAL GATHER REWARD) ---
        if (actionType == ACT_GATHER)
        {
            // 1. Hedeflenen Grid Index'i (Flat) Koordinata (x,y) çevir
            // Not: Map.Grid.GetLength(0) harita genişliğini verir.
            int w = _world.Map.Grid.GetLength(0);
            int x = targetIndex % w;
            int y = targetIndex / w;

            // 2. O koordinatta gerçekten bir kaynak var mı bul
            // GridPosition struct olduğu için x ve y karşılaştırması yapıyoruz.
            var resource = _world.Resources.Values.FirstOrDefault(r => r.GridPosition.x == x && r.GridPosition.y == y);

            if (resource != null)
            {
                var player = _world.Players[1];
                int currentResourceAmount = 0;

                // 3. Kaynağın türünü belirle ve oyuncunun mevcut stoğuna bak
                switch (resource.Type)
                {
                    case SimResourceType.Wood:
                        currentResourceAmount = player.Wood;
                        break;
                    case SimResourceType.Stone:
                        currentResourceAmount = player.Stone;
                        break;
                    case SimResourceType.Meat:
                        currentResourceAmount = player.Meat;
                        break;
                }

                // 4. Kural: Kaynak 250'den azsa ödül ver
                if (currentResourceAmount < 250)
                {
                    // Ödül Miktarı Ayarı:
                    // 0.02f = Ufak bir teşvik. Spamlamasını engellemek için çok büyük vermiyoruz.
                    // 0.1f  = Çok güçlü bir teşvik.
                    float criticalBonus = 0.004f;

                    // Sadece bu kararı veren "Action" çıktısını ödüllendiriyoruz.
                    // GroupReward verirsek tüm takımı ödüllendirir, ActionReward sadece o anki kararı pekiştirir.
                    Orchestrator.AddActionRewardOnly(criticalBonus);

                    // İstersen Target (Konum) seçimini de ayrıca pekiştirebilirsin:
                    // Orchestrator.AddTargetRewardOnly(criticalBonus);

                    // Konsolda görüp teyit etmek için (Eğitimde kapatabilirsin):
                    // Debug.Log($"[Critical Eco] {resource.Type} is low ({currentResourceAmount})! Gather Order Reward: +{criticalBonus}");
                }
            }
        }
    }

    private IMacroAI CreateBot(AIOpponentType type, int playerID)
    {
        switch (type)
        {
            case AIOpponentType.Rusher: return new RusherAI(_world, playerID);
            case AIOpponentType.Turtle: return new TurtleAI(_world, playerID);
            case AIOpponentType.EcoBoom: return new EcoBoomAI(_world, playerID);
            case AIOpponentType.WorkerRush: return new WorkerRushAI(_world, playerID);
            case AIOpponentType.Harasser: return new HarasserAI(_world, playerID);
            case AIOpponentType.EliteCommander: return new EliteCommanderAI(_world, playerID);
            default: return new SimpleMacroAI(_world, playerID, 1.0f);
        }
    }

}

/*
girdi kaymanlarına oradaki birimin saldırıp saldırmadığını ve oradaki bina-birimin hasar alıp almadığı bilgisini resnet ile vermemiz lazım

reward fikirleri
anabinaya saldıran askerlere saldırmak
ekonomi binaları dikmek
gather odaklı bir reward yapısı
*/