using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;
using Unity.Mathematics;

// HATA ÇÖZÜMÜ: Hangi int2'nin kullanılacağını açıkça belirtiyoruz.
using int2 = RTS.Simulation.Data.int2;

public class SelfPlayTrainerRunner : MonoBehaviour
{
    [Header("Ajanlar")]
    public RTSAgent AgentA; // Player ID 1 (Team 0)
    public RTSAgent AgentB; // Player ID 2 (Team 1)

    [Header("Ayarlar")]
    public int MapSize = 20;
    public int MaxSteps = 5000;

    [Tooltip("Harita rastgeleliğini kontrol etmek için kullanılır.")]
    public int mapSeed = 12345;
    public bool useRandomSeed = true;

    [Header("Zaman Ayarları")]
    public bool IsTrainingMode = true;
    public float _simStepSize = 0.0025f;
    [Range(1f, 100f)]
    public float _simStepCountPerFrame = 1f;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    // SİSTEMLER
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    // TAKİP DEĞİŞKENLERİ
    private struct AgentProgress
    {
        public int lastSoldiers;
        public int lastWorkers;
        public int lastWood;
        public int lastMeat;
        public int lastStone;
        public int lastEnemyUnitCount;
        public int lastEnemyBuildingCount;
        public float lastEnemyBaseHealth;
    }

    private AgentProgress _progressA;
    private AgentProgress _progressB;

    private int _currentStep = 0;
    private bool _gameEnded = false;
    private int _agentDecisionCounter = 0;

    // Karar aralığı
    private const int AGENT_DECISION_INTERVAL = 4;

    void Start()
    {
        if (IsTrainingMode)
        {
            Application.targetFrameRate = 60;
        }
        ResetSimulation();
    }

    void Update()
    {
        if (_gameEnded) return;

        if (IsTrainingMode)
        {
            for (int i = 0; i < _simStepCountPerFrame; i++)
            {
                SimulationStep(_simStepSize);
            }
        }
        else
        {
            SimulationStep(_simStepSize);
        }
    }

    public void SimulationStep(float dt)
    {
        // ... (Kodun üst kısımları aynı) ...

        // 1. Ajanlar Karar Versin
        _agentDecisionCounter++;
        if (_agentDecisionCounter >= AGENT_DECISION_INTERVAL)
        {
            _agentDecisionCounter = 0;
            if (AgentA != null) AgentA.RequestDecision();
            if (AgentB != null) AgentB.RequestDecision();
        }

        // 2. Simülasyonu İlerlet
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);
        if (_unitSys != null) _unitSys.UpdateAllUnits(dt);

        // 3. Ödülleri Hesapla
        CalculateRewards(1, AgentA, ref _progressA, 2);
        CalculateRewards(2, AgentB, ref _progressB, 1);

        ApplyIdlePenalty(1, AgentA);
        ApplyIdlePenalty(2, AgentB);

        // 4. Bitiş Kontrolü (Yıkım var mı?)
        CheckGameResult();

        _currentStep++;

        // --- ZAMAN AŞIMI KONTROLÜ (DÜZENLENDİ) ---
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            // BERABERLİK CEZASI:
            // Kimse birbirini yenemeden süre bittiyse her ikisine de ceza ver.
            // Böylece oyunu "kilitlemek" yerine risk alıp saldırmayı tercih ederler.

            if (AgentA != null) AgentA.AddReward(-100.0f); // Kaybetmiş kadar olmasa da ciddi bir ceza
            if (AgentB != null) AgentB.AddReward(-100.0f);

            // Debug.Log("Süre doldu! Beraberlik cezası uygulandı.");

