using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // OrderBy için gerekli

/*
 * RLEnvironment.cs
 *
 * Bridge between Unity RTS game and Q-Learning agent.
 * UPDATED: 
 * 1. Syncs worker count with ResourceManager every step.
 * 2. Visual workers are now assigned a gathering task immediately.
 */

public class RLEnvironment : MonoBehaviour
{
    [Header("RL Training Configuration")]
    public bool isTrainingMode = false;
    public int maxEpisodesSteps = 500;

    [Header("Visual Debugging")]
    public bool visualizeTraining = true;

    [Header("Resource Collection Rates")]
    public float resourceCollectionRate = 5f; // Units per second

    [Header("References")]
    public ResourceManager resourceManager;
    public GameObject workerPrefab;
    public GameObject barracksPrefab;

    // Episode tracking
    private int currentStep = 0;
    private int currentWorkerCount = 0;
    private bool episodeCompleted = false;

    // Visual tracking
    private Base playerBase;

    // State space constants
    private const int WOOD_STATES = 4;
    private const int STONE_STATES = 4;
    private const int MEAT_STATES = 2;
    private const int POP_STATES = 2;

    // Thresholds
    private readonly int[] woodThresholds = { 0, 50, 100, 200 };
    private readonly int[] stoneThresholds = { 0, 50, 100, 200 };
    private readonly int[] meatThresholds = { 0, 50 };

    // Costs
    private const int WORKER_MEAT_COST = 50;
    private const int BARRACKS_WOOD_COST = 180;
    private const int BARRACKS_STONE_COST = 180;

    void Awake()
    {
        if (resourceManager == null && GameManager.Instance != null)
        {
            resourceManager = GameManager.Instance.resourceManager;
        }
    }

    void Start()
    {
        if (resourceManager == null && GameManager.Instance != null)
        {
            resourceManager = GameManager.Instance.resourceManager;
        }
        
        FindPlayerBase();
    }

    private void FindPlayerBase()
    {
        playerBase = null; 
        Base[] bases = FindObjectsByType<Base>(FindObjectsSortMode.None);
        
        foreach (var b in bases)
        {
            if (b.playerID == 1)
            {
                playerBase = b;
                break;
            }
        }
    }

    // --- STATE ENCODING ---
    public int GetCurrentState()
    {
        if (resourceManager == null) return 0;

        PlayerResourceData resources = resourceManager.GetPlayerResources(1);

        // --- FIX: Sync worker count with actual population ---
        // Bu satýr, "nüfus artýyor ama sistem fark etmiyor" sorununu çözer.
        currentWorkerCount = resources.currentPopulation; 
        // ----------------------------------------------------
        
        int woodState = GetResourceState(resources.wood, woodThresholds);
        int stoneState = GetResourceState(resources.stone, stoneThresholds);
        int meatState = GetResourceState(resources.meat, meatThresholds);
        int popState = (resources.currentPopulation >= resources.maxPopulation) ? 1 : 0;

        int stateIndex = (woodState * 16) + (stoneState * 4) + (meatState * 2) + popState;
        return stateIndex;
    }

    private int GetResourceState(int amount, int[] thresholds)
    {
        for (int i = thresholds.Length - 1; i >= 0; i--)
        {
            if (amount >= thresholds[i]) return i;
        }
        return 0;
    }

    // --- ACTION EXECUTION ---
    public float ExecuteAction(int action)
    {
        if (episodeCompleted) return 0f;

        currentStep++;
        float reward = -1f; // Time penalty

        switch (action)
        {
            case 0: reward += CollectResource(ResourceType.Wood); break;
            case 1: reward += CollectResource(ResourceType.Stone); break;
            case 2: reward += CollectResource(ResourceType.Meat); break;
            case 3: reward += RecruitWorker(); break;
            case 4: reward += BuildBarracks(); break;
            default: reward = -10f; break; // Invalid
        }

        if (currentStep >= maxEpisodesSteps)
        {
            episodeCompleted = true;
        }

        return reward;
    }

    private float CollectResource(ResourceType resourceType)
    {
        if (currentWorkerCount == 0) return -10f; 
        
        int amount = Mathf.RoundToInt(resourceCollectionRate * currentWorkerCount);
        resourceManager.AddResource(1, resourceType, amount);
        return 0f;
    }

