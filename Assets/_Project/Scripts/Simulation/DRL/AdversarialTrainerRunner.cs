using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;

public class AdversarialTrainerRunner : MonoBehaviour
{
    [Header("Ayarlar")]
    public RTSAgent Agent;
    public int MapSize = 20;
    public int MaxSteps = 5000;

    [Range(1f, 100f)]
    public float SimulationTimeScale = 20.0f;
    public float dt = 0.1f;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Rakip Ayarları")]
    public bool UseMacroAI = true;
    public AIDifficulty EnemyDifficulty = AIDifficulty.Passive;

    [Header("Debug")]
    public int LogInterval = 1000; // Kaç adımda bir log atılsın?

    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    private SimpleMacroAI _enemyAI;
    private int _currentStep = 0;
    private bool _gameEnded = false;

    void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();

        Application.targetFrameRate = -1;
        Time.timeScale = SimulationTimeScale;

        // ResetSimulation Start içinde değil, Agent tarafından OnEpisodeBegin ile çağrılacak
        // Ancak ilk başlangıç için manuel çağırabiliriz
        if (Agent == null) ResetSimulation();
    }

    void Update()
    {
        if (_world != null && !_gameEnded)
        {
            SimulationStep();
        }
    }

    public void SimulationStep()
    {
        if (_enemyAI != null) _enemyAI.Update(dt);
        if (Agent != null) Agent.RequestDecision();
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);

        var unitIds = _world.Units.Keys.ToList();
        foreach (var uid in unitIds)
        {
            if (_world.Units.TryGetValue(uid, out SimUnitData unit))
                if (_unitSys != null) _unitSys.UpdateUnit(unit, dt);
        }
        if (_currentStep > 0 && _currentStep % LogInterval == 0)
        {
            float currentReward = Agent != null ? Agent.GetCumulativeReward() : 0f;
            Debug.Log($"⏱️ [SİMÜLASYON] Adım: {_currentStep}/{MaxSteps} | Anlık Ödül: {currentReward:F3}");
        }

        CheckGameResult();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            EndGame(0); // Berabere / Zaman Doldu
        }
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;

        _world = new SimWorldState(MapSize, MapSize);
        GenerateMap();

        // --- HATANIN ÇÖZÜMÜ BURADA ---
        // ÖNCE SİSTEMLERİ KURUYORUZ, SONRA KULLANIYORUZ
        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world); // Artık null değil
        _resSys = new SimResourceSystem(_world);
        // -----------------------------

        // Oyuncuları Ekle
        if (_world.Players.ContainsKey(1))
        {
            var p1 = _world.Players[1];
            p1.Wood = 500; p1.Stone = 500; p1.Meat = 500; p1.MaxPopulation = 20;
        }
        if (!_world.Players.ContainsKey(2))
        {
            _world.Players.Add(2, new SimPlayerData { PlayerID = 2, Wood = 500, Stone = 500, Meat = 500, MaxPopulation = 20 });
        }

        // Şimdi sistemler hazır olduğu için hata vermeyecek
        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        if (Agent != null) Agent.Setup(_world, _gridSys, _unitSys, _buildSys);

        if (UseMacroAI) _enemyAI = new SimpleMacroAI(_world, 2, EnemyDifficulty);
        else _enemyAI = null;

        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (myBase == null) EndGame(-1.0f); // Kaybettik
        else if (enemyBase == null) EndGame(1.0f); // Kazandık (Pozitif değer)
    }

    private void EndGame(float result)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        if (Agent != null)
        {
            float totalReward = 0f;

            if (result > 0) // KAZANDIYSA
            {
                // 1. Temel Kazanma Ödülü (Bunu yüksek tutuyoruz)
                totalReward += 40.0f;

                // 2. Zaman Bonusu (Ne kadar erken biterse o kadar iyi)
                // Formül: (Kalan Adım Sayısı / Maksimum Adım Sayısı) * Bonus Çarpanı
                // Örnek: 1000. adımda bitirdi (Max 5000). Kalan 4000. (4000/5000) * 5 = +4.0 Puan Bonus
                float timeBonus = ((float)(MaxSteps - _currentStep) / MaxSteps) * 20.0f;
                totalReward += timeBonus;

                Debug.Log($"🏆 ZAFER! Adım: {_currentStep} | Baz: 10.0 | Zaman Bonusu: {timeBonus:F2} | Toplam: {totalReward:F2}");
            }
            else if (result < 0) // KAYBETTİYSE
            {
                totalReward -= 1.0f; // Kaybetme cezası
            }
            else // BERABERE / ZAMAN DOLDU
            {
                totalReward -= 0.5f; // Zaman dolması cezası (Hafif)
            }

            Agent.AddReward(totalReward);
            Agent.EndEpisode();
        }
    }

    private void GenerateMap()
    {
        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 0; y < MapSize; y++)
            {
                _world.Map.Grid[x, y] = new SimMapNode { x = x, y = y, Type = SimTileType.Grass, IsWalkable = true, OccupantID = -1 };
            }
        }

        for (int i = 0; i < 40; i++)
        {
            int x = UnityEngine.Random.Range(0, MapSize);
            int y = UnityEngine.Random.Range(0, MapSize);
            if ((x < 5 && y < 5) || (x > MapSize - 5 && y > MapSize - 5)) continue;
            if (!_world.Map.Grid[x, y].IsWalkable) continue;

            var res = new SimResourceData { ID = _world.NextID(), GridPosition = new int2(x, y), AmountRemaining = 500 };
            float r = UnityEngine.Random.value;
            if (r < 0.3f) { res.Type = SimResourceType.Wood; _world.Map.Grid[x, y].Type = SimTileType.Forest; }
            else if (r < 0.6f) { res.Type = SimResourceType.Stone; _world.Map.Grid[x, y].Type = SimTileType.Stone; }
            else { res.Type = SimResourceType.Meat; _world.Map.Grid[x, y].Type = SimTileType.MeatBush; }

            _world.Resources.Add(res.ID, res);
            _world.Map.Grid[x, y].OccupantID = res.ID;
            _world.Map.Grid[x, y].IsWalkable = false;
        }
    }

    private void SetupBase(int pid, int2 pos)
    {
        // SimBuildingSystem.CreateBuilding kullanıyoruz (Factory Method)
        // NOT: SimBuildingSystem içindeki CreateBuilding statik olabilir, 
        // ama burada instance üzerinden SpawnUnit çağırıyorduk.

        var building = SimBuildingSystem.CreateBuilding(_world, pid, SimBuildingType.Base, pos);
        building.Health = 1000; building.MaxHealth = 1000; building.IsConstructed = true;

        // Başlangıç işçileri
        for (int i = 0; i < 3; i++)
        {
            if (_buildSys != null) // Güvenlik kontrolü
                _buildSys.SpawnUnit(pos, SimUnitType.Worker, pid);
        }
    }
}