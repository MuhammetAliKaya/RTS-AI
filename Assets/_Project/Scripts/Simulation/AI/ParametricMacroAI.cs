using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using UnityEngine;

public class ParametricMacroAI
{
    private SimWorldState _world;
    private int _playerID;
    private float[] _genes;
    private float _timer;
    private System.Random _rng;

    public ParametricMacroAI(SimWorldState world, int playerID, float[] genes, System.Random rng)
    {
        _world = world;
        _playerID = playerID;
        _genes = genes;
        _rng = rng ?? new System.Random();
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < 0.25f) return;
        _timer = 0;

        // --- 1. DURUM ANALİZİ ---
        var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
        var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
        var pData = SimResourceSystem.GetPlayer(_world, _playerID);

        int workers = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
        int soldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);

        int barracksCount = myBuildings.Count(b => b.Type == SimBuildingType.Barracks);
        int towerCount = myBuildings.Count(b => b.Type == SimBuildingType.Tower);
        int farmCount = myBuildings.Count(b => b.Type == SimBuildingType.Farm);
        int woodCutterCount = myBuildings.Count(b => b.Type == SimBuildingType.WoodCutter);
        int stonePitCount = myBuildings.Count(b => b.Type == SimBuildingType.StonePit);

        int enemySoldierCount = _world.Units.Values.Count(u => u.PlayerID != _playerID && u.UnitType == SimUnitType.Soldier);
        var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);

        // KONTROL: Haritada toplanabilir kaynak kaldı mı?
        bool anyResourceLeft = _world.Resources.Count > 0;

        // --- GEN OKUMA ---
        int targetWorker = SimMath.Clamp(SimMath.RoundToInt(_genes[0]), 3, 50);
        int targetSoldier = SimMath.Clamp(SimMath.RoundToInt(_genes[1]), 5, 80);
        int attackThreshold = SimMath.Clamp(SimMath.RoundToInt(_genes[2]), 5, 60);
        float defenseRatio = SimMath.Clamp01(_genes[3] / 20f);
        int targetBarracks = SimMath.Clamp(SimMath.RoundToInt(_genes[4] / 5f), 1, 5);
        float ecoBias = SimMath.Clamp01(_genes[5] / 40f);

        int targetFarm = SimMath.Clamp(SimMath.RoundToInt(_genes[6] / 5f), 0, 5);
        int targetWoodCutter = SimMath.Clamp(SimMath.RoundToInt(_genes[7] / 5f), 0, 5);
        int targetStonePit = SimMath.Clamp(SimMath.RoundToInt(_genes[8] / 2f), 0, 5);
        int houseBuffer = SimMath.Clamp(SimMath.RoundToInt(_genes[9] / 4f), 1, 10);
        float towerPosBias = SimMath.Clamp01(_genes[10] / 40f);
        float defenseRadius = 5f + _genes[11];

        int genSaveThreshold = SimMath.Clamp(SimMath.RoundToInt(_genes[12] / 2f), 0, 20);
        int safeSaveThreshold = Math.Max(5, genSaveThreshold);

        // --- 2. ACİL DURUM: AKTİF SAVUNMA ---
        if (CheckBaseDefense(myUnits, baseB, defenseRadius))
        {
            // Savunma modu
        }
        else
        {
            // Sadece askerler saldırır
            if (soldiers >= attackThreshold && enemyBase != null)
            {
                foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                    SimUnitSystem.OrderAttack(s, enemyBase, _world);
            }
        }

        // --- 3. İNŞAAT VE ÜRETİM KARARLARI ---

        // A. EV
        int freePop = pData.MaxPopulation - pData.CurrentPopulation;
        if (freePop <= houseBuffer)
        {
            bool built = TryBuildBuilding(SimBuildingType.House, myUnits, baseB,
                SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);

            // Kaynak bitmediyse (anyResourceLeft == true) ve paramız yoksa bekleriz.
            // Ama kaynak bittiyse (anyResourceLeft == false) beklemeye gerek yok, akış devam etsin.
            if (!built && freePop == 0 && anyResourceLeft && !SimResourceSystem.CanAfford(_world, _playerID, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
                return;
        }

        // B. ACİL ASKER ÜRETİMİ
        bool underThreat = (soldiers <= enemySoldierCount + 1);
        if (underThreat && soldiers < targetSoldier && freePop > 0)
        {
            foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
            {
                if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                    SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
            }
        }

        // C. SAVUNMA KULESİ
        int neededTowers = SimMath.FloorToInt((enemySoldierCount + 10) * defenseRatio);
        if (towerCount < neededTowers && towerCount < 15)
        {
            bool built = TryBuildDefensiveStructure(myUnits, baseB, enemyBase, towerPosBias);

            // Kaynak tükendi ise (anyResourceLeft == false) return yapma, devam et.
            if (!built && !underThreat && anyResourceLeft && !SimResourceSystem.CanAfford(_world, _playerID, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT))
            {
                if (soldiers < targetSoldier * 0.5f) return;
                if (workers > safeSaveThreshold) return;
            }
        }

        // D. KIŞLA
        if (barracksCount < targetBarracks)
        {
            bool built = TryBuildBuilding(SimBuildingType.Barracks, myUnits, baseB,
                SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);

            // Kaynak tükendi ise return yapma.
            if (!built && !underThreat && anyResourceLeft && !SimResourceSystem.CanAfford(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            {
                if (workers > safeSaveThreshold) return;
            }
        }

        // E. EKONOMİ BİNALARI
        if (farmCount < targetFarm)
            TryBuildBuilding(SimBuildingType.Farm, myUnits, baseB, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);

        // Sadece kaynak varsa toplayıcı bina kurmaya çalış
        if (anyResourceLeft)
        {
            if (woodCutterCount < targetWoodCutter)
                TryBuildBuilding(SimBuildingType.WoodCutter, myUnits, baseB, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT);
            if (stonePitCount < targetStonePit)
                TryBuildBuilding(SimBuildingType.StonePit, myUnits, baseB, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT);
        }

        // F. NORMAL ASKER ÜRETİMİ
        if (soldiers < targetSoldier && freePop > 0)
        {
            foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
            {
                if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                    SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
            }
        }

        // G. İŞÇİ ÜRETİMİ
        if (baseB != null && !baseB.IsTraining && workers < targetWorker && freePop > 0)
        {
            // Kaynak bittiyse yeni işçi basma, mevcut parayı askere sakla
            if (!anyResourceLeft) { /* Pas geç */ }
            else if (underThreat && workers > 5 && SimResourceSystem.GetPlayer(_world, _playerID).Wood < 300)
            {
                if (SimConfig.EnableLogs) Debug.Log("🛡️ AI: Saldırı var, odunu askere saklıyorum.");
            }
            else if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
            {
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            }
        }

        // --- SON ADIM: İŞÇİ YÖNETİMİ ---
        // İşçileri saldırıya göndermiyoruz, sadece kaynak varsa toplamaya gönderiyoruz.
        // Kaynak yoksa Idle kalırlar, böylece bina yapımı için hazır olurlar.
        if (!CheckBaseDefense(myUnits, baseB, defenseRadius))
        {
            ManageWorkers(myUnits, ecoBias, pData, anyResourceLeft);
        }
    }

    // --- YARDIMCILAR ---

    private SimUnitData GetAvailableWorker(List<SimUnitData> units)
    {
        var w = units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        if (w != null) return w;
        w = units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Gathering);
        return w;
    }

    private bool TryBuildDefensiveStructure(List<SimUnitData> units, SimBuildingData myBase, SimBuildingData enemyBase, float forwardBias)
    {
        if (!SimResourceSystem.CanAfford(_world, _playerID, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT)) return false;

        var worker = GetAvailableWorker(units);
        if (worker == null || myBase == null) return false;

        int2 buildCenter = myBase.GridPosition;

        if (enemyBase != null)
        {
            if (forwardBias > 0.2f)
            {
                float dx = enemyBase.GridPosition.x - myBase.GridPosition.x;
                float dy = enemyBase.GridPosition.y - myBase.GridPosition.y;
                float distance = 2f + (forwardBias * 25f);

                float length = SimMath.Sqrt(dx * dx + dy * dy);
                if (length > 0)
                {
                    int offsetX = SimMath.RoundToInt((dx / length) * distance);
                    int offsetY = SimMath.RoundToInt((dy / length) * distance);
                    buildCenter = new int2(myBase.GridPosition.x + offsetX, myBase.GridPosition.y + offsetY);
                }
            }
        }

        int2 pos = FindBuildSpot(buildCenter, 1, 6);

        if (pos.x != -1)
        {
            SimResourceSystem.SpendResources(_world, _playerID, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
            var b = SpawnPlaceholder(SimBuildingType.Tower, pos);
            SimUnitSystem.OrderBuild(worker, b, _world);
            return true;
        }
        return false;
    }

    private bool TryBuildBuilding(SimBuildingType type, List<SimUnitData> units, SimBuildingData near, int costWood, int costStone, int costMeat)
    {
        if (!SimResourceSystem.CanAfford(_world, _playerID, costWood, costStone, costMeat)) return false;

        var worker = GetAvailableWorker(units);
        int2 searchCenter = (near != null) ? near.GridPosition : (worker != null ? worker.GridPosition : new int2(25, 25));

        if (worker != null)
        {
            int buildingCount = _world.Buildings.Values.Count(b => b.PlayerID == _playerID);
            int minRadius = 3 + (buildingCount / 5) * 2;
            int maxRadius = minRadius + 5;

            int2 pos = FindBuildSpot(searchCenter, minRadius, maxRadius);

            if (pos.x != -1)
            {
                SimResourceSystem.SpendResources(_world, _playerID, costWood, costStone, costMeat);
                var b = SpawnPlaceholder(type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                return true;
            }
        }
        return false;
    }

    // DÜZELTİLDİ: Saldırı mantığı kaldırıldı.
    private void ManageWorkers(List<SimUnitData> units, float ecoBias, SimPlayerData pData, bool anyResourceLeft)
    {
        // Kaynak yoksa işçilere görev atama (Idle kalsınlar, bina yapımında kullanılırlar)
        if (!anyResourceLeft) return;

        var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
        if (idleWorkers.Count == 0) return;

        foreach (var w in idleWorkers)
        {
            // Kaynak varsa toplama mantığı devam eder
            SimResourceType targetType = SimResourceType.Wood;

            if (pData.Meat < SimConfig.WORKER_COST_MEAT) targetType = SimResourceType.Meat;
            else if (pData.Wood < SimConfig.HOUSE_COST_WOOD) targetType = SimResourceType.Wood;
            else if (pData.Stone < SimConfig.TOWER_COST_STONE) targetType = SimResourceType.Stone;
            else
            {
                double rng = _rng.NextDouble();
                if (rng < 0.25) targetType = SimResourceType.Stone;
                else if (rng < 0.25 + (ecoBias * 0.75)) targetType = SimResourceType.Meat;
                else targetType = SimResourceType.Wood;
            }

            var res = FindNearestResource(w.GridPosition, targetType);
            if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);
            if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
        }
    }

    private bool CheckBaseDefense(List<SimUnitData> myUnits, SimBuildingData myBase, float radius)
    {
        if (myBase == null) return false;
        SimUnitData nearestEnemy = null;
        float minDst = radius * radius;
        foreach (var unit in _world.Units.Values)
        {
            if (unit.PlayerID == _playerID || unit.State == SimTaskType.Dead) continue;
            if (unit.UnitType != SimUnitType.Soldier) continue;
            float dst = SimGridSystem.GetDistanceSq(myBase.GridPosition, unit.GridPosition);
            if (dst < minDst) { minDst = dst; nearestEnemy = unit; }
        }
        if (nearestEnemy != null)
        {
            foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Attacking))
            {
                if (s.TargetID == nearestEnemy.ID) continue;
                SimUnitSystem.OrderAttackUnit(s, nearestEnemy, _world);
            }
            return true;
        }
        return false;
    }

    private int2 FindBuildSpot(int2 center, int minRadius, int maxRadius)
    {
        for (int r = minRadius; r <= maxRadius; r++)
        {
            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    if (System.Math.Abs(x) == r || System.Math.Abs(y) == r)
                    {
                        int2 pos = new int2(center.x + x, center.y + y);
                        if (SimGridSystem.IsWalkable(_world, pos)) return pos;
                    }
                }
            }
        }
        return new int2(-1, -1);
    }

    private SimBuildingData SpawnPlaceholder(SimBuildingType type, int2 pos)
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
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        _world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
        return b;
    }

    private SimResourceData FindNearestResource(int2 pos, SimResourceType type)
    {
        SimResourceData best = null;
        float minDst = float.MaxValue;
        foreach (var r in _world.Resources.Values)
        {
            if (type != SimResourceType.None && r.Type != type) continue;
            float d = SimGridSystem.GetDistanceSq(pos, r.GridPosition);
            if (d < minDst) { minDst = d; best = r; }
        }
        return best;
    }
}