using UnityEngine;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensor
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;

    private const float MAX_HP = 500f;
    private const int TEAM_ME = 0;

    public RTSGridSensor(SimWorldState world, SimGridSystem gridSystem)
    {
        _world = world;
        _gridSystem = gridSystem;
    }

    public void AddGridObservations(VectorSensor sensor)
    {
        int width = _world.Map.Width;
        int height = _world.Map.Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var node = _gridSystem.GetNode(x, y);

                float entityType = 0f;
                float teamInfo = 0f;
                float hpRatio = 0f;

                if (node != null && node.OccupantID != -1)
                {
                    if (_world.Buildings.ContainsKey(node.OccupantID))
                    {
                        var b = _world.Buildings[node.OccupantID];
                        entityType = 0.3f;
                        teamInfo = (b.PlayerID == TEAM_ME) ? 1f : -1f;
                        hpRatio = (float)b.Health / MAX_HP;
                    }
                    else if (_world.Units.ContainsKey(node.OccupantID))
                    {
                        var u = _world.Units[node.OccupantID];
                        entityType = 0.6f;
                        teamInfo = (u.PlayerID == TEAM_ME) ? 1f : -1f;
                        hpRatio = (float)u.Health / MAX_HP;
                    }
                }

                sensor.AddObservation(entityType);
                sensor.AddObservation(teamInfo);
                sensor.AddObservation(hpRatio);
            }
        }
    }

    public void AddGlobalStats(VectorSensor sensor)
    {
        if (_world.Players.ContainsKey(1))
        {
            var player = _world.Players[1];
            sensor.AddObservation(player.Wood / 2000f);
            sensor.AddObservation(player.Meat / 2000f);
            sensor.AddObservation(player.Stone / 2000f);
            sensor.AddObservation((float)player.CurrentPopulation / 50f);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
        sensor.AddObservation(0f);
    }
}