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
    public int DebugLevel = 4;

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

    // --- ÖDÜL TAKİBİ VE SAYAÇLAR ---
    private int _lastWood = 0;
    private int _lastStone = 0;
    private int _lastMeat = 0;

    // Ünite Sayaçları
    private int _lastWorkerCount = 0;
    private int _lastSoldierCount = 0;

    // Bina Sayaçları
    private int _lastHouseCount = 0;
    private int _lastFarmCount = 0;
    private int _lastWoodCutterCount = 0;
    private int _lastStonePitCount = 0;
    private int _lastBarracksCount = 0;
    private int _lastTowerCount = 0;

    private int _lastOtherBuildingsCount = 0; // Geriye uyumluluk için

    private float _decisionTimer = 0f;

    // --- CURRICULUM ---
    private float _currentLevel = 0;
    public float CurrentLevel => _currentLevel;

    private void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();
        if (Agent != null) Agent.Runner = this;

        Application.targetFrameRate = !TrainMode ? 60 : -1;
        Time.timeScale = !TrainMode ? 1.0f : 20.0f;

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
        CheckWinCondition();
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
            _currentLevel = DebugLevel;
            Debug.Log($"🎥 İZLEME MODU: Ajan Level {_currentLevel} Başlatıldı.");
        }

        MaxSteps = _currentLevel == 2 ? 2500 : (_currentLevel < 2 ? 1500 : 5000);

        if (!TrainMode) Debug.Log($"🔄 SİMÜLASYON BAŞLADI | Ders: {_currentLevel}");

        _world = new SimWorldState(20, 20);
        GenerateRTSMap();

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        // --- KAYNAK AYARLAMASI ---
        int startWood = 2000;
        int startStone = 2000;
        int startMeat = 2000;

        // DERS 0: Odun
        if (_currentLevel == 0) startWood = 0;
        // DERS 1: Taş
        else if (_currentLevel == 1) startStone = 0;
        // DERS 2: Et (Yatırım)
        else if (_currentLevel == 2) startMeat = 300;
        // DERS 4: General (Savaş)
        else if (_currentLevel >= 4)
        {
            // DÜZELTME: 6 İşçi (1500) + 3 Asker (600) = 2100 Et lazım.
            // 2000 Et ile başlarsa tıkanıyor. Bu yüzden biraz fazlasını veriyoruz.
            startMeat = 2500;
        }

        _resSys.AddResource(1, SimResourceType.Wood, startWood);
        _resSys.AddResource(1, SimResourceType.Stone, startStone);
        _resSys.AddResource(1, SimResourceType.Meat, startMeat);

        _resSys.IncreaseMaxPopulation(1, 10);

        SetupBase(1, new int2(2, 2));

        // Değişkenleri Sıfırla
        _currentStep = 0;
        _isInitialized = true;
        _decisionTimer = 0f;

        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood;
        _lastStone = p.Stone;
        _lastMeat = p.Meat;

        _lastWorkerCount = 1;
        _lastSoldierCount = 0;

        // Bina sayaçlarını sıfırla
        _lastHouseCount = 0;
        _lastFarmCount = 0;
        _lastWoodCutterCount = 0;
        _lastStonePitCount = 0;
        _lastBarracksCount = 0;
        _lastTowerCount = 0;
        _lastOtherBuildingsCount = 1;

        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void CheckWinCondition()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);

        if (_currentLevel == 0 && player.Wood >= 300) { EndGame(1.0f); return; }
        if (_currentLevel == 1 && player.Stone >= 200) { EndGame(1.0f); return; }
        if (_currentLevel == 2 && player.Meat >= 600) { EndGame(2.0f); return; }

        if (_currentLevel == 3)
        {
            bool hasBarracks = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Barracks && b.IsConstructed);
            if (hasBarracks) { EndGame(2.0f); return; }
        }

        if (_currentLevel >= 4)
        {
            int soldierCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier);
            if (soldierCount >= 3) { EndGame(2.0f); return; }
        }

        // Kaybetme: Hiçbir ünite kalmadıysa
        int totalUnits = _world.Units.Values.Count(u => u.PlayerID == 1);
        if (totalUnits == 0 && _currentStep > 10)
        {
            EndGame(-1.0f);
        }
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null) return;

        // 1. KAYNAK TOPLAMA (Savaş Ekonomisi Mantığı)
        int deltaWood = player.Wood - _lastWood;
        int deltaStone = player.Stone - _lastStone;
        int deltaMeat = player.Meat - _lastMeat;

        float resMultiplier = 0.0001f; // Normalde çok düşük

        // YENİ: Eğer asker sayısı 3'ten azsa ve paramız yoksa, kaynak toplamak çok değerlidir!
        int currentSoldiers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
        if (currentSoldiers < 3 && player.Meat < 300)
        {
            // "Asker basmam lazım ama param yok" durumu
            if (deltaMeat > 0) Agent.AddReward(deltaMeat * 0.01f); // 100 kat daha değerli ödül!
        }
        else
        {
            if (deltaWood > 0) Agent.AddReward(deltaWood * resMultiplier);
            if (deltaStone > 0) Agent.AddReward(deltaStone * resMultiplier);
            if (deltaMeat > 0) Agent.AddReward(deltaMeat * resMultiplier);
        }

        _lastWood = player.Wood;
        _lastStone = player.Stone;
        _lastMeat = player.Meat;

        // 2. İNŞAAT ÖDÜLLERİ
        float rewardPerResource = 0.002f;
        float barracksBonus = 15.0f;
        float houseCrisisBonus = 0.5f;

        int curHouse = 0, curFarm = 0, curWood = 0, curStone = 0, curBarracks = 0, curTower = 0;

        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 1 && b.IsConstructed)
            {
                switch (b.Type)
                {
                    case SimBuildingType.House: curHouse++; break;
                    case SimBuildingType.Farm: curFarm++; break;
                    case SimBuildingType.WoodCutter: curWood++; break;
                    case SimBuildingType.StonePit: curStone++; break;
                    case SimBuildingType.Barracks: curBarracks++; break;
                    case SimBuildingType.Tower: curTower++; break;
                }
            }
        }

        // EV (House)
        if (curHouse > _lastHouseCount)
        {
            float cost = SimConfig.HOUSE_COST_WOOD + SimConfig.HOUSE_COST_STONE + SimConfig.HOUSE_COST_MEAT;
            float houseReward = cost * rewardPerResource;

            // Popülasyon %70 dolunca ev bonusu ver (Önceki 0.8 bazen geç kalıyordu)
            if ((float)player.CurrentPopulation / player.MaxPopulation >= 0.7f)
            {
                houseReward += houseCrisisBonus;
            }
            Agent.AddReward(houseReward * (curHouse - _lastHouseCount));
        }

        if (curFarm > _lastFarmCount)
        {
            float cost = SimConfig.FARM_COST_WOOD + SimConfig.FARM_COST_STONE + SimConfig.FARM_COST_MEAT;
            Agent.AddReward((cost * rewardPerResource) * (curFarm - _lastFarmCount));
        }
        if (curWood > _lastWoodCutterCount)
        {
            float cost = SimConfig.WOODCUTTER_COST_WOOD + SimConfig.WOODCUTTER_COST_STONE + SimConfig.WOODCUTTER_COST_MEAT;
            Agent.AddReward((cost * rewardPerResource) * (curWood - _lastWoodCutterCount));
        }
        if (curStone > _lastStonePitCount)
        {
            float cost = SimConfig.STONEPIT_COST_WOOD + SimConfig.STONEPIT_COST_STONE + SimConfig.STONEPIT_COST_MEAT;
            Agent.AddReward((cost * rewardPerResource) * (curStone - _lastStonePitCount));
        }
        if (curTower > _lastTowerCount)
        {
            float cost = SimConfig.TOWER_COST_WOOD + SimConfig.TOWER_COST_STONE + SimConfig.TOWER_COST_MEAT;
            Agent.AddReward((cost * rewardPerResource) * (curTower - _lastTowerCount));
        }

        // KIŞLA (Jackpot)
        if (curBarracks > _lastBarracksCount)
        {
            float cost = SimConfig.BARRACKS_COST_WOOD + SimConfig.BARRACKS_COST_STONE + SimConfig.BARRACKS_COST_MEAT;

            // YENİ MANTIK: Eğer bu inşa edilen İLK kışla ise Bonus ver.
            // 2., 3. kışlalar sadece maliyet ödülü alır (Bonus yok).
            // Bu sayede ajan "Et bitti bari kışla yapayım" demez, et toplamaya gider.

            float currentBonus = 0f;
            if (curBarracks == 1) // Sadece ilkinde bonus!
            {
                currentBonus = barracksBonus; // +15.0 Puan
            }

            float totalReward = (cost * rewardPerResource) + currentBonus;
            Agent.AddReward(totalReward * (curBarracks - _lastBarracksCount));
        }

        _lastHouseCount = curHouse;
        _lastFarmCount = curFarm;
        _lastWoodCutterCount = curWood;
        _lastStonePitCount = curStone;
        _lastBarracksCount = curBarracks;
        _lastTowerCount = curTower;

        // 3. ÜNİTE ÜRETİMİ
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        int idleWorkerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);

        // İşçi Kotası (Anti-Spam)
        if (currentWorkers > _lastWorkerCount)
        {
            int workerHardCap = (_currentLevel >= 4) ? 6 : 20;
            if (currentWorkers > workerHardCap)
            {
                Agent.AddReward(-1.0f); // Kota aşımı cezası
            }
            else if (idleWorkerCount <= 2)
            {
                Agent.AddReward(1.0f); // İhtiyaç varsa ödül
            }
        }
        _lastWorkerCount = currentWorkers;

        // Asker Ödülü (Süper Yüksek)
        if (currentSoldiers > _lastSoldierCount)
        {
            int diff = currentSoldiers - _lastSoldierCount;
            Agent.AddReward(5.0f * diff);
        }
        _lastSoldierCount = currentSoldiers;

        // Tembellik Cezası
        if (idleWorkerCount > 0)
        {
            Agent.AddReward(-0.001f * idleWorkerCount);
        }

        // Varoluş Cezası
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

    private void GenerateRTSMap()
    {
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
                    node.OccupantID = res.ID;
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