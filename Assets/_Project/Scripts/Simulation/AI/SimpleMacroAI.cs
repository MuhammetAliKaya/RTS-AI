using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System.Collections.Generic;
using UnityEngine; // Mathf ve Random için

public class SimpleMacroAI : IMacroAI
{
    private SimWorldState _world;
    private int _playerID;
    private float _timer = 0f;

    // YENİ: Zorluk artık bir float (Config'den gelen değer)
    // 0.0 = Pasif (Sadece ekonomi)
    // 0.5 = Çok Kolay (Savunma ağırlıklı, saldırı yok)
    // 1.0 = Kolay (Az saldırı)
    // 2.0 = Agresif (Tam güç)
    private float _difficultyLevel;

    // Zorluğa göre değişen iç parametreler
    private float _decisionInterval;
    private int _maxSoldierCount;
    private float _attackCheckInterval;
    private float _attackProbability; // Her kontrol anında saldırma şansı

    private bool _isAttacking = false;

    public SimpleMacroAI(SimWorldState world, int playerID, float difficultyLevel = 0.0f)
    {
        _world = world;
        _playerID = playerID;
        SetDifficulty(difficultyLevel);
    }

    // Config dosyasındaki "value" buraya gelecek
    public void SetDifficulty(float value)
    {
        _difficultyLevel = Mathf.Clamp(value, 0f, 2.0f);

        // 1. Karar Verme Hızı: Zor bot daha hızlı düşünür (2sn -> 0.5sn)
        _decisionInterval = Mathf.Lerp(2.0f, 0.5f, _difficultyLevel / 2.0f);

        // 2. Asker Limiti: 
        // 0.0 -> 0 asker
        // 0.5 -> 3 asker (Savunma için)
        // 2.0 -> 15 asker (Full ordu)
        if (_difficultyLevel < 0.2f) _maxSoldierCount = 0;
        else _maxSoldierCount = (int)Mathf.Lerp(3, 1, (_difficultyLevel - 0.2f) / 1.8f);

        // 3. Saldırganlık:
        // 0.0 - 0.8 arası -> %0 Saldırı (Sadece savunma)
        // 2.0 -> %100 Saldırı isteği
        if (_difficultyLevel < 0.8f) _attackProbability = 0f;
        else _attackProbability = Mathf.Lerp(0.1f, 1.0f, (_difficultyLevel - 0.8f) / 1.2f);
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer >= _decisionInterval)
        {
            _timer = 0f;
            MakeDecisions();
        }
    }

    private void MakeDecisions()
    {
        var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
        var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
        var pData = SimResourceSystem.GetPlayer(_world, _playerID);

        int workerCount = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
        int soldierCount = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
        bool hasBarracks = myBuildings.Any(b => b.Type == SimBuildingType.Barracks);
        var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);

        // --- 1. EKONOMİ (HER ZORLUKTA AYNI) ---
        // Hedef 5 işçi (Zorluk çok düşükse daha yavaş basabilir ama mantık aynı)
        if (baseB != null && !baseB.IsTraining && workerCount < 5)
        {
            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
        }

        // --- 2. KIŞLA İNŞASI ---
        // Zorluk 0.2'den büyükse ve 3 işçi varsa kışla kurmaya başlar
        if (_difficultyLevel > 0.2f && workerCount >= 3 && !hasBarracks)
        {
            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            {
                var worker = myUnits.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Building);
                if (worker != null)
                {
                    int2 buildPos = FindBuildSpot(baseB.GridPosition);
                    if (buildPos.x != -1)
                    {
                        SimResourceSystem.SpendResources(_world, _playerID, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                        var barracks = SpawnBuildingPlaceholder(SimBuildingType.Barracks, buildPos);
                        SimUnitSystem.OrderBuild(worker, barracks, _world);
                    }
                }
            }
        }

        // --- 3. ASKER ÜRETİMİ (DİNAMİK LİMİT) ---
        if (hasBarracks && soldierCount < _maxSoldierCount)
        {
            // Eğer asker sayımız limitin yarısının altındaysa, saldırıyı durdur ve toplan
            if (soldierCount < _maxSoldierCount * 0.5f) _isAttacking = false;

            var barracks = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Barracks && b.IsConstructed);
            if (barracks != null && !barracks.IsTraining)
            {
                if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                    SimBuildingSystem.StartTraining(barracks, _world, SimUnitType.Soldier);
            }
        }

        // --- 4. İŞÇİ YÖNETİMİ ---
        ManageWorkersSmart(myUnits, pData, hasBarracks);

        // --- 5. SALDIRI KARARI ---
        // Saldırı modu kapalıysa ve asker sayısı limite ulaştıysa şansını dene
        if (!_isAttacking && soldierCount >= _maxSoldierCount && _maxSoldierCount > 0)
        {
            // Random.value (0.0 - 1.0)
            if (UnityEngine.Random.value < _attackProbability)
            {
                _isAttacking = true;
            }
        }

        if (_isAttacking)
        {
            AttackWithAllSoldiers(myUnits);
        }
        else
        {
            // Saldırmıyorsak ama askerimiz varsa, Base etrafında devriye/savunma
            DefendBase(myUnits, baseB);
        }
    }

    // --- YARDIMCI METOTLAR (ESKİ KODDAN ALINDI & İYİLEŞTİRİLDİ) ---

    private void DefendBase(List<SimUnitData> myUnits, SimBuildingData baseB)
    {
        if (baseB == null) return;
        foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
        {
            if (s.State == SimTaskType.Idle)
            {
                // Base etrafında rastgele bir nokta
                // Basitçe olduğu yerde beklesin veya yakına gelen düşmana saldırsın (SimUnitSystem otomatik yapar)
            }
        }
    }

    private void ManageWorkersSmart(List<SimUnitData> myUnits, SimPlayerData pData, bool hasBarracks)
    {
        var workers = myUnits.Where(u => u.UnitType == SimUnitType.Worker).ToList();
        foreach (var w in workers)
        {
            if (w.State == SimTaskType.Building || (w.State == SimTaskType.Moving && w.TargetID != -1)) continue;

            SimResourceType targetType = SimResourceType.Wood;

            // Mantık: İşçi azsa yemek, kışla yoksa odun/taş, kışla varsa yemek/odun (asker için)
            if (myUnits.Count(u => u.UnitType == SimUnitType.Worker) < 5) targetType = SimResourceType.Meat;
            else if (!hasBarracks)
            {
                if (pData.Wood < SimConfig.BARRACKS_COST_WOOD) targetType = SimResourceType.Wood;
                else targetType = SimResourceType.Stone;
            }
            else
            {
                if (pData.Meat < SimConfig.SOLDIER_COST_MEAT) targetType = SimResourceType.Meat;
                else targetType = SimResourceType.Wood;
            }

            var res = FindNearestResource(w.GridPosition, targetType);
            if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);

            if (res != null && w.TargetID != res.ID)
                SimUnitSystem.TryAssignGatherTask(w, res, _world);
        }
    }

    private void AttackWithAllSoldiers(List<SimUnitData> myUnits)
    {
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);
        // Base yoksa herhangi bir binaya saldır
        if (enemyBase == null) enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID);

        if (enemyBase != null)
        {
            foreach (var soldier in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
            {
                // Zaten o hedefe saldırıyorsa emri yenileme
                if (soldier.State == SimTaskType.Attacking && soldier.TargetID == enemyBase.ID) continue;

                SimUnitSystem.OrderAttack(soldier, enemyBase, _world);
            }
        }
    }

    // --- GRİD VE SPAWN YARDIMCILARI (DEĞİŞMEDİ) ---
    private int2 FindBuildSpot(int2 center)
    {
        for (int x = center.x - 4; x <= center.x + 4; x++)
        {
            for (int y = center.y - 4; y <= center.y + 4; y++)
            {
                int2 pos = new int2(x, y);
                if (SimGridSystem.IsWalkable(_world, pos)) return pos;
            }
        }
        return new int2(-1, -1);
    }

    private SimResourceData FindNearestResource(int2 pos, SimResourceType type = SimResourceType.None)
    {
        SimResourceData best = null;
        float minDst = float.MaxValue;
        foreach (var r in _world.Resources.Values)
        {
            if (type != SimResourceType.None && r.Type != type) continue;
            float d = SimGridSystem.GetDistanceSq(pos, r.GridPosition);
            if (d < minDst) { minDst = d; best = r; }
        }
        return best;
    }

    private SimBuildingData SpawnBuildingPlaceholder(SimBuildingType type, int2 pos)
    {
        var b = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = _playerID,
            Type = type,
            GridPosition = pos,
            IsConstructed = false,
            ConstructionProgress = 0f,
            Health = 100 // Varsayılan can
        };
        SimBuildingSystem.InitializeBuildingStats(b);
        // // Eğer statik bir metodun varsa kullan
        _world.Buildings.Add(b.ID, b);
        _world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        return b;
    }
}