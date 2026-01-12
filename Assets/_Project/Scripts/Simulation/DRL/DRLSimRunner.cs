using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;
using RTS.Simulation.AI;

public class DRLSimRunner : MonoBehaviour
{
    [Header("AI Ayarları")]
    public RTSAgent Agent;
    public bool TrainMode = true;
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

    // --- ÖDÜL SAYAÇLARI ---
    private int _lastWood = 0;
    private int _lastStone = 0;
    private int _lastMeat = 0;
    private int _lastWorkerCount = 0;
    private int _lastSoldierCount = 0;
    private int _lastBarracksCount = 0;

    private float _decisionTimer = 0f;
    private float _currentLevel = 0;

    private IMacroAI _enemyAI; // Rakip bot (Player 2)

    private void Start()
    {
        // Ajan ve Runner bağlantısını kur
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();
        if (Agent != null) Agent.Runner = this;

        // Performans ayarları
        Application.targetFrameRate = !TrainMode ? 60 : -1;
        Time.timeScale = !TrainMode ? 1.0f : 20.0f;

        ResetSimulation();
    }

    private void Update()
    {
        // EĞİTİM MODUNDA DA ÇALIŞMALI: ManualUpdate simülasyonun kalbidir.
        ManualUpdate();
    }

    public void ManualUpdate()
    {
        if (!_isInitialized) return;

        // Eğitimde sabit, normal oyunda değişken delta time
        float dt = TrainMode ? 0.1f : Time.deltaTime;

        // 1. DÜŞMANI (BOT) GÜNCELLE
        if (_enemyAI != null)
        {
            _enemyAI.Update(dt);
        }

        // 2. KARAR MEKANİZMASI (Sadece monolitik tek beyin için)
        bool requestDecision = true;
        if (!TrainMode)
        {
            _decisionTimer += dt;
            if (_decisionTimer < 0.1f) requestDecision = false;
            else _decisionTimer = 0f;
        }

        if (requestDecision && Agent != null)
        {
            Agent.RequestDecision();
        }

        // 3. SİSTEMLERİ İLERLET
        _buildSys.UpdateAllBuildings(dt);
        _unitSys.UpdateAllUnits(dt); // Instance tabanlı UpdateAllUnits kullanılıyor

        // 4. ANALİZ VE BİTİŞ KONTROLÜ
        CalculateDenseRewards();
        CheckWinCondition();

        _currentStep++;
        if (_currentStep >= MaxSteps)
        {
            EndGame(0); // Zaman dolduğunda beraberlik/0 ödül
        }
    }

    public void ResetSimulation()
    {
        // A. SİSTEMLERİ VE DÜNYAYI BAŞLAT
        _world = new SimWorldState(20, 20);
        if (!_world.Players.ContainsKey(2))
        {
            _world.Players.Add(2, new SimPlayerData { PlayerID = 2, MaxPopulation = 0 });
        }
        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        // B. KRİTİK: NÜFUS LİMİTLERİNİ İŞÇİLERDEN ÖNCE AYARLA
        // Bu adım eksik olursa SpawnUnit "yer yok" diyerek işçi oluşturmaz.
        _resSys.IncreaseMaxPopulation(1, 10);
        _resSys.IncreaseMaxPopulation(2, 10);

        // C. BAŞLANGIÇ KAYNAKLARI (Her iki taraf için)
        _resSys.AddResource(1, SimResourceType.Wood, 500);
        _resSys.AddResource(1, SimResourceType.Stone, 500);
        _resSys.AddResource(1, SimResourceType.Meat, 500);

        _resSys.AddResource(2, SimResourceType.Wood, 500);
        _resSys.AddResource(2, SimResourceType.Stone, 500);
        _resSys.AddResource(2, SimResourceType.Meat, 500);

        // D. HARİTAYI OLUŞTUR
        GenerateRTSMap();

        // E. ÜSLERİ VE İLK İŞÇİLERİ KUR
        SetupBase(1, new int2(2, 2));         // Oyuncu (Player 1)
        SetupEnemyBase(new int2(17, 17));      // Bot (Player 2)

        // F. BOT VE AJAN KURULUMU
        if (TrainMode && Academy.IsInitialized)
        {
            _currentLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("rts_level", 4.0f);
        }

        // Botu (SimpleMacroAI) Constructor ile oluşturuyoruz
        _enemyAI = new SimpleMacroAI(_world, 2, _currentLevel);

        if (Agent != null)
        {
            Agent.Setup(_world, _gridSys, _unitSys, _buildSys);
        }

        // G. SAYAÇLARI VE DURUMU SIFIRLA
        _currentStep = 0;
        _isInitialized = true;
        ResetCounters();

        // Görselleştirme
        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void ResetCounters()
    {
        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood;
        _lastStone = p.Stone;
        _lastMeat = p.Meat;
        _lastWorkerCount = 1;
        _lastSoldierCount = 0;
        _lastBarracksCount = 0;
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null || Agent == null) return;

        // 1. Kaynak Toplama Ödülü (0.0001)
        int deltaWood = player.Wood - _lastWood;
        int deltaStone = player.Stone - _lastStone;
        int deltaMeat = player.Meat - _lastMeat;

        if (deltaWood > 0) Agent.AddReward(deltaWood * 0.0001f);
        if (deltaStone > 0) Agent.AddReward(deltaStone * 0.0001f);
        if (deltaMeat > 0) Agent.AddReward(deltaMeat * 0.0001f);

        _lastWood = player.Wood; _lastStone = player.Stone; _lastMeat = player.Meat;

        // 2. Birim ve Bina Üretimi
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        if (currentWorkers > _lastWorkerCount) Agent.AddReward(0.1f);
        _lastWorkerCount = currentWorkers;

        int currentSoldiers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
        if (currentSoldiers > _lastSoldierCount) Agent.AddReward(0.1f);
        _lastSoldierCount = currentSoldiers;

        int barracksCount = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Barracks && b.IsConstructed);
        if (barracksCount > _lastBarracksCount) Agent.AddReward(2.0f);
        _lastBarracksCount = barracksCount;

