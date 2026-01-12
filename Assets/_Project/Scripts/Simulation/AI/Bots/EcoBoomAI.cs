using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;
using int2 = RTS.Simulation.Data.int2;

public class EcoBoomAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;

    // --- AYARLAR ---
    private const int TARGET_WORKER_COUNT = 15; // Hedef işçi sayısı
    private const int ECO_BUILDINGS_PER_TOWER = 9; // Her 9 ekonomi binası için 1 kule

    // --- SAVAŞ TETİKLEYİCİLERİ ---
    private const int ATTACK_TRIGGER_SOLDIER_COUNT = 10; // 10 Asker olunca saldır
    private const int MAX_BARRACKS = 3; // Parayı hızlı harcamak için çoklu kışla

    private bool _warModeActive = false; // Savaş modu kilidi

    public EcoBoomAI(SimWorldState w, int id)
    {
        _world = w;
        _myPlayerID = id;
        Debug.Log("[EcoBoomAI v3] Hazır. Izgara Yerleşimi + Geç Oyun Saldırısı.");
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < 0.25f) return;
        _timer = 0;

        if (_world == null) return;
        var me = _world.Players[_myPlayerID];

        // --- 0. SAVAŞ MODU KONTROLÜ ---
        // İşçi sayısı tamamlandıysa ve kaynaklar taşıyorsa savaş modunu aç
        int workerCount = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker);
        if (!_warModeActive)
        {
            if (workerCount >= TARGET_WORKER_COUNT && (me.Wood > 2500 || me.Meat > 2500))
            {
                _warModeActive = true;
                Debug.Log("[EcoBoomAI] EKONOMİ TAMAMLANDI! SAVAŞ MODUNA GEÇİLİYOR!");
            }
        }

        // --- 1. NÜFUS (Her zaman öncelikli, asker basmak için de lazım) ---
        if (me.CurrentPopulation >= me.MaxPopulation)
        {
            if (!TryBuildBuilding(SimBuildingType.House, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
                AssignSmartGatherTasks(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
            return;
        }

        // --- 2. SAVAŞ MODU (War Mode) ---
        if (_warModeActive)
        {
            ManageWarLogic(me);
            return; // Savaş modundaysak aşağıya (sadece ekonomi kasmaya) inme
        }

        // --- 3. EKONOMİ MODU (Eco Mode) ---

        // A. İşçi Bas
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (workerCount < TARGET_WORKER_COUNT && baseB != null && !baseB.IsTraining)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            else
                AssignSmartGatherTasks(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
            return;
        }

        // B. Savunma (Kule)
        if (CheckDefenseNeeds())
        {
            if (!TryBuildBuilding(SimBuildingType.Tower, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT))
                AssignSmartGatherTasks(SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
            return;
        }

        // C. Ekonomi Büyüt
        ManageEconomyExpansion();
    }

    // =================================================================================================
    //                                      SAVAŞ MANTIĞI
    // =================================================================================================
    private void ManageWarLogic(SimPlayerData me)
    {
        // 1. Kışla Kontrolü (Yeterince kışlamız var mı?)
        int barracksCount = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks);

        if (barracksCount < MAX_BARRACKS)
        {
            if (TryBuildBuilding(SimBuildingType.Barracks, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
                return; // Yapmaya gittik

            // Yapamadıysak kaynak toplayalım ama işi kilitlemeyelim, asker de basmaya çalışalım
            if (me.Wood < SimConfig.BARRACKS_COST_WOOD || me.Stone < SimConfig.BARRACKS_COST_STONE)
                AssignSmartGatherTasks(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
        }

        // 2. Asker Basımı (Tüm kışlalardan)
        var myBarracks = _world.Buildings.Values.Where(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks && b.IsConstructed).ToList();
        bool isTraining = false;
        foreach (var barrack in myBarracks)
        {
            if (!barrack.IsTraining)
            {
                if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                {
                    SimBuildingSystem.StartTraining(barrack, _world, SimUnitType.Soldier);
                    isTraining = true;
                }
            }
        }

        // Eğer asker basamıyorsak ve bina yapmıyorsak -> Kaynak Topla (Asker için)
        if (!isTraining && barracksCount >= 1)
        {
            AssignSmartGatherTasks(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        }

        // 3. Saldırı Emri
        int soldierCount = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier);
        if (soldierCount >= ATTACK_TRIGGER_SOLDIER_COUNT)
        {
            PerformAttack();
        }
    }

    private void PerformAttack()
    {
        var soldiers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle).ToList();
        if (soldiers.Count == 0) return;

        // Hedef Seçimi: Önce Asker, Sonra Üs
        SimUnitData enemyUnit = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);
        SimBuildingData enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);

        if (enemyBase == null) enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID); // Herhangi bir bina

        foreach (var s in soldiers)
        {
            if (enemyUnit != null) SimUnitSystem.OrderAttackUnit(s, enemyUnit, _world);
            else if (enemyBase != null) SimUnitSystem.OrderAttack(s, enemyBase, _world);
            else SimUnitSystem.OrderMove(s, new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2), _world); // Ortaya git
        }
    }

    // =================================================================================================
    //                                      EKONOMİ MANTIĞI
    // =================================================================================================

    private bool CheckDefenseNeeds()
    {
        int ecoCount = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && (b.Type == SimBuildingType.Farm || b.Type == SimBuildingType.WoodCutter || b.Type == SimBuildingType.StonePit));
        int towerCount = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Tower);

        // 9 binaya 1 kule
        int desiredTowers = ecoCount / ECO_BUILDINGS_PER_TOWER;
        return (ecoCount > 0 && towerCount < desiredTowers);
    }

    private void ManageEconomyExpansion()
    {
        int farms = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Farm);
        int cutters = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.WoodCutter);
        int pits = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.StonePit);

        SimBuildingType targetType = SimBuildingType.WoodCutter;
        int costW = 0, costS = 0, costM = 0;

        // Dengeli üretim
        if (farms <= cutters && farms <= pits) { targetType = SimBuildingType.Farm; costW = SimConfig.FARM_COST_WOOD; costS = SimConfig.FARM_COST_STONE; costM = SimConfig.FARM_COST_MEAT; }
        else if (pits < cutters) { targetType = SimBuildingType.StonePit; costW = SimConfig.STONEPIT_COST_WOOD; costS = SimConfig.STONEPIT_COST_STONE; costM = SimConfig.STONEPIT_COST_MEAT; }
        else { targetType = SimBuildingType.WoodCutter; costW = SimConfig.WOODCUTTER_COST_WOOD; costS = SimConfig.WOODCUTTER_COST_STONE; costM = SimConfig.WOODCUTTER_COST_MEAT; }

        if (!TryBuildBuilding(targetType, costW, costS, costM))
        {
            AssignSmartGatherTasks(costW, costS, costM);
        }
    }

    // --- ŞEHİR PLANI: IZGARA SİSTEMİ (3'e Bölünme Kuralı) ---
    // --- GÜNCELLENEN: GÜVENLİ ŞEHİR BLOĞU YERLEŞİMİ ---
    private bool TryBuildBuilding(SimBuildingType type, int w, int s, int m)
    {
        bool isAlreadyBuilding = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == type && !b.IsConstructed);
        if (isAlreadyBuilding) return true;

        if (SimResourceSystem.CanAfford(_world, _myPlayerID, w, s, m))
        {
            var worker = GetIdleWorker();
            if (worker != null)
            {
                var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
                if (baseB == null) return false;

                // Düşman üssünü bul (Mesafe kontrolü için)
                var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);

                for (int i = 0; i < 50; i++)
                {
                    // Yarıçapı biraz kıstık (18 -> 12) böylece daha kompakt bir şehir olur
                    int radius = 12;
                    int2 offset = new int2(UnityEngine.Random.Range(-radius, radius + 1), UnityEngine.Random.Range(-radius, radius + 1));
                    int2 pos = baseB.GridPosition + offset;

                    // 1. KURAL: IZGARA (Yol Bırakma)
                    if (pos.x % 3 == 0 || pos.y % 3 == 0) continue;

                    // 2. KURAL: HARİTA SINIRLARI
                    if (!_world.Map.IsInBounds(pos) || !_world.Map.Grid[pos.x, pos.y].IsWalkable) continue;

                    // 3. KURAL: BÖLGE KONTROLÜ (Düşmana çok yaklaşma)
                    if (enemyBase != null)
                    {
                        float distToMe = SimGridSystem.GetDistanceSq(pos, baseB.GridPosition);
                        float distToEnemy = SimGridSystem.GetDistanceSq(pos, enemyBase.GridPosition);

                        // Eğer seçtiğim nokta düşmana benden daha yakınsa, İPTAL ET.
                        // (Güvenli bölgede kal)
                        if (distToEnemy < distToMe) continue;
                    }

                    // Her şey uygunsa inşa et
                    SimResourceSystem.SpendResources(_world, _myPlayerID, w, s, m);
                    var b = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, pos);
                    SimUnitSystem.OrderBuild(worker, b, _world);
                    return true;
                }
            }
        }
        return false;
    }

    private void AssignSmartGatherTasks(int reqWood, int reqStone, int reqMeat)
    {
        var me = _world.Players[_myPlayerID];
        int deficitWood = Mathf.Max(0, reqWood - me.Wood);
        int deficitStone = Mathf.Max(0, reqStone - me.Stone);
        int deficitMeat = Mathf.Max(0, reqMeat - me.Meat);

        if (deficitWood == 0 && deficitStone == 0 && deficitMeat == 0) { deficitWood = 1; deficitStone = 1; deficitMeat = 1; }

        var idleWorkers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();

        foreach (var w in idleWorkers)
        {
            SimResourceType targetRes = SimResourceType.None;
            float total = deficitWood + deficitStone + deficitMeat;
            float r = UnityEngine.Random.value;

            if (r < (float)deficitWood / total) targetRes = SimResourceType.Wood;
            else if (r < (float)(deficitWood + deficitStone) / total) targetRes = SimResourceType.Stone;
            else targetRes = SimResourceType.Meat;

            var resNode = FindNearestResource(w.GridPosition, targetRes);
            if (resNode == null) resNode = FindNearestResource(w.GridPosition, SimResourceType.Wood);

            if (resNode != null) SimUnitSystem.TryAssignGatherTask(w, resNode, _world);
        }
    }

    private SimUnitData GetIdleWorker() => _world.Units.Values.FirstOrDefault(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
    private SimResourceData FindNearestResource(int2 pos, SimResourceType type)
    {
        SimResourceData best = null; float minDst = float.MaxValue;
        foreach (var res in _world.Resources.Values)
        {
            if (res.Type == type && res.AmountRemaining > 0)
            {
                float d = SimGridSystem.GetDistanceSq(pos, res.GridPosition);
                if (d < minDst) { minDst = d; best = res; }
            }
        }
        return best;
    }
}