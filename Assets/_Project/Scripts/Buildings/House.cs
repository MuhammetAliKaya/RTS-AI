// using UnityEngine;

// /*
//  * House.cs
//  * Custom script for the 'House' building.
//  * Inherits from the 'Building' base class.
//  *
//  * TASK (LOGIC):
//  * 1. Overrides 'OnBuildingComplete' to increase the global population limit
//  * via 'ResourceManager' when construction finishes.
//  * 2. Overrides 'Die' to decrease the population limit when the building is destroyed.
//  */
// public class House : Building // Inherits from 'Building' instead of 'MonoBehaviour'
// {
//     [Header("House Settings")]
//     [Tooltip("How much this house increases the max population limit.")]
//     public int populationIncreaseAmount = 5;

//     // We MUST implement the 'abstract' 'Die()' method from 'Building.cs'.
//     public override void Die()
//     {
//         // When building is destroyed, decrease the population cap
//         Debug.Log("House destroyed! Max Population decreased.");
//         GameManager.Instance.resourceManager.IncreaseMaxPopulation(this.playerID, -populationIncreaseAmount);

//         // (Optional: Add destruction effect/sound here)

//         // Finally, make the node walkable again
//         if (buildingNode != null)
//         {
//             buildingNode.isWalkable = true;
//         }

//         // Destroy the GameObject
//         Destroy(gameObject);
//     }

//     // Overrides the 'virtual' 'OnBuildingComplete' method in 'Building.cs'
//     // to add CUSTOM logic for the House.
//     protected override void OnBuildingComplete()
//     {
//         // First, run the base logic (e.g., change color/alpha)
//         base.OnBuildingComplete();

//         // --- HOUSE SPECIFIC LOGIC ---
//         // Construction finished, tell ResourceManager to increase Max Population
//         Debug.Log($"House finished! Max Population increased by +{populationIncreaseAmount}.");
//         GameManager.Instance.resourceManager.IncreaseMaxPopulation(this.playerID, populationIncreaseAmount);
//     }

//     // NOTE: This script does not need 'Start()', 'Update()', 'Construct()'
//     // or 'TakeDamage()' override. The 'Building.cs' base class handles those.
//     // Keeping the code clean.
// }