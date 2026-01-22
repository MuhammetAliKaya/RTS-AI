using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;
using int2 = RTS.Simulation.Data.int2;

public class EliteCommanderAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;
    private float _microTimer;

    // --- STRATEJİK DEĞİŞKENLER ---
    private bool _openingFinished = false;
    private int _currentBuildStep = 0;

    private int dcCountScripted = 0;


    // Açılış Sırası
    private readonly List<BuildOrderStep> _buildOrder = new List<BuildOrderStep>
    {
        new BuildOrderStep(SimUnitType.Worker, 4),
        new BuildOrderStep(SimBuildingType.Barracks, 1),
        new BuildOrderStep(SimBuildingType.House, 1),
        new BuildOrderStep(SimUnitType.Soldier, 2),
        new BuildOrderStep(SimUnitType.Worker, 7),
        new BuildOrderStep(SimBuildingType.Tower, 1)
    };

    private class BuildOrderStep
    {
        public bool IsUnit;
        public SimUnitType UnitType;
        public SimBuildingType BuildingType;
        public int TargetCount;

        public BuildOrderStep(SimUnitType u, int count) { IsUnit = true; UnitType = u; TargetCount = count; }
        public BuildOrderStep(SimBuildingType b, int count) { IsUnit = false; BuildingType = b; TargetCount = count; }
    }

    public EliteCommanderAI(SimWorldState w, int id)
    {
        _world = w;
        _myPlayerID = id;
        Debug.Log("[EliteCommander v2] Hazır. Agresif Mod Aktif.");
    }

    public void Update(float dt)
    {
        _timer += dt;
        _microTimer += dt;

        if (_world == null) return;

        // 1. MİKRO YÖNETİM (Çok Hızlı Çalışmalı - 0.1s)
        if (_microTimer >= 1f)
        {
            dcCountScripted++;
            // Debug.Log("dcCountScripted " + dcCountScripted);

            _microTimer = 0;
            PerformCombatMicro();
        }

        // 2. MAKRO YÖNETİM (Daha Yavaş - 0.5s)
        if (_timer <= 5f) return;
        _timer = 0;

        var me = _world.Players[_myPlayerID];

        // Acil Durum: Saldırı altındaysak savun
        if (IsUnderAttack())
        {
            PerformEmergencyDefense();
            return;
        }

        // AÇILIŞ KİTABI (Build Order)
        if (!_openingFinished)
        {
            ExecuteOpening(me);
        }
        else
        {
            // OYUN ORTASI (Mid-Game Logic)
            ExecuteMidGameStrategy(me);
        }
    }

    // ============================================================================================
    //                                      1. AÇILIŞ (THE OPENING)
    // ============================================================================================
    private void ExecuteOpening(SimPlayerData me)
    {
        if (_currentBuildStep >= _buildOrder.Count)
        {
            _openingFinished = true;
            Debug.Log("[EliteCommander] Açılış tamamlandı. Mid-Game moduna geçiliyor.");
            return;
        }

        var step = _buildOrder[_currentBuildStep];

        // Mevcut sayıyı kontrol et
        int currentCount = 0;
        if (step.IsUnit)
            currentCount = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == step.UnitType);
        else
            currentCount = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == step.BuildingType);

        // Hedefe ulaşıldı mı?
        if (currentCount >= step.TargetCount)
        {
            _currentBuildStep++;
            return;
        }

        // Hedefe ulaşılmadı, üret/inşa et
        if (step.IsUnit)
        {
            TryTrainUnit(step.UnitType);
            AssignSmartGatherTasks(GetCost(step.UnitType));
        }
        else
        {
            if (!TryBuildStructure(step.BuildingType))
            {
                AssignSmartGatherTasks(GetCost(step.BuildingType));
            }
        }
    }

    // ============================================================================================
    //                                      2. OYUN ORTASI (MID GAME)
    // ============================================================================================
    private void ExecuteMidGameStrategy(SimPlayerData me)
    {
        // A. NÜFUS YÖNETİMİ
        if (me.CurrentPopulation >= me.MaxPopulation)
        {
            if (!TryBuildStructure(SimBuildingType.House))
                AssignSmartGatherTasks(GetCost(SimBuildingType.House));
            return;
        }

        // B. EKONOMİ DENGESİ
        int workers = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker);
        if (workers < 15)
        {
            TryTrainUnit(SimUnitType.Worker);
        }

        // C. ORDU ÜRETİMİ (Sürekli asker bas)
        int barracks = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks);
        if (barracks < 2)
        {
            TryBuildStructure(SimBuildingType.Barracks);
        }

        TryTrainUnit(SimUnitType.Soldier);

        // D. SALDIRI KARARI (GÜNCELLENDİ)
        int soldiers = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier);

        // 10 Asker varsa topyekün saldır, az varsa taciz et
        if (soldiers >= 10)
        {
            PerformTotalAssault();
        }
        else if (soldiers >= 2) // En az 2 askerle tacize başla
        {
            PerformHarass();
        }
        else
        {
            // Asker çok azsa savunmada/üretimde kal, işçiler boş durmasın
            AssignSmartGatherTasks(GetCost(SimUnitType.Soldier));
        }

        // Boşta kalan kaynak varsa harca
        if (me.Wood > 600) TryBuildStructure(SimBuildingType.Tower);
    }

    // ============================================================================================
    //                                      3. SAVAŞ MİKROSU (COMBAT MICRO)
    // ============================================================================================
    private void PerformCombatMicro()
    {
        var mySoldiers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier).ToList();

        foreach (var s in mySoldiers)
        {
            // 1. CANI AZALANI GERİ ÇEK (Kite)
            if (s.Health < s.MaxHealth * 0.3f)
            {
                var nearestThreat = FindNearestEnemyUnit(s.GridPosition, 4.0f);
                if (nearestThreat != null)
                {
                    int2 retreatPos = s.GridPosition + (s.GridPosition - nearestThreat.GridPosition);
                    SimUnitSystem.OrderMove(s, retreatPos, _world);
                    continue;
                }
            }

            // 2. FOCUS FIRE (En zayıf düşmana odaklan)
            if (s.State == SimTaskType.Attacking)
            {
                var enemiesInRange = GetEnemiesInRange(s.GridPosition, s.AttackRange + 1.0f);
                var weakest = enemiesInRange.OrderBy(e => e.Health).FirstOrDefault();

                if (weakest != null && weakest.ID != s.TargetID)
                {
                    SimUnitSystem.OrderAttackUnit(s, weakest, _world);
                }
            }
        }
    }

    // --- GÜNCELLENEN: TACİZ MODU (ARTIK BOŞ DURMAZ) ---
    private void PerformHarass()
    {
        var squad = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle).ToList();
        if (squad.Count == 0) return;

        // Öncelik 1: İşçiler
        SimUnitData target = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID && u.UnitType == SimUnitType.Worker);

        // Öncelik 2: Ekonomi Binaları
        if (target == null)
        {
            // İşçi yoksa ekonomi binası bul (Farm, Woodcutter vb.)
            var ecoBuilding = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type != SimBuildingType.Base && b.Type != SimBuildingType.Tower);
            if (ecoBuilding != null)
            {
                foreach (var s in squad) SimUnitSystem.OrderAttack(s, ecoBuilding, _world);
                return;
            }
        }

        // Öncelik 3: Herhangi bir şey (Base dahil)
        if (target == null) target = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);

        // Saldırı Emri
        if (target != null)
        {
            foreach (var s in squad) SimUnitSystem.OrderAttackUnit(s, target, _world);
        }
        else
        {
            // Hiçbir şey yoksa düşman base'ine saldır
            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);
            if (enemyBase != null)
            {
                foreach (var s in squad) SimUnitSystem.OrderAttack(s, enemyBase, _world);
            }
            else
            {
                // O da yoksa harita ortasına git (Search)
                foreach (var s in squad) SimUnitSystem.OrderMove(s, new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2), _world);
            }
        }
    }

    // --- GÜNCELLENEN: TOTAL SALDIRI ---
    private void PerformTotalAssault()
    {
        var army = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier).ToList();

        // Hedef bulma mantığını iyileştirdik
        object target = FindBestTarget();

        // Hedef yoksa harita ortasına git (Arama modu)
        if (target == null)
        {
            foreach (var s in army)
                if (s.State == SimTaskType.Idle)
                    SimUnitSystem.OrderMove(s, new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2), _world);
            return;
        }

        foreach (var s in army)
        {
            // Sadece boşta olanları değil, yanlış hedefe gidenleri de güncelle
            // Ama sürekli emir spamlamamak için 'Attacking' olanlara dokunmuyoruz.
            if (s.State == SimTaskType.Idle
            // || s.State == SimTaskType.Moving
            )
            {
                if (target is SimUnitData u) SimUnitSystem.OrderAttackUnit(s, u, _world);
                else if (target is SimBuildingData b) SimUnitSystem.OrderAttack(s, b, _world);
            }
        }
    }

    // ============================================================================================
    //                                      YARDIMCI SİSTEMLER
    // ============================================================================================

    private object FindBestTarget()
    {
        // 1. Önce Base'i bitirmeye çalış
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);
        if (enemyBase != null) return enemyBase;

        // 2. Base yoksa askerleri temizle
        var enemyUnit = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);
        if (enemyUnit != null) return enemyUnit;

        // 3. Hiçbir şey yoksa binaları yık
        return _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID);
    }

    private void AssignSmartGatherTasks(ResourceCost cost)
    {
        var me = _world.Players[_myPlayerID];
        SimResourceType priority = SimResourceType.None;

        if (me.Meat < cost.Meat) priority = SimResourceType.Meat;
        else if (me.Wood < cost.Wood) priority = SimResourceType.Wood;
        else if (me.Stone < cost.Stone) priority = SimResourceType.Stone;

        if (priority == SimResourceType.None) priority = SimResourceType.Wood; // Varsayılan

        var idleWorkers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
        foreach (var w in idleWorkers)
        {
            var res = FindNearestResource(w.GridPosition, priority);
            if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
        }
    }

    private bool TryBuildStructure(SimBuildingType type)
    {
        var cost = GetCost(type);
        if (!SimResourceSystem.CanAfford(_world, _myPlayerID, cost.Wood, cost.Stone, cost.Meat)) return false;

        var worker = GetIdleWorker();
        if (worker == null) return false;

        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (baseB == null) return false;

        for (int i = 0; i < 30; i++)
        {
            int2 pos = baseB.GridPosition + new int2(Random.Range(-8, 9), Random.Range(-8, 9));
            if (pos.x % 2 == 0 && _world.Map.IsInBounds(pos) && _world.Map.Grid[pos.x, pos.y].IsWalkable)
            {
                SimResourceSystem.SpendResources(_world, _myPlayerID, cost.Wood, cost.Stone, cost.Meat);
                var b = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                return true;
            }
        }
        return false;
    }

    private void TryTrainUnit(SimUnitType type)
    {
        var cost = GetCost(type);
        if (!SimResourceSystem.CanAfford(_world, _myPlayerID, cost.Wood, cost.Stone, cost.Meat))
        {
            AssignSmartGatherTasks(cost);
            return;
        }

        SimBuildingData producer = null;
        if (type == SimUnitType.Worker)
            producer = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base && !b.IsTraining);
        else
            producer = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks && b.IsConstructed && !b.IsTraining);

        if (producer != null)
        {
            SimBuildingSystem.StartTraining(producer, _world, type);
        }
    }

    // --- YAPILAR ve VERİ ---
    private struct ResourceCost { public int Wood, Stone, Meat; }
    private ResourceCost GetCost(SimUnitType t)
    {
        if (t == SimUnitType.Worker) return new ResourceCost { Wood = SimConfig.WORKER_COST_WOOD, Stone = SimConfig.WORKER_COST_STONE, Meat = SimConfig.WORKER_COST_MEAT };
        return new ResourceCost { Wood = SimConfig.SOLDIER_COST_WOOD, Stone = SimConfig.SOLDIER_COST_STONE, Meat = SimConfig.SOLDIER_COST_MEAT };
    }
    private ResourceCost GetCost(SimBuildingType t)
    {
        if (t == SimBuildingType.House) return new ResourceCost { Wood = SimConfig.HOUSE_COST_WOOD, Stone = SimConfig.HOUSE_COST_STONE, Meat = SimConfig.HOUSE_COST_MEAT };
        if (t == SimBuildingType.Barracks) return new ResourceCost { Wood = SimConfig.BARRACKS_COST_WOOD, Stone = SimConfig.BARRACKS_COST_STONE, Meat = SimConfig.BARRACKS_COST_MEAT };
        if (t == SimBuildingType.Tower) return new ResourceCost { Wood = SimConfig.TOWER_COST_WOOD, Stone = SimConfig.TOWER_COST_STONE, Meat = SimConfig.TOWER_COST_MEAT };
        return new ResourceCost();
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

    private bool IsUnderAttack()
    {
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (myBase == null) return false;
        return _world.Units.Values.Any(u => u.PlayerID != _myPlayerID && SimGridSystem.GetDistanceSq(u.GridPosition, myBase.GridPosition) < 100f);
    }

    private void PerformEmergencyDefense()
    {
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (myBase == null) return;
        var enemy = FindNearestEnemyUnit(myBase.GridPosition, 15f);
        if (enemy == null) return;

        foreach (var u in _world.Units.Values.Where(u => u.PlayerID == _myPlayerID))
        {
            if (u.TargetID != enemy.ID) SimUnitSystem.OrderAttackUnit(u, enemy, _world);
        }
    }

    private SimUnitData FindNearestEnemyUnit(int2 pos, float range)
    {
        float rSq = range * range;
        return _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID && SimGridSystem.GetDistanceSq(pos, u.GridPosition) < rSq);
    }

    private List<SimUnitData> GetEnemiesInRange(int2 pos, float range)
    {
        float rSq = range * range;
        return _world.Units.Values.Where(u => u.PlayerID != _myPlayerID && SimGridSystem.GetDistanceSq(pos, u.GridPosition) < rSq).ToList();
    }
}