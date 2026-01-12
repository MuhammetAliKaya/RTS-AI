using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;
using int2 = RTS.Simulation.Data.int2;

public class HarasserAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;
    private const int MAX_WORKER_COUNT = 5; // Az işçi, çok asker
    // SQUAD_SIZE genelde bu botta kullanılmıyor, anında çıkıyorlar ama durabilir.

    public HarasserAI(SimWorldState world, int playerID)
    {
        _world = world;
        _myPlayerID = playerID;
        Debug.Log("[HarasserAI v2] Başlatıldı. Hedef: İşçiler ve Ekonomi Binaları.");
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < 0.2f) return; // Daha sık karar versin (Micro yönetimi için)
        _timer = 0;

        if (_world == null || !_world.Players.ContainsKey(_myPlayerID)) return;
        var me = _world.Players[_myPlayerID];

        // 1. Nüfus Kontrolü (Standart)
        if (CheckPopulation(me)) return;

        // 2. Ekonomi ve Üretim
        ManageProduction(me);

        // 3. MİKRO YÖNETİM (Vur-Kaç Taktikleri)
        ManageHarassMicro();
    }

    private void ManageProduction(SimPlayerData me)
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        var barracks = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks && b.IsConstructed);
        int workers = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker);

        // A. İşçi (Limitle)
        if (workers < MAX_WORKER_COUNT && baseB != null && !baseB.IsTraining)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            else
                AssignGatherTasks(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
            return;
        }

        // B. Kışla (Erken yap)
        if (barracks == null)
        {
            // İnşaat halinde kışla var mı?
            bool buildingBarracks = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks);
            if (!buildingBarracks)
            {
                if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
                {
                    var w = GetIdleWorker();
                    if (w != null) BuildNearBase(w, SimBuildingType.Barracks);
                }
                else
                {
                    AssignGatherTasks(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                }
            }
        }
        // C. Asker (Sürekli)
        else if (!barracks.IsTraining)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                SimBuildingSystem.StartTraining(barracks, _world, SimUnitType.Soldier);
            else
                AssignGatherTasks(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        }
    }

    private void ManageHarassMicro()
    {
        // Benim askerlerim
        var mySoldiers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier).ToList();
        if (mySoldiers.Count == 0) return;

        // Düşman tehditleri (Askerler ve Kuleler)
        var enemyThreats = _world.Units.Values.Where(u => u.PlayerID != _myPlayerID && u.UnitType == SimUnitType.Soldier).ToList();
        var enemyTowers = _world.Buildings.Values.Where(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Tower).ToList();

        // --- HEDEF LİSTELERİ ---
        // 1. İşçiler
        var enemyWorkers = _world.Units.Values.Where(u => u.PlayerID != _myPlayerID && u.UnitType == SimUnitType.Worker).ToList();

        // 2. Ekonomi Binaları (YENİ EKLENDİ)
        var enemyEcoBuildings = _world.Buildings.Values.Where(b =>
            b.PlayerID != _myPlayerID &&
            (b.Type == SimBuildingType.Farm || b.Type == SimBuildingType.WoodCutter || b.Type == SimBuildingType.StonePit)
        ).ToList();

        // 3. Base
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);

        foreach (var soldier in mySoldiers)
        {
            // --- 1. HAYATTA KALMA (Kite/Retreat) ---
            // En yakın tehdidi bul
            float closestThreatDist = float.MaxValue;
            int2 threatPos = new int2(0, 0);

            foreach (var threat in enemyThreats)
            {
                float d = SimGridSystem.GetDistanceSq(soldier.GridPosition, threat.GridPosition);
                if (d < closestThreatDist) { closestThreatDist = d; threatPos = threat.GridPosition; }
            }
            foreach (var tower in enemyTowers)
            {
                float d = SimGridSystem.GetDistanceSq(soldier.GridPosition, tower.GridPosition);
                if (d < closestThreatDist) { closestThreatDist = d; threatPos = tower.GridPosition; }
            }

            // Eğer tehdit çok yakındaysa (örn 5 birim kare) ve canım azsa veya sayımız azsa -> KAÇ
            if (closestThreatDist < 25.0f && (soldier.Health < soldier.MaxHealth * 0.5f || mySoldiers.Count < 3))
            {
                RunAway(soldier, threatPos);
                continue;
            }

            // --- 2. SALDIRI MANTIĞI ---
            // Eğer zaten saldırıyorsa ve hedefi ölmediyse, hedefini değiştirmek için çok zorlama (titremeyi önle)
            // Ancak daha iyi bir hedef (işçi) yanından geçerse vurmalı.

            if (soldier.State == SimTaskType.Idle || soldier.State == SimTaskType.Moving || soldier.State == SimTaskType.Attacking)
            {
                // ÖNCELİK 1: İŞÇİLER
                if (enemyWorkers.Count > 0)
                {
                    var target = enemyWorkers.OrderBy(w => SimGridSystem.GetDistanceSq(soldier.GridPosition, w.GridPosition)).First();
                    // Eğer mevcut hedefim bu değilse saldır
                    if (soldier.TargetID != target.ID)
                        SimUnitSystem.OrderAttackUnit(soldier, target, _world);
                }
                // ÖNCELİK 2: EKONOMİ BİNALARI (YENİ)
                else if (enemyEcoBuildings.Count > 0)
                {
                    // En yakın ekonomi binasına saldır
                    var target = enemyEcoBuildings.OrderBy(b => SimGridSystem.GetDistanceSq(soldier.GridPosition, b.GridPosition)).First();
                    if (soldier.TargetID != target.ID)
                        SimUnitSystem.OrderAttack(soldier, target, _world);
                }
                // ÖNCELİK 3: ANA ÜS (BASE)
                else if (enemyBase != null)
                {
                    if (soldier.TargetID != enemyBase.ID)
                        SimUnitSystem.OrderAttack(soldier, enemyBase, _world);
                }
                // HEDEF YOKSA: ORTAYA GİT (Arama)
                else
                {
                    if (soldier.State == SimTaskType.Idle)
                        SimUnitSystem.OrderMove(soldier, new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2), _world);
                }
            }
        }
    }

    private void RunAway(SimUnitData unit, int2 threatPos)
    {
        int2 dir = unit.GridPosition - threatPos;
        int2 runTarget = unit.GridPosition + new int2(Mathf.Clamp(dir.x, -5, 5), Mathf.Clamp(dir.y, -5, 5));

        runTarget.x = Mathf.Clamp(runTarget.x, 0, _world.Map.Grid.GetLength(0) - 1);
        runTarget.y = Mathf.Clamp(runTarget.y, 0, _world.Map.Grid.GetLength(1) - 1);

        if (SimGridSystem.IsWalkable(_world, runTarget))
        {
            SimUnitSystem.OrderMove(unit, runTarget, _world);
        }
    }

    // --- YARDIMCI FONKSİYONLAR ---
    private bool CheckPopulation(SimPlayerData me)
    {
        if (me.CurrentPopulation >= me.MaxPopulation)
        {
            bool isBuildingHouse = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.House && !b.IsConstructed);
            if (!isBuildingHouse)
            {
                if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
                {
                    var w = GetIdleWorker();
                    if (w != null) BuildNearBase(w, SimBuildingType.House);
                    return true;
                }
                else AssignGatherTasks(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
            }
        }
        return false;
    }

    private void AssignGatherTasks(int reqW, int reqS, int reqM)
    {
        var me = _world.Players[_myPlayerID];
        SimResourceType t = SimResourceType.None;
        if (me.Wood < reqW) t = SimResourceType.Wood;
        else if (me.Stone < reqS) t = SimResourceType.Stone;
        else if (me.Meat < reqM) t = SimResourceType.Meat;
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
        SimResourceData best = null; float minDst = float.MaxValue;
        foreach (var res in _world.Resources.Values)
        {
            if (res.Type == type && res.AmountRemaining > 0)
            {
                float dst = SimGridSystem.GetDistanceSq(pos, res.GridPosition);
                if (dst < minDst) { minDst = dst; best = res; }
            }
        }
        return best;
    }

    private void BuildNearBase(SimUnitData w, SimBuildingType type)
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (baseB == null) return;

        for (int i = 0; i < 30; i++)
        {
            int2 pos = baseB.GridPosition + new int2(UnityEngine.Random.Range(-6, 7), UnityEngine.Random.Range(-6, 7));
            if (_world.Map.IsInBounds(pos) && _world.Map.Grid[pos.x, pos.y].IsWalkable)
            {
                int wc = 0, sc = 0, mc = 0;
                if (type == SimBuildingType.Barracks) { wc = SimConfig.BARRACKS_COST_WOOD; sc = SimConfig.BARRACKS_COST_STONE; }
                else if (type == SimBuildingType.House) { wc = SimConfig.HOUSE_COST_WOOD; sc = SimConfig.HOUSE_COST_STONE; mc = SimConfig.HOUSE_COST_MEAT; }

                SimResourceSystem.SpendResources(_world, _myPlayerID, wc, sc, mc);
                var b = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, pos);
                SimUnitSystem.OrderBuild(w, b, _world);
                break;
            }
        }
    }
}