using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;
using int2 = RTS.Simulation.Data.int2;

public class RusherAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;
    private float _interval = 0.5f;
    private const int TARGET_WORKER_COUNT = 4;

    public RusherAI(SimWorldState world, int playerID)
    {
        _world = world;
        _myPlayerID = playerID;
        Debug.Log("[RusherAI] Başlatıldı. Nüfus kontrolü aktif.");
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < _interval) return;
        _timer = 0;

        if (_world == null || !_world.Players.ContainsKey(_myPlayerID)) return;
        var me = _world.Players[_myPlayerID];

        // --- 0. NÜFUS KONTROLÜ ---
        if (CheckPopulationCap(me)) return;

        // --- DURUM ---
        int workerCount = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker);
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        bool hasBarracks = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks);
        var completedBarracks = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks && b.IsConstructed);

        // 1. İşçi
        if (workerCount < TARGET_WORKER_COUNT && baseB != null && !baseB.IsTraining)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            else
                AssignGatherTasks(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
            return;
        }

        // 2. Kışla
        if (!hasBarracks)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            {
                var builder = GetIdleWorker();
                if (builder != null) TryBuildBuilding(builder, SimBuildingType.Barracks);
            }
            else
            {
                AssignGatherTasks(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
            }
        }
        else if (completedBarracks != null)
        {
            // 3. Asker ve Savaş
            if (!completedBarracks.IsTraining)
            {
                if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                    SimBuildingSystem.StartTraining(completedBarracks, _world, SimUnitType.Soldier);
                else
                    AssignGatherTasks(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
            }
            HandleCombat();
            CheckResourceStarvation();
        }
    }

    // --- NÜFUS YÖNETİMİ ---
    private bool CheckPopulationCap(SimPlayerData me)
    {
        if (me.CurrentPopulation >= me.MaxPopulation)
        {
            bool isBuildingHouse = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.House && !b.IsConstructed);
            if (!isBuildingHouse)
            {
                if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
                {
                    var builder = GetIdleWorker();
                    if (builder != null)
                    {
                        TryBuildBuilding(builder, SimBuildingType.House);
                        Debug.Log("[RusherAI] Nüfus doldu! Ev yapılıyor.");
                        return true;
                    }
                }
                else
                {
                    AssignGatherTasks(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                }
            }
        }
        return false;
    }

    private void CheckResourceStarvation()
    {
        int mapWood = _world.Resources.Values.Count(r => r.Type == SimResourceType.Wood && r.AmountRemaining > 0);
        int myWoodCutters = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.WoodCutter);

        if (mapWood < 5 && myWoodCutters == 0)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WOODCUTTER_COST_WOOD, 0, 0))
            {
                var w = GetIdleWorker();
                if (w != null) TryBuildBuilding(w, SimBuildingType.WoodCutter);
            }
        }
    }

    private void HandleCombat()
    {
        var soldiers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle).ToList();
        if (soldiers.Count >= 3)
        {
            var enemyUnit = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);
            if (enemyUnit != null) { foreach (var s in soldiers) SimUnitSystem.OrderAttackUnit(s, enemyUnit, _world); return; }

            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);
            if (enemyBase == null) enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID);

            if (enemyBase != null) { foreach (var s in soldiers) SimUnitSystem.OrderAttack(s, enemyBase, _world); }
        }
    }

    private void AssignGatherTasks(int reqW, int reqS, int reqM)
    {
        var me = _world.Players[_myPlayerID];
        SimResourceType t = SimResourceType.None;
        if (me.Meat < reqM) t = SimResourceType.Meat;
        else if (me.Wood < reqW) t = SimResourceType.Wood;
        else if (me.Stone < reqS) t = SimResourceType.Stone;
        if (t == SimResourceType.None) return;

        var workers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
        foreach (var w in workers)
        {
            var r = FindNearestResource(w.GridPosition, t);
            if (r != null) SimUnitSystem.TryAssignGatherTask(w, r, _world);
        }
    }

    private SimUnitData GetIdleWorker() => _world.Units.Values.FirstOrDefault(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);

    private SimResourceData FindNearestResource(int2 pos, SimResourceType type)
    {
        SimResourceData best = null;
        float minDst = float.MaxValue;
        foreach (var res in _world.Resources.Values)
        {
            if (res.Type == type && res.AmountRemaining > 0)
            {
                float dx = pos.x - res.GridPosition.x;
                float dy = pos.y - res.GridPosition.y;
                float dst = dx * dx + dy * dy;
                if (dst < minDst) { minDst = dst; best = res; }
            }
        }
        return best;
    }

    private void TryBuildBuilding(SimUnitData worker, SimBuildingType type)
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (baseB == null) return;
        for (int i = 0; i < 50; i++)
        {
            int2 offset = new int2(UnityEngine.Random.Range(-6, 7), UnityEngine.Random.Range(-6, 7));
            int2 pos = baseB.GridPosition + offset;
            if (_world.Map.IsInBounds(pos) && _world.Map.Grid[pos.x, pos.y].IsWalkable)
            {
                int w = 0, s = 0, m = 0;
                if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
                else if (type == SimBuildingType.WoodCutter) { w = SimConfig.WOODCUTTER_COST_WOOD; }
                else if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; s = SimConfig.HOUSE_COST_STONE; m = SimConfig.HOUSE_COST_MEAT; } // Ev

                SimResourceSystem.SpendResources(_world, _myPlayerID, w, s, m);
                var b = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                break;
            }
        }
    }
}