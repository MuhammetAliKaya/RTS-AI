using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;

    // Debug
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
    }

    public override void OnEpisodeBegin()
    {
        if (Runner != null) Runner.ResetSimulation();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null) return;
        _gridSensor.AddGlobalStats(sensor);
        _gridSensor.AddGridObservations(sensor);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_world == null) return;

        int command = actions.DiscreteActions[0];
        int targetX = actions.DiscreteActions[1];
        int targetY = actions.DiscreteActions[2];

        _lastDebugCommand = command;
        _lastDebugX = targetX;
        _lastDebugY = targetY;

        bool isSuccess = _translator.ExecuteAction(command, targetX, targetY);

        // Ödül Sistemi
        if (!isSuccess && command != 0)
        {
            AddReward(-0.005f); // Geçersiz hamle cezası
        }
        else if (command != 0)
        {
            AddReward(0.001f); // Geçerli hamle teşviki
        }

        // Loglama (Sadece izleme modunda)
        if (Runner != null && !Runner.TrainMode && command != 0)
        {
            // Log mantığı burada...
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_world == null || !_world.Players.ContainsKey(1)) return;
        var player = _world.Players[1];

        // --- VARLIK KONTROLLERİ ---
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

        // --- 1. SEVİYE KISITLAMALARI ---
        if (Runner.CurrentLevel < 3)
        {
            actionMask.SetActionEnabled(0, 1, false); // Ev
            actionMask.SetActionEnabled(0, 2, false); // Kışla
            actionMask.SetActionEnabled(0, 7, false); // Çiftlik
            actionMask.SetActionEnabled(0, 8, false); // Oduncu
            actionMask.SetActionEnabled(0, 9, false); // Taş Ocağı
        }
        if (Runner.CurrentLevel < 4)
        {
            actionMask.SetActionEnabled(0, 4, false); // Asker Üret
            actionMask.SetActionEnabled(0, 5, false); // Saldır
        }

        // --- 2. BİRİM GEREKSİNİMLERİ ---
        if (!hasWorker)
        {
            // İşçi yoksa inşaat ve toplama yapılamaz
            int[] workerActions = { 1, 2, 6, 7, 8, 9 };
            foreach (var act in workerActions) actionMask.SetActionEnabled(0, act, false);
        }
        if (soldierCount == 0)
        {
            actionMask.SetActionEnabled(0, 5, false); // Saldır
        }

        // --- 3. KAYNAK KONTROLLERİ (Generic) ---
        CheckAffordability(actionMask, 1, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
        CheckAffordability(actionMask, 2, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
        CheckAffordability(actionMask, 7, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);
        CheckAffordability(actionMask, 8, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT);
        CheckAffordability(actionMask, 9, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT);

        // --- 4. ÜRETİM KONTROLLERİ ---
        // İşçi Üretimi (Base + Kaynak + Popülasyon)
        bool canAffordWorker = SimResourceSystem.CanAfford(_world, 1, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        if (!hasBase || !canAffordWorker || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 3, false);

        // Asker Üretimi (Kışla + Kaynak + Popülasyon)
        bool canAffordSoldier = SimResourceSystem.CanAfford(_world, 1, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        if (!hasBarracks || !canAffordSoldier || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 4, false);
    }

    private void CheckAffordability(IDiscreteActionMask mask, int actionIndex, int w, int s, int m)
    {
        if (!SimResourceSystem.CanAfford(_world, 1, w, s, m))
        {
            mask.SetActionEnabled(0, actionIndex, false);
        }
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