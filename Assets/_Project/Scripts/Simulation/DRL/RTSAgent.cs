using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;

    // Setup: Runner tarafından çağrılır
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

        // Hamleyi dene ve sonucunu al
        bool isSuccess = _translator.ExecuteAction(command, targetX, targetY);

        // --- İSTATİSTİK VE CEZA MEKANİZMASI ---

        if (!isSuccess && command != 0) // Beklemek (0) hariç, başarısız her hamle cezadır
        {
            AddReward(-0.005f); // Hatalı hamle cezası

            // DÜZELTME: 'Unity.MLAgents.Stats.' kısmı kaldırıldı.
            Unity.MLAgents.Academy.Instance.StatsRecorder.Add("Actions/Invalid_Move", 1.0f, StatAggregationMethod.Sum);
        }
        else if (command != 0)
        {
            AddReward(0.001f); // Geçerli işlem ödülü (Motivasyon)

            // DÜZELTME: 'Unity.MLAgents.Stats.' kısmı kaldırıldı.
            Unity.MLAgents.Academy.Instance.StatsRecorder.Add("Actions/Valid_Move", 1.0f, StatAggregationMethod.Sum);
        }
    }
}