            EndGame(0);
        }
    }

    // SelfPlayTrainerRunner.cs içine bu fonksiyonları ekle veya güncelle:

    private void CalculateRewards(int myPlayerID, RTSAgent myAgent, ref AgentProgress prog, int enemyPlayerID)
    {
        if (myAgent == null) return;
        if (!_world.Players.ContainsKey(myPlayerID)) return;

        var myPlayer = _world.Players[myPlayerID];

        // --- 1. EKONOMİ ÖDÜLLERİ ---
        // İşçi sayısı arttıysa ödül ver (Ekonomiyi büyütmeye teşvik)
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == myPlayerID && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);
        if (currentWorkers > prog.lastWorkers)
        {
            myAgent.AddReward(0.5f); // Yeni işçi ödülü
        }

        // --- 2. ORDU ÖDÜLLERİ ---
        // Asker sayısı arttıysa ödül ver
        int currentSoldiers = _world.Units.Values.Count(u => u.PlayerID == myPlayerID && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);
        if (currentSoldiers > prog.lastSoldiers)
        {
            myAgent.AddReward(1.0f); // Yeni asker ödülü
        }

        // --- 3. İNŞAAT/YIKIM ÖDÜLLERİ ---
        int currentEnemyUnits = 0;
        int currentEnemyBuildings = 0;
        float currentEnemyBaseHealth = 0;

        foreach (var u in _world.Units.Values)
            if (u.PlayerID == enemyPlayerID && u.State != SimTaskType.Dead) currentEnemyUnits++;

        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == enemyPlayerID)
            {
                currentEnemyBuildings++;
                if (b.Type == SimBuildingType.Base) currentEnemyBaseHealth = b.Health;
            }
        }

        // Düşman birimi öldürme
        if (currentEnemyUnits < prog.lastEnemyUnitCount)
        {
            int killCount = prog.lastEnemyUnitCount - currentEnemyUnits;
            myAgent.AddReward(0.5f * killCount);
        }

        // Düşman binası yıkma
        if (currentEnemyBuildings < prog.lastEnemyBuildingCount)
        {
            int destroyCount = prog.lastEnemyBuildingCount - currentEnemyBuildings;
            myAgent.AddReward(2.0f * destroyCount);
        }

        // Ana üsse hasar
        if (currentEnemyBaseHealth < prog.lastEnemyBaseHealth)
        {
            float damage = prog.lastEnemyBaseHealth - currentEnemyBaseHealth;
            myAgent.AddReward(damage * 0.1f);
        }

        // İlerleme durumunu kaydet
        prog.lastWorkers = currentWorkers;
        prog.lastSoldiers = currentSoldiers;
        prog.lastEnemyUnitCount = currentEnemyUnits;
        prog.lastEnemyBuildingCount = currentEnemyBuildings;
        prog.lastEnemyBaseHealth = currentEnemyBaseHealth;
    }

    private void ApplyIdlePenalty(int pid, RTSAgent agent)
    {
        if (agent == null) return;
        int idleCount = _world.Units.Values.Count(u =>
            u.PlayerID == pid &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        );

        if (idleCount > 0)
        {
            agent.AddReward(idleCount * -0.0001f); // Ufak bir tembellik cezası
        }
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;

        _world = new SimWorldState(MapSize, MapSize);

        int finalSeed = mapSeed;
        if (useRandomSeed || mapSeed <= 0)
        {
            finalSeed = System.DateTime.Now.Millisecond + System.DateTime.Now.Second * 1000;
        }
        GenerateMap(finalSeed);

        SimGameContext.ActiveWorld = _world;

        // HATA DÜZELTME: Player 1 zaten constructor'da eklendiği için kontrol edip güncelliyoruz.
        if (_world.Players.ContainsKey(1))
        {
            var p1 = _world.Players[1];
            p1.Wood = 500;
            p1.Stone = 500;
            p1.Meat = 500;
            p1.MaxPopulation = 20;
        }
        else
        {
            _world.Players.Add(1, new SimPlayerData { PlayerID = 1, Wood = 500, Stone = 500, Meat = 500, MaxPopulation = 20 });
        }

        // Player 2 için de aynı güvenli kontrolü yapıyoruz (SimWorldState ilerde değişirse patlamasın diye)
        if (_world.Players.ContainsKey(2))
        {
            var p2 = _world.Players[2];
            p2.Wood = 500;
            p2.Stone = 500;
            p2.Meat = 500;
            p2.MaxPopulation = 20;
        }
        else
        {
            _world.Players.Add(2, new SimPlayerData { PlayerID = 2, Wood = 500, Stone = 500, Meat = 500, MaxPopulation = 20 });
        }

        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        // Ajan Başlatma ve ID Atamaları
        if (AgentA != null)
        {
            AgentA.MyPlayerID = 1;
            AgentA.Setup(_world, _gridSys, _unitSys, _buildSys);
        }

        if (AgentB != null)
        {
            AgentB.MyPlayerID = 2;
            AgentB.Setup(_world, _gridSys, _unitSys, _buildSys);
        }

        if (Visualizer != null) Visualizer.Initialize(_world);

        ResetProgress(ref _progressA);
        ResetProgress(ref _progressB);
    }

    private void ResetProgress(ref AgentProgress p)
    {
        p.lastSoldiers = 0;
        p.lastWorkers = 1;
        p.lastWood = 500;
        p.lastMeat = 500;
        p.lastStone = 500;
        p.lastEnemyBaseHealth = 1000f;
        p.lastEnemyUnitCount = 1;
        p.lastEnemyBuildingCount = 1;
    }

    private void GenerateMap(int seed)
    {
        int MapSize = _world.Map.Grid.GetLength(0);
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

        UnityEngine.Random.InitState(seed);
        int resourceCount = 50;
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
        // 1. Ana Üssü (Base) Oluştur
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

        // Bina istatistiklerini başlat
        SimBuildingSystem.InitializeBuildingStats(building, true);

        // Dünyaya ve haritaya ekle
        _world.Buildings.Add(building.ID, building);
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        _world.Map.Grid[pos.x, pos.y].OccupantID = building.ID;

        // 2. Başlangıç İşçilerini Oluştur (AdversarialTrainerRunner'daki döngü yapısı)
        int start_workercount = 1; // İleride bu sayıyı artırmak isterseniz buradan değiştirebilirsiniz.
        for (int i = 0; i < start_workercount; i++)
        {
            // Üssün etrafında yürünebilir boş bir komşu bul
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

                // Birimi dünyaya ve haritaya ekle
                _world.Units.Add(unit.ID, unit);
                _world.Map.Grid[spawnPos.Value.x, spawnPos.Value.y].OccupantID = unit.ID;

                // Nüfusu güncelle
                SimResourceSystem.ModifyPopulation(_world, pid, 1);
            }
        }
    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        var p1Base = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var p2Base = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        // --- HIZ BONUSU HESAPLAMA ---
        // Ne kadar erken biterse o kadar yüksek puan (Max 4.0 puan)
        float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;
        float speedBonus = timeFactor * 500.0f;

        if (p1Base == null && p2Base == null)
        {
            // Beraberlik (İkisi de aynı anda yıkıldı - çok nadir)
            AgentA?.AddReward(-1f); AgentA?.EndEpisode();
            AgentB?.AddReward(-1f); AgentB?.EndEpisode();
            EndGame(0);
        }
        else if (p1Base == null) // P1 Kaybetti, P2 Kazandı
        {
            // KAYBEDEN (P1)
            AgentA?.AddReward(-20.0f);
            AgentA?.EndEpisode();

            // KAZANAN (P2) -> 2.0 Puan + Hız Bonusu
            float totalReward = 200.0f + speedBonus;
            AgentB?.AddReward(totalReward);
            AgentB?.EndEpisode();

            // Debug.Log($"P2 KAZANDI! Hız Bonusu: {speedBonus:F2} | Toplam: {totalReward:F2}");
            EndGame(2);
        }
        else if (p2Base == null) // P2 Kaybetti, P1 Kazandı
        {
            // KAZANAN (P1) -> 2.0 Puan + Hız Bonusu
            float totalReward = 200.0f + speedBonus;
            AgentA?.AddReward(totalReward);
            AgentA?.EndEpisode();

            // KAYBEDEN (P2)
            AgentB?.AddReward(-20.0f);
            AgentB?.EndEpisode();

            // Debug.Log($"P1 KAZANDI! Hız Bonusu: {speedBonus:F2} | Toplam: {totalReward:F2}");
            EndGame(1);
        }
        // else
        // {

        //     AgentA?.AddReward(-100f);
        //     AgentA?.EndEpisode();

        //     AgentB?.AddReward(-100f);
        //     AgentB?.EndEpisode();
        // }
    }

    private void EndGame(int winnerID)
    {
        // Eğer zaten oyun bittiyse tekrar işlem yapma (Çifte reset olmaması için)
        if (_gameEnded) return;

        _gameEnded = true;

        // --- EKSİK OLAN KISIM BURASI ---
        // Oyun süre/beraberlik veya başka bir sebeple burada bittiyse,
        // ajanlara bölümü bitirmelerini söylemeliyiz ki 'OnEpisodeBegin' tetiklensin ve 'ResetSimulation' çalışsın.

        if (AgentA != null)
        {
            // Eğer ödül vermediyseniz (Beraberlik durumunda) ufak bir teselli veya ceza verebilirsiniz.
            // AgentA.AddReward(0f); 
            AgentA.EndEpisode();
        }

        if (AgentB != null)
        {
            // AgentB.AddReward(0f);
            AgentB.EndEpisode();
        }
    }
}