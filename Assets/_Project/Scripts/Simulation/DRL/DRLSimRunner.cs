using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;

public class DRLSimRunner : MonoBehaviour
{
    [Header("AI Ayarları")]
    public RTSAgent Agent;
    public bool TrainMode = true;
    [Range(0, 4)]
    public int DebugLevel = 4; // <--- YENİ: Elle level seçimi (Varsayılan 4-General)

    public int MaxSteps = 5000;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    // --- SİSTEMLER ---
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    private int _currentStep = 0;
    private bool _isInitialized = false;

    // --- ÖDÜL TAKİBİ ---
    private int _lastWood = 0;
    private int _lastStone = 0; // Yeni
    private int _lastMeat = 0;  // Yeni
    private int _lastUnitCount = 0;
    private int _lastBuildingCount = 0;
    private int _lastBarracksCount = 0;
    private float _decisionTimer = 0f;

    // --- CURRICULUM (MÜFREDAT) AYARLARI ---
    private float _currentLevel = 0; // Varsayılan: 0 (Odun Toplama)

    public float CurrentLevel => _currentLevel;

    private void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();
        if (Agent != null) Agent.Runner = this;

        Application.targetFrameRate = !TrainMode ? 60 : -1;
        Time.timeScale = !TrainMode ? 1.0f : 20.0f; // Eğitimde hızı 20x-100x yapabilirsiniz

