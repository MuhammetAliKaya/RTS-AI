using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/*
 * RLQuickSetup.cs
 * 
 * Unity Editor menÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¼sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¼nden tek tÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â±kla RL Training sistemini kurar.
 * KullanÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â±m: Tools > RTS > Setup RL Training System
 */

public class RLQuickSetup : Editor
{
    [MenuItem("Tools/RTS/Setup RL Training System")]
    static void SetupRLTrainingSystem()
    {
        // Mevcut scene'de RLTraining GameObject'i var mÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â± kontrol et
        GameObject existingRL = GameObject.Find("RLTraining");
        if (existingRL != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "RL Training Already Exists",
                "RLTraining GameObject already exists in the scene. Do you want to delete and recreate it?",
                "Yes, Recreate",
                "No, Cancel"
            );
            
            if (!overwrite)
            {
                Debug.Log("Setup cancelled by user.");
                return;
            }
            
            DestroyImmediate(existingRL);
        }
        
        // Yeni GameObject oluÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€¦Ã‚Â¸tur
        GameObject rlTraining = new GameObject("RLTraining");
        
        // Component'leri ekle
        RLEnvironment environment = rlTraining.AddComponent<RLEnvironment>();
        QLearningAgent agent = rlTraining.AddComponent<QLearningAgent>();
        TrainingManager trainingManager = rlTraining.AddComponent<TrainingManager>();
        RLTrainingHelper trainingHelper = rlTraining.AddComponent<RLTrainingHelper>();
        
        // TrainingManager referanslarÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â±nÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â± ayarla
        trainingManager.environment = environment;
        trainingManager.agent = agent;
        trainingManager.autoStartTraining = false; // GÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¼venlik iÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â§in false baÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€¦Ã‚Â¸lat
        trainingManager.totalEpisodes = 500;
        trainingManager.timeScaleDuringTraining = 10f;
        
        // RLEnvironment ayarlarÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â±
        environment.isTrainingMode = true;
        environment.maxEpisodesSteps = 500;
        environment.resourceCollectionRate = 5f;
        
        // QLearningAgent ayarlarÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â± (default deÃƒÆ’Ã¢â‚¬ÂÃƒâ€¦Ã‚Â¸erler zaten doÃƒÆ’Ã¢â‚¬ÂÃƒâ€¦Ã‚Â¸ru ama manuel set edelim)
        agent.learningRate = 0.1f;
        agent.discountFactor = 0.9f;
        agent.epsilon = 1.0f;
        agent.epsilonMin = 0.01f;
        agent.epsilonDecay = 0.995f;
        
        // Scene'i dirty yap (save gerektirecek)
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        
        // GameObject'i seÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â§
        Selection.activeGameObject = rlTraining;
        
        // Success mesajÃƒÆ’Ã¢â‚¬ÂÃƒâ€šÃ‚Â±
        EditorUtility.DisplayDialog(
            "RL Setup Complete!",
            "RLTraining GameObject created successfully!\n\n" +
            "? RLEnvironment added\n" +
            "? QLearningAgent added\n" +
            "? TrainingManager added\n" +
            "? All references configured\n\n" +
            "Next steps:\n" +
            "1. Save the scene (Ctrl+S)\n" +
            "2. Set 'Auto Start Training' to TRUE in TrainingManager\n" +
            "3. Press Play!\n\n" +
            "Check Console for training progress.",
            "Got it!"
        );
        
        Debug.Log("?? RL Training System setup complete! GameObject: RLTraining");
        Debug.Log("?? Remember to set 'Auto Start Training' to TRUE before pressing Play!");
    }
    
    [MenuItem("Tools/RTS/Remove RL Training System")]
    static void RemoveRLTrainingSystem()
    {
        GameObject rlTraining = GameObject.Find("RLTraining");
        if (rlTraining == null)
        {
            EditorUtility.DisplayDialog(
                "Not Found",
                "RLTraining GameObject not found in the scene.",
                "OK"
            );
            return;
        }
        
        bool confirm = EditorUtility.DisplayDialog(
            "Confirm Deletion",
            "Are you sure you want to delete the RLTraining GameObject?",
            "Yes, Delete",
            "Cancel"
        );
        
        if (confirm)
        {
            DestroyImmediate(rlTraining);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("RLTraining GameObject deleted.");
        }
    }
}
