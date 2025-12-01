// using UnityEngine;
// using System.Collections;

// /*
//  * Woodcutter.cs
//  * Special script for the 'Woodcutter Hut' building.
//  * Inherits from 'Building.cs' (Building) parent class.
//  *
//  * OVERVIEW (LOGIC):
//  * 1. 'OnBuildingComplete' (When Construction is Complete), passively produces Wood
//  * by starting a Coroutine (Timer).
//  * 2. When the building is destroyed ('Die'), the Coroutine is stopped.
//  */
// public class Woodcutter : Building // Inherits from Building instead of MonoBehaviour
// {
//     [Header("Woodcutter Settings")]
//     [Tooltip("Resource 'tick' (production) interval in seconds.")]
//     public float productionRate = 10.0f; // Every 10 seconds

//     [Tooltip("How much Wood is produced per 'tick' (production).")]
//     public int woodPerProduction = 5; // 5 Wood

//     // Store the passive production Coroutine (Timer)
//     private Coroutine productionCoroutine;


//     // --- BUILDING LOGIC FUNCTIONS ---

//     // We MUST write the 'abstract' 'Die()' method from 'Building.cs' (parent class).
//     public override void Die()
//     {
//         Debug.Log("Woodcutter Hut destroyed, passive production stopped.");

//         // --- IMPORTANT: Stop the Coroutine ---
//         // If the building is destroyed and we don't stop the Coroutine,
//         // the game will try to produce resources from a 'null' (empty) object until it crashes.
//         if (productionCoroutine != null)
//         {
//             StopCoroutine(productionCoroutine);
//         }

//         // Make the 'Node' (cell) walkable again
//         if (buildingNode != null)
//         {
//             buildingNode.isWalkable = true;
//         }

//         Destroy(gameObject);
//     }

//     // Override the 'virtual' 'OnBuildingComplete' method from 'Building.cs' (parent class)
//     // to add WOODCUTTER-SPECIFIC logic.
//     protected override void OnBuildingComplete()
//     {
//         // First call the parent class's function (prints Debug.Log)
//         base.OnBuildingComplete();

//         // --- NOW THE ACTUAL LOGIC OF THIS BUILDING ---
//         // Construction finished, start the passive resource production timer (Coroutine).
//         Debug.Log("Woodcutter Hut complete! Passive Wood production starting.");
//         productionCoroutine = StartCoroutine(ProduceResourcesCoroutine());
//     }

//     /// <summary>
//     /// While the building is 'isFunctional' (functional),
//     /// periodically adds Wood to the 'ResourceManager' at 'productionRate' intervals.
//     /// </summary>
//     private IEnumerator ProduceResourcesCoroutine()
//     {
//         // This Coroutine runs indefinitely until stopped in the 'Die()' function.
//         while (isFunctional)
//         {
//             // WAIT for 'productionRate' (e.g. 10 seconds)
//             yield return new WaitForSeconds(productionRate);

//             // Waiting done, now add resources
//             if (GameManager.Instance.resourceManager != null)
//             {
//                 GameManager.Instance.resourceManager.AddResource(this.playerID, ResourceType.Wood, woodPerProduction);
//                 Debug.Log($"Passive Income: +{woodPerProduction} Wood");
//             }
//         }
//     }
// }