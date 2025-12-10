using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;

public class DRLActionTranslator
{
    private SimWorldState _world;
    private SimUnitSystem _unitSystem; // Senin mevcut sistemin
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

    public bool ExecuteAction(int actionType)
    {
        // TargetX ve TargetY parametrelerini kaldırdık.
        // Artık "En mantıklı hedefi" biz kodla bulup senin sistemine (unitSystem) veriyoruz.

        switch (actionType)
        {
            case 0: return true; // Bekle
            case 1: return TryBuildStructureAuto(SimBuildingType.House);
            case 2: return TryBuildStructureAuto(SimBuildingType.Barracks);
            case 3: return TryTrainUnit(SimUnitType.Worker);
            case 4: return TryTrainUnit(SimUnitType.Soldier);
            case 5: return CommandAllArmyAttackBase(); // Rush Saldırısı
            case 6: return CommandAllArmyAttackNearest(); // Yakına Saldır
            case 7: return CommandAutoGather(); // <--- İŞTE BURASI DÜZELDİ
        }
        return false;
    }

    // --- SENİN SİSTEMİNİ KULLANAN YENİ GATHER MANTIĞI ---
    private bool CommandAutoGather()
    {
        // 1. Boşta duran işçilerimi bul
        var idleWorkers = _world.Units.Values.Where(u =>
            u.PlayerID == MY_PLAYER_ID &&
            u.UnitType == SimUnitType.Worker &&
            u.State == SimTaskType.Idle
        ).ToList();

        if (idleWorkers.Count == 0) return true; // İşçiler zaten çalışıyorsa başarılı say (Ceza verme)

        // 2. Haritadaki kaynakları tara
        var resources = _world.Resources.Values.Where(r => r.AmountRemaining > 0).ToList();
        if (resources.Count == 0) return false;

        bool anyCommandGiven = false;

        foreach (var worker in idleWorkers)
        {
            // İşçiye en yakın kaynağı bul
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

            // 3. SENİN VAR OLAN FONKSİYONUNU ÇAĞIR
            if (bestRes != null)
            {
                // SimUnitSystem içindeki TryAssignGatherTask zaten pathfinding ve state değişimini yapıyor.
                // Biz sadece doğru hedefi (bestRes) ona veriyoruz.
                bool result = _unitSystem.TryAssignGatherTask(worker, bestRes);
                if (result) anyCommandGiven = true;
            }
        }

        return anyCommandGiven;
    }

    // --- DİĞER YARDIMCI METOTLAR (Önceki mantıkla aynı) ---
    private bool TryBuildStructureAuto(SimBuildingType type)
    {
        if (!CanAfford(type)) return false;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == MY_PLAYER_ID && b.Type == SimBuildingType.Base);
        if (myBase == null) return false;

        // Base etrafında boş yer bul
        int2 bestPos = FindBuildPosition(myBase.GridPosition, 5);
        if (bestPos.x == -1) return false;

        SimUnitData worker = FindWorker();
        if (worker == null) return false;

        SpendResources(type);
        SimBuildingData newBuilding = _buildingSystem.CreateBuilding(MY_PLAYER_ID, type, bestPos);

        // Senin sisteminle inşa emri
        _unitSystem.OrderBuild(worker, newBuilding);
        return true;
    }

    private bool TryTrainUnit(SimUnitType type)
    {
        // ... (Önceki cevaptaki mantığın aynısı, senin sistemine bağlı) ...
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

    // --- SALDIRI KISMI (SENİN SİSTEMİNLE) ---
    private bool CommandAllArmyAttackBase()
    {
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != MY_PLAYER_ID && b.Type == SimBuildingType.Base);
        if (enemyBase == null) return false;

        bool attacked = false;
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier)
            {
                // Senin saldırı fonksiyonun
                _unitSystem.OrderAttack(u, enemyBase);
                attacked = true;
            }
        }
        return attacked;
    }

    private bool CommandAllArmyAttackNearest()
    {
        var enemy = _world.Units.Values.FirstOrDefault(u => u.PlayerID != MY_PLAYER_ID);
        if (enemy == null) return false;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == MY_PLAYER_ID && u.UnitType == SimUnitType.Soldier)
            {
                _unitSystem.OrderAttackUnit(u, enemy);
            }
        }
        return true;
    }

    // --- UTILS ---
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