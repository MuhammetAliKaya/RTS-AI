using UnityEngine;

/*
 * RLTrainingHelper.cs
 *
 * Helper script to clean the training environment during RL training sessions.
 * Functions:
 * - Disables enemy AI to create a controlled environment.
 * - Hides UI canvases for better performance (headless mode simulation).
 * - Destroys Player 2 objects if only the RL agent (Player 1) is needed.
 */

public class RLTrainingHelper : MonoBehaviour
{
    [Header("Training Environment Settings")]
    [Tooltip("Automatically disable enemy AI controllers on Start.")]
    public bool disableEnemyAI = true;

    [Tooltip("Hide UI canvases (good for performance during training).")]
    public bool hideUI = false;

    [Tooltip("Destroy all Player 2 objects (Units/Buildings/Base) on Start.")]
    public bool destroyPlayer2Objects = true;

    void Start()
    {
        if (disableEnemyAI)
        {
            DisableAllEnemyAI();
        }

        if (hideUI)
        {
            HideAllUI();
        }

        if (destroyPlayer2Objects)
        {
            DestroyPlayer2();
        }

        Debug.Log("[RLTrainingHelper] Environment cleaned based on settings.");
    }

    /// <summary>
    /// Disables all enemy AI controllers (EnemyAIController and ReferenceAIController).
    /// </summary>
    private void DisableAllEnemyAI()
    {
        // Find EnemyAIController components
        EnemyAIController[] enemies = FindObjectsByType<EnemyAIController>(FindObjectsSortMode.None);

        foreach (var enemy in enemies)
        {
            enemy.enabled = false;
            Debug.Log($"Disabled EnemyAIController on {enemy.gameObject.name}");
        }

        // Also find ReferenceAIControllers (if any)
        ReferenceAIController[] refAIs = FindObjectsByType<ReferenceAIController>(FindObjectsSortMode.None);

        foreach (var refAI in refAIs)
        {
            refAI.enabled = false;
            Debug.Log($"Disabled ReferenceAIController on {refAI.gameObject.name}");
        }

        Debug.Log($"Total AI controllers disabled: {enemies.Length + refAIs.Length}");
    }

    /// <summary>
    /// Hides all UI Canvases in the scene.
    /// </summary>
    private void HideAllUI()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        foreach (var canvas in canvases)
        {
            canvas.gameObject.SetActive(false);
            Debug.Log($"Hidden Canvas: {canvas.gameObject.name}");
        }

        Debug.Log($"Hidden {canvases.Length} UI canvases.");
    }

    /// <summary>
    /// Destroys all objects belonging to Player 2.
    /// </summary>
    private void DestroyPlayer2()
    {
        int destroyedCount = 0;

        // Find and destroy Player 2 Base
        Base[] bases = FindObjectsByType<Base>(FindObjectsSortMode.None);
        foreach (var baseBuilding in bases)
        {
            if (baseBuilding.playerID == 2)
            {
                Destroy(baseBuilding.gameObject);
                destroyedCount++;
                Debug.Log("Destroyed Player 2 Base");
            }
        }

        // Find and destroy Player 2 units
        Unit[] units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (var unit in units)
        {
            if (unit.playerID == 2)
            {
                Destroy(unit.gameObject);
                destroyedCount++;
            }
        }

        // Find and destroy Player 2 buildings
        Building[] buildings = FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (var building in buildings)
        {
            if (building.playerID == 2)
            {
                Destroy(building.gameObject);
                destroyedCount++;
            }
        }

        Debug.Log($"Destroyed {destroyedCount} Player 2 objects.");
    }
}