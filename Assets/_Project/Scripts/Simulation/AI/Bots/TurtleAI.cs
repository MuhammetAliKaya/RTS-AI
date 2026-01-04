using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;
using int2 = RTS.Simulation.Data.int2;

public class TurtleAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;
    private float _interval = 0.5f;

    // --- AYARLAR ---
    private const int TARGET_WORKER_COUNT = 7;
    private const int DESIRED_TOWER_COUNT = 4; // Kuleyi 4'e çektik (daha hızlı orduya geçsin)
    private const int ATTACK_SQUAD_SIZE = 8;   // 12 çok fazlaydı, 8'e düşürdük.

    public TurtleAI(SimWorldState w, int id)
    {
        _world = w;
        _myPlayerID = id;
        Debug.Log($"[TurtleAI] Plan: {TARGET_WORKER_COUNT} İşçi -> {DESIRED_TOWER_COUNT} Kule -> {ATTACK_SQUAD_SIZE} Asker -> SALDIRI");
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < _interval) return;
        _timer = 0;

        if (_world == null || !_world.Players.ContainsKey(_myPlayerID)) return;
        var me = _world.Players[_myPlayerID];

        // --- 0. KRİTİK: NÜFUS YÖNETİMİ ---
        // Eğer nüfus dolduysa BAŞKA HİÇBİR ŞEY YAPMA (Parayı askere harcama)
        if (CheckPopulationCap(me)) return;

        // --- DURUM ANALİZİ ---
        int workerCount = _world.Units.Values.Count(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker);
        int towerCount = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Tower && b.IsConstructed);

        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        var barracks = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Barracks);

        // --- 1. İŞÇİ ---
        if (workerCount < TARGET_WORKER_COUNT && baseB != null && !baseB.IsTraining)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            else
                AssignGatherTasks(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
            return;
        }

        // --- 2. SÜRDÜRÜLEBİLİR EKONOMİ ---
        if (EnsureInfiniteResources()) return;

        // --- 3. SAVUNMA (KULE) ---
        if (towerCount < DESIRED_TOWER_COUNT)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT))
            {
                var builder = GetIdleWorker();
                if (builder != null) TryBuildDefensiveStructure(builder, SimBuildingType.Tower);
            }
            else
            {
                AssignGatherTasks(SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
            }
        }

        // --- 4. KIŞLA ---
        if (towerCount >= 2 && barracks == null)
        {
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            {
                var builder = GetIdleWorker();
                if (builder != null) TryBuildDefensiveStructure(builder, SimBuildingType.Barracks);
            }
            else
            {
                AssignGatherTasks(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
            }
        }

        // --- 5. ORDU VE SALDIRI ---
        if (barracks != null && barracks.IsConstructed)
        {
            if (!barracks.IsTraining)
            {
                // Nüfus limiti kontrolü burada da önemli
                if (me.CurrentPopulation < me.MaxPopulation)
                {
                    if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                        SimBuildingSystem.StartTraining(barracks, _world, SimUnitType.Soldier);
                    else
                        AssignGatherTasks(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
                }
            }
            HandleTurtleCombat();
        }
        else
        {
            AssignGatherTasks(9999, 9999, 9999); // Boş durma stok yap
        }
    }

    // --- DÜZELTİLEN FONKSİYON: NÜFUS KİLİDİ ---
    private bool CheckPopulationCap(SimPlayerData me)
    {
        // Limite ulaştıysak (örn: 10/10)
        if (me.CurrentPopulation >= me.MaxPopulation)
        {
            // Zaten inşa edilen bir ev var mı?
            bool isBuildingHouse = _world.Buildings.Values.Any(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.House && !b.IsConstructed);

            if (isBuildingHouse)
            {
                // Ev yapılıyor, sadece bekle (parayı harcama)
                return true;
            }

            // Ev yok, yapmaya çalış
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
            {
                var builder = GetIdleWorker();
                if (builder != null)
                {
                    TryBuildDefensiveStructure(builder, SimBuildingType.House);
                    Debug.Log("[TurtleAI] Nüfus doldu! Ev inşa ediliyor.");
                    return true;
                }
            }
            else
            {
                // Para yetmiyor, SADECE ev için kaynak topla. Asker basmaya çalışma!
                AssignGatherTasks(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                return true; // Burası kritik: True döndürerek Update'i kesiyoruz.
            }
        }
        return false;
    }

    // --- DİĞER FONKSİYONLAR (Aynen Kalabilir) ---
    private bool EnsureInfiniteResources()
    {
        int mapWood = _world.Resources.Values.Count(r => r.Type == SimResourceType.Wood && r.AmountRemaining > 0);
        int myWoodCutters = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.WoodCutter);
        if ((mapWood < 20 || myWoodCutters == 0) && myWoodCutters < 2)
            if (TryBuildEconomyBuilding(SimBuildingType.WoodCutter, SimConfig.WOODCUTTER_COST_WOOD, 0, 0)) return true;

        int mapStone = _world.Resources.Values.Count(r => r.Type == SimResourceType.Stone && r.AmountRemaining > 0);
        int myStonePits = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.StonePit);
        if ((mapStone < 15 || myStonePits == 0) && myStonePits < 2)
            if (TryBuildEconomyBuilding(SimBuildingType.StonePit, 0, SimConfig.STONEPIT_COST_STONE, 0)) return true;

        int mapMeat = _world.Resources.Values.Count(r => r.Type == SimResourceType.Meat && r.AmountRemaining > 0);
        int myFarms = _world.Buildings.Values.Count(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Farm);
        if ((mapMeat < 15 || myFarms == 0) && myFarms < 2)
            if (TryBuildEconomyBuilding(SimBuildingType.Farm, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT)) return true;

        return false;
    }

    private bool TryBuildEconomyBuilding(SimBuildingType type, int cWood, int cStone, int cMeat)
    {
        if (SimResourceSystem.CanAfford(_world, _myPlayerID, cWood, cStone, cMeat))
        {
            var builder = GetIdleWorker();
            if (builder != null)
            {
                TryBuildDefensiveStructure(builder, type);
                return true;
            }
        }
        else
        {
            AssignGatherTasks(cWood, cStone, cMeat);
        }
        return false;
    }

    private void HandleTurtleCombat()
    {
        var soldiers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle).ToList();

        // Savunma
        var threat = FindEnemyNearBase(12.0f);
        if (threat != null)
        {
            foreach (var s in soldiers) SimUnitSystem.OrderAttackUnit(s, threat, _world);
            return;
        }

        // Saldırı (8 Asker olunca)
        if (soldiers.Count >= ATTACK_SQUAD_SIZE)
        {
            var target = FindEnemyTarget();
            if (target != null)
            {
                foreach (var s in soldiers) SimUnitSystem.OrderAttackUnit(s, target, _world);
                // Debug.Log($"[TurtleAI] {soldiers.Count} askerle saldırıya çıkılıyor!");
            }
            else
            {
                var bTarget = FindEnemyBuildingTarget();
                if (bTarget != null) foreach (var s in soldiers) SimUnitSystem.OrderAttack(s, bTarget, _world);
            }
        }
    }

    private void AssignGatherTasks(int requiredWood, int requiredStone, int requiredMeat)
    {
        var me = _world.Players[_myPlayerID];
        SimResourceType targetRes = SimResourceType.None;
        if (me.Stone < requiredStone) targetRes = SimResourceType.Stone;
        else if (me.Wood < requiredWood) targetRes = SimResourceType.Wood;
        else if (me.Meat < requiredMeat) targetRes = SimResourceType.Meat;

        if (targetRes == SimResourceType.None) return;

        var idleWorkers = _world.Units.Values.Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
        foreach (var worker in idleWorkers)
        {
            var resNode = FindNearestResource(worker.GridPosition, targetRes);
            if (resNode != null) SimUnitSystem.TryAssignGatherTask(worker, resNode, _world);
        }
    }

    private void TryBuildDefensiveStructure(SimUnitData worker, SimBuildingType type)
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (baseB == null) return;

        for (int i = 0; i < 40; i++)
        {
            int range = UnityEngine.Random.Range(2, 6);
            if (type == SimBuildingType.House) range = UnityEngine.Random.Range(4, 8); // Evi biraz daha uzağa yapabilir

            float angle = UnityEngine.Random.Range(0, 360) * Mathf.Deg2Rad;
            int2 pos = baseB.GridPosition + new int2((int)(Mathf.Cos(angle) * range), (int)(Mathf.Sin(angle) * range));

            if (_world.Map.IsInBounds(pos) && _world.Map.Grid[pos.x, pos.y].IsWalkable)
            {
                int w = 0, s = 0, m = 0;
                if (type == SimBuildingType.Tower) { w = SimConfig.TOWER_COST_WOOD; s = SimConfig.TOWER_COST_STONE; }
                else if (type == SimBuildingType.Barracks) { w = SimConfig.BARRACKS_COST_WOOD; s = SimConfig.BARRACKS_COST_STONE; }
                else if (type == SimBuildingType.House) { w = SimConfig.HOUSE_COST_WOOD; s = SimConfig.HOUSE_COST_STONE; m = SimConfig.HOUSE_COST_MEAT; }
                else if (type == SimBuildingType.WoodCutter) { w = SimConfig.WOODCUTTER_COST_WOOD; }
                else if (type == SimBuildingType.StonePit) { s = SimConfig.STONEPIT_COST_STONE; }
                else if (type == SimBuildingType.Farm) { m = SimConfig.FARM_COST_MEAT; }

                SimResourceSystem.SpendResources(_world, _myPlayerID, w, s, m);
                var b = SimBuildingSystem.CreateBuilding(_world, _myPlayerID, type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                break;
            }
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

    private SimUnitData FindEnemyNearBase(float range)
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);
        if (baseB == null) return null;
        float rSqr = range * range;
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID != _myPlayerID)
            {
                float dx = u.GridPosition.x - baseB.GridPosition.x;
                float dy = u.GridPosition.y - baseB.GridPosition.y;
                if ((dx * dx + dy * dy) <= rSqr) return u;
            }
        }
        return null;
    }
    private SimUnitData FindEnemyTarget() => _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);
    private SimBuildingData FindEnemyBuildingTarget()
    {
        var b = _world.Buildings.Values.FirstOrDefault(x => x.PlayerID != _myPlayerID && x.Type == SimBuildingType.Base);
        return b ?? _world.Buildings.Values.FirstOrDefault(x => x.PlayerID != _myPlayerID);
    }
}