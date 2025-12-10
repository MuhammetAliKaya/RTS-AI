using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System.Collections.Generic;

public class DRLActionTranslator
{
    private SimWorldState _world;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;
    private SimGridSystem _gridSystem;

    private const int MY_PLAYER_ID = 1;

    public DRLActionTranslator(SimWorldState world, SimUnitSystem unitSys, SimBuildingSystem buildSys, SimGridSystem gridSys)
    {
        _world = world;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;
        _gridSystem = gridSys;
    }

    // --- ANA ÇALIŞTIRMA FONKSİYONU ---
    public bool ExecuteAction(int actionType, int targetIndex = -1)
    {
        // Grid Index -> Koordinat Çevrimi (Mouse tıklamaları için)
        int2 targetPos = int2.Zero;
        if (targetIndex != -1)
        {
            int w = _world.Map.Width;
            targetPos = new int2(targetIndex % w, targetIndex / w);
        }
        Debug.Log($"[TRANSLATOR] Executing Type: {actionType} at Pos: {targetPos}");
        switch (actionType)
        {
            case 0: return true; // Bekle
            case 1: return TryBuildStructureAuto(SimBuildingType.House);
            case 2: return TryBuildStructureAuto(SimBuildingType.Barracks);
            case 3: return TryTrainUnit(SimUnitType.Worker);
            case 4: return TryTrainUnit(SimUnitType.Soldier);
            case 5: return CommandAllArmyAttackBase(); // Rush Saldırısı
            case 6: return CommandAllArmyAttackNearest(); // Yakına Saldır
            case 7: return CommandAutoGather(); // Otomatik Topla

            // --- MANUEL / MOUSE KOMUTLARI ---
            case 10: return CommandArmyManual(targetPos); // Sağ tık (Ordu)
            case 11: return CommandWorkerManual(targetPos); // Sol tık (İşçi)
        }
        return false;
    }

    // --- YENİ MANUEL KONTROL FONKSİYONLARI ---
    private bool CommandArmyManual(int2 targetPos)
    {
        if (!_world.Map.IsInBounds(targetPos)) return false;

        // Saldırılacak hedef var mı?
        var targetUnit = _world.Units.Values.FirstOrDefault(u => u.GridPosition == targetPos && u.PlayerID != MY_PLAYER_ID);
        var targetBuilding = _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == targetPos && b.PlayerID != MY_PLAYER_ID);

        var mySoldiers = _world.Units.Values.Where(u => u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier).ToList();
        if (mySoldiers.Count == 0) return false;

