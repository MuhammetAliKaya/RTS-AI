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

    // --- ANA ÇALIŞTIRMA FONKSİYONU (GÜNCELLENDİ: Source + Target) ---
    public bool ExecuteAction(int actionType, int sourceIndex, int targetIndex)
    {
        // 1. KOORDİNAT HESAPLA
        int w = _world.Map.Width;

        // Target (Hedef) Koordinatı
        int2 targetPos = new int2(targetIndex % w, targetIndex / w);

        // Source (Kaynak) Koordinatı - Eylemi kim yapacak?
        int2 sourcePos = new int2(sourceIndex % w, sourceIndex / w);

        // Debug.Log($"[TRANSLATOR] Act:{actionType} | Src:{sourcePos} | Tgt:{targetPos}");

        // 2. KAYNAK VARLIĞI BUL (Source Location Check)
        // Bu koordinatta benim bir ünitem veya binam var mı?
        SimUnitData sourceUnit = _world.Units.Values.FirstOrDefault(u => u.GridPosition == sourcePos && u.PlayerID == MY_PLAYER_ID);
        SimBuildingData sourceBuilding = _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == sourcePos && b.PlayerID == MY_PLAYER_ID);

        // Eğer kaynak boşsa (o karede benim adamım yoksa) ve eylem "Bekle" değilse -> HATA
        if (sourceUnit == null && sourceBuilding == null && actionType != 0)
            return false;

        // 3. AKSİYONLARI UYGULA
        switch (actionType)
        {
            case 0: return true; // Bekle

            // --- İNŞAAT (Kaynak: WORKER olmalı) ---
            // 1. Ev (House)
            case 1: return TryBuild(sourceUnit, SimBuildingType.House, targetPos);
            // 2. Kışla (Barracks)
            case 2: return TryBuild(sourceUnit, SimBuildingType.Barracks, targetPos);
            // 5. Oduncu (Woodcutter) - YENİ
            case 5: return TryBuild(sourceUnit, SimBuildingType.WoodCutter, targetPos);
            // 6. Taş Ocağı (StonePit) - YENİ
            case 6: return TryBuild(sourceUnit, SimBuildingType.StonePit, targetPos);
            // 7. Çiftlik (Farm) - YENİ
            case 7: return TryBuild(sourceUnit, SimBuildingType.Farm, targetPos);
            // 8. Kule (Tower)
            case 8: return TryBuild(sourceUnit, SimBuildingType.Tower, targetPos);
            // 9. Duvar (Wall)
            case 9: return TryBuild(sourceUnit, SimBuildingType.Wall, targetPos);

            // --- ÜRETİM (Kaynak: BİNA olmalı) ---
            // Target Index üretimde önemsizdir, bina kendi içinde üretir.
            case 3: return TryTrain(sourceBuilding, SimUnitType.Worker);
            case 4: return TryTrain(sourceBuilding, SimUnitType.Soldier);

            // --- HAREKET / SALDIRI / TOPLAMA (Kaynak: UNIT olmalı) ---
            // Akıllı Komut (Hedefe göre Move/Attack/Gather kararı verir)
            case 10: return CommandUnitSmart(sourceUnit, targetPos);
        }
        return false;
    }

    // --- YENİ YARDIMCI METOTLAR ---

    private bool TryBuild(SimUnitData worker, SimBuildingType type, int2 buildPos)
    {
        // Kaynak bir işçi mi?
        if (worker == null || worker.UnitType != SimUnitType.Worker) return false;

        // Kaynak (Resource) kontrolü
        if (!CanAfford(type)) return false;

        // Yer uygun mu? (Harita içi mi, yürünebilir mi?)
        if (!_world.Map.IsInBounds(buildPos) || !_gridSystem.IsWalkable(buildPos)) return false;

        // İnşaat işlemini başlat
        SpendResources(type);
        SimBuildingData newBuilding = _buildingSystem.CreateBuilding(MY_PLAYER_ID, type, buildPos);

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

        var (w, s, m) = GetCost(unitType);
        if (!SimResourceSystem.CanAfford(_world, MY_PLAYER_ID, w, s, m)) return false;

        SimBuildingSystem.StartTraining(building, _world, unitType);
        return true;
    }

    private bool CommandUnitSmart(SimUnitData unit, int2 targetPos)
    {
        if (unit == null) return false;
        if (!_world.Map.IsInBounds(targetPos)) return false;

        // Hedef karesinde ne var?
        var enemyUnit = _world.Units.Values.FirstOrDefault(u => u.GridPosition == targetPos && u.PlayerID != MY_PLAYER_ID);
        var enemyBuilding = _world.Buildings.Values.FirstOrDefault(b => b.GridPosition == targetPos && b.PlayerID != MY_PLAYER_ID);
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
        // 3. Kaynak -> İşçiyse Topla (x konumundakini topla)
        else if (resource != null && unit.UnitType == SimUnitType.Worker)
        {
            return _unitSystem.TryAssignGatherTask(unit, resource);
        }

        // 4. Boş Alan -> Yürü (x konumuna yürü/saldır)
        _unitSystem.OrderMove(unit, targetPos);
        return true;
    }

    // --- MALİYET KONTROLLERİ ---

    private bool CanAfford(SimBuildingType type)
    {
        int w = 0, s = 0, m = 0;
        if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; m = SimConfig.HOUSE_COST_MEAT; }
        else if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
        // YENİ BİNA MALİYETLERİ
        else if (type == SimBuildingType.WoodCutter) { w = SimConfig.WOODCUTTER_COST_WOOD; }
        else if (type == SimBuildingType.StonePit) { s = SimConfig.STONEPIT_COST_STONE; }
        else if (type == SimBuildingType.Farm) { m = SimConfig.FARM_COST_MEAT; }
        // MEVCUT DİĞER BİNALAR
        else if (type == SimBuildingType.Tower) { w = 100; s = 50; } // Config'e eklenebilir
        else if (type == SimBuildingType.Wall) { w = 20; s = 10; }
        return SimResourceSystem.CanAfford(_world, MY_PLAYER_ID, w, s, m);
    }

    private void SpendResources(SimBuildingType type)
    {
        int w = 0, s = 0, m = 0;
        if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; m = SimConfig.HOUSE_COST_MEAT; }
        else if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
        // YENİ BİNA MALİYETLERİ
        else if (type == SimBuildingType.WoodCutter) { w = SimConfig.WOODCUTTER_COST_WOOD; }
        else if (type == SimBuildingType.StonePit) { s = SimConfig.STONEPIT_COST_STONE; }
        else if (type == SimBuildingType.Farm) { m = SimConfig.FARM_COST_MEAT; }
        // MEVCUT DİĞER BİNALAR
        else if (type == SimBuildingType.Tower) { w = 100; s = 50; }
        else if (type == SimBuildingType.Wall) { w = 20; s = 10; }
        SimResourceSystem.SpendResources(_world, MY_PLAYER_ID, w, s, m);
    }

    private (int, int, int) GetCost(SimUnitType type)
    {
        if (type == SimUnitType.Worker) return (SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        return (SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
    }
}