    private float RecruitWorker()
    {
        PlayerResourceData resources = resourceManager.GetPlayerResources(1);
        
        if (resources.meat < WORKER_MEAT_COST) return -10f;
        if (resources.currentPopulation >= resources.maxPopulation) return -10f;

        if (!resourceManager.SpendResources(1, 0, 0, WORKER_MEAT_COST)) return -10f;

        // Nüfusu artýr (State güncellemesinde currentWorkerCount da artacak)
        resourceManager.AddPopulation(1, 1);

        // --- GÖRSEL ÝÞÇÝ OLUÞTURMA ve GÖREV ATAMA ---
        if (visualizeTraining && workerPrefab != null && playerBase != null)
        {
            Vector3 spawnPos = playerBase.transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0);
            GameObject newWorkerObj = Instantiate(workerPrefab, spawnPos, Quaternion.identity, TilemapVisualizer.Instance.objectParent);
            
            // Yeni iþçiyi bul ve ona hemen görev ver
            Worker newWorkerScript = newWorkerObj.GetComponent<Worker>();
            if (newWorkerScript != null)
            {
                newWorkerScript.playerID = 1; // Sahipliði ata
                
                // En yakýn kaynaðý bul ve toplamaya gönder
                ResourceNode nearestResource = FindNearestResource(newWorkerObj.transform.position);
                if (nearestResource != null)
                {
                    newWorkerScript.Gather(nearestResource);
                }
            }
        }
        // ---------------------------------------------

        return 5f; 
    }

    // Yardýmcý Fonksiyon: En yakýn kaynaðý bulur
    private ResourceNode FindNearestResource(Vector3 position)
    {
        ResourceNode[] allResources = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        if (allResources.Length == 0) return null;

        return allResources.OrderBy(r => Vector3.Distance(position, r.transform.position)).FirstOrDefault();
    }

    private float BuildBarracks()
    {
        PlayerResourceData resources = resourceManager.GetPlayerResources(1);

        if (resources.wood < BARRACKS_WOOD_COST || resources.stone < BARRACKS_STONE_COST) return -10f;

        if (!resourceManager.SpendResources(1, BARRACKS_WOOD_COST, BARRACKS_STONE_COST, 0)) return -10f;

        episodeCompleted = true; 
        
        if (visualizeTraining && barracksPrefab != null && playerBase != null)
        {
            Vector3 buildPos = playerBase.transform.position + new Vector3(3f, 0, 0);
            Instantiate(barracksPrefab, buildPos, Quaternion.identity, TilemapVisualizer.Instance.objectParent);
        }

        return 1000f; 
    }

    public bool IsTerminal() { return episodeCompleted; }

    // --- RESET LOGIC ---
    public void ResetEpisode()
    {
        currentStep = 0;
        currentWorkerCount = 0;
        episodeCompleted = false;

        CleanupAndResetMap();
        
        Debug.Log("Episode reset! Map regenerated.");
    }

    private void CleanupAndResetMap()
    {
        if (resourceManager == null)
        {
            if (GameManager.Instance != null) resourceManager = GameManager.Instance.resourceManager;
            if (resourceManager == null) { Debug.LogError("ResourceManager missing in Reset!"); return; }
        }

        if (resourceManager != null)
        {
            resourceManager.SetResources(1, 0, 0, 50); 
            resourceManager.SetPopulation(1, 0);
            resourceManager.SetResources(2, 0, 0, 50); 
            resourceManager.SetPopulation(2, 0);
        }

        if (TilemapVisualizer.Instance != null)
        {
            if (isTrainingMode)
            {
                TilemapVisualizer.Instance.spawnEnemy = false;
            }
            
            TilemapVisualizer.Instance.ResetSimulation();
        }
        else
        {
            Debug.LogError("[RLEnvironment] TilemapVisualizer not found! Cannot reset map.");
        }

        FindPlayerBase();
    }

    public string GetEpisodeSummary()
    {
        if (resourceManager == null) return "Error";
        PlayerResourceData res = resourceManager.GetPlayerResources(1);
        return $"Workers: {currentWorkerCount} | W:{res.wood} S:{res.stone} M:{res.meat}";
    }
}