        ResetSimulation();
    }

    private void Update()
    {
        if (!TrainMode) ManualUpdate();
    }

    public void ManualUpdate()
    {
        if (!_isInitialized) return;

        float dt = TrainMode ? 0.1f : Time.deltaTime;

        // Karar Mekanizması
        bool requestDecision = true;
        if (!TrainMode)
        {
            _decisionTimer += dt;
            if (_decisionTimer < 0.1f) requestDecision = false;
            else _decisionTimer = 0f;
        }

        if (requestDecision && Agent != null) Agent.RequestDecision();

        // Simülasyonu İlerlet
        _buildSys.UpdateAllBuildings(dt);

        var unitIds = _world.Units.Keys.ToList();
        foreach (var uid in unitIds)
        {
            if (_world.Units.TryGetValue(uid, out SimUnitData unit))
                _unitSys.UpdateUnit(unit, dt);
        }

        // Ödül ve Bitiş Kontrolü
        CalculateDenseRewards();
        CheckWinCondition(); // <-- Ders seviyesine göre kazanma kontrolü burada
        _currentStep++;

        if (_currentStep >= MaxSteps)
        {
            EndGame(0); // Zaman doldu
        }
    }

    public void ResetSimulation()
    {
        if (TrainMode)
        {
            if (Academy.IsInitialized)
            {
                _currentLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("rts_level", 0.0f);
            }
        }
        else
        {
            _currentLevel = 4.0f; // Level 4 = General (Her şey serbest)
            Debug.Log("🎥 İZLEME MODU: Ajan General Seviyesinde (Level 4) Başlatıldı.");
        }

        // Level 2 (Et) ve sonrası için süreyi biraz uzat ki strateji kurabilsin
        MaxSteps = _currentLevel == 2 ? 2000 : (_currentLevel < 2 ? 1500 : 5000);

        if (!TrainMode) Debug.Log($"🔄 SİMÜLASYON BAŞLADI | Ders: {_currentLevel}");

        _world = new SimWorldState(20, 20);
        GenerateRTSMap();

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        // --- 2. KAYNAK AYARLAMASI (YENİ STRATEJİ) ---

        int startWood = 2000;
        int startStone = 2000;
        int startMeat = 2000; // Varsayılan zengin başlangıç

        // DERS 0: Odun Toplama
        if (_currentLevel == 0)
        {
            startWood = 0;
        }
        // DERS 1: Taş Toplama
        else if (_currentLevel == 1)
        {
            startStone = 0;
        }
        // DERS 2: Et Toplama & YATIRIM DERSİ
        else if (_currentLevel == 2)
        {
            // KRİTİK HAMLE: Ajana 1 işçi parası (250) veriyoruz!
            // Böylece "Parayı harcayıp işçi mi basayım, yoksa saklayayım mı?" ikilemini yaşayacak.
            // Doğru cevap: İşçi basmak.
            startMeat = 300;
        }

        _resSys.AddResource(1, SimResourceType.Wood, startWood);
        _resSys.AddResource(1, SimResourceType.Stone, startStone);
        _resSys.AddResource(1, SimResourceType.Meat, startMeat);

        _resSys.IncreaseMaxPopulation(1, 10); // Nüfus limitini baştan açtık

        SetupBase(1, new int2(2, 2));

        // ... (Değişken sıfırlama kısımları aynı) ...
        _currentStep = 0;
        _isInitialized = true;
        _decisionTimer = 0f;

        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood;
        _lastStone = p.Stone;
        _lastMeat = p.Meat;
        _lastUnitCount = 1;
        _lastBuildingCount = 1;
        _lastBarracksCount = 0;

        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void CheckWinCondition()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);

        // DERS 0: ODUN (Hedef 300)
        if (_currentLevel == 0)
        {
            if (player.Wood >= 300) EndGame(1.0f);
            return;
        }

        // DERS 1: TAŞ (Hedef 200)
        if (_currentLevel == 1)
        {
            if (player.Stone >= 200) EndGame(1.0f);
            return;
        }

        // DERS 2: ET & YATIRIM (Hedef Yükseltildi: 600)
        // Neden 600? Çünkü 300 ile başlıyor. Sadece 300 toplarsa dersi geçerse yatırım yapmaz.
        // Ama 600 yaparsak, tek işçi ile yetişemez, mecburen işçi basıp (harcama yapıp) hızlanmak zorunda kalır.
        if (_currentLevel == 2)
        {
            if (player.Meat >= 600) EndGame(2.0f); // Zor görev, büyük ödül
            return;
        }

        // DERS 3: İNŞAAT (Kışla)
        if (_currentLevel == 3)
        {
            bool hasBarracks = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Barracks);
            if (hasBarracks) EndGame(2.0f);
            return;
        }

        // DERS 4: SAVAŞ (Asker)
        if (_currentLevel >= 4)
        {
            int soldierCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier);
            if (soldierCount >= 3) EndGame(2.0f);
        }

        // KAYBETME: İşçi kalmadıysa
        int workerCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Worker);
        int soldierTotal = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier);

        if (workerCount == 0 && soldierTotal == 0 && _currentStep > 10)
        {
            EndGame(-1.0f);
        }
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null) return;

        int deltaWood = player.Wood - _lastWood;
        int deltaStone = player.Stone - _lastStone;
        int deltaMeat = player.Meat - _lastMeat;

        _lastWood = player.Wood;
        _lastStone = player.Stone;
        _lastMeat = player.Meat;

        // ÖDÜL AYARLAMALARI
        if (_currentLevel == 0) // Odun
        {
            if (deltaWood > 0) Agent.AddReward(0.01f * deltaWood);
        }
        else if (_currentLevel == 1) // Taş
        {
            if (deltaStone > 0) Agent.AddReward(0.01f * deltaStone);
        }
        else if (_currentLevel == 2) // Et (TEŞVİK ARTIRILDI)
        {
            // Et toplamak bu levelde çok daha değerli olsun ki dikkati dağılmasın
            if (deltaMeat > 0) Agent.AddReward(0.05f * deltaMeat);
        }
        else // Serbest Piyasa
        {
            float resourceReward = 0;
            if (deltaWood > 0) resourceReward += deltaWood;
            if (deltaStone > 0) resourceReward += deltaStone;
            if (deltaMeat > 0) resourceReward += deltaMeat;
            if (resourceReward > 0) Agent.AddReward(0.001f * resourceReward);
        }

        // ... (Bina ve Ünite ödülleri aynı kalsın - Özellikle Worker Teşviği Önemli) ...
        // İşçi basma ödülünü (0.5f) koruduğumuzdan emin ol (önceki adımda eklemiştik)

        // --- 3. ÜNİTE VE NÜFUS ÖDÜLÜ (Önceki Turn'den Hatırlatma) ---
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        int currentTotal = _world.Units.Count;

        if (currentTotal > _lastUnitCount)
        {
            // İşçi sayısı arttıysa ve level 2 ise bu harika bir şeydir!
            if (currentWorkers > 0) Agent.AddReward(0.5f);
        }
        _lastUnitCount = currentTotal;

        // Ceza (Hızlandırma)
        Agent.AddReward(-0.0005f);
    }
    private void EndGame(float reward)
    {
        if (Agent != null)
        {
            Agent.AddReward(reward);
            Agent.EndEpisode();
        }
    }

    // ... (GenerateRTSMap ve SetupBase fonksiyonlarınız aynı kalabilir) ...
    private void GenerateRTSMap()
    {
        // (Sizin düzelttiğiniz OccupantID = res.ID içeren kod buraya gelecek)
        // Kodu kısa tutmak için burayı atlıyorum, eski haliyle aynı kalabilir.
        // Sadece OccupantID satırının olduğundan emin olun.

        // Önceki turn'de düzelttiğimiz GenerateRTSMap kodunu buraya yapıştırın.
        int mapSize = 20;
        for (int x = 0; x < mapSize; x++)
        {
            for (int y = 0; y < mapSize; y++)
            {
                var node = _world.Map.Grid[x, y];
                node.Type = SimTileType.Grass;
                node.IsWalkable = true;
                node.OccupantID = -1;

                if (UnityEngine.Random.value < 0.1f)
                {
                    if (x < 5 && y < 5) continue;

                    var res = new SimResourceData { ID = _world.NextID(), GridPosition = new int2(x, y), AmountRemaining = 500 };
                    float r = UnityEngine.Random.value;
                    if (r < 0.33f) { res.Type = SimResourceType.Wood; node.Type = SimTileType.Forest; }
                    else if (r < 0.66f) { res.Type = SimResourceType.Stone; node.Type = SimTileType.Stone; }
                    else { res.Type = SimResourceType.Meat; }

                    _world.Resources.Add(res.ID, res);
                    node.OccupantID = res.ID; // KRİTİK SATIR
                    node.IsWalkable = false;
                }
            }
        }
    }

    private void SetupBase(int playerID, int2 pos)
    {
        var baseB = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = playerID,
            Type = SimBuildingType.Base,
            GridPosition = pos,
            IsConstructed = true,
            ConstructionProgress = 100f,
            Health = SimConfig.BASE_MAX_HEALTH,
            MaxHealth = SimConfig.BASE_MAX_HEALTH
        };
        _world.Buildings.Add(baseB.ID, baseB);
        _world.Map.Grid[pos.x, pos.y].OccupantID = baseB.ID;
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;

        int2? workerPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
        if (workerPos.HasValue) _buildSys.SpawnUnit(workerPos.Value, SimUnitType.Worker, playerID);
    }
}