// using UnityEngine;
// using System.Collections; // For Coroutine

// /*
//  * Stonepit.cs
//  * Custom script for the 'Stonepit' building.
//  * Inherits from the 'Building' base class.
//  *
//  * TASK (LOGIC):
//  * 1. Overrides 'OnBuildingComplete' to start a Coroutine that passively produces Stone.
//  * 2. Overrides 'Die' to stop the production Coroutine when destroyed.
//  */
// public class Stonepit : Building // Inherits from 'Building' instead of 'MonoBehaviour'
// {
//     [Header("Stonepit Settings")]
//     [Tooltip("Resource production interval in seconds (tick rate).")]
//     public float productionRate = 10.0f; // Every 10 seconds

//     [Tooltip("Amount of Stone produced per production tick.")]
//     public int stonePerProduction = 5; // 5 Stone

//     private Coroutine productionCoroutine;


//     // --- MANDATORY OVERRIDES FROM BASE CLASS ---

//     // We must implement the abstract 'Die' method from Building.cs
//     public override void Die()
//     {
//         Debug.Log("Stonepit destroyed! Passive Stone production stopped.");

//         // Stop the production coroutine if it's running
//         if (productionCoroutine != null)
//         {
//             StopCoroutine(productionCoroutine);
//         }

//         // Make the node walkable again
//         if (buildingNode != null)
//         {
//             buildingNode.isWalkable = true;
//         }

//         Destroy(gameObject);
//     }

//     // Override 'OnBuildingComplete' to start logic when construction finishes
//     protected override void OnBuildingComplete()
//     {
//         base.OnBuildingComplete();

//         Debug.Log("Stonepit finished! Passive Stone production starting.");
//         productionCoroutine = StartCoroutine(ProduceResourcesCoroutine());
//     }

//     /// <summary>
//     /// Coroutine that adds Stone resources at regular intervals while the building is functional.
//     /// </summary>
//     private IEnumerator ProduceResourcesCoroutine()
//     {
//         // Run loop as long as the building is functional (alive and constructed)
//         while (isFunctional)
//         {
//             // Wait for the production interval
//             yield return new WaitForSeconds(productionRate);

//             if (GameManager.Instance.resourceManager != null)
//             {
//                 // --- LOGIC SPECIFIC TO STONEPIT ---
//                 GameManager.Instance.resourceManager.AddResource(playerID, ResourceType.Stone, stonePerProduction);
//                 Debug.Log($"Passive Income: +{stonePerProduction} Stone");
//             }
//         }
//     }
// }