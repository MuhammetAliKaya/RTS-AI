using UnityEngine;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance { get; private set; }

    [Header("Simulation Settings")]
    public bool isTrainingMode = false;
    [Tooltip("Multiplier for Time.deltaTime when in normal play mode.")]
    public float timeScale = 1.0f;

    private List<ISimulationEntity> entities = new List<ISimulationEntity>();
    private List<ISimulationEntity> entitiesToAdd = new List<ISimulationEntity>();
    private List<ISimulationEntity> entitiesToRemove = new List<ISimulationEntity>();

    // --- Statistics ---
    public int TicksPerSecond { get; private set; }
    public float SimulatedTimePerRealSecond { get; private set; }

    private int tickCounter = 0;
    private float simulatedTimeAccumulator = 0f;
    private float statsTimer = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // Update Statistics every 1 second (Real Time)
        statsTimer += Time.unscaledDeltaTime;
        if (statsTimer >= 1.0f)
        {
            TicksPerSecond = tickCounter;
            SimulatedTimePerRealSecond = simulatedTimeAccumulator;
            
            tickCounter = 0;
            simulatedTimeAccumulator = 0f;
            statsTimer = 0f;
        }

        // In normal mode, we tick with Unity's frame time
        if (!isTrainingMode)
        {
            Tick(Time.deltaTime * timeScale);
        }
    }

    /// <summary>
    /// Manually advance the simulation by dt seconds.
    /// Call this from your RL training loop.
    /// </summary>
    /// <param name="dt"></param>
    public void Tick(float dt)
    {
        // Stats
        tickCounter++;
        simulatedTimeAccumulator += dt;

        // Process list modifications
        if (entitiesToAdd.Count > 0)
        {
            entities.AddRange(entitiesToAdd);
            entitiesToAdd.Clear();
        }

        if (entitiesToRemove.Count > 0)
        {
            foreach (var entity in entitiesToRemove)
            {
                entities.Remove(entity);
            }
            entitiesToRemove.Clear();
        }

        // Update all entities
        // We iterate backwards or use a copy if we expect immediate removals during tick, 
        // but since we queue removals, a standard loop is fine unless OnTick calls Register/Unregister immediately.
        // Queueing handles the modification safety.
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] != null)
            {
                entities[i].OnTick(dt);
            }
        }
    }

    public void RegisterEntity(ISimulationEntity entity)
    {
        if (!entities.Contains(entity) && !entitiesToAdd.Contains(entity))
        {
            entitiesToAdd.Add(entity);
        }
    }

    public void UnregisterEntity(ISimulationEntity entity)
    {
        if (entities.Contains(entity) || entitiesToAdd.Contains(entity))
        {
            entitiesToRemove.Add(entity);
        }
    }
}
