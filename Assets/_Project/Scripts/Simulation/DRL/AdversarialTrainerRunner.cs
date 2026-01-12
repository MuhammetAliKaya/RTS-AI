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


public class AdversarialTrainerRunner : MonoBehaviour
{
    [Header("Ayarlar")]
    public RTSOrchestrator Orchestrator;

    public int MapSize = 20;
    public int MaxSteps = 5000;

    public string AllowedAgentName = "AdvTrainerRunner";

    [Header("Inference Analizi")]
    public bool RecordInferenceToCSV = true;
    private string _inferenceFilePath;
    private List<string> _inferenceBuffer = new List<string>();

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
        GUILayout.Label($"🪵 {_lastWood}", textStyle);
        GUILayout.Label($"🪨 {_lastStone}", textStyle);
        GUILayout.Label($"🍖 {_lastMeat}", textStyle);
        GUILayout.EndHorizontal();

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
        // --- KİLİT MEKANİZMASI ---
        if (Orchestrator != null && Orchestrator.CurrentState != RTSOrchestrator.OrchestratorState.Idle)
        {
            return;
        }
        _frameAttackLog.Clear();
        // _enemyDecisionCounter++;
        // if (_enemyDecisionCounter >= ENEMY_DECISION_INTERVAL && IsTrainingMode)
        // {
        //     _enemyDecisionCounter = 0;
        //     // if (_enemyAI != null) _enemyAI.Update(dt
        //     // // * ENEMY_DECISION_INTERVAL
        //     // ); // dt'yi biriken zamanla çarpabilirsin
        // }
        _enemyAI.Update(dt);
        // Ajan Güncellemesi
        _agentDecisionTimer += dt; // Gelen simülasyon adım süresini ekle
        if (_agentDecisionTimer >= AgentDecisionTimeStep)
        {
            dcCountAI++;
            // Debug.Log("dcCountAI " + dcCountAI);
            _agentDecisionTimer = 0f; // Zamanlayıcıyı sıfırla
            if (Orchestrator != null)
                Orchestrator.RequestFullDecision();
        }
        // 3. Simülasyonu İlerlet
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);
        if (_unitSys != null) _unitSys.UpdateAllUnits(dt);

        // 4. İstatistikleri ve Ödülleri Güncelle
        // UpdateStatisticsVariables(); // YENİ: İstatistikleri topla
        CheckSurvivalMilestones();
        CalculateCombatRewards();
        CalculateEconomyRewards();
        ApplyIdlePenalty();
        CheckGameResult();
        if (_currentStep > 0 && _currentStep % 100 == 0)
        {
            CheckWorkerSurvivalBonus();
        }

        // 5. Grafik verisini güncelle (Her 10 adımda bir güncelle ki grafik çok hızlı akmasın)
        if (_currentStep % 10 == 0) UpdateGraphHistory();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            EndGame(0);
        }
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

        // Sadece toplam toplanan odun 1000'den azsa ödül ver
        // (_currentStats.TotalWoodGathered kümülatiftir, harcayınca azalmaz)
        if (deltaWood > 0 && _currentStats != null && _currentStats.TotalWoodGathered <= 10000)
        {
            // Not: 0.0001f çok düşük olabilir, öğrenmeyi hızlandırmak için 0.001f veya 0.01f deneyebilirsin.
            Orchestrator.AddGroupReward(deltaWood * 0.0015f);
        }

        // Aynısını Taş ve Et için de yapmak istersen:
        if (deltaStone > 0 && _currentStats != null && _currentStats.TotalStoneGathered <= 10000)
        {
            Orchestrator.AddGroupReward(deltaStone * 0.0015f);
        }

        if (deltaMeat > 0 && _currentStats != null && _currentStats.TotalMeatGathered <= 100000)
        {
            Orchestrator.AddGroupReward(deltaMeat * 0.005f);
        }

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
                    Orchestrator.AddGroupReward(3.0f);
                    Debug.Log(">>> FIRST BARRACKS REWARD GIVEN! (+3.0) <<<");
                }
                else
                {
                    // --- SONRAKİ KIŞLALAR: STANDART ÖDÜL (1.0) ---
                    Orchestrator.AddGroupReward(1.0f);
                    // Debug.Log(">>> Additional Barracks Built (+1.0) <<<");
                }
            }
        }
        _lastBarracksCount = currentBarracks;

        // --- 3. İŞÇİ ÜRETİMİ (ESKİSİ) ---
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        if (currentWorkers > _lastWorkerCount)
        {
            float rewardAmount = 0.2f;
            Orchestrator.AddActionRewardOnly(rewardAmount);
            Orchestrator.AddUnitRewardOnly(rewardAmount);
            TrackReward(rewardAmount * 2);
        }

        // --- KULE (SAVUNMA) BONUSU ---
        // Mevcut (bitmiş) kule sayısını bul
        int currentTowers = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Tower && b.IsConstructed);

        // Eğer kule sayısı artmışsa VE abartmamışsa (Max 5 kule)
        if (currentTowers > _lastTowerCount && currentTowers <= 5)
        {
            if (Orchestrator != null)
            {
                Orchestrator.AddGroupReward(1f); // Kule stratejik yatırımdır
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
                    Orchestrator.AddGroupReward(15.0f);
                }
                // Sonraki çiftlikler (Sadece ilk 8 tanesi ödül verir)
                else if (currentFarms <= 8)
                {
                    Orchestrator.AddGroupReward(2.5f);
                }
                // 8'den fazlası gereksiz harcamadır, ödül yok.
            }
        }

        // 2. ODUNCU (CUTTER) - Limit: 5 Adet
        if (currentCutters > _lastWoodCutterCount)
        {
            // Sadece mantıklı sayıda yaparsa ödül ver
            if (currentCutters <= 5) Orchestrator.AddGroupReward(2.5f);
        }

        // 3. TAŞ OCAĞI (PIT) - Limit: 5 Adet
        if (currentPits > _lastStonePitCount)
        {
            if (currentPits <= 5) Orchestrator.AddGroupReward(2.5f);
        }

        if (!_fullEcoMilestoneGiven && currentFarms > 0 && currentCutters > 0 && currentPits > 0)
        {
            _fullEcoMilestoneGiven = true;
            float milestoneReward = 15.0f; // İlk kez üçüne de sahip olduğu için büyük ödül

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

        // 1. Asker Sayısı Artışı
        if (currentSoldiers > _lastSoldiers)
        {
            float r = 1f;
            Orchestrator.AddUnitRewardOnly(r);
            Orchestrator.AddActionRewardOnly(r);
            TrackReward(r * 2);
        }

        // 2. Düşman Öldürme
        if (currentEnemyUnits < _lastEnemyUnitCount)
        {
            int killCount = _lastEnemyUnitCount - currentEnemyUnits;

            // İSTATİSTİK GÜNCELLEME
            if (killCount > 0) _cumulativeKills += killCount;

            float rTarget = 0.2f * killCount;
            float rAction = 0.2f * killCount;
            float rUnit = 0.05f * killCount;

            Orchestrator.AddTargetRewardOnly(rTarget);
            Orchestrator.AddActionRewardOnly(rAction);
            Orchestrator.AddUnitRewardOnly(rUnit);

            TrackReward(rTarget + rAction + rUnit);
        }

        // 3. Bina Yıkma
        if (currentEnemyBuildings < _lastEnemyBuildingCount)
        {
            int destroyCount = _lastEnemyBuildingCount - currentEnemyBuildings;

            // İSTATİSTİK GÜNCELLEME
            if (destroyCount > 0) _cumulativeRazes += destroyCount;

            float baseReward = 1.0f * destroyCount;
            Orchestrator.AddGroupReward(baseReward);
            TrackReward(baseReward);
        }

        // 4. Kendi Üssümüz Hasar Alırsa
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        if (myBase != null)
        {
            if (myBase.Health < _lastMyBaseHealth)
            {
                float damageTaken = _lastMyBaseHealth - myBase.Health;
                float penalty = -damageTaken * 0.005f;
                Orchestrator.AddGroupReward(penalty);
                TrackReward(penalty);
            }
            _lastMyBaseHealth = myBase.Health;
        }

        if (currentEnemyBaseHealth < _lastEnemyBaseHealth)
        {
            float damageDealt = _lastEnemyBaseHealth - currentEnemyBaseHealth;
            // Hasar başına puan (Örn: 10 hasar = 0.1 puan)
            Orchestrator.AddGroupReward(damageDealt * 0.001f);
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

        if (myBase == null) // Kaybettik
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 10.0f;
            EndGame(-50.0f);
            Debug.Log("Game Lost");
        }
        else if (enemyBase == null) // Kazandık
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 10.0f;
            Debug.Log("Game Won");
            EndGame(50.0f);
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
        ResetSimulation();

    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;
        _timer = 0;
        _statsCurrentReward = 0f;
        _farmRewardGiven = false;
        _fullEcoMilestoneGiven = false;

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
        SetupBase(1, new int2(MapSize - 3, MapSize - 3));
        SetupBase(2, new int2(2, 2));

        if (Orchestrator != null)
        {
            Orchestrator.Setup(_world, _gridSys, _unitSys, _buildSys, this);
        }

        if (UseMacroAI)
        {
            switch (SelectedBotType)
            {
                case AIOpponentType.Rusher: _enemyAI = new RusherAI(_world, 2); break;
                case AIOpponentType.Turtle: _enemyAI = new TurtleAI(_world, 2); break;
                case AIOpponentType.EcoBoom: _enemyAI = new EcoBoomAI(_world, 2); break;
                case AIOpponentType.WorkerRush: _enemyAI = new WorkerRushAI(_world, 2); break;
                case AIOpponentType.Harasser: _enemyAI = new HarasserAI(_world, 2); break;
                case AIOpponentType.EliteCommander: _enemyAI = new EliteCommanderAI(_world, 2); break;
                case AIOpponentType.Random:
                    int rand = UnityEngine.Random.Range(0, 5);
                    if (rand == 0) _enemyAI = new SimpleMacroAI(_world, 2, 1f);
                    else if (rand == 1) _enemyAI = new RusherAI(_world, 2);
                    else if (rand == 2) _enemyAI = new TurtleAI(_world, 2);
                    else if (rand == 3) _enemyAI = new EcoBoomAI(_world, 2);
                    else _enemyAI = new WorkerRushAI(_world, 2);
                    break;
                default: _enemyAI = new SimpleMacroAI(_world, 2, 1.0f); break;
            }
        }
        else
        {
            _enemyAI = null;
        }

        if (Visualizer != null) Visualizer.Initialize(_world);

        _lastSoldiers = 0;
        _lastEnemyUnitCount = 0;
        _lastEnemyBuildingCount = 1;
        _lastEnemyBaseHealth = 1000f;
        _lastMyBaseHealth = 1000f;
        _lastFarmCount = 0;
        _lastWoodCutterCount = 0;
        _lastStonePitCount = 0;
        _barracksRewardGiven = false;

        // ANALİTİK BAŞLATMA:
        _currentStats = new MatchAnalytics(MapSize);
        _currentStats.Opponent = SelectedBotType;
        if (Orchestrator != null) Orchestrator.CurrentMatchStats = _currentStats;

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
                    // Örn: 10 hasar = 0.2 puan
                    Orchestrator.AddGroupReward(damage * 0.02f);
                }
            }
        }
    }

    // 3. İŞÇİLERİ SAĞ TUTMA (PERİYODİK KONTROL)
    private void CheckWorkerSurvivalBonus()
    {
        if (_world == null) return;

        int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

        // Oyunun süresine göre beklentimiz artıyor
        int expected = 0;
        if (_currentStep < 1500) expected = 5;       // Erken oyun
        else if (_currentStep < 3000) expected = 10; // Orta oyun
        else expected = 15;                          // Geç oyun

        if (workerCount >= expected && Orchestrator != null)
        {
            // "Aferin, ekonomini koruyorsun" ödülü
            Orchestrator.AddGroupReward(1.0f);
        }
    }

    private void HandleTowerDamage(SimBuildingData tower, SimUnitData victim, float damage)
    {
        // Sadece BENİM kulem düşmana vuruyorsa
        if (tower.PlayerID == 1 && victim.PlayerID != 1)
        {
            if (Orchestrator != null)
            {
                // Hasar başına ufak ödül (Savunmayı teşvik eder)
                Orchestrator.AddGroupReward(damage * 0.005f);
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
            Orchestrator.AddGroupReward(13.0f); // Çok büyük bir hayatta kalma bonusu!
            Debug.Log($"<color=green><b>🛡️ SURVIVAL TARGET REACHED! ({targetStep} Steps) -> +10.0 Reward</b></color>");

            // İstersen burada grafiğe de işaret koyabilirsin
            TrackReward(10.0f);
        }

        // 2. ARA ÖDÜLLER (Motivasyonu korumak için her 500 adımda bir)
        else if (_currentStep > 0 && _currentStep % 500 == 0)
        {
            float milestoneReward = 1.0f;
            Orchestrator.AddGroupReward(milestoneReward);
            // Debug.Log($"⏱️ Survival Milestone: {_currentStep} Steps (+{milestoneReward})");
        }
    }


    // Bu metodu AdversarialTrainerRunner sınıfının içine uygun bir yere ekleyin.
    // Orchestrator'da aksiyonlar işlenirken bu metod çağrılmalıdır.
    private const int ACT_ATTACK = 10;
    private const int ACT_GATHER = 12;
    public void NotifyAgentAction(int actionType, int targetIndex)
    {
        if (Orchestrator == null || _world == null || !_world.Players.ContainsKey(1)) return;

        // 1. Ekonomi binalarının (İnşaatı bitmiş) anlık durumunu kontrol et
        int currentFarms = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Farm && b.IsConstructed);
        int currentCutters = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.WoodCutter && b.IsConstructed);
        int currentPits = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.StonePit && b.IsConstructed);

        bool hasFullEco = (currentFarms > 0 && currentCutters > 0 && currentPits > 0);

        // --- SENARYO A: EKONOMİSİZ SALDIRI CEZASI ---
        if (actionType == ACT_ATTACK) // 10: ACT_ATTACK_ENEMY
        {
            if (!hasFullEco)
            {
                // float heavyPenalty = -5.0f; // Çok ağır ceza
                // Orchestrator.AddGroupReward(heavyPenalty);
                // Orchestrator.AddActionRewardOnly(heavyPenalty);

                // Debug.Log($"<color=red><b>STRATEJİ HATASI:</b> 3 ekonomi binası olmadan saldırı emri! (-5.0)</color>");
                // TrackReward(heavyPenalty);
            }
        }

        // --- SENARYO B: TAM EKONOMİ AKSİYON BONUSU ---
        // 3 binaya da sahipse, her yaptığı aksiyon için küçük bir teşvik alacak
        // if (hasFullEco)
        // {
        //     float ecoBonus = 0.05f;
        //     Orchestrator.AddActionRewardOnly(ecoBonus);
        // }

        // --- SENARYO C: KRİTİK KAYNAK TOPLAMA BONUSU ---

        if (actionType == ACT_GATHER) // 12: ACT_GATHER_RES
        {
            // Hedeflenen karedeki kaynağı bul
            int w = _world.Map.Width;
            int2 targetPos = new int2(targetIndex % w, targetIndex / w);
            var resource = _world.Resources.Values.FirstOrDefault(r => r.GridPosition == targetPos);

            if (resource != null)
            {
                var player = _world.Players[1];
                int currentResourceAmount = 0;

                if (resource.Type == SimResourceType.Wood) currentResourceAmount = player.Wood;
                else if (resource.Type == SimResourceType.Stone) currentResourceAmount = player.Stone;
                else if (resource.Type == SimResourceType.Meat) currentResourceAmount = player.Meat;

                // Eğer o kaynak 150'den azsa ve ajan toplamaya gittiyse ödüllendir
                if (currentResourceAmount < 250)
                {
                    float criticalBonus = 0.5f;
                    Orchestrator.AddActionRewardOnly(criticalBonus);
                    Orchestrator.AddTargetRewardOnly(criticalBonus * 2);

                    // Debug.Log($"<color=cyan>Kritik Kaynak Toplama: {resource.Type} bitiyor!</color>");
                }
            }
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