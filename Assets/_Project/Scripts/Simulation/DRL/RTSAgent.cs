using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq; // Linq kütüphanesini eklemeyi unutma


public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem; // ARTIK BU ÖNEMLİ
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;
    public AdversarialTrainerRunner CombatRunner;

    private int _lastDebugX = -1;
    private int _lastDebugY = -1;
    private int _lastDebugCommand = 0;

    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys)
    {
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        _gridSensor = new RTSGridSensor(_world, _gridSystem);
        _translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem);

        // Setup çağrıldığında eventleri yeniden bağlamamız gerekebilir
        // Ama genellikle OnEnable yeterlidir. Setup OnEnable'dan sonra çağrılırsa diye:
        RebindEvents();
    }

    // --- DEĞİŞİKLİK BURADA: Static Event yerine Instance Event ---
    protected override void OnEnable()
    {
        base.OnEnable();
        RebindEvents();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;
        }
    }

    private void RebindEvents()
    {
        // Önce temizle (çift aboneliği önlemek için)
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;

            // Sonra ekle
            _unitSystem.OnUnitAttackedUnit += HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding += HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy += HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding += HandleUnitDestroyedBuilding;
        }
    }
    // -------------------------------------------------------------

    private void HandleUnitAttackedUnit(SimUnitData attacker, SimUnitData victim, float damage)
    {
        if (attacker.PlayerID == 1) AddReward(damage * 0.002f);
    }

    private void HandleUnitAttackedBuilding(SimUnitData attacker, SimBuildingData building, float damage)
    {
        if (attacker.PlayerID == 1)
        {
            float multiplier = (building.Type == SimBuildingType.Base) ? 0.005f : 0.002f;
            AddReward(damage * multiplier);
        }
    }

    private void HandleUnitKilledEnemy(SimUnitData attacker, SimUnitData victim)
    {
        if (attacker.PlayerID == 1) AddReward(1.0f);
    }

    private void HandleUnitDestroyedBuilding(SimUnitData attacker, SimBuildingData building)
    {
        if (attacker.PlayerID == 1)
        {
            if (building.Type == SimBuildingType.Base)
            {
                AddReward(10.0f);
                EndEpisode();
            }
            else AddReward(2.0f);
        }
    }

    public override void OnEpisodeBegin()
    {
        if (Runner != null) Runner.ResetSimulation();
        else if (CombatRunner != null) CombatRunner.ResetSimulation();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null) return;

        // 1. Grid Sensor (Eğer kullanıyorsan kalsın, ama RUSH için çok şart değil)
        // if (_gridSensor != null) _gridSensor.AddGridObservations(sensor);

        // 2. Global İstatistikler (RTSGridSensor içindekini buraya alıyoruz ki kontrol bizde olsun)
        if (_world.Players.ContainsKey(1))
        {
            var me = _world.Players[1];
            sensor.AddObservation(me.Wood / 2000f); // Kaynaklar
            sensor.AddObservation(me.Stone / 1000f);
            sensor.AddObservation(me.Meat / 1000f);
            sensor.AddObservation(me.CurrentPopulation / 50f);

            // Asker Sayısı (Çok Önemli)
            int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
            sensor.AddObservation(soldierCount / 20f);
        }
        else
        {
            sensor.AddObservation(new float[5]); // Boş veri
        }

        // 3. PUSULA (Düşman Nerede?) - İŞTE BU ÇOK ÖNEMLİ
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != 1 && b.Type == SimBuildingType.Base);

        if (myBase != null && enemyBase != null)
        {
            // Düşmana olan yön vektörü
            Vector2 dir = new Vector2(enemyBase.GridPosition.x - myBase.GridPosition.x,
                                      enemyBase.GridPosition.y - myBase.GridPosition.y).normalized;
            sensor.AddObservation(dir); // 2 float

            // Mesafe (Yaklaştıkça saldırı isteği artsın)
            float dist = Vector2.Distance(new Vector2(myBase.GridPosition.x, myBase.GridPosition.y),
                                          new Vector2(enemyBase.GridPosition.x, enemyBase.GridPosition.y));
            sensor.AddObservation(dist / 40f); // Harita boyutuna böl
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_world == null) return;

        // Sadece 1 tane discrete action alıyoruz (0-7 arası)
        int command = actions.DiscreteActions[0];

        // Eski TargetX, TargetY artık yok. Translator kendi buluyor.
        bool isSuccess = _translator.ExecuteAction(command);

        // --- MİKRO ÖDÜLLER ---

        // Geçersiz hamle cezası (Param yokken kışla basmaya çalışma)
        if (!isSuccess && command != 0)
        {
            AddReward(-0.01f);
        }

        // Asker basma teşviği (Rush stratejisi için)
        if (isSuccess && command == 4) // Asker
        {
            AddReward(0.5f); // Asker basmak iyidir
        }

        // Saldırı komutu teşviği (Sadece askerim varsa)
        if (isSuccess && command == 5) // Attack Base
        {
            int soldiers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
            if (soldiers > 3)
            {
                AddReward(1.0f); // Ordun var ve saldırdın, bravo!
            }
            else
            {
                AddReward(-0.5f); // Askerin yokken intihar etme
            }
        }

        // Ufak bir zaman cezası (Hızlı bitirmeye teşvik)
        AddReward(-0.0001f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // (Bu kısım değişmedi, aynen kalabilir)
        if (_world == null || !_world.Players.ContainsKey(1)) return;
        var player = _world.Players[1];

        bool hasWorker = false;
        int soldierCount = 0;
        bool hasBarracks = false;
        bool hasBase = false;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == 1)
            {
                if (u.UnitType == SimUnitType.Worker) hasWorker = true;
                if (u.UnitType == SimUnitType.Soldier) soldierCount++;
            }
        }
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 1 && b.IsConstructed)
            {
                if (b.Type == SimBuildingType.Barracks) hasBarracks = true;
                if (b.Type == SimBuildingType.Base) hasBase = true;
            }
        }

        if (Runner != null)
        {
            // if (Runner.CurrentLevel < 3)
            // {
            //     actionMask.SetActionEnabled(0, 1, false);
            //     actionMask.SetActionEnabled(0, 2, false);
            //     actionMask.SetActionEnabled(0, 7, false);
            //     actionMask.SetActionEnabled(0, 8, false);
            //     actionMask.SetActionEnabled(0, 9, false);
            // }
            // if (Runner.CurrentLevel < 4)
            // {
            //     actionMask.SetActionEnabled(0, 4, false);
            //     actionMask.SetActionEnabled(0, 5, false);
            // }
        }

        if (!hasWorker)
        {
            int[] workerActions = { 1, 2, 6, 7, 8, 9 };
            foreach (var act in workerActions) actionMask.SetActionEnabled(0, act, false);
        }

        if (soldierCount == 0) actionMask.SetActionEnabled(0, 5, false);

        CheckAffordability(actionMask, 1, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
        CheckAffordability(actionMask, 2, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
        CheckAffordability(actionMask, 7, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);
        CheckAffordability(actionMask, 8, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT);
        CheckAffordability(actionMask, 9, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT);

        bool canAffordWorker = SimResourceSystem.CanAfford(_world, 1, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        if (!hasBase || !canAffordWorker || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 3, false);

        bool canAffordSoldier = SimResourceSystem.CanAfford(_world, 1, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        if (!hasBarracks || !canAffordSoldier || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 4, false);
    }

    private void CheckAffordability(IDiscreteActionMask mask, int actionIndex, int w, int s, int m)
    {
        if (!SimResourceSystem.CanAfford(_world, 1, w, s, m))
            mask.SetActionEnabled(0, actionIndex, false);
    }

    private void OnDrawGizmos()
    {
        if (_world == null || _lastDebugX == -1) return;
        Vector3 targetPos = new Vector3(_lastDebugX, 0.5f, _lastDebugY);
        Color debugColor = Color.white;
        switch (_lastDebugCommand)
        {
            case 0: debugColor = Color.gray; break;
            case 1: case 2: case 7: case 8: case 9: debugColor = Color.yellow; break;
            case 3: case 4: debugColor = Color.cyan; break;
            case 5: debugColor = Color.red; break;
            case 6: debugColor = Color.green; break;
        }
        Gizmos.color = debugColor;
        Gizmos.DrawWireCube(targetPos, new Vector3(0.9f, 0.1f, 0.9f));
        Gizmos.DrawLine(transform.position, targetPos);
    }
}