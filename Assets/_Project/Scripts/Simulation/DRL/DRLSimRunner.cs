using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class DRLSimRunner : MonoBehaviour
{
    [Header("AI Ayarları")]
    public RTSAgent Agent;
    public bool TrainMode = true;
    public int MaxSteps = 5000;

    [Header("Simülasyon Hızı")]
    public int StepsPerFrame = 20;

    // --- INSTANCE SİSTEMLER ---
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    private int _currentStep = 0;
    private bool _isInitialized = false;

    // --- ÖDÜL TAKİBİ ---
    private int _lastWood = 0;
    private int _lastUnitCount = 0;
    private int _lastBuildingCount = 0;

    private void Start()
    {
        if (Agent == null) Agent = FindObjectOfType<RTSAgent>();
        Agent.Runner = this;

        if (TrainMode)
        {
            Time.timeScale = 100.0f; // Zamanı 50x hızlandır
            Application.targetFrameRate = -1;
        }

        ResetSimulation();
    }

    private void FixedUpdate()
    {
        if (!_isInitialized) return;

        int loopCount = TrainMode ? StepsPerFrame : 1;

        for (int i = 0; i < loopCount; i++)
        {
            StepSimulation();

            if (_currentStep >= MaxSteps)
            {
                EndGame(0); // Zaman doldu
                break;
            }
        }
    }

    private void StepSimulation()
    {
        float dt = 0.1f;

        // Sistemleri Güncelle
        _buildSys.UpdateAllBuildings(dt);

        var unitIds = _world.Units.Keys.ToList();
        foreach (var uid in unitIds)
        {
            if (_world.Units.TryGetValue(uid, out SimUnitData unit))
            {
                _unitSys.UpdateUnit(unit, dt);
            }
        }

        // --- DETAYLI LOGLAMA (Her 100 adımda bir) ---
        if (_currentStep % 100 == 0)
        {
            var p1 = SimResourceSystem.GetPlayer(_world, 1);
            if (p1 != null)
            {
                var stats = Unity.MLAgents.Academy.Instance.StatsRecorder;
                stats.Add("Economy/Wood", p1.Wood);
                stats.Add("Units/Worker_Count", _world.Units.Values.Count(u => u.UnitType == SimUnitType.Worker));
                stats.Add("Units/Soldier_Count", _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier));
            }
        }

        // Ödül ve Bitiş Kontrolü
        CalculateDenseRewards();
        CheckWinCondition();
        _currentStep++;
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null) return;

        // 1. KAYNAK ÖDÜLÜ
        int currentWood = player.Wood + player.Stone + player.Meat;
        int deltaRes = currentWood - _lastWood;
        if (deltaRes > 0) Agent.AddReward(0.001f * deltaRes);
        _lastWood = currentWood;

        // 2. BİNA ÖDÜLÜ
        int currentBuildings = _world.Buildings.Values.Count(b => b.PlayerID == 1);
        if (currentBuildings > _lastBuildingCount) Agent.AddReward(0.1f);
        _lastBuildingCount = currentBuildings;

        // 3. ÜNİTE ÖDÜLÜ
        int currentUnits = _world.Units.Values.Count(u => u.PlayerID == 1);
        if (currentUnits > _lastUnitCount) Agent.AddReward(0.2f);
        _lastUnitCount = currentUnits;

        // 4. VAROLMA CEZASI (Hızlandırma)
        Agent.AddReward(-0.0001f);
    }

    public void ResetSimulation()
    {
        _world = new SimWorldState(20, 20);
        SimGridSystem.GenerateMazeMap(_world.Map);

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        // Başlangıç Kaynakları
        _resSys.AddResource(1, SimResourceType.Wood, 1000);
        _resSys.AddResource(1, SimResourceType.Meat, 500);
        _resSys.AddResource(1, SimResourceType.Stone, 200);
        _resSys.IncreaseMaxPopulation(1, 10);

        // Başlangıç İşçisi
        int2 startPos = new int2(2, 2);
        if (!_gridSys.IsWalkable(startPos))
        {
            var safe = SimGridSystem.FindWalkableNeighbor(_world, startPos);
            if (safe.HasValue) startPos = safe.Value;
        }

        // SpawnUnit Wrapper üzerinden
        _buildSys.SpawnUnit(startPos, SimUnitType.Worker, 1);

        _currentStep = 0;
        _isInitialized = true;

        // Ödül Değerlerini Sıfırla
        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood + p.Stone + p.Meat;
        _lastUnitCount = 1;
        _lastBuildingCount = 0;
    }

    private void CheckWinCondition()
    {
        int soldierCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier);

        if (soldierCount >= 5)
        {
            EndGame(2.0f); // KAZANDIN
            return;
        }

        int workerCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Worker);
        if (workerCount == 0 && soldierCount == 0 && _currentStep > 10)
        {
            EndGame(-1.0f); // KAYBETTİN
            return;
        }
    }

    private void EndGame(float reward)
    {
        if (reward > 1.0f) Debug.Log($"<color=green>🎉 KAZANDI! Adım: {_currentStep}</color>");
        Agent.AddReward(reward);
        Agent.EndEpisode();
    }
}