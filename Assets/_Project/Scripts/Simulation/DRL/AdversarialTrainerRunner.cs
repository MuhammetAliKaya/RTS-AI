using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;

public enum AIDifficulty
{
    Passive,
    Defensive,
    Aggressive
}

public class AdversarialTrainerRunner : MonoBehaviour
{
    [Header("Ayarlar")]
    public RTSAgent Agent;
    public int MapSize = 32;
    public int MaxSteps = 5000;

    public string AllowedAgentName = "AdversarialTrainerRunner";

    [Tooltip("Harita rastgeleliğini kontrol etmek için kullanılır. 0 veya negatif ise rastgele seed oluşturulur.")]
    public int mapSeed = 12345;

    [Tooltip("mapSeed 0 veya negatif olduğunda, o anki zamana göre rastgele bir seed oluşturur.")]
    public bool useRandomSeed = true;

    [Header("Zaman Ayarları")]
    [Tooltip("Eğitim için True, Demo kaydı için False yap!")]
    public bool IsTrainingMode = false;

    public float _simStepSize = 0.0025f;
    [Range(1f, 10000f)]
    public float _simStepCountPerFrame = 1f;

    private float _timer = 0f;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Rakip Ayarları")]
    public bool UseMacroAI = true;
    public AIDifficulty EnemyDifficulty = AIDifficulty.Passive;

    // SİSTEMLER
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    // TAKİP DEĞİŞKENLERİ
    private int _lastSoldiers = 0;


    private int _lastEnemyUnitCount = 0;
    private int _lastEnemyBuildingCount = 0;
    private float _lastEnemyBaseHealth = 1000f;
    private int _lastWood = 0;
    private int _lastMeat = 0;
    private int _lastStone = 0;
    private int _lastWorkerCount = 0;

    private SimpleMacroAI _enemyAI;
    private int _currentStep = 0;
    private bool _gameEnded = false;

    private float decisionTimer = 0f;

    private int _agentDecisionCounter = 0;
    private int stepCount = 0;

    // Kaç adımda bir karar versin? (Örn: 4 adımda bir. 4 * 0.25 = 1 saniyede bir karar)
    private const int AGENT_DECISION_INTERVAL = 4;

