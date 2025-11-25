using UnityEngine;
using System.Collections;

/*
 * Barracks.cs
 * * Represents the Barracks building.
 * Responsible for training Soldier units.
 * Inherits from the 'Building' base class.
 */
public class Barracks : Building
{
    [Header("Production Settings")]
    public GameObject soldierPrefab;
    public Transform spawnPoint;

    [Tooltip("Cost and time to train a single Soldier.")]
    public ProductionCost soldierProductionCost; // Defined in Base.cs (Global class)

    private bool isTraining = false;

    // --- BUILDING BASE METHODS ---

    public override void Die()
    {
        Debug.Log("Barracks destroyed.");

        // Make the node walkable again
        if (buildingNode != null)
        {
            buildingNode.isWalkable = true;
        }

        Destroy(gameObject);
    }

    // --- PRODUCTION LOGIC ---

    public void StartTrainingSoldier()
    {
        // 1. Check status
        if (isTraining)
        {
            Debug.Log("Barracks is already training.");
            return;
        }
        if (!isFunctional)
        {
            Debug.Log("Barracks construction not finished yet.");
            return;
        }

        // 2. Check Resources & Population via Managers
        if (GameManager.Instance != null)
        {
            ResourceManager rm = GameManager.Instance.resourceManager;
            PlayerResourceData pData = rm.GetPlayerResources(this.playerID);

            // Check Population Limit
            if (pData.currentPopulation >= pData.maxPopulation)
            {
                Debug.Log("Population limit reached! Cannot train Soldier.");
                return;
            }

            // Check and Spend Resources
            if (rm.SpendResources(this.playerID,
                soldierProductionCost.woodCost,
                soldierProductionCost.stoneCost,
                soldierProductionCost.meatCost))
            {
                // Success: Start production
                StartCoroutine(TrainSoldierRoutine());
            }
            else
            {
                Debug.Log("Not enough resources to train Soldier.");
            }
        }
    }

    private IEnumerator TrainSoldierRoutine()
    {
        isTraining = true;
        Debug.Log("Training Soldier...");

        // Wait for training time
        yield return new WaitForSeconds(soldierProductionCost.trainTime);

        // Spawn Soldier
        if (soldierPrefab != null && spawnPoint != null)
        {
            GameObject soldierObj = Instantiate(soldierPrefab, spawnPoint.position, Quaternion.identity);

            Unit unitScript = soldierObj.GetComponent<Unit>();
            if (unitScript != null)
            {
                unitScript.playerID = this.playerID;
            }

            // Update Population
            if (GameManager.Instance != null)
            {
                // GameManager.Instance.resourceManager.AddPopulation(this.playerID, 1);
            }

            Debug.Log("Soldier trained successfully!");
        }
        else
        {
            Debug.LogError("Soldier Prefab or SpawnPoint is missing in Barracks!");
        }

        isTraining = false;
    }
}