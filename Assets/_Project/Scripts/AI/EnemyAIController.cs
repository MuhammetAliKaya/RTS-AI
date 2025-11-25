using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/*
 * EnemyAIController.cs
 *
 * Controls the Enemy AI logic.
 * Decides on actions like training workers, building structures, and attacking.
 */
public class EnemyAIController : MonoBehaviour
{
    [Header("AI Settings")]
    public float decisionRate = 1.0f;
    public int aiPlayerID = 2;

    [Header("Strategic Ratios")]
    public int desiredWorkerCount = 5;
    public int attackThresholdSoldiers = 5;

    [Header("Building Prefabs")]
    [Tooltip("Prefab for the Barracks building.")]
    public GameObject barracksPrefab;

    // --- Internal State ---
    private float decisionTimer = 0f;
    private Base aiBase;

    private List<Unit> aiUnits = new List<Unit>();
    private List<Building> aiBuildings = new List<Building>();
    private GridSystem gridSystem;
    private ResourceManager resourceManager;

    void Start()
    {
        if (GameManager.Instance != null)
        {
            gridSystem = TilemapVisualizer.Instance.gridSystem;
            resourceManager = GameManager.Instance.resourceManager;
        }
    }

    public void RunAI()
    {
        if (aiBase == null)
        {
            InitializeAI();
            return;
        }

        decisionTimer += Time.deltaTime;

        if (decisionTimer >= decisionRate)
        {
            decisionTimer = 0f;
            MakeDecision();
        }
    }

    private void InitializeAI()
    {
        Base foundBase = FindObjectsByType<Base>(FindObjectsSortMode.None).FirstOrDefault(b => b.playerID == aiPlayerID);
        if (foundBase != null)
        {
            aiBase = foundBase;
            Debug.Log($"[AI] Player {aiPlayerID} Base found. AI Active.");
        }
    }


    private void MakeDecision()
    {
        if (aiBase == null || !aiBase.isFunctional) return;

        // Ensure resourceManager is available
        if (resourceManager == null)
        {
            if (GameManager.Instance != null)
            {
                resourceManager = GameManager.Instance.resourceManager;
            }
            
            if (resourceManager == null) return;
        }

        // 1. State Detection
        aiUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u => u.playerID == aiPlayerID && u.currentHealth > 0).ToList();
        aiBuildings = FindObjectsByType<Building>(FindObjectsSortMode.None).Where(b => b.playerID == aiPlayerID).ToList();

        int currentWorkers = aiUnits.Count(u => u is Worker);
        int currentSoldiers = aiUnits.Count(u => u is Soldier);

        Worker idleWorker = aiUnits
            .Where(u => u is Worker)
            .Cast<Worker>()
            .FirstOrDefault(w => w.IsIdle());


        // --- DECISION 1: TRAIN WORKER ---
        if (currentWorkers < desiredWorkerCount)
        {
            ProductionCost workerCost = aiBase.workerProductionCost;

            // We do NOT use SpendResources here because Base.StartTrainingWorker() handles it.
            if (resourceManager.CanAfford(aiPlayerID, workerCost.woodCost, workerCost.stoneCost, workerCost.meatCost) &&
                resourceManager.GetPlayerResources(aiPlayerID).currentPopulation < resourceManager.GetPlayerResources(aiPlayerID).maxPopulation)
            {
                aiBase.StartTrainingWorker();
                Debug.Log($"[AI: P{aiPlayerID}] DECISION: TRAIN WORKER.");
                return;
            }
        }

        // --- DECISION 2: BUILD BARRACKS ---
        bool hasBarracks = aiBuildings.Any(b => b is Barracks);

