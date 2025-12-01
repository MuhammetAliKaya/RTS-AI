// using UnityEngine;
// using System.Collections;
// using System.Collections.Generic;
// using System.IO;
// using System.Text;

// /*
//  * TrainingManager.cs
//  *
//  * Manages the training loop, episode iteration, metrics logging, and convergence detection.
//  * UPDATED: Added 'stepDelayDuration' to control visual speed directly.
//  */

// public class TrainingManager : MonoBehaviour
// {
//     public static TrainingManager Instance { get; private set; } // Singleton for easy UI access

//     [Header("Training Configuration")]
//     public bool autoStartTraining = false;
//     public int totalEpisodes = 500;

//     [Range(1f, 100f)]
//     public float timeScaleDuringTraining = 1f;

//     [Header("Visualization")]
//     [Tooltip("Enable to see units and buildings spawned during training")]
//     public bool visualizeTraining = true;

//     [Tooltip("Delay between steps in seconds (Lower = Faster, Higher = Slower)")]
//     public float stepDelayDuration = 0.002f; // Increased to 0.2s for better observation

//     [Header("Early Stopping")]
//     public bool enableEarlyStop = true;
//     public float targetSuccessRate = 0.95f;     // Stop if 95% success rate reached
//     public int successRateWindow = 50;         // Look at last 50 episodes

//     [Header("References")]
//     public RLEnvironment environment;
//     public QLearningAgent agent;

//     [Header("Output")]
//     public string metricsFilePath = "training_metrics.csv";
//     public string qTableFilePath = "qtable.csv";

//     // Training state - Made Public/Property for UI
//     public bool IsTraining { get; private set; } = false;
//     public int CurrentEpisode { get; private set; } = 0;

//     // Metrics - Made Public for UI
//     public List<EpisodeMetrics> AllMetrics { get; private set; } = new List<EpisodeMetrics>();
//     private float originalTimeScale;

//     [System.Serializable]
//     public class EpisodeMetrics // Made Public for UI
//     {
//         public int episode;
//         public int steps;
//         public float totalReward;
//         public bool success;
//         public float epsilon;

//         public EpisodeMetrics(int ep, int st, float rew, bool suc, float eps)
//         {
//             episode = ep;
//             steps = st;
//             totalReward = rew;
//             success = suc;
//             epsilon = eps;
//         }

//         public override string ToString()
//         {
//             return $"{episode},{steps},{totalReward:F2},{(success ? 1 : 0)},{epsilon:F4}";
//         }
//     }

//     void Awake()
//     {
//         if (Instance != null && Instance != this) { Destroy(gameObject); return; }
//         Instance = this;
//     }

//     void Start()
//     {
//         originalTimeScale = Time.timeScale;

//         if (environment == null || agent == null)
//         {
//             Debug.LogError("[TrainingManager] Missing references!");
//             return;
//         }

//         if (autoStartTraining)
//         {
//             StartTraining();
//         }
//     }

//     void Update()
//     {
//         // Allow dynamic speed adjustment during training
//         if (IsTraining)
//         {
//             Time.timeScale = timeScaleDuringTraining;
//         }
//     }

//     /// <summary>
//     /// Starts the training process.
//     /// </summary>
//     public void StartTraining()
//     {
//         if (IsTraining)
//         {
//             Debug.LogWarning("[TrainingManager] Training already in progress!");
//             return;
//         }

//         // Sync visualization setting
//         if (environment != null)
//         {
//             // environment.visualizeTraining = visualizeTraining;
//         }

//         IsTraining = true;
//         CurrentEpisode = 0;
//         AllMetrics.Clear();

//         // Set training time scale
//         Time.timeScale = timeScaleDuringTraining;

//         Debug.Log($"[TrainingManager] TRAINING STARTED! Target: {totalEpisodes} episodes | Speed: {timeScaleDuringTraining}x");

//         StartCoroutine(TrainingLoop());
//     }

//     /// <summary>
//     /// Main training loop.
//     /// </summary>
//     private IEnumerator TrainingLoop()
//     {
//         for (int ep = 0; ep < totalEpisodes; ep++)
//         {
//             CurrentEpisode = ep + 1;

//             // Run one episode (wait for completion)
//             EpisodeMetrics metrics = null;
//             yield return StartCoroutine(RunEpisode(result => metrics = result));

//             if (metrics != null)
//             {
//                 AllMetrics.Add(metrics);
//             }

//             // Epsilon decay
//             agent.DecayEpsilon();

//             // Log progress
//             if (CurrentEpisode % 10 == 0)
//             {
//                 LogProgress();
//             }

//             // Early stopping check
//             if (enableEarlyStop && CheckConvergence())
//             {
//                 Debug.Log($"[TrainingManager] EARLY STOP: Converged at episode {CurrentEpisode}!");
//                 break;
//             }

//             // Small delay between episodes (optional)
//             yield return null;
//         }

//         // Training completed
//         OnTrainingComplete();
//     }

//     /// <summary>
//     /// Runs a single episode.
//     /// </summary>
//     private IEnumerator RunEpisode(System.Action<EpisodeMetrics> onComplete)
//     {
//         environment.ResetEpisode();

//         int steps = 0;
//         float totalReward = 0f;
//         bool success = false;

