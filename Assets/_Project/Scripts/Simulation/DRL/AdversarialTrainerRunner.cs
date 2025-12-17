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
    // DEĞİŞİKLİK 1: Artık RTSAgent yerine Orchestrator'ı tutuyoruz.
    public RTSOrchestrator Orchestrator;

    public int MapSize = 32;
    public int MaxSteps = 5000;

    public string AllowedAgentName = "AdvTrainerRunner";

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
    public AIDifficulty EnemyDifficulty = AIDifficulty.Passive;

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

    private SimpleMacroAI _enemyAI;
    private int _currentStep = 0;
    private bool _gameEnded = false;

    private int _agentDecisionCounter = 0;
    private const int AGENT_DECISION_INTERVAL = 4;

    void Start()
    {
        // DEĞİŞİKLİK 2: Orchestrator bileşenini child objeden buluyoruz.
        if (Orchestrator == null) Orchestrator = GetComponentInChildren<RTSOrchestrator>();

        // Start'ta reset çağırmak yerine sadece Orchestrator'ı kuruyoruz
        if (Orchestrator != null)
        {
            // Setup'a kendini (this) gönder
            Orchestrator.Setup(_world, _gridSys, _unitSys, _buildSys, this); // <--- DEĞİŞİKLİK
        }

        // İlk reset'i el ile çağırıyoruz. Sonrakiler Agent.OnEpisodeBegin'den gelecek.
        ResetSimulation();
    }

    void Update()
    {
        if (_gameEnded) return;

        if (IsTrainingMode)
        {
            // Eğitim hızı artırma (isteğe bağlı döngü sayısı artırılabilir)
            for (int i = 0; i < 1; i++) SimulationStep(_simStepSize);
        }
        else
        {
            SimulationStep(_simStepSize);
        }
    }

    public void SimulationStep(float dt)
    {
        // --- KİLİT MEKANİZMASI (LOCK) ---
        // Eğer Orchestrator şu an karar verme zincirindeyse (Idle değilse),
        // simülasyonu (hareketleri, inşaatları vb.) DURDUR ve bu frame'i pas geç.
        if (Orchestrator != null && Orchestrator.CurrentState != RTSOrchestrator.OrchestratorState.Idle)
        {
            return; // Dünyayı dondur, ajanın kararını bekle.
        }
        // --------------------------------

        // 1. Düşman AI Hamlesi
        if (_enemyAI != null) _enemyAI.Update(dt);

        // 2. Karar Mekanizması Tetikleyici
        _agentDecisionCounter++;
        if (_agentDecisionCounter >= AGENT_DECISION_INTERVAL && IsTrainingMode)
        {
            _agentDecisionCounter = 0;

            // Karar isteği gönderilir.
            // Bu çağrı Orchestrator'ın state'ini 'WaitingUnit' yapar.
            // Bir sonraki frame'de yukarıdaki "if" bloğuna takılacağı için simülasyon donacaktır.
            if (Orchestrator != null) Orchestrator.RequestFullDecision();
        }

        // 3. Simülasyonu İlerlet (Sadece IDLE iken buraya ulaşılır)
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);
        if (_unitSys != null) _unitSys.UpdateAllUnits(dt);

        // 4. Ödüller ve Bitiş Kontrolleri...
        CalculateCombatRewards();
        CalculateEconomyRewards();
        ApplyIdlePenalty();
        CheckGameResult();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            EndGame(0);
        }
    }

    private void CalculateEconomyRewards()
    {
        if (Orchestrator == null) return;

        // Kaynak toplama event tabanlı olduğu için buradaki check'i kaldırabiliriz
        // veya sadece "Toplam Kaynak Miktarı" artışına genel bir strateji ödülü verebiliriz.

        // İşçi Üretimi (Action Agent sorumluluğunda)
        var myPlayer = _world.Players[1];
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);

        if (currentWorkers > _lastWorkerCount)
        {
            // İşçi üretmek ActionAgent'ın kararıdır.
            Orchestrator.AddActionRewardOnly(0.1f);
        }
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

        // 1. Asker Sayısı Artışı (Ordu Kurma Teşviği)
        if (currentSoldiers > _lastSoldiers)
        {
            // Ordu kurmak stratejik bir karardır, Unit ve Action agentlar sorumludur.
            Orchestrator.AddUnitRewardOnly(0.02f);
            Orchestrator.AddActionRewardOnly(0.02f);
        }

        // 2. Düşman Öldürme (BÜYÜK ORTAK ÖDÜL)
        if (currentEnemyUnits < _lastEnemyUnitCount)
        {
            int killCount = _lastEnemyUnitCount - currentEnemyUnits;
            // Tüm ekip başardı!
            Orchestrator.AddGroupReward(0.5f * killCount);
        }

        // 3. Bina Yıkma (ÇOK BÜYÜK ORTAK ÖDÜL)
        if (currentEnemyBuildings < _lastEnemyBuildingCount)
        {
            int destroyCount = _lastEnemyBuildingCount - currentEnemyBuildings;
            Orchestrator.AddGroupReward(2.0f * destroyCount);
        }

        // 4. Kendi Üssümüz Hasar Alırsa (ORTAK CEZA)
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        if (myBase != null)
        {
            if (myBase.Health < _lastMyBaseHealth)
            {
                float damageTaken = _lastMyBaseHealth - myBase.Health;
                Orchestrator.AddGroupReward(-damageTaken * 0.005f);
            }
            _lastMyBaseHealth = myBase.Health;
        }

        _lastSoldiers = currentSoldiers;
        _lastEnemyUnitCount = currentEnemyUnits;
        _lastEnemyBuildingCount = currentEnemyBuildings;
        _lastEnemyBaseHealth = currentEnemyBaseHealth;
    }

    private void ApplyIdlePenalty()
    {
        if (Orchestrator == null) return;

        // Boşta duran işçiler UnitAgent'ın sorunudur. Onları seçip iş vermesi gerekir.
        int idleCount = _world.Units.Values.Count(u =>
            u.PlayerID == 1 &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        );

        if (idleCount > 0)
        {
            // Sadece UnitAgent'a ceza veriyoruz.
            Orchestrator.AddUnitRewardOnly(idleCount * -0.001f);
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
            float speedBonus = timeFactor * 15.0f;
            EndGame(-250.0f - speedBonus);
            Debug.Log("Game Lost");
        }
        else if (enemyBase == null) // Kazandık
        {
            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
            float speedBonus = timeFactor * 15.0f;
            Debug.Log("Game Won");
            EndGame(250.0f + speedBonus);
        }
    }

    private void EndGame(float reward)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        if (Orchestrator != null)
        {
            if (reward == 0 && EnemyDifficulty == AIDifficulty.Passive) reward = -1.0f;

            // Ödülü ver ve bölümü bitir.
            Orchestrator.AddGroupReward(reward);
            Orchestrator.EndGroupEpisode();
        }
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;
        _timer = 0;

        // Zorluk ayarı (Academy üzerinden gelebilir)
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

        // 1. ÖNCE SİSTEMLERİ BAŞLAT
        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        // 2. SONRA OYUNCU KAYNAKLARINI ATAN
        _world.Players.Clear();  // TAMAMEN TEMİZLE
        _world.Players.Add(1, new SimPlayerData
        {
            PlayerID = 1,
            Wood = 500,
            Stone = 500,
            Meat = 500,
            MaxPopulation = 20,
            CurrentPopulation = 0  // EKSTRA: Population'ı sıfırla
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

        // Debug için ekstra kontrol
        Debug.Log("Player 1 Resources - Wood: " + _world.Players[1].Wood + " Stone: " + _world.Players[1].Stone + " Meat: " + _world.Players[1].Meat);
        Debug.Log("Player 2 Resources - Wood: " + _world.Players[2].Wood + " Stone: " + _world.Players[2].Stone + " Meat: " + _world.Players[2].Meat);

        _lastWood = 500;
        _lastStone = 500;
        _lastMeat = 500;
        _lastWorkerCount = 0;

        // 3. EN SON BASE'LERİ KUR
        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        // DEĞİŞİKLİK 7: Agent.Setup yerine Orchestrator.Setup
        if (Orchestrator != null)
        {
            Orchestrator.Setup(_world, _gridSys, _unitSys, _buildSys, this);
        }

        if (UseMacroAI) _enemyAI = new SimpleMacroAI(_world, 2, difficultyLevel);
        else _enemyAI = null;

        if (Visualizer != null) Visualizer.Initialize(_world);

        // Sayaçları sıfırla
        _lastSoldiers = 0;
        _lastEnemyUnitCount = 0;
        _lastEnemyBuildingCount = 1;
        _lastEnemyBaseHealth = 1000f;
        _lastMyBaseHealth = 1000f;
    }

    // --- HARİTA VE BASE OLUŞTURMA KODLARI AYNEN KALDI ---
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
}