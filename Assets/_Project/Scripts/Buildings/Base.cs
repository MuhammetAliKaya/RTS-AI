using UnityEngine;
using System.Collections;

/*
 * Base.cs
 * * Represents the main Base building.
 * Handles Worker production and game over condition upon destruction.
 */

/// <summary>
/// Struct to hold production cost and time for a unit.
/// </summary>
[System.Serializable]
public class ProductionCost
{
    public float trainTime = 5.0f;
    public int woodCost = 0;
    public int stoneCost = 0;
    public int meatCost = 50;
}

public class Base : Building
{
    [Header("Base Settings")]
    public int populationIncreaseAmount = 5;

    [Header("Production")]
    public GameObject workerPrefab;
    public Transform spawnPoint;

    [Tooltip("Production cost and time settings for the Worker unit.")]
    public ProductionCost workerProductionCost;

    private bool isTraining = false;

    // --- BUILDING LOGIC ---

    public override void Die()
    {
        GameManager.Instance.resourceManager.IncreaseMaxPopulation(this.playerID, -populationIncreaseAmount);
        if (buildingNode != null)
        {
            buildingNode.isWalkable = true;
        }
        Debug.LogError("BASE DESTROYED! GAME OVER!");
        Destroy(gameObject);
    }

    protected override void OnBuildingComplete()
    {
        base.OnBuildingComplete();
        GameManager.Instance.resourceManager.IncreaseMaxPopulation(this.playerID, populationIncreaseAmount);
    }


    // --- PRODUCTION FUNCTIONS ---

    /// <summary>
    /// Starts the process of training a Worker.
    /// </summary>
    public void StartTrainingWorker()
    {
        // 1. Is building busy or incomplete?
        if (isTraining || !isFunctional)
        {
            Debug.LogWarning("Building is busy or construction is not complete.");
            return;
        }
        
        PlayerResourceData pData = GameManager.Instance.resourceManager.GetPlayerResources(this.playerID);

        // 2. Is there enough population capacity?
        if (pData.currentPopulation >= pData.maxPopulation)
        {
            Debug.LogWarning("Population limit reached! Build a House first.");
            return;
        }

        // 3. Are there enough resources?
        if (!GameManager.Instance.resourceManager.SpendResources(
            this.playerID,
            workerProductionCost.woodCost,
            workerProductionCost.stoneCost,
            workerProductionCost.meatCost))
        {
            Debug.LogWarning($"Not enough resources to train Worker! Required: {workerProductionCost.meatCost} Meat");
            return;
        }

        // --- All checks passed ---
        Debug.Log("Worker training started...");
        isTraining = true;
        StartCoroutine(TrainWorkerCoroutine());
    }

    /// <summary>
    /// Coroutine to handle the training time.
    /// </summary>
    private IEnumerator TrainWorkerCoroutine()
    {
        yield return new WaitForSeconds(workerProductionCost.trainTime);

        // Spawn Worker
        if (workerPrefab != null && spawnPoint != null)
        {
            GameObject worker = Instantiate(workerPrefab, spawnPoint.position, Quaternion.identity);
            
            // Initialize Worker
            Unit unitScript = worker.GetComponent<Unit>();
            if (unitScript != null)
            {
                unitScript.playerID = this.playerID;
            }

            // Add to population
            GameManager.Instance.resourceManager.AddPopulation(this.playerID, 1);
            
            Debug.Log("Worker trained!");
        }
        else
        {
            Debug.LogError("Worker Prefab or Spawn Point is missing in Base!");
        }

        isTraining = false;
    }
}