//         // *** DÜZELTME: IsTerminal kontrolünü geri ekle ***
//         // Böylece RLEnvironment "episodeCompleted = true" dediği an döngü kırılır.
//         while (!environment.IsTerminal() || steps < environment.maxEpisodesSteps)
//         {
//             int state = environment.GetCurrentState();
//             int action = agent.SelectAction(state);
//             float reward = environment.ExecuteAction(action);

//             totalReward += reward;
//             steps++;

//             int nextState = environment.GetCurrentState();
//             bool isTerminal = environment.IsTerminal(); // Artık burası true olabiliyor

//             agent.UpdateQValue(state, action, reward, nextState, isTerminal);

//             // Başarı kontrolü (Puan veya Flag üzerinden)
//             if (environment.hasBuiltBarracks)
//             {
//                 success = true;
//             }

//             if (visualizeTraining) yield return new WaitForSeconds(stepDelayDuration);
//         }

//         onComplete?.Invoke(new EpisodeMetrics(CurrentEpisode, steps, totalReward, success, agent.epsilon));
//     }

//     /// <summary>
//     /// Logs training progress.
//     /// </summary>
//     private void LogProgress()
//     {
//         float recentSuccessRate = GetRecentSuccessRate();
//         float avgReward = GetRecentAvgReward();

//         Debug.Log($"[TrainingManager] Episode {CurrentEpisode}: Success Rate (last {successRateWindow}): {recentSuccessRate:P1} | Avg Reward: {avgReward:F1} | {agent.GetStats()}");
//     }

//     // --- Helper Methods for UI ---

//     public float GetRecentSuccessRate()
//     {
//         if (AllMetrics.Count == 0) return 0f;
//         int recentSuccessCount = 0;
//         int recentStart = Mathf.Max(0, AllMetrics.Count - successRateWindow);
//         int count = 0;

//         for (int i = recentStart; i < AllMetrics.Count; i++)
//         {
//             if (AllMetrics[i].success) recentSuccessCount++;
//             count++;
//         }
//         if (count == 0) return 0f;
//         return (float)recentSuccessCount / count;
//     }

//     public float GetRecentAvgReward()
//     {
//         if (AllMetrics.Count == 0) return 0f;
//         float avgReward = 0f;
//         int recentStart = Mathf.Max(0, AllMetrics.Count - successRateWindow);
//         int count = 0;

//         for (int i = recentStart; i < AllMetrics.Count; i++)
//         {
//             avgReward += AllMetrics[i].totalReward;
//             count++;
//         }
//         if (count == 0) return 0f;
//         return avgReward / count;
//     }

//     /// <summary>
//     /// Checks for convergence.
//     /// </summary>
//     private bool CheckConvergence()
//     {
//         if (AllMetrics.Count < successRateWindow) return false;
//         return GetRecentSuccessRate() >= targetSuccessRate;
//     }

//     /// <summary>
//     /// Called when training is complete.
//     /// </summary>
//     private void OnTrainingComplete()
//     {
//         IsTraining = false;
//         Time.timeScale = originalTimeScale;

//         Debug.Log("[TrainingManager] TRAINING COMPLETED!");

//         // Save metrics
//         SaveMetrics();

//         // Save Q-table
//         agent.SaveQTable(Path.Combine(Application.dataPath, "../", qTableFilePath));

//         // Print learned policy
//         agent.PrintLearnedPolicy();

//         // Final statistics
//         PrintFinalStatistics();
//     }

//     /// <summary>
//     /// Saves metrics to CSV.
//     /// </summary>
//     private void SaveMetrics()
//     {
//         string filePath = Path.Combine(Application.dataPath, "../", metricsFilePath);

//         try
//         {
//             StringBuilder csv = new StringBuilder();
//             csv.AppendLine("Episode,Steps,TotalReward,Success,Epsilon");

//             foreach (var m in AllMetrics)
//             {
//                 csv.AppendLine(m.ToString());
//             }

//             File.WriteAllText(filePath, csv.ToString());

//             Debug.Log($"[TrainingManager] Metrics saved to: {filePath}");
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError($"[TrainingManager] Failed to save metrics: {e.Message}");
//         }
//     }

//     /// <summary>
//     /// Prints final statistics.
//     /// </summary>
//     private void PrintFinalStatistics()
//     {
//         int totalSuccess = 0;
//         float avgSteps = 0f;
//         float avgReward = 0f;

//         foreach (var m in AllMetrics)
//         {
//             if (m.success) totalSuccess++;
//             avgSteps += m.steps;
//             avgReward += m.totalReward;
//         }

//         if (AllMetrics.Count > 0)
//         {
//             avgSteps /= AllMetrics.Count;
//             avgReward /= AllMetrics.Count;
//             float overallSuccessRate = (float)totalSuccess / AllMetrics.Count;

//             Debug.Log("=== FINAL STATISTICS ===");
//             Debug.Log($"Total Episodes: {AllMetrics.Count}");
//             Debug.Log($"Success Rate: {overallSuccessRate:P1} ({totalSuccess}/{AllMetrics.Count})");
//             Debug.Log($"Average Steps: {avgSteps:F1}");
//             Debug.Log($"Average Reward: {avgReward:F1}");
//             Debug.Log($"Final Epsilon: {agent.epsilon:F4}");
//         }
//     }

//     /// <summary>
//     /// Manually stops training.
//     /// </summary>
//     public void StopTraining()
//     {
//         if (IsTraining)
//         {
//             StopAllCoroutines();
//             OnTrainingComplete();
//         }
//     }
// }