        foreach (var s in mySoldiers)
        {
            if (targetUnit != null) _unitSystem.OrderAttackUnit(s, targetUnit);
            else if (targetBuilding != null) _unitSystem.OrderAttack(s, targetBuilding);
            else _unitSystem.OrderMove(s, targetPos);
        }
        return true;
    }

    private bool CommandWorkerManual(int2 targetPos)
    {
        if (!_world.Map.IsInBounds(targetPos)) return false;

        // Hedefte kaynak var mı?
        var resource = _world.Resources.Values.FirstOrDefault(r => r.GridPosition == targetPos);

        // Boştaki bir işçiyi al (Yoksa en yakın herhangi bir işçiyi al)
        var worker = _world.Units.Values
            .Where(u => u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Worker)
            .OrderBy(u => u.State == SimTaskType.Idle ? 0 : 1) // Önce boşta olanlar
            .ThenBy(u => SimGridSystem.GetDistanceSq(u.GridPosition, targetPos)) // Sonra en yakın olan
            .FirstOrDefault();

        if (worker == null) return false;

        if (resource != null)
        {
            return _unitSystem.TryAssignGatherTask(worker, resource);
        }
        else
        {
            _unitSystem.OrderMove(worker, targetPos);
            return true;
        }
    }

    // --- OTOMATİK TOPLAMA (AUTO GATHER) ---
    private bool CommandAutoGather()
    {
        // Boşta duran veya sadece yürüyen (işsiz) işçileri bul
        var availableWorkers = _world.Units.Values.Where(u =>
            u.PlayerID == MY_PLAYER_ID &&
            u.UnitType == SimUnitType.Worker &&
            (u.State == SimTaskType.Idle || (u.State == SimTaskType.Moving && u.TargetID == -1))
        ).ToList();

        if (availableWorkers.Count == 0) return true; // İşçiler zaten çalışıyorsa başarılı say

        var resources = _world.Resources.Values.Where(r => r.AmountRemaining > 0).ToList();
        if (resources.Count == 0) return false;

        bool anyAction = false;
        foreach (var worker in availableWorkers)
        {
            SimResourceData bestRes = null;
            float minDst = float.MaxValue;

            foreach (var res in resources)
            {
                float d = SimGridSystem.GetDistanceSq(worker.GridPosition, res.GridPosition);
                if (d < minDst)
                {
                    minDst = d;
                    bestRes = res;
                }
            }

            if (bestRes != null)
            {
                if (_unitSystem.TryAssignGatherTask(worker, bestRes))
                    anyAction = true;
            }
        }
        return anyAction;
    }

    // --- YARDIMCI METOTLAR (ARTIK EKSİK DEĞİL) ---

    private bool TryBuildStructureAuto(SimBuildingType type)
    {
        if (!CanAfford(type)) return false;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == MY_PLAYER_ID && b.Type == SimBuildingType.Base);
        if (myBase == null) return false;

        // Base etrafında boş yer bul
        int2 bestPos = FindBuildPosition(myBase.GridPosition, 8); // Yarıçapı biraz artırdım
        if (bestPos.x == -1) return false;

        SimUnitData worker = FindWorker();
        if (worker == null) return false;

        SpendResources(type);
        SimBuildingData newBuilding = _buildingSystem.CreateBuilding(MY_PLAYER_ID, type, bestPos);

        _unitSystem.OrderBuild(worker, newBuilding);
        return true;
    }

    private bool TryTrainUnit(SimUnitType type)
    {
        var (w, s, m) = GetCost(type);
        if (!SimResourceSystem.CanAfford(_world, MY_PLAYER_ID, w, s, m)) return false;

        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == MY_PLAYER_ID && b.IsConstructed && !b.IsTraining)
            {
                if ((type == SimUnitType.Worker && b.Type == SimBuildingType.Base) ||
                   (type == SimUnitType.Soldier && b.Type == SimBuildingType.Barracks))
                {
                    SimBuildingSystem.StartTraining(b, _world, type);
                    return true;
                }
            }
        }
        return false;
    }

    private bool CommandAllArmyAttackBase()
    {
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != MY_PLAYER_ID && b.Type == SimBuildingType.Base);
        if (enemyBase == null) return false;

        bool attacked = false;
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier)
            {
                _unitSystem.OrderAttack(u, enemyBase);
                attacked = true;
            }
        }
        return attacked;
    }

    private bool CommandAllArmyAttackNearest()
    {
        // En yakın düşman ünitesi veya binasını bul
        SimUnitData closestEnemy = null;
        float minDist = float.MaxValue;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID != MY_PLAYER_ID)
            {
                // Basitlik için ilk bulduğumuza saldıralım ya da mesafeye bakabiliriz
                closestEnemy = u;
                break;
            }
        }

        if (closestEnemy != null)
        {
            foreach (var u in _world.Units.Values)
            {
                if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier)
                {
                    _unitSystem.OrderAttackUnit(u, closestEnemy);
                }
            }
            return true;
        }
        return false;
    }

    // --- ALT YARDIMCI FONKSİYONLAR ---

    private int2 FindBuildPosition(int2 center, int radius)
    {
        for (int r = 1; r <= radius; r++)
        {
            for (int x = center.x - r; x <= center.x + r; x++)
            {
                for (int y = center.y - r; y <= center.y + r; y++)
                {
                    int2 pos = new int2(x, y);
                    if (_world.Map.IsInBounds(pos) && _gridSystem.IsWalkable(pos))
                    {
                        if (_gridSystem.GetNode(x, y).Type == SimTileType.Grass) return pos;
                    }
                }
            }
        }
        return new int2(-1, -1);
    }

    private SimUnitData FindWorker()
    {
        return _world.Units.Values.FirstOrDefault(u => u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle)
            ?? _world.Units.Values.FirstOrDefault(u => u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Worker);
    }

    private bool CanAfford(SimBuildingType type)
    {
        int w = 0, s = 0, m = 0;
        if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; m = SimConfig.HOUSE_COST_MEAT; }
        else if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
        return SimResourceSystem.CanAfford(_world, MY_PLAYER_ID, w, s, m);
    }

    private void SpendResources(SimBuildingType type)
    {
        int w = 0, s = 0, m = 0;
        if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; m = SimConfig.HOUSE_COST_MEAT; }
        else if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
        SimResourceSystem.SpendResources(_world, MY_PLAYER_ID, w, s, m);
    }

    private (int, int, int) GetCost(SimUnitType type)
    {
        if (type == SimUnitType.Worker) return (SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        return (SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
    }
}