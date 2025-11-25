using UnityEngine;
using System.Collections.Generic;
using System.IO;

/*
 * QLearningAgent.cs
 * * Implements Q-Learning algorithm.
 * Manages Q-Table (64 states x 5 actions) and performs epsilon-greedy exploration.
 */

public class QLearningAgent : MonoBehaviour
{
    [Header("Q-Learning Hyperparameters")]
    [Range(0f, 1f)]
    public float learningRate = 0.1f;        // Alpha
    
    [Range(0f, 1f)]
    public float discountFactor = 0.9f;      // Gamma
    
    [Range(0f, 1f)]
    public float epsilon = 1.0f;             // Exploration rate (starts at 1.0)
    
    [Range(0f, 1f)]
    public float epsilonMin = 0.01f;         // Minimum epsilon
    
    [Range(0f, 1f)]
    public float epsilonDecay = 0.995f;      // Epsilon decay rate per episode
    
    [Header("Q-Table Configuration")]
    private const int NUM_STATES = 64;       // 4*4*2*2
    private const int NUM_ACTIONS = 5;       // Collect Wood/Stone/Meat, Recruit, Build
    
    // Q-Table: [state, action] -> Q-value
    private float[,] qTable;
    
    // Statistics
    private int totalTrainingSteps = 0;
    private int episodeCount = 0;
    
    void Awake()
    {
        InitializeQTable();
    }
    
    /// <summary>
    /// Initializes Q-Table with zero values.
    /// </summary>
    private void InitializeQTable()
    {
        qTable = new float[NUM_STATES, NUM_ACTIONS];
        
        // Optional: Random initialization
        // for (int s = 0; s < NUM_STATES; s++)
        // {
        //     for (int a = 0; a < NUM_ACTIONS; a++)
        //     {
        //         qTable[s, a] = Random.Range(0f, 0.01f);
        //     }
        // }
        
        Debug.Log($"Q-Table initialized: {NUM_STATES} states x {NUM_ACTIONS} actions");
    }
    
    /// <summary>
    /// Epsilon-greedy action selection
    /// </summary>
    public int SelectAction(int state)
    {
        // Exploration: Random action
        if (Random.value < epsilon)
        {
            int randomAction = Random.Range(0, NUM_ACTIONS);
            // Debug.Log($"[EXPLORE e={epsilon:F3}] State {state} -> Random Action {randomAction}");
            return randomAction;
        }
        
        // Exploitation: Best action from Q-table
        int bestAction = GetBestAction(state);
        // Debug.Log($"[EXPLOIT] State {state} -> Best Action {bestAction} (Q={qTable[state, bestAction]:F2})");
        return bestAction;
    }
    
    /// <summary>
    /// Returns the action with the highest Q-value for the given state.
    /// </summary>
    private int GetBestAction(int state)
    {
        int bestAction = 0;
        float maxQValue = qTable[state, 0];
        
        for (int a = 1; a < NUM_ACTIONS; a++)
        {
            if (qTable[state, a] > maxQValue)
            {
                maxQValue = qTable[state, a];
                bestAction = a;
            }
        }
        
        return bestAction;
    }
    
    /// <summary>
    /// Q-value update (Bellman equation)
    /// Q(s,a) = Q(s,a) + alpha * [R + gamma * max Q(s',a') - Q(s,a)]
    /// </summary>
    public void UpdateQValue(int state, int action, float reward, int nextState, bool isTerminal)
    {
        float currentQ = qTable[state, action];
        
        float maxNextQ = 0f;
        if (!isTerminal)
        {
            // Find max Q-value for next state
            maxNextQ = qTable[nextState, 0];
            for (int a = 1; a < NUM_ACTIONS; a++)
            {
                if (qTable[nextState, a] > maxNextQ)
                {
                    maxNextQ = qTable[nextState, a];
                }
            }
        }
        
        // Bellman update
        float tdTarget = reward + discountFactor * maxNextQ;
        float tdError = tdTarget - currentQ;
        float newQ = currentQ + learningRate * tdError;
        
        qTable[state, action] = newQ;
        
        totalTrainingSteps++;
        
        // Debug log (every 50 steps)
        if (totalTrainingSteps % 50 == 0)
        {
            // Debug.Log($"Q-Update | S:{state} A:{action} R:{reward:F1} -> Q:{currentQ:F2} -> {newQ:F2} (Error={tdError:F2})");
        }
    }
    
    /// <summary>
    /// Decays epsilon at the end of an episode (exploration -> exploitation).
    /// </summary>
    public void DecayEpsilon()
    {
        episodeCount++;
        epsilon = Mathf.Max(epsilonMin, epsilon * epsilonDecay);
        
        Debug.Log($"Episode {episodeCount} completed. Epsilon decayed to {epsilon:F4}");
    }
    
    /// <summary>
    /// Returns Q-value for specific state and action.
    /// </summary>
    public float GetQValue(int state, int action)
    {
        return qTable[state, action];
    }
    
    /// <summary>
    /// Saves Q-Table to CSV format.
    /// </summary>
    public void SaveQTable(string filePath)
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                // Header
                writer.Write("State");
                for (int a = 0; a < NUM_ACTIONS; a++)
                {
                    writer.Write($",Action{a}");
                }
                writer.WriteLine();
                
                // Q-values
                for (int s = 0; s < NUM_STATES; s++)
                {
                    writer.Write(s);
                    for (int a = 0; a < NUM_ACTIONS; a++)
                    {
                        writer.Write($",{qTable[s, a]:F4}");
                    }
                    writer.WriteLine();
                }
            }
            
            Debug.Log($"Q-Table saved to: {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save Q-Table: {e.Message}");
        }
    }
    
    /// <summary>
    /// Loads Q-Table from CSV.
    /// </summary>
    public void LoadQTable(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Q-Table file not found: {filePath}");
                return;
            }
            
            string[] lines = File.ReadAllLines(filePath);
            
            // Skip header
            for (int i = 1; i < lines.Length && i <= NUM_STATES; i++)
            {
                string[] values = lines[i].Split(',');
                int state = int.Parse(values[0]);
                
                for (int a = 0; a < NUM_ACTIONS; a++)
                {
                    qTable[state, a] = float.Parse(values[a + 1]);
                }
            }
            
            Debug.Log($"Q-Table loaded from: {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load Q-Table: {e.Message}");
        }
    }
    
    /// <summary>
    /// Analyzes learned policy (for debugging).
    /// </summary>
    public void PrintLearnedPolicy()
    {
        Debug.Log("=== LEARNED POLICY (Top 10 States) ===");
        
        // Sample important states
        int[] sampleStates = { 0, 1, 8, 16, 24, 32, 40, 48, 56, 63 };
        string[] actionNames = { "CollectWood", "CollectStone", "CollectMeat", "RecruitWorker", "BuildBarracks" };
        
        foreach (int state in sampleStates)
        {
            int bestAction = GetBestAction(state);
            float maxQ = qTable[state, bestAction];
            
            Debug.Log($"State {state:D2} -> {actionNames[bestAction]} (Q={maxQ:F2})");
        }
    }
    
    /// <summary>
    /// Returns training statistics.
    /// </summary>
    public string GetStats()
    {
        return $"Episodes: {episodeCount} | Steps: {totalTrainingSteps} | e: {epsilon:F4} | a: {learningRate} | g: {discountFactor}";
    }
}