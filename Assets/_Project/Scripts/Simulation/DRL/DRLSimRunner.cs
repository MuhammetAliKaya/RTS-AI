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
    [Tooltip("TİKİ KALDIRIRSAN: Detaylı Log + Yavaş Mod.\nTİKLERSEN: Hızlı Eğitim Modu.")]
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

    // --- TAKİP ---
    private int _lastWood = 0;
    private int _lastUnitCount = 0;
    private int _lastBuildingCount = 0;
    private float _decisionTimer = 0f;

    private void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();
        if (Agent != null) Agent.Runner = this;

        // İzleme modunda FPS kilidini aç, Eğitimde kaldır
        Application.targetFrameRate = !TrainMode ? 60 : -1;
        Time.timeScale = !TrainMode ? 1.0f : 100.0f;

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

        // 1. Karar (İzleme modunda her 0.1 saniyede bir karar versin, daha okunabilir olur)
        bool requestDecision = true;
        if (!TrainMode)
        {
            _decisionTimer += dt;
            if (_decisionTimer < 0.1f) requestDecision = false;
            else _decisionTimer = 0f;
        }

        if (requestDecision && Agent != null) Agent.RequestDecision();

        // 2. Simülasyonu İlerlet
        _buildSys.UpdateAllBuildings(dt);

        var unitIds = _world.Units.Keys.ToList();
        foreach (var uid in unitIds)
        {
            if (_world.Units.TryGetValue(uid, out SimUnitData unit))
                _unitSys.UpdateUnit(unit, dt);
        }

        // --- DETAYLI LOG (SADECE İZLEME MODU) ---
        if (!TrainMode)
        {
            var p = SimResourceSystem.GetPlayer(_world, 1);
            Debug.Log($"⏱️ <b>[SIM STEP {_currentStep}]</b> Wood: {p.Wood} | Meat: {p.Meat} | Stone: {p.Stone} | Pop: {p.CurrentPopulation}/{p.MaxPopulation}");
        }

        // 3. Ödül ve Bitiş
        CalculateDenseRewards();
        CheckWinCondition();
        _currentStep++;

        if (_currentStep >= MaxSteps)
        {
            if (!TrainMode) Debug.Log("⌛ <b>ZAMAN DOLDU! Restart atılıyor...</b>");
            EndGame(0);
        }
    }

    private void CalculateDenseRewards()
    {
        var player = SimResourceSystem.GetPlayer(_world, 1);
        if (player == null) return;

        // 1. KAYNAK ÖDÜLÜ (Biraz artırıldı ve normalize edildi)
        // Eskiden: 0.001f * delta
        // Şimdi: 0.005f * delta (Kaynak toplamak daha tatlı olsun)
        int currentResources = player.Wood + player.Stone + player.Meat;
        int deltaRes = currentResources - _lastWood; // _lastWood ismini _lastResources olarak düşün
        if (deltaRes > 0)
        {
            Agent.AddReward(0.005f * deltaRes);
        }
        _lastWood = currentResources;

        // 2. BİNA ÖDÜLÜ (Dengeli)
        // Her bina 0.5 puan (Eskiden 0.1 idi, çok düşüktü)
        int currentBuildings = _world.Buildings.Values.Count(b => b.PlayerID == 1);
        if (currentBuildings > _lastBuildingCount)
        {
            Agent.AddReward(0.5f);
            // Ekstra teşvik: Eğer yapılan bina BASE değilse ve ilk defa yapılıyorsa bonus verilebilir
        }
        _lastBuildingCount = currentBuildings;

        // 3. ÜNİTE ÖDÜLÜ (Stratejik)
        // Her ünite 0.3 puan (Eskiden 0.2 idi)
        int currentUnits = _world.Units.Values.Count(u => u.PlayerID == 1);
        if (currentUnits > _lastUnitCount)
        {
            Agent.AddReward(0.3f);
        }
        _lastUnitCount = currentUnits;

        // 4. VAROLMA CEZASI (ARTIRILDI!)
        // Eskiden: -0.0001f (Çok azdı)
        // Şimdi: -0.001f (10 kat artırıldı)
        // Ajan artık "Boş durursam puanım eriyor, hemen bir şeyler yapmalıyım!" diyecek.
        Agent.AddReward(-0.001f);
    }

    public void ResetSimulation()
    {
        if (!TrainMode) Debug.Log("🔄 <b>SİMÜLASYON SIFIRLANDI (RESTART)</b>");

        _world = new SimWorldState(20, 20);
        GenerateRTSMap();

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        _resSys.AddResource(1, SimResourceType.Wood, 1000);
        _resSys.AddResource(1, SimResourceType.Meat, 500);
        _resSys.AddResource(1, SimResourceType.Stone, 200);
        _resSys.IncreaseMaxPopulation(1, 10);

        SetupBase(1, new int2(2, 2));

        _currentStep = 0;
        _isInitialized = true;
        _decisionTimer = 0f;

        var p = SimResourceSystem.GetPlayer(_world, 1);
        _lastWood = p.Wood + p.Stone + p.Meat;
        _lastUnitCount = 1;
        _lastBuildingCount = 1;

        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void CheckWinCondition()
    {
        int soldierCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier);
        if (soldierCount >= 5)
        {
            if (!TrainMode) Debug.Log("🏆 <b>KAZANDIN! (5 Asker Üretildi) - Restart...</b>");
            EndGame(2.0f);
            return;
        }

        int workerCount = _world.Units.Values.Count(u => u.UnitType == SimUnitType.Worker);
        if (workerCount == 0 && soldierCount == 0 && _currentStep > 10)
        {
            if (!TrainMode) Debug.Log("💀 <b>KAYBETTİN! (Birim Kalmadı) - Restart...</b>");
            EndGame(-1.0f);
            return;
        }
    }

    private void EndGame(float reward)
    {
        if (Agent != null)
        {
            Agent.AddReward(reward);
            // EndEpisode çağrısı ML-Agents tarafından otomatik olarak OnEpisodeBegin'i tetikler.
            // OnEpisodeBegin de ResetSimulation'ı çağırır. Yani döngü sonsuzdur.
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