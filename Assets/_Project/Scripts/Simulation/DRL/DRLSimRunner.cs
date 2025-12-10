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

    // --- SAYAÇLAR ---
    private int _lastWood = 0;
    private int _lastStone = 0;
    private int _lastMeat = 0;
    private int _lastWorkerCount = 0;
    private int _lastSoldierCount = 0;

    // Bina Sayaçları
    private int _lastHouseCount = 0;
    private int _lastBarracksCount = 0;
    // Diğer bina sayaçlarını basitleştirdik, gerekirse eklersin

    private float _decisionTimer = 0f;
    private float _currentLevel = 0;

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
        if (TrainMode && Academy.IsInitialized)
        {
            _currentLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("rts_level", 0.0f);
        }

        _world = new SimWorldState(20, 20);
        GenerateRTSMap();

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        // Rush eğitimi için bol kaynak verelim
        _resSys.AddResource(1, SimResourceType.Wood, 2000);
        _resSys.AddResource(1, SimResourceType.Stone, 500); // Kışla için lazım
        _resSys.AddResource(1, SimResourceType.Meat, 1000); // İşçi ve Asker için

        _resSys.IncreaseMaxPopulation(1, 10);
        SetupBase(1, new int2(2, 2));

        // Değişkenleri Sıfırla
        _currentStep = 0;
        _isInitialized = true;

        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood;
        _lastStone = p.Stone;
        _lastMeat = p.Meat;
        _lastWorkerCount = 1;
        _lastSoldierCount = 0;
        _lastHouseCount = 0;
        _lastBarracksCount = 0;

        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void CheckWinCondition()
    {
        // 1. KAZANMA KOŞULU: Düşman üssünü yıkmak
        bool enemyBaseExists = _world.Buildings.Values.Any(b => b.PlayerID != 1 && b.Type == SimBuildingType.Base);

        if (!enemyBaseExists)
        {
            // JACKPOT! Büyük ödül
            EndGame(50.0f);
            return;
        }

        // 2. KAYBETME KOŞULU: Kendi üssünün yıkılması
        bool myBaseExists = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        if (!myBaseExists)
        {
            EndGame(-1.0f);
            return;
        }
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null) return;

        // 1. KAYNAK TOPLAMA (Mevcut mantık)
        int deltaMeat = player.Meat - _lastMeat;
        if (deltaMeat > 0) Agent.AddReward(deltaMeat * 0.0001f);
        _lastMeat = player.Meat;

        // 2. BİNA TEŞVİĞİ (Mevcut mantık)
        int curBarracks = 0;
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 1 && b.IsConstructed && b.Type == SimBuildingType.Barracks)
                curBarracks++;
        }

        if (curBarracks > _lastBarracksCount)
        {
            float reward = (_lastBarracksCount == 0) ? 5.0f : 1.0f;
            Agent.AddReward(reward);
        }
        _lastBarracksCount = curBarracks;

        // 3. ASKER ÜRETİMİ (Mevcut mantık)
        int currentSoldiers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
        if (currentSoldiers > _lastSoldierCount)
        {
            Agent.AddReward(1.0f);
        }
        _lastSoldierCount = currentSoldiers;

        // --- YENİ EKLENEN KISIM ---
        // 4. TEMBELLİK CEZASI (IDLE WORKER PENALTY)
        int idleWorkers = _world.Units.Values.Count(u =>
            u.PlayerID == 1 &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        );

        if (idleWorkers > 0)
        {
            // Her boş işçi için adım başına ceza.
            // Örneğin 5 işçi boşsa: 5 * -0.002 = -0.01 ceza yer.
            // Bu, ajanı sürekli "Topla" (Action 7) emri vermeye zorlar.
            Agent.AddReward(-0.02f * idleWorkers);
        }
        // ---------------------------

        // Varoluş cezası (Hızlı bitirmeye zorla)
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

    // --- HARİTA OLUŞTURMA ---
    private void GenerateRTSMap()
    {
        // Basit bir harita: Düz çim, ortaya biraz engel
        for (int x = 0; x < 20; x++)
        {
            for (int y = 0; y < 20; y++)
            {
                var node = _world.Map.Grid[x, y];
                node.Type = SimTileType.Grass;
                node.IsWalkable = true;
                node.OccupantID = -1;
            }
        }

        // Düşman Üssü (Sağ Üst Köşe - Uzak)
        SetupEnemyBase(new int2(17, 17));
    }

    private void SetupBase(int playerID, int2 pos)
    {
        var baseB = SpawnBuilding(playerID, SimBuildingType.Base, pos);
        int2? workerPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
        if (workerPos.HasValue) _buildSys.SpawnUnit(workerPos.Value, SimUnitType.Worker, playerID);
    }

    private void SetupEnemyBase(int2 pos)
    {
        // Düşman üssü (Player 2) - Sadece bir bina, savunmasız (Başlangıç için)
        SpawnBuilding(2, SimBuildingType.Base, pos);
    }

    private SimBuildingData SpawnBuilding(int pid, SimBuildingType type, int2 pos)
    {
        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = pid,
            Type = type,
            GridPosition = pos,
            IsConstructed = true,
            ConstructionProgress = 100f,
            Health = 500,
            MaxHealth = 500
        };
        _world.Buildings.Add(b.ID, b);
        _world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        return b;
    }
}