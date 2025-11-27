using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Collections.Generic;

public class SimRunner : MonoBehaviour
{
    [Header("Simulation Settings")]
    public int MapWidth = 50;
    public int MapHeight = 50;

    public bool RunFastMode = false;
    [Range(1, 1000)]
    public int FastModeMultiplier = 100;

    // Dışarıya açık dünya verisi
    private SimWorldState _world;
    public SimWorldState World => _world;

    void Start()
    {
        InitSimulation();
    }

    void InitSimulation()
    {
        Debug.Log("🚀 Simülasyon Başlatılıyor...");

        _world = new SimWorldState(MapWidth, MapHeight);

        // Başlangıç Kaynakları
        SimResourceSystem.AddResource(_world, 1, SimResourceType.Meat, 500);
        SimResourceSystem.AddResource(_world, 1, SimResourceType.Wood, 500);
        SimResourceSystem.AddResource(_world, 1, SimResourceType.Stone, 500);

        // --- GÜNCELLEME 1: ET KAYNAKLARI EKLENDİ ---
        for (int i = 0; i < 15; i++) SpawnResourceAtRandom(SimResourceType.Wood);
        for (int i = 0; i < 10; i++) SpawnResourceAtRandom(SimResourceType.Stone);
        for (int i = 0; i < 10; i++) SpawnResourceAtRandom(SimResourceType.Meat); // ARTIK ET DE VAR!

        // Base Kurulumu
        var basePos = new int2(MapWidth / 2, MapHeight / 2);
        SpawnBuilding(SimBuildingType.Base, basePos);

        // Yanına Bir Tane de Çiftlik (Test İçin)
        SpawnBuilding(SimBuildingType.Farm, new int2(basePos.x + 2, basePos.y));

        // İşçi Ekle
        SimBuildingSystem.SpawnUnit(_world, basePos, SimUnitType.Worker, 1);

        Debug.Log($"✅ Başlatıldı! Nüfus: {_world.Units.Count}");
    }

    void Update()
    {
        if (_world == null) return;

        if (RunFastMode)
        {
            float fixedDt = SimConfig.TICK_RATE;
            for (int i = 0; i < FastModeMultiplier; i++)
            {
                SimulateStep(fixedDt);
            }
        }
        else
        {
            SimulateStep(Time.deltaTime);
        }

        HandleInput();
    }

    void SimulateStep(float dt)
    {
        _world.TickCount++;
        SimBuildingSystem.UpdateAllBuildings(_world, dt);

        var allUnits = new List<SimUnitData>(_world.Units.Values);
        foreach (var unit in allUnits)
        {
            SimUnitSystem.UpdateUnit(unit, _world, dt);
        }
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.R)) InitSimulation();

        if (Input.GetKeyDown(KeyCode.Space))
        {
            // Rastgele bir işçiye rastgele bir kaynak toplama emri ver
            foreach (var unit in _world.Units.Values)
            {
                if (unit.UnitType == SimUnitType.Worker && unit.State == SimTaskType.Idle)
                {
                    foreach (var res in _world.Resources.Values)
                    {
                        if (SimUnitSystem.TryAssignGatherTask(unit, res, _world))
                        {
                            Debug.Log($"👷 İşçi {unit.ID} göreve atandı: {res.Type} topluyor.");
                            break;
                        }
                    }
                }
            }
        }
    }

    // --- GÜNCELLENEN SPAWN FONKSİYONLARI ---

    void SpawnResourceAtRandom(SimResourceType type)
    {
        int x = Random.Range(0, MapWidth);
        int y = Random.Range(0, MapHeight);
        var pos = new int2(x, y);

        if (SimGridSystem.IsWalkable(_world, pos))
        {
            var res = new SimResourceData
            {
                ID = _world.NextID(),
                Type = type,
                GridPosition = pos,
                AmountRemaining = 250
            };
            _world.Resources.Add(res.ID, res);

            // Tile Tipini Güncelle (Görsel İçin)
            _world.Map.Grid[x, y].IsWalkable = false;

            if (type == SimResourceType.Wood) _world.Map.Grid[x, y].Type = SimTileType.Forest;
            else if (type == SimResourceType.Stone) _world.Map.Grid[x, y].Type = SimTileType.Stone;
            else if (type == SimResourceType.Meat) _world.Map.Grid[x, y].Type = SimTileType.MeatBush; // EKLENDİ
        }
    }

    void SpawnBuilding(SimBuildingType type, int2 pos)
    {
        if (!SimGridSystem.IsWalkable(_world, pos)) return;

        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = 1,
            Type = type,
            GridPosition = pos,
            IsConstructed = true
        };

        // --- KRİTİK GÜNCELLEME: STATLARI BAŞLAT ---
        // Artık Farm ise üretim yapması gerektiğini bilecek
        SimBuildingSystem.InitializeBuildingStats(b);

        _world.Buildings.Add(b.ID, b);
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
    }
}