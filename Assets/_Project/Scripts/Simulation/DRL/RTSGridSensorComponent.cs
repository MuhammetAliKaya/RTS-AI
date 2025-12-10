using UnityEngine;
using Unity.MLAgents.Sensors;
using Unity.MLAgents;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensorComponent : SensorComponent
{
    [Header("Configurations")]
    public string SensorName = "RTSMapSensor";

    private RTSGridSensor _sensor;

    public void InitializeSensor(SimWorldState world, SimGridSystem gridSys)
    {
        if (_sensor != null)
        {
            _sensor.SetReferences(world, gridSys);
        }
        else
        {
            _sensor = new RTSGridSensor(world, gridSys, SensorName);
        }
    }

    public override ISensor[] CreateSensors()
    {
        _sensor = new RTSGridSensor(null, null, SensorName);
        return new ISensor[] { _sensor };
    }
}