    void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();
        if (IsTrainingMode)
        {
            Application.targetFrameRate = 60; // Maksimum hız
        }
        else
        {
            Application.targetFrameRate = 60;
        }
        ResetSimulation();
    }

    void Update()
    {
        if (_gameEnded) return;
        // Eğer eğitim modundaysak ve hızlanmak istiyorsak
        if (IsTrainingMode)
        {
            // Bir karede 10 simülasyon adımı birden at
            for (int i = 0; i < _simStepCountPerFrame; i++)
            {
                // dt her zaman sabit olmalı(örn: 0.1f veya 0.02f)
                SimulationStep(_simStepSize);
            }
        }
        else
        {
            // Normal oyun modunda gerçek zamanlı akış
            SimulationStep(_simStepSize);
        }
    }

    // Artık dt parametre olarak geliyor
    public void SimulationStep(float dt)
    {

        // 1. Düşman AI Hamlesi
        if (_enemyAI != null)
        {
            _enemyAI.Update(dt);
        }

        if (stepCount % 1 == 0)
        {
            // 1. Kullanıcıdan gelen bir emir var mı? (Tıklama önceliği)
            bool hasUserOverride = (Agent != null && Agent._overrideActionType != 0);

            // 2. Son 4 işlem boş (0) mı? (Spam kontrolü)
            bool isSpammingWait = (Agent != null && Agent.IsIdleSpamming());

            // MANTIK:
            // Kullanıcı bir şeye bastıysa (hasUserOverride) -> İSTEK AT.
            // VEYA
            // Kullanıcı basmadı ama ajan spam yapmıyor (hala hareketli veya yeni durdu) -> İSTEK AT.
            if (hasUserOverride || !isSpammingWait)
            {
                if (Agent != null)
                {
                    Agent.RequestDecision();
                    // Debug.Log("RequestDecision: Kayıt Alınıyor...");
                }
            }
        }

        _agentDecisionCounter++;
        if (_agentDecisionCounter >= AGENT_DECISION_INTERVAL && IsTrainingMode)
        {
            _agentDecisionCounter = 0;
            if (Agent != null && IsTrainingMode) Agent.RequestDecision();
        }

        // 3. Simülasyonu İlerlet
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);

        // 4. Birimleri güncelle
        if (_unitSys != null) _unitSys.UpdateAllUnits(dt);

        // Ödüller
        CalculateCombatRewards();
        CalculateEconomyRewards();
        ApplyIdlePenalty();

        // 4. Bitiş Kontrolü
        CheckGameResult();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            EndGame(0);
        }
        stepCount++;
    }

    private void CalculateEconomyRewards()
    {
        if (Agent == null || EnemyDifficulty != AIDifficulty.Passive) return;

        var myPlayer = _world.Players[1];
        int woodDelta = myPlayer.Wood - _lastWood;
        int meatDelta = myPlayer.Meat - _lastMeat;
        int stoneDelta = myPlayer.Stone - _lastStone;

        // if (woodDelta > 0) Agent.AddReward(woodDelta * 0.01f);
        // if (meatDelta > 0) Agent.AddReward(meatDelta * 0.01f);
        // if (stoneDelta > 0) Agent.AddReward(stoneDelta * 0.01f);

        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        if (currentWorkers > _lastWorkerCount)
        {
            // Agent.AddReward(0.25f);
            Debug.Log("New worker " + currentWorkers);
        }


        _lastWood = myPlayer.Wood;
        _lastMeat = myPlayer.Meat;
        _lastStone = myPlayer.Stone;
        _lastWorkerCount = currentWorkers;
    }

    private void CalculateCombatRewards()
    {
        if (Agent == null) return;

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

        if (currentSoldiers > _lastSoldiers)
        {
            // Agent.AddReward(0.5f);
        }

        if (currentEnemyUnits < _lastEnemyUnitCount)
        {
            int killCount = _lastEnemyUnitCount - currentEnemyUnits;
            // Agent.AddReward(0.5f * killCount);
        }

        if (currentEnemyBuildings < _lastEnemyBuildingCount)
        {
            int destroyCount = _lastEnemyBuildingCount - currentEnemyBuildings;
            // Agent.AddReward(2.0f * destroyCount);
        }

        if (currentEnemyBaseHealth < _lastEnemyBaseHealth)
        {
            float damage = _lastEnemyBaseHealth - currentEnemyBaseHealth;
            // Agent.AddReward(damage * 0.001f);
        }
        _lastSoldiers = currentSoldiers;

        _lastEnemyUnitCount = currentEnemyUnits;
        _lastEnemyBuildingCount = currentEnemyBuildings;
        _lastEnemyBaseHealth = currentEnemyBaseHealth;
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;
        _timer = 0; // Timer'ı sıfırla

        float difficultyLevel = 0.0f;
        if (Academy.IsInitialized)
            difficultyLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("enemy_difficulty_level", 0.0f);
        if (EnemyDifficulty == AIDifficulty.Passive)
        {
            difficultyLevel = 0;
        }
        else if (EnemyDifficulty == AIDifficulty.Defensive)
        {
            difficultyLevel = 0.5f;
        }
        else
        {
            difficultyLevel = 2;
        }

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

        if (_world.Players.ContainsKey(1))
        {
            var p1 = _world.Players[1];
            p1.Wood = 500; p1.Stone = 500; p1.Meat = 500; p1.MaxPopulation = 20;
            _lastWood = 500; _lastStone = 500; _lastMeat = 500; _lastWorkerCount = 0;
        }
        if (!_world.Players.ContainsKey(2))
        {
            _world.Players.Add(2, new SimPlayerData { PlayerID = 2, Wood = 500, Stone = 500, Meat = 500, MaxPopulation = 20 });
        }

        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        if (UseMacroAI) _enemyAI = new SimpleMacroAI(_world, 2, difficultyLevel);
        else _enemyAI = null;

        if (Visualizer != null) Visualizer.Initialize(_world);

        // Reset counters
        _lastSoldiers = 0;

        _lastEnemyUnitCount = 0;
        _lastEnemyBuildingCount = 1;
        _lastEnemyBaseHealth = 1000f;
    }

    private void GenerateMap(int seed) // Metot artık bir seed parametresi alıyor
    {
        // Harita boyutunu alma kodu (eğer MapSize bir alan/özellik ise)
        int MapSize = _world.Map.Grid.GetLength(0);

        // 1. Haritayı Başlatma (Rastgelelik içermez)
        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 0; y < MapSize; y++)
            {
                _world.Map.Grid[x, y] = new SimMapNode
                {
                    x = x,
                    y = y,
                    Type = SimTileType.Grass,
                    IsWalkable = true,
                    OccupantID = -1
                };
            }
        }

        // 2. Kaynak Yerleştirme (Seed Tabanlı Deterministic Rastgelelik)

        // Harita kaynaklarının rastgeleliğini kontrol etmek için seed'i ayarla
        UnityEngine.Random.InitState(seed); //

        int resourceCount = 45;
        for (int i = 0; i < resourceCount; i++)
        {
            // Seed ayarlandığı için bu Range ve value çağrıları artık deterministiktir.
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

        // Başlangıç işçileri
        int start_workercount = 1;
        for (int i = 0; i < start_workercount; i++)
        {
            int2? spawnPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
            if (spawnPos.HasValue)
            {
                var unit = new SimUnitData
                {
                    ID = _world.NextID(),
                    PlayerID = pid,
                    UnitType = SimUnitType.Worker,
                    GridPosition = spawnPos.Value,
                    Health = 50,
                    MaxHealth = 50,
                    State = SimTaskType.Idle,
                    MoveSpeed = 5.0f
                };
                _world.Units.Add(unit.ID, unit);
                _world.Map.Grid[spawnPos.Value.x, spawnPos.Value.y].OccupantID = unit.ID;
                SimResourceSystem.ModifyPopulation(_world, pid, 1);
            }
        }
    }

    private void ApplyIdlePenalty()
    {
        if (Agent == null) return;

        int idleCount = _world.Units.Values.Count(u =>
            u.PlayerID == 1 &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        );

        if (idleCount > 0)
        {
            Agent.AddReward(idleCount * -0.00005f);
        }
    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (myBase == null) // Kaybettik
        {
            EndGame(-2.0f);
        }
        else if (enemyBase == null) // Kazandık
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 4.0f;
            Debug.Log("speedBonus" + (speedBonus + 2f));
            EndGame(2.0f + speedBonus);
        }
    }

    private void EndGame(float reward)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        if (Agent != null)
        {
            if (reward == 0 && EnemyDifficulty == AIDifficulty.Passive) reward = -1.0f;

            // Agent.AddReward(reward);
            Agent.EndEpisode();
        }
    }
}