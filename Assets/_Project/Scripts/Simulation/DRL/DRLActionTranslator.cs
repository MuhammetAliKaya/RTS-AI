using UnityEngine;
using RTS.Simulation.Data; // int2 ve Enumlar buradan geliyor
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

    private int _myPlayerID;
    public DRLActionTranslator(SimWorldState world, SimUnitSystem unitSys, SimBuildingSystem buildSys, SimGridSystem gridSys, int playerID)
    {
        _world = world;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;
        _gridSystem = gridSys;
        _myPlayerID = playerID; // Oyuncu kimliğini kaydet
    }

    public bool IsUnitOwnedByPlayer(int gridIndex, int playerID)
    {
        // Vector2Int yerine int2 kullanıyoruz
        int2 pos = GetPosFromIndex(gridIndex);

        // 1. Önce Birim Var mı?
        var unit = GetUnitAt(pos);
        if (unit != null && unit.PlayerID == playerID) return true;

        // 2. Yoksa Bina Var mı?
        var building = GetBuildingAt(pos);
        if (building != null && building.PlayerID == playerID) return true;

        return false;
    }

    public SimUnitType GetUnitTypeAt(int gridIndex)
    {
        var unit = GetUnitAt(GetPosFromIndex(gridIndex));
        // HATA DÜZELTME: SimUnitType.None tanımlı değil.
        // Eğer birim yoksa -1 döndürerek (Undefined) olduğunu belirtiyoruz.
        return unit != null ? unit.UnitType : (SimUnitType)(-1);
    }

    public SimBuildingType GetBuildingTypeAt(int gridIndex)
    {
        var building = GetBuildingAt(GetPosFromIndex(gridIndex));
        return building != null ? building.Type : SimBuildingType.None;
    }

    public float GetEncodedTypeAt(int gridIndex)
    {
        int2 pos = GetPosFromIndex(gridIndex);

        var unit = GetUnitAt(pos);
        if (unit != null)
        {
            // Örnek Kodlama: 0.1 = Worker, 0.2 = Soldier
            if (unit.UnitType == SimUnitType.Worker) return 0.1f;
            if (unit.UnitType == SimUnitType.Soldier) return 0.2f;
        }

        var building = GetBuildingAt(pos);
        if (building != null)
        {
            // Örnek Kodlama: 0.5 = Base, 0.6 = Barracks
            if (building.Type == SimBuildingType.Base) return 0.5f;
            if (building.Type == SimBuildingType.Barracks) return 0.6f;
            // Diğer binalar...
            return 0.7f;
        }

        return 0f; // Boş veya tanımsız
    }

    public bool IsTargetValidForAction(int actionType, int targetIndex)
    {
        int2 targetPos = GetPosFromIndex(targetIndex);
        if (!_world.Map.IsInBounds(targetPos)) return false;

        // Hedef karesinde ne var?
        var unitAtTarget = GetUnitAt(targetPos);
        var buildingAtTarget = GetBuildingAt(targetPos);
        bool isWalkable = SimGridSystem.IsWalkable(_world, targetPos);
        bool hasResource = _world.Resources.Values.Any(r => r.GridPosition == targetPos);

        // Kendi birimim/binam mı? (Saldırı için önemli)
        bool isMyUnit = (unitAtTarget != null && unitAtTarget.PlayerID == _myPlayerID);
        bool isMyBuilding = (buildingAtTarget != null && buildingAtTarget.PlayerID == _myPlayerID);

        // Düşman mı?
        bool isEnemyUnit = (unitAtTarget != null && unitAtTarget.PlayerID != _myPlayerID);
        bool isEnemyBuilding = (buildingAtTarget != null && buildingAtTarget.PlayerID != _myPlayerID);

        switch (actionType)
        {
            case 0: return true; // Wait

            // İNŞAATLAR (1-9) - (Aynı Kalıyor)
            case 1:
            case 2:
            case 5:
            case 6:
            case 7:
            case 8:
            case 9:
                return isWalkable && unitAtTarget == null && buildingAtTarget == null && !hasResource;

            // ÜRETİM (3-4) - (Aynı Kalıyor)
            case 3:
            case 4:
                return isWalkable && buildingAtTarget == null; // Rally point

            // --- YENİ AYRIŞTIRILMIŞ EYLEMLER ---

            case 10: // ATTACK (Sadece Düşman Varsa Geçerli)
                return isEnemyUnit || isEnemyBuilding;

            case 11: // MOVE (Sadece Boş ve Yürünebilir ise Geçerli)
                     // Birim, Bina veya Kaynak varsa oraya "Yürü" emri verilemez (Oraya saldırılır veya toplanır)
                return isWalkable && unitAtTarget == null && buildingAtTarget == null && !hasResource;

            case 12: // GATHER (Sadece Kaynak Varsa Geçerli)
                return hasResource;

            default: return false;
        }
    }

    // HATA DÜZELTME: Dönüş tipi Vector2Int yerine int2 yapıldı
    private int2 GetPosFromIndex(int index)
    {
        int x = index % _world.Map.Width;
        int y = index / _world.Map.Width;
        return new int2(x, y);
    }

    // HATA DÜZELTME: Parametre int2 yapıldı
    private SimUnitData GetUnitAt(int2 pos)
    {
        // int2 struct olduğu için LINQ sorgusunda == operatörü düzgün çalışır
        return _world.Units.Values.FirstOrDefault(u => u.GridPosition == pos && u.State != SimTaskType.Dead);
    }

    // HATA DÜZELTME: Parametre int2 yapıldı
    private SimBuildingData GetBuildingAt(int2 pos)
    {
        return _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == pos && b.Health > 0);
    }

    // --- ANA ÇALIŞTIRMA FONKSİYONU ---
    public bool ExecuteAction(int actionType, int sourceIndex, int targetIndex)
    {
        // 1. KOORDİNAT HESAPLA (int2 kullanarak)
        int w = _world.Map.Width;
        int2 targetPos = new int2(targetIndex % w, targetIndex / w);
        int2 sourcePos = new int2(sourceIndex % w, sourceIndex / w);

        // 2. KAYNAK VARLIĞI BUL (Source Location Check)
        SimUnitData sourceUnit = GetUnitAt(sourcePos);
        // Unit kontrolü: Sadece benim olmalı
        if (sourceUnit != null && sourceUnit.PlayerID != _myPlayerID) sourceUnit = null;

        SimBuildingData sourceBuilding = GetBuildingAt(sourcePos);
        // Bina kontrolü: Sadece benim olmalı
        if (sourceBuilding != null && sourceBuilding.PlayerID != _myPlayerID) sourceBuilding = null;

        // Eğer kaynak boşsa ve eylem "Bekle" değilse -> HATA
        if (sourceUnit == null && sourceBuilding == null && actionType != 0)
            return false;

        // 3. AKSİYONLARI UYGULA
        switch (actionType)
        {
            case 0: return true; // Bekle

            // --- İNŞAAT (Kaynak: WORKER olmalı) ---
            case 1: return TryBuild(sourceUnit, SimBuildingType.House, targetPos);
            case 2: return TryBuild(sourceUnit, SimBuildingType.Barracks, targetPos);
            // 3 ve 4 Üretim için ayrıldı
            case 5: return TryBuild(sourceUnit, SimBuildingType.WoodCutter, targetPos);
            case 6: return TryBuild(sourceUnit, SimBuildingType.StonePit, targetPos);
            case 7: return TryBuild(sourceUnit, SimBuildingType.Farm, targetPos);
            case 8: return TryBuild(sourceUnit, SimBuildingType.Tower, targetPos);
            case 9: return TryBuild(sourceUnit, SimBuildingType.Wall, targetPos);

            // --- ÜRETİM (Kaynak: BİNA olmalı) ---
            // Target Index üretimde önemsizdir ama validasyon için kullanılabilir.
            case 3: return TryTrain(sourceBuilding, SimUnitType.Worker);
            case 4: return TryTrain(sourceBuilding, SimUnitType.Soldier);

            case 10: // SALDIR
                if (sourceUnit == null) return false;
                // Hedefte ne var?
                var enemyUnit = _world.Units.Values.FirstOrDefault(u => u.GridPosition == targetPos && u.PlayerID != _myPlayerID);
                var enemyBuilding = _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == targetPos && b.PlayerID != _myPlayerID);

                if (enemyUnit != null) { _unitSystem.OrderAttackUnit(sourceUnit, enemyUnit); return true; }
                if (enemyBuilding != null) { _unitSystem.OrderAttack(sourceUnit, enemyBuilding); return true; }
                return false;

            case 11: // YÜRÜ
                if (sourceUnit == null) return false;
                // Zaten Validasyon yapıldığı için buranın boş olduğunu varsayıyoruz ama yine de check atılabilir.
                if (SimGridSystem.IsWalkable(_world, targetPos))
                {
                    _unitSystem.OrderMove(sourceUnit, targetPos);
                    return true;
                }
                return false;

            case 12: // TOPLA
                if (sourceUnit == null || sourceUnit.UnitType != SimUnitType.Worker) return false; // Sadece işçi toplar
                var resource = _world.Resources.Values.FirstOrDefault(r => r.GridPosition == targetPos);

                if (resource != null)
                {
                    return _unitSystem.TryAssignGatherTask(sourceUnit, resource);
                }
                return false;
        }
        return false;
    }

    // --- YARDIMCI METOTLAR ---
    private bool TryBuild(SimUnitData worker, SimBuildingType type, int2 buildPos)
    {
        // Kaynak bir işçi mi?
        if (worker == null || worker.UnitType != SimUnitType.Worker) return false;

        // Kaynak (Resource) kontrolü
        if (!CanAfford(type)) return false;

        // Yer uygun mu? (Harita sınırları ve yürünebilirlik)
        // SimGridSystem.IsWalkable statik çağrısı int2 alır
        if (!SimGridSystem.IsWalkable(_world, buildPos)) return false;

        // O karede zaten bir bina var mı? (Ekstra güvenlik)
        if (_world.Buildings.Values.Any(b => b.GridPosition == buildPos)) return false;

        // İnşaat işlemini başlat
        SpendResources(type);
        // SimBuildingSystem.CreateBuilding factory methodunu kullanıyoruz
        SimBuildingData newBuilding = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, buildPos);

        // İşçiye emri ver
        _unitSystem.OrderBuild(worker, newBuilding);
        return true;
    }

    private bool TryTrain(SimBuildingData building, SimUnitType unitType)
    {
        // Bina var mı, inşaatı bitmiş mi, şu an meşgul mü?
        if (building == null || !building.IsConstructed || building.IsTraining) return false;

        // Doğru bina doğru üniteyi mi basıyor?
        if (unitType == SimUnitType.Worker && building.Type != SimBuildingType.Base) return false;
        if (unitType == SimUnitType.Soldier && building.Type != SimBuildingType.Barracks) return false;

        var (wood, stone, meat) = GetUnitCost(unitType);
        if (!SimResourceSystem.CanAfford(_world, _myPlayerID, wood, stone, meat)) return false;

        SimBuildingSystem.StartTraining(building, _world, unitType);
        return true;
    }

    private bool CommandUnitSmart(SimUnitData unit, int2 targetPos)
    {
        if (unit == null) return false;
        if (!_world.Map.IsInBounds(targetPos)) return false;

        // Hedef karesinde ne var?
        var enemyUnit = _world.Units.Values.FirstOrDefault(u => u.GridPosition == targetPos && u.PlayerID != _myPlayerID);
        var enemyBuilding = _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == targetPos && b.PlayerID != _myPlayerID);
        var resource = _world.Resources.Values.FirstOrDefault(r => r.GridPosition == targetPos);

        // 1. Düşman Ünitesi -> Saldır
        if (enemyUnit != null)
        {
            _unitSystem.OrderAttackUnit(unit, enemyUnit);
            return true;
        }
        // 2. Düşman Binası -> Saldır
        else if (enemyBuilding != null)
        {
            _unitSystem.OrderAttack(unit, enemyBuilding);
            return true;
        }
        // 3. Kaynak -> İşçiyse Topla
        else if (resource != null && unit.UnitType == SimUnitType.Worker)
        {
            return _unitSystem.TryAssignGatherTask(unit, resource);
        }

        // 4. Boş Alan -> Yürü
        _unitSystem.OrderMove(unit, targetPos);
        return true;
    }

    // --- MALİYET YÖNETİMİ ---

    private (int wood, int stone, int meat) GetBuildingCost(SimBuildingType type)
    {
        switch (type)
        {
            case SimBuildingType.House: return (SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
            case SimBuildingType.Barracks: return (SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
            case SimBuildingType.WoodCutter: return (SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT);
            case SimBuildingType.StonePit: return (SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT);
            case SimBuildingType.Farm: return (SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);
            case SimBuildingType.Tower: return (SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
            case SimBuildingType.Wall: return (SimConfig.WALL_COST_WOOD, SimConfig.WALL_COST_STONE, SimConfig.WALL_COST_MEAT);
            default: return (0, 0, 0);
        }
    }

    private (int wood, int stone, int meat) GetUnitCost(SimUnitType type)
    {
        if (type == SimUnitType.Worker)
            return (SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        // Soldier
        return (SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
    }

    private bool CanAfford(SimBuildingType type)
    {
        var (w, s, m) = GetBuildingCost(type);
        return SimResourceSystem.CanAfford(_world, _myPlayerID, w, s, m);
    }

    private void SpendResources(SimBuildingType type)
    {
        var (w, s, m) = GetBuildingCost(type);
        SimResourceSystem.SpendResources(_world, _myPlayerID, w, s, m);
    }
    public SimUnitData GetUnitAtPosIndex(int index)
    {
        int2 pos = GetPosFromIndex(index);
        return GetUnitAt(pos);
    }
}