        // 3. Tembellik Cezası (-0.001)
        int idleWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        if (idleWorkers > 0) Agent.AddReward(-0.001f * idleWorkers);

        // Varoluş Cezası (Zamanla yarış için)
        Agent.AddReward(-0.0005f);
    }

    private void CheckWinCondition()
    {
        bool enemyBaseExists = _world.Buildings.Values.Any(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);
        if (!enemyBaseExists)
        {
            EndGame(50.0f); // Galibiyet
            return;
        }

        bool myBaseExists = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        if (!myBaseExists)
        {
            EndGame(-1.0f); // Mağlubiyet
            return;
        }
    }

    private void EndGame(float reward)
    {
        if (Agent != null)
        {
            // 1. Galibiyet durumunu belirle (1: Galibiyet, 0: Mağlubiyet/Beraberlik)
            float winStat = (reward > 0) ? 1.0f : 0.0f;

            // 2. TensorBoard'a gönder
            Unity.MLAgents.Academy.Instance.StatsRecorder.Add("Custom/WinRate", winStat);

            // Mevcut ödül kodları
            float finalReward = (reward > 0) ? 250.0f : (reward < 0 ? -250.0f : 0f);
            Agent.AddReward(finalReward);
            Agent.EndEpisode();
        }
        ResetSimulation();
    }

    private void GenerateRTSMap()
    {
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

        SpawnResourceOnMap(SimResourceType.Wood, 15);
        SpawnResourceOnMap(SimResourceType.Stone, 10);
        SpawnResourceOnMap(SimResourceType.Meat, 10);
    }

    private void SpawnResourceOnMap(SimResourceType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int rx = UnityEngine.Random.Range(2, 18);
            int ry = UnityEngine.Random.Range(2, 18);
            int2 pos = new int2(rx, ry);

            if (SimGridSystem.IsWalkable(_world, pos))
            {
                var res = new SimResourceData
                {
                    ID = _world.NextID(),
                    Type = type,
                    GridPosition = pos,
                    AmountRemaining = 500
                };
                _world.Resources.Add(res.ID, res);
                _world.Map.Grid[rx, ry].OccupantID = res.ID;
                _world.Map.Grid[rx, ry].IsWalkable = false;

                if (type == SimResourceType.Wood) _world.Map.Grid[rx, ry].Type = SimTileType.Forest;
                else if (type == SimResourceType.Stone) _world.Map.Grid[rx, ry].Type = SimTileType.Stone;
                else _world.Map.Grid[rx, ry].Type = SimTileType.MeatBush;
            }
        }
    }

    private void SetupBase(int playerID, int2 pos)
    {
        SpawnBuilding(playerID, SimBuildingType.Base, pos);
        int2? workerPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
        if (workerPos.HasValue)
            _buildSys.SpawnUnit(workerPos.Value, SimUnitType.Worker, playerID);
    }

    private void SetupEnemyBase(int2 pos)
    {
        SpawnBuilding(2, SimBuildingType.Base, pos);
        int2? workerPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
        if (workerPos.HasValue)
        {
            _buildSys.SpawnUnit(workerPos.Value, SimUnitType.Worker, 2);
        }
    }

    private SimBuildingData SpawnBuilding(int pid, SimBuildingType type, int2 pos)
    {
        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = pid,
            Type = type,
            GridPosition = pos,
            IsConstructed = true, // Başlangıç binaları kurulu gelmeli
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