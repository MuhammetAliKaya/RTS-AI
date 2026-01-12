using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using System.Collections.Generic;

public class WorkerRushAI : IMacroAI
{
    private SimWorldState _world;
    private int _myPlayerID;
    private float _timer;
    private const float INTERVAL = 0.5f; // Saniyede 2 kez durumu kontrol et

    public WorkerRushAI(SimWorldState w, int id)
    {
        _world = w;
        _myPlayerID = id;
        Debug.Log("[WorkerRushAI v2] Aktif. Strateji: %50 Et Topla (Üretim) + %50 Saldır.");
    }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < INTERVAL) return;
        _timer = 0;

        if (_world == null) return;

        // 1. ÜRETİM: Sürekli yeni işçi bas (Rush'ı beslemek için)
        TryTrainReinforcements();

        // 2. ORDU YÖNETİMİ: İşçileri böl ve görevlendir
        ManageArmySplit();
    }

    private void TryTrainReinforcements()
    {
        var baseB = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _myPlayerID && b.Type == SimBuildingType.Base);

        // Eğer paramız varsa ve base şu an üretim yapmıyorsa -> İşçi Bas
        if (baseB != null && !baseB.IsTraining)
        {
            // İşçi maliyeti config'den gelir (Genelde 250 ET)
            if (SimResourceSystem.CanAfford(_world, _myPlayerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
            {
                SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
            }
        }
    }

    private void ManageArmySplit()
    {
        // Tüm işçilerimi bul ve ID'ye göre sırala (Listenin sabit kalması için)
        var myWorkers = _world.Units.Values
            .Where(u => u.PlayerID == _myPlayerID && u.UnitType == SimUnitType.Worker)
            .OrderBy(u => u.ID)
            .ToList();

        int totalWorkers = myWorkers.Count;
        if (totalWorkers == 0) return;

        // Hedef Belirle: Öncelik Düşman Üssü
        var target = FindBestTarget();

        // --- TAKTİKSEL BÖLÜNME ---
        // Toplam işçinin yarısı saldırıya, kalanı ekonomiye
        // Örn: 5 işçi -> 2 Saldırı, 3 Ekonomi
        int attackSquadSize = totalWorkers / 2;

        // Eğer çok az işçimiz varsa (örn 2 tane), en az 1'i saldırsın
        if (totalWorkers > 0 && attackSquadSize == 0) attackSquadSize = 1;

        for (int i = 0; i < totalWorkers; i++)
        {
            var worker = myWorkers[i];

            // --- GRUP 1: SALDIRI TİMİ ---
            if (i < attackSquadSize)
            {
                if (target != null)
                {
                    // Eğer işçi şu an saldırmıyorsa veya boş boş duruyorsa saldırı emri ver
                    // (Sürekli emir verip hareketi resetlememek için durum kontrolü yapıyoruz)
                    bool isBusyAttacking = (worker.State == SimTaskType.Attacking || worker.State == SimTaskType.Moving);

                    // Hedef değiştirmek veya ilk kez saldırmak için:
                    if (!isBusyAttacking || worker.TargetID == -1)
                    {
                        CommandAttack(worker, target);
                    }
                }
                else
                {
                    // Düşman yoksa haritanın ortasına git (Search)
                    if (worker.State == SimTaskType.Idle)
                        SimUnitSystem.OrderMove(worker, new RTS.Simulation.Data.int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2), _world);
                }
            }
            // --- GRUP 2: EKONOMİ TİMİ (Sadece ET) ---
            else
            {
                // Sadece boşta olanlara emir ver
                if (worker.State == SimTaskType.Idle)
                {
                    // Sadece ET (Meat) topla ki yeni işçi basabilelim
                    var meat = FindNearestResource(worker.GridPosition, SimResourceType.Meat);
                    if (meat != null)
                    {
                        SimUnitSystem.TryAssignGatherTask(worker, meat, _world);
                    }
                }
            }
        }
    }

    private object FindBestTarget()
    {
        // 1. Düşman Binası (Base) - Oyunu bitirmek için öncelik
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _myPlayerID && b.Type == SimBuildingType.Base);
        if (enemyBase != null) return enemyBase;

        // 2. Düşman İşçisi - Ekonomisini çökertmek için
        var enemyWorker = _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID && u.UnitType == SimUnitType.Worker);
        if (enemyWorker != null) return enemyWorker;

        // 3. Herhangi bir düşman
        return _world.Units.Values.FirstOrDefault(u => u.PlayerID != _myPlayerID);
    }

    private void CommandAttack(SimUnitData worker, object target)
    {
        if (target is SimUnitData u) SimUnitSystem.OrderAttackUnit(worker, u, _world);
        else if (target is SimBuildingData b) SimUnitSystem.OrderAttack(worker, b, _world);
    }

    private SimResourceData FindNearestResource(RTS.Simulation.Data.int2 pos, SimResourceType type)
    {
        SimResourceData best = null;
        float minDst = float.MaxValue;
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
}