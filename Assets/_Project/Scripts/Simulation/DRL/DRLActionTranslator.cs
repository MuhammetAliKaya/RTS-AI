using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // SimConfig için

public class DRLActionTranslator
{
    private SimWorldState _world;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;
    private SimGridSystem _gridSystem;

    // Oyuncu ID'si (Bizim ajanımız)
    private const int MY_PLAYER_ID = 1;

    public DRLActionTranslator(SimWorldState world, SimUnitSystem unitSys, SimBuildingSystem buildSys, SimGridSystem gridSys)
    {
        _world = world;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;
        _gridSystem = gridSys;
    }

    /// <summary>
    /// AI'dan gelen kararı uygular ve SONUCU (Başarılı/Başarısız) döndürür.
    /// </summary>
    public bool ExecuteAction(int actionType, int targetX, int targetY)
    {
        // 1. Harita Dışı Kontrolü
        if (!_world.Map.IsInBounds(new int2(targetX, targetY))) return false;

        int2 targetPos = new int2(targetX, targetY);
        bool success = false;

        switch (actionType)
        {
            case 0: // Bekle
                return true;

            case 1: // İNŞA ET: EV
                success = TryBuildBuilding(SimBuildingType.House, targetX, targetY);
                break;
            case 2: // İNŞA ET: KIŞLA
                success = TryBuildBuilding(SimBuildingType.Barracks, targetX, targetY);
                break;
            case 3: // ÜRET: İŞÇİ
                success = TryTrainUnit(SimUnitType.Worker);
                break;
            case 4: // ÜRET: ASKER
                success = TryTrainUnit(SimUnitType.Soldier);
                break;
            case 5: // SALDIR
                success = CommandArmyAttack(targetPos);
                break;
            case 6: // TOPLA
                success = CommandGatherResource(targetPos);
                break;

            // --- YENİ EKLENEN EKONOMİK BİNALAR ---
            case 7: // İNŞA ET: ÇİFTLİK (FARM) -> ET ÜRETİMİ
                success = TryBuildBuilding(SimBuildingType.Farm, targetX, targetY);
                break;
            case 8: // İNŞA ET: ODUNCU (WOODCUTTER) -> ODUN ÜRETİMİ
                success = TryBuildBuilding(SimBuildingType.WoodCutter, targetX, targetY);
                break;
            case 9: // İNŞA ET: TAŞ OCAĞI (STONEPIT) -> TAŞ ÜRETİMİ
                success = TryBuildBuilding(SimBuildingType.StonePit, targetX, targetY);
                break;
        }
        return success;
    }

    // --- YARDIMCI EYLEMLER ---

    private bool TryBuildBuilding(SimBuildingType type, int x, int y)
    {
        int2 pos = new int2(x, y);

        // KONTROL 1: Yürünebilir mi?
        if (!_gridSystem.IsWalkable(pos)) return false;

        // KONTROL 2: Zemin Grass mı?
        var node = _gridSystem.GetNode(x, y);
        if (node.Type != SimTileType.Grass) return false;

        // KONTROL 3: İşçi Var mı?
        SimUnitData worker = FindBestWorker(pos);
        if (worker == null) return false;

        // KONTROL 4: Kaynak Var mı?
        int wood = 0, stone = 0, meat = 0;
        switch (type)
        {
            case SimBuildingType.House: wood = SimConfig.HOUSE_COST_WOOD; break;
            case SimBuildingType.Farm: wood = SimConfig.FARM_COST_WOOD; break;
            case SimBuildingType.WoodCutter: meat = SimConfig.WOODCUTTER_COST_MEAT; break;
            case SimBuildingType.StonePit: wood = SimConfig.STONEPIT_COST_WOOD; break;
            case SimBuildingType.Barracks: wood = SimConfig.BARRACKS_COST_WOOD; stone = SimConfig.BARRACKS_COST_STONE; break;
            case SimBuildingType.Tower: wood = SimConfig.TOWER_COST_WOOD; stone = SimConfig.TOWER_COST_STONE; break;
            case SimBuildingType.Wall: stone = SimConfig.WALL_COST_STONE; break;
        }

        if (!SimResourceSystem.CanAfford(_world, MY_PLAYER_ID, wood, stone, meat)) return false;

        // --- İŞLEM ---
        SimResourceSystem.SpendResources(_world, MY_PLAYER_ID, wood, stone, meat);

        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = MY_PLAYER_ID,
            Type = type,
            GridPosition = pos,
            IsConstructed = false,
            ConstructionProgress = 0
        };

        SimBuildingSystem.InitializeBuildingStats(b);
        _world.Buildings.Add(b.ID, b);

        if (node != null)
        {
            node.OccupantID = b.ID;
            node.IsWalkable = false;
        }

        _unitSystem.OrderBuild(worker, b);

        return true;
    }

    private bool TryTrainUnit(SimUnitType type)
    {
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == MY_PLAYER_ID && b.IsConstructed && !b.IsTraining)
            {
                if ((type == SimUnitType.Worker && b.Type == SimBuildingType.Base) ||
                    (type == SimUnitType.Soldier && b.Type == SimBuildingType.Barracks))
                {
                    // Not: Kaynak kontrolünü SimBuildingSystem.StartTraining içinde yapıyor
                    // ama bize bool dönmüyor. Şimdilik çağırıp true dönüyoruz.
                    SimBuildingSystem.StartTraining(b, _world, type);
                    return true;
                }
            }
        }
        return false;
    }

    private bool CommandArmyAttack(int2 targetPos)
    {
        var node = _gridSystem.GetNode(targetPos.x, targetPos.y);
        // Hedef geçersizse false dön
        if (node == null) return false;

        SimUnitData targetUnit = null;
        SimBuildingData targetBuilding = null;

        if (node.OccupantID != -1)
        {
            if (_world.Units.TryGetValue(node.OccupantID, out SimUnitData u))
            {
                if (u.PlayerID != MY_PLAYER_ID) targetUnit = u;
            }
            else if (_world.Buildings.TryGetValue(node.OccupantID, out SimBuildingData b))
            {
                if (b.PlayerID != MY_PLAYER_ID) targetBuilding = b;
            }
        }

        bool anyOrderGiven = false;
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier)
            {
                if (targetUnit != null) _unitSystem.OrderAttackUnit(u, targetUnit);
                else if (targetBuilding != null) _unitSystem.OrderAttack(u, targetBuilding);
                else _unitSystem.OrderMove(u, targetPos);

                anyOrderGiven = true;
            }
        }
        return anyOrderGiven;
    }

    private bool CommandGatherResource(int2 targetPos)
    {
        SimResourceData targetRes = null;
        foreach (var res in _world.Resources.Values)
        {
            if (res.GridPosition.x == targetPos.x && res.GridPosition.y == targetPos.y)
            {
                targetRes = res; break;
            }
        }

        if (targetRes != null)
        {
            SimUnitData worker = FindBestWorker(targetPos);
            if (worker != null)
            {
                _unitSystem.TryAssignGatherTask(worker, targetRes);
                return true;
            }
        }
        return false;
    }

    private SimUnitData FindBestWorker(int2 target)
    {
        SimUnitData best = null;
        float minDist = float.MaxValue;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle)
            {
                float dist = SimGridSystem.GetDistanceSq(u.GridPosition, target);
                if (dist < minDist)
                {
                    minDist = dist;
                    best = u;
                }
            }
        }

        // Boşta yoksa herhangi birini al (Acil durum)
        if (best == null)
        {
            foreach (var u in _world.Units.Values)
            {
                if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Worker) return u;
            }
        }
        return best;
    }
}