using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System.Collections.Generic;

// UnityEngine YOK!

public class SimpleMacroAI
{
    private SimWorldState _world;
    private int _playerID;
    private float _timer = 0f;
    private float _decisionInterval = 1.0f;
    private bool _isAttacking = false; // Saldırı modunda mıyız?

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
        var pData = SimResourceSystem.GetPlayer(_world, _playerID);

        int workerCount = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
        int soldierCount = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
        bool hasBarracks = myBuildings.Any(b => b.Type == SimBuildingType.Barracks);
        var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);

        // 1. İŞÇİ BAS (HEDEF 5)
        if (baseB != null && !baseB.IsTraining && workerCount < 5)
        {
            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
        }

        // 2. KIŞLA KUR (5 İŞÇİ VARSA)
        if (workerCount >= 5 && !hasBarracks)
        {
            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            {
                var worker = myUnits.FirstOrDefault(u => u.UnitType == SimUnitType.Worker);
                if (worker != null)
                {
                    int2 buildPos = FindBuildSpot(baseB.GridPosition);
                    if (buildPos.x != -1)
                    {
                        SimResourceSystem.SpendResources(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                        var barracks = SpawnBuildingPlaceholder(SimBuildingType.Barracks, buildPos);
                        SimUnitSystem.OrderBuild(worker, barracks, _world);
                    }
                }
            }
        }

        // 3. ASKER BAS (HEDEF 5)
        if (hasBarracks && soldierCount < 5)
        {
            _isAttacking = false; // Asker azaldıysa saldırıyı durdur, üretime dön
            var barracks = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Barracks && b.IsConstructed);
            if (barracks != null && !barracks.IsTraining)
            {
                if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                    SimBuildingSystem.StartTraining(barracks, _world, SimUnitType.Soldier);
            }
        }

        // 4. İŞÇİ YÖNETİMİ (HEDEFE GÖRE)
        ManageWorkersSmart(myUnits, pData, hasBarracks);

        // 5. SALDIRI (RUSH)
        if (soldierCount >= 5)
        {
            _isAttacking = true; // Saldırı modunu aç
        }

        if (_isAttacking)
        {
            AttackWithAllSoldiers(myUnits);
        }
    }

    private void ManageWorkersSmart(List<SimUnitData> myUnits, SimPlayerData pData, bool hasBarracks)
    {
        var workers = myUnits.Where(u => u.UnitType == SimUnitType.Worker).ToList();

        foreach (var w in workers)
        {
            // İnşaat yapan veya yürüyen işçiyi elleme
            if (w.State == SimTaskType.Building || (w.State == SimTaskType.Moving && w.TargetID != -1)) continue;

            SimResourceType targetType = SimResourceType.Wood;

            // KURAL 1: Önce 5 işçi parası (Et)
            if (myUnits.Count(u => u.UnitType == SimUnitType.Worker) < 5)
            {
                targetType = SimResourceType.Meat;
            }
            // KURAL 2: İşçi tamsa ve Kışla yoksa -> Kışla parası (Odun/Taş)
            else if (!hasBarracks)
            {
                if (pData.Wood < SimConfig.BARRACKS_COST_WOOD) targetType = SimResourceType.Wood;
                else targetType = SimResourceType.Stone;
            }
            // KURAL 3: Kışla varsa -> Asker parası (Et/Odun)
            else
            {
                if (pData.Meat < SimConfig.SOLDIER_COST_MEAT) targetType = SimResourceType.Meat;
                else targetType = SimResourceType.Wood;
            }

            // Şu anki işi doğru mu? Değilse değiştir.
            // (Basitlik için her turda yeniden atıyoruz)
            var res = FindNearestResource(w.GridPosition, targetType);
            if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);

            if (res != null && w.TargetID != res.ID)
                SimUnitSystem.TryAssignGatherTask(w, res, _world);
        }
    }

    private void AttackWithAllSoldiers(List<SimUnitData> myUnits)
    {
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);
        if (enemyBase == null) enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID); // Herhangi bir bina

        if (enemyBase != null)
        {
            foreach (var soldier in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
            {
                // Zaten saldırıyorsa elleme
                if (soldier.State == SimTaskType.Attacking && soldier.TargetID != -1) continue;

                // YOL KONTROLÜ
                bool canReach = IsReachable(soldier.GridPosition, enemyBase.GridPosition);
                if (canReach)
                {
                    SimUnitSystem.OrderAttack(soldier, enemyBase, _world);
                }
                else
                {
                    var breachTarget = FindClosestEnemyBuilding(soldier.GridPosition);
                    if (breachTarget != null) SimUnitSystem.OrderAttack(soldier, breachTarget, _world);
                }
            }
        }
    }

    // --- YARDIMCILAR (AYNI) ---
    private bool IsReachable(int2 start, int2 end)
    {
        int2? standPos = SimGridSystem.FindWalkableNeighbor(_world, end);
        if (standPos == null) return false;
        var path = SimGridSystem.FindPath(_world, start, standPos.Value);
        if (path.Count == 0 && start != standPos.Value) return false;
        return true;
    }

    private SimBuildingData FindClosestEnemyBuilding(int2 pos)
    {
        SimBuildingData best = null;
        float minDst = float.MaxValue;
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == _playerID) continue;
            float dst = SimGridSystem.GetDistanceSq(pos, b.GridPosition);
            if (dst < minDst) { minDst = dst; best = b; }
        }
        return best;
    }

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

    private SimResourceData FindNearestResource(int2 pos, SimResourceType type = SimResourceType.None)
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

    private SimUnitData FindNearestEnemyUnit(int2 pos)
    {
        SimUnitData best = null;
        float minDst = float.MaxValue;
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == _playerID || u.State == SimTaskType.Dead) continue;
            float d = SimGridSystem.GetDistanceSq(pos, u.GridPosition);
            if (d < minDst) { minDst = d; best = u; }
        }
        return best;
    }
}