        if (!hasBarracks && currentWorkers >= desiredWorkerCount && idleWorker != null)
        {
            if (barracksPrefab == null)
            {
                Debug.LogError("[AI] 'barracksPrefab' not assigned in EnemyAIController!");
                return;
            }

            Building prefabBuilding = barracksPrefab.GetComponent<Building>();

            // Use SpendResources here to deduct resources immediately for construction
            if (resourceManager.SpendResources(
                aiPlayerID,
                prefabBuilding.cost.woodCost,
                prefabBuilding.cost.stoneCost,
                prefabBuilding.cost.meatCost))
            {
                // Successfully spent resources
                Node baseNode = TilemapVisualizer.Instance.NodeFromWorldPoint(aiBase.transform.position);
                Node buildLocation = FindBuildLocationNear(baseNode);

                if (buildLocation != null)
                {
                    bool success = idleWorker.StartBuildingTask(buildLocation, barracksPrefab);
                    if (success)
                    {
                        Debug.Log($"[AI: P{aiPlayerID}] DECISION: BUILD BARRACKS -> ({buildLocation.x}, {buildLocation.y})");
                        return;
                    }
                }
            }
        }
        // --- DONE ---

        // --- DECISION 3: TRAIN SOLDIER ---
        Barracks aiBarracks = aiBuildings.FirstOrDefault(b => b is Barracks && b.isFunctional) as Barracks;
        if (aiBarracks != null)
        {
            ProductionCost soldierCost = aiBarracks.soldierProductionCost;

            // We do NOT use SpendResources here because Barracks.StartTrainingSoldier() handles it.
            if (resourceManager.CanAfford(aiPlayerID, soldierCost.woodCost, soldierCost.stoneCost, soldierCost.meatCost) &&
                resourceManager.GetPlayerResources(aiPlayerID).currentPopulation < resourceManager.GetPlayerResources(aiPlayerID).maxPopulation)
            {
                aiBarracks.StartTrainingSoldier();
                Debug.Log($"[AI: P{aiPlayerID}] DECISION: TRAIN SOLDIER.");
                return;
            }
        }

        // --- DECISION 4: GATHER RESOURCES ---
        if (idleWorker != null)
        {
            GatherResources(idleWorker);
            return;
        }

        // --- DECISION 5: ATTACK ---
        if (currentSoldiers >= attackThresholdSoldiers)
        {
            Base enemyBase = FindObjectsByType<Base>(FindObjectsSortMode.None).FirstOrDefault(b => b.playerID != aiPlayerID);
            if (enemyBase != null)
            {
                var soldiers = aiUnits.Where(u => u is Soldier).Cast<Soldier>().ToList();
                foreach (var soldier in soldiers)
                {
                    if (soldier.IsIdle())
                    {
                        soldier.AttackBuilding(enemyBase);
                    }
                }
                Debug.Log($"[AI: P{aiPlayerID}] DECISION: ATTACK!");
            }
        }
    }

    private void GatherResources(Worker worker)
    {
        PlayerResourceData data = resourceManager.GetPlayerResources(aiPlayerID);
        ResourceType neededType = ResourceType.Wood;

        if (data.wood < data.meat + 50) { neededType = ResourceType.Wood; }
        else if (data.stone < data.wood) { neededType = ResourceType.Stone; }
        else { neededType = ResourceType.Meat; }

        ResourceNode targetResource = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
            .Where(r => r.resourceType == neededType)
            .FirstOrDefault(r => FindWalkableSpotNearForAI(r.transform.position) != null);

        if (targetResource != null)
        {
            worker.Gather(targetResource);
        }
    }

    private Node FindWalkableSpotNearForAI(Vector3 resourceWorldPos)
    {
        if (gridSystem == null) return null;
        Node resourceNode = TilemapVisualizer.Instance.NodeFromWorldPoint(resourceWorldPos);
        if (resourceNode == null) return null;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                Node neighbour = gridSystem.GetNode(resourceNode.x + x, resourceNode.y + y);
                if (neighbour != null && neighbour.isWalkable)
                {
                    return neighbour;
                }
            }
        }
        return null;
    }

    private Node FindBuildLocationNear(Node centerNode)
    {
        if (gridSystem == null || centerNode == null) return null;
        int searchRadius = 5;

        for (int x = -searchRadius; x <= searchRadius; x++)
        {
            for (int y = -searchRadius; y <= searchRadius; y++)
            {
                if (Mathf.Abs(x) < 2 && Mathf.Abs(y) < 2) continue;

                Node node = gridSystem.GetNode(centerNode.x + x, centerNode.y + y);

                if (node != null && node.isWalkable && node.type == NodeType.Grass)
                {
                    return node;
                }
            }
        }
        return null;
    }
}