using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;

public class SimpleMacroAI
{
    private SimWorldState _world;
    private int _playerID;
    private float _timer = 0f;
    private float _decisionInterval = 1.0f;

    public SimpleMacroAI(SimWorldState world, int playerID)
    {
        _world = world;
        _playerID = playerID;
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer >= _decisionInterval)
        {
            _timer = 0f;
            MakeDecisions();
        }
    }

    private void MakeDecisions()
    {
        var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
        var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();

        int workerCount = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
        int soldierCount = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
        bool hasBarracks = myBuildings.Any(b => b.Type == SimBuildingType.Barracks); // İnşa halinde de olsa say

        // 1. İŞÇİ BAS (Eğer azsa)
        if (workerCount < 3)
        {
            var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            if (baseB != null && !baseB.IsTraining)
            {
                // Para hesabı StartTraining içinde yapılıyor, biz sadece çağıralım
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            }
        }

        // 2. KIŞLA KUR (Eğer yoksa ve para varsa)
        if (!hasBarracks)
        {
            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, 0))
            {
                var idleWorker = myUnits.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
                if (idleWorker != null)
                {
                    // Üssün etrafında boş yer ara
                    var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
                    if (baseB != null)
                    {
                        int2 buildPos = FindBuildSpot(baseB.GridPosition);
                        if (buildPos.x != -1)
                        {
                            // Kaynağı Harca
                            SimResourceSystem.SpendResources(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, 0);

                            // Binayı "Temel Atılmış" olarak oluştur
                            var barracks = SpawnBuildingPlaceholder(SimBuildingType.Barracks, buildPos);

                            // İşçiye "Git İnşa Et" emri ver
                            SimUnitSystem.OrderBuild(idleWorker, barracks, _world);
                            Debug.Log("🤖 AI: Kışla İnşaatına Başladı.");
                            return;
                        }
                    }
                }
            }
        }

        // 3. ASKER BAS (Kışla varsa)
        if (hasBarracks && soldierCount < 5)
        {
            var barracks = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Barracks && b.IsConstructed);
            if (barracks != null && !barracks.IsTraining)
            {
                SimBuildingSystem.StartTraining(barracks, _world, SimUnitType.Soldier);
            }
        }

        // 4. BOŞTA KALAN İŞÇİLERİ ÇALIŞTIR (Kaynak Topla)
        foreach (var worker in myUnits.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle))
        {
            var res = FindNearestResource(worker.GridPosition);
            if (res != null)
            {
                SimUnitSystem.TryAssignGatherTask(worker, res, _world);
            }
        }

        // 5. SALDIRI (Yeterli asker varsa)
        if (soldierCount >= 3)
        {
            // Hedef: Düşman Base'i
            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);
            if (enemyBase != null)
            {
                foreach (var soldier in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
                {
                    // Sadece boştaysa veya devriye geziyorsa saldırı emri ver
                    if (soldier.State == SimTaskType.Idle || soldier.State == SimTaskType.Moving)
                    {
                        SimUnitSystem.OrderMove(soldier, enemyBase.GridPosition, _world);
                        // UnitSystem'de "Yürürken düşman görürse dal" mantığı (UpdateCombat) varsa çalışır.
                        // Yoksa buraya "AttackMove" mantığı eklenmeli.
                        // Şimdilik hedefe yürümesi, yolda karşılaşınca savaşması için yeterli.
                    }
                }
            }
        }
    }

    // --- YARDIMCILAR ---
    private int2 FindBuildSpot(int2 center)
    {
        for (int x = center.x - 4; x <= center.x + 4; x++)
        {
            for (int y = center.y - 4; y <= center.y + 4; y++)
            {
                int2 pos = new int2(x, y);
                if (SimGridSystem.IsWalkable(_world, pos)) return pos;
            }
        }
        return new int2(-1, -1);
    }

    private SimResourceData FindNearestResource(int2 pos)
    {
        SimResourceData best = null;
        float minDst = float.MaxValue;
        foreach (var r in _world.Resources.Values)
        {
            float d = SimGridSystem.GetDistance(pos, r.GridPosition);
            if (d < minDst) { minDst = d; best = r; }
        }
        return best;
    }

    private SimBuildingData SpawnBuildingPlaceholder(SimBuildingType type, int2 pos)
    {
        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = _playerID,
            Type = type,
            GridPosition = pos,
            IsConstructed = false,
            ConstructionProgress = 0f
        };
        SimBuildingSystem.InitializeBuildingStats(b);
        _world.Buildings.Add(b.ID, b);
        _world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        